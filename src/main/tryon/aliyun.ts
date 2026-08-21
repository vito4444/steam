import { PROVIDER_PROFILES, type TryOnPlan } from '../../shared/tryon';
import { ensureNotAborted, errorMessage, imageResponseToDataUrl, jsonResponse } from './http';
import {
  ProviderGenerationFailure,
  type CloudTryOnProvider,
  type ProviderGeneration,
} from './provider';

const MODEL = 'aitryon-plus';
const API_ROOT = 'https://dashscope.aliyuncs.com/api/v1';

interface Options {
  apiKey?: string;
  fetchFn?: typeof fetch;
  sleep?: (milliseconds: number) => Promise<void>;
  pollIntervalMs?: number;
}

interface UploadPolicy {
  policy: string;
  signature: string;
  upload_dir: string;
  upload_host: string;
  oss_access_key_id: string;
  x_oss_object_acl: string;
  x_oss_forbid_overwrite: string;
}

interface TaskResponse {
  output?: {
    task_id?: string;
    task_status?: string;
    image_url?: string;
    code?: string;
    message?: string;
  };
  code?: string;
  message?: string;
  usage?: { image_count?: unknown };
  request_id?: unknown;
}

export class AliyunProvider implements CloudTryOnProvider {
  readonly id = 'aliyun' as const;
  readonly name = '阿里百炼 OutfitAnyone Plus';
  readonly cacheIdentity = 'aitryon-plus:original-resolution:restore-face';
  readonly profile = PROVIDER_PROFILES.aliyun;
  readonly configured: boolean;
  readonly missingConfiguration?: string;
  readonly privacySummary = '人物与衣物上传到百炼北京区临时 OSS，输入 URI 48 小时失效，任务和输出链接 24 小时后清理；官方声明模型调用数据不用于训练。';

  private readonly apiKey: string;
  private readonly fetchFn: typeof fetch;
  private readonly sleep: (milliseconds: number) => Promise<void>;
  private readonly pollIntervalMs: number;

  constructor(options: Options) {
    this.apiKey = options.apiKey?.trim() ?? '';
    this.configured = this.apiKey.length > 0;
    if (!this.configured) this.missingConfiguration = 'DASHSCOPE_API_KEY 未配置';
    this.fetchFn = options.fetchFn ?? fetch;
    this.sleep = options.sleep ?? ((milliseconds) => new Promise((resolve) => setTimeout(resolve, milliseconds)));
    this.pollIntervalMs = options.pollIntervalMs ?? 4_000;
  }

  async generate(baseImageDataUrl: string, plan: TryOnPlan, signal: AbortSignal): Promise<ProviderGeneration> {
    if (!this.configured) throw new Error(this.missingConfiguration);
    ensureNotAborted(signal);
    const dress = plan.steps.find((item) => item.category === 'dress');
    const top = dress ?? plan.steps.find((item) => item.category === 'top');
    const bottom = dress ? undefined : plan.steps.find((item) => item.category === 'bottom');
    if (!top && !bottom) throw new Error('阿里百炼：没有可提交的上装、下装或连衣裙');

    const policy = await this.uploadPolicy(signal);
    const personUrl = await this.upload(policy, baseImageDataUrl, 'person', signal);
    const topUrl = top ? await this.upload(policy, top.imageDataUrl, top.category, signal) : undefined;
    const bottomUrl = bottom ? await this.upload(policy, bottom.imageDataUrl, 'bottom', signal) : undefined;
    return this.submitAndDownload(personUrl, topUrl, bottomUrl, signal);
  }

  private async uploadPolicy(signal: AbortSignal): Promise<UploadPolicy> {
    const response = await jsonResponse<{ data?: Partial<UploadPolicy> }>(await this.fetchFn(
      API_ROOT + '/uploads?action=getPolicy&model=' + MODEL,
      { headers: { ...this.headers(), 'Content-Type': 'application/json' }, signal },
    ), '阿里百炼临时上传凭证');
    const data = response.data;
    const keys: (keyof UploadPolicy)[] = [
      'policy', 'signature', 'upload_dir', 'upload_host', 'oss_access_key_id',
      'x_oss_object_acl', 'x_oss_forbid_overwrite',
    ];
    if (!data || keys.some((key) => typeof data[key] !== 'string' || data[key]?.length === 0)) {
      throw new Error('阿里百炼临时上传凭证：响应字段不完整');
    }
    return data as UploadPolicy;
  }

  private async upload(
    policy: UploadPolicy,
    dataUrl: string,
    stem: string,
    signal: AbortSignal,
  ): Promise<string> {
    ensureNotAborted(signal);
    const image = parseImageDataUrl(dataUrl);
    const filename = stem + '-' + crypto.randomUUID() + '.' + image.extension;
    const key = policy.upload_dir + '/' + filename;
    const form = new FormData();
    form.append('OSSAccessKeyId', policy.oss_access_key_id);
    form.append('Signature', policy.signature);
    form.append('policy', policy.policy);
    form.append('x-oss-object-acl', policy.x_oss_object_acl);
    form.append('x-oss-forbid-overwrite', policy.x_oss_forbid_overwrite);
    form.append('key', key);
    form.append('success_action_status', '200');
    form.append('file', new Blob([image.bytes], { type: image.mime }), filename);
    const response = await this.fetchFn(policy.upload_host, { method: 'POST', body: form, signal });
    if (!response.ok) {
      const raw = await response.text();
      throw new Error('阿里百炼临时上传：' + (raw || response.statusText) + ' (' + response.status + ')');
    }
    return 'oss://' + key;
  }

