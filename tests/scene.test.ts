import { describe, expect, it } from 'vitest';

import { DEFAULT_OCCLUSION } from '../src/shared/occlusion';
import { BASE_ANCHORS, type Slot } from '../src/shared/spec';
import { DEFAULT_FIT, type Asset, type BaseBodySet } from '../src/shared/types';
import { buildScene } from '../src/renderer/src/render/scene';
import type { BaseMetrics } from '../src/renderer/src/render/metrics';
import type { RenderInput } from '../src/renderer/src/render/types';

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

const base: BaseBodySet = {
  body: 'base_f02',
  canvas: metrics.canvas,
  layers: [{ file: 'body.png', url: 'body.png', z: 20, mode: 'normal' }],
  tones: [],
  hair: {},
  anchors: metrics.anchors,
  pack: 'scene-test',
  source: 'builtin',
};

function asset(slot: Slot): Asset {
  return {
    schema_version: 3,
    id: `scene-${slot}`,
    name: `Scene ${slot}`,
    category: slot === 'outer' ? 'outer' : 'top',
    slot,
    z_offset: 0,
    occupies: [slot],
    companion: null,
    pose: 'front_idle',
    canvas: metrics.canvas,
    bitmap: { file: `${slot}.png`, w: 300, h: 360 },
    source_resolution: null,
    anchor: { base: 'shoulder_line', x: 150, y: 30 },
    landmarks: {
      shoulder_l: { x: 70, y: 50 },
      shoulder_r: { x: 230, y: 50 },
      top_edge: { x: 150, y: 30 },
      hem: { x: 150, y: 330 },
    },
    landmarks_given: ['shoulder_l', 'shoulder_r'],
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
    cutoutUrl: `${slot}.png`,
    thumbUrl: `${slot}-thumb.png`,
  };
}

describe('buildScene structural order', () => {
  it('clips unstable photo fragments outside the measured material span', () => {
    const bottom = asset('bottom');
    bottom.bitmap = { file: 'bottom.png', w: 256, h: 254 };
    bottom.source.origin = 'photo';
    bottom.review_status = 'needs_optimization';
    bottom.landmarks = {
      top_edge: { x: 161, y: 125 },
      hem: { x: 110, y: 248 },
      waist_l: { x: 89, y: 130 },
      waist_r: { x: 233, y: 130 },
    };
    bottom.landmarks_given = [];
    const input: RenderInput = {
      base,
      body: 'base_f02',
      tone: 0,
      hairStyle: 'h01',
      hairHex: '#000000',
      worn: [
        { asset: bottom, slot: 'bottom', z: 40, fit: DEFAULT_FIT, hidden: false, highlight: false, tuck: 'out' },
      ],
      background: 'none',
      occlusion: DEFAULT_OCCLUSION,
    };

    const layer = buildScene(input, metrics, 1).layers.find((candidate) => candidate.key === bottom.id)!;

    expect(layer.clip?.keepFrom).toBeCloseTo(838.16);
    expect(layer.clip?.keepTo).toBeCloseTo(1021.506875);
  });

  it('uses custom slot order plus clamped caller item offsets', () => {
    const top = asset('top');
    const outer = asset('outer');
    const occlusion = {
      ...DEFAULT_OCCLUSION,
      order: { ...DEFAULT_OCCLUSION.order, top: 120, outer: 140 },
    };
    const input: RenderInput = {
      base,
      body: 'base_f02',
      tone: 0,
      hairStyle: 'h01',
      hairHex: '#000000',
      worn: [
        { asset: top, slot: 'top', z: 90, fit: DEFAULT_FIT, hidden: false, highlight: false, tuck: 'out' },
        { asset: outer, slot: 'outer', z: 10, fit: DEFAULT_FIT, hidden: false, highlight: false, tuck: 'out' },
      ],
      background: 'none',
      noOcclusion: true,
      occlusion,
    };

    const layers = buildScene(input, metrics, 1).layers;
    const bodyZ = layers.find((layer) => layer.key === 'base_body.png')!.z;
    const topZ = layers.find((layer) => layer.key === top.id)!.z;
    const outerZ = layers.find((layer) => layer.key === outer.id)!.z;

    expect(topZ).toBe(124);
    expect(outerZ).toBe(136);
    expect(bodyZ).toBeLessThan(topZ);
    expect(topZ).toBeLessThan(outerZ);
  });

  it('keeps default adjacent clothing slots in structural order under opposing offsets', () => {
    const slots: Slot[] = ['bottom', 'legwear', 'shoe_base', 'shoe_shaft'];
    const input: RenderInput = {
      base,
      body: 'base_f02',
      tone: 0,
      hairStyle: 'h01',
      hairHex: '#000000',
      worn: slots.map((slot, index) => ({
        asset: asset(slot),
        slot,
        z: index === 0 ? 44 : ({ legwear: 40, shoe_base: 42, shoe_shaft: 44 } as const)[slot as 'legwear' | 'shoe_base' | 'shoe_shaft'],
        fit: DEFAULT_FIT,
        hidden: false,
        highlight: false,
        tuck: 'out' as const,
      })),
      background: 'none',
      noOcclusion: true,
      occlusion: DEFAULT_OCCLUSION,
    };

    const garmentZ = new Map(
      buildScene(input, metrics, 1).layers
        .filter((layer) => layer.kind === 'garment')
        .map((layer) => [layer.key, layer.z]),
    );

    expect(garmentZ.get('scene-bottom')!).toBeLessThan(garmentZ.get('scene-legwear')!);
    expect(garmentZ.get('scene-legwear')!).toBeLessThan(garmentZ.get('scene-shoe_base')!);
    expect(garmentZ.get('scene-shoe_base')!).toBeLessThan(garmentZ.get('scene-shoe_shaft')!);
  });

  it('keeps a tight custom top and outer order under opposing offsets', () => {
    const top = asset('top');
    const outer = asset('outer');
    const occlusion = {
      ...DEFAULT_OCCLUSION,
      order: { ...DEFAULT_OCCLUSION.order, top: 120, outer: 121 },
    };
    const input: RenderInput = {
      base,
      body: 'base_f02',
      tone: 0,
      hairStyle: 'h01',
      hairHex: '#000000',
      worn: [
        { asset: top, slot: 'top', z: 90, fit: DEFAULT_FIT, hidden: false, highlight: false, tuck: 'out' },
        { asset: outer, slot: 'outer', z: 10, fit: DEFAULT_FIT, hidden: false, highlight: false, tuck: 'out' },
      ],
      background: 'none',
      noOcclusion: true,
      occlusion,
    };

    const layers = buildScene(input, metrics, 1).layers;
    expect(layers.find((layer) => layer.key === top.id)!.z)
      .toBeLessThan(layers.find((layer) => layer.key === outer.id)!.z);
  });

  it('keeps the bottom contact-shadow profile independent of hip sizing', () => {
    const bottom = asset('bottom');
    const input: RenderInput = {
      base,
      body: 'base_f02',
      tone: 0,
      hairStyle: 'h01',
      hairHex: '#000000',
      worn: [
        { asset: bottom, slot: 'bottom', z: 40, fit: DEFAULT_FIT, hidden: false, highlight: false, tuck: 'out' },
      ],
      background: 'none',
      noOcclusion: true,
      occlusion: DEFAULT_OCCLUSION,
    };

    expect(buildScene(input, metrics, 1).layers.find((layer) => layer.key === bottom.id)!.contact)
      .toEqual({ blur: metrics.shoulderW * 0.05, offset: metrics.shoulderW * 0.016, alpha: 0.4 });
  });
});
