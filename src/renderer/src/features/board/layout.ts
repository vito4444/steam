/**
 * 画板自动布局。
 *
 * 需求里最硬的一条：**默认布局每件单品主体不被遮挡**。所以这里不做「自由摆盘」，
 * 而是两列严格纵向排列：
 *
 *   主列（左）  外套 → 上装 / 连衣裙 → 内层 → 下装 → 袜/打底 → 鞋
 *   副列（右）  包 → 帽 / 眼镜 / 围巾 / 腰带 / 手套 / 其他配饰
 *
 * 纵向排列天然不重叠，剩下要保证的只有「一列装不下时整体缩小」和
 * 「单件太宽时按列宽反算高度」。收尾再跑一次 `findOverlaps` 兜底，
 * 真有相交就把后面那件往下推，宁可留白也不叠。
 *
 * 「穿着感」重叠（上装衣摆压住下装腰头）是可选项，默认 0 —— 打开时也只允许
 * 压到上一件高度的 12%，且只发生在上装 / 下装这一对相邻件之间。
 */

import type { Asset } from '@shared/types';
import type { Category } from '@shared/spec';
import { BOARD_CANVAS, findOverlaps, itemBounds, type BoardItem } from '@shared/board';

/** 每个品类在主列里的顺序权重（小的排上面） */
const MAIN_ORDER: Partial<Record<Category, number>> = {
  outer: 10,
  top: 20,
  dress: 20,
  underlayer: 25,
  bottom: 40,
  legwear: 50,
  shoe: 60,
};

/** 副列顺序 */
const SIDE_ORDER: Partial<Record<Category, number>> = {
  headwear: 10,
  eyewear: 15,
  neckwear: 20,
  bag: 30,
  belt: 40,
  gloves: 50,
  other: 60,
};

/**
 * 高度权重：一件在列里占多大份额。
 * 数值是相对的，真实像素由「列高 ÷ 权重和」决定，所以件数变了也不会溢出。
 */
const HEIGHT_WEIGHT: Record<Category, number> = {
  outer: 1.15,
  top: 1.0,
  dress: 1.75,
  underlayer: 0.85,
  bottom: 1.5,
  legwear: 0.95,
  shoe: 0.5,
  bag: 0.78,
  headwear: 0.5,
  eyewear: 0.34,
  neckwear: 0.52,
  belt: 0.34,
  gloves: 0.42,
  other: 0.55,
};

const GAP = 34;
const MARGIN_X = 78;
const MARGIN_Y = 96;
/** 有标题时主区往下让出的高度 */
const TITLE_BAND = 168;

export interface LayoutOptions {
  canvas?: { w: number; h: number };
  /** 0 = 完全不重叠（默认）；1 = 上装衣摆最多压住下装 12% */
  wearOverlap?: number;
  /** 画板上是否有标题文字，有的话上方留白 */
  hasTitle?: boolean;
}

interface Slotted {
  asset: Asset;
  aspect: number;
  weight: number;
  order: number;
}

function prepare(asset: Asset): { aspect: number } {
  const w = asset.bitmap?.w || 1;
  const h = asset.bitmap?.h || 1;
  return { aspect: w / h };
}

function packColumn(
  entries: Slotted[],
  colX: number,
  colW: number,
  top: number,
  bottom: number,
): { asset: Asset; x: number; y: number; w: number; h: number }[] {
  if (entries.length === 0) return [];
  const avail = bottom - top - GAP * (entries.length - 1);
  const totalWeight = entries.reduce((s, e) => s + e.weight, 0);
  const unit = avail / totalWeight;

  // 先按权重给高度，再对「按这个高度会超出列宽」的件按列宽反算
  const sized = entries.map((e) => {
    let h = e.weight * unit;
    let w = h * e.aspect;
    if (w > colW) {
      w = colW;
      h = w / e.aspect;
    }
    return { ...e, w, h };
  });

  // 反算之后总高可能变矮（留白）也可能仍偏高（极端窄图），统一再缩放一次。
  // 偏矮时也放大 —— 一列只有两三件时不撑开会留一大块空白，拼贴看着像没排完。
  // 放大受列宽约束：任何一件都不许被撑出列外，否则两列会互相咬。
  const totalH = sized.reduce((s, e) => s + e.h, 0) + GAP * (sized.length - 1);
  const room = bottom - top;
  const widthCap = Math.min(...sized.map((e) => colW / e.w));
  const k = totalH > room
    ? room / totalH
    : Math.min(room / totalH, widthCap, 1.35);

  const scaled = sized.map((e) => ({ ...e, w: e.w * k, h: e.h * k }));
  const finalH = scaled.reduce((s, e) => s + e.h, 0) + GAP * (scaled.length - 1) * k;
  let y = top + Math.max(0, (room - finalH) / 2);

  return scaled.map((e) => {
    const cy = y + e.h / 2;
    y += e.h + GAP * k;
    return { asset: e.asset, x: colX + colW / 2, y: cy, w: e.w, h: e.h };
  });
}

