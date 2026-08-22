/**
 * 自动贴合：把一张真实衣物照片放到模特身上该有的位置和大小。
 *
 * 位置优先按**可穿戴语义**对齐：上装肩点对肩点、下装腰点落腰线、鞋材质跨度
 * 覆盖踝线重叠区到脚底、围巾第一材质行留出下巴净空。语义点缺失或未通过可信度
 * 校验时，才退回素材 anchor / 边缘与槽位锚点的几何关系。
 *
 * 大小要难得多，因为真实照片的像素尺寸毫无规律，而且**包围盒宽度的语义
 * 随拍法而变**：
 *   摊平拍的毛衣  包围盒宽度 = 袖展，比肩宽大一倍多
 *   挂着拍的卫衣  包围盒宽度 = 半个胸宽
 *   手提包        landmark 量到的「肩线」其实是提手，只有包身的三分之一
 * 所以先按可穿戴语义分别解横纵轴；缺语义点时再用 CERE-14 的两路夹取回落：
 *
 *   按宽度  身体那一段的实测宽度 × 版型宽松量 ÷ 素材参照宽度
 *   按衣长  身上那一段的长度 ÷ 素材的上沿→下摆
 *
 * 上装 / 外套 / 连衣裙以衣长为主、被宽度夹住；下装 / 鞋 / 配饰以宽度为主。
 * 两路都错的时候（侧身拍、褶皱堆叠）自动解就是不准 —— 这是这条路线的上限，
 * 所以画布上的手动微调必须做实，见 `docs/occlusion-rules.md` §7。
 */

import {
  SLOT_PLACEMENT, type AnchorName, type Slot,
} from '@shared/spec';
import type { Asset, Fit, GarmentLandmark } from '@shared/types';

import type { BaseMetrics } from './metrics';

export interface Placement {
  x: number;
  y: number;
  w: number;
  h: number;
  /** 素材坐标到画布坐标的最终横向比例（含用户 scale 与 stretch_x） */
  scaleX: number;
  /** 素材坐标到画布坐标的最终纵向比例（含用户 scale，不含 stretch_x） */
  scaleY: number;
  /** 自动解出的缩放，界面上要能显示出来 */
  autoScale: number;
  /** 是否用上了素材包**明确给出**的 landmarks（自己量的不算） */
  precise: boolean;
}

/**
 * 按衣长解出来的缩放，不许离按宽度解出来的太远。
 *
 * 单靠衣长，一件挂着拍、下摆垂到画面外的大衣会被放大到离谱；单靠宽度，
 * 摊平的毛衣会缩成胸口一小块。互相夹一道之后，两种失败都退化成「偏了一点」
 * 而不是「完全不能看」。
 */
const WIDTH_GUARD: [number, number] = [0.7, 1.45];

/** 成品宽度的兜底上限：再怎么错也不许比肩宽宽出这么多倍 */
const MAX_WIDTH_K = 2.4;

/**
 * `attributes.length` 给了衣长语义时，衣长该落到身上哪个位置。
 * 素材没声明就用槽位规则里的 `lengthSpan` 默认值。
 */
const LENGTH_ANCHOR: Record<string, [AnchorName, AnchorName, number]> = {
  crop:  ['chest', 'chest', 0],
  waist: ['waist', 'waist', 0],
  hip:   ['hip', 'hip', 0],
  thigh: ['crotch', 'knee', 0.35],
  knee:  ['knee', 'knee', 0],
  midi:  ['knee', 'ankle_l', 0.55],
  ankle: ['ankle_l', 'ankle_l', 0],
  floor: ['foot_base', 'foot_base', 0],
};

/** User-photo outerwear stops around the hip unless the asset declares a length. */
const PHOTO_LENGTH_SPAN: Partial<Record<Slot, [AnchorName, AnchorName, number]>> = {
  outer: ['shoulder_line', 'hip', 0.1],
};

/** 只有素材包**明确给出**的单点才可信。成对点另走 `plausiblePair` 的语义校验。 */
function given(asset: Asset, key: GarmentLandmark): { x: number; y: number } | undefined {
  return asset.landmarks_given?.includes(key) ? asset.landmarks?.[key] : undefined;
}

interface LandmarkPair {
  left: { x: number; y: number };
  right: { x: number; y: number };
  explicit: boolean;
}

/**
 * 成对 landmark 即使来自导入器实测，只要跨度、倾斜和坐标都像一条真实衣物边，
 * 也比全图包围盒更有语义。配饰等未声明 `alignPair` 的槽位仍不会采信自动量点。
 */
