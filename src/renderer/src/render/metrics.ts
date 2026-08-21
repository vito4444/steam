/**
 * 底图轮廓实测。
 *
 * 真实照片素材的像素尺寸毫无规律，唯一能把它缩放到「穿得上」的依据是
 * 身体本身有多宽。锚点只给点、不给宽度，所以这里直接量底图 alpha 轮廓：
 * 在锚点所在的行上找出包含身体中线的那段连续不透明像素，它的长度就是
 * 那一段的身体宽度。
 *
 * 手臂在腰胯高度是与躯干分开的独立段，按「取中线所在段」的规则会被自动
 * 排除；肩线高度手臂与躯干相连，量到的就是含肩的肩宽 —— 正是想要的。
 */

import {
  BASE_ANCHORS, WIDTH_PROBE, type AnchorName, type Point, type WidthRef,
} from '@shared/spec';
import type { BaseBodySet } from '@shared/types';

import { loadImage } from './images';

export interface BaseMetrics {
  canvas: { w: number; h: number };
  anchors: Record<AnchorName, Point>;
  /** 肩宽：所有与画布尺寸无关的偏移都以它为单位 */
  shoulderW: number;
  width: Record<WidthRef, number>;
  /** 实测失败（底图读不出像素）时为 true，此时宽度是按锚点估的 */
  estimated: boolean;
}

const cache = new Map<string, Promise<BaseMetrics>>();

export function baseMetrics(base: BaseBodySet, toneIndex: number): Promise<BaseMetrics> {
  const url = bodyLayerUrl(base, toneIndex);
  const key = `${base.pack}|${base.body}|${url}`;
  let p = cache.get(key);
  if (!p) {
    p = measure(base, url);
    cache.set(key, p);
  }
  return p;
}

/** 量轮廓用的那一层：肤色档位的第一层（人体本体），退而求其次用公共层 */
function bodyLayerUrl(base: BaseBodySet, toneIndex: number): string {
  const tone = base.tones[Math.min(Math.max(toneIndex, 0), Math.max(base.tones.length - 1, 0))];
  return tone?.layers[0]?.url ?? base.layers[0]?.url ?? '';
}

function anchorTable(base: BaseBodySet): Record<AnchorName, Point> {
  const out = { ...BASE_ANCHORS };
  const scaleX = base.canvas.w / 1152;
  const scaleY = base.canvas.h / 2304;
  for (const name of Object.keys(out) as AnchorName[]) {
    const given = base.anchors?.[name];
    // 底图包没给这个锚点时，按画布尺寸等比换算回落值，而不是直接用绝对坐标
    out[name] = given ?? { x: BASE_ANCHORS[name].x * scaleX, y: BASE_ANCHORS[name].y * scaleY };
  }
  return out;
}

async function measure(base: BaseBodySet, url: string): Promise<BaseMetrics> {
  const anchors = anchorTable(base);
  const shoulderW = Math.abs(anchors.shoulder_r.x - anchors.shoulder_l.x);
  const fallback = estimateWidths(anchors, shoulderW);

  const img = url ? await loadImage(url).catch(() => null) : null;
  if (!img) {
    return { canvas: base.canvas, anchors, shoulderW, width: fallback, estimated: true };
  }

  const w = img.naturalWidth || base.canvas.w;
  const h = img.naturalHeight || base.canvas.h;
  const c = document.createElement('canvas');
  c.width = w;
  c.height = h;
  const ctx = c.getContext('2d', { willReadFrequently: true });
  if (!ctx) return { canvas: base.canvas, anchors, shoulderW, width: fallback, estimated: true };
  ctx.drawImage(img, 0, 0, w, h);

  // 底图位图尺寸可能与 manifest 声明的画布不同，按比例换算取样坐标
  const kx = w / base.canvas.w;
  const ky = h / base.canvas.h;

  let data: Uint8ClampedArray;
  try {
    data = ctx.getImageData(0, 0, w, h).data;
  } catch {
    return { canvas: base.canvas, anchors, shoulderW, width: fallback, estimated: true };
  }

  const runsAt = (y: number): [number, number][] => {
    const row = Math.min(Math.max(Math.round(y), 0), h - 1);
    const out: [number, number][] = [];
    let start = -1;
    for (let x = 0; x < w; x++) {
      const a = data[(row * w + x) * 4 + 3];
      if (a > 48) {
        if (start < 0) start = x;
      } else if (start >= 0) {
        out.push([start, x - 1]);
        start = -1;
      }
    }
    if (start >= 0) out.push([start, w - 1]);
    return out.filter(([a, b]) => b - a > 2);
  };

  const width: Record<string, number> = {};
  let estimated = false;

  for (const ref of Object.keys(WIDTH_PROBE) as WidthRef[]) {
    const probe = WIDTH_PROBE[ref];
    const anchor = anchors[probe.anchor];
    const y = (anchor.y + (probe.offsetK ?? 0) * shoulderW) * ky;
    const runs = runsAt(y);
    if (runs.length === 0) {
      width[ref] = fallback[ref];
      estimated = true;
      continue;
    }
    if (ref === 'ankle') {
      // 踝高度两条腿是分开的两段，这里要的是**双脚站距**（最左到最右的整幅），
      // 因为用它的只有鞋 —— 一张鞋素材是两只鞋并排，按单只脚踝缩会小掉一半。
      width[ref] = (runs[runs.length - 1][1] - runs[0][0] + 1) / kx;
      continue;
    }
    const cx = anchor.x * kx;
    const central = runs.find(([a, b]) => cx >= a - 2 && cx <= b + 2);
    if (central) {
      width[ref] = (central[1] - central[0] + 1) / kx;
    } else {
      // 中线落在空隙里（例如两腿之间），退回全幅跨度
      width[ref] = (runs[runs.length - 1][1] - runs[0][0] + 1) / kx;
    }
  }

  return {
    canvas: base.canvas,
    anchors,
    shoulderW,
    width: width as Record<WidthRef, number>,
    estimated,
  };
}

/** 读不出像素时的兜底：全部按肩宽的经验比例推 */
function estimateWidths(anchors: Record<AnchorName, Point>, shoulderW: number): Record<WidthRef, number> {
  return {
    head: shoulderW * 0.62,
    shoulder: shoulderW,
    chest: shoulderW * 0.86,
    waist: shoulderW * 0.74,
    hip: shoulderW * 0.95,
    thigh: shoulderW * 0.52,
    knee: shoulderW * 0.34,
    // 双脚站距的兜底：两踝中心距再往外各放半只脚
    ankle: Math.abs(anchors.ankle_r.x - anchors.ankle_l.x) * 1.35 || shoulderW * 0.55,
  };
}
