/**
 * CERE-59：把 382 MB 的 ONNX 权重从安装包里拆出来，做成按需下载的资源包。
 *
 * 为什么只拆模型、不把整个 pipeline 运行时也拆走：
 *  - 两个 .onnx 合计 382 MB，而且是**已压缩过的二进制**，NSIS 再压也压不动，
 *    它们几乎 1:1 地变成安装包体积——500 MB 里最大的一块就是它们。
 *  - 它们由 `models.lock.json` 钉死（字节数 + MD5），版本之间**根本不变**，
 *    每次随包重下一遍是纯浪费。
 *  - PyInstaller 运行时（约 308 MB 未压缩）会随 Python 依赖变，但变化很小，
 *    blockmap 差分下载对它很有效，留在安装包里反而更省事。
 *
 * 结果：常规版本更新只碰安装包，模型一辈子只下一次。
 */
import { app } from 'electron';
import crypto from 'node:crypto';
import fs from 'node:fs';
import fsp from 'node:fs/promises';
import path from 'node:path';

import type { ModelPackFailure, ModelPackFile, ModelPackState } from '../../shared/update';

export interface LockedModel {
  route: string;
  runtime_filename: string;
  bytes: number;
  md5: string;
  /** 上游原始地址，作为镜像不可用时的回落 */
  download_url: string;
}

interface ModelLock {
  models?: LockedModel[];
}

/** 我们自己托管的模型镜像。和应用版本解耦，只有 models.lock.json 变了才发新 tag。 */
export const MODEL_PACK_TAG = 'models-v1';

export function mirrorUrl(filename: string): string {
  return `https://github.com/vito4444/steam/releases/download/${MODEL_PACK_TAG}/${filename}`;
}

export function readModelLock(pipelineDir: string): LockedModel[] {
  try {
    const raw = JSON.parse(
      fs.readFileSync(path.join(pipelineDir, 'models.lock.json'), 'utf8'),
    ) as ModelLock;
    return (raw.models ?? []).filter((model) => model.runtime_filename && model.bytes > 0);
  } catch {
    return [];
  }
}

/** 资源包的落盘位置：用户数据目录，安装目录通常没有写权限。 */
export function externalModelsDir(): string {
  return path.join(app.getPath('userData'), 'model-pack');
}

function complete(dir: string, models: LockedModel[]): boolean {
  if (models.length === 0) return false;
  return models.every((model) => {
    try {
      return fs.statSync(path.join(dir, model.runtime_filename)).size === model.bytes;
    } catch {
      return false;
    }
  });
}

/**
 * 决定这次运行用哪个目录里的模型。
 *
 * 先看安装包自带的 `pipeline/models`——0.4.3 及更早的布局、以及开发机上
 * 跑 `prepare-windows-pipeline.ps1` 之后都是这种情况，用户不需要下载任何东西。
 * 自带的不完整才回到外部资源包。
 */
export function resolveModelsDir(pipelineDir: string): { dir: string; bundled: boolean; ready: boolean } {
  const models = readModelLock(pipelineDir);
  const bundledDir = path.join(pipelineDir, 'models');
  if (complete(bundledDir, models)) return { dir: bundledDir, bundled: true, ready: true };
  const external = externalModelsDir();
  return { dir: external, bundled: false, ready: complete(external, models) };
}

async function md5(file: string): Promise<string> {
  const hash = crypto.createHash('md5');
  const stream = fs.createReadStream(file);
  for await (const chunk of stream) hash.update(chunk as Buffer);
  return hash.digest('hex');
}

/**
 * CERE-64：清掉上一次没下完留下的垃圾。
 *
 * 两种残留会让后续每一次尝试都失败：
 *  - `*.part`：下到一半进程被杀（关窗口、断电、任务管理器结束进程），
 *    临时文件留在盘上白占几百 MB，而且用户根本不知道它是什么。
 *  - 字节数不对的正式文件：老版本 / 手动拷贝进来的半个模型。`complete()`
 *    看字节数判定它「没就绪」，于是界面永远显示未下载，但磁盘一直被占着。
 *
 * 每次启动和每次下载前都扫一遍，删掉的文件名如实报给界面 —— 不静默删。
 */
