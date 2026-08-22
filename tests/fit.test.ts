import { describe, expect, it } from 'vitest';

import { BASE_ANCHORS, type Slot } from '../src/shared/spec';
import { DEFAULT_FIT, type Asset } from '../src/shared/types';
import { placeGarment } from '../src/renderer/src/render/fit';
import type { BaseMetrics } from '../src/renderer/src/render/metrics';

const metrics: BaseMetrics = {
  canvas: { w: 1152, h: 2304 },
  anchors: { ...BASE_ANCHORS },
  shoulderW: 392,
  width: {
    head: 244,
    shoulder: 392,
    chest: 337,
    waist: 280,
    hip: 360,
    thigh: 204,
    knee: 133,
    ankle: 230,
  },
  estimated: false,
};

function asset(slot: Slot, bitmap: { w: number; h: number }, landmarks: Asset['landmarks'] = {}, given: NonNullable<Asset['landmarks_given']> = []): Asset {
  return {
    schema_version: 3,
    id: `fit-${slot}`,
    name: `Fit ${slot}`,
    category: slot === 'bottom' ? 'bottom'
      : slot === 'neckwear' ? 'neckwear'
        : slot === 'bag' ? 'bag'
        : slot.startsWith('shoe') ? 'shoe'
          : slot === 'outer' ? 'outer'
            : slot === 'dress' ? 'dress'
              : 'top',
    slot,
    z_offset: 0,
    occupies: [slot],
    companion: null,
    pose: 'front_idle',
    canvas: { w: 1152, h: 2304 },
    bitmap: { file: `${slot}.png`, ...bitmap },
    source_resolution: null,
    anchor: { base: 'shoulder_line', x: bitmap.w / 2, y: 0 },
    landmarks,
    landmarks_given: given,
    fit: DEFAULT_FIT,
    palette: { dominant: '#000000', color_family: 'black_grey', colors: [] },
    attributes: {},
    tags: [],
    season: [],
    source: { photo_id: null, photo_file: null, bbox: null, imported_at: '2026-08-20T00:00:00.000Z' },
    provenance: { model: 'test', model_version: '1', confidence: 1, edited_by_user: false, edit_ops: [] },
    files: { original: null, cutout: `${slot}.png`, thumb: `${slot}-thumb.png` },
    favorite: false,
    wear_count: 0,
    created_at: '2026-08-20T00:00:00.000Z',
    updated_at: '2026-08-20T00:00:00.000Z',
    cutoutUrl: '',
    thumbUrl: '',
  };
}

