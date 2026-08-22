/**
 * CERE-64：把「缺模型」就地解决掉的那扇门。
 *
 * 用户点的是「导入这张照片」，不是「下载模型」。所以这里的规矩只有一条：
 * **下完继续他原本的动作**，不把人赶去设置页，也不让他下完再走一遍流程。
 *
 * 三个入口共用它 —— 导入页「选择照片并自动识别」、衣橱空态的导入按钮、
 * 基底页的模特照片上传。三条路失败的原因是同一个，就不该有三种说法。
 */
import {
  createContext, useCallback, useContext, useEffect, useMemo, useRef, useState,
} from 'react';

import { MODEL_PACK_HINT, type ModelPackState } from '@shared/update';
import { useStore } from '@/state/store';
import { useModelPackState } from '@/features/update/UpdatePanel';
import { IconDownload } from '@/ui/icons';
import { etaText, gateDecision, percentOf, speedText } from './model-gate';

type PendingAction = () => void | Promise<void>;

interface GateCtx {
  /**
   * 需要自动识别的动作都从这里走。模型就绪就直接跑；缺模型就弹说明、
   * 下完再跑同一个 action；运行时没装则如实告知，不假装能修。
   */
  runWithModel: (action: PendingAction) => void;
  /** 设置页之外也能主动打开这扇门（衣橱空态的「下载识别模型」） */
  openGate: () => void;
  pack: ModelPackState | null;
}

const Ctx = createContext<GateCtx | null>(null);

export function useModelGate(): GateCtx {
  const ctx = useContext(Ctx);
  if (!ctx) throw new Error('useModelGate outside provider');
  return ctx;
}

