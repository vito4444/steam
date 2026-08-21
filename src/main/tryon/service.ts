import {
  estimateTryOn,
  planTryOn,
  type TryOnFailureCode,
  type TryOnGenerateRequest,
  type TryOnGenerateResult,
  type TryOnProviderStatus,
} from '../../shared/tryon';
import type { CloudTryOnProvider } from './provider';

export interface TryOnCacheLike {
  keyFor(request: TryOnGenerateRequest, provider: CloudTryOnProvider): string;
  get(key: string): Promise<string | null>;
  set(key: string, imageDataUrl: string): Promise<void>;
}

export class TryOnService {
  constructor(
    private readonly provider: CloudTryOnProvider,
    private readonly cache: TryOnCacheLike,
  ) {}

  status(): TryOnProviderStatus {
    return {
      provider: this.provider.id,
      name: this.provider.name,
      configured: this.provider.configured,
      missingConfiguration: this.provider.missingConfiguration,
      profile: this.provider.profile,
      privacySummary: this.provider.privacySummary,
    };
  }

  async generate(request: TryOnGenerateRequest, signal: AbortSignal): Promise<TryOnGenerateResult> {
    if (request.consent !== true) return this.failure('CONSENT_REQUIRED', '必须明确同意上传后才能生成');
    if (!this.provider.configured) {
      return this.failure('NOT_CONFIGURED', this.provider.missingConfiguration ?? '云端服务未配置');
    }

    const plan = planTryOn(request.garments, this.provider.profile);
    const estimate = estimateTryOn(plan, this.provider.profile);
    if (plan.steps.length === 0) {
      return this.failure('NO_SUPPORTED_ITEMS', '当前服务商不支持这套搭配中的品类', plan, estimate);
    }

    const key = this.cache.keyFor(request, this.provider);
    const cached = await this.cache.get(key);
    if (cached) {
      return {
        ok: true,
        cached: true,
        imageDataUrl: cached,
        provider: this.provider.id,
        providerRequestIds: [],
        creditsUsed: 0,
        usageImageCount: null,
        providerResponseRequestId: null,
        elapsedMs: 0,
        plan,
        estimate,
      };
    }

    const started = Date.now();
    try {
      const generated = await this.provider.generate(request.baseImageDataUrl, plan, signal);
      await this.cache.set(key, generated.imageDataUrl);
      return {
        ok: true,
        cached: false,
        imageDataUrl: generated.imageDataUrl,
        provider: this.provider.id,
        providerRequestIds: generated.providerRequestIds,
        creditsUsed: generated.creditsUsed,
        usageImageCount: generated.usageImageCount,
        providerResponseRequestId: generated.providerResponseRequestId,
        elapsedMs: Date.now() - started,
        plan,
        estimate,
      };
    } catch (error) {
      const timedOut = signal.aborted
        && signal.reason instanceof DOMException
        && signal.reason.name === 'TimeoutError';
      const aborted = signal.aborted || (error instanceof DOMException && error.name === 'AbortError');
      return this.failure(
        timedOut ? 'TIMEOUT' : aborted ? 'CANCELLED' : 'PROVIDER_ERROR',
        timedOut
          ? '生成超时，已回退到本地预览'
          : aborted ? '生成已取消，继续显示本地预览' : messageOf(error),
        plan,
        estimate,
      );
    }
  }

  private failure(
    code: TryOnFailureCode,
    error: string,
    plan?: ReturnType<typeof planTryOn>,
    estimate?: ReturnType<typeof estimateTryOn>,
  ): TryOnGenerateResult {
    return { ok: false, fallback: true, code, error, provider: this.provider.id, plan, estimate };
  }
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}
