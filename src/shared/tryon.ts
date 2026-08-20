export type TryOnProviderId = 'fashn' | 'aliyun' | 'fal';

export type TryOnCategory =
  | 'dress'
  | 'bottom'
  | 'top'
  | 'outer'
  | 'shoe'
  | 'bag'
  | 'accessory';

export interface TryOnGarment {
  id: string;
  name: string;
  category: TryOnCategory;
  imageDataUrl: string;
}

export interface ProviderProfile {
  id: TryOnProviderId;
  label: string;
  supportedCategories: TryOnCategory[];
  usdPerStep: number;
  cnyPerStep?: number;
  creditsPerStep: number | null;
  billingUnit?: 'item' | 'outfit';
  secondsPerStep: { min: number; max: number };
}

export const PROVIDER_PROFILES: Record<TryOnProviderId, ProviderProfile> = {
  fashn: {
    id: 'fashn',
    label: 'FASHN Try-On Max',
    supportedCategories: ['dress', 'bottom', 'top', 'outer', 'shoe', 'bag', 'accessory'],
    usdPerStep: 0.075,
    creditsPerStep: 1,
    secondsPerStep: { min: 10, max: 10 },
  },
  aliyun: {
    id: 'aliyun',
    label: '阿里百炼 OutfitAnyone Plus',
    supportedCategories: ['dress', 'bottom', 'top'],
    usdPerStep: 0.071,
    cnyPerStep: 0.5,
    creditsPerStep: null,
    billingUnit: 'outfit',
    secondsPerStep: { min: 90, max: 90 },
  },
  fal: {
    id: 'fal',
    label: 'fal Image Apps V2',
    supportedCategories: ['dress', 'bottom', 'top', 'outer'],
    usdPerStep: 0.04,
    creditsPerStep: null,
    secondsPerStep: { min: 15, max: 40 },
  },
};

const ORDER: Record<TryOnCategory, number> = {
  dress: 0,
  bottom: 1,
  top: 2,
  outer: 3,
  shoe: 4,
  bag: 5,
  accessory: 6,
};

export interface TryOnPlan {
  steps: TryOnGarment[];
  unsupported: TryOnGarment[];
}

export function planTryOn(garments: TryOnGarment[], profile: ProviderProfile): TryOnPlan {
  const supported = new Set(profile.supportedCategories);
  const sorted = [...garments].sort((a, b) => ORDER[a.category] - ORDER[b.category]);
  return {
    steps: sorted.filter((item) => supported.has(item.category)),
    unsupported: sorted.filter((item) => !supported.has(item.category)),
  };
}

export interface TryOnEstimate {
  billableImages: number;
  credits: number | null;
  usd: number;
  cny?: number;
  typicalSeconds: { min: number; max: number };
}

function money(value: number): number {
  return Math.round(value * 1000) / 1000;
}

export function estimateTryOn(plan: TryOnPlan, profile: ProviderProfile): TryOnEstimate {
  const count = profile.billingUnit === 'outfit'
    ? Number(plan.steps.length > 0)
    : plan.steps.length;
  const estimate: TryOnEstimate = {
    billableImages: count,
    credits: profile.creditsPerStep === null ? null : profile.creditsPerStep * count,
    usd: money(profile.usdPerStep * count),
    typicalSeconds: {
      min: profile.secondsPerStep.min * count,
      max: profile.secondsPerStep.max * count,
    },
  };
  if (profile.cnyPerStep !== undefined) estimate.cny = money(profile.cnyPerStep * count);
  return estimate;
}

export interface TryOnProviderOption {
  id: TryOnProviderId;
  label: string;
  configured: boolean;
  keySource: 'saved' | 'environment' | 'none';
  profile: ProviderProfile;
  privacySummary: string;
}

export interface TryOnSettingsState {
  selectedProvider: TryOnProviderId;
  providers: TryOnProviderOption[];
}

export interface TryOnSettingsUpdate {
  selectedProvider: TryOnProviderId;
  apiKey?: string;
  clearApiKey?: boolean;
}

export interface TryOnGenerateRequest {
  clientRequestId: string;
  consent: boolean;
  baseImageDataUrl: string;
  garments: TryOnGarment[];
}

export interface TryOnProviderStatus {
  provider: TryOnProviderId;
  name: string;
  configured: boolean;
  missingConfiguration?: string;
  profile: ProviderProfile;
  privacySummary: string;
}

export type TryOnFailureCode =
  | 'CONSENT_REQUIRED'
  | 'NOT_CONFIGURED'
  | 'NO_SUPPORTED_ITEMS'
  | 'CANCELLED'
  | 'TIMEOUT'
  | 'PROVIDER_ERROR';

export interface TryOnSuccessResult {
  ok: true;
  cached: boolean;
  imageDataUrl: string;
  provider: TryOnProviderId;
  providerRequestIds: string[];
  creditsUsed: number | null;
  usageImageCount: number | null;
  providerResponseRequestId: string | null;
  elapsedMs: number;
  plan: TryOnPlan;
  estimate: TryOnEstimate;
}

export interface TryOnFailureResult {
  ok: false;
  fallback: true;
  code: TryOnFailureCode;
  error: string;
  provider: TryOnProviderId;
  plan?: TryOnPlan;
  estimate?: TryOnEstimate;
}

export type TryOnGenerateResult = TryOnSuccessResult | TryOnFailureResult;
