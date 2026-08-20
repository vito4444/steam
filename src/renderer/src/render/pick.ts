/**
 * 命中测试：鼠标点在模特身上时，点中的是哪一件。
 *
 * 手动微调（拖一件衣服换位置）必须先知道拖的是哪件。按包围盒判会很难用 ——
 * 一件裙子的包围盒盖住半个画布，点哪都是它。所以按 **alpha** 判：只有真的
 * 点在不透明像素上才算命中，从 z 高的往下找，第一个命中的就是答案。
 *
 * 每张素材只需要一张很小的 alpha 网格（长边 320），拖动时不会去读原图。
 */

import { loadImage } from './images';
import type { StageScene } from './types';

interface AlphaGrid {
  w: number;
  h: number;
  a: Uint8ClampedArray;
  /** 不透明区里 alpha 的上限（99.5 分位）。抠图欠实的素材这个值明显低于 255 */
  ceiling: number;
}

const cache = new Map<string, Promise<AlphaGrid | null>>();
const MAX_SIDE = 320;

function grid(url: string): Promise<AlphaGrid | null> {
  let p = cache.get(url);
  if (!p) {
    p = build(url).catch(() => null);
    cache.set(url, p);
  }
  return p;
}

async function build(url: string): Promise<AlphaGrid | null> {
  const img = await loadImage(url);
  const iw = img.naturalWidth;
  const ih = img.naturalHeight;
  if (!iw || !ih) return null;
  const k = Math.min(MAX_SIDE / Math.max(iw, ih), 1);
  const w = Math.max(Math.round(iw * k), 1);
  const h = Math.max(Math.round(ih * k), 1);
  const c = document.createElement('canvas');
  c.width = w;
  c.height = h;
  const ctx = c.getContext('2d', { willReadFrequently: true });
  if (!ctx) return null;
  ctx.drawImage(img, 0, 0, w, h);
  const data = ctx.getImageData(0, 0, w, h).data;
  const a = new Uint8ClampedArray(w * h);
  for (let i = 0; i < w * h; i++) a[i] = data[i * 4 + 3];

  // alpha 上限：取不透明像素里的 99.5 分位，避开个别尖峰
  const hist = new Int32Array(256);
  let n = 0;
  for (let i = 0; i < a.length; i++) {
    if (a[i] > 16) {
      hist[a[i]]++;
      n++;
    }
  }
  let ceiling = 255;
  if (n > 0) {
    let acc = 0;
    for (let v = 255; v >= 0; v--) {
      acc += hist[v];
      if (acc >= n * 0.005) {
        ceiling = v;
        break;
      }
    }
  }
  return { w, h, a, ceiling };
}

/**
 * 这张素材的 alpha 上限。
 *
 * 用途：CERE-10 有几件抠图是**整件半透明**的（`soft_matte_edges`，全图没有
 * 一个 alpha=255 的像素），直接贴上去就是一件能看见身体的衬衫。根因在素材
 * 侧（质检门槛是 CERE-12 的活），但渲染层不能装作没看见 —— 拿这个值做一次
 * 保守的不透明度补偿，见 `canvas2d.ts` 的 `buildSprite`。
 */
export async function alphaCeiling(url: string): Promise<number> {
  const g = await grid(url);
  return g?.ceiling ?? 255;
}

/**
 * 找出显示坐标 (x, y) 上最靠前的衣物层，返回它的 key（= assetId）。
 *
 * 注意：这里只认区间守卫，不认身体遮罩与成对挖除 —— 那两步要等合成器画完
 * 才知道结果。代价是被裁掉的一小圈边缘仍然可点中，换来的是拖动时零延迟。
 */
export async function hitTest(scene: StageScene, x: number, y: number): Promise<string | null> {
  const layers = scene.layers
    .filter((l) => l.kind === 'garment')
    .sort((a, b) => b.z - a.z);

  for (const l of layers) {
    if (x < l.dx || x > l.dx + l.dw || y < l.dy || y > l.dy + l.dh) continue;
    const clip = l.clip;
    if (clip) {
      if (clip.keepFrom !== undefined && y < clip.keepFrom) continue;
      if (clip.keepTo !== undefined && y > clip.keepTo) continue;
    }
    const g = await grid(l.url);
    if (!g) continue;
    const u = Math.min(Math.floor(((x - l.dx) / l.dw) * g.w), g.w - 1);
    const v = Math.min(Math.floor(((y - l.dy) / l.dh) * g.h), g.h - 1);
    if (g.a[v * g.w + u] > 24) return l.key;
  }
  return null;
}
