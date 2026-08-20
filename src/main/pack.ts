/**
 * 素材包导入。
 *
 * CERE-10 产真实照片素材，应用侧只负责把它们收进衣橱 —— 这里是那个入口。
 * 一个素材包就是一个目录：
 *
 *   pack/
 *     index.json
 *     <各件的透明底 PNG>
 *
 * index.json：
 *   {
 *     "pack": "cere10-real-v1",
 *     "items": [
 *       {
 *         "id": "itm_XXX",            // 可选，缺省自动生成
 *         "name": "藏青条纹长袖",
 *         "category": "top",          // 见 spec.ts 的 Category
 *         "cutout": "navy_stripe.png",
 *         "landmarks": {              // 可选，位图自身坐标；给了贴合更准
 *           "shoulder_l": { "x": 120, "y": 60 },
 *           "shoulder_r": { "x": 640, "y": 62 },
 *           "hem":        { "x": 380, "y": 900 }
 *         },
 *         "attributes": { "sleeve": "long", "pattern": "stripe" },
 *         "palette":    { "dominant": "#2C3E58", "color_family": "blue" },
 *         "tags": ["条纹"], "source_photo": "orig.jpg"
 *       }
 *     ]
 *   }
 *
 * 导入时**不做任何风格化**：不描边、不量化、不改色。只做两件几何上必需的事
 *   1. 裁到 alpha 包围盒 —— 素材周围的透明留白会让「按宽度贴合」整体失准
 *   2. 生成缩略图
 */

import { nativeImage } from 'electron';
import fsp from 'node:fs/promises';
import path from 'node:path';

import { CATEGORY_SLOT, SLOT_ANCHOR, SLOT_PLACEMENT, CANVAS } from '../shared/spec';
import type { Category, Slot } from '../shared/spec';
import type { AssetMeta, GarmentLandmark } from '../shared/types';
import { Store, newId } from './storage';

interface PackItem {
  id?: string;
  name?: string;
  category?: Category;
  cutout: string;
  landmarks?: Partial<Record<GarmentLandmark, { x: number; y: number }>>;
  attributes?: AssetMeta['attributes'];
  palette?: { dominant?: string; color_family?: string };
  tags?: string[];
  subcategory?: string;
  source_photo?: string;
}

export interface PackResult {
  imported: number;
  failed: { file: string; error: string }[];
  pack: string;
}

export async function importPack(store: Store, dir: string): Promise<PackResult> {
  const index = JSON.parse(await fsp.readFile(path.join(dir, 'index.json'), 'utf8')) as {
    pack?: string;
    demo?: boolean;
    items: PackItem[];
  };
  const result: PackResult = { imported: 0, failed: [], pack: index.pack ?? path.basename(dir) };

  for (const item of index.items ?? []) {
    try {
      await importItem(store, dir, item, result.pack, index.demo === true);
      result.imported++;
    } catch (err) {
      result.failed.push({ file: item.cutout, error: String(err) });
    }
  }
  return result;
}

