import { describe, expect, it, vi } from 'vitest';

import { assetUrlToDataUrl, categoryForTryOn } from '../src/renderer/src/tryon/cloud';

describe('renderer cloud request helpers', () => {
  it('maps every PixelFit category to a provider-facing category', () => {
    expect(categoryForTryOn('dress')).toBe('dress');
    expect(categoryForTryOn('outer')).toBe('outer');
    expect(categoryForTryOn('shoe')).toBe('shoe');
    expect(categoryForTryOn('headwear')).toBe('accessory');
    expect(categoryForTryOn('eyewear')).toBe('accessory');
  });

  it('converts a local asset response to a data URI without exposing its path to IPC', async () => {
    const fetchFn = vi.fn(async () => new Response(Uint8Array.from([137, 80, 78, 71]), {
      status: 200,
      headers: { 'content-type': 'image/png' },
    }));

    const result = await assetUrlToDataUrl('pf://asset/cutout.png', fetchFn as typeof fetch);

    expect(result).toBe('data:image/png;base64,iVBORw==');
    expect(fetchFn).toHaveBeenCalledWith('pf://asset/cutout.png');
  });
});
