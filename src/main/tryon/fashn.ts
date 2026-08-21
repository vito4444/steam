import type { ProviderProfile, TryOnCategory, TryOnPlan } from '../../shared/tryon';
import { ensureNotAborted, errorMessage, jsonResponse } from './http';
import type { CloudTryOnProvider, ProviderGeneration } from './provider';

type FashnModel = 'tryon-max' | 'tryon-v1.6';
type FashnMode = 'fast' | 'performance' | 'balanced' | 'quality';

interface Options {
  apiKey?: string;
  fetchFn?: typeof fetch;
  sleep?: (milliseconds: number) => Promise<void>;
  model?: FashnModel;
  mode?: FashnMode;
  resolution?: '1k' | '2k' | '4k';
  pollIntervalMs?: number;
}

interface RunResponse { id?: string; error?: unknown }
interface StatusResponse {
  id?: string;
  status?: 'starting' | 'in_queue' | 'processing' | 'completed' | 'failed';
  output?: unknown;
  error?: unknown;
}

const V16_CATEGORY: Partial<Record<TryOnCategory, 'tops' | 'bottoms' | 'one-pieces'>> = {
  top: 'tops',
  outer: 'tops',
  bottom: 'bottoms',
  dress: 'one-pieces',
};

export class FashnProvider implements CloudTryOnProvider {
  readonly id = 'fashn' as const;
  readonly name: string;
  readonly cacheIdentity: string;
  readonly profile: ProviderProfile;
  readonly configured: boolean;
  readonly missingConfiguration?: string;
  readonly privacySummary = '图片以 base64 发送；完整图片不写入请求历史，输出最多可取 60 分钟，元数据/状态历史仍保留。';

  private readonly apiKey: string;
  private readonly fetchFn: typeof fetch;
  private readonly sleep: (milliseconds: number) => Promise<void>;
  private readonly model: FashnModel;
  private readonly mode: FashnMode;
  private readonly resolution: '1k' | '2k' | '4k';
  private readonly pollIntervalMs: number;

  constructor(options: Options) {
    this.apiKey = options.apiKey?.trim() ?? '';
    this.configured = this.apiKey.length > 0;
    if (!this.configured) this.missingConfiguration = 'FASHN_API_KEY 未配置';
    this.fetchFn = options.fetchFn ?? fetch;
    this.sleep = options.sleep ?? ((milliseconds) => new Promise((resolve) => setTimeout(resolve, milliseconds)));
    this.model = options.model ?? 'tryon-max';
    this.mode = options.mode ?? 'fast';
    this.resolution = options.resolution ?? '1k';
    this.name = this.model === 'tryon-max' ? 'FASHN Try-On Max' : 'FASHN Try-On v1.6';
    this.cacheIdentity = `${this.model}:${this.mode}:${this.resolution}`;
    this.profile = fashnProfile(this.model, this.mode, this.resolution);
    this.pollIntervalMs = options.pollIntervalMs ?? 1_000;
  }

  async generate(baseImageDataUrl: string, plan: TryOnPlan, signal: AbortSignal): Promise<ProviderGeneration> {
    if (!this.configured) throw new Error(this.missingConfiguration);
    let current = baseImageDataUrl;
    const requestIds: string[] = [];
    let creditsUsed = 0;

    for (const garment of plan.steps) {
      ensureNotAborted(signal);
      const inputs = this.model === 'tryon-max'
        ? {
            model_image: current,
            product_image: garment.imageDataUrl,
            prompt: promptFor(garment.category),
            resolution: this.resolution,
            generation_mode: this.mode === 'performance' ? 'fast' : this.mode,
            num_images: 1,
            output_format: 'png',
            return_base64: true,
          }
        : {
            model_image: current,
            garment_image: garment.imageDataUrl,
            category: V16_CATEGORY[garment.category] ?? 'auto',
            garment_photo_type: 'flat-lay',
            mode: this.mode === 'fast' ? 'performance' : this.mode,
            num_samples: 1,
            output_format: 'png',
            return_base64: true,
          };
      const submitted = await jsonResponse<RunResponse>(await this.fetchFn('https://api.fashn.ai/v1/run', {
        method: 'POST',
        headers: this.headers(),
        body: JSON.stringify({ model_name: this.model, inputs }),
        signal,
      }), 'FASHN submit');
      if (!submitted.id) throw new Error(`FASHN submit: ${errorMessage(submitted) ?? 'missing prediction id'}`);
      requestIds.push(submitted.id);
      const completed = await this.poll(submitted.id, signal);
      current = outputDataUrl(completed.output);
      creditsUsed += completed.credits;
    }

    return {
      imageDataUrl: current,
      providerRequestIds: requestIds,
      creditsUsed,
      usageImageCount: null,
      providerResponseRequestId: null,
    };
  }

