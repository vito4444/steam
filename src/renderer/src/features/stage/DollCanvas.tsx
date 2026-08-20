import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';

import { defaultTuck } from '@shared/occlusion';
import { SLOT_LABEL, SLOT_Z, type Slot } from '@shared/spec';
import { DEFAULT_FIT, type Asset } from '@shared/types';
import type { RenderInput } from '@/render/types';
import { toneIndex, useStore, type OutfitState } from '@/state/store';
import type { TryOnEngine } from '@/tryon/types';

export function useRenderInput(outfit: OutfitState, highlightSlot: Slot | null): RenderInput | null {
  const { assets, bases, occlusion } = useStore();
  return useMemo(() => {
    const base = bases[outfit.body];
    if (!base) return null;
    const byId = new Map(assets.map((a) => [a.id, a]));
    const worn: RenderInput['worn'] = [];
    for (const [slot, id] of Object.entries(outfit.slots)) {
      if (!id) continue;
      const asset = byId.get(id) as Asset | undefined;
      if (!asset) continue;
      const s = slot as Slot;
      worn.push({
        asset,
        slot: s,
        z: SLOT_Z[s] + (outfit.zOverrides[id] ?? asset.z_offset ?? 0),
        fit: { ...DEFAULT_FIT, ...asset.fit, ...outfit.fitOverrides[id] },
        hidden: outfit.hidden.includes(s),
        highlight: highlightSlot === s,
        tuck: outfit.tuckOverrides[id] ?? defaultTuck(asset.attributes, s),
      });
    }
    worn.sort((a, b) => a.z - b.z);
    return {
      base,
      body: outfit.body,
      tone: toneIndex(outfit.skin, base),
      hairStyle: outfit.hair,
      hairHex: outfit.hairColor,
      worn,
      background: outfit.background,
      rawCompositing: outfit.rawCompositing,
      noOcclusion: outfit.noOcclusion,
      occlusion,
    };
  }, [assets, bases, outfit, highlightSlot, occlusion]);
}

interface Props {
  outfit: OutfitState;
  highlightSlot?: Slot | null;
  label?: string;
  onDropAsset?: (assetId: string) => void;
  engineRef?: React.MutableRefObject<TryOnEngine | null>;
  inputRef?: React.MutableRefObject<RenderInput | null>;
  overlayUrl?: string | null;
  /** 允许在画布上直接拖动 / 缩放 / 调层级 */
  interactive?: boolean;
  children?: React.ReactNode;
}

/**
 * 人物舞台。画布内部坐标就是底图包声明的画布（CERE-6 写实底图是
 * 1152×2304），显示时按容器高度等比缩放 —— 缩放系数进入 buildScene，
 * 每层各自按目标尺寸绘制，比先合成再整体缩放清晰得多。
 *
 * 这里只认 TryOnEngine 接口，不认分层贴图：换成 VTON 引擎时本文件不用改。
 * 画布上的手动微调走引擎的可选能力（hitTest / fitUnit）—— 引擎给不出，
 * 微调就自己收起来，而不是留一个点了没反应的假交互。
 */
