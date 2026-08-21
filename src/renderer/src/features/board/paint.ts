/**
 * 画板的绘制层。
 *
 * 屏幕上的画板和导出的图片走**同一套背景绘制代码**（都画进 canvas），
 * 这样「所见即所得」不靠人肉对齐 CSS 与 canvas 两份实现。
 * 单品在屏幕上用 DOM 摆（拖拽 / 旋转手柄要的是 DOM 命中测试），
 * 导出时按同样的变换重画一遍。
 *
 * 噪点用固定种子的伪随机，否则每次重绘纹理都在抖。
 */

import type { Asset } from '@shared/types';
import {
  BOARD_FONT_STACK, boardBackground, itemBounds,
  type BoardBackgroundId, type BoardItem, type BoardLook,
} from '@shared/board';

function mulberry32(seed: number): () => number {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

export function paintBackground(
  ctx: CanvasRenderingContext2D,
  id: BoardBackgroundId,
  w: number,
  h: number,
): void {
  const bg = boardBackground(id);
  ctx.save();
  ctx.fillStyle = bg.base;
  ctx.fillRect(0, 0, w, h);

  if (bg.kind === 'gradient') {
    const g = ctx.createLinearGradient(0, 0, w * 0.4, h);
    g.addColorStop(0, bg.base);
    g.addColorStop(1, bg.detail);
    ctx.fillStyle = g;
    ctx.fillRect(0, 0, w, h);
  }

  if (bg.kind === 'paper') {
    const rnd = mulberry32(20260821);
    ctx.fillStyle = bg.detail;
    const dots = Math.round((w * h) / 2600);
    for (let i = 0; i < dots; i++) {
      const x = rnd() * w;
      const y = rnd() * h;
      const r = rnd() * (w / 900) + w / 2400;
      ctx.beginPath();
      ctx.arc(x, y, r, 0, Math.PI * 2);
      ctx.fill();
    }
    // 极浅的暗角，让纸有厚度而不是一块死色
    const v = ctx.createRadialGradient(w / 2, h / 2, Math.min(w, h) * 0.32, w / 2, h / 2, Math.max(w, h) * 0.78);
    v.addColorStop(0, 'rgba(0,0,0,0)');
    v.addColorStop(1, 'rgba(90,72,50,0.07)');
    ctx.fillStyle = v;
    ctx.fillRect(0, 0, w, h);
  }

  if (bg.kind === 'linen') {
    ctx.strokeStyle = bg.detail;
    ctx.lineWidth = Math.max(1, w / 1400);
    const step = w / 90;
    for (let x = 0; x < w; x += step) {
      ctx.beginPath();
      ctx.moveTo(x, 0);
      ctx.lineTo(x, h);
      ctx.stroke();
    }
    for (let y = 0; y < h; y += step) {
      ctx.beginPath();
      ctx.moveTo(0, y);
      ctx.lineTo(w, y);
      ctx.stroke();
    }
  }

  if (bg.kind === 'grid') {
    ctx.strokeStyle = bg.detail;
    ctx.lineWidth = Math.max(1, w / 1600);
    const step = w / 16;
    for (let x = step; x < w; x += step) {
      ctx.beginPath();
      ctx.moveTo(x, 0);
      ctx.lineTo(x, h);
      ctx.stroke();
    }
    for (let y = step; y < h; y += step) {
      ctx.beginPath();
      ctx.moveTo(0, y);
      ctx.lineTo(w, y);
      ctx.stroke();
    }
  }

  ctx.restore();
}

/** 背景在屏幕上的预览用同一份代码画进一张小 canvas，避免 CSS / canvas 两套实现走偏 */
export function backgroundSwatch(id: BoardBackgroundId, size = 44): string {
  const c = document.createElement('canvas');
  c.width = size;
  c.height = size;
  const ctx = c.getContext('2d');
  if (!ctx) return '';
  paintBackground(ctx, id, size, size);
  return c.toDataURL();
}

const imageCache = new Map<string, HTMLImageElement>();

export function loadImage(url: string): Promise<HTMLImageElement> {
  const hit = imageCache.get(url);
  if (hit?.complete && hit.naturalWidth) return Promise.resolve(hit);
  return new Promise((resolve, reject) => {
    const img = new Image();
    img.onload = () => {
      imageCache.set(url, img);
      resolve(img);
    };
    img.onerror = () => reject(new Error(`image failed: ${url}`));
    img.src = url;
  });
}

export function textFont(item: BoardItem, sizePx: number): string {
  const font = item.font ?? 'serif';
  if (font === 'serif_italic') return `italic 600 ${sizePx}px ${BOARD_FONT_STACK.serif}`;
  if (font === 'sans') return `600 ${sizePx}px ${BOARD_FONT_STACK.sans}`;
  return `600 ${sizePx}px ${BOARD_FONT_STACK.serif}`;
}

/**
 * 把整块画板画成一张图。
 *
 * `scale` = 1 出画布原尺寸（1400 × 1800），导出用 2 / 3 倍，Look 封面用 0.3 倍。
 * 单品带一层很淡的接触阴影 —— 拼贴少了这层会像贴纸浮在纸上。
 */
export async function renderBoard(
  board: BoardLook,
  assets: Asset[],
  scale = 1,
  opts: { shadow?: boolean; crop?: 'none' | 'content' } = {},
): Promise<string> {
  const { canvas } = board;
  // Look 封面按内容裁剪：卡片上要的是拼贴本身，不是画布四周的空白
  const view = opts.crop === 'content' ? contentView(board) : { x: 0, y: 0, w: canvas.w, h: canvas.h };
  const c = document.createElement('canvas');
  c.width = Math.round(view.w * scale);
  c.height = Math.round(view.h * scale);
  const ctx = c.getContext('2d');
  if (!ctx) throw new Error('canvas 2d unavailable');
  ctx.imageSmoothingEnabled = true;
  ctx.imageSmoothingQuality = 'high';

  paintBackground(ctx, board.background, c.width, c.height);
  ctx.scale(scale, scale);
  ctx.translate(-view.x, -view.y);

  const byId = new Map(assets.map((a) => [a.id, a]));
  const ordered = [...board.items].sort((a, b) => a.z - b.z);
  const shadow = opts.shadow ?? true;

  for (const item of ordered) {
    if (item.kind === 'text') {
      const size = item.h * 0.72;
      ctx.save();
      ctx.translate(item.x, item.y);
      ctx.rotate((item.rotation * Math.PI) / 180);
      ctx.font = textFont(item, size);
      ctx.fillStyle = item.color ?? boardBackground(board.background).ink;
      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';
      ctx.fillText(item.text ?? '', 0, 0);
      ctx.restore();
      continue;
    }
    const asset = item.assetId ? byId.get(item.assetId) : undefined;
    if (!asset) continue; // 素材被删了：跳过，不画破图
    let img: HTMLImageElement;
    try {
      img = await loadImage(asset.cutoutUrl);
    } catch {
      continue;
    }
    ctx.save();
    ctx.translate(item.x, item.y);
    ctx.rotate((item.rotation * Math.PI) / 180);
    if (item.flipX) ctx.scale(-1, 1);
    if (shadow) {
      ctx.shadowColor = 'rgba(48,38,26,0.16)';
      ctx.shadowBlur = Math.max(8, item.h * 0.035);
      ctx.shadowOffsetY = Math.max(3, item.h * 0.012);
    }
    ctx.drawImage(img, -item.w / 2, -item.h / 2, item.w, item.h);
    ctx.restore();
  }

  return c.toDataURL('image/png');
}

/** 所有元素的外接矩形 + 一圈留白，超出画布的部分裁掉 */
function contentView(board: BoardLook): { x: number; y: number; w: number; h: number } {
  const items = board.items;
  if (items.length === 0) return { x: 0, y: 0, w: board.canvas.w, h: board.canvas.h };
  let x1 = Infinity; let y1 = Infinity; let x2 = -Infinity; let y2 = -Infinity;
  for (const it of items) {
    const b = itemBounds(it);
    x1 = Math.min(x1, b.x1);
    y1 = Math.min(y1, b.y1);
    x2 = Math.max(x2, b.x2);
    y2 = Math.max(y2, b.y2);
  }
  const pad = Math.max(28, (x2 - x1) * 0.06);
  x1 = Math.max(0, x1 - pad);
  y1 = Math.max(0, y1 - pad);
  x2 = Math.min(board.canvas.w, x2 + pad);
  y2 = Math.min(board.canvas.h, y2 + pad);
  return { x: x1, y: y1, w: Math.max(1, x2 - x1), h: Math.max(1, y2 - y1) };
}
