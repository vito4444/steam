import { PROVIDER_PROFILES, type TryOnPlan } from '../../shared/tryon';
import { ensureNotAborted, imageResponseToDataUrl, jsonResponse } from './http';
import type { CloudTryOnProvider, ProviderGeneration } from './provider';

interface Options {
  apiKey?: string;
  fetchFn?: typeof fetch;
  sleep?: (milliseconds: number) => Promise<void>;
  endpoint?: string;
  pollIntervalMs?: number;
}

interface SubmitResponse {
  request_id?: string;
  status_url?: string;
  response_url?: string;
}

interface StatusResponse { status?: 'IN_QUEUE' | 'IN_PROGRESS' | 'COMPLETED' }

export class FalProvider implements CloudTryOnProvider {
  readonly id = 'fal' as const;
  readonly name = 'fal Image Apps V2';
  readonly cacheIdentity: string;
  readonly profile = PROVIDER_PROFILES.fal;
  readonly configured: boolean;
  readonly missingConfiguration?: string;
  readonly privacySummary = '请求设置 X-Fal-Store-IO: 0，输出媒体要求 1 小时后过期。';

  private readonly apiKey: string;
  private readonly fetchFn: typeof fetch;
  private readonly sleep: (milliseconds: number) => Promise<void>;
  private readonly endpoint: string;
  private readonly pollIntervalMs: number;

  constructor(options: Options) {
    this.apiKey = options.apiKey?.trim() ?? '';
    this.configured = this.apiKey.length > 0;
    if (!this.configured) this.missingConfiguration = 'FAL_KEY 未配置';
    this.fetchFn = options.fetchFn ?? fetch;
    this.sleep = options.sleep ?? ((milliseconds) => new Promise((resolve) => setTimeout(resolve, milliseconds)));
    this.endpoint = options.endpoint ?? 'fal-ai/image-apps-v2/virtual-try-on';
    this.cacheIdentity = `fal:${this.endpoint}:preserve-pose`;
    this.pollIntervalMs = options.pollIntervalMs ?? 1_000;
  }

  async generate(baseImageDataUrl: string, plan: TryOnPlan, signal: AbortSignal): Promise<ProviderGeneration> {
    if (!this.configured) throw new Error(this.missingConfiguration);
    let current = baseImageDataUrl;
    const requestIds: string[] = [];

    for (const garment of plan.steps) {
      ensureNotAborted(signal);
      const submit = await jsonResponse<SubmitResponse>(await this.fetchFn(
        `https://queue.fal.run/${this.endpoint}`,
        {
          method: 'POST',
          headers: this.headers(true),
          body: JSON.stringify({
            person_image_url: current,
            clothing_image_url: garment.imageDataUrl,
            preserve_pose: true,
          }),
          signal,
        },
      ), 'fal submit');
      if (!submit.request_id || !submit.status_url || !submit.response_url) {
        throw new Error('fal submit: missing queue URLs');
      }
      requestIds.push(submit.request_id);
      await this.poll(submit.status_url, signal);
      const output = await jsonResponse<unknown>(await this.fetchFn(submit.response_url, {
        headers: this.headers(false),
        signal,
      }), 'fal result');
      const url = outputUrl(output);
      current = url.startsWith('data:image/')
        ? url
        : await imageResponseToDataUrl(await this.fetchFn(url, { signal }), 'fal output download');
    }

    return {
      imageDataUrl: current,
      providerRequestIds: requestIds,
      creditsUsed: null,
      usageImageCount: null,
      providerResponseRequestId: null,
    };
  }

  private async poll(statusUrl: string, signal: AbortSignal): Promise<void> {
    while (true) {
      ensureNotAborted(signal);
      const status = await jsonResponse<StatusResponse>(await this.fetchFn(statusUrl, {
        headers: this.headers(false),
        signal,
      }), 'fal status');
      if (status.status === 'COMPLETED') return;
      if (!status.status || !['IN_QUEUE', 'IN_PROGRESS'].includes(status.status)) {
        throw new Error(`fal status: unexpected state ${String(status.status)}`);
      }
      await this.sleep(this.pollIntervalMs);
    }
  }

  private headers(includePrivacy: boolean): Record<string, string> {
    const headers: Record<string, string> = { Authorization: `Key ${this.apiKey}` };
    if (includePrivacy) {
      headers['Content-Type'] = 'application/json';
      headers['X-Fal-Store-IO'] = '0';
      headers['X-Fal-Object-Lifecycle-Preference'] = JSON.stringify({ expiration_duration_seconds: 3600 });
    }
    return headers;
  }
}

function outputUrl(output: unknown): string {
  if (!output || typeof output !== 'object') throw new Error('fal result: missing image');
  const record = output as Record<string, unknown>;
  const images = Array.isArray(record.images) ? record.images : [];
  const first = images[0];
  if (first && typeof first === 'object') {
    const url = (first as Record<string, unknown>).url;
    if (typeof url === 'string') return url;
  }
  if (record.image && typeof record.image === 'object') {
    const url = (record.image as Record<string, unknown>).url;
    if (typeof url === 'string') return url;
  }
  throw new Error('fal result: missing image URL');
}
