/**
 * 画板画布：屏幕上的摆放与编辑。
 *
 * 背景走 canvas（和导出同一份绘制代码），单品走 DOM —— 拖拽、旋转手柄、
 * 命中测试用 DOM 最省事，也最跟手。两者叠在同一个按比例缩放的舞台里。
 *
 * 交互对齐参考产品：选中框 + 左上角 × 删除 + 右下角旋转手柄 + 四角缩放，
 * 拖动时出中线与对齐参考线。
 */

import React, { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';

import type { Asset } from '@shared/types';
import { BOARD_CANVAS, boardBackground, itemBounds, type BoardItem } from '@shared/board';
import { useBoard } from './boardStore';
import { paintBackground, textFont } from './paint';

const SNAP = 10;

type Drag =
  | { mode: 'move'; id: string; dx: number; dy: number }
  | { mode: 'scale'; id: string; cx: number; cy: number; startDist: number; w: number; h: number }
  | { mode: 'rotate'; id: string; cx: number; cy: number; start: number; rotation: number };

export function BoardCanvas({ assets }: { assets: Asset[] }) {
  const { state, selected, select, update, remove, beginGesture } = useBoard();
  const wrapRef = useRef<HTMLDivElement>(null);
  const bgRef = useRef<HTMLCanvasElement>(null);
  const [scale, setScale] = useState(0.4);
  const [drag, setDrag] = useState<Drag | null>(null);
  const [guides, setGuides] = useState<{ x: number[]; y: number[] }>({ x: [], y: [] });
  const [editing, setEditing] = useState<string | null>(null);

  const byId = new Map(assets.map((a) => [a.id, a]));
  const ink = boardBackground(state.background).ink;

  // 舞台按容器大小等比缩放，画布坐标始终是 1400 × 1800
  useLayoutEffect(() => {
    const el = wrapRef.current;
    if (!el) return;
    const fit = () => {
      const pad = 32;
      const w = el.clientWidth - pad;
      const h = el.clientHeight - pad;
      if (w <= 0 || h <= 0) return;
      setScale(Math.min(w / BOARD_CANVAS.w, h / BOARD_CANVAS.h));
    };
    fit();
    const ro = new ResizeObserver(fit);
    ro.observe(el);
    return () => ro.disconnect();
  }, []);

  useEffect(() => {
    const c = bgRef.current;
    if (!c) return;
    const ctx = c.getContext('2d');
    if (!ctx) return;
    c.width = BOARD_CANVAS.w;
    c.height = BOARD_CANVAS.h;
    paintBackground(ctx, state.background, c.width, c.height);
  }, [state.background]);

  const toCanvas = useCallback((e: { clientX: number; clientY: number }) => {
    const stage = wrapRef.current?.querySelector('.board-stage') as HTMLElement | null;
    if (!stage) return { x: 0, y: 0 };
    const r = stage.getBoundingClientRect();
    return { x: (e.clientX - r.left) / scale, y: (e.clientY - r.top) / scale };
  }, [scale]);

  const snapMove = useCallback((item: BoardItem, x: number, y: number) => {
    const gx: number[] = [];
    const gy: number[] = [];
    const tol = SNAP / scale;
    let nx = x;
    let ny = y;

    const targetsX = [BOARD_CANVAS.w / 2];
    const targetsY = [BOARD_CANVAS.h / 2];
    for (const other of state.items) {
      if (other.id === item.id) continue;
      targetsX.push(other.x);
      targetsY.push(other.y);
    }
    for (const t of targetsX) {
      if (Math.abs(nx - t) < tol) {
        nx = t;
        gx.push(t);
        break;
      }
    }
    for (const t of targetsY) {
      if (Math.abs(ny - t) < tol) {
        ny = t;
        gy.push(t);
        break;
      }
    }
    setGuides({ x: gx, y: gy });
    return { x: nx, y: ny };
  }, [state.items, scale]);

  useEffect(() => {
    if (!drag) return;
    const onMove = (e: PointerEvent) => {
      const p = toCanvas(e);
      const item = state.items.find((i) => i.id === drag.id);
      if (!item) return;
      if (drag.mode === 'move') {
        const s = snapMove(item, p.x - drag.dx, p.y - drag.dy);
        update(drag.id, { x: s.x, y: s.y }, { history: false });
      } else if (drag.mode === 'scale') {
        const dist = Math.hypot(p.x - drag.cx, p.y - drag.cy);
        const k = Math.max(0.12, dist / Math.max(1, drag.startDist));
        update(drag.id, { w: drag.w * k, h: drag.h * k }, { history: false });
      } else {
        const ang = (Math.atan2(p.y - drag.cy, p.x - drag.cx) * 180) / Math.PI;
        let next = drag.rotation + (ang - drag.start);
        if (Math.abs(next % 90) < 4) next = Math.round(next / 90) * 90; // 贴近 90° 的吸一下
        update(drag.id, { rotation: next }, { history: false });
      }
    };
    const onUp = () => {
      setDrag(null);
      setGuides({ x: [], y: [] });
    };
    window.addEventListener('pointermove', onMove);
    window.addEventListener('pointerup', onUp);
    return () => {
      window.removeEventListener('pointermove', onMove);
      window.removeEventListener('pointerup', onUp);
    };
  }, [drag, state.items, toCanvas, snapMove, update]);

  const startMove = (e: React.PointerEvent, item: BoardItem) => {
    e.preventDefault();
    e.stopPropagation();
    select(item.id);
    beginGesture();
    const p = toCanvas(e);
    setDrag({ mode: 'move', id: item.id, dx: p.x - item.x, dy: p.y - item.y });
  };

  const startScale = (e: React.PointerEvent, item: BoardItem) => {
    e.preventDefault();
    e.stopPropagation();
    beginGesture();
    const p = toCanvas(e);
    setDrag({
      mode: 'scale',
      id: item.id,
      cx: item.x,
      cy: item.y,
      startDist: Math.max(1, Math.hypot(p.x - item.x, p.y - item.y)),
      w: item.w,
      h: item.h,
    });
  };

  const startRotate = (e: React.PointerEvent, item: BoardItem) => {
    e.preventDefault();
    e.stopPropagation();
    beginGesture();
    const p = toCanvas(e);
    setDrag({
      mode: 'rotate',
      id: item.id,
      cx: item.x,
      cy: item.y,
      start: (Math.atan2(p.y - item.y, p.x - item.x) * 180) / Math.PI,
      rotation: item.rotation,
    });
  };

  const ordered = [...state.items].sort((a, b) => a.z - b.z);

  return (
    <div className="board-wrap" ref={wrapRef} onPointerDown={() => select(null)}>
      <div
        className="board-stage"
        style={{
          width: BOARD_CANVAS.w * scale,
          height: BOARD_CANVAS.h * scale,
        }}
      >
        <canvas ref={bgRef} className="board-bg" />

        {guides.x.map((x) => (
          <div key={`gx${x}`} className="board-guide v" style={{ left: x * scale }} />
        ))}
        {guides.y.map((y) => (
          <div key={`gy${y}`} className="board-guide h" style={{ top: y * scale }} />
        ))}

        {ordered.map((item) => {
          const asset = item.assetId ? byId.get(item.assetId) : undefined;
          if (item.kind === 'asset' && !asset) return null;
          const isSel = selected === item.id;
          const box = {
            left: (item.x - item.w / 2) * scale,
            top: (item.y - item.h / 2) * scale,
            width: item.w * scale,
            height: item.h * scale,
            transform: `rotate(${item.rotation}deg)`,
          } as React.CSSProperties;

          return (
            <div
              key={item.id}
              className={`board-item${isSel ? ' selected' : ''}`}
              style={box}
              onPointerDown={(e) => startMove(e, item)}
              onDoubleClick={() => item.kind === 'text' && setEditing(item.id)}
            >
              {item.kind === 'asset' ? (
                <img
                  src={asset!.cutoutUrl}
                  alt={asset!.name}
                  draggable={false}
                  style={{ transform: item.flipX ? 'scaleX(-1)' : undefined }}
                />
              ) : editing === item.id ? (
                <input
                  className="board-text-edit"
                  autoFocus
                  defaultValue={item.text}
                  style={{ fontSize: item.h * 0.72 * scale, color: item.color ?? ink }}
                  onPointerDown={(e) => e.stopPropagation()}
                  onBlur={(e) => {
                    update(item.id, { text: e.target.value });
                    setEditing(null);
                  }}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter') (e.target as HTMLInputElement).blur();
                  }}
                />
              ) : (
                <span
                  className="board-text"
                  style={{
                    fontSize: item.h * 0.72 * scale,
                    color: item.color ?? ink,
                    font: undefined,
                    fontFamily: textFont(item, 16).split('px ')[1],
                    fontStyle: item.font === 'serif_italic' ? 'italic' : 'normal',
                  }}
                >
                  {item.text}
                </span>
              )}

              {isSel && (
                <>
                  <span className="board-frame" />
                  <button
                    className="board-handle del"
                    title="删除"
                    onPointerDown={(e) => {
                      e.preventDefault();
                      e.stopPropagation();
                      remove(item.id);
                    }}
                  >
                    ×
                  </button>
                  <span
                    className="board-handle scale"
                    title="缩放"
                    onPointerDown={(e) => startScale(e, item)}
                  />
                  <span
                    className="board-handle rotate"
                    title="旋转"
                    onPointerDown={(e) => startRotate(e, item)}
                  >
                    ⟳
                  </span>
                </>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
}

/** 给检查器用：一件在画布里的可见范围，判断是否越界 */
export function outOfCanvas(item: BoardItem): boolean {
  const b = itemBounds(item);
  return b.x1 < 0 || b.y1 < 0 || b.x2 > BOARD_CANVAS.w || b.y2 > BOARD_CANVAS.h;
}
