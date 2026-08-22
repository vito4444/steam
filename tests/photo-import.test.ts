import path from 'node:path';

import { describe, expect, it } from 'vitest';

import * as photoImport from '../src/main/photo-import';

const {
  admittedPipelineAssets,
  collectPipelineCandidates,
  decideLinkImport,
  settlePipelineCandidateImports,
} = photoImport;

describe('pipeline candidate persistence', () => {
  it('returns successful assets and sanitized failures when one candidate write fails', async () => {
    const root = path.resolve('private', 'pipeline-import');
    const candidates = [
      {
        id: 'upper', category: 'top', file: path.join(root, 'upper.png'),
        importFile: path.join(root, 'upper.png'), importable: true,
        state: 'ready', visibility: 'complete', score: 100, reasons: [],
      },
      {
        id: 'lower', category: 'bottom', file: path.join(root, 'lower.png'),
        importFile: path.join(root, 'lower.png'), importable: true,
        state: 'needs_optimization', visibility: 'complete', score: 75, reasons: [],
      },
    ] as Parameters<typeof settlePipelineCandidateImports<string>>[0];

    const result = await settlePipelineCandidateImports(candidates, async (candidate) => {
      if (candidate.id === 'lower') throw new Error(`cannot persist ${candidate.importFile}`);
      return candidate.id;
    });

    expect(result).toEqual({
      assets: ['upper'],
      failures: ['lower：cannot persist lower.png'],
      reviewStates: ['ready'],
    });
    expect(JSON.stringify(result)).not.toContain(root);
  });
});

describe('photo candidate renderer contract', () => {
  it('accepts only an explicit JSON list of absolute photo fixtures for shot mode', () => {
    const parseShotPhotoFixtures = (photoImport as unknown as {
      parseShotPhotoFixtures?: (raw: string | undefined) => string[];
    }).parseShotPhotoFixtures;
    expect(parseShotPhotoFixtures).toBeTypeOf('function');

    const flat = path.resolve('fixtures', 'flat.png');
    const street = path.resolve('fixtures', 'street.png');
    expect(parseShotPhotoFixtures?.(JSON.stringify([flat, street]))).toEqual([flat, street]);
    expect(parseShotPhotoFixtures?.(undefined)).toEqual([]);
    expect(() => parseShotPhotoFixtures?.('["relative.png"]')).toThrow(/absolute/i);
    expect(() => parseShotPhotoFixtures?.('{"file":"flat.png"}')).toThrow(/array/i);
  });

  it('maps a safe internal candidate to a renderer preview without exposing its path', () => {
    const toPhotoImportCandidate = (photoImport as unknown as {
      toPhotoImportCandidate?: (
        candidate: {
          id: string;
          category: 'top';
          file: string;
          state: 'needs_optimization';
          visibility: 'partial';
          score: number;
          reasons: [];
        },
        toUrl: (file: string) => string,
      ) => unknown;
    }).toPhotoImportCandidate;
    expect(toPhotoImportCandidate).toBeTypeOf('function');

    const internalPath = path.resolve('private', 'upper.png');
    const result = toPhotoImportCandidate?.({
      id: 'upper',
      category: 'top',
      file: internalPath,
      state: 'needs_optimization',
      visibility: 'partial',
      score: 75,
      reasons: [],
    }, () => 'pf://candidate/upper.png');

    expect(result).toEqual({
      id: 'upper',
      category: 'top',
      previewUrl: 'pf://candidate/upper.png',
      state: 'needs_optimization',
      visibility: 'partial',
      score: 75,
      reasons: [],
    });
    expect(JSON.stringify(result)).not.toContain(internalPath);
  });

  it('persists an explicit optimization tag only for affected automatic imports', () => {
    const automaticImportTags = (photoImport as unknown as {
      automaticImportTags?: (state: 'ready' | 'needs_optimization') => string[];
    }).automaticImportTags;
    expect(automaticImportTags).toBeTypeOf('function');
    expect(automaticImportTags?.('ready')).toEqual(['照片识别']);
    expect(automaticImportTags?.('needs_optimization')).toEqual(['照片识别', '待优化']);
  });
});

