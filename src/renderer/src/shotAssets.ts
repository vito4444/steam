import { resolveRecipeAssetId } from '@shared/shots';
import type { Asset } from '@shared/types';

export const currentShotAssetId = resolveRecipeAssetId;

/** A deterministic evidence recipe is invalid when even one requested asset is absent. */
export function resolveShotAssets(ids: string[], assets: readonly Asset[]): Asset[] {
  return ids.map((recipeId) => {
    const resolvedId = currentShotAssetId(recipeId);
    const asset = assets.find((candidate) => candidate.id === resolvedId);
    if (!asset) throw new Error(`Missing shot asset ${recipeId} (resolved to ${resolvedId})`);
    return asset;
  });
}