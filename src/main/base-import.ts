/**
 * 「上传模特照片」的主进程实现（CERE-28）。
 *
 * 成员的原话是「不用去找文件夹修改」。以前换底图要自己抠图、自己写
 * `manifest.json`、自己把包解压到 `library/base/<体型>/`，这条路对着代码写文档
 * 也没用 —— 它就不是给人走的。
 *
 * 现在整条链路是：选图 → 本地抠人（BiRefNet，CPU，不上传）→ 裁掉多余留白 →
 * 量剪影推 17 个锚点 → 写底图包 → 立刻接管。全程没有一步要求用户打开图片编辑器。
 */

import { nativeImage } from 'electron';
import fsp from 'node:fs/promises';
import fs from 'node:fs';
import path from 'node:path';

import { deriveAnchors, type Silhouette } from '../shared/base-anchors';
import type { AnchorName, BodyType, Point } from '../shared/spec';

export interface BasePhotoImportResult {
  pack: string;
  canvas: { w: number; h: number };
  anchors: Record<AnchorName, Point>;
  /** true = 剪影量不出来，锚点用的是按画布缩放的兜底值 */
  fallbackAnchors: boolean;
  notes: string[];
  visibleRatio: number;
}

interface SubjectCutout {
  file: string;
  width: number;
  height: number;
  visible_ratio: number;
}

/** nativeImage 给的是 BGRA，我们只要 alpha 那一路。 */
export function alphaFromBitmap(bitmap: Buffer, width: number, height: number): Uint8Array {
  const alpha = new Uint8Array(width * height);
  for (let i = 0; i < alpha.length; i++) alpha[i] = bitmap[i * 4 + 3];
  return alpha;
}

export function readSilhouette(file: string): Silhouette {
  const image = nativeImage.createFromPath(file);
  const { width, height } = image.getSize();
  if (width < 1 || height < 1) throw new Error('BASE_IMAGE_UNREADABLE: 抠好的底图读不出尺寸');
  return { width, height, alpha: alphaFromBitmap(image.toBitmap(), width, height) };
}

/**
 * 底图包 manifest。
 *
 * 刻意不写 `mask` 字段：渲染层没有正式遮罩时会回落到人体层自己的 alpha
 * （见 `render/mask.ts`），而照片抠出来的 alpha 本身就是人体轮廓 —— 再单独存
 * 一份一模一样的灰度图只是浪费磁盘。
 *
 * `tones` 同理留空：一张真人照片只有一档肤色，内置的 F02 底图包也是这么写的。
 */
export function buildBaseManifest(input: {
  pack: string;
  canvas: { w: number; h: number };
  anchors: Record<AnchorName, Point>;
  bodyFile: string;
}): Record<string, unknown> {
  return {
    pack: input.pack,
    canvas: input.canvas,
    layers: [{ file: input.bodyFile, z: 20, mode: 'normal' }],
    tones: [],
    hair: {},
    anchors: input.anchors,
  };
}

export interface BasePhotoDeps {
  /** 调本地管线把人抠出来，返回透明底 PNG 的路径 */
  cutoutSubject(imagePath: string, destination: string): Promise<SubjectCutout>;
  /** 读 PNG 的 alpha 通道 */
  readSilhouette(file: string): Silhouette;
  /** library/base 的位置 */
  baseLibraryDir: string;
  /** 临时目录 */
  tmpDir: string;
}

export async function importBasePhoto(
  body: BodyType,
  sourceFile: string,
  deps: BasePhotoDeps,
): Promise<BasePhotoImportResult> {
  const stamp = Date.now().toString(36);
  const staging = path.join(deps.tmpDir, `base-import-${stamp}`);
  await fsp.mkdir(staging, { recursive: true });
  const cutoutFile = path.join(staging, 'body.png');

  try {
    const cutout = await deps.cutoutSubject(sourceFile, cutoutFile);
    const silhouette = deps.readSilhouette(cutout.file);
    const derived = deriveAnchors(silhouette);

    const target = path.join(deps.baseLibraryDir, body);
    // 整目录替换：留着上一版底图的散图会让 manifest 和实际文件对不上。
    await fsp.rm(target, { recursive: true, force: true });
    await fsp.mkdir(target, { recursive: true });
    await fsp.copyFile(cutout.file, path.join(target, 'body.png'));

    const pack = `photo-${body}-${stamp}`;
    const manifest = buildBaseManifest({
      pack,
      canvas: { w: silhouette.width, h: silhouette.height },
      anchors: derived.anchors,
      bodyFile: 'body.png',
    });
    await fsp.writeFile(
      path.join(target, 'manifest.json'),
      `${JSON.stringify(manifest, null, 2)}\n`,
      'utf8',
    );

    const notes = [...derived.notes];
    if (cutout.visible_ratio > 0.85) {
      notes.push('人物几乎占满整张图，背景可能没有完全去掉');
    }
    return {
      pack,
      canvas: { w: silhouette.width, h: silhouette.height },
      anchors: derived.anchors,
      fallbackAnchors: derived.fallback,
      notes,
      visibleRatio: cutout.visible_ratio,
    };
  } finally {
    await fsp.rm(staging, { recursive: true, force: true }).catch(() => undefined);
  }
}

/** 删掉上传的底图包，回到应用内置的写实模特。 */
export async function resetBasePhoto(body: BodyType, baseLibraryDir: string): Promise<boolean> {
  const target = path.join(baseLibraryDir, body);
  if (!fs.existsSync(path.join(target, 'manifest.json'))) return false;
  await fsp.rm(target, { recursive: true, force: true });
  return true;
}
