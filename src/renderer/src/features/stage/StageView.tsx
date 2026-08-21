import { useEffect, useRef, useState } from 'react';

import { BODY_LABEL, BODY_TYPES, SLOT_LABEL } from '@shared/spec';
import type { TryOnProviderStatus } from '@shared/tryon';
import { toneSwatches, useStore, useWorn } from '@/state/store';
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
        <ModelMenu open={modelOpen} onOpenChange={setModelOpen} />

        <div className="seg subtle">
          <button className={engineId === 'layered' ? 'active' : ''} onClick={() => setEngineId('layered')}>
            即时预览
          </button>
          <button className={engineId === 'vton' ? 'active' : ''} onClick={() => setEngineId('vton')}>
            AI 高清
          </button>
        </div>

        <div className="seg subtle">
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

        <div style={{ flex: 1 }} />

        <button
          className={`btn sm ghost${outfit.noOcclusion ? ' on' : ''}`}
          title="关掉遮挡与身体遮罩裁切，看纯锚点叠图"
          onClick={() => dispatch({ type: 'set', patch: { noOcclusion: !outfit.noOcclusion } })}
        >
          {outfit.noOcclusion ? '无遮挡' : '遮挡开'}
        </button>
        <button
          className={`btn sm ghost${outfit.rawCompositing ? ' on' : ''}`}
          title="关掉羽化与接触阴影，看未处理的原始叠图"
          onClick={() => dispatch({ type: 'set', patch: { rawCompositing: !outfit.rawCompositing } })}
        >
          {outfit.rawCompositing ? '原始叠图' : '贴合处理'}
        </button>
        <button className="btn sm ghost" onClick={undo} disabled={!canUndo} title="撤销 Ctrl+Z">
          <IconUndo size={14} />
          撤销
        </button>
        {compare.length > 0 ? (
          <button className="btn sm ghost" onClick={clearCompare}>
            <IconX size={14} />
            退出对比
          </button>
        ) : (
          <button className="btn sm ghost" onClick={addToCompare}>
            <IconCompare size={14} />
            加入对比
          </button>
        )}
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
        >
          <div className="stage-caption">
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
          {engineId === 'vton' && (
            <CloudTryOnPanel
              status={cloudStatus}
              worn={worn}
              outfitKey={outfitKey}
              onResult={setCloudImage}
            />
          )}
        </DollCanvas>
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
    </section>
  );
}

/** 体型 / 肤色这类基底设置塞进一个下拉里，不再占满整条工具栏 */
function ModelMenu({ open, onOpenChange }: { open: boolean; onOpenChange: (v: boolean) => void }) {
  const { outfit, dispatch, bases } = useStore();
  const base = bases[outfit.body];
  const swatches = toneSwatches(base);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) onOpenChange(false);
    };
    window.addEventListener('mousedown', onDown);
    return () => window.removeEventListener('mousedown', onDown);
  }, [open, onOpenChange]);

  return (
    <div className="menu-wrap" ref={ref}>
      <button className={`btn sm ghost${open ? ' on' : ''}`} onClick={() => onOpenChange(!open)}>
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

          <p className="menu-note">
            底图：{base?.pack ?? '—'}
            {base?.source === 'library' ? '（素材库）' : '（内置）'}
          </p>
        </div>
      )}
    </div>
  );
}
