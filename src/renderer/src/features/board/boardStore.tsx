/**
 * 画板状态。
 *
 * 刻意**不塞进 `state/store.tsx`**：模特换装那条线（CERE-14）正在同一份代码上改
 * 遮挡与贴合，全局 store 是双方都要碰的文件。画板自己起一个 Provider，
 * 与模特视图共享的只有衣橱数据和 Look 存储这两件已经存在的东西。
 *
 * 撤销 / 重做是整块快照栈。画板元素最多几十个，快照几 KB，
 * 用 diff 反而把「拖到一半撤销」这种边界搞复杂。
 */

import React, {
  createContext, useCallback, useContext, useEffect, useMemo, useRef, useState,
} from 'react';

import type { Asset } from '@shared/types';
import {
  BOARD_CANVAS, findOverlaps, type BoardBackgroundId, type BoardItem, type BoardLook,
} from '@shared/board';
import { useStore } from '@/state/store';
import { autoLayout, separate } from './layout';
import { renderBoard } from './paint';
import { boardShotHooks } from './shotHooks';
import { saveBoardLook } from './looks';

const HISTORY_LIMIT = 60;

export interface BoardState {
  background: BoardBackgroundId;
  items: BoardItem[];
  /** 0–1，自动布局时上装压住下装的程度；默认 0 = 完全不重叠 */
  wearOverlap: number;
}

const EMPTY_BOARD: BoardState = {
  background: 'paper_warm',
  items: [],
  wearOverlap: 0,
};

let seq = 0;
function newItemId(prefix: string): string {
  seq += 1;
  return `${prefix}_${Date.now().toString(36)}${seq.toString(36)}`;
}

function topZ(items: BoardItem[]): number {
  return items.reduce((m, i) => Math.max(m, i.z), 0);
}

export interface BoardCtx {
  state: BoardState;
  selected: string | null;
  select: (id: string | null) => void;

  /** 当前画板上有效的素材（跳过已被删除的） */
  boardAssets: Asset[];
  overlaps: [string, string][];

  add: (asset: Asset) => void;
  remove: (id: string) => void;
  clear: () => void;
  addTitle: (text?: string) => void;
  update: (id: string, patch: Partial<BoardItem>, opts?: { history?: boolean }) => void;
  setBackground: (id: BoardBackgroundId) => void;
  setWearOverlap: (v: number) => void;
  relayout: () => void;
  tidy: () => void;
  raise: (id: string, to: 'front' | 'back' | 'up' | 'down') => void;

  /** 手势开始时压一帧，结束时不用再压 */
  beginGesture: () => void;
  undo: () => void;
  redo: () => void;
  canUndo: boolean;
  canRedo: boolean;

  toBoardLook: () => BoardLook;
  load: (board: BoardLook) => void;

  /** 成图预览：把画板真的渲一遍，所见即导出 */
  previewUrl: string | null;
  openPreview: () => Promise<void>;
  closePreview: () => void;
}

const Ctx = createContext<BoardCtx | null>(null);

export function useBoard(): BoardCtx {
  const ctx = useContext(Ctx);
  if (!ctx) throw new Error('useBoard outside provider');
  return ctx;
}

/**
 * 让画板外面的界面（Look 库里点一张画板 Look）能把布局塞进来。
 * 与 `stage/handles.ts` 同一个套路：产生方和使用方在组件树上离得远，
 * 层层传 props 只会污染中间层。
 */
export const boardHandles: {
  load: ((board: BoardLook) => void) | null;
} = { load: null };

