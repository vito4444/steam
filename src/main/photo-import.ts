import path from 'node:path';

import type { Category } from '../shared/spec';
import type { ManualImportFailure } from '../shared/ipc';

export interface PipelineAssetRecord {
  asset_id: string;
  category: string;
  auto_file: string | null;
  admission: { allowed: boolean };
}

export interface PipelineMetadata {
  assets?: PipelineAssetRecord[];
}

export interface AdmittedPipelineAsset {
  id: string;
  category: Category;
  file: string;
}

const PIPELINE_CATEGORY: Record<string, Category> = {
  'upper-body': 'top',
  bottoms: 'bottom',
  outerwear: 'outer',
  'dress-or-full-body': 'dress',
  shoes: 'shoe',
  bag: 'bag',
  accessory: 'other',
  other: 'other',
};

/** Fail closed: only CERE-12 records with allowed=true and a safe auto_file may enter the wardrobe. */
export function admittedPipelineAssets(metadata: PipelineMetadata, importRoot: string): AdmittedPipelineAsset[] {
  const root = path.resolve(importRoot);
  return (metadata.assets ?? []).flatMap((record) => {
    if (!record.admission?.allowed || !record.auto_file) return [];
    const file = path.resolve(root, record.auto_file);
    const relative = path.relative(root, file);
    if (relative.startsWith('..') || path.isAbsolute(relative)) {
      throw new Error(`pipeline output escapes import directory: ${record.auto_file}`);
    }
    return [{
      id: record.asset_id,
      category: PIPELINE_CATEGORY[record.category] ?? 'other',
      file,
    }];
  });
}

interface LinkImportDecisionInput {
  status: 'ok' | 'partial' | 'manual_required';
  image: { path: string } | null;
  error?: { message: string };
}

export type LinkImportDecision =
  | { action: 'pipeline'; imagePath: string }
  | { action: 'manual'; message: string };

export function decideLinkImport(result: LinkImportDecisionInput): LinkImportDecision {
  if (result.status !== 'manual_required' && result.image?.path) {
    return { action: 'pipeline', imagePath: result.image.path };
  }
  return {
    action: 'manual',
    message: result.error?.message ?? '没有取得可用商品主图，请保存图片后使用手动导入。',
  };
}

/** Keep renderer-visible failures useful without exposing the selected absolute path. */
export function manualImportFailure(file: string, error: unknown): ManualImportFailure {
  const basename = file.split(/[\\/]/).filter(Boolean).at(-1) ?? 'unknown-file';
  let message = error instanceof Error ? error.message : String(error);
  for (const candidate of new Set([
    file,
    file.replaceAll('\\', '/'),
    file.replaceAll('/', '\\'),
  ])) {
    message = message.replaceAll(candidate, basename);
  }
  return { file: basename, message: message.trim() || '导入失败' };
}
