import { useEffect, useRef, useState } from 'react';

import { BODY_LABEL, BODY_TYPES, SLOT_LABEL } from '@shared/spec';
import type { TryOnProviderStatus } from '@shared/tryon';
import { hasBakedTones, toneSwatches, useStore, useWorn } from '@/state/store';
import { IconCompare, IconUndo, IconX } from '@/ui/icons';
import { DollCanvas } from './DollCanvas';
import { stageHandles } from './handles';
import { CloudTryOnPanel } from './CloudTryOnPanel';

const BACKGROUNDS: { key: 'studio_warm' | 'studio_cool' | 'solid' | 'none'; label: string }[] = [
  { key: 'studio_warm', label: '暖光' },
  { key: 'studio_cool', label: '冷光' },
  { key: 'solid', label: '纯色' },
  { key: 'none', label: '透明' },
];

export function StageView() {
  const {
    outfit, dispatch, assets, wear, undo, canUndo, selectedSlot,
    compare, clearCompare, addToCompare, stageTab, bases, engine, engineId, setEngineId,
    occlusion, setSelectedSlot,
  } = useStore();
  const worn = useWorn();
  const [modelOpen, setModelOpen] = useState(false);
  const [renderOpen, setRenderOpen] = useState(false);
  const [cloudImage, setCloudImage] = useState<string | null>(null);
  const [cloudStatus, setCloudStatus] = useState<TryOnProviderStatus | null>(null);
  const base = bases[outfit.body];
  const engineStatus = engine.status();
  const outfitKey = JSON.stringify({
    body: outfit.body,
    skin: outfit.skin,
    hair: outfit.hair,
    hairColor: outfit.hairColor,
    slots: outfit.slots,
    hidden: outfit.hidden,
  });

  useEffect(() => {
    void window.pixelfit.tryOn.status().then(setCloudStatus);
  }, []);

  const onDropAsset = (id: string) => {
    const asset = assets.find((a) => a.id === id);
    if (asset) wear(asset);
  };

  return (
    <section className="stage-wrap">
      <div className="stage-bar">
        <div className="stage-bar-main">
          <ModelMenu open={modelOpen} onOpenChange={setModelOpen} />

          <div className="seg subtle">
            <button className={engineId === 'layered' ? 'active' : ''} onClick={() => setEngineId('layered')}>
              即时预览
            </button>
            <button className={engineId === 'vton' ? 'active' : ''} onClick={() => setEngineId('vton')}>
              AI 高清
            </button>
          </div>

          {/* 背景只在宽窗口露在工具栏；窗口一窄就收进「模特」菜单，
              不占主工具栏的位置（CERE-28）。 */}
          <BackgroundSeg className="stage-bar-bg" />
        </div>

        <div className="stage-bar-aux">
          <button className="btn sm quiet" onClick={undo} disabled={!canUndo} title="撤销 Ctrl+Z">
            <IconUndo size={14} />
            撤销
          </button>
          {compare.length > 0 ? (
            <button className="btn sm quiet" onClick={clearCompare}>
              <IconX size={14} />
              退出对比
            </button>
          ) : (
            <button className="btn sm quiet" onClick={addToCompare}>
              <IconCompare size={14} />
              加入对比
            </button>
          )}
          <RenderMenu open={renderOpen} onOpenChange={setRenderOpen} />
        </div>
      </div>

      <div className={`stage${compare.length ? ' compare' : ''}`}>
        {compare.map((c, i) => (
          <DollCanvas key={`cmp-${i}`} outfit={c} label={`对比 ${i + 1}`} />
        ))}
        <DollCanvas
          outfit={outfit}
          highlightSlot={stageTab === 'layers' || stageTab === 'fit' ? selectedSlot : null}
          onDropAsset={onDropAsset}
          engineRef={stageHandles.engine}
          inputRef={stageHandles.input}
          overlayUrl={engineId === 'vton' ? cloudImage : null}
          interactive
        />
      </div>

      {/* 画布尺寸 / 引擎 / 遮挡规则是「状态说明」。CERE-28：以前它浮在模特脚上，
          还被生成面板压住；现在落到舞台下面单独一行，字重压到最轻。 */}
      <div className="stage-foot">
        <span>{base?.canvas.w} × {base?.canvas.h} · 原样素材</span>
        <span className="dot-sep" />
        <span>
          {engineId === 'vton'
            ? `${cloudStatus?.name ?? engine.name}${cloudImage ? '（AI 结果）' : '（本地兜底）'}`
            : `${engine.name}${engineStatus.available ? '' : '（回落）'}`}
        </span>
        <span className="dot-sep" />
        <span>{outfit.noOcclusion ? '遮挡已关' : `遮挡规则 ${occlusion.occlusions.length} 条`}</span>
      </div>

      {/*
        当前穿着的一条横带。放在画布**外面**：CERE-11 把它浮在舞台右上角，
        衣橱一有素材就压住模特半只手臂 —— 舞台唯一要给人看的就是模特。
      */}
      {worn.length > 0 && (
        <div className="worn-strip">
          <span className="worn-strip-head">共 {worn.length} 件</span>
          <div className="worn-strip-list">
            {[...worn].reverse().map((w) => (
              <button
                key={w.asset.id}
                className={`worn-chip${selectedSlot === w.slot ? ' active' : ''}`}
                title={`${w.asset.name} · 点击选中，×脱下`}
                onClick={() => setSelectedSlot(selectedSlot === w.slot ? null : w.slot)}
              >
                <img src={w.asset.thumbUrl} alt="" />
                <span className="worn-chip-slot">{SLOT_LABEL[w.slot]}</span>
                <span
                  className="worn-chip-x"
                  title="脱下"
                  onClick={(e) => {
                    e.stopPropagation();
                    dispatch({ type: 'takeOff', slot: w.slot });
                  }}
                >
                  <IconX size={11} />
                </span>
              </button>
            ))}
          </div>
        </div>
      )}

      {/* AI 高清的操作坤。在舞台**下面**，不在画布里：展开时舞台高度变小、
          人物等比缩下去让位，任何时候都不会盖住模特（CERE-28）。 */}
      {engineId === 'vton' && (
        <CloudTryOnPanel
          status={cloudStatus}
          worn={worn}
          outfitKey={outfitKey}
          onResult={setCloudImage}
        />
      )}
    </section>
  );
}