function plausiblePair(asset: Asset, pair: 'shoulder' | 'waist'): LandmarkPair | undefined {
  const leftKey: GarmentLandmark = `${pair}_l`;
  const rightKey: GarmentLandmark = `${pair}_r`;
  const left = asset.landmarks?.[leftKey];
  const right = asset.landmarks?.[rightKey];
  if (!left || !right) return undefined;

  const finite = [left.x, left.y, right.x, right.y].every(Number.isFinite);
  const inBounds = left.x >= 0 && left.x <= asset.bitmap.w
    && right.x >= 0 && right.x <= asset.bitmap.w
    && left.y >= 0 && left.y <= asset.bitmap.h
    && right.y >= 0 && right.y <= asset.bitmap.h;
  const width = right.x - left.x;
  const level = Math.abs(right.y - left.y) <= Math.max(8, asset.bitmap.h * 0.2);
  if (!finite || !inBounds || width <= 4 || width <= asset.bitmap.w * 0.25 || !level) return undefined;

  return {
    left,
    right,
    explicit: asset.landmarks_given?.includes(leftKey) === true
      && asset.landmarks_given?.includes(rightKey) === true,
  };
}

export function placeGarment(
  asset: Asset,
  slot: Slot,
  m: BaseMetrics,
  fit: Fit,
): Placement {
  const baseRule = SLOT_PLACEMENT[slot] ?? SLOT_PLACEMENT.top;
  // A photographed handbag has a measured handle and belongs in the hand.
  // Built-in bags include backpacks and retain their established hip-side mount.
  const rule = asset.source.origin === 'photo' && slot === 'bag'
    ? {
      ...baseRule,
      anchor: 'wrist_r' as const,
      singleAnchor: true,
      edge: 'top' as const,
      attachLandmark: 'top_edge' as const,
      offsetXK: undefined,
    }
    : baseRule;
  const bmp = asset.bitmap;
  const lm = asset.landmarks;

  // ---- 1. 按宽度：身体宽度 ÷ 素材参照宽度
  const targetW = (m.width[rule.widthRef] ?? m.shoulderW) * rule.widthK;
  const measuredPair = rule.alignPair ? plausiblePair(asset, rule.alignPair) : undefined;
  // A row inferred from an arbitrary user photo is not a shoulder/waist semantic.
  // CERE-53's jacket is 382 px wide, but its inferred shoulder row is only 195 px;
  // using that row nearly doubles the rendered jacket. The already-cropped alpha
  // subject bounds are the stable reference until a pair is explicitly provided.
  const semanticPair = asset.source.origin === 'photo' && !measuredPair?.explicit
    ? undefined
    : measuredPair;
  // 声明了语义 pair 的槽位只有两种状态：整对通过校验，或整对完全不用。
  // 不能让被拒绝的点绕回旧逻辑继续影响宽度或中心。
  const legacyPair = !rule.alignPair
    ? rule.widthRef === 'waist' || rule.widthRef === 'hip'
      ? [given(asset, 'waist_l'), given(asset, 'waist_r')]
      : [given(asset, 'shoulder_l'), given(asset, 'shoulder_r')]
    : [undefined, undefined];

  let refW = bmp.w;
  let precise = false;
  if (semanticPair) {
    refW = semanticPair.right.x - semanticPair.left.x;
    precise = semanticPair.explicit;
  } else if (legacyPair[0] && legacyPair[1]) {
    const measured = Math.abs(legacyPair[1]!.x - legacyPair[0]!.x);
    // 量出来的跨度不该只有包围盒的一小条，否则多半踩到破洞或提手了
    if (measured > 4 && measured > bmp.w * 0.25) {
      refW = measured;
      precise = true;
    }
  }
  const byWidth = targetW / Math.max(refW, 1);

  // ---- 2. 按衣长：身上那一段 ÷ 素材上沿→下摆
  const attach = anchorPoint(rule.anchor, m, !rule.singleAnchor);
  const hemY = lm?.hem?.y ?? bmp.h;
  const topY = lm?.top_edge?.y ?? 0;
  const span = hemY - topY;
  const lengthSpec = LENGTH_ANCHOR[asset.attributes?.length ?? '']
    ?? (asset.source.origin === 'photo' ? PHOTO_LENGTH_SPAN[slot] : undefined)
    ?? rule.lengthSpan;

  let byLength: number | null = null;
  if (lengthSpec && span > 8) {
    const [a1, a2, extraK] = lengthSpec;
    const from = m.anchors[a1]?.y ?? attach.y;
    const to = (m.anchors[a2]?.y ?? attach.y) + extraK * m.shoulderW;
    const want = to - from;
    if (want > 0) byLength = want / span;
  }

  let autoY: number;
  if (rule.driver === 'length' && byLength !== null) {
    autoY = clamp(byLength, byWidth * WIDTH_GUARD[0], byWidth * WIDTH_GUARD[1]);
  } else if (byLength !== null && asset.attributes?.length) {
    // 按宽度驱动的槽位，素材若自己声明了衣长语义，也让衣长把缩放拉一把
    autoY = clamp(byLength, byWidth * 0.88, byWidth * 1.28);
  } else {
    autoY = byWidth;
  }

  if (rule.verticalSpan && hemY - topY > 8) {
    const [fromAnchor, fromOffsetK, toAnchor, toOffsetK] = rule.verticalSpan;
    const from = anchorPoint(fromAnchor, m).y + fromOffsetK * m.shoulderW;
    const to = anchorPoint(toAnchor, m).y + toOffsetK * m.shoulderW;
    if (to > from) autoY = (to - from) / (hemY - topY);
  }

  // 抠图带大片空白 / 比例异常时挡两道：宽度不许超过肩宽的 MAX_WIDTH_K 倍，
  // 高度不许超过画布 1.2 倍
  let autoX = byWidth;
  if (rule.alignPair === 'shoulder' && semanticPair) {
    autoX = Math.abs(m.anchors.shoulder_r.x - m.anchors.shoulder_l.x) / refW;
  }
  autoX = Math.min(autoX, (m.shoulderW * MAX_WIDTH_K) / Math.max(bmp.w, 1));
  autoY = Math.min(autoY, (m.canvas.h * 1.2) / Math.max(bmp.h, 1));

  const fitScale = fit.scale || 1;
  const stretch = clamp(fit.stretch_x || 1, 0.85, 1.15);
  const scaleX = autoX * fitScale * stretch;
  const scaleY = autoY * fitScale;

  const w = bmp.w * scaleX;
  const h = bmp.h * scaleY;

  // ---- 3. 位置：素材上的贴附点对到底图锚点
  let ax = bmp.w / 2;
  const gl = !rule.alignPair ? given(asset, 'shoulder_l') : undefined;
  const gr = !rule.alignPair ? given(asset, 'shoulder_r') : undefined;
  const wl = !rule.alignPair ? given(asset, 'waist_l') : undefined;
  const wr = !rule.alignPair ? given(asset, 'waist_r') : undefined;
  if (gl && gr) ax = (gl.x + gr.x) / 2;
  else if (wl && wr) ax = (wl.x + wr.x) / 2;
  else if (asset.schema_version >= 3) ax = asset.anchor.x;

  const attachLandmark = rule.attachLandmark ? lm?.[rule.attachLandmark] : undefined;
  if (attachLandmark) ax = attachLandmark.x;

  let ay: number;
  if (rule.edge === 'top') {
    ay = lm?.top_edge?.y ?? (gl && gr ? (gl.y + gr.y) / 2 : 0);
  } else if (rule.edge === 'bottom') {
    ay = lm?.hem?.y ?? bmp.h;
  } else {
    ay = bmp.h / 2;
  }
  if (attachLandmark) ay = attachLandmark.y;

  let targetX = attach.x + (rule.offsetXK ?? 0) * m.shoulderW;
  let targetY = attach.y + (rule.offsetK ?? 0) * m.shoulderW;
  if (semanticPair) {
    ax = (semanticPair.left.x + semanticPair.right.x) / 2;
    ay = (semanticPair.left.y + semanticPair.right.y) / 2;
    if (rule.alignPair === 'shoulder') {
      targetX = (m.anchors.shoulder_l.x + m.anchors.shoulder_r.x) / 2;
      targetY = (m.anchors.shoulder_l.y + m.anchors.shoulder_r.y) / 2;
    } else {
      targetX = m.anchors.waist.x;
      targetY = m.anchors.waist.y;
    }
  } else if (rule.verticalSpan) {
    const [fromAnchor, fromOffsetK] = rule.verticalSpan;
    ay = topY;
    targetY = anchorPoint(fromAnchor, m).y + fromOffsetK * m.shoulderW;
  }

  const unit = m.shoulderW;
  const x = targetX - ax * scaleX + (fit.dx || 0) * unit;
  const y = targetY - ay * scaleY + (fit.dy || 0) * unit;

  return { x, y, w, h, scaleX, scaleY, autoScale: autoY, precise };
}

