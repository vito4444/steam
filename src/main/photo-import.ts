import path from 'node:path';

import type { Category } from '../shared/spec';
import type {
  ManualImportFailure,
  PhotoImportCandidate,
} from '../shared/ipc';

export interface PipelineAssetRecord {
  asset_id: string;
  category: string;
  preview_file?: string | null;
  auto_file: string | null;
  quarantine_file?: string | null;
  review_state?: 'approved' | 'needs_optimization' | 'retry';
  visibility?: 'complete' | 'partial';
  admission: { allowed: boolean; score?: number };
  quality?: {
    score?: number;
    reasons?: PipelineQualityReason[];
  };
}

export interface PipelineMetadata {
  assets?: PipelineAssetRecord[];
}

/**
 * Deterministic real-photo input for the Electron screenshot harness.
 *
 * This is deliberately strict and only consumed while the app runs with
 * `--shots`: normal member imports still come exclusively from the native
 * file picker.
 */
export function parseShotPhotoFixtures(raw: string | undefined): string[] {
  if (!raw?.trim()) return [];
  let parsed: unknown;
  try {
    parsed = JSON.parse(raw);
  } catch {
    throw new Error('PIXELFIT_PHOTO_IMPORT_FIXTURES must be valid JSON');
  }
  if (!Array.isArray(parsed)) {
    throw new Error('PIXELFIT_PHOTO_IMPORT_FIXTURES must be a JSON array');
  }
  const files = parsed.map((value) => {
    if (typeof value !== 'string' || !value.trim()) {
      throw new Error('PIXELFIT_PHOTO_IMPORT_FIXTURES must contain file paths');
    }
    const file = value.trim();
    if (!path.isAbsolute(file)) {
      throw new Error('PIXELFIT_PHOTO_IMPORT_FIXTURES paths must be absolute');
    }
    return path.normalize(file);
  });
  if (new Set(files.map((file) => file.toLowerCase())).size !== files.length) {
    throw new Error('PIXELFIT_PHOTO_IMPORT_FIXTURES contains duplicate paths');
  }
  return files;
}

export interface AdmittedPipelineAsset {
  id: string;
  category: Category;
  file: string;
}

export interface PipelineQualityReason {
  code: string;
  metric: string;
  value: number | boolean;
  threshold: number | boolean;
  message: string;
}

export type PipelineCandidateState = 'ready' | 'needs_optimization' | 'retry';

export interface PipelineCandidate {
  id: string;
  category: Category;
  file: string;
  importFile: string | null;
  importable: boolean;
  state: PipelineCandidateState;
  visibility: 'complete' | 'partial';
  score: number;
  reasons: PipelineQualityReason[];
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

function resolvePipelineFile(importRoot: string, relativeFile: string): string {
  const root = path.resolve(importRoot);
  const file = path.resolve(root, relativeFile);
  const relative = path.relative(root, file);
  if (relative.startsWith('..') || path.isAbsolute(relative)) {
    throw new Error(`pipeline output escapes import directory: ${relativeFile}`);
  }
  return file;
}

function candidateState(record: PipelineAssetRecord): PipelineCandidateState {
  if (record.review_state === 'needs_optimization') return 'needs_optimization';
  if (record.review_state === 'retry') return 'retry';
  return record.admission?.allowed ? 'ready' : 'retry';
}

/** Preserve inspectable previews while applying the same containment check to every path. */
export function collectPipelineCandidates(
  metadata: PipelineMetadata,
  importRoot: string,
): PipelineCandidate[] {
  return (metadata.assets ?? []).flatMap((record) => {
    const preview = record.preview_file ?? record.auto_file ?? record.quarantine_file;
    if (!preview) return [];
    const file = resolvePipelineFile(importRoot, preview);
    const importFile = record.admission?.allowed && record.auto_file
      ? resolvePipelineFile(importRoot, record.auto_file)
      : null;
    return [{
      id: record.asset_id,
      category: PIPELINE_CATEGORY[record.category] ?? 'other',
      file,
      importFile,
      importable: importFile !== null,
      state: candidateState(record),
      visibility: record.visibility ?? 'complete',
      score: record.admission?.score ?? record.quality?.score ?? 0,
      reasons: record.quality?.reasons ?? [],
    }];
  });
}

/** Compatibility projection for callers that only need importable assets. */
export function admittedPipelineAssets(metadata: PipelineMetadata, importRoot: string): AdmittedPipelineAsset[] {
  return collectPipelineCandidates(metadata, importRoot).flatMap((candidate) =>
    candidate.importFile
      ? [{ id: candidate.id, category: candidate.category, file: candidate.importFile }]
      : []);
}

export function toPhotoImportCandidate(
  candidate: Pick<
    PipelineCandidate,
    'id' | 'category' | 'file' | 'state' | 'visibility' | 'score' | 'reasons'
  >,
  toUrl: (file: string) => string,
): PhotoImportCandidate {
  return {
    id: candidate.id,
    category: candidate.category,
    previewUrl: toUrl(candidate.file),
    state: candidate.state,
    visibility: candidate.visibility,
    score: candidate.score,
    reasons: candidate.reasons,
  };
}

export function automaticImportTags(
  state: 'ready' | 'needs_optimization',
): string[] {
  return state === 'needs_optimization'
    ? ['照片识别', '待优化']
    : ['照片识别'];
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
