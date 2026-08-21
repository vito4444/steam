import { DEFAULT_OCCLUSION } from '@shared/occlusion';
import { SLOT_Z, type Slot } from '@shared/spec';

import { placeGarment, normalizeFit } from './fit';
import { bodyMask } from './mask';
import type { BaseMetrics } from './metrics';
import { resolveOcclusion, type LayerOcclusion, type WornRef } from './occlusion';
import type { RenderInput, StageLayer, StageScene } from './types';

const MAX_ITEM_Z_OFFSET = 4;

/**
 * 把「底图 + 一叠衣物」摊成渲染器直接能画的层列表。
 *
 * 这里不做任何风格化：不描边、不量化、不加卡通阴影。真实照片的纹理是
 * 唯一的真实感来源，任何后处理都是在削弱它。只做四件让贴图落到身上的事：
 *   - 遮挡：按规则表算出谁盖谁、哪一段被挖掉
 *   - 裁切：超出身体轮廓的部分按身体遮罩削掉，别让衣服飘在空中
 *   - 羽化：抠图边缘轻微软化，挡住硬白边
 *   - 接触阴影：衣物压在下层上的那圈投影
 * 四件都只作用于 alpha 与交界处，不碰衣物本身的像素颜色。
 */
export function buildScene(input: RenderInput, m: BaseMetrics, displayScale: number): StageScene {
  const k = displayScale;
  const canvas = m.canvas;
  const layers: StageLayer[] = [];
  const raw = !!input.rawCompositing;
  const noOcc = !!input.noOcclusion;

  // 羽化与阴影的尺度跟着人体走，不跟着画布像素走
  const unit = m.shoulderW * k;
  const featherPx = raw ? 0 : Math.max(0.6, unit * 0.006);

  const push = (l: StageLayer) => layers.push({
    ...l,
    dx: l.dx * k, dy: l.dy * k, dw: l.dw * k, dh: l.dh * k,
  });

  const baseLayer = (
    key: string,
    l: { file: string; url: string; z: number; mode: 'fill' | 'multiply' | 'normal'; tint?: 'skin' | 'hair' },
  ): StageLayer => ({
    key,
    url: l.url,
    z: l.z,
    mode: l.mode,
    tint: l.tint === 'hair' ? input.hairHex : undefined,
    opacity: 1,
    kind: 'base',
    feather: 0,
    dx: 0, dy: 0, dw: canvas.w, dh: canvas.h,
  });

  const hair = input.base.hair?.[input.hairStyle] ?? input.base.hair?.['h01'];
  for (const l of hair?.back ?? []) push(baseLayer(`hair_back_${l.file}`, l));

  const tone = input.base.tones[Math.min(input.tone, Math.max(input.base.tones.length - 1, 0))];
  for (const l of tone?.layers ?? []) push(baseLayer(`tone_${l.file}`, l));
  for (const l of input.base.layers) push(baseLayer(`base_${l.file}`, l));

  // 遮挡只看「真的画出来的」那些件：隐藏的层不参与，否则一件被临时关掉的
  // 外套还在挖里面的上装，界面上看是凭空缺一块。
  const visible = input.worn.filter((w) => !w.hidden);
  const refs: WornRef[] = visible.map((w) => ({ key: w.asset.id, slot: w.slot, tuck: w.tuck }));
  const config = input.occlusion ?? DEFAULT_OCCLUSION;
  const occ: Map<string, LayerOcclusion> = noOcc
    ? new Map()
    : resolveOcclusion(refs, m, config);

  for (const worn of visible) {
    const fit = normalizeFit(worn.asset, worn.fit);
    const p = placeGarment(worn.asset, worn.slot, m, fit);
    const o = occ.get(worn.asset.id);
    // Callers historically sent SLOT_Z + item override. Recover only that bounded
    // per-item delta; structural order always comes from the active rule table.
    const callerZ = Number.isFinite(worn.z) ? worn.z : SLOT_Z[worn.slot];
    const itemOffset = clamp(callerZ - SLOT_Z[worn.slot], -MAX_ITEM_Z_OFFSET, MAX_ITEM_Z_OFFSET);

    push({
      key: worn.asset.id,
      url: worn.asset.cutoutUrl,
      z: structuralZ(config.order, worn.slot, itemOffset),
      mode: 'normal',
      opacity: 1,
      kind: 'garment',
      feather: featherPx,
      contact: raw ? undefined : contactShadow(worn.slot, unit),
      highlight: worn.highlight,
      dx: p.x, dy: p.y, dw: p.w, dh: p.h,
      // clip 里的 y 与长度是显示像素，画布坐标统一在这里乘一次 k
      clip: o && {
        keepFrom: o.keepFrom === undefined ? undefined : o.keepFrom * k,
        keepTo: o.keepTo === undefined ? undefined : o.keepTo * k,
        keepFeather: o.keepFeather * k,
        mask: o.mask && {
          growCanvas: o.mask.grow,
          from: o.mask.from === undefined ? undefined : o.mask.from * k,
          to: o.mask.to === undefined ? undefined : o.mask.to * k,
          feather: o.mask.feather * k,
        },
        erasedBy: o.erasedBy.map((e) => ({
          key: e.key,
          from: e.from === undefined ? undefined : e.from * k,
          to: e.to === undefined ? undefined : e.to * k,
          grow: e.grow * k,
          feather: e.feather * k,
        })),
      },
    });
  }

  for (const l of hair?.front ?? []) push(baseLayer(`hair_front_${l.file}`, l));

  return {
    width: canvas.w * k,
    height: canvas.h * k,
    background: input.background,
    backgroundColor: input.backgroundColor,
    groundShadow: true,
    layers,
    bodyMask: (grow: number) => bodyMask(input.base, input.tone, grow, canvas.w),
  };
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), max);
}

