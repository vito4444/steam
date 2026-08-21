import { describe, expect, it } from 'vitest';

import { createProvider, createSettingsState } from '../src/main/tryon/provider-registry';

describe('createProvider', () => {
  it('uses the saved provider and saved key before environment configuration', () => {
    const provider = createProvider(
      { PIXELFIT_VTON_PROVIDER: 'fashn', FASHN_API_KEY: 'env-fashn' },
      {},
      { selectedProvider: 'aliyun', apiKeys: { aliyun: 'saved-dashscope' } },
    );

    expect(provider.id).toBe('aliyun');
    expect(provider.configured).toBe(true);
  });

  it('falls back to the provider environment key when no key is saved', () => {
    const provider = createProvider(
      { DASHSCOPE_API_KEY: 'env-dashscope' },
      {},
      { selectedProvider: 'aliyun', apiKeys: {} },
    );

    expect(provider.id).toBe('aliyun');
    expect(provider.configured).toBe(true);
  });

  it('keeps the legacy environment provider when no UI selection has been persisted', () => {
    const provider = createProvider(
      { PIXELFIT_VTON_PROVIDER: 'aliyun', DASHSCOPE_API_KEY: 'env-dashscope' },
      {},
      { selectedProvider: 'fashn', apiKeys: {}, selectionSaved: false },
    );

    expect(provider.id).toBe('aliyun');
  });
});

describe('createSettingsState', () => {
  it('reports key presence and source without returning any secret', () => {
    const state = createSettingsState(
      { selectedProvider: 'aliyun', apiKeys: { aliyun: 'saved-secret' } },
      { FASHN_API_KEY: 'env-secret' },
    );

    expect(state.selectedProvider).toBe('aliyun');
    expect(state.providers.find((item) => item.id === 'aliyun')).toMatchObject({
      configured: true,
      keySource: 'saved',
    });
    expect(state.providers.find((item) => item.id === 'fashn')).toMatchObject({
      configured: true,
      keySource: 'environment',
    });
    expect(JSON.stringify(state)).not.toContain('saved-secret');
    expect(JSON.stringify(state)).not.toContain('env-secret');
  });
});
