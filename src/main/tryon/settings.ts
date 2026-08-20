import fsp from 'node:fs/promises';
import path from 'node:path';

import type { TryOnProviderId, TryOnSettingsUpdate } from '../../shared/tryon';

export interface SecretCipher {
  encrypt(value: string): string;
  decrypt(value: string): string;
}

export interface LoadedTryOnSettings {
  selectedProvider: TryOnProviderId;
  apiKeys: Partial<Record<TryOnProviderId, string>>;
  selectionSaved?: boolean;
}

interface StoredSettings {
  version: 1;
  selectedProvider: TryOnProviderId;
  encryptedApiKeys: Partial<Record<TryOnProviderId, string>>;
}

const PROVIDERS: TryOnProviderId[] = ['fashn', 'aliyun', 'fal'];

export class TryOnSettingsRepository {
  constructor(
    private readonly filePath: string,
    private readonly cipher: SecretCipher,
  ) {}

  async load(): Promise<LoadedTryOnSettings> {
    const { settings: stored, selectionSaved } = await this.readStored();
    const apiKeys: LoadedTryOnSettings['apiKeys'] = {};
    for (const provider of PROVIDERS) {
      const encrypted = stored.encryptedApiKeys[provider];
      if (!encrypted) continue;
      try {
        const decrypted = this.cipher.decrypt(encrypted).trim();
        if (decrypted) apiKeys[provider] = decrypted;
      } catch {
        // A key sealed by another Windows profile is unusable, but must not break startup.
      }
    }
    return { selectedProvider: stored.selectedProvider, apiKeys, selectionSaved };
  }

  async update(update: TryOnSettingsUpdate): Promise<LoadedTryOnSettings> {
    if (!isProvider(update.selectedProvider)) throw new Error('未知云试穿供应商');
    const { settings: stored } = await this.readStored();
    stored.selectedProvider = update.selectedProvider;
    if (update.clearApiKey) delete stored.encryptedApiKeys[update.selectedProvider];
    const apiKey = update.apiKey?.trim();
    if (apiKey) stored.encryptedApiKeys[update.selectedProvider] = this.cipher.encrypt(apiKey);
    await this.writeStored(stored);
    return this.load();
  }

  private async readStored(): Promise<{ settings: StoredSettings; selectionSaved: boolean }> {
    try {
      const parsed = JSON.parse(await fsp.readFile(this.filePath, 'utf8')) as Partial<StoredSettings>;
      if (parsed.version !== 1 || !isProvider(parsed.selectedProvider) || !isRecord(parsed.encryptedApiKeys)) {
        return { settings: defaults(), selectionSaved: false };
      }
      return {
        selectionSaved: true,
        settings: {
          version: 1,
          selectedProvider: parsed.selectedProvider,
          encryptedApiKeys: Object.fromEntries(
            Object.entries(parsed.encryptedApiKeys)
              .filter(([key, value]) => isProvider(key) && typeof value === 'string'),
          ),
        },
      };
    } catch {
      return { settings: defaults(), selectionSaved: false };
    }
  }

  private async writeStored(settings: StoredSettings): Promise<void> {
    await fsp.mkdir(path.dirname(this.filePath), { recursive: true });
    const temporary = this.filePath + '.' + process.pid + '.' + Date.now() + '.tmp';
    try {
      await fsp.writeFile(temporary, JSON.stringify(settings, null, 2) + '\n', {
        encoding: 'utf8',
        mode: 0o600,
      });
      await fsp.rename(temporary, this.filePath);
    } finally {
      await fsp.rm(temporary, { force: true });
    }
  }
}

function defaults(): StoredSettings {
  return { version: 1, selectedProvider: 'fashn', encryptedApiKeys: {} };
}

function isProvider(value: unknown): value is TryOnProviderId {
  return typeof value === 'string' && PROVIDERS.includes(value as TryOnProviderId);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}
