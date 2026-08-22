import { describe, expect, it } from 'vitest';

import {
  alphaBoundsFromBgra,
  measureLandmarksFromBgra,
} from '../src/main/image-geometry';

function silhouette(width: number, height: number): Uint8Array {
  return new Uint8Array(width * height * 4);
}

function fillAlpha(
  bitmap: Uint8Array,
  width: number,
  y: number,
  fromX: number,
  toX: number,
): void {
  for (let x = fromX; x <= toX; x++) bitmap[(y * width + x) * 4 + 3] = 255;
}

describe('transparent cutout geometry', () => {
  it('crops transparent photo-sized canvas to the visible alpha bounds with padding', () => {
    const bitmap = silhouette(10, 10);
    for (let y = 2; y <= 7; y++) fillAlpha(bitmap, 10, y, 1, 8);

    expect(alphaBoundsFromBgra(bitmap, 10, 10, 1)).toEqual({
      x: 0, y: 1, width: 10, height: 8,
    });
  });

  it('returns no geometry for a fully transparent candidate', () => {
    expect(alphaBoundsFromBgra(silhouette(4, 4), 4, 4)).toBeNull();
  });

  it('measures stable garment edges from the cropped alpha silhouette', () => {
    const bitmap = silhouette(10, 10);
    fillAlpha(bitmap, 10, 2, 3, 6);
    fillAlpha(bitmap, 10, 3, 2, 7);
    for (let y = 4; y <= 7; y++) fillAlpha(bitmap, 10, y, 1, 8);

    expect(measureLandmarksFromBgra(bitmap, 10, 10)).toMatchObject({
      top_edge: { x: 5, y: 2 },
      hem: { x: 5, y: 7 },
      shoulder_l: { x: 1, y: 4 },
      shoulder_r: { x: 8, y: 4 },
    });
  });
});