function mb(bytes: number): string {
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

export function ModelGateProvider({ children }: { children: React.ReactNode }) {
  const { pipeline, refreshPipeline, notify } = useStore();
  const pack = useModelPackState();
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  /** 「后台下载」之后不再自动续跑，只在完成时提示一句 —— 突然弹出选图框会吓到人。 */
  const [background, setBackground] = useState(false);
  const pending = useRef<PendingAction | null>(null);
  const running = useRef(false);

  const runWithModel = useCallback((action: PendingAction) => {
    const decision = gateDecision(pipeline, pack);
    if (decision === 'run') {
      void action();
      return;
    }
    if (decision === 'unavailable') {
      notify(pipeline?.message ?? '本地识别不可用，请先用透明底图片导入。');
      return;
    }
    pending.current = action;
    setBackground(false);
    setOpen(true);
  }, [pipeline, pack, notify]);

  const openGate = useCallback(() => {
    pending.current = null;
    setBackground(false);
    setOpen(true);
  }, []);

  const start = useCallback(async () => {
    setBusy(true);
    try {
      await window.pixelfit.modelPack.download();
    } finally {
      setBusy(false);
    }
  }, []);

  /**
   * 下完自动续跑。
   *
   * `pipeline.automatic` 是主进程按磁盘上的文件重新算的，必须先刷新再跑 ——
   * 否则续跑的那一次仍然拿着「模型缺失」的旧状态，又被自己的门拦一遍。
   */
  useEffect(() => {
    if (pack?.phase !== 'ready' || running.current) return;
    const action = pending.current;
    if (background) {
      if (action) notify('识别模型已就绪，现在可以自动抠图了。');
      pending.current = null;
      void refreshPipeline();
      return;
    }
    if (!open) return;
    running.current = true;
    void (async () => {
      try {
        await refreshPipeline();
        pending.current = null;
        setOpen(false);
        if (action) {
          notify('模型已就绪，继续导入…');
          await action();
        }
      } finally {
        running.current = false;
      }
    })();
  }, [pack?.phase, open, background, notify, refreshPipeline]);

  const value = useMemo<GateCtx>(() => ({ runWithModel, openGate, pack }), [runWithModel, openGate, pack]);

  return (
    <Ctx.Provider value={value}>
      {children}
      {open && (
        <ModelGateDialog
          pack={pack}
          busy={busy}
          hasPending={!!pending.current}
          onDownload={() => void start()}
          onCancelDownload={() => void window.pixelfit.modelPack.cancel()}
          onRedownload={() => {
            setBusy(true);
            void window.pixelfit.modelPack.redownload().finally(() => setBusy(false));
          }}
          onBackground={() => {
            setBackground(true);
            setOpen(false);
          }}
          onDismiss={() => {
            pending.current = null;
            setOpen(false);
          }}
        />
      )}
    </Ctx.Provider>
  );
}

interface DialogProps {
  pack: ModelPackState | null;
  busy: boolean;
  hasPending: boolean;
  onDownload: () => void;
  onCancelDownload: () => void;
  onRedownload: () => void;
  onBackground: () => void;
  onDismiss: () => void;
}

function ModelGateDialog({
  pack, busy, hasPending, onDownload, onCancelDownload, onRedownload, onBackground, onDismiss,
}: DialogProps) {
  const downloading = pack?.phase === 'downloading' || pack?.phase === 'verifying';
  const percent = percentOf(pack);
  const total = pack?.totalBytes ?? 0;
  const failed = pack?.phase === 'error';

  return (
    <div className="modal-backdrop" role="dialog" aria-modal="true" aria-label="下载识别模型">
      <div className="modal model-gate" data-testid="model-gate">
        <h3>
          <IconDownload size={16} />
          自动抠图还差一次性下载
        </h3>

        <p className="model-gate-lead">
          识别衣物、自动抠图靠的是两个本地模型，合计
          <strong> {total > 0 ? mb(total) : '约 382 MB'}</strong>。
          它们太大，装进安装包会让每次更新都多下几百兆，所以做成了单独下载。
        </p>
        <ul className="model-gate-facts">
          <li><strong>只下一次</strong>，以后升级版本都不会再下这部分。</li>
          <li>下载完成后<strong>自动继续你刚才的导入</strong>，不用重来一遍。</li>
          <li>下载期间可以点「后台下载」先去衣橱逛，或者直接导入已经抠好的透明底 PNG。</li>
          <li>模型来自本项目的 GitHub Releases，落盘在 <code>{pack?.dir || '用户数据目录'}</code>，按字节数和 MD5 校验。</li>
        </ul>

        {pack && pack.swept.length > 0 && !downloading && (
          <p className="model-gate-swept" data-testid="model-gate-swept">
            已清理上次没下完的残留文件（{pack.swept.join('、')}），这次从干净状态开始。
          </p>
        )}

        {downloading && (
          <div className="update-progress" data-testid="model-gate-progress">
            <div className="update-bar">
              <span style={{ width: `${Math.max(1, percent)}%` }} />
            </div>
            <div className="update-progress-meta">
              <span>{mb(pack?.downloadedBytes ?? 0)} / {mb(total)}</span>
              <span>
                {pack?.phase === 'verifying'
                  ? '正在校验…'
                  : [`${percent}%`, speedText(pack?.bytesPerSecond), etaText(pack?.etaSeconds)]
                      .filter(Boolean)
                      .join(' · ')}
              </span>
            </div>
          </div>
        )}

        {failed && (
          <div className="model-gate-error" data-testid="model-gate-error">
            <strong>{MODEL_PACK_HINT[pack?.failure ?? 'unknown']}</strong>
            <p>{pack?.error}</p>
          </div>
        )}

        <div className="row update-actions">
          {downloading ? (
            <>
              <button className="btn ghost" onClick={onCancelDownload}>
                取消下载
              </button>
              <button className="btn" onClick={onBackground}>
                后台下载
              </button>
            </>
          ) : (
            <>
              <button className="btn ghost" onClick={onDismiss}>
                以后再说
              </button>
              {failed && (
                <button className="btn" onClick={() => window.pixelfit.update.openReleasePage()}>
                  打开 Releases 页面
                </button>
              )}
              {failed && pack?.failure === 'checksum' && (
                <button className="btn" disabled={busy} onClick={onRedownload}>
                  清空重下
                </button>
              )}
              <button className="btn primary" disabled={busy} onClick={onDownload}>
                {failed ? '重试下载' : hasPending ? '立即下载并继续导入' : '立即下载'}
              </button>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
