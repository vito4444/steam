import fsp from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';

import { afterEach, describe, expect, it } from 'vitest';

import { TryOnSettingsRepository, type SecretCipher } from '../src/main/tryon/settings';

const roots: string[] = [];
const cipher: SecretCipher = {
  encrypt: (value) => Buffer.from('sealed:' + value).toString('base64'),
  decrypt: (value) => Buffer.from(value, 'base64').toString('utf8').replace(/^sealed:/, ''),
};

afterEach(async () => {
  await Promise.all(roots.splice(0).map((root) => fsp.rm(root, { recursive: true, force: true })));
});

async function repository(): Promise<{ root: string; repo: TryOnSettingsRepository }> {
  const root = await fsp.mkdtemp(path.join(os.tmpdir(), 'pixelfit-settings-'));
  roots.push(root);
  return { root, repo: new TryOnSettingsRepository(path.join(root, 'settings.json'), cipher) };
}

describe('TryOnSettingsRepository', () => {
  it('persists only encrypted keys and decrypts them inside the main process', async () => {
    const { root, repo } = await repository();

    await repo.update({ selectedProvider: 'aliyun', apiKey: 'dash-secret' });

    expect(await repo.load()).toEqual({
      selectedProvider: 'aliyun',
      apiKeys: { aliyun: 'dash-secret' },
      selectionSaved: true,
    });
    const raw = await fsp.readFile(path.join(root, 'settings.json'), 'utf8');
    expect(raw).not.toContain('dash-secret');
    expect(JSON.parse(raw)).toMatchObject({
      version: 1,
      selectedProvider: 'aliyun',
      encryptedApiKeys: { aliyun: expect.any(String) },
    });
  });

  it('clears only the selected provider key', async () => {
    const { repo } = await repository();
    await repo.update({ selectedProvider: 'fashn', apiKey: 'fashn-secret' });
    await repo.update({ selectedProvider: 'aliyun', apiKey: 'dash-secret' });

    await repo.update({ selectedProvider: 'aliyun', clearApiKey: true });

    expect(await repo.load()).toEqual({
      selectedProvider: 'aliyun',
      apiKeys: { fashn: 'fashn-secret' },
      selectionSaved: true,
    });
  });

  it('recovers to defaults when settings JSON is corrupt', async () => {
    const { root, repo } = await repository();
    await fsp.writeFile(path.join(root, 'settings.json'), '{broken', 'utf8');

    expect(await repo.load()).toEqual({ selectedProvider: 'fashn', apiKeys: {}, selectionSaved: false });
  });
});