export function BoardProvider({ children }: { children: React.ReactNode }) {
  const { assets } = useStore();
  const [state, setState] = useState<BoardState>(EMPTY_BOARD);
  const [selected, setSelected] = useState<string | null>(null);
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);
  const stateRef = useRef<BoardState>(state);
  stateRef.current = state;
  const past = useRef<BoardState[]>([]);
  const future = useRef<BoardState[]>([]);
  const [stamp, setStamp] = useState(0); // 只为把 canUndo/canRedo 推给界面

  const push = useCallback((next: BoardState | ((s: BoardState) => BoardState)) => {
    setState((cur) => {
      past.current = [...past.current.slice(-HISTORY_LIMIT), cur];
      future.current = [];
      return typeof next === 'function' ? (next as (s: BoardState) => BoardState)(cur) : next;
    });
    setStamp((n) => n + 1);
  }, []);

  const assetById = useMemo(() => new Map(assets.map((a) => [a.id, a])), [assets]);

  const boardAssets = useMemo(
    () => state.items
      .filter((i) => i.kind === 'asset' && i.assetId && assetById.has(i.assetId))
      .map((i) => assetById.get(i.assetId!)!),
    [state.items, assetById],
  );

  const overlaps = useMemo(() => findOverlaps(state.items), [state.items]);

  const layoutFor = useCallback(
    (items: BoardItem[], wearOverlap: number): BoardItem[] => {
      const list = items
        .filter((i) => i.kind === 'asset' && i.assetId && assetById.has(i.assetId))
        .map((i) => assetById.get(i.assetId!)!);
      const hasTitle = items.some((i) => i.kind === 'text');
      const placed = autoLayout(list, { wearOverlap, hasTitle });
      const next = items.map((it) => {
        if (it.kind !== 'asset' || !it.assetId) return it;
        const p = placed.get(it.assetId);
        if (!p) return it;
        return { ...it, x: p.x, y: p.y, w: p.w, h: p.h, rotation: 0 };
      });
      // 标题回到顶部横幅位置
      return next.map((it) => (it.kind === 'text'
        ? { ...it, x: BOARD_CANVAS.w / 2, y: 150, rotation: 0 }
        : it));
    },
    [assetById],
  );

  const add = useCallback((asset: Asset) => {
    push((cur) => {
      const existing = cur.items.find((i) => i.assetId === asset.id);
      if (existing) {
        // 再点一次 = 从画板拿掉，和衣橱里「再点一次脱下」一致
        const items = cur.items.filter((i) => i.id !== existing.id);
        return { ...cur, items: layoutFor(items, cur.wearOverlap) };
      }
      const item: BoardItem = {
        id: newItemId('bi'),
        kind: 'asset',
        assetId: asset.id,
        x: BOARD_CANVAS.w / 2,
        y: BOARD_CANVAS.h / 2,
        w: 300,
        h: 300,
        rotation: 0,
        z: topZ(cur.items) + 1,
      };
      return { ...cur, items: layoutFor([...cur.items, item], cur.wearOverlap) };
    });
  }, [push, layoutFor]);

  const remove = useCallback((id: string) => {
    push((cur) => ({ ...cur, items: cur.items.filter((i) => i.id !== id) }));
    setSelected((s) => (s === id ? null : s));
  }, [push]);

  const clear = useCallback(() => {
    push((cur) => ({ ...cur, items: [] }));
    setSelected(null);
  }, [push]);

  const addTitle = useCallback((text = "Today's outfit") => {
    push((cur) => {
      const item: BoardItem = {
        id: newItemId('tx'),
        kind: 'text',
        text,
        font: 'serif_italic',
        x: BOARD_CANVAS.w / 2,
        y: 150,
        w: 720,
        h: 104,
        rotation: 0,
        z: topZ(cur.items) + 1,
      };
      // 加标题要给顶部让位，所以整块重排一次
      return { ...cur, items: layoutFor([...cur.items, item], cur.wearOverlap) };
    });
  }, [push, layoutFor]);

  const update = useCallback((id: string, patch: Partial<BoardItem>, opts?: { history?: boolean }) => {
    const apply = (cur: BoardState): BoardState => ({
      ...cur,
      items: cur.items.map((i) => (i.id === id ? { ...i, ...patch } : i)),
    });
    if (opts?.history === false) setState(apply);
    else push(apply);
  }, [push]);

  const setBackground = useCallback((id: BoardBackgroundId) => {
    push((cur) => ({ ...cur, background: id }));
  }, [push]);

  const setWearOverlap = useCallback((v: number) => {
    push((cur) => ({ ...cur, wearOverlap: v, items: layoutFor(cur.items, v) }));
  }, [push, layoutFor]);

  const relayout = useCallback(() => {
    push((cur) => ({ ...cur, items: layoutFor(cur.items, cur.wearOverlap) }));
  }, [push, layoutFor]);

  const tidy = useCallback(() => {
    push((cur) => ({ ...cur, items: separate(cur.items) }));
  }, [push]);

  const raise = useCallback((id: string, to: 'front' | 'back' | 'up' | 'down') => {
    push((cur) => {
      const sorted = [...cur.items].sort((a, b) => a.z - b.z);
      const idx = sorted.findIndex((i) => i.id === id);
      if (idx < 0) return cur;
      const [it] = sorted.splice(idx, 1);
      const at = to === 'front' ? sorted.length
        : to === 'back' ? 0
          : to === 'up' ? Math.min(sorted.length, idx + 1)
            : Math.max(0, idx - 1);
      sorted.splice(at, 0, it);
      const renumbered = sorted.map((i, n) => ({ ...i, z: n + 1 }));
      return { ...cur, items: renumbered };
    });
  }, [push]);

  const beginGesture = useCallback(() => {
    setState((cur) => {
      past.current = [...past.current.slice(-HISTORY_LIMIT), cur];
      future.current = [];
      return cur;
    });
    setStamp((n) => n + 1);
  }, []);

  const undo = useCallback(() => {
    setState((cur) => {
      const prev = past.current.pop();
      if (!prev) return cur;
      future.current = [...future.current.slice(-HISTORY_LIMIT), cur];
      return prev;
    });
    setStamp((n) => n + 1);
  }, []);

  const redo = useCallback(() => {
    setState((cur) => {
      const next = future.current.pop();
      if (!next) return cur;
      past.current = [...past.current.slice(-HISTORY_LIMIT), cur];
      return next;
    });
    setStamp((n) => n + 1);
  }, []);

  const toBoardLook = useCallback((): BoardLook => ({
    version: 1,
    canvas: { ...BOARD_CANVAS },
    background: state.background,
    items: state.items.map((i) => ({ ...i })),
  }), [state]);

  const load = useCallback((board: BoardLook) => {
    push(() => ({
      background: board.background,
      items: board.items.map((i) => ({ ...i })),
      wearOverlap: 0,
    }));
    setSelected(null);
  }, [push]);

  const openPreview = useCallback(async () => {
    const url = await renderBoard({
      version: 1,
      canvas: { ...BOARD_CANVAS },
      background: state.background,
      items: state.items,
    }, assets, 1);
    setPreviewUrl(url);
  }, [state, assets]);

  const closePreview = useCallback(() => setPreviewUrl(null), []);

  // 截图脚本的驱动入口：全部走界面同一套动作
  useEffect(() => {
    boardShotHooks.reset = () => {
      push(() => ({ ...EMPTY_BOARD }));
      setSelected(null);
      setPreviewUrl(null);
    };
    boardShotHooks.fill = (categories, list) => {
      const picked = categories
        .map((c) => list.find((a) => a.category === c))
        .filter(Boolean) as Asset[];
      push((cur) => {
        let items = cur.items;
        for (const asset of picked) {
          if (items.some((i) => i.assetId === asset.id)) continue;
          items = [...items, {
            id: newItemId('bi'),
            kind: 'asset' as const,
            assetId: asset.id,
            x: BOARD_CANVAS.w / 2,
            y: BOARD_CANVAS.h / 2,
            w: 300,
            h: 300,
            rotation: 0,
            z: topZ(items) + 1,
          }];
        }
        return { ...cur, items: layoutFor(items, cur.wearOverlap) };
      });
    };
    boardShotHooks.addTitle = () => addTitle();
    (window as unknown as { __pfUndo?: () => void }).__pfUndo = undo;
    boardShotHooks.selectFirst = (category) => {
      const asset = assets.find((a) => a.category === category);
      if (!asset) return;
      const item = stateRef.current.items.find((i) => i.assetId === asset.id);
      if (item) setSelected(item.id);
    };
    boardShotHooks.setBackground = (id) => setBackground(id);
    boardShotHooks.preview = async () => {
      const url = await renderBoard({
        version: 1,
        canvas: { ...BOARD_CANVAS },
        background: stateRef.current.background,
        items: stateRef.current.items,
      }, assets, 1);
      setPreviewUrl(url);
    };
    boardShotHooks.saveLook = async (name, occasion) => {
      await saveBoardLook({
        board: {
          version: 1,
          canvas: { ...BOARD_CANVAS },
          background: stateRef.current.background,
          items: stateRef.current.items,
        },
        assets,
        name,
        occasion,
      });
    };
  }, [assets, push, layoutFor, addTitle, setBackground, undo]);

  useEffect(() => {
    boardHandles.load = load;
    return () => {
      if (boardHandles.load === load) boardHandles.load = null;
    };
  }, [load]);

  const value = useMemo<BoardCtx>(() => ({
    state,
    selected,
    select: setSelected,
    boardAssets,
    overlaps,
    add, remove, clear, addTitle, update, setBackground, setWearOverlap,
    relayout, tidy, raise,
    beginGesture, undo, redo,
    canUndo: past.current.length > 0,
    canRedo: future.current.length > 0,
    toBoardLook, load,
    previewUrl, openPreview, closePreview,
    // stamp 只是让 canUndo / canRedo 跟着历史栈变化重算
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }), [
    state, selected, boardAssets, overlaps, add, remove, clear, addTitle, update,
    setBackground, setWearOverlap, relayout, tidy, raise, beginGesture, undo, redo,
    toBoardLook, load, previewUrl, openPreview, closePreview, stamp,
  ]);

  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}
