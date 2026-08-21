import { mkdir, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { httpGetBinary } from '../fetch/httpClient.js';
import { LinkImportError } from '../errors.js';
import { probeImage } from './probeImage.js';
import type { FetchedImage } from '../types.js';

const EXT: Record<string, string> = {
  'image/jpeg': '.jpg', 'image/png': '.png', 'image/webp': '.webp', 'image/gif': '.gif',
};

export interface DownloadOptions {
  timeoutMs: number;
  maxBytes: number;
  minEdge: number;
  dir?: string;
  userAgent?: string;
  /** 带 Referer 能提高部分 CDN 的成功率；不带也大多能过（见 probe-logs） */
  referer?: string;
  fetchImpl?: typeof fetch;
}

export async function downloadImage(url: string, opts: DownloadOptions): Promise<FetchedImage> {
  const res = await httpGetBinary(url, {
    timeoutMs: opts.timeoutMs,
    maxBytes: opts.maxBytes,
    ...(opts.userAgent ? { userAgent: opts.userAgent } : {}),
    ...(opts.referer ? { headers: { Referer: opts.referer } } : {}),
    ...(opts.fetchImpl ? { fetchImpl: opts.fetchImpl } : {}),
  });

  const info = probeImage(res.body);
  if (!info) {
    throw new LinkImportError('IMAGE_DOWNLOAD_FAILED', `下载到的不是可识别的图片（${res.body.byteLength} 字节）`);
  }
  if (res.body.byteLength >= opts.maxBytes) {
    throw new LinkImportError('IMAGE_REJECTED', `图片超过 ${Math.round(opts.maxBytes / 1024 / 1024)}MB 上限`);
  }
  if (info.width < opts.minEdge || info.height < opts.minEdge) {
    throw new LinkImportError(
      'IMAGE_REJECTED',
      `图片只有 ${info.width}×${info.height}，低于 ${opts.minEdge}px 下限，抠出来的素材会糊`,
    );
  }
  if (info.mime === 'image/gif') {
    // GIF 基本是动图/占位图，抠图管线也不吃；直接挡掉比让 CERE-5 收到坏输入好
    throw new LinkImportError('IMAGE_REJECTED', 'GIF 不适合做衣物素材，请换静态图');
  }

  const dir = opts.dir ?? join(tmpdir(), 'pixelfit-link-import');
  await mkdir(dir, { recursive: true });
  // 用 URL 摘要做文件名：同一张图重复导入不会堆一堆临时文件
  const name = createHash('sha1').update(url).digest('hex').slice(0, 16) + (EXT[info.mime] ?? '.img');
  const path = join(dir, name);
  await writeFile(path, res.body);

  return { path, bytes: res.body.byteLength, mime: info.mime, width: info.width, height: info.height, sourceUrl: url };
}