/** Keep item tweaks inside non-overlapping midpoint bands between structural slots. */
function structuralZ(order: Record<Slot, number>, slot: Slot, itemOffset: number): number {
  const configured = order[slot];
  const base = Number.isFinite(configured) ? configured : SLOT_Z[slot];
  const values = Object.entries(order)
    .map(([candidate, value]) => Number.isFinite(value) ? value : SLOT_Z[candidate as Slot])
    .filter((value, index, all) => all.indexOf(value) === index)
    .sort((a, b) => a - b);
  const lower = values.filter((value) => value < base).at(-1);
  const upper = values.find((value) => value > base);
  const finiteGaps = [lower === undefined ? Infinity : base - lower, upper === undefined ? Infinity : upper - base]
    .filter(Number.isFinite);
  const epsilon = finiteGaps.length ? Math.min(0.001, Math.min(...finiteGaps) / 4) : 0.001;
  const min = lower === undefined ? -Infinity : (lower + base) / 2 + epsilon;
  const max = upper === undefined ? Infinity : (base + upper) / 2 - epsilon;
  return clamp(base + itemOffset, min, max);
}

/**
 * 接触阴影的强度按件的贴身程度分档：外套离身体远、投影散且淡，
 * 鞋踩在地面上、投影紧且实。这是「贴纸感」和「穿在身上」之间
 * 最省成本的一道分界。
 */
function contactShadow(slot: Slot, unit: number): { blur: number; offset: number; alpha: number } {
  switch (slot) {
    case 'shoe_base':
    case 'shoe_shaft':
      return { blur: unit * 0.045, offset: unit * 0.014, alpha: 0.42 };
    case 'headwear':
    case 'eyewear':
    case 'hair_back':
    case 'hair_front':
      return { blur: unit * 0.06, offset: unit * 0.022, alpha: 0.34 };
    case 'bottom':
    case 'belt':
      return { blur: unit * 0.05, offset: unit * 0.016, alpha: 0.4 };
    default:
      return { blur: unit * 0.07, offset: unit * 0.024, alpha: 0.38 };
  }
}
