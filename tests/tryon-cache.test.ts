import fsp from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { describe, expect, it } from 'vitest';

import { TryOnDiskCache } from '../src/main/tryon/cache';
import type { CloudTryOnProvider } from '../src/main/tryon/provider';
import type { TryOnGenerateRequest } from '@shared/tryon';

const request: TryOnGenerateRequest = {
  clientRequestId: 'client-1',
  consent: true,
  baseImageDataUrl: 'data:image/png;base64,cGVyc29u',
  garments: [{
    id: 'top-1',
    name: 'Top',
    category: 'top',
    imageDataUrl: 'data:image/png;base64,Z2FybWVudA==',
  }],
};

function provider(cacheIdentity: string): CloudTryOnProvider {
  return {
    id: 'fashn',
    name: 'FASHN',
    cacheIdentity,
    profile: {
      id: 'fashn', label: 'FASHN',
      supportedCategories: ['top'], usdPerStep: 0.075, creditsPerStep: 1,
      secondsPerStep: { min: 10, max: 10 },
    },
    configured: true,
    generate: async () => ({ imageDataUrl: '', providerRequestIds: [], creditsUsed: 0 }),
  };
}

describe('TryOnDiskCache', () => {
  it('changes the content key when provider model options change', async () => {
    const root = await fsp.mkdtemp(path.join(os.tmpdir(), 'pixelfit-cache-test-'));
    const cache = new TryOnDiskCache(root);

    expect(cache.keyFor(request, provider('tryon-max:fast:1k')))
      .not.toBe(cache.keyFor(request, provider('tryon-max:quality:4k')));
  });

  it('round-trips JPEG provider output instead of failing after a billable call', async () => {
    const root = await fsp.mkdtemp(path.join(os.tmpdir(), 'pixelfit-cache-test-'));
    const cache = new TryOnDiskCache(root);
    const value = 'data:image/jpeg;base64,/9j/AA==';

    await cache.set('jpeg-output', value);

    expect(await cache.get('jpeg-output')).toBe(value);
  });
});
