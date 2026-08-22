/**
 * CERE-64：给取证跑合成一张「带背景的商品照」。
 *
 *   npx vite-node --script scripts/cere64-photo-fixture.ts <cutout.png> <out.png>
 *
 * 取证要证明的是**流程**：缺模型 → 就地下载 → 自动续跑导入 → 素材入库。
 * 这条链路里管线必须真的做一次分割，所以喂进去的不能是已经抠好的透明底图 ——
 * 那种图走的是手动入口，根本不碰模型。仓库里又没有可公开分发的真实照片，
 * 于是把已有的抠图贴回一块纯色背景上：对管线来说这就是一张普通商品图，
 * 它仍然要自己找出衣物边界、自己抠、自己过质量门，一步都没省。
 */
import fs from 'node:fs';
import path from 'node:path';

import { PNG } from './png';

const [, , source, destination] = process.argv;
if (!source || !destination) {
  console.error('usage: cere64-photo-fixture.ts <cutout.png> <out.png>');
  process.exit(1);
}

const cutout = PNG.read(fs.readFileSync(source));

// 留白：真实商品图不会把衣服顶到画布边上，边缘贴死会让分割结果被裁掉。
const pad = Math.round(Math.max(cutout.width, cutout.height) * 0.12);
const width = cutout.width + pad * 2;
const height = cutout.height + pad * 2;
const data = new Uint8Array(width * height * 4);

// 冷灰背景：和素材本身的颜色拉开，又不像纯白那样和衣服的高光糊在一起。
const BACKGROUND = [0xd8, 0xda, 0xdd];
for (let i = 0; i < width * height; i++) {
  data[i * 4] = BACKGROUND[0];
  data[i * 4 + 1] = BACKGROUND[1];
  data[i * 4 + 2] = BACKGROUND[2];
  data[i * 4 + 3] = 255;
}

for (let y = 0; y < cutout.height; y++) {
  for (let x = 0; x < cutout.width; x++) {
    const from = (y * cutout.width + x) * 4;
    const alpha = cutout.data[from + 3] / 255;
    if (alpha <= 0) continue;
    const to = ((y + pad) * width + (x + pad)) * 4;
    for (let channel = 0; channel < 3; channel++) {
      data[to + channel] = Math.round(
        cutout.data[from + channel] * alpha + data[to + channel] * (1 - alpha),
      );
    }
  }
}

fs.mkdirSync(path.dirname(path.resolve(destination)), { recursive: true });
fs.writeFileSync(destination, PNG.write(width, height, data));
console.log(`[cere64] ${destination} ${width}x${height}`);
