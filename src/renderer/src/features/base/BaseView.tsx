import { useState } from 'react';

import { BODY_LABEL, BODY_TYPES } from '@shared/spec';
import { hasBakedTones, toneSwatches, useStore } from '@/state/store';
import { errorMessage } from '@/features/import/importFeedback';
import { useModelGate } from '@/features/import/ModelGate';
import { gateDecision, modelStatus } from '@/features/import/model-gate';
import { IconImport } from '@/ui/icons';

/**
 * 模特基底。
 *
 * CERE-28：这页以前是一屏写给开发者看的说明 —— manifest.json 里有哪几个字段、
 * 底图包要解压到哪个目录 —— 唯独没有「换成我自己的模特」这个动作。成员的原话
 * 是「不用去找文件夹修改」。
 *
 * 现在这页的主体就是上传入口：选一张全身照，本地抠图 + 自动量锚点，直接接管。
 * 那份字段说明降级成一个默认收起的 disclosure —— 它对排查有用，但不该是首屏。
 */
export function BaseView() {
  const { outfit, dispatch, bases, reloadBases, pipeline, notify, setView } = useStore();
  const { runWithModel, pack } = useModelGate();
  const modelState = modelStatus(pipeline, pack);
  const decision = gateDecision(pipeline, pack);
  const base = bases[outfit.body];
  const swatches = toneSwatches(base);
  const tonesUsable = hasBakedTones(base);
  const [busy, setBusy] = useState<'import' | 'reset' | null>(null);
  const [notes, setNotes] = useState<string[]>([]);
  const custom = base?.source === 'library';
  const preview = base?.tones[0]?.layers[0]?.url ?? base?.layers[0]?.url ?? null;

  const uploadPhoto = async () => {
    setBusy('import');
    setNotes([]);
    try {
      const result = await window.pixelfit.base.importPhoto(outfit.body);
      if (result.canceled) return;
      if (!result.ok) {
        notify(`底图未更新：${result.error ?? '抠图未完成'}`);
        return;
      }
      await reloadBases();
      const extra = [...(result.notes ?? [])];
      if (result.fallbackAnchors) extra.push('剪影量不出尺寸，锚点用了按画布缩放的兜底值');
      setNotes(extra);
      notify(`已换成你上传的模特（${result.canvas?.w} × ${result.canvas?.h}）`);
    } catch (error) {
      notify(`底图未更新：${errorMessage(error)}`);
    } finally {
      setBusy(null);
    }
  };

  const restoreBuiltin = async () => {
    setBusy('reset');
    setNotes([]);
    try {
      const result = await window.pixelfit.base.reset(outfit.body);
      await reloadBases();
      notify(result.restored ? '已恢复内置模特' : '当前用的已经是内置模特');
    } finally {
      setBusy(null);
    }
  };

  return (
    <div className="view">
      <header className="view-head">
        <h2>模特基底</h2>
        <p>选一张全身照，应用在本地把人抠出来并自己量好锚点。照片不会上传。</p>
      </header>

      <section className="block">
        <div className="block-head">
          <h3>当前模特</h3>
          <span className={`tag${custom ? ' ok' : ''}`}>{custom ? '你上传的' : '应用内置'}</span>
        </div>

        <div className="base-swap">
          <figure className="base-preview">
            {preview
              ? <img src={preview} alt="当前模特底图" />
              : <span className="base-preview-empty">没有底图</span>}
          </figure>

          <div className="base-swap-body">
            <dl className="fact-list">
              <div><dt>底图包</dt><dd>{base?.pack ?? '—'}</dd></div>
              <div><dt>画布</dt><dd>{base?.canvas.w} × {base?.canvas.h}</dd></div>
              <div><dt>肤色档位</dt><dd>{tonesUsable ? swatches.length : '仅一档'}</dd></div>
            </dl>

            <div className="row wrap">
              {/* CERE-64：上传模特照片同样要走本地抠图，缺模型时走同一扇门就地补下载。 */}
              <button
                className="btn primary"
                disabled={busy !== null || decision === 'unavailable'}
                onClick={() => runWithModel(uploadPhoto)}
              >
                <IconImport size={14} />
                {busy === 'import' ? '正在抠图…' : '上传模特照片'}
              </button>
              {custom && (
                <>
                  <button className="btn quiet" disabled={busy !== null} onClick={() => void restoreBuiltin()}>
                    {busy === 'reset' ? '恢复中…' : '恢复内置模特'}
                  </button>
                  <button className="btn quiet" onClick={() => setView('wardrobe')}>去试穿</button>
                </>
              )}
            </div>

            {!pipeline?.automatic && (
              <p className={`note ${decision === 'gate' ? '' : 'warn'}`} data-testid="base-model-status">
                {modelState.label}：{modelState.detail}
              </p>
            )}
            {notes.length > 0 && (
              <ul className="note-list">
                {notes.map((item) => <li key={item}>{item}</li>)}
              </ul>
            )}
            <p className="note">
              正面全身、双手自然下垂、背景干净时量得最准。头顶和脚底都要在画面里。
            </p>
          </div>
        </div>
      </section>

      <section className="block">
        <div className="block-head">
          <h3>体型与肤色</h3>
        </div>
        <div className="row wrap" style={{ alignItems: 'center' }}>
          <div className="seg">
            {BODY_TYPES.map((body) => (
              <button
                key={body}
                className={outfit.body === body ? 'active' : ''}
                onClick={() => dispatch({ type: 'set', patch: { body } })}
              >
                {BODY_LABEL[body]}
              </button>
            ))}
          </div>
          {tonesUsable ? (
            <div className="swatch-row">
              {swatches.map((tone, i) => (
                <button
                  key={tone + i}
                  title={base?.tones[i]?.name ?? `肤色 ${i + 1}`}
                  className={`swatch${outfit.skin === i + 1 ? ' active' : ''}`}
                  style={{ background: tone }}
                  onClick={() => dispatch({ type: 'set', patch: { skin: i + 1 } })}
                />
              ))}
            </div>
          ) : (
            <span className="note">这套底图没有烘焙多档肤色，不提供换肤</span>
          )}
        </div>
        <p className="note">两个体型各自一套底图，上传只替换当前选中的这一套。</p>
      </section>

      <details className="disclosure">
        <summary>底图包是怎么回事（不看也能用）</summary>
        <p>
          一个底图包就是 <code>library/base/&lt;体型&gt;/</code> 下的一张透明 PNG 加一份
          <code>manifest.json</code>：<code>canvas</code> 是画布尺寸，<code>anchors</code> 是肩 / 胸 /
          腰 / 胯 / 膝 / 踝等 17 个对位点。上传照片时这两项都是量完剪影自动写好的。
        </p>
        <p>
          衣物的缩放不读这份 manifest，而是实测底图轮廓：在锚点所在的行上量身体有多宽，
          再按版型宽松量把衣服缩到那个宽度。所以底图换成真人照片后，衣物尺寸会自动跟着人体走。
        </p>
      </details>
    </div>
  );
}