describe('collectPipelineCandidates', () => {
  it('keeps an optimization candidate importable and a retry candidate inspectable', () => {
    const root = path.resolve('tmp', 'candidate-import');
    const result = collectPipelineCandidates({
      assets: [
        {
          asset_id: 'upper',
          category: 'upper-body',
          preview_file: 'assets/upper-auto.png',
          auto_file: 'assets/upper-auto.png',
          review_state: 'needs_optimization',
          visibility: 'complete',
          admission: { allowed: true, score: 75 },
          quality: {
            reasons: [{
              code: 'MASK_STRUCTURE_UNRELIABLE',
              metric: 'input_contour_roughness',
              value: 0.24,
              threshold: 0.05,
              message: 'input mask has structural bites',
            }],
          },
        },
        {
          asset_id: 'lower',
          category: 'bottoms',
          preview_file: 'quarantine/lower-candidate.png',
          auto_file: null,
          review_state: 'retry',
          visibility: 'partial',
          admission: { allowed: false, score: 0 },
          quality: { reasons: [] },
        },
      ],
    }, root);

    expect(result).toEqual([
      {
        id: 'upper',
        category: 'top',
        file: path.join(root, 'assets', 'upper-auto.png'),
        importFile: path.join(root, 'assets', 'upper-auto.png'),
        importable: true,
        state: 'needs_optimization',
        visibility: 'complete',
        score: 75,
        reasons: [{
          code: 'MASK_STRUCTURE_UNRELIABLE',
          metric: 'input_contour_roughness',
          value: 0.24,
          threshold: 0.05,
          message: 'input mask has structural bites',
        }],
      },
      {
        id: 'lower',
        category: 'bottom',
        file: path.join(root, 'quarantine', 'lower-candidate.png'),
        importFile: null,
        importable: false,
        state: 'retry',
        visibility: 'partial',
        score: 0,
        reasons: [],
      },
    ]);
  });

  it('fails closed when admission metadata disagrees with the review state or output file', () => {
    const root = path.resolve('tmp', 'candidate-incoherent');
    const result = collectPipelineCandidates({
      assets: [
        {
          asset_id: 'missing-auto',
          category: 'upper-body',
          preview_file: 'quarantine/missing-auto.png',
          auto_file: null,
          review_state: 'needs_optimization',
          admission: { allowed: true, score: 75 },
        },
        {
          asset_id: 'retry-with-auto',
          category: 'bottoms',
          preview_file: 'quarantine/retry-with-auto.png',
          auto_file: 'assets/retry-with-auto.png',
          review_state: 'retry',
          admission: { allowed: true, score: 100 },
        },
      ],
    }, root);

    expect(result.map(({ id, importFile, importable, state }) => ({
      id, importFile, importable, state,
    }))).toEqual([
      { id: 'missing-auto', importFile: null, importable: false, state: 'retry' },
      { id: 'retry-with-auto', importFile: null, importable: false, state: 'retry' },
    ]);
  });

  it('rejects a retry preview that escapes the pipeline import directory', () => {
    const root = path.resolve('tmp', 'candidate-escape');
    expect(() => collectPipelineCandidates({
      assets: [{
        asset_id: 'lower',
        category: 'bottoms',
        preview_file: '../private.png',
        auto_file: null,
        review_state: 'retry',
        admission: { allowed: false, score: 0 },
      }],
    }, root)).toThrow(/escapes/i);
  });
});

describe('admittedPipelineAssets', () => {
  it('imports only explicitly admitted outputs and maps CERE-12 categories', () => {
    const root = path.resolve('tmp', 'import-1');
    const result = admittedPipelineAssets({
      assets: [
        { asset_id: 'upper', category: 'upper-body', auto_file: 'assets/upper-auto.png', admission: { allowed: true } },
        { asset_id: 'lower', category: 'bottoms', auto_file: null, admission: { allowed: false } },
        { asset_id: 'full', category: 'dress-or-full-body', auto_file: 'assets/full-auto.png', admission: { allowed: true } },
      ],
    }, root);

    expect(result).toEqual([
      { id: 'upper', category: 'top', file: path.join(root, 'assets', 'upper-auto.png') },
      { id: 'full', category: 'dress', file: path.join(root, 'assets', 'full-auto.png') },
    ]);
  });

  it('rejects an admitted path that escapes the pipeline import directory', () => {
    const root = path.resolve('tmp', 'import-2');
    expect(() => admittedPipelineAssets({
      assets: [{ asset_id: 'upper', category: 'upper-body', auto_file: '../escape.png', admission: { allowed: true } }],
    }, root)).toThrow(/escapes/i);
  });
});

describe('decideLinkImport', () => {
  it('routes a downloaded product image into the photo pipeline', () => {
    expect(decideLinkImport({ status: 'partial', image: { path: 'product.webp' } })).toEqual({
      action: 'pipeline',
      imagePath: 'product.webp',
    });
  });

  it('keeps login-wall failures manual instead of importing an opaque page image', () => {
    expect(decideLinkImport({
      status: 'manual_required',
      image: null,
      error: { message: '需要登录后手动选择商品主图' },
    })).toEqual({ action: 'manual', message: '需要登录后手动选择商品主图' });
  });
});

describe('manual import failure sanitization', () => {
  it('exposes a safe basename and never the selected absolute path', () => {
    const manualImportFailure = (photoImport as unknown as {
      manualImportFailure?: (file: string, error: unknown) => { file: string; message: string };
    }).manualImportFailure;
    expect(manualImportFailure).toBeTypeOf('function');

    const absolute = 'C:\\Users\\private\\wardrobe\\opaque.png';
    const failure = manualImportFailure?.(
      absolute,
      new Error(`ENOENT: could not read '${absolute}'`),
    );

    expect(failure).toEqual({
      file: 'opaque.png',
      message: "ENOENT: could not read 'opaque.png'",
    });
    expect(JSON.stringify(failure)).not.toContain('C:\\Users\\private');
  });
});