/** 背景切换。工具栏和「模特」菜单各渲染一份，由断点决定哪一份可见。 */
function BackgroundSeg({ className }: { className: string }) {
  const { outfit, dispatch } = useStore();
  return (
    <div className={`seg subtle ${className}`}>
      {BACKGROUNDS.map((b) => (
        <button
          key={b.key}
          className={outfit.background === b.key ? 'active' : ''}
          onClick={() => dispatch({ type: 'set', patch: { background: b.key } })}
        >
          {b.label}
        </button>
      ))}
    </div>
  );
}

/** 一个关闭按钮的下拉壳：点外面就收起。 */
function useDismiss(open: boolean, onOpenChange: (v: boolean) => void) {
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) onOpenChange(false);
    };
    window.addEventListener('mousedown', onDown);
    return () => window.removeEventListener('mousedown', onDown);
  }, [open, onOpenChange]);
  return ref;
}

/**
 * 渲染开关（遮挡 / 贴合处理）。这两个是排查用的开关，
 * 不该和「换模特」「换引擎」同一个重量摆在工具栏上（CERE-28）。
 */
function RenderMenu({ open, onOpenChange }: { open: boolean; onOpenChange: (v: boolean) => void }) {
  const { outfit, dispatch } = useStore();
  const ref = useDismiss(open, onOpenChange);
  const tweaked = outfit.noOcclusion || outfit.rawCompositing;

  return (
    <div className="menu-wrap" ref={ref}>
      <button
        className={`btn sm quiet${open || tweaked ? ' on' : ''}`}
        title="渲染开关"
        onClick={() => onOpenChange(!open)}
      >
        渲染
        {tweaked && <i className="dot-mark" />}
      </button>

      {open && (
        <div className="menu right">
          <div className="menu-label">遮挡与叠图</div>
          <label className="menu-switch">
            <input
              type="checkbox"
              checked={!outfit.noOcclusion}
              onChange={() => dispatch({ type: 'set', patch: { noOcclusion: !outfit.noOcclusion } })}
            />
            <span>
              遮挡规则
              <em>关掉后看纯锚点叠图，不裁切身体遮罩</em>
            </span>
          </label>
          <label className="menu-switch">
            <input
              type="checkbox"
              checked={!outfit.rawCompositing}
              onChange={() => dispatch({ type: 'set', patch: { rawCompositing: !outfit.rawCompositing } })}
            />
            <span>
              贴合处理
              <em>羽化与接触阴影；关掉看未处理的原始叠图</em>
            </span>
          </label>
        </div>
      )}
    </div>
  );
}

/** 体型 / 肤色这类基底设置塞进一个下拉里，不再占满整条工具栏 */
function ModelMenu({ open, onOpenChange }: { open: boolean; onOpenChange: (v: boolean) => void }) {
  const { outfit, dispatch, bases } = useStore();
  const base = bases[outfit.body];
  const swatches = toneSwatches(base);
  const ref = useDismiss(open, onOpenChange);

  return (
    <div className="menu-wrap" ref={ref}>
      <button className={`btn sm quiet${open ? ' on' : ''}`} onClick={() => onOpenChange(!open)}>
        模特 · {BODY_LABEL[outfit.body]}
        <i className="swatch mini" style={{ background: swatches[Math.min(outfit.skin - 1, swatches.length - 1)] }} />
      </button>

      {open && (
        <div className="menu">
          <div className="menu-label">体型</div>
          <div className="seg">
            {BODY_TYPES.map((b) => (
              <button
                key={b}
                className={outfit.body === b ? 'active' : ''}
                onClick={() => dispatch({ type: 'set', patch: { body: b } })}
              >
                {BODY_LABEL[b]}
              </button>
            ))}
          </div>

          {/* 底图包没烘培肤色时不摆色块：点了不动的控件比没有控件更坏（CERE-28） */}
          {hasBakedTones(base) && (
            <>
              <div className="menu-label">肤色</div>
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
            </>
          )}

          <div className="menu-label menu-bg-label">背景</div>
          <BackgroundSeg className="menu-bg" />

          <p className="menu-note">
            底图：{base?.pack ?? '—'}
            {base?.source === 'library' ? '（素材库）' : '（内置）'}
          </p>
        </div>
      )}
    </div>
  );
}