  private async poll(id: string, signal: AbortSignal): Promise<{ output: unknown; credits: number }> {
    while (true) {
      ensureNotAborted(signal);
      const response = await this.fetchFn(`https://api.fashn.ai/v1/status/${encodeURIComponent(id)}`, {
        headers: this.headers(),
        signal,
      });
      const status = await jsonResponse<StatusResponse>(response, 'FASHN status');
      if (status.status === 'completed') {
        const parsed = Number(response.headers.get('x-fashn-credits-used') ?? '0');
        return { output: status.output, credits: Number.isFinite(parsed) ? parsed : 0 };
      }
      if (status.status === 'failed') {
        throw new Error(`FASHN generation failed: ${errorMessage(status) ?? 'unknown error'}`);
      }
      if (!status.status || !['starting', 'in_queue', 'processing'].includes(status.status)) {
        throw new Error(`FASHN status: unexpected state ${String(status.status)}`);
      }
      await this.sleep(this.pollIntervalMs);
    }
  }

  private headers(): Record<string, string> {
    return {
      Authorization: `Bearer ${this.apiKey}`,
      'Content-Type': 'application/json',
    };
  }
}

function fashnProfile(model: FashnModel, mode: FashnMode, resolution: '1k' | '2k' | '4k'): ProviderProfile {
  if (model === 'tryon-v1.6') {
    const seconds = mode === 'quality'
      ? { min: 12, max: 17 }
      : mode === 'balanced'
        ? { min: 8, max: 8 }
        : { min: 5, max: 5 };
    return {
      id: 'fashn',
      label: 'FASHN Try-On v1.6',
      supportedCategories: ['dress', 'bottom', 'top', 'outer'],
      usdPerStep: 0.075,
      creditsPerStep: 1,
      secondsPerStep: seconds,
    };
  }

  const normalizedMode = mode === 'performance' ? 'fast' : mode;
  const creditsByMode = {
    fast: { '1k': 1, '2k': 2, '4k': 3 },
    balanced: { '1k': 2, '2k': 3, '4k': 4 },
    quality: { '1k': 3, '2k': 4, '4k': 5 },
  } as const;
  const credits = creditsByMode[normalizedMode][resolution];
  const seconds = normalizedMode === 'fast' && resolution === '1k'
    ? { min: 10, max: 10 }
    : normalizedMode === 'balanced' && resolution === '2k'
      ? { min: 25, max: 25 }
      : normalizedMode === 'quality' && resolution === '4k'
        ? { min: 55, max: 55 }
        : { min: 10, max: 55 };
  return {
    id: 'fashn',
    label: 'FASHN Try-On Max',
    supportedCategories: ['dress', 'bottom', 'top', 'outer', 'shoe', 'bag', 'accessory'],
    usdPerStep: credits * 0.075,
    creditsPerStep: credits,
    secondsPerStep: seconds,
  };
}

function outputDataUrl(output: unknown): string {
  const first = Array.isArray(output) ? output[0] : null;
  if (typeof first === 'string' && first.startsWith('data:image/')) return first;
  if (first && typeof first === 'object') {
    const record = first as Record<string, unknown>;
    if (typeof record.url === 'string' && record.url.startsWith('data:image/')) return record.url;
  }
  throw new Error('FASHN status: completed response did not contain a base64 image');
}

function promptFor(category: TryOnCategory): string {
  if (category === 'outer') return 'Wear this as the outermost layer and keep the garments underneath visible.';
  if (category === 'shoe') return 'Replace the existing footwear with this product.';
  if (category === 'bag') return 'Add this bag naturally without changing the outfit.';
  if (category === 'accessory') return 'Add this wearable accessory naturally without changing the outfit.';
  return 'Wear this product naturally while preserving the person, pose, face, and existing compatible outfit items.';
}