  private async submitAndDownload(
    personUrl: string,
    topUrl: string | undefined,
    bottomUrl: string | undefined,
    signal: AbortSignal,
  ): Promise<ProviderGeneration> {
    const input: Record<string, string> = { person_image_url: personUrl };
    if (topUrl) input.top_garment_url = topUrl;
    if (bottomUrl) input.bottom_garment_url = bottomUrl;
    const submitted = await jsonResponse<TaskResponse>(await this.fetchFn(
      API_ROOT + '/services/aigc/image2image/image-synthesis',
      {
        method: 'POST',
        headers: {
          ...this.headers(),
          'Content-Type': 'application/json',
          'X-DashScope-Async': 'enable',
          'X-DashScope-OssResourceResolve': 'enable',
        },
        body: JSON.stringify({
          model: MODEL,
          input,
          parameters: { resolution: -1, restore_face: true },
        }),
        signal,
      },
    ), '阿里百炼提交');
    const taskId = submitted.output?.task_id;
    if (!taskId) throw new Error('阿里百炼提交：' + (responseMessage(submitted) ?? '缺少 task_id'));
    const completed = await this.poll(taskId, signal);
    let imageDataUrl: string;
    try {
      imageDataUrl = await imageResponseToDataUrl(
        await this.fetchFn(completed.imageUrl, { signal }),
        '阿里百炼输出下载',
      );
    } catch {
      throw new ProviderGenerationFailure('阿里百炼输出下载失败（任务已成功并可能已计费）', {
        providerRequestIds: [taskId],
        usageImageCount: completed.usageImageCount,
        providerResponseRequestId: completed.providerResponseRequestId,
      }, 'output_download');
    }
    return {
      imageDataUrl,
      providerRequestIds: [taskId],
      creditsUsed: null,
      usageImageCount: completed.usageImageCount,
      providerResponseRequestId: completed.providerResponseRequestId,
    };
  }

  private async poll(taskId: string, signal: AbortSignal): Promise<{
    imageUrl: string;
    usageImageCount: number | null;
    providerResponseRequestId: string | null;
  }> {
    while (true) {
      ensureNotAborted(signal);
      const response = await jsonResponse<TaskResponse>(await this.fetchFn(
        API_ROOT + '/tasks/' + encodeURIComponent(taskId),
        { headers: this.headers(), signal },
      ), '阿里百炼任务查询');
      const status = response.output?.task_status;
      if (status === 'SUCCEEDED') {
        const { billingEvidence, valid } = billingEvidenceForSucceededTask(response, taskId);
        if (!valid) {
          throw new ProviderGenerationFailure('阿里百炼任务查询：成功响应缺少或包含无效计费凭据', billingEvidence, 'billing_evidence');
        }
        if (!response.output?.image_url) {
          throw new ProviderGenerationFailure('阿里百炼任务查询：成功响应缺少 image_url', billingEvidence);
        }
        return {
          imageUrl: response.output.image_url,
          usageImageCount: billingEvidence.usageImageCount,
          providerResponseRequestId: billingEvidence.providerResponseRequestId,
        };
      }
      if (status === 'FAILED' || status === 'UNKNOWN' || status === 'CANCELED') {
        throw new Error('阿里百炼生成失败：' + (responseMessage(response) ?? status));
      }
      if (!status || !['PENDING', 'PRE-PROCESSING', 'RUNNING', 'POST-PROCESSING'].includes(status)) {
        throw new Error('阿里百炼任务查询：未知状态 ' + String(status));
      }
      await this.sleep(this.pollIntervalMs);
    }
  }

  private headers(): Record<string, string> {
    return { Authorization: 'Bearer ' + this.apiKey };
  }
}

function parseImageDataUrl(dataUrl: string): { mime: string; extension: string; bytes: ArrayBuffer } {
  const match = /^data:(image\/(?:png|jpeg|jpg|webp|bmp));base64,([A-Za-z0-9+/=]+)$/i.exec(dataUrl);
  if (!match) throw new Error('阿里百炼：图片必须是 PNG、JPEG、WEBP 或 BMP data URL');
  const mime = match[1].toLowerCase() === 'image/jpg' ? 'image/jpeg' : match[1].toLowerCase();
  const extension = mime === 'image/jpeg' ? 'jpg' : mime.slice('image/'.length);
  const bytes = Uint8Array.from(Buffer.from(match[2], 'base64'));
  return { mime, extension, bytes: bytes.buffer as ArrayBuffer };
}

function responseMessage(response: TaskResponse): string | null {
  return response.output?.message ?? response.output?.code ?? errorMessage(response);
}

function billingEvidenceForSucceededTask(response: TaskResponse, taskId: string): {
  billingEvidence: { providerRequestIds: string[]; usageImageCount: number | null; providerResponseRequestId: string | null };
  valid: boolean;
} {
  const usage = usageImageCount(response);
  const requestId = providerResponseRequestId(response);
  return {
    billingEvidence: {
      providerRequestIds: [taskId],
      usageImageCount: usage.value,
      providerResponseRequestId: requestId.value,
    },
    valid: usage.valid && requestId.valid,
  };
}

function usageImageCount(response: TaskResponse): { value: number | null; valid: boolean } {
  if (response.usage?.image_count === undefined) return { value: null, valid: false };
  const count = response.usage.image_count;
  if (typeof count !== 'number' || !Number.isInteger(count) || count !== 1) {
    return { value: null, valid: false };
  }
  return { value: count, valid: true };
}

function providerResponseRequestId(response: TaskResponse): { value: string | null; valid: boolean } {
  if (response.request_id === undefined) return { value: null, valid: false };
  if (typeof response.request_id !== 'string' || response.request_id.trim().length === 0) {
    return { value: null, valid: false };
  }
  return { value: response.request_id, valid: true };
}
