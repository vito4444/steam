/**
 * CERE-59：应用内检查更新。
 *
 * 更新源是 `vito4444/steam` 的 GitHub Releases（public 仓库，不需要 token）。
 * 三条硬规则：
 *  1. 不静默强制更新——检查是静默的，下载和安装都必须用户点。
 *  2. Portable 版不走 NSIS 安装器，单独一条「下到本地再替换」的路。
 *  3. 应用没有代码签名，安装时 Windows 会弹 SmartScreen。提示里如实写，
 *     不假装没有这回事。
 */
import { app, shell } from 'electron';
import fs from 'node:fs';
import fsp from 'node:fs/promises';
import path from 'node:path';
import type { AppUpdater } from 'electron-updater';

import type {
  DifferentialReport,
  InstallKind,
  UpdateInfoView,
  UpdateProgress,
  UpdateState,
} from '../../shared/update';
import { fullDownloadReport, isDifferentialFallback, parseDifferentialLine } from './differential';
import { UpdatePreferencesRepository } from './settings';

export const UPDATE_REPO = { owner: 'vito4444', repo: 'steam' } as const;

export function releaseUrl(version: string): string {
  return `https://github.com/${UPDATE_REPO.owner}/${UPDATE_REPO.repo}/releases/tag/v${version}`;
}

/** 和 electron-builder.yml 的 `portable.artifactName` 必须保持一致。 */
export function portableAssetUrl(version: string): string {
  return `https://github.com/${UPDATE_REPO.owner}/${UPDATE_REPO.repo}/releases/download/v${version}/PixelFit-Portable-${version}-x64.exe`;
}

/**
 * 判断当前是怎么装上的。
 *
 * electron-builder 的 portable 目标在启动时会设 `PORTABLE_EXECUTABLE_FILE`，
 * 指向用户双击的那个 exe。这是区分免安装版最可靠的信号——不能靠安装目录猜，
 * NSIS 也可以被装到任意目录。
 */
export function detectInstallKind(env: NodeJS.ProcessEnv, packaged: boolean): InstallKind {
  if (!packaged) return 'dev';
  return env['PORTABLE_EXECUTABLE_FILE'] ? 'portable' : 'nsis';
}

export interface UpdaterDeps {
  updater: AppUpdater;
  preferences: UpdatePreferencesRepository;
  logFile: string;
  installKind: InstallKind;
  currentVersion: string;
  /** 把状态推给渲染进程；窗口还没建好时是 no-op */
  emit(state: UpdateState): void;
}

const NO_UPDATE_NOTES = '这一版没有附更新说明。';

export class UpdaterService {
  private phase: UpdateState['phase'] = 'idle';
  private info: UpdateInfoView | null = null;
  private progress: UpdateProgress | null = null;
  private error: string | null = null;
  private differential: DifferentialReport | null = null;
  private downloading = false;

  constructor(private readonly deps: UpdaterDeps) {
    this.wire();
  }

  // ------------------------------------------------------------ 对外接口

  state(): UpdateState {
    const preferences = this.deps.preferences.load();
    return {
      phase: this.phase,
      currentVersion: this.deps.currentVersion,
      install: this.deps.installKind,
      checkOnLaunch: preferences.checkOnLaunch,
      info: this.info,
      progress: this.progress,
      error: this.error,
      lastCheckedAt: preferences.lastCheckedAt,
      differential: this.differential,
    };
  }

  async setCheckOnLaunch(checkOnLaunch: boolean): Promise<UpdateState> {
    await this.deps.preferences.save({ checkOnLaunch });
    this.push();
    return this.state();
  }

  /** 启动时的静默检查。关掉开关就一个字节都不发。 */
  async checkOnLaunch(): Promise<void> {
    if (!this.deps.preferences.load().checkOnLaunch) {
      this.log('launch check skipped: disabled by user');
      return;
    }
    await this.check({ silent: true });
  }

