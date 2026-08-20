/**
 * 画板右侧检查器：画布设置、布局、选中元素属性、保存与导出。
 *
 * 「重叠自检」是这块的重点 —— 需求的第一条验收就是每件单品不被遮挡，
 * 所以把检查结果直接摆在界面上，而不是让人肉眼判断。
 */

import { useMemo, useState } from 'react';

import {
  BOARD_BACKGROUNDS, BOARD_FONT_LABEL, OCCASIONS, type BoardFont,
} from '@shared/board';
import { useStore } from '@/state/store';
import {
  IconDownload, IconLayers, IconRefresh, IconSparkle, IconTrash, IconUndo,
} from '@/ui/icons';
import { useBoard } from './boardStore';
import { backgroundSwatch, renderBoard } from './paint';
import { saveBoardLook } from './looks';

export function BoardInspector() {
  const { assets, notify, refresh } = useStore();
  const {
    state, selected, update, remove, setBackground, setWearOverlap,
    relayout, tidy, raise, addTitle, clear, overlaps, toBoardLook, undo, redo, canUndo, canRedo,
    openPreview,
  } = useBoard();
  const [name, setName] = useState('');
  const [occasion, setOccasion] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);

  const swatches = useMemo(
    () => BOARD_BACKGROUNDS.map((b) => ({ ...b, url: backgroundSwatch(b.id) })),
    [],
  );

  const item = state.items.find((i) => i.id === selected) ?? null;
  const itemCount = state.items.filter((i) => i.kind === 'asset').length;
  const hasTitle = state.items.some((i) => i.kind === 'text');

  const doExport = async (scale: number) => {
    if (itemCount === 0) {
      notify('画板是空的，先加几件单品');
      return;
    }
    setBusy(true);
    try {
      const dataUrl = await renderBoard(toBoardLook(), assets, scale);
      const res = await window.pixelfit.exportPng({
        dataUrl,
        suggestedName: `pixelfit-board-${Date.now()}.png`,
      });
      if (res.ok) notify(`已导出到 ${res.path}`);
      else if (!res.canceled) notify(`导出失败：${res.error}`);
    } finally {
      setBusy(false);
    }
  };

  const doSave = async () => {
    if (itemCount === 0) {
      notify('画板是空的，先加几件单品');
      return;
    }
    setBusy(true);
    try {
      const saved = await saveBoardLook({
        board: toBoardLook(),
        assets,
        name: name.trim() || `搭配 ${new Date().toLocaleDateString('zh-CN')}`,
        occasion,
      });
      await refresh();
      setName('');
      notify(`已保存 Look「${saved.name}」`);
    } catch (err) {
      notify(`保存失败：${String(err)}`);
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="panel outfit">
      <div className="panel-head">
        <span className="panel-title">画板</span>
        <span className="count-pill">{itemCount} 件</span>
        <div style={{ flex: 1 }} />
        <button className="icon-btn" title="撤销" disabled={!canUndo} onClick={undo}>
          <IconUndo size={15} />
        </button>
        <button className="icon-btn" title="重做" disabled={!canRedo} onClick={redo}>
          <span style={{ display: 'inline-block', transform: 'scaleX(-1)' }}>
            <IconUndo size={15} />
          </span>
        </button>
        <button className="icon-btn" title="清空画板" onClick={clear}>
          <IconTrash size={15} />
        </button>
      </div>

      <div className="insp">
        <div className={`overlap-note${overlaps.length ? ' bad' : ''}`}>
          <span className="dot" />
          {overlaps.length === 0
            ? '布局自检：每件单品都完整可见，无遮挡'
            : `布局自检：${overlaps.length} 处遮挡，点「整理」推开`}
        </div>

        <div className="insp-sec">
          <h5>布局</h5>
          <div className="row">
            <button className="btn sm" onClick={relayout}>
              <IconRefresh size={13} />
              自动排版
            </button>
            <button className="btn sm ghost" onClick={tidy}>整理（推开重叠）</button>
          </div>
          <label className="slider-row">
            <span>穿着感重叠</span>
            <input
              type="range"
              min={0}
              max={100}
              value={Math.round(state.wearOverlap * 100)}
              onChange={(e) => setWearOverlap(Number(e.target.value) / 100)}
            />
            <b>{Math.round(state.wearOverlap * 100)}%</b>
          </label>
          <p className="insp-hint">
            0% = 完全不重叠（默认）。往上拉只让上装衣摆压住下装腰头，最多 12%，
            主体始终露着。
          </p>
        </div>

        <div className="insp-sec">
          <h5>画布背景</h5>
          <div className="bg-row">
            {swatches.map((b) => (
              <button
                key={b.id}
                className={`bg-swatch${state.background === b.id ? ' active' : ''}`}
                title={b.name}
                onClick={() => setBackground(b.id)}
              >
                <img src={b.url} alt={b.name} />
                <span>{b.name}</span>
              </button>
            ))}
          </div>
        </div>

        <div className="insp-sec">
          <h5>标题文字</h5>
          {hasTitle ? (
            <p className="insp-hint">画板上已有标题，双击它可以改文字。</p>
          ) : (
            <div className="row">
              <button className="btn sm" onClick={() => addTitle()}>加 “Today’s outfit”</button>
              <button className="btn sm ghost" onClick={() => addTitle('我的搭配')}>加中文标题</button>
            </div>
          )}
        </div>

        <div className="insp-sec">
          <h5>选中元素</h5>
          {!item ? (
            <p className="insp-hint">在画板上点一件单品来调整它的层级、旋转和大小。</p>
          ) : (
            <>
              <div className="row">
                <button className="btn sm ghost" onClick={() => raise(item.id, 'front')}>
                  <IconLayers size={13} />
                  置顶
                </button>
                <button className="btn sm ghost" onClick={() => raise(item.id, 'up')}>上移</button>
                <button className="btn sm ghost" onClick={() => raise(item.id, 'down')}>下移</button>
                <button className="btn sm ghost" onClick={() => raise(item.id, 'back')}>置底</button>
              </div>
              <label className="slider-row">
                <span>旋转</span>
                <input
                  type="range"
                  min={-180}
                  max={180}
                  value={Math.round(item.rotation)}
                  onChange={(e) => update(item.id, { rotation: Number(e.target.value) }, { history: false })}
                />
                <b>{Math.round(item.rotation)}°</b>
              </label>
              <label className="slider-row">
                <span>大小</span>
                <input
                  type="range"
                  min={20}
                  max={260}
                  value={Math.round((item.w / 400) * 100)}
                  onChange={(e) => {
                    const k = Number(e.target.value) / 100;
                    const ratio = item.h / item.w;
                    const w = 400 * k;
                    update(item.id, { w, h: w * ratio }, { history: false });
                  }}
                />
                <b>{Math.round(item.w)}px</b>
              </label>
              {item.kind === 'text' ? (
                <div className="row">
                  {(Object.keys(BOARD_FONT_LABEL) as BoardFont[]).map((f) => (
                    <button
                      key={f}
                      className={`chip${item.font === f ? ' active' : ''}`}
                      onClick={() => update(item.id, { font: f })}
                    >
                      {BOARD_FONT_LABEL[f]}
                    </button>
                  ))}
                </div>
              ) : (
                <div className="row">
                  <button
                    className={`chip${item.flipX ? ' active' : ''}`}
                    onClick={() => update(item.id, { flipX: !item.flipX })}
                  >
                    水平翻转
                  </button>
                  <button className="chip" onClick={() => update(item.id, { rotation: 0 })}>
                    摆正
                  </button>
                </div>
              )}
              <button className="btn sm ghost danger" onClick={() => remove(item.id)}>
                <IconTrash size={13} />
                从画板删除
              </button>
            </>
          )}
        </div>

        <div className="insp-sec">
          <h5>保存为 Look</h5>
          <input
            className="text-input"
            value={name}
            placeholder="给这套起个名字"
            onChange={(e) => setName(e.target.value)}
          />
          <div className="row wrap">
            {OCCASIONS.map((o) => (
              <button
                key={o.key}
                className={`chip${occasion.includes(o.key) ? ' active' : ''}`}
                onClick={() => setOccasion((cur) => (
                  cur.includes(o.key) ? cur.filter((k) => k !== o.key) : [...cur, o.key]
                ))}
              >
                {o.label}
              </button>
            ))}
          </div>
          <button className="btn primary" disabled={busy} onClick={() => void doSave()}>
            <IconSparkle size={14} />
            保存为 Look
          </button>
        </div>

        <div className="insp-sec">
          <h5>导出</h5>
          <div className="row">
            <button className="btn sm" disabled={busy} onClick={() => void doExport(2)}>
              <IconDownload size={13} />
              2× (2800×3600)
            </button>
            <button className="btn sm ghost" disabled={busy} onClick={() => void doExport(3)}>
              3× 高清
            </button>
          </div>
          <button
            className="btn sm ghost"
            disabled={busy || itemCount === 0}
            onClick={() => void openPreview()}
          >
            预览成图
          </button>
          <p className="insp-hint">
            预览用的是导出同一份绘制代码（背景 + 单品 + 接触阴影），所见即导出。
          </p>
        </div>
      </div>
    </section>
  );
}
