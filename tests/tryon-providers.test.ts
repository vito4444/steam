import { describe, expect, it, vi } from 'vitest';

import { FalProvider } from '../src/main/tryon/fal';
import { FashnProvider } from '../src/main/tryon/fashn';
import { AliyunProvider } from '../src/main/tryon/aliyun';
import { ProviderGenerationFailure } from '../src/main/tryon/provider';
import type { TryOnPlan } from '@shared/tryon';

const plan: TryOnPlan = {
  steps: [{
    id: 'top-1',
    name: 'White shirt',
    category: 'top',
    imageDataUrl: 'data:image/png;base64,Z2FybWVudA==',
  }],
  unsupported: [],
};

describe('FashnProvider', () => {
  it('uses privacy-focused base64 delivery and polls to completion', async () => {
    let polls = 0;
    const fetchFn = vi.fn(async (input: string | URL | Request, init?: RequestInit) => {
      const url = String(input);
      if (url.endsWith('/v1/run')) {
        return new Response(JSON.stringify({ id: 'prediction-1', error: null }), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        });
      }
      polls += 1;
      return new Response(JSON.stringify(polls === 1
        ? { id: 'prediction-1', status: 'processing', error: null }
        : { id: 'prediction-1', status: 'completed', output: ['data:image/png;base64,b3V0'], error: null }), {
        status: 200,
        headers: { 'content-type': 'application/json', 'x-fashn-credits-used': '1' },
      });
    });
    const provider = new FashnProvider({
      apiKey: 'secret',
      fetchFn: fetchFn as typeof fetch,
      sleep: async () => undefined,
      model: 'tryon-max',
      mode: 'fast',
      resolution: '1k',
    });

    const result = await provider.generate('data:image/png;base64,cGVyc29u', plan, new AbortController().signal);

    expect(result.imageDataUrl).toBe('data:image/png;base64,b3V0');
    expect(result.creditsUsed).toBe(1);
    const submitted = JSON.parse(String(fetchFn.mock.calls[0]?.[1]?.body)) as {
      model_name: string;
      inputs: Record<string, unknown>;
    };
    expect(submitted.model_name).toBe('tryon-max');
    expect(submitted.inputs).toMatchObject({
      model_image: 'data:image/png;base64,cGVyc29u',
      product_image: 'data:image/png;base64,Z2FybWVudA==',
      return_base64: true,
      output_format: 'png',
      resolution: '1k',
      generation_mode: 'fast',
    });
  });
});

describe('FalProvider', () => {
  it('disables payload storage, sets one-hour media expiry, and downloads output', async () => {
    const fetchFn = vi.fn(async (input: string | URL | Request) => {
      const url = String(input);
      if (url === 'https://queue.fal.run/fal-ai/image-apps-v2/virtual-try-on') {
        return new Response(JSON.stringify({
          request_id: 'request-1',
          status_url: 'https://queue.fal.run/status/1',
          response_url: 'https://queue.fal.run/response/1',
        }), { status: 200, headers: { 'content-type': 'application/json' } });
      }
      if (url.endsWith('/status/1')) {
        return new Response(JSON.stringify({ status: 'COMPLETED' }), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        });
      }
      if (url.endsWith('/response/1')) {
        return new Response(JSON.stringify({ images: [{ url: 'https://cdn.example/result.png' }] }), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        });
      }
      return new Response(Uint8Array.from([137, 80, 78, 71]), {
        status: 200,
        headers: { 'content-type': 'image/png' },
      });
    });
    const provider = new FalProvider({
      apiKey: 'secret',
      fetchFn: fetchFn as typeof fetch,
      sleep: async () => undefined,
    });

    const result = await provider.generate('data:image/png;base64,cGVyc29u', plan, new AbortController().signal);

    expect(result.imageDataUrl).toBe('data:image/png;base64,iVBORw==');
    const submitHeaders = new Headers(fetchFn.mock.calls[0]?.[1]?.headers);
    expect(submitHeaders.get('x-fal-store-io')).toBe('0');
    expect(JSON.parse(submitHeaders.get('x-fal-object-lifecycle-preference') ?? '{}'))
      .toEqual({ expiration_duration_seconds: 3600 });
  });
});