  async check(options: { silent?: boolean } = {}): Promise<UpdateState> {
    if (this.downloading) return this.state();
    if (this.deps.installKind === 'dev' && !process.env['PIXELFIT_UPDATE_DEV']) {
      this.phase = 'disabled';
      this.error = '开发模式不检查更新（打包后的应用才有版本可比）。';
      this.push();
      return this.state();
    }
    this.phase = 'checking';
    this.error = null;
    this.push();
    try {
      const result = await this.deps.updater.checkForUpdates();
      await this.deps.preferences.save({ lastCheckedAt: new Date().toISOString() });
      // updateInfo 里的版本不比当前新时，electron-updater 不会发
      // update-available，phase 仍停在 checking，这里补一个终态。
      if (this.phase === 'checking') {
        this.phase = 'current';
        this.info = result?.updateInfo ? this.toView(result.updateInfo) : null;
      }
    } catch (err) {
      this.phase = 'error';
      this.error = describeError(err, options.silent === true);
    }
    this.push();
    return this.state();
  }

  /**
   * 下载。NSIS 走 electron-updater（差分下载在这里生效）；
   * Portable 走「下到磁盘 + 在资源管理器里指出来」，不去动正在运行的 exe。
   */
  async download(): Promise<UpdateState> {
    if (this.downloading || !this.info) return this.state();
    this.downloading = true;
    this.phase = 'downloading';
    this.error = null;
    this.progress = { percent: 0, transferred: 0, total: this.info.bytes ?? 0, bytesPerSecond: 0 };
    this.differential = null;
    this.push();
    try {
      if (this.deps.installKind === 'portable') {
        await this.downloadPortable(this.info);
      } else {
        await this.deps.updater.downloadUpdate();
      }
      this.phase = 'ready';
    } catch (err) {
      this.phase = 'error';
      this.error = describeError(err, false);
    } finally {
      this.downloading = false;
    }
    this.push();
    return this.state();
  }

  /** 退出并安装。Portable 没有这一步，界面上给的是「打开文件夹」。 */
  install(): void {
    if (this.deps.installKind === 'portable') {
      void shell.showItemInFolder(this.portableTarget(this.info?.version ?? this.deps.currentVersion));
      return;
    }
    /*
     * isSilent=true：更新走静默安装器（`/S`），装完自动重启应用。
     *
     * 不是「静默强制更新」——用户已经在弹窗里点过下载、又点过重启安装了。
     * 这里静默的是**安装器向导**：assisted 安装器每次更新都重问一遍安装目录，
     * 而更新本来就该装回原处。
     */
    setImmediate(() => this.deps.updater.quitAndInstall(true, true));
  }

  openReleasePage(): void {
    void shell.openExternal(this.info?.releaseUrl ?? releaseUrl(this.deps.currentVersion));
  }

  // ------------------------------------------------------------ 内部

  private portableDir(): string {
    const current = process.env['PORTABLE_EXECUTABLE_FILE'];
    // 免安装版常被放在只读位置（U 盘、Program Files）。写不进去就退到下载目录，
    // 不要在这里失败——「点了更新之后卡住」正是这条需求要消灭的体验。
    if (current) {
      const dir = path.dirname(current);
      try {
        fs.accessSync(dir, fs.constants.W_OK);
        return dir;
      } catch {
        // 落到下载目录
      }
    }
    return app.getPath('downloads');
  }

  private portableTarget(version: string): string {
    return path.join(this.portableDir(), `PixelFit-Portable-${version}-x64.exe`);
  }

  private async downloadPortable(info: UpdateInfoView): Promise<void> {
    const target = this.portableTarget(info.version);
    const temporary = `${target}.part`;
    const response = await fetch(portableAssetUrl(info.version));
    if (!response.ok || !response.body) {
      throw new Error(`下载免安装版失败：HTTP ${response.status}`);
    }
    const total = Number(response.headers.get('content-length') ?? info.bytes ?? 0);
    await fsp.mkdir(path.dirname(target), { recursive: true });
    const handle = await fsp.open(temporary, 'w');
    let transferred = 0;
    const started = Date.now();
    try {
      for await (const chunk of response.body as unknown as AsyncIterable<Uint8Array>) {
        await handle.write(chunk);
        transferred += chunk.byteLength;
        const seconds = Math.max((Date.now() - started) / 1000, 0.001);
        this.progress = {
          percent: total > 0 ? (transferred / total) * 100 : 0,
          transferred,
          total,
          bytesPerSecond: transferred / seconds,
        };
        this.push();
      }
    } finally {
      await handle.close();
    }
    await fsp.rename(temporary, target);
    this.differential = fullDownloadReport(
      transferred,
      '免安装版是整包替换，没有差分下载可用；差分只适用于 NSIS 安装版。',
    );
  }