export async function sweepStale(dir: string, models: LockedModel[]): Promise<string[]> {
  let entries: string[];
  try {
    entries = await fsp.readdir(dir);
  } catch {
    return [];
  }
  const expected = new Map(models.map((model) => [model.runtime_filename, model.bytes]));
  const removed: string[] = [];
  for (const entry of entries) {
    const full = path.join(dir, entry);
    const wanted = expected.get(entry);
    const partial = entry.endsWith('.part') || entry.endsWith('.download');
    if (!partial && wanted === undefined) continue;
    try {
      const stat = await fsp.stat(full);
      if (!stat.isFile()) continue;
      if (!partial && stat.size === wanted) continue;
      await fsp.rm(full, { force: true });
      removed.push(entry);
    } catch {
      // 删不掉（被占用 / 权限）就留着，下载路径本来就会覆盖它。
    }
  }
  return removed;
}

/** 目标盘剩余可用字节；拿不到就返回 null，不因为查不到而拦下载。 */
async function freeBytes(dir: string): Promise<number | null> {
  try {
    const stat = await fsp.statfs(dir);
    return Number(stat.bavail) * Number(stat.bsize);
  } catch {
    return null;
  }
}

/**
 * 把原始异常归到用户能照做的那几类。
 *
 * 顺序有讲究：取消要排在网络之前 —— abort 掉的 fetch 报的是 `AbortError`，
 * 按关键字它会被当成网络故障，但用户自己点的取消不是故障。
 */
export function classifyFailure(error: unknown): ModelPackFailure {
  const err = error as { name?: string; code?: string; message?: string } | null;
  const name = err?.name ?? '';
  const code = err?.code ?? '';
  const message = err instanceof Error ? err.message : String(error ?? '');
  if (name === 'AbortError' || /aborted|已取消/i.test(message)) return 'canceled';
  if (/校验不通过|字节数不符/.test(message)) return 'checksum';
  if (code === 'ENOSPC' || /ENOSPC|no space left|磁盘空间/i.test(message)) return 'disk';
  if (
    /ENOTFOUND|ECONNREFUSED|ECONNRESET|ETIMEDOUT|EAI_AGAIN|ENETUNREACH|CERT_|HTTP \d{3}|fetch failed|network/i
      .test(`${code} ${message}`)
  ) {
    return 'network';
  }
  return 'unknown';
}

export interface ModelPackEvents {
  onState(state: ModelPackState): void;
}

/** 进度推送节流：一次 382 MB 的下载有几万个 chunk，逐个推 IPC 只会拖慢下载。 */
const EMIT_INTERVAL_MS = 250;

export class ModelPackService {
  private phase: ModelPackState['phase'] = 'unknown';
  private error: string | null = null;
  private failure: ModelPackFailure | null = null;
  private downloadedBytes = 0;
  private busy = false;
  private swept: string[] = [];
  private controller: AbortController | null = null;
  private startedAt = 0;
  private startedFrom = 0;
  private lastEmit = 0;

  constructor(
    private readonly pipelineDir: () => string | null,
    private readonly events: ModelPackEvents,
  ) {}

  /**
   * 启动时调用一次：把上次留下的残缺文件清掉，让界面从一个干净状态出发。
   * 失败不抛 —— 清理不了也不该拦住应用启动。
   */
  async init(): Promise<void> {
    const dir = this.pipelineDir();
    if (!dir) return;
    try {
      this.swept = await sweepStale(externalModelsDir(), readModelLock(dir));
    } catch {
      this.swept = [];
    }
    if (this.swept.length > 0) this.emit(true);
  }

