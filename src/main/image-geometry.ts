import type { AssetMeta } from '../shared/types';

export interface AlphaBounds {
  x: number;
  y: number;
  width: number;
  height: number;
}

/** Alpha bounds for Electron's BGRA bitmap representation. */
export function alphaBoundsFromBgra(
  bitmap: Uint8Array,
  width: number,
  height: number,
  padding = 2,
  threshold = 16,
): AlphaBounds | null {
  if (bitmap.length < width * height * 4) throw new Error('bitmap is smaller than its declared size');
  let x0 = width;
  let y0 = height;
  let x1 = -1;
  let y1 = -1;
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      if (bitmap[(y * width + x) * 4 + 3] > threshold) {
        if (x < x0) x0 = x;
        if (x > x1) x1 = x;
        if (y < y0) y0 = y;
        if (y > y1) y1 = y;
      }
    }
  }
  if (x1 < 0) return null;
  const pad = Math.max(0, Math.floor(padding));
  x0 = Math.max(x0 - pad, 0);
  y0 = Math.max(y0 - pad, 0);
  x1 = Math.min(x1 + pad, width - 1);
  y1 = Math.min(y1 + pad, height - 1);
  return { x: x0, y: y0, width: x1 - x0 + 1, height: y1 - y0 + 1 };
}

/**
 * Measure stable garment edges without treating small alpha fragments as fit anchors.
 * The returned shoulder/waist estimates stay advisory unless a pack marks them given.
 */
export function measureLandmarksFromBgra(
  bitmap: Uint8Array,
  width: number,
  height: number,
  threshold = 16,
): NonNullable<AssetMeta['landmarks']> {
  if (bitmap.length < width * height * 4) throw new Error('bitmap is smaller than its declared size');
  const lo: number[] = new Array(height).fill(-1);
  const hi: number[] = new Array(height).fill(-1);
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      if (bitmap[(y * width + x) * 4 + 3] > threshold) {
        if (lo[y] < 0) lo[y] = x;
        hi[y] = x;
      }
    }
  }
  const span = (y: number) => (lo[y] < 0 ? 0 : hi[y] - lo[y] + 1);
  const max = Math.max(...lo.map((_, y) => span(y)), 1);
  const cut = max * 0.25;

  let top = 0;
  for (let y = 0; y < height; y++) {
    if (span(y) >= cut) {
      top = y;
      break;
    }
  }
  let hem = height - 1;
  for (let y = height - 1; y >= 0; y--) {
    if (span(y) >= cut) {
      hem = y;
      break;
    }
  }

  const rowAt = (fraction: number) => {
    const y = Math.min(Math.max(Math.round(top + (hem - top) * fraction), 0), height - 1);
    const candidates: number[] = [];
    for (let delta = -3; delta <= 3; delta++) {
      const candidate = Math.min(Math.max(y + delta, 0), height - 1);
      if (span(candidate) > 0) candidates.push(candidate);
    }
    if (candidates.length === 0) return y;
    candidates.sort((a, b) => span(a) - span(b));
    return candidates[Math.floor(candidates.length / 2)];
  };

  const shoulderRow = rowAt(0.12);
  const waistRow = rowAt(0.04);
  return {
    top_edge: { x: Math.round((lo[top] + hi[top]) / 2), y: top },
    hem: { x: Math.round((lo[hem] + hi[hem]) / 2), y: hem },
    shoulder_l: { x: lo[shoulderRow], y: shoulderRow },
    shoulder_r: { x: hi[shoulderRow], y: shoulderRow },
    waist_l: { x: lo[waistRow], y: waistRow },
    waist_r: { x: hi[waistRow], y: waistRow },
  };
}
