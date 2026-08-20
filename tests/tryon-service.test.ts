import { describe, expect, it, vi } from 'vitest';

import { TryOnService, type TryOnCacheLike } from '../src/main/tryon/service';
import type { CloudTryOnProvider } from '../src/main/tryon/provider';
import type { TryOnGenerateRequest } from '@shared/tryon';

const request = (consent: boolean): TryOnGenerateRequest => ({
  clientRequestId: 'client-1',
  consent,
  baseImageDataUrl: 'data:image/png;base64,cGVyc29u',
  garments: [{
    id: 'top-1',
    name: 'Top',
    category: 'top',
    imageDataUrl: 'data:image/png;base64,Z2FybWVudA==',
  }],
});

function provider(configured = true): CloudTryOnProvider {
  return {
    id: 'fashn',
    name: 'FASHN Try-On Max',
    cacheIdentity: 'tryon-max:fast:1k',
    profile: {
      id: 'fashn',
      label: 'FASHN Try-On Max',
      supportedCategories: ['top', 'bottom', 'dress', 'outer', 'shoe', 'bag', 'accessory'],
      usdPerStep: 0.075,
      creditsPerStep: 1,
      secondsPerStep: { min: 10, max: 10 },
    },
    configured,
    missingConfiguration: configured ? undefined : 'FASHN_API_KEY 未配置',
    privacySummary: 'test privacy',
    generate: vi.fn(async () => ({
      imageDataUrl: 'data:image/png;base64,b3V0',
      providerRequestIds: ['p-1'],
      creditsUsed: 1,
      usageImageCount: 1,
      providerResponseRequestId: 'response-1',
    })),
  };
}

function memoryCache(hit: string | null = null): TryOnCacheLike {
  return {
    keyFor: vi.fn(() => 'cache-key'),
    get: vi.fn(async () => hit),
    set: vi.fn(async () => undefined),
  };
}

describe('TryOnService', () => {
  it('rejects a request unless consent is explicit', async () => {
    const cloud = provider();
    const service = new TryOnService(cloud, memoryCache());

    const result = await service.generate(request(false), new AbortController().signal);

    expect(result).toMatchObject({ ok: false, fallback: true, code: 'CONSENT_REQUIRED' });
    expect(cloud.generate).not.toHaveBeenCalled();
  });

  it('falls back locally when the selected provider has no key', async () => {
    const cloud = provider(false);
    const service = new TryOnService(cloud, memoryCache());

    const result = await service.generate(request(true), new AbortController().signal);

    expect(result).toMatchObject({ ok: false, fallback: true, code: 'NOT_CONFIGURED' });
    expect(cloud.generate).not.toHaveBeenCalled();
  });

  it('returns a cache hit without another paid generation', async () => {
    const cloud = provider();
    const service = new TryOnService(cloud, memoryCache('data:image/png;base64,Y2FjaGU='));

    const result = await service.generate(request(true), new AbortController().signal);

    expect(result).toMatchObject({
      ok: true,
      cached: true,
      imageDataUrl: 'data:image/png;base64,Y2FjaGU=',
    });
    expect(cloud.generate).not.toHaveBeenCalled();
  });

  it('turns provider errors into an explicit layered fallback', async () => {
    const cloud = provider();
    vi.mocked(cloud.generate).mockRejectedValueOnce(new Error('network down'));
    const service = new TryOnService(cloud, memoryCache());

    const result = await service.generate(request(true), new AbortController().signal);

    expect(result).toMatchObject({ ok: false, fallback: true, code: 'PROVIDER_ERROR' });
    expect(result.error).toContain('network down');
  });

  it('preserves provider billing metadata for a non-cached generation', async () => {
    const cloud = provider();
    const service = new TryOnService(cloud, memoryCache());

    const result = await service.generate(request(true), new AbortController().signal);

    expect(result).toMatchObject({
      ok: true,
      cached: false,
      providerRequestIds: ['p-1'],
      usageImageCount: 1,
      providerResponseRequestId: 'response-1',
    });
  });
});
