/**
 * CERE-28：把自动量出来的锚点画到抠好的底图上，人眼复核一遍。
 *
 *   npx vite-node --script scripts/cere28-anchor-preview.ts -- <cutout.png> <out.png>
 *
 * 锚点是自动推的，所以「推得对不对」必须有一张能看的图，而不是只看一串坐标。
 */

import fs from 'node:fs';
import path from 'node:path';
import { PNG } from './png';
import { deriveAnchors } from '../src/shared/base-anchors';
import type { AnchorName } from '../src/shared/spec';

const COLORS: Record<string, [number, number, number]> = {
  head_top: [220, 60, 60],
  eye_line: [220, 140, 60],
  chin: [220, 200, 60],
  neck: [120, 200, 60],
  shoulder_line: [40, 170, 120],
  shoulder_l: [40, 200, 200],
  shoulder_r: [40, 200, 200],
  chest: [60, 140, 220],
  waist: [110, 90, 220],
  hip: [190, 70, 200],
  crotch: [220, 60, 140],
  wrist_l: [255, 120, 40],
  wrist_r: [255, 120, 40],
  knee: [80, 80, 80],
  ankle_l: [20, 20, 20],
  ankle_r: [20, 20, 20],
  foot_base: [0, 0, 0],
};

function main(): void {
  const args = process.argv.slice(2).filter((value) => value !== '--');
  const [source, output] = args;
  if (!source || !output) throw new Error('usage: cere28-anchor-preview <cutout.png> <out.png>');

  const png = PNG.read(fs.readFileSync(source));
  const alpha = new Uint8Array(png.width * png.height);
  for (let i = 0; i < alpha.length; i++) alpha[i] = png.data[i * 4 + 3];

  const derived = deriveAnchors({ width: png.width, height: png.height, alpha });
  console.log('fallback:', derived.fallback, 'notes:', derived.notes);
  for (const [name, point] of Object.entries(derived.anchors)) {
    console.log(`  ${name.padEnd(14)} x=${String(point.x).padStart(4)} y=${String(point.y).padStart(4)}`);
  }

  // 白底 + 半透明人物，锚点画在上面才看得清
  const out = new Uint8Array(png.width * png.height * 4);
  for (let i = 0; i < png.width * png.height; i++) {
    const a = png.data[i * 4 + 3] / 255;
    for (let c = 0; c < 3; c++) {
      out[i * 4 + c] = Math.round(255 * (1 - a) + png.data[i * 4 + c] * a * 0.55 + 255 * a * 0.45);
    }
    out[i * 4 + 3] = 255;
  }

  const put = (x: number, y: number, rgb: [number, number, number]) => {
    if (x < 0 || y < 0 || x >= png.width || y >= png.height) return;
    const i = (y * png.width + x) * 4;
    out[i] = rgb[0]; out[i + 1] = rgb[1]; out[i + 2] = rgb[2];
  };

  for (const [name, point] of Object.entries(derived.anchors) as [AnchorName, { x: number; y: number }][]) {
    const rgb = COLORS[name] ?? [255, 0, 255];
    for (let x = 0; x < png.width; x += 3) put(x, point.y, rgb);      // 横线：这一层在哪
    for (let d = -7; d <= 7; d++) {                                    // 十字：具体点位
      put(point.x + d, point.y, rgb);
      put(point.x, point.y + d, rgb);
      put(point.x + d, point.y + 1, rgb);
      put(point.x + 1, point.y + d, rgb);
    }
  }

  fs.mkdirSync(path.dirname(path.resolve(output)), { recursive: true });
  fs.writeFileSync(output, PNG.write(png.width, png.height, out));
  console.log('wrote', output);
}

main();