/**
 * 给一组素材算出互不重叠的摆位。
 *
 * 返回的是「素材 id → 位置」，调用方负责把它合并进已有的 BoardItem
 * （保留 id / z / 旋转这些用户改过的东西是调用方的事，这里只管几何）。
 */
export function autoLayout(
  assets: Asset[],
  opts: LayoutOptions = {},
): Map<string, { x: number; y: number; w: number; h: number }> {
  const canvas = opts.canvas ?? BOARD_CANVAS;
  const out = new Map<string, { x: number; y: number; w: number; h: number }>();
  if (assets.length === 0) return out;

  const main: Slotted[] = [];
  const side: Slotted[] = [];
  for (const asset of assets) {
    const { aspect } = prepare(asset);
    const cat = asset.category;
    const weight = HEIGHT_WEIGHT[cat] ?? 0.7;
    if (MAIN_ORDER[cat] !== undefined) {
      main.push({ asset, aspect, weight, order: MAIN_ORDER[cat]! });
    } else {
      side.push({ asset, aspect, weight, order: SIDE_ORDER[cat] ?? 70 });
    }
  }
  main.sort((a, b) => a.order - b.order || a.asset.name.localeCompare(b.asset.name, 'zh'));
  side.sort((a, b) => a.order - b.order || a.asset.name.localeCompare(b.asset.name, 'zh'));

  const top = MARGIN_Y + (opts.hasTitle ? TITLE_BAND : 0);
  const bottom = canvas.h - MARGIN_Y;
  const usableW = canvas.w - MARGIN_X * 2;

  // 只有主列时让它吃满宽度；两列时主列 60%、副列 32%，中间留 8% 的沟
  const twoCol = side.length > 0 && main.length > 0;
  const mainW = twoCol ? usableW * 0.58 : usableW;
  const sideW = twoCol ? usableW * 0.32 : usableW;
  const mainX = MARGIN_X;
  const sideX = twoCol ? MARGIN_X + usableW - sideW : MARGIN_X;

  const placedMain = packColumn(main, mainX, mainW, top, bottom);
  const placedSide = packColumn(side, twoCol ? sideX : mainX, sideW, top, bottom);

  for (const p of [...placedMain, ...placedSide]) {
    out.set(p.asset.id, { x: p.x, y: p.y, w: p.w, h: p.h });
  }

  // 可选的「穿着感」：只让上装往下压住下装一点点，且只压 12% 以内
  const k = Math.max(0, Math.min(1, opts.wearOverlap ?? 0));
  if (k > 0 && placedMain.length > 1) {
    for (let i = 1; i < placedMain.length; i++) {
      const prev = placedMain[i - 1];
      const cur = placedMain[i];
      const topish = prev.asset.category === 'top' || prev.asset.category === 'outer' ||
        prev.asset.category === 'underlayer';
      const bottomish = cur.asset.category === 'bottom' || cur.asset.category === 'legwear';
      if (!topish || !bottomish) continue;
      const shift = Math.min(prev.h, cur.h) * 0.12 * k + GAP * k;
      for (let j = i; j < placedMain.length; j++) {
        const p = placedMain[j];
        const cell = out.get(p.asset.id)!;
        out.set(p.asset.id, { ...cell, y: cell.y - shift });
      }
      break;
    }
  }

  return out;
}

/**
 * 兜底：真出现相交就把靠后那件沿 y 推开。
 *
 * 自动布局本身不会产生相交，这条是给「手动摆过之后点整理」和
 * 「素材尺寸极端」两种情况留的保险，宁可留白也不允许交付一张糊在一起的图。
 */
export function separate(items: BoardItem[], canvas = BOARD_CANVAS): BoardItem[] {
  const next = items.map((i) => ({ ...i }));
  const byId = new Map(next.map((i) => [i.id, i]));
  for (let pass = 0; pass < 8; pass++) {
    const clashes = findOverlaps(next);
    if (clashes.length === 0) break;
    for (const [aId, bId] of clashes) {
      const a = byId.get(aId)!;
      const b = byId.get(bId)!;
      const A = itemBounds(a);
      const B = itemBounds(b);
      const dy = Math.min(A.y2, B.y2) - Math.max(A.y1, B.y1);
      const dx = Math.min(A.x2, B.x2) - Math.max(A.x1, B.x1);
      const lower = a.y <= b.y ? b : a;
      const upper = lower === a ? b : a;
      if (dy <= dx) {
        lower.y += dy / 2 + 6;
        upper.y -= dy / 2 + 6;
      } else {
        const right = a.x <= b.x ? b : a;
        const left = right === a ? b : a;
        right.x += dx / 2 + 6;
        left.x -= dx / 2 + 6;
      }
    }
  }
  // 推出画布的拉回来
  for (const it of next) {
    const b = itemBounds(it);
    if (b.x1 < 8) it.x += 8 - b.x1;
    if (b.x2 > canvas.w - 8) it.x -= b.x2 - (canvas.w - 8);
    if (b.y1 < 8) it.y += 8 - b.y1;
    if (b.y2 > canvas.h - 8) it.y -= b.y2 - (canvas.h - 8);
  }
  return next;
}
