/**
 * CERE-59：更新界面。
 *
 * 三条约束直接写进 UI：
 *  1. 发现新版只是**提示**——版本号、更新说明、包大小摆出来，下不下载用户点。
 *  2. 应用没有代码签名，安装时 Windows 会弹 SmartScreen。提示里就写清楚，
 *     用户看到「未知发布者」时才不会以为中毒。
 *  3. Portable 版走不了原地覆盖安装，给它自己的一条路，不做成点了没反应的按钮。
 */
import { useCallback, useEffect, useState } from 'react';

import type { ModelPackState, UpdateState } from '@shared/update';
import { IconDownload, IconRefresh } from '@/ui/icons';

function mb(bytes: number | null | undefined): string {
  if (!bytes || bytes <= 0) return '未知大小';
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

function speed(bytesPerSecond: number): string {
  if (!Number.isFinite(bytesPerSecond) || bytesPerSecond <= 0) return '';
  return `${(bytesPerSecond / 1024 / 1024).toFixed(1)} MB/s`;
}

// ---------------------------------------------------------------- 数据订阅

export function useUpdateState(): [UpdateState | null, (next: UpdateState | null) => void] {
  const [state, setState] = useState<UpdateState | null>(null);
  useEffect(() => {
    let alive = true;
    void window.pixelfit.update.state().then((next) => {
      if (alive) setState(next);
    });
    const off = window.pixelfit.update.onState((next) => setState(next));
    return () => {
      alive = false;
      off();
    };
  }, []);
  return [state, setState];
}

export function useModelPackState(): ModelPackState | null {
  const [state, setState] = useState<ModelPackState | null>(null);
  useEffect(() => {
    let alive = true;
    void window.pixelfit.modelPack.state().then((next) => {
      if (alive) setState(next);
    });
    const off = window.pixelfit.modelPack.onState((next) => setState(next));
    return () => {
      alive = false;
      off();
    };
  }, []);
  return state;
}

// ---------------------------------------------------------------- 提示弹窗

const UNSIGNED_NOTICE =
  'PixelFit 没有购买代码签名证书，安装时 Windows Defender SmartScreen 会提示' +
  '「Windows 已保护你的电脑 / 未知发布者」。这是未签名程序的正常表现，不是病毒：' +
  '点「更多信息」→「仍要运行」即可。安装包直接来自本项目的 GitHub Releases。';

export function UpdateDialog({
  state,
  onDismiss,
}: {
  state: UpdateState;
  onDismiss: () => void;
}) {
  const info = state.info;
  if (!info) return null;
  const portable = state.install === 'portable';
  const progress = state.progress;

  return (
    <div className="modal-backdrop" role="dialog" aria-modal="true" aria-label="有新版本可用">
      <div className="modal update-modal" data-testid="update-dialog">
        <h3>
          新版本 {info.version}
          <span className="update-version-from">当前 {state.currentVersion}</span>
        </h3>

        <div className="update-meta">
          <span>安装包 {mb(info.bytes)}</span>
          {info.releasedAt && <span>发布于 {info.releasedAt.slice(0, 10)}</span>}
          <span>{portable ? '免安装版' : '安装版'}</span>
        </div>

        <div className="update-notes" data-testid="update-notes">
          {info.notes ?? '这一版没有附更新说明。'}
        </div>

        {state.phase === 'downloading' && progress && (
          <div className="update-progress" data-testid="update-progress">
            <div className="update-bar">
              <span style={{ width: `${Math.max(1, Math.min(100, progress.percent))}%` }} />
            </div>
            <div className="update-progress-meta">
              <span>
                {mb(progress.transferred)} / {mb(progress.total)}（{progress.percent.toFixed(0)}%）
              </span>
              <span>{speed(progress.bytesPerSecond)}</span>
            </div>
          </div>
        )}

        {state.differential && (
          <p className="update-differential" data-testid="update-differential">
            {state.differential.applied
              ? `差分下载生效：完整包 ${mb(state.differential.fullBytes)}，实际只取了 ${mb(
                  state.differential.downloadedBytes,
                )}。`
              : state.differential.reason}
          </p>
        )}

        {state.phase === 'ready' && (
          <p className="update-ready" data-testid="update-ready">
            {portable
              ? '新的免安装版已经下好了。关掉当前窗口后，用新文件替换旧的即可；点下面的按钮可以在资源管理器里找到它。'
              : '安装包已下载完成。点「重启并安装」后应用会退出、运行安装器，装完自己启动。'}
          </p>
        )}

        {state.phase === 'error' && (
          <p className="update-error" data-testid="update-error">
            {state.error}
          </p>
        )}

        <p className="update-unsigned">{UNSIGNED_NOTICE}</p>

        <div className="row update-actions">
          {state.phase === 'available' && (
            <>
              <button className="btn ghost" onClick={onDismiss}>
                以后再说
              </button>
              <button className="btn" onClick={() => window.pixelfit.update.openReleasePage()}>
                查看 Release
              </button>
              <button className="btn primary" onClick={() => void window.pixelfit.update.download()}>
                <IconDownload size={14} />
                {portable ? '下载新版免安装包' : '下载更新'}
              </button>
            </>
          )}
          {state.phase === 'downloading' && (
            <button className="btn ghost" onClick={onDismiss}>
              放到后台下载
            </button>
          )}
          {state.phase === 'ready' && (
            <>
              <button className="btn ghost" onClick={onDismiss}>
                稍后
              </button>
              <button className="btn primary" onClick={() => window.pixelfit.update.install()}>
                {portable ? '在资源管理器中打开' : '重启并安装'}
              </button>
            </>
          )}
          {state.phase === 'error' && (
            <>
              <button className="btn ghost" onClick={onDismiss}>
                关闭
              </button>
              <button className="btn" onClick={() => window.pixelfit.update.openReleasePage()}>
                去 GitHub 手动下载
              </button>
            </>
          )}
        </div>
      </div>
    </div>
  );
}

/**
 * 弹窗的挂载点。只有「有新版 / 正在下载 / 下载完」这三个态才弹，
 * 而且用户关掉之后同一个版本不再自动弹第二次 —— 设置页永远能手动打开。
 */
export function UpdateGate() {
  const [state] = useUpdateState();
  const [dismissed, setDismissed] = useState<string | null>(null);

  const version = state?.info?.version ?? null;
  const shouldShow =
    !!state &&
    !!version &&
    dismissed !== version &&
    (state.phase === 'available' || state.phase === 'downloading' || state.phase === 'ready');

  if (!shouldShow || !state) return null;
  return <UpdateDialog state={state} onDismiss={() => setDismissed(version)} />;
}

// ---------------------------------------------------------------- 设置页卡片

const PHASE_LABEL: Record<UpdateState['phase'], string> = {
  idle: '还没检查过',
  checking: '正在检查…',
  current: '已是最新版本',
  available: '有新版本可用',
  downloading: '正在下载更新…',
  ready: '更新已下载，等待安装',
  error: '检查失败',
  disabled: '开发模式不检查更新',
};

export function UpdateCard() {
  const [state, setState] = useUpdateState();
  const [busy, setBusy] = useState(false);
  const [open, setOpen] = useState(false);

  const check = useCallback(async () => {
    setBusy(true);
    setState(await window.pixelfit.update.check());
    setBusy(false);
    setOpen(true);
  }, [setState]);

  if (!state) return null;
  const dialogVisible =
    open && (state.phase === 'available' || state.phase === 'downloading' || state.phase === 'ready');

  return (
    <div className="info-card" data-testid="update-card">
      <h4>
        <IconRefresh size={15} />
        应用更新
      </h4>
      <p>
        当前版本 <code>{state.currentVersion}</code>（
        {state.install === 'portable' ? '免安装版' : state.install === 'dev' ? '开发模式' : '安装版'}）
        · {PHASE_LABEL[state.phase]}
        {state.lastCheckedAt && ` · 上次检查 ${new Date(state.lastCheckedAt).toLocaleString('zh-CN')}`}
      </p>
      {state.phase === 'error' && state.error && <p className="update-error">{state.error}</p>}

      <label className="update-toggle">
        <input
          type="checkbox"
          checked={state.checkOnLaunch}
          disabled={busy}
          onChange={async (event) => {
            setState(await window.pixelfit.update.setCheckOnLaunch(event.target.checked));
          }}
        />
        <span>启动时自动检查更新</span>
      </label>
      <p className="update-privacy">
        检查更新只向 GitHub 请求 <code>latest.yml</code> 这一个版本信息文件，
        <strong>不上传任何本地数据</strong>——不发送素材库、照片、设置或任何标识信息，
        也不做使用统计。关掉这个开关之后，应用不会为了更新访问网络；
        需要时仍可以在这里手动点「立即检查更新」。
      </p>

      <div className="row" style={{ marginTop: 12 }}>
        <button className="btn sm" disabled={busy} onClick={() => void check()}>
          {busy ? '检查中…' : '立即检查更新'}
        </button>
        <button className="btn sm ghost" onClick={() => window.pixelfit.update.openReleasePage()}>
          打开 Releases 页面
        </button>
      </div>

      {dialogVisible && <UpdateDialog state={state} onDismiss={() => setOpen(false)} />}
    </div>
  );
}

// ---------------------------------------------------------------- 模型资源包

export function ModelPackCard() {
  const state = useModelPackState();
  const [busy, setBusy] = useState(false);
  if (!state || state.bundled) return null;

  const percent =
    state.totalBytes > 0 ? Math.min(100, (state.downloadedBytes / state.totalBytes) * 100) : 0;

  return (
    <div className="info-card" data-testid="model-pack-card">
      <h4>
        <IconDownload size={15} />
        离线识别模型
      </h4>
      <p>
        自动抠图用的两个模型合计 <strong>{mb(state.totalBytes)}</strong>，
        已经从安装包里拆出来单独下载 —— 它们在版本之间不变，
        所以只需要下载一次，之后每次应用更新都不会再重下这部分。
        下载地址是 GitHub Releases，落盘在 <code>{state.dir}</code>，
        每个文件都按 <code>models.lock.json</code> 里钉死的字节数和 MD5 校验。
      </p>
      <ul>
        {state.files.map((file) => (
          <li key={file.name}>
            <code>{file.name}</code> · {mb(file.bytes)} · {file.present ? '已就绪' : '未下载'}
          </li>
        ))}
      </ul>

      {(state.phase === 'downloading' || state.phase === 'verifying') && (
        <div className="update-progress" data-testid="model-pack-progress">
          <div className="update-bar">
            <span style={{ width: `${Math.max(1, percent)}%` }} />
          </div>
          <div className="update-progress-meta">
            <span>
              {mb(state.downloadedBytes)} / {mb(state.totalBytes)}
            </span>
            <span>{state.phase === 'verifying' ? '正在校验…' : `${percent.toFixed(0)}%`}</span>
          </div>
        </div>
      )}
      {state.phase === 'error' && state.error && <p className="update-error">{state.error}</p>}

      <div className="row" style={{ marginTop: 12 }}>
        <button
          className="btn sm primary"
          disabled={busy || state.phase === 'ready' || state.phase === 'downloading'}
          onClick={async () => {
            setBusy(true);
            await window.pixelfit.modelPack.download();
            setBusy(false);
          }}
        >
          {state.phase === 'ready' ? '模型已就绪' : busy ? '下载中…' : '下载识别模型'}
        </button>
      </div>
    </div>
  );
}
