import type {
  ProviderProfile,
  TryOnPlan,
  TryOnProviderId,
} from '../../shared/tryon';

export interface ProviderGeneration {
  imageDataUrl: string;
  providerRequestIds: string[];
  creditsUsed: number | null;
  usageImageCount: number | null;
  providerResponseRequestId: string | null;
}

export interface ProviderBillingEvidence {
  providerRequestIds: string[];
  usageImageCount: number | null;
  providerResponseRequestId: string | null;
}

export type ProviderGenerationFailureStage = 'billing_evidence' | 'output_download';

/** A provider completed a billable generation but could not deliver its image bytes. */
export class ProviderGenerationFailure extends Error {
  constructor(
    message: string,
    readonly billingEvidence: ProviderBillingEvidence,
    readonly stage: ProviderGenerationFailureStage = 'output_download',
  ) {
    super(message);
    this.name = 'ProviderGenerationFailure';
  }
}

export interface CloudTryOnProvider {
  readonly id: TryOnProviderId;
  readonly name: string;
  readonly cacheIdentity: string;
  readonly profile: ProviderProfile;
  readonly configured: boolean;
  readonly missingConfiguration?: string;
  readonly privacySummary: string;

  generate(baseImageDataUrl: string, plan: TryOnPlan, signal: AbortSignal): Promise<ProviderGeneration>;
}

export interface ProviderDependencies {
  fetchFn?: typeof fetch;
  sleep?: (milliseconds: number) => Promise<void>;
}
