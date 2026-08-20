import path from 'node:path';

import { describe, expect, it } from 'vitest';

import * as photoImport from '../src/main/photo-import';

const { admittedPipelineAssets, decideLinkImport } = photoImport;

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
