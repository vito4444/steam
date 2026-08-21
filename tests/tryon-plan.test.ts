import { describe, expect, it } from 'vitest';

import {
  estimateTryOn,
  planTryOn,
  PROVIDER_PROFILES,
  type TryOnGarment,
} from '@shared/tryon';

const garment = (id: string, category: TryOnGarment['category']): TryOnGarment => ({
  id,
  name: id,
  category,
  imageDataUrl: `data:image/png;base64,${id}`,
});

describe('planTryOn', () => {
  it('orders a multi-item outfit from structural layers to finishing items', () => {
    const plan = planTryOn([
      garment('shoe', 'shoe'),
      garment('coat', 'outer'),
      garment('shirt', 'top'),
      garment('trousers', 'bottom'),
    ], PROVIDER_PROFILES.fashn);

    expect(plan.steps.map((item) => item.id)).toEqual(['trousers', 'shirt', 'coat', 'shoe']);
    expect(plan.unsupported).toEqual([]);
  });

  it('lists unsupported fal items instead of silently billing them', () => {
    const plan = planTryOn([
      garment('shirt', 'top'),
      garment('shoe', 'shoe'),
      garment('bag', 'bag'),
    ], PROVIDER_PROFILES.fal);

    expect(plan.steps.map((item) => item.id)).toEqual(['shirt']);
    expect(plan.unsupported.map((item) => item.id)).toEqual(['shoe', 'bag']);
  });
});

describe('estimateTryOn', () => {
  it('prices every sequential FASHN Try-On Max step', () => {
    const plan = planTryOn([
      garment('bottom', 'bottom'),
      garment('top', 'top'),
      garment('outer', 'outer'),
      garment('shoe', 'shoe'),
    ], PROVIDER_PROFILES.fashn);

    expect(estimateTryOn(plan, PROVIDER_PROFILES.fashn)).toEqual({
      billableImages: 4,
      credits: 4,
      usd: 0.3,
      typicalSeconds: { min: 40, max: 40 },
    });
  });

  it('uses fal current per-image pricing for supported steps only', () => {
    const plan = planTryOn([
      garment('top', 'top'),
      garment('outer', 'outer'),
      garment('bag', 'bag'),
    ], PROVIDER_PROFILES.fal);

    expect(estimateTryOn(plan, PROVIDER_PROFILES.fal)).toMatchObject({
      billableImages: 2,
      credits: null,
      usd: 0.08,
    });
  });

  it('prices an Aliyun top and bottom as one combined output', () => {
    const plan = planTryOn([
      garment('top', 'top'),
      garment('bottom', 'bottom'),
      garment('shoe', 'shoe'),
    ], PROVIDER_PROFILES.aliyun);

    expect(plan.unsupported.map((item) => item.id)).toEqual(['shoe']);
    expect(estimateTryOn(plan, PROVIDER_PROFILES.aliyun)).toEqual({
      billableImages: 1,
      credits: null,
      cny: 0.5,
      usd: 0.071,
      typicalSeconds: { min: 90, max: 90 },
    });
  });
});