describe('placeGarment wearable-region placement', () => {
  it.each(['top', 'outer', 'dress'] as const)('maps a %s shoulder pair to both model shoulders', (slot) => {
    const garment = asset(slot, { w: 300, h: 360 }, {
      shoulder_l: { x: 70, y: 50 },
      shoulder_r: { x: 230, y: 50 },
      top_edge: { x: 150, y: 30 },
      hem: { x: 150, y: 300 },
    }, ['shoulder_l', 'shoulder_r']);
    const placed = placeGarment(garment, slot, metrics, DEFAULT_FIT);
    const left = garment.landmarks!.shoulder_l!;
    const right = garment.landmarks!.shoulder_r!;

    expect(placed.x + left.x * placed.scaleX).toBeCloseTo(metrics.anchors.shoulder_l.x);
    expect(placed.x + right.x * placed.scaleX).toBeCloseTo(metrics.anchors.shoulder_r.x);
    expect(placed.y + left.y * placed.scaleY).toBeCloseTo(metrics.anchors.shoulder_line.y);
  });

  it('uses the cropped subject bounds instead of inferred shoulder rows for photo outerwear', () => {
    const outer = asset('outer', { w: 382, h: 462 }, {
      top_edge: { x: 218, y: 34 },
      hem: { x: 63, y: 430 },
      shoulder_l: { x: 100, y: 82 },
      shoulder_r: { x: 295, y: 82 },
    });
    outer.source.origin = 'photo';
    outer.review_status = 'needs_optimization';

    const placed = placeGarment(outer, 'outer', metrics, DEFAULT_FIT);
    const materialTop = placed.y + outer.landmarks!.top_edge!.y * placed.scaleY;
    const materialHem = placed.y + outer.landmarks!.hem!.y * placed.scaleY;

    expect(placed.w).toBeCloseTo(454.72);
    expect(materialTop).toBeCloseTo(468.64);
    // The outer's -0.08 shoulder-width collar offset leaves the hem just below the hip.
    expect(materialHem).toBeCloseTo(1017.84);
    expect(placed.precise).toBe(false);
  });

  it('mounts a photo handbag by its handle top at the right wrist', () => {
    const bag = asset('bag', { w: 277, h: 390 }, {
      top_edge: { x: 221, y: 13 },
      hem: { x: 186, y: 381 },
    });
    bag.source.origin = 'photo';

    const placed = placeGarment(bag, 'bag', metrics, DEFAULT_FIT);
    const renderedHandle = {
      x: placed.x + bag.landmarks!.top_edge!.x * placed.scaleX,
      y: placed.y + bag.landmarks!.top_edge!.y * placed.scaleY,
    };

    expect(renderedHandle.x).toBeCloseTo(metrics.anchors.wrist_r.x);
    expect(renderedHandle.y).toBeCloseTo(metrics.anchors.wrist_r.y);
  });

  it('keeps a bundled backpack on the legacy hip-side mount', () => {
    const backpack = asset('bag', { w: 277, h: 390 }, {
      top_edge: { x: 140, y: 15 },
      hem: { x: 140, y: 380 },
    });
    backpack.source.origin = 'bundle';
    backpack.tags = ['backpack'];

    const placed = placeGarment(backpack, 'bag', metrics, DEFAULT_FIT);
    const renderedCenter = {
      x: placed.x + backpack.bitmap.w / 2 * placed.scaleX,
      y: placed.y + backpack.bitmap.h / 2 * placed.scaleY,
    };

    expect(renderedCenter.x).toBeCloseTo(metrics.anchors.hip.x + metrics.shoulderW * 0.58);
    expect(renderedCenter.y).toBeCloseTo(metrics.anchors.hip.y);
  });

  it.each([
    ['just below', 126, false],
    ['just above', 124, true],
  ])('keeps a semantic shoulder pair %s the rendered bitmap-width safety limit', (_label, pairWidth, clamps) => {
    const garment = asset('top', { w: 300, h: 360 }, {
      shoulder_l: { x: 100, y: 50 },
      shoulder_r: { x: 100 + pairWidth, y: 50 },
      top_edge: { x: 150, y: 30 },
      hem: { x: 150, y: 300 },
    });

    const placed = placeGarment(garment, 'top', metrics, DEFAULT_FIT);
    const maxWidth = metrics.shoulderW * 2.4;

    expect(placed.w).toBeLessThanOrEqual(maxWidth);
    if (clamps) expect(placed.w).toBeCloseTo(maxWidth);
    else expect(placed.w).toBeCloseTo(metrics.shoulderW * garment.bitmap.w / pairWidth);
  });

  it('aligns a bottom waist pair at the body waist while covering the hips', () => {
    const bottom = asset('bottom', { w: 200, h: 500 }, {
      waist_l: { x: 15, y: 25 },
      waist_r: { x: 185, y: 25 },
      top_edge: { x: 100, y: 25 },
      hem: { x: 100, y: 470 },
    }, ['waist_l', 'waist_r']);
    const placed = placeGarment(bottom, 'bottom', metrics, DEFAULT_FIT);
    const left = bottom.landmarks!.waist_l!;
    const right = bottom.landmarks!.waist_r!;
    const renderedLeft = {
      x: placed.x + left.x * placed.scaleX,
      y: placed.y + left.y * placed.scaleY,
    };
    const renderedRight = {
      x: placed.x + right.x * placed.scaleX,
      y: placed.y + right.y * placed.scaleY,
    };

    expect((renderedLeft.x + renderedRight.x) / 2).toBeCloseTo(metrics.anchors.waist.x);
    expect((renderedLeft.y + renderedRight.y) / 2).toBeCloseTo(metrics.anchors.waist.y);
    expect(renderedRight.x - renderedLeft.x).toBeGreaterThanOrEqual(metrics.width.hip);
  });

  it('ignores a rejected tilted shoulder pair for fallback scale and attachment', () => {
    const top = asset('top', { w: 300, h: 360 }, {
      shoulder_l: { x: 20, y: 10 },
      shoulder_r: { x: 250, y: 300 },
      hem: { x: 150, y: 300 },
    }, ['shoulder_l', 'shoulder_r']);
    top.anchor = { base: 'shoulder_line', x: 40, y: 0 };

    const placed = placeGarment(top, 'top', metrics, DEFAULT_FIT);

    // Fallback x scale = 392 * 1.06 / 300; attach x uses asset.anchor.x = 40.
    expect(placed.scaleX).toBeCloseTo(1.3850666667);
    expect(placed.x).toBeCloseTo(520.5973333333);
    // With no valid pair or top_edge, bitmap row 0 attaches at shoulder_line - 0.05 shoulderW.
    expect(placed.y).toBeCloseTo(480.4);
    expect(placed.precise).toBe(false);
  });

  it('keeps neckwear material below the face-clearance margin', () => {
    const neckwear = asset('neckwear', { w: 400, h: 220 }, {
      top_edge: { x: 200, y: 40 },
      hem: { x: 200, y: 190 },
    });
    const placed = placeGarment(neckwear, 'neckwear', metrics, DEFAULT_FIT);
    const firstMaterialRow = placed.y + neckwear.landmarks!.top_edge!.y * (placed.h / neckwear.bitmap.h);
    // Keep the wearable region clear of the face, not merely one rendered pixel below the chin.
    const faceClearanceMargin = metrics.width.shoulder * 0.1;

    expect(firstMaterialRow).toBeGreaterThanOrEqual(metrics.anchors.chin.y + faceClearanceMargin);
  });

  it('overlaps above the ankle and maps the shoe hem to the foot base', () => {
    const shoes = asset('shoe_base', { w: 400, h: 100 }, {
      top_edge: { x: 200, y: 0 },
      hem: { x: 200, y: 100 },
    });
    const placed = placeGarment(shoes, 'shoe_base', metrics, DEFAULT_FIT);
    const materialTop = placed.y + shoes.landmarks!.top_edge!.y * placed.scaleY;
    const materialHem = placed.y + shoes.landmarks!.hem!.y * placed.scaleY;

    expect(materialTop).toBeLessThan(metrics.anchors.ankle_l.y);
    expect(materialHem).toBeCloseTo(metrics.anchors.foot_base.y);
  });

  it('gives tall photo shoes enough foot length without exceeding the two-ankle span', () => {
    const shoes = asset('shoe_base', { w: 255, h: 337 }, {
      top_edge: { x: 127, y: 2 },
      hem: { x: 127, y: 331 },
    });
    shoes.source.origin = 'photo';

    const placed = placeGarment(shoes, 'shoe_base', metrics, DEFAULT_FIT);
    const materialTop = placed.y + shoes.landmarks!.top_edge!.y * placed.scaleY;
    const materialHem = placed.y + shoes.landmarks!.hem!.y * placed.scaleY;

    expect(placed.w).toBeCloseTo(257.6);
    expect(materialTop).toBeCloseTo(2077.28);
    expect(materialHem).toBeCloseTo(metrics.anchors.foot_base.y);
  });

  it('applies fit.scale to both placement axes', () => {
    const top = asset('top', { w: 300, h: 360 }, {
      shoulder_l: { x: 70, y: 50 },
      shoulder_r: { x: 230, y: 50 },
      top_edge: { x: 150, y: 30 },
      hem: { x: 150, y: 300 },
    }, ['shoulder_l', 'shoulder_r']);
    const baseline = placeGarment(top, 'top', metrics, DEFAULT_FIT);
    const scaled = placeGarment(top, 'top', metrics, { ...DEFAULT_FIT, scale: 1.2 });

    expect(scaled.scaleX / baseline.scaleX).toBeCloseTo(1.2);
    expect(scaled.scaleY / baseline.scaleY).toBeCloseTo(1.2);
  });

  it('applies stretch_x to x without changing y scale', () => {
    const top = asset('top', { w: 300, h: 360 }, {
      shoulder_l: { x: 70, y: 50 },
      shoulder_r: { x: 230, y: 50 },
      top_edge: { x: 150, y: 30 },
      hem: { x: 150, y: 300 },
    }, ['shoulder_l', 'shoulder_r']);
    const baseline = placeGarment(top, 'top', metrics, DEFAULT_FIT);
    const stretched = placeGarment(top, 'top', metrics, { ...DEFAULT_FIT, stretch_x: 1.1 });

    expect(stretched.scaleX / baseline.scaleX).toBeCloseTo(1.1);
    expect(stretched.scaleY).toBeCloseTo(baseline.scaleY);
  });
});
