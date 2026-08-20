/**
 * 应用自带素材的定位。
 *
 * assets/base/  写实模特底图（CERE-6 交付的底图包；CERE-10 的真人底图落到
 *               library/base/ 之后自动接管）
 *
 * 手绘占位衣物已整体删除 —— 方向变更后衣橱里只放真实照片素材，
 * 留着手绘件只会让人以为成品就长那样。
 */

import { app } from 'electron';
import fs from 'node:fs';
import path from 'node:path';

export function assetsDir(): string {
  const candidates = [
    path.join(app.getAppPath(), 'assets'),
    path.join(process.resourcesPath ?? '', 'assets'),
    path.join(__dirname, '..', '..', 'assets'),
  ];
  for (const c of candidates) {
    if (c && fs.existsSync(path.join(c, 'base', 'base_f02', 'manifest.json'))) return c;
  }
  return candidates[0];
}

export function baseAssetsDir(): string {
  return path.join(assetsDir(), 'base');
}