  state(): ModelPackState {
    const dir = this.pipelineDir();
    if (!dir) {
      return {
        phase: 'error',
        dir: '',
        files: [],
        totalBytes: 0,
        downloadedBytes: 0,
        error: '本地识别运行时没有安装，模型资源包无处可用。',
        failure: 'unknown',
        bytesPerSecond: null,
        etaSeconds: null,
        swept: this.swept,
        bundled: false,
      };
    }
    const models = readModelLock(dir);
    const resolved = resolveModelsDir(dir);
    const files: ModelPackFile[] = models.map((model) => ({
      name: model.runtime_filename,
      bytes: model.bytes,
      present: fileMatches(path.join(resolved.dir, model.runtime_filename), model.bytes),
    }));
    const phase = this.busy
      ? this.phase
      : resolved.ready
        ? 'ready'
        : this.phase === 'error'
          ? 'error'
          : 'missing';
    const totalBytes = models.reduce((sum, model) => sum + model.bytes, 0);
    const downloadedBytes = this.busy
      ? this.downloadedBytes
      : files.reduce((sum, file) => sum + (file.present ? file.bytes : 0), 0);
    return {
      phase,
      dir: resolved.dir,
      files,
      totalBytes,
      downloadedBytes,
      error: this.error,
      failure: this.failure,
      ...this.rate(totalBytes, downloadedBytes),
      swept: this.swept,
      bundled: resolved.bundled,
    };
  }

  /**
   * 速度与剩余时间。
   *
   * 用「这次下载开始以来的平均速度」而不是瞬时速度：瞬时值抖得厉害，
   * 剩余时间会在几秒和十几分钟之间来回跳，比不显示更让人不安。
   * 头两秒样本太少，不给估计值，宁可留空。
   */
  private rate(totalBytes: number, downloadedBytes: number): {
    bytesPerSecond: number | null;
    etaSeconds: number | null;
  } {
    if (!this.busy || this.phase !== 'downloading' || this.startedAt === 0) {
      return { bytesPerSecond: null, etaSeconds: null };
    }
    const elapsed = (Date.now() - this.startedAt) / 1000;
    const moved = downloadedBytes - this.startedFrom;
    if (elapsed < 2 || moved <= 0) return { bytesPerSecond: null, etaSeconds: null };
    const bytesPerSecond = moved / elapsed;
    const remaining = Math.max(0, totalBytes - downloadedBytes);
    return { bytesPerSecond, etaSeconds: Math.round(remaining / bytesPerSecond) };
  }

  private emit(force = false): void {
    const now = Date.now();
    if (!force && now - this.lastEmit < EMIT_INTERVAL_MS) return;
    this.lastEmit = now;
    this.events.onState(this.state());
  }

  /** 用户点「取消」。已经下好的整块文件保留，下次接着下。 */
  cancel(): ModelPackState {
    this.controller?.abort();
    return this.state();
  }

  /**
   * 把外部资源包整个删掉再下一遍。
   *
   * 留给这一种情况：文件字节数对得上、界面显示「已就绪」，但管线仍然报
   * MODEL_MISSING —— 说明磁盘上那份内容是坏的（字节数校验拦不住内容损坏）。
   * 这时候唯一能自救的动作就是全删重下，所以给它一个明确入口，
   * 而不是让用户自己去翻用户数据目录。
   */
  async redownload(): Promise<ModelPackState> {
    if (this.busy) return this.state();
    const dir = this.pipelineDir();
    if (!dir) return this.state();
    for (const model of readModelLock(dir)) {
      await fsp.rm(path.join(externalModelsDir(), model.runtime_filename), { force: true });
    }
    return this.download();
  }

