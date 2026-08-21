import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';

import { afterEach, describe, expect, it, vi } from 'vitest';

import { ensureBundledWardrobe } from '../src/main/bundled';

const roots: string[] = [];

async function fixture() {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'pixelfit-bundle-'));
  roots.push(root);
  const bundle = path.join(root, 'bundle');
  await fs.mkdir(bundle, { recursive: true });
  await fs.writeFile(path.join(bundle, 'index.json'), JSON.stringify({
    pack: 'cere13-v1',
    items: [{ id: 'item-a', cutout: 'a.png' }, { id: 'item-b', cutout: 'b.png' }],
  }));
  const assetDir = (id: string) => path.join(root, 'library', 'assets', id);
  const deleteAsset = async (id: string) => {
    await fs.rm(assetDir(id), { recursive: true, force: true });
  };
  return { root, bundle, assetDir, deleteAsset };
}

afterEach(async () => {
  await Promise.all(roots.splice(0).map((root) => fs.rm(root, { recursive: true, force: true })));
});

describe('ensureBundledWardrobe', () => {
  it('imports once and records a marker only after a complete import', async () => {
    const store = await fixture();
    const importer = vi.fn(async () => {
      for (const id of ['item-a', 'item-b']) {
        await fs.mkdir(store.assetDir(id), { recursive: true });
        await fs.writeFile(path.join(store.assetDir(id), 'meta.json'), '{}');
      }
      return { pack: 'cere13-v1', imported: 2, failed: [] };
    });

    expect(await ensureBundledWardrobe(store, store.bundle, importer)).toMatchObject({ seeded: true, imported: 2 });
    expect(await ensureBundledWardrobe(store, store.bundle, importer)).toMatchObject({ seeded: false, imported: 0 });
    expect(importer).toHaveBeenCalledTimes(1);
  });

  it('re-seeds when a bundled item has been deleted', async () => {
    const store = await fixture();
    const importer = vi.fn(async () => {
      for (const id of ['item-a', 'item-b']) {
        await fs.mkdir(store.assetDir(id), { recursive: true });
        await fs.writeFile(path.join(store.assetDir(id), 'meta.json'), '{}');
      }
      return { pack: 'cere13-v1', imported: 2, failed: [] };
    });

    await ensureBundledWardrobe(store, store.bundle, importer);
    await fs.rm(store.assetDir('item-b'), { recursive: true, force: true });
    expect(await ensureBundledWardrobe(store, store.bundle, importer)).toMatchObject({ seeded: true });
    expect(importer).toHaveBeenCalledTimes(2);
  });

  it('does not mark a partial import as complete', async () => {
    const store = await fixture();
    const importer = vi.fn(async () => ({
      pack: 'cere13-v1', imported: 1, failed: [{ file: 'b.png', error: 'broken' }],
    }));

    const result = await ensureBundledWardrobe(store, store.bundle, importer);
    expect(result.seeded).toBe(false);
    await expect(fs.access(path.join(store.root, '.bundled-wardrobe.json'))).rejects.toThrow();
  });

  it('migrates only stale IDs from the previous bundled marker', async () => {
    const store = await fixture();
    await fs.writeFile(path.join(store.bundle, 'index.json'), JSON.stringify({
      pack: 'pixelfit-demo-daily-v2',
      items: [{ id: 'kept', cutout: 'kept.png' }],
    }));
    const previousMarker = { pack: 'pixelfit-cere13-real-photo-v1', items: ['kept', 'historic'] };
    await fs.writeFile(path.join(store.root, '.bundled-wardrobe.json'), JSON.stringify(previousMarker));
    for (const id of ['kept', 'historic', 'user-photo']) {
      await fs.mkdir(store.assetDir(id), { recursive: true });
      await fs.writeFile(path.join(store.assetDir(id), 'meta.json'), '{}');
    }
    const importer = vi.fn(async () => ({ pack: 'pixelfit-demo-daily-v2', imported: 0, failed: [] }));

    const result = await ensureBundledWardrobe(store, store.bundle, importer);

    expect(result).toMatchObject({ seeded: true, imported: 0, removed: 1, failed: [], pack: 'pixelfit-demo-daily-v2' });
    await expect(fs.access(store.assetDir('kept'))).resolves.toBeUndefined();
    await expect(fs.access(store.assetDir('historic'))).rejects.toThrow();
    await expect(fs.access(store.assetDir('user-photo'))).resolves.toBeUndefined();
    await expect(fs.readFile(path.join(store.root, '.bundled-wardrobe.json'), 'utf8')).resolves.toContain(
      'pixelfit-demo-daily-v2',
    );
  });

  it('does not advance the marker when stale-demo deletion fails', async () => {
    const store = await fixture();
    await fs.writeFile(path.join(store.bundle, 'index.json'), JSON.stringify({
      pack: 'pixelfit-demo-daily-v2',
      items: [{ id: 'kept', cutout: 'kept.png' }],
    }));
    const previousMarker = { pack: 'pixelfit-cere13-real-photo-v1', items: ['kept', 'historic'] };
    await fs.writeFile(path.join(store.root, '.bundled-wardrobe.json'), JSON.stringify(previousMarker));
    for (const id of ['kept', 'historic', 'user-photo']) {
      await fs.mkdir(store.assetDir(id), { recursive: true });
      await fs.writeFile(path.join(store.assetDir(id), 'meta.json'), '{}');
    }
    const deleteAsset = async (id: string) => {
      if (id === 'historic') throw new Error('cannot remove historic');
      await fs.rm(store.assetDir(id), { recursive: true, force: true });
    };
    const failingStore = { ...store, deleteAsset };
    const importer = vi.fn(async () => ({ pack: 'pixelfit-demo-daily-v2', imported: 0, failed: [] }));

    await expect(ensureBundledWardrobe(failingStore, store.bundle, importer)).rejects.toThrow('cannot remove historic');
    await expect(fs.readFile(path.join(store.root, '.bundled-wardrobe.json'), 'utf8')).resolves.toBe(
      JSON.stringify(previousMarker),
    );
  });
});