  private toView(info: { version: string; releaseNotes?: unknown; releaseDate?: string; files?: Array<{ size?: number }> }): UpdateInfoView {
    const notes = typeof info.releaseNotes === 'string' ? info.releaseNotes.trim() : '';
    return {
      version: info.version,
      notes: notes || NO_UPDATE_NOTES,
      releasedAt: info.releaseDate ?? null,
      bytes: info.files?.[0]?.size ?? null,
      releaseUrl: releaseUrl(info.version),
    };
  }

  private push(): void {
    this.deps.emit(this.state());
  }

  private log(line: string): void {
    const stamped = `[${new Date().toISOString()}] ${line}\n`;
    try {
      fs.mkdirSync(path.dirname(this.deps.logFile), { recursive: true });
      fs.appendFileSync(this.deps.logFile, stamped, 'utf8');
    } catch {
      // 日志写不进去不该影响更新流程本身
    }
    console.log(`[updater] ${line}`);
  }

  private wire(): void {
    const updater = this.deps.updater;
    updater.autoDownload = false;
    // 「不要静默强制更新」：下载完也不在退出时偷偷装，必须用户点重启安装。
    updater.autoInstallOnAppQuit = false;
    updater.allowDowngrade = false;

    // 把 electron-updater 的日志接过来：差分下载的实际字节数只在这里出现。
    const observe = (level: string) => (message: unknown) => {
      const line = message instanceof Error ? (message.stack ?? message.message) : String(message);
      this.log(`${level} ${line}`);
      const parsed = parseDifferentialLine(line);
      if (parsed) {
        this.differential = { ...parsed, applied: true, reason: '按 blockmap 只取了变化的块。' };
        this.push();
      } else if (isDifferentialFallback(line)) {
        this.differential = fullDownloadReport(
          this.info?.bytes ?? 0,
          '本机没有可比对的上一版安装包（上一版不是通过应用内更新装的），这次是全量下载；下一次更新起差分生效。',
        );
        this.push();
      }
    };
    updater.logger = {
      info: observe('info'),
      warn: observe('warn'),
      error: observe('error'),
      debug: () => undefined,
    };

    updater.on('update-available', (info) => {
      this.info = this.toView(info as never);
      this.phase = 'available';
      this.push();
    });
    updater.on('update-not-available', (info) => {
      this.info = this.toView(info as never);
      this.phase = 'current';
      this.push();
    });
    updater.on('download-progress', (p) => {
      this.progress = {
        percent: p.percent,
        transferred: p.transferred,
        total: p.total,
        bytesPerSecond: p.bytesPerSecond,
      };
      this.push();
    });
    updater.on('update-downloaded', () => {
      this.phase = 'ready';
      this.progress = null;
      this.push();
    });
    updater.on('error', (err) => {
      this.phase = 'error';
      this.error = describeError(err, false);
      this.push();
    });
  }
}

export function describeError(err: unknown, silent: boolean): string {
  const raw = err instanceof Error ? err.message : String(err);
  if (/ENOTFOUND|EAI_AGAIN|ECONNREFUSED|ETIMEDOUT|ENETUNREACH/i.test(raw)) {
    return silent ? '连不上 GitHub，稍后会再试。' : '连不上 GitHub，请检查网络后重试。';
  }
  if (/404/.test(raw)) {
    return '更新源上还没有可用的版本信息（latest.yml 缺失）。';
  }
  return raw;
}
