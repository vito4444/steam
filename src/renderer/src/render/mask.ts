/**
 * 身体遮罩（body mask）。
 *
 * 用途只有一个：把衣物超出身体轮廓的部分裁掉，别让袖子飘在体侧、下摆悬在空中。
 *
 * 遮罩从哪来，按优先级：
 *   1. 底图包 manifest 的 `mask` 字段 —— CERE-13 会随贴身底图一起给（格式见
 *      `docs/occlusion-rules.md` §5）。给了就用它，它比第 2 条准。
 *   2. 底图人体层自己的 alpha —— 底图本来就是透明底的人形，alpha 就是轮廓。
 *      现成能用，所以 CERE-13 没到位之前遮挡链路照样跑得通。
 *
 * 每个槽位允许超出轮廓多少不一样（宽松外套 vs 贴身内层），所以遮罩要按
 * 「外扩量」分别烘一张。外扩用 12 方向偏移画一圈实现 —— 形态学膨胀的廉价
 * 近似，比高斯模糊准（模糊会让轮廓一起变虚，裁出来的边是糊的）。
 */

import type { BaseBodySet } from '@shared/types';

import { loadImage } from './images';

/** 一张已经外扩好的遮罩，画布坐标系与底图画布一致 */
export interface BodyMask {
  canvas: HTMLCanvasElement;
  /** 遮罩位图的像素尺寸（可能与底图画布不同） */
  w: number;
  h: number;
  /** true = 用的是底图 alpha 兜底，不是底图包给的正式遮罩 */
  derived: boolean;
}

const cache = new Map<string, Promise<BodyMask | null>>();

function maskUrl(base: BaseBodySet, toneIndex: number): { url: string; derived: boolean } | null {
  if (base.mask?.url) return { url: base.mask.url, derived: false };
  const tone = base.tones[Math.min(Math.max(toneIndex, 0), Math.max(base.tones.length - 1, 0))];
  const url = tone?.layers[0]?.url ?? base.layers[0]?.url;
  return url ? { url, derived: true } : null;
}

/**
 * 取一张外扩 `growCanvas`（**画布坐标**单位）的身体遮罩。
 *
 * 外扩量用画布坐标而不是遮罩位图像素：遮罩位图尺寸可能和底图画布不一致，
 * 换算放在这里做一次，调用方只管说「外扩 0.1 个肩宽」。
 *
 * 读不到底图像素时返回 null，调用方要能接受「这次不裁」而不是崩掉。
 */
export function bodyMask(
  base: BaseBodySet,
  toneIndex: number,
  growCanvas: number,
  canvasW: number,
): Promise<BodyMask | null> {
  const src = maskUrl(base, toneIndex);
  if (!src) return Promise.resolve(null);
  // 量化到 1 画布单位，避免连续微调时每帧都烘一张新遮罩
  const grow = Math.max(Math.round(growCanvas), 0);
  const key = `${base.pack}|${base.body}|${src.url}|${grow}|${Math.round(canvasW)}`;
  let p = cache.get(key);
  if (!p) {
    p = build(src.url, src.derived, grow, canvasW).catch(() => null);
    cache.set(key, p);
  }
  return p;
}

async function build(
  url: string,
  derived: boolean,
  growCanvas: number,
  canvasW: number,
): Promise<BodyMask | null> {
  const img = await loadImage(url);
  const w = img.naturalWidth;
  const h = img.naturalHeight;
  if (!w || !h) return null;
  const grow = Math.round(growCanvas * (w / Math.max(canvasW, 1)));

  const c = document.createElement('canvas');
  c.width = w;
  c.height = h;
  const ctx = c.getContext('2d');
  if (!ctx) return null;

  if (grow <= 0) {
    ctx.drawImage(img, 0, 0);
    return { canvas: c, w, h, derived };
  }

  // 12 方向 + 两圈半径，把轮廓向外推 grow 像素。方向少了会出十二边形的棱角，
  // 两圈是为了填掉大半径下相邻方向之间的缝。
  const dirs = 12;
  for (const r of [grow * 0.55, grow]) {
    for (let i = 0; i < dirs; i++) {
      const a = (i / dirs) * Math.PI * 2;
      ctx.drawImage(img, Math.cos(a) * r, Math.sin(a) * r);
    }
  }
  ctx.drawImage(img, 0, 0);

  // 外扩后 alpha 是叠出来的，边缘还带原图的半透明过渡；这里不做二值化，
  // 半透明的那一圈正好当作裁切的软过渡。
  return { canvas: c, w, h, derived };
}

export function clearMaskCache(): void {
  cache.clear();
}
