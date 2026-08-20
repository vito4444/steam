import { createHash } from 'node:crypto';
import fsp from 'node:fs/promises';
import path from 'node:path';

import type { TryOnGenerateRequest } from '../../shared/tryon';
import type { CloudTryOnProvider } from './provider';
import type { TryOnCacheLike } from './service';

export class TryOnDiskCache implements TryOnCacheLike {
  constructor(private readonly root: string) {}

  keyFor(request: TryOnGenerateRequest, provider: CloudTryOnProvider): string {
    const hash = createHash('sha256');
    hash.update(provider.id);
    hash.update(provider.cacheIdentity);
    hash.update(request.baseImageDataUrl);
    for (const garment of request.garments) {
      hash.update(garment.id);
      hash.update(garment.category);
      hash.update(garment.imageDataUrl);
    }
    return hash.digest('hex');
  }

  async get(key: string): Promise<string | null> {
    try {
      const imageDataUrl = await fsp.readFile(path.join(this.root, `${key}.data`), 'utf8');
      assertImageDataUrl(imageDataUrl);
      return imageDataUrl;
    } catch (error) {
      const code = error && typeof error === 'object' ? (error as { code?: string }).code : undefined;
      if (code === 'ENOENT') return null;
      throw error;
    }
  }

  async set(key: string, imageDataUrl: string): Promise<void> {
    assertImageDataUrl(imageDataUrl);
    await fsp.mkdir(this.root, { recursive: true });
    const target = path.join(this.root, `${key}.data`);
    const temporary = path.join(this.root, `${key}.${process.pid}.tmp`);
    await fsp.writeFile(temporary, imageDataUrl, 'utf8');
    await fsp.rename(temporary, target);
  }
}

function assertImageDataUrl(value: string): void {
  if (!/^data:image\/(?:png|jpeg|webp);base64,[A-Za-z0-9+/=]+$/.test(value)) {
    throw new Error('Try-on cache only accepts PNG, JPEG, or WebP data URLs');
  }
}
