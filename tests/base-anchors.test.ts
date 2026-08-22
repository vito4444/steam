import { describe, expect, it } from 'vitest';

import {
  ANCHOR_ORDER,
  deriveAnchors,
  normalizeAnchors,
  scaledFallbackAnchors,
  type Silhouette,
} from '../src/shared/base-anchors';
import { alphaFromBitmap, buildBaseManifest } from '../src/main/base-import';
import type { AnchorName, Point } from '../src/shared/spec';

/**
 * 画一个「站着的人」的剪影：头 / 脖子 / 肩 / 躯干（腰收进去、胯张开）/
 * 垂在体侧且**与躯干分开**的两条手臂 / 分开的两条腿。
 *
 * 手臂必须是分开的 —— 真人照片抠出来就是这样，而这正是最早把腰和裆量错的原因。
 */
function standingFigure(width = 400, height = 1000): Silhouette {
  const alpha = new Uint8Array(width * height);
  const cx = Math.round(width / 2);
  const fill = (y: number, from: number, to: number) => {
    for (let x = Math.max(0, from); x <= Math.min(width - 1, to); x++) alpha[y * width + x] = 255;
  };

  for (let y = 0; y < height; y++) {
    const t = y / height;
    if (t < 0.06) continue;                                   // 头顶留白
    if (t < 0.16) { fill(y, cx - 34, cx + 34); continue; }     // 头
    if (t < 0.19) { fill(y, cx - 16, cx + 16); continue; }     // 脖子（最窄）
    if (t < 0.22) { fill(y, cx - 76, cx + 76); continue; }     // 肩（突然变宽）

    // 躯干：胸 70 → 腰 52 → 胯 78
    let half = 70;
    if (t < 0.34) half = 70;
    else if (t < 0.44) half = 52;
    else if (t < 0.56) half = 78;
    else half = 74;

    if (t < 0.60) {
      fill(y, cx - half, cx + half);
      if (t >= 0.24 && t < 0.58) {                            // 双臂，和躯干之间留缝
        fill(y, cx - half - 34, cx - half - 12);
        fill(y, cx + half + 12, cx + half + 34);
      }
      continue;
    }
    // 双腿，中间有缝
    fill(y, cx - 70, cx - 12);
    fill(y, cx + 12, cx + 70);
  }
  return { width, height, alpha };
}

describe('deriveAnchors', () => {
  const figure = standingFigure();
  const { anchors, fallback, notes } = deriveAnchors(figure);

  it('measures a silhouette instead of falling back', () => {
    expect(fallback).toBe(false);
    expect(notes).toEqual([]);
  });

  it('keeps the anatomical order top to bottom', () => {
    const ys = ANCHOR_ORDER.map((name) => anchors[name].y);
    for (let i = 1; i < ys.length; i++) {
      expect(ys[i], `${ANCHOR_ORDER[i]} must sit below ${ANCHOR_ORDER[i - 1]}`).toBeGreaterThan(ys[i - 1]);
    }
  });

  it('puts the shoulder line at the shoulders, not down at the armpit', () => {
    // 图里肩在 21% 高度；取「最宽的一行」会掉到手臂那一段去（>24%）。
    const ratio = anchors.shoulder_line.y / figure.height;
    expect(ratio).toBeGreaterThan(0.18);
    expect(ratio).toBeLessThan(0.245);
    expect(anchors.shoulder_l.x).toBeLessThan(anchors.shoulder_line.x);
    expect(anchors.shoulder_r.x).toBeGreaterThan(anchors.shoulder_line.x);
  });

  it('finds the waist above the hip, both from torso width and not arm span', () => {
    expect(anchors.waist.y / figure.height).toBeGreaterThan(0.33);
    expect(anchors.waist.y / figure.height).toBeLessThan(0.46);
    expect(anchors.hip.y / figure.height).toBeGreaterThanOrEqual(0.44);
    expect(anchors.hip.y / figure.height).toBeLessThan(0.60);
  });

  it('takes the crotch from the gap between the legs, not the gap beside an arm', () => {
    // 手臂从 24% 起就和躯干分开了；只认中轴上的缝才不会把裆定在那里。
    expect(anchors.crotch.y / figure.height).toBeGreaterThan(0.55);
    expect(anchors.crotch.y / figure.height).toBeLessThan(0.68);
  });

  it('reads the wrists from the outermost silhouette points', () => {
    expect(anchors.wrist_l.x).toBeLessThan(anchors.shoulder_l.x);
    expect(anchors.wrist_r.x).toBeGreaterThan(anchors.shoulder_r.x);
  });

  it('separates the two ankles and keeps them above the sole', () => {
    expect(anchors.ankle_l.x).toBeLessThan(anchors.ankle_r.x);
    expect(anchors.foot_base.y).toBeGreaterThan(anchors.ankle_l.y);
  });

  it('flags a skirt-like silhouette instead of inventing a crotch split', () => {
    const width = 300;
    const height = 900;
    const alpha = new Uint8Array(width * height);
    for (let y = Math.round(height * 0.06); y < height; y++) {
      const half = y / height < 0.2 ? 30 : 90;
      for (let x = 150 - half; x <= 150 + half; x++) alpha[y * width + x] = 255;
    }
    const skirt = deriveAnchors({ width, height, alpha });
    expect(skirt.fallback).toBe(false);
    expect(skirt.notes.join('')).toContain('并腿');
  });

  it('falls back to scaled defaults when there is no figure at all', () => {
    const empty = deriveAnchors({ width: 200, height: 400, alpha: new Uint8Array(200 * 400) });
    expect(empty.fallback).toBe(true);
    expect(empty.anchors.foot_base.y).toBeLessThan(400);
  });
});

describe('normalizeAnchors', () => {
  it('clamps into the canvas and repairs an inverted order', () => {
    const broken = Object.fromEntries(
      ANCHOR_ORDER.map((name) => [name, { x: -50, y: 900 }]),
    ) as Record<AnchorName, Point>;
    const filled = { ...scaledFallbackAnchors(100, 200), ...broken };
    const fixed = normalizeAnchors(filled, 100, 200);
    for (const point of Object.values(fixed)) {
      expect(point.x).toBeGreaterThanOrEqual(0);
      expect(point.x).toBeLessThan(100);
      expect(point.y).toBeLessThan(200);
    }
    const ys = ANCHOR_ORDER.map((name) => fixed[name].y);
    for (let i = 1; i < ys.length; i++) expect(ys[i]).toBeGreaterThan(ys[i - 1]);
  });
});

describe('base pack manifest', () => {
  it('omits mask and tones so the renderer falls back to the photo alpha', () => {
    const manifest = buildBaseManifest({
      pack: 'photo-f-test',
      canvas: { w: 800, h: 1600 },
      anchors: scaledFallbackAnchors(800, 1600),
      bodyFile: 'body.png',
    });
    expect(manifest.mask).toBeUndefined();
    expect(manifest.tones).toEqual([]);
    expect(manifest.layers).toEqual([{ file: 'body.png', z: 20, mode: 'normal' }]);
    expect(manifest.canvas).toEqual({ w: 800, h: 1600 });
  });
});

describe('alphaFromBitmap', () => {
  it('reads the alpha lane out of a BGRA bitmap', () => {
    const bitmap = Buffer.from([1, 2, 3, 200, 4, 5, 6, 0, 7, 8, 9, 255]);
    expect(Array.from(alphaFromBitmap(bitmap, 3, 1))).toEqual([200, 0, 255]);
  });
});