async function importItem(store: Store, dir: string, item: PackItem, pack: string, demo: boolean): Promise<void> {
  const buf = await fsp.readFile(path.join(dir, item.cutout));
  const img = nativeImage.createFromBuffer(buf);
  if (img.isEmpty()) throw new Error('unsupported image');

  const box = alphaBounds(img);
  if (!box) throw new Error('fully transparent cutout');
  const cropped = img.crop(box);
  const size = cropped.getSize();

  const category = (item.category ?? 'top') as Category;
  const slot: Slot = CATEGORY_SLOT[category] ?? 'top';
  const rule = SLOT_PLACEMENT[slot];
  const anchorY = rule.edge === 'top' ? 0 : rule.edge === 'bottom' ? size.height : size.height / 2;

  // landmarks 是原图坐标，裁剪之后要跟着平移
  const given = item.landmarks
    ? Object.fromEntries(
      Object.entries(item.landmarks).map(([k, v]) => [k, { x: v.x - box.x, y: v.y - box.y }]),
    ) as NonNullable<AssetMeta['landmarks']>
    : {};
  // 素材包没给上下沿时自己量一遍稳健值（抠图碎屑不算数）
  const measured = measureLandmarks(cropped);
  const landmarks: AssetMeta['landmarks'] = { ...measured, ...given };
  const landmarksGiven = Object.keys(given) as GarmentLandmark[];

  const thumbSide = 256;
  const k = thumbSide / Math.max(size.width, size.height);
  const thumb = cropped.resize({
    width: Math.max(Math.round(size.width * k), 1),
    height: Math.max(Math.round(size.height * k), 1),
    quality: 'best',
  });

  const id = item.id ?? newId('itm');
  const now = new Date().toISOString();
  const meta: AssetMeta = {
    schema_version: 3,
    id,
    name: item.name ?? path.basename(item.cutout, path.extname(item.cutout)),
    category,
    subcategory: item.subcategory ?? '',
    slot,
    z_offset: 0,
    occupies: category === 'dress' ? ['top', 'bottom'] : [],
    companion: null,
    pose: 'front_idle',
    canvas: { w: CANVAS.w, h: CANVAS.h },
    bitmap: { file: 'cutout.png', w: size.width, h: size.height },
    source_resolution: { w: img.getSize().width, h: img.getSize().height },
    anchor: { base: SLOT_ANCHOR[slot], x: Math.round(size.width / 2), y: Math.round(anchorY) },
    landmarks,
    landmarks_given: landmarksGiven,
    fit: { scale: 1, dx: 0, dy: 0, stretch_x: 1 },
    palette: {
      dominant: item.palette?.dominant ?? '#8A8A90',
      color_family: (item.palette?.color_family ?? 'multi') as AssetMeta['palette']['color_family'],
      colors: [{ hex: item.palette?.dominant ?? '#8A8A90', ratio: 1, role: 'mid' }],
    },
    attributes: item.attributes ?? {},
    tags: item.tags ?? [],
    season: [],
    source: {
      origin: demo ? 'bundle' : 'manual',
      demo,
      photo_id: null,
      photo_file: item.source_photo ?? null,
      bbox: [box.x, box.y, box.width, box.height],
      imported_at: now,
    },
    provenance: {
      model: pack,
      model_version: '1',
      confidence: 1,
      edited_by_user: false,
      edit_ops: ['crop_to_alpha'],
    },
    files: { original: null, cutout: 'cutout.png', thumb: 'thumb.png' },
    favorite: false,
    wear_count: 0,
    created_at: now,
    updated_at: now,
  };

  await store.writeAsset(meta, { cutout: cropped.toPNG(), thumb: thumb.toPNG() });
}

/**
 * 从抠图自身量出贴合要用的关键点。
 *
 * 两个坑都在这里堵掉：
 *   1. 直接拿 alpha 包围盒的上下边会被抠图碎屑带偏 —— 领口上方飘着几十像素
 *      的头发残留，衣服就整体往下掉半个身位。按行统计跨度，只认跨度达到最大
 *      跨度 25% 的行。
 *   2. 直接拿包围盒宽度当「衣服有多宽」也不对 —— 摊平拍的裤子两条腿是分开
 *      的，包围盒比腰围宽得多；夹克袖子摊开也比肩宽得多。所以肩宽量的是上沿
 *      往下 12% 那一行，腰宽量的是上沿往下 4% 那一行。
 */
function measureLandmarks(img: Electron.NativeImage): NonNullable<AssetMeta['landmarks']> {
  const { width, height } = img.getSize();
  const bmp = img.toBitmap();
  const lo: number[] = new Array(height).fill(-1);
  const hi: number[] = new Array(height).fill(-1);
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      if (bmp[(y * width + x) * 4 + 3] > 16) {
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

  const rowAt = (frac: number) => {
    const y = Math.min(Math.max(Math.round(top + (hem - top) * frac), 0), height - 1);
    // 单行容易踩到破洞，取邻近若干行里跨度的中位数那一行
    const cand: number[] = [];
    for (let d = -3; d <= 3; d++) {
      const yy = Math.min(Math.max(y + d, 0), height - 1);
      if (span(yy) > 0) cand.push(yy);
    }
    if (cand.length === 0) return y;
    cand.sort((a, b) => span(a) - span(b));
    return cand[Math.floor(cand.length / 2)];
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

/** alpha 包围盒。素材周围的透明留白不裁掉，「按身体宽度缩放」会整体偏小 */
function alphaBounds(img: Electron.NativeImage): Electron.Rectangle | null {
  const { width, height } = img.getSize();
  const bmp = img.toBitmap(); // BGRA
  let x0 = width;
  let y0 = height;
  let x1 = -1;
  let y1 = -1;
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      if (bmp[(y * width + x) * 4 + 3] > 16) {
        if (x < x0) x0 = x;
        if (x > x1) x1 = x;
        if (y < y0) y0 = y;
        if (y > y1) y1 = y;
      }
    }
  }
  if (x1 < 0) return null;
  const pad = 2;
  x0 = Math.max(x0 - pad, 0);
  y0 = Math.max(y0 - pad, 0);
  x1 = Math.min(x1 + pad, width - 1);
  y1 = Math.min(y1 + pad, height - 1);
  return { x: x0, y: y0, width: x1 - x0 + 1, height: y1 - y0 + 1 };
}