  /**
   * 下载缺失的模型。
   *
   * 顺序：先清残留 → 查磁盘空间 → 先试我们自己的 Release 镜像，失败再回上游地址。
   * 两个来源都用 `models.lock.json` 里钉死的字节数 + MD5 校验，校验不过就删掉重来，
   * 绝不把一个半截文件留在磁盘上当成「已就绪」。
   */
  async download(): Promise<ModelPackState> {
    if (this.busy) return this.state();
    const pipelineDir = this.pipelineDir();
    if (!pipelineDir) return this.state();
    const models = readModelLock(pipelineDir);
    if (models.length === 0) {
      this.error = 'models.lock.json 缺失或为空，无法确认要下载哪些模型。';
      this.failure = 'unknown';
      this.phase = 'error';
      this.emit(true);
      return this.state();
    }

    this.busy = true;
    this.error = null;
    this.failure = null;
    this.phase = 'downloading';
    this.controller = new AbortController();
    const target = externalModelsDir();
    await fsp.mkdir(target, { recursive: true });
    this.swept = await sweepStale(target, models);

    const already = models
      .filter((model) => fileMatches(path.join(target, model.runtime_filename), model.bytes))
      .reduce((sum, model) => sum + model.bytes, 0);
    this.downloadedBytes = already;
    this.startedAt = Date.now();
    this.startedFrom = already;
    this.emit(true);

    try {
      const needed = models.reduce((sum, model) => sum + model.bytes, 0) - already;
      await this.assertDiskSpace(target, needed);
      for (const model of models) {
        const destination = path.join(target, model.runtime_filename);
        if (fileMatches(destination, model.bytes)) continue;
        await this.fetchOne(model, destination);
        this.phase = 'verifying';
        this.emit(true);
        const digest = await md5(destination);
        if (digest !== model.md5.toLowerCase()) {
          await fsp.rm(destination, { force: true });
          throw new Error(`${model.runtime_filename} 校验不通过（MD5 ${digest}），已删除，请重试。`);
        }
        this.phase = 'downloading';
      }
      this.phase = 'ready';
    } catch (err) {
      this.failure = classifyFailure(err);
      this.phase = this.failure === 'canceled' ? 'missing' : 'error';
      this.error = this.failure === 'canceled'
        ? null
        : err instanceof Error ? err.message : String(err);
    } finally {
      this.busy = false;
      this.controller = null;
      this.startedAt = 0;
    }
    this.emit(true);
    return this.state();
  }

  /**
   * 空间不够就在**下载开始前**说清楚，而不是下到 90% 才 ENOSPC。
   * 留 64 MB 余量：`.part` 和改名后的正式文件在改名瞬间不会同时存在，
   * 但系统本身也在往这个盘写东西。
   */
  private async assertDiskSpace(dir: string, needed: number): Promise<void> {
    if (needed <= 0) return;
    const free = await freeBytes(dir);
    if (free === null) return;
    const required = needed + 64 * 1024 * 1024;
    if (free >= required) return;
    const mb = (bytes: number) => `${(bytes / 1024 / 1024).toFixed(0)} MB`;
    throw Object.assign(
      new Error(`磁盘空间不足：还需要约 ${mb(required)}，${dir} 所在磁盘只剩 ${mb(free)}。`),
      { code: 'ENOSPC' },
    );
  }

  private async fetchOne(model: LockedModel, destination: string): Promise<void> {
    const sources = [mirrorUrl(model.runtime_filename), model.download_url];
    let lastError: unknown = null;
    for (const source of sources) {
      try {
        await this.stream(source, destination, model);
        return;
      } catch (err) {
        lastError = err;
        await fsp.rm(`${destination}.part`, { force: true });
        // 用户点的取消不该再去试下一个源。
        if (classifyFailure(err) === 'canceled') throw err;
      }
    }
    throw new Error(
      `下载 ${model.runtime_filename} 失败：${lastError instanceof Error ? lastError.message : String(lastError)}`,
    );
  }

  private async stream(url: string, destination: string, model: LockedModel): Promise<void> {
    const response = await fetch(url, { signal: this.controller?.signal });
    if (!response.ok || !response.body) throw new Error(`HTTP ${response.status} @ ${url}`);
    const temporary = `${destination}.part`;
    const handle = await fsp.open(temporary, 'w');
    const base = this.downloadedBytes;
    let written = 0;
    try {
      for await (const chunk of response.body as unknown as AsyncIterable<Uint8Array>) {
        await handle.write(chunk);
        written += chunk.byteLength;
        this.downloadedBytes = base + written;
        this.emit();
      }
    } finally {
      await handle.close();
    }
    if (this.controller?.signal.aborted) {
      await fsp.rm(temporary, { force: true });
      throw Object.assign(new Error('已取消下载'), { name: 'AbortError' });
    }
    if (written !== model.bytes) {
      await fsp.rm(temporary, { force: true });
      throw new Error(`字节数不符：期望 ${model.bytes}，实际 ${written}`);
    }
    await fsp.rename(temporary, destination);
  }
}

function fileMatches(file: string, bytes: number): boolean {
  try {
    return fs.statSync(file).size === bytes;
  } catch {
    return false;
  }
}