describe('AliyunProvider', () => {
  it('uploads private inputs, submits one top-bottom outfit, and downloads the completed image', async () => {
    const calls: string[] = [];
    let polls = 0;
    const fetchFn = vi.fn(async (input: string | URL | Request, init?: RequestInit) => {
      const url = String(input);
      calls.push(url);
      if (url.includes('/api/v1/uploads?')) {
        return new Response(JSON.stringify({
          data: {
            policy: 'policy',
            signature: 'signature',
            upload_dir: 'dashscope-instant/account/job',
            upload_host: 'https://upload.example',
            oss_access_key_id: 'access',
            x_oss_object_acl: 'private',
            x_oss_forbid_overwrite: 'true',
          },
        }), { status: 200, headers: { 'content-type': 'application/json' } });
      }
      if (url === 'https://upload.example') {
        const fields = Array.from((init?.body as FormData).keys());
        expect(fields.at(-1)).toBe('file');
        expect((init?.body as FormData).get('x-oss-object-acl')).toBe('private');
        return new Response('', { status: 200 });
      }
      if (url.includes('/image-synthesis')) {
        const body = JSON.parse(String(init?.body)) as Record<string, unknown>;
        expect(new Headers(init?.headers).get('x-dashscope-ossresourceresolve')).toBe('enable');
        expect(body).toMatchObject({
          model: 'aitryon-plus',
          input: {
            person_image_url: expect.stringMatching(/^oss:\/\//),
            top_garment_url: expect.stringMatching(/^oss:\/\//),
            bottom_garment_url: expect.stringMatching(/^oss:\/\//),
          },
          parameters: { resolution: -1, restore_face: true },
        });
        return new Response(JSON.stringify({ output: { task_id: 'task-1', task_status: 'PENDING' } }), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        });
      }
      if (url.endsWith('/api/v1/tasks/task-1')) {
        polls += 1;
        return new Response(JSON.stringify(polls === 1
          ? { output: { task_id: 'task-1', task_status: 'RUNNING' } }
          : {
              output: { task_id: 'task-1', task_status: 'SUCCEEDED', image_url: 'https://output.example/result.jpg' },
              usage: { image_count: 1 },
              request_id: 'request-1',
            }), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        });
      }
      if (url === 'https://output.example/result.jpg') {
        return new Response(Uint8Array.from([137, 80, 78, 71]), {
          status: 200,
          headers: { 'content-type': 'image/png' },
        });
      }
      throw new Error(`Unexpected URL: ${url}`);
    });
    const provider = new AliyunProvider({
      apiKey: 'secret',
      fetchFn: fetchFn as typeof fetch,
      sleep: async () => undefined,
    });
    const pairPlan: TryOnPlan = {
      steps: [
        plan.steps[0],
        { ...plan.steps[0], id: 'bottom-1', name: 'Trousers', category: 'bottom' },
      ],
      unsupported: [],
    };

    const result = await provider.generate(
      'data:image/png;base64,cGVyc29u',
      pairPlan,
      new AbortController().signal,
    );

    expect(result).toEqual({
      imageDataUrl: 'data:image/png;base64,iVBORw==',
      providerRequestIds: ['task-1'],
      creditsUsed: null,
      usageImageCount: 1,
      providerResponseRequestId: 'request-1',
    });
    expect(calls.filter((url) => url === 'https://upload.example')).toHaveLength(3);
  });

  it('retains billed task evidence when completed output download fails', async () => {
    const fetchFn = vi.fn(async (input: string | URL | Request) => {
      const url = String(input);
      if (url.includes('/uploads?')) {
        return new Response(JSON.stringify({ data: {
          policy: 'policy', signature: 'signature', upload_dir: 'job', upload_host: 'https://upload.example',
          oss_access_key_id: 'access', x_oss_object_acl: 'private', x_oss_forbid_overwrite: 'true',
        } }), { headers: { 'content-type': 'application/json' } });
      }
      if (url === 'https://upload.example') return new Response('', { status: 200 });
      if (url.includes('/image-synthesis')) {
        return new Response(JSON.stringify({ output: { task_id: 'task-1' } }), { headers: { 'content-type': 'application/json' } });
      }
      if (url.endsWith('/tasks/task-1')) {
        return new Response(JSON.stringify({
          output: { task_status: 'SUCCEEDED', image_url: 'https://output.example/result.png' },
          usage: { image_count: 1 }, request_id: 'request-1',
        }), { headers: { 'content-type': 'application/json' } });
      }
      return new Response('download unavailable', { status: 502 });
    });
    const provider = new AliyunProvider({ apiKey: 'secret', fetchFn: fetchFn as typeof fetch });

    await expect(provider.generate('data:image/png;base64,cGVyc29u', plan, new AbortController().signal))
      .rejects.toMatchObject({
        name: ProviderGenerationFailure.name,
        billingEvidence: {
          providerRequestIds: ['task-1'], usageImageCount: 1, providerResponseRequestId: 'request-1',
        },
      });
  });

  it('retains billed task evidence when a succeeded poll omits image_url', async () => {
    const fetchFn = vi.fn(async (input: string | URL | Request) => {
      const url = String(input);
      if (url.includes('/uploads?')) {
        return new Response(JSON.stringify({ data: uploadPolicy() }), { headers: { 'content-type': 'application/json' } });
      }
      if (url === 'https://upload.example') return new Response('', { status: 200 });
      if (url.includes('/image-synthesis')) {
        return new Response(JSON.stringify({ output: { task_id: 'task-1' } }), { headers: { 'content-type': 'application/json' } });
      }
      return new Response(JSON.stringify({
        output: { task_status: 'SUCCEEDED' }, usage: { image_count: 1 }, request_id: 'request-1',
      }), { headers: { 'content-type': 'application/json' } });
    });
    const provider = new AliyunProvider({ apiKey: 'secret', fetchFn: fetchFn as typeof fetch });

    await expect(provider.generate('data:image/png;base64,cGVyc29u', plan, new AbortController().signal))
      .rejects.toMatchObject({
        name: ProviderGenerationFailure.name,
        billingEvidence: {
          providerRequestIds: ['task-1'], usageImageCount: 1, providerResponseRequestId: 'request-1',
        },
      });
  });

  it.each([
    ['omits usage', { request_id: 'request-1' }, { usageImageCount: null, providerResponseRequestId: 'request-1' }],
    ['has invalid usage', { usage: { image_count: 'one' }, request_id: 'request-1' }, { usageImageCount: null, providerResponseRequestId: 'request-1' }],
    ['omits request ID', { usage: { image_count: 1 } }, { usageImageCount: 1, providerResponseRequestId: null }],
    ['has invalid request ID', { usage: { image_count: 1 }, request_id: 42 }, { usageImageCount: 1, providerResponseRequestId: null }],
    ['reports zero images', { usage: { image_count: 0 }, request_id: 'request-1' }, { usageImageCount: null, providerResponseRequestId: 'request-1' }],
    ['reports two images', { usage: { image_count: 2 }, request_id: 'request-1' }, { usageImageCount: null, providerResponseRequestId: 'request-1' }],
  ])('preserves valid Aliyun billing fields when a succeeded poll %s', async (_label, partial, expected) => {
    const fetchFn = succeededBillingFetch(partial);
    const provider = new AliyunProvider({ apiKey: 'secret', fetchFn: fetchFn as typeof fetch });

    await expect(provider.generate('data:image/png;base64,cGVyc29u', plan, new AbortController().signal))
      .rejects.toMatchObject({
        name: ProviderGenerationFailure.name,
        stage: 'billing_evidence',
        billingEvidence: { providerRequestIds: ['task-1'], ...expected },
      });
  });
});

function uploadPolicy() {
  return {
    policy: 'policy', signature: 'signature', upload_dir: 'job', upload_host: 'https://upload.example',
    oss_access_key_id: 'access', x_oss_object_acl: 'private', x_oss_forbid_overwrite: 'true',
  };
}

function succeededBillingFetch(partial: Record<string, unknown>) {
  return async (input: string | URL | Request) => {
    const url = String(input);
    if (url.includes('/uploads?')) return new Response(JSON.stringify({ data: uploadPolicy() }), { headers: { 'content-type': 'application/json' } });
    if (url === 'https://upload.example') return new Response('', { status: 200 });
    if (url.includes('/image-synthesis')) return new Response(JSON.stringify({ output: { task_id: 'task-1' } }), { headers: { 'content-type': 'application/json' } });
    return new Response(JSON.stringify({
      output: { task_status: 'SUCCEEDED', image_url: 'https://output.example/result.png' },
      ...partial,
    }), { headers: { 'content-type': 'application/json' } });
  };
}
