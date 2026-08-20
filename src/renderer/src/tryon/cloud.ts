import type { Category } from '@shared/spec';
import type { Asset } from '@shared/types';
import type { TryOnCategory, TryOnGarment } from '@shared/tryon';

export function categoryForTryOn(category: Category): TryOnCategory {
  if (category === 'dress') return 'dress';
  if (category === 'bottom') return 'bottom';
  if (category === 'top') return 'top';
  if (category === 'outer') return 'outer';
  if (category === 'shoe') return 'shoe';
  if (category === 'bag') return 'bag';
  return 'accessory';
}

export async function assetUrlToDataUrl(
  url: string,
  fetchFn: typeof fetch = fetch,
): Promise<string> {
  const response = await fetchFn(url);
  if (!response.ok) throw new Error(`读取衣物图片失败：${response.status}`);
  const contentType = response.headers.get('content-type')?.split(';')[0] || 'image/png';
  const bytes = new Uint8Array(await response.arrayBuffer());
  let binary = '';
  const chunkSize = 0x8000;
  for (let offset = 0; offset < bytes.length; offset += chunkSize) {
    binary += String.fromCharCode(...bytes.subarray(offset, offset + chunkSize));
  }
  return `data:${contentType};base64,${btoa(binary)}`;
}

export async function assetToTryOnGarment(asset: Asset): Promise<TryOnGarment> {
  return {
    id: asset.id,
    name: asset.name,
    category: categoryForTryOn(asset.category),
    imageDataUrl: await assetUrlToDataUrl(asset.cutoutUrl),
  };
}
