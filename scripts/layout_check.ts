/**
 * 自动布局的无遮挡回归检查。
 *
 * 需求第一条验收是「每件单品从上至下清晰展示、互不重叠」。靠眼睛看几张截图
 * 证明不了这件事 —— 素材的长宽比千奇百怪（一条阔腿裤 900×2600，一副耳环 1100×400），
 * 所以这里随机生成上千种组合，跑真正的 `autoLayout`，再用真正的 `findOverlaps` 判定。
 *
 *   npm run check:layout
 */

import { findOverlaps, BOARD_CANVAS, itemBounds, type BoardItem } from '../src/shared/board.ts';
import { autoLayout } from '../src/renderer/src/features/board/layout.ts';
import type { Category } from '../src/shared/spec.ts';
import type { Asset } from '../src/shared/types.ts';

const CATEGORIES: Category[] = [
  'top', 'bottom', 'dress', 'outer', 'shoe', 'bag',
  'headwear', 'eyewear', 'neckwear', 'belt', 'gloves', 'legwear', 'underlayer', 'other',
];

function rng(seed: number): () => number {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

function fakeAsset(id: string, category: Category, w: number, h: number): Asset {
  return {
    id, name: id, category,
    bitmap: { file: 'cutout.png', w, h },
  } as unknown as Asset;
}

function run(): number {
  const rnd = rng(20260821);
  let failures = 0;
  let checked = 0;
  let worstFill = 1;

  for (let trial = 0; trial < 2000; trial++) {
    const n = 1 + Math.floor(rnd() * 9); // 1–9 件
    const assets: Asset[] = [];
    for (let i = 0; i < n; i++) {
      const cat = CATEGORIES[Math.floor(rnd() * CATEGORIES.length)];
      // 极端长宽比也要覆盖：0.2（细长）到 4.5（扁平）
      const aspect = 0.2 + rnd() * 4.3;
      const h = 400 + rnd() * 2400;
      assets.push(fakeAsset(`a${i}`, cat, Math.round(h * aspect), Math.round(h)));
    }
    const hasTitle = rnd() < 0.4;
    const placed = autoLayout(assets, { hasTitle, wearOverlap: 0 });

    const items: BoardItem[] = assets
      .filter((a) => placed.has(a.id))
      .map((a, i) => {
        const p = placed.get(a.id)!;
        return {
          id: `i${i}`, kind: 'asset', assetId: a.id,
          x: p.x, y: p.y, w: p.w, h: p.h, rotation: 0, z: i + 1,
        };
      });

    checked += items.length;
    const clashes = findOverlaps(items);
    if (clashes.length) {
      failures++;
      if (failures <= 3) {
        console.error(`[fail] trial ${trial}: ${clashes.length} 处重叠`,
          assets.map((a) => `${a.category} ${a.bitmap.w}x${a.bitmap.h}`));
      }
    }

    // 越界检查：单品不能被排到画布外
    for (const it of items) {
      const b = itemBounds(it);
      if (b.x1 < -1 || b.y1 < -1 || b.x2 > BOARD_CANVAS.w + 1 || b.y2 > BOARD_CANVAS.h + 1) {
        failures++;
        if (failures <= 6) console.error(`[fail] trial ${trial}: 越界`, b);
        break;
      }
    }

    // 顺序检查：上装在上、下装在下、鞋在最下。
    // top 与 dress 同级（连衣裙占的就是上装那一格），并列时不比先后。
    const catOf = new Map(assets.map((a) => [a.id, a.category]));
    const RANK: Partial<Record<Category, number>> = {
      outer: 1, top: 2, dress: 2, underlayer: 3, bottom: 4, legwear: 5, shoe: 6,
    };
    const mains = items
      .filter((i) => RANK[catOf.get(i.assetId!)!] !== undefined)
      .sort((a, b) => a.y - b.y)
      .map((i) => catOf.get(i.assetId!)!);
    for (let i = 1; i < mains.length; i++) {
      if (RANK[mains[i]]! < RANK[mains[i - 1]]!) {
        failures++;
        if (failures <= 6) console.error(`[fail] trial ${trial}: 主列顺序反了`, mains);
        break;
      }
    }

    if (items.length >= 3) {
      const used = items.reduce((s, i) => s + i.w * i.h, 0) / (BOARD_CANVAS.w * BOARD_CANVAS.h);
      worstFill = Math.min(worstFill, used);
    }
  }

  console.log(`[layout] 2000 组随机搭配，共 ${checked} 件；最稀疏一版占画布 ${(worstFill * 100).toFixed(1)}%`);
  if (failures) {
    console.error(`[layout] 失败 ${failures} 项`);
    return 1;
  }
  console.log('[layout] 全部通过：无遮挡、无越界、主列顺序正确');
  return 0;
}

process.exit(run());