/**
 * `xxx_l` / `xxx_r` 这类成对锚点取中点。
 * 一张鞋子素材里通常是**两只**鞋，对到单只脚踝上会整体偏一半。
 */
function anchorPoint(name: string, m: BaseMetrics, pairSides = true): { x: number; y: number } {
  const table = m.anchors as Record<string, { x: number; y: number }>;
  const self = table[name];
  const pair = pairSides && name.endsWith('_l') ? table[`${name.slice(0, -2)}_r`]
    : pairSides && name.endsWith('_r') ? table[`${name.slice(0, -2)}_l`]
      : null;
  if (self && pair) return { x: (self.x + pair.x) / 2, y: (self.y + pair.y) / 2 };
  return self ?? { x: m.canvas.w / 2, y: m.canvas.h / 2 };
}

function clamp(v: number, lo: number, hi: number): number {
  return Math.min(Math.max(v, lo), hi);
}

/**
 * schema_version 2 的素材里 `fit.scale` 存的是绝对缩放（照插画娃娃画布算的），
 * 在新的相对语义下必须归一化成 1，否则整衣橱错位。
 */
export function normalizeFit(asset: Asset, fit: Fit): Fit {
  if (asset.schema_version >= 3) return fit;
  return { ...fit, scale: 1 };
}
