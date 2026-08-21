import { LinkImportError, toLinkImportError } from '../errors.js';

export const DESKTOP_UA =
  'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36';

export interface HttpOptions {
  timeoutMs: number;
  maxBytes: number;
  userAgent?: string;
  headers?: Record<string, string>;
  fetchImpl?: typeof fetch;
}

export interface HttpTextResult {
  finalUrl: string;
  status: number;
  contentType: string;
  body: string;
  bytes: number;
}

export interface HttpBinaryResult {
  finalUrl: string;
  status: number;
  contentType: string;
  body: Uint8Array;
}

function baseHeaders(ua: string, extra?: Record<string, string>): Record<string, string> {
  return {
    'User-Agent': ua,
    'Accept-Language': 'zh-CN,zh;q=0.9,en;q=0.8',
    Accept: 'text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8',
    ...extra,
  };
}

/**
 * 读 body 时按 maxBytes 截断。
 * 不能用 `res.text()`：商品页动辄几 MB，几十个并发导入会直接把主进程内存吃满。
 */
async function readCapped(res: Response, maxBytes: number): Promise<Uint8Array> {
  if (!res.body) return new Uint8Array(0);
  const reader = res.body.getReader();
  const chunks: Uint8Array[] = [];
  let total = 0;
  try {
    for (;;) {
      const { done, value } = await reader.read();
      if (done) break;
      if (!value) continue;
      total += value.byteLength;
      if (total > maxBytes) {
        chunks.push(value.subarray(0, value.byteLength - (total - maxBytes)));
        await reader.cancel().catch(() => {});
        break;
      }
      chunks.push(value);
    }
  } finally {
    reader.releaseLock?.();
  }
  const out = new Uint8Array(Math.min(total, maxBytes));
  let off = 0;
  for (const c of chunks) {
    out.set(c, off);
    off += c.byteLength;
  }
  return out;
}

/** 优先用响应头声明的 charset；国内老站点仍有 GBK 页面，硬按 UTF-8 解会得到乱码标题 */
function decode(bytes: Uint8Array, contentType: string): string {
  const declared = contentType.match(/charset=([\w-]+)/i)?.[1]?.toLowerCase();
  const head = new TextDecoder('utf-8', { fatal: false }).decode(bytes.subarray(0, 2048));
  const inHtml = head.match(/charset=["']?([\w-]+)/i)?.[1]?.toLowerCase();
  const charset = declared || inHtml || 'utf-8';
  try {
    return new TextDecoder(charset, { fatal: false }).decode(bytes);
  } catch {
    return new TextDecoder('utf-8', { fatal: false }).decode(bytes);
  }
}

export async function httpGetText(url: string, opts: HttpOptions): Promise<HttpTextResult> {
  const doFetch = opts.fetchImpl ?? fetch;
  const ctl = new AbortController();
  const timer = setTimeout(() => ctl.abort(), opts.timeoutMs);
  try {
    const res = await doFetch(url, {
      redirect: 'follow',
      signal: ctl.signal,
      headers: baseHeaders(opts.userAgent ?? DESKTOP_UA, opts.headers),
    });
    const contentType = res.headers.get('content-type') ?? '';
    if (!res.ok) {
      throw new LinkImportError(
        'HTTP_ERROR',
        `${new URL(url).hostname} 返回 HTTP ${res.status}`,
        res.status >= 500 || res.status === 429,
      );
    }
    const bytes = await readCapped(res, opts.maxBytes);
    return {
      finalUrl: res.url || url,
      status: res.status,
      contentType,
      body: decode(bytes, contentType),
      bytes: bytes.byteLength,
    };
  } catch (err) {
    throw toLinkImportError(err);
  } finally {
    clearTimeout(timer);
  }
}

export async function httpGetBinary(url: string, opts: HttpOptions): Promise<HttpBinaryResult> {
  const doFetch = opts.fetchImpl ?? fetch;
  const ctl = new AbortController();
  const timer = setTimeout(() => ctl.abort(), opts.timeoutMs);
  try {
    const res = await doFetch(url, {
      redirect: 'follow',
      signal: ctl.signal,
      headers: baseHeaders(opts.userAgent ?? DESKTOP_UA, opts.headers),
    });
    if (!res.ok) {
      throw new LinkImportError(
        'IMAGE_DOWNLOAD_FAILED',
        `图片地址返回 HTTP ${res.status}`,
        res.status >= 500 || res.status === 429,
      );
    }
    return {
      finalUrl: res.url || url,
      status: res.status,
      contentType: res.headers.get('content-type') ?? '',
      body: await readCapped(res, opts.maxBytes),
    };
  } catch (err) {
    throw toLinkImportError(err);
  } finally {
    clearTimeout(timer);
  }
}
