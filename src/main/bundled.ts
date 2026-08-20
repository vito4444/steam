import fs from 'node:fs/promises';
import path from 'node:path';

import type { PackResult } from './pack';

interface SeedStore {
  root: string;
  assetDir(id: string): string;
  deleteAsset(id: string): Promise<void>;
}

interface BundleIndex {
  pack?: string;
  items?: Array<{ id?: string; cutout: string }>;
}

export interface BundledSeedResult {
  seeded: boolean;
  imported: number;
  removed: number;
  failed: PackResult['failed'];
  pack: string;
}

type PackImporter<TStore extends SeedStore> = (store: TStore, dir: string) => Promise<PackResult>;

async function exists(file: string): Promise<boolean> {
  try {
    await fs.access(file);
    return true;
  } catch {
    return false;
  }
}

/**
 * Seed the built-in demo wardrobe without overwriting user edits on every
 * launch. A deleted bundled item is restored on the next launch, while a
 * partial/failed import is deliberately left unmarked so it can retry later.
 */
export async function ensureBundledWardrobe<TStore extends SeedStore>(
  store: TStore,
  bundleDir: string,
  importer: PackImporter<TStore>,
): Promise<BundledSeedResult> {
  const index = JSON.parse(await fs.readFile(path.join(bundleDir, 'index.json'), 'utf8')) as BundleIndex;
  const pack = index.pack ?? path.basename(bundleDir);
  const ids = (index.items ?? []).map((item) => item.id).filter((id): id is string => !!id);
  const markerPath = path.join(store.root, '.bundled-wardrobe.json');

  let previousMarker: { pack?: string; items?: string[] } = {};
  try {
    previousMarker = JSON.parse(await fs.readFile(markerPath, 'utf8')) as { pack?: string; items?: string[] };
  } catch {
    // First launch or a stale/corrupt marker: import below.
  }
  const complete = ids.length > 0 && (await Promise.all(
    ids.map((id) => exists(path.join(store.assetDir(id), 'meta.json'))),
  )).every(Boolean);
  if (previousMarker.pack === pack && complete) {
    return { seeded: false, imported: 0, removed: 0, failed: [], pack };
  }

  const result = await importer(store, bundleDir);
  const importedComplete = result.failed.length === 0 && (await Promise.all(
    ids.map((id) => exists(path.join(store.assetDir(id), 'meta.json'))),
  )).every(Boolean);
  if (!importedComplete) {
    return { seeded: false, imported: result.imported, removed: 0, failed: result.failed, pack: result.pack };
  }

  const previousItems = previousMarker.items ?? [];
  const staleIds = previousItems.filter((id) => !ids.includes(id));
  for (const id of staleIds) await store.deleteAsset(id);

  const tmp = `${markerPath}.tmp`;
  await fs.writeFile(tmp, JSON.stringify({ pack, items: ids, seeded_at: new Date().toISOString() }, null, 2));
  await fs.rename(tmp, markerPath);
  return { seeded: true, imported: result.imported, removed: staleIds.length, failed: [], pack };
}