export function DollCanvas({
  outfit, highlightSlot = null, label, onDropAsset, engineRef, inputRef,
  overlayUrl, interactive = false, children,
}: Props) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const wrapRef = useRef<HTMLDivElement>(null);
  const { engine, bases, dispatch, assets, selectedSlot, setSelectedSlot, notify } = useStore();
  const [scale, setScale] = useState(0.3);
  const [dropActive, setDropActive] = useState(false);
  const [grabbing, setGrabbing] = useState(false);

  const canvas = bases[outfit.body]?.canvas ?? { w: 1152, h: 2304 };
  const input = useRenderInput(outfit, highlightSlot);
  if (inputRef) inputRef.current = input;
  if (engineRef) engineRef.current = engine;

  const unitRef = useRef<number | null>(null);
  const dragRef = useRef<{
    id: string; slot: Slot; x0: number; y0: number; dx0: number; dy0: number; moved: boolean;
  } | null>(null);

  useEffect(() => {
    if (canvasRef.current) engine.mount(canvasRef.current);
    return () => engine.destroy();
  }, [engine]);

  useEffect(() => {
    const el = wrapRef.current;
    if (!el) return;
    const ro = new ResizeObserver(() => {
      const h = el.clientHeight;
      const w = el.clientWidth;
      setScale(Math.max(Math.min(h / canvas.h, w / canvas.w), 0.05));
    });
    ro.observe(el);
    return () => ro.disconnect();
  }, [canvas.h, canvas.w]);

  useEffect(() => {
    if (!input) return;
    const el = canvasRef.current;
    if (el) {
      el.style.width = `${canvas.w * scale}px`;
      el.style.height = `${canvas.h * scale}px`;
    }
    void engine.render(input, scale);
  }, [input, scale, engine, canvas.h, canvas.w]);

  // 一个肩宽等于多少显示像素 —— 微调量的单位换算靠它
  useEffect(() => {
    if (!interactive || !input || !engine.fitUnit) {
      unitRef.current = null;
      return;
    }
    let alive = true;
    void engine.fitUnit(input, scale).then((u) => {
      if (alive) unitRef.current = u;
    });
    return () => { alive = false; };
  }, [interactive, input, scale, engine]);

  /** 显示坐标（相对画布左上角） */
  const toLocal = useCallback((e: React.PointerEvent | React.WheelEvent) => {
    const el = canvasRef.current;
    if (!el) return null;
    const r = el.getBoundingClientRect();
    return { x: e.clientX - r.left, y: e.clientY - r.top };
  }, []);

  const wornOf = useCallback((id: string): Slot | null => {
    for (const [slot, ref] of Object.entries(outfit.slots)) {
      if (ref === id) return slot as Slot;
    }
    return null;
  }, [outfit.slots]);

  const onPointerDown = async (e: React.PointerEvent) => {
    if (!interactive || !input || !engine.hitTest) return;
    const pt = toLocal(e);
    if (!pt) return;
    const id = await engine.hitTest(input, scale, pt.x, pt.y);
    if (!id) {
      setSelectedSlot(null);
      return;
    }
    const slot = wornOf(id);
    if (!slot) return;
    setSelectedSlot(slot);
    const cur = outfit.fitOverrides[id] ?? {};
    const asset = assets.find((a) => a.id === id);
    dragRef.current = {
      id,
      slot,
      x0: e.clientX,
      y0: e.clientY,
      dx0: cur.dx ?? asset?.fit.dx ?? 0,
      dy0: cur.dy ?? asset?.fit.dy ?? 0,
      moved: false,
    };
    setGrabbing(true);
    (e.currentTarget as HTMLElement).setPointerCapture?.(e.pointerId);
  };

  const onPointerMove = (e: React.PointerEvent) => {
    const d = dragRef.current;
    const unit = unitRef.current;
    if (!d || !unit) return;
    const ddx = e.clientX - d.x0;
    const ddy = e.clientY - d.y0;
    if (!d.moved && Math.abs(ddx) + Math.abs(ddy) < 3) return; // 点选和拖动分开
    d.moved = true;
    dispatch({
      type: 'setFit',
      assetId: d.id,
      patch: { dx: d.dx0 + ddx / unit, dy: d.dy0 + ddy / unit },
    });
  };

  const endDrag = (e: React.PointerEvent) => {
    if (!dragRef.current) return;
    (e.currentTarget as HTMLElement).releasePointerCapture?.(e.pointerId);
    dragRef.current = null;
    setGrabbing(false);
  };

  // 选中的那一件（画布与右侧面板共用同一个选中态）
  const selectedId = interactive && selectedSlot ? outfit.slots[selectedSlot] ?? null : null;
  const selected = selectedId
    ? { id: selectedId, slot: selectedSlot as Slot, name: assets.find((a) => a.id === selectedId)?.name ?? '' }
    : null;

  const onWheel = (e: React.WheelEvent) => {
    if (!interactive) return;
    const id = selectedId;
    if (!id) return;
    e.preventDefault();
    const cur = outfit.fitOverrides[id]?.scale
      ?? assets.find((a) => a.id === id)?.fit.scale ?? 1;
    const next = Math.min(Math.max(cur * (e.deltaY < 0 ? 1.04 : 1 / 1.04), 0.6), 1.6);
    dispatch({ type: 'setFit', assetId: id, patch: { scale: next } });
  };

  const nudgeZ = (delta: number) => {
    const id = selectedId;
    if (!id) return;
    const asset = assets.find((a) => a.id === id);
    const cur = outfit.zOverrides[id] ?? asset?.z_offset ?? 0;
    const next = Math.min(Math.max(cur + delta, -4), 4);
    dispatch({ type: 'setZ', assetId: id, value: next });
    notify(delta > 0 ? '上移一层' : '下移一层');
  };

  return (
    <div
      ref={wrapRef}
      className={`doll${dropActive ? ' drop-active' : ''}${interactive ? ' interactive' : ''}${grabbing ? ' grabbing' : ''}`}
      style={{ aspectRatio: `${canvas.w} / ${canvas.h}` }}
      onPointerDown={(e) => void onPointerDown(e)}
      onPointerMove={onPointerMove}
      onPointerUp={endDrag}
      onPointerCancel={endDrag}
      onWheel={onWheel}
      onDragOver={(e) => {
        if (!onDropAsset) return;
        e.preventDefault();
        e.dataTransfer.dropEffect = 'copy';
        setDropActive(true);
      }}
      onDragLeave={() => setDropActive(false)}
      onDrop={(e) => {
        if (!onDropAsset) return;
        e.preventDefault();
        setDropActive(false);
        const id = e.dataTransfer.getData('text/pixelfit-asset');
        if (id) onDropAsset(id);
      }}
    >
      <canvas ref={canvasRef} />
      {overlayUrl && <img className="cloud-result-image" src={overlayUrl} alt="AI 高清试穿结果" />}
      {label && <span className="doll-tag">{label}</span>}
      {dropActive && <div className="drop-hint">松手即穿上</div>}

      {selected && (
        <div className="stage-tool" onPointerDown={(e) => e.stopPropagation()}>
          <span className="stage-tool-name">
            {SLOT_LABEL[selected.slot]} · {selected.name}
          </span>
          <button title="上移一层" onClick={() => nudgeZ(1)}>▲</button>
          <button title="下移一层" onClick={() => nudgeZ(-1)}>▼</button>
          <button
            title="恢复自动贴合"
            onClick={() => {
              dispatch({ type: 'resetFit', assetId: selected.id });
              dispatch({ type: 'setZ', assetId: selected.id, value: 0 });
            }}
          >
            复位
          </button>
          <span className="stage-tool-hint">拖动移位 · 滚轮缩放</span>
        </div>
      )}

      {children}
    </div>
  );
}
