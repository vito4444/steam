import { normalizeLink, extractUrl, listPlatformRules } from './url/normalize.js';
import { httpGetText, DESKTOP_UA } from './fetch/httpClient.js';
import { extractFromHtml } from './extract/index.js';
import { downloadImage } from './image/download.js';
import { buildCommerceSource } from './asset/metadata.js';
import { LinkImportError, toLinkImportError, ERROR_MESSAGES } from './errors.js';
import type {
  FetchedImage, ImageCandidate, ImportOptions, ImportResult, TraceEntry,
} from './types.js';

const DEFAULTS = {
  timeoutMs: 15_000,
  maxHtmlBytes: 3 * 1024 * 1024,
  maxImageBytes: 12 * 1024 * 1024,
  // 低于 400px 的图抠完再放进衣橱卡片就已经发虚了，CERE-5 也修不回来
  minImageEdge: 400,
  downloadImage: true,
} as const;

/**
 * 商品链接 → 可进衣橱的素材输入。
 *
 * 三条路的第一条：纯 HTTP 自动解析。
 * 失败不抛异常，而是返回 status: 'manual_required'，让 UI 平滑降级到
 * 「应用内浏览器取图」或「拖拽/粘贴图片」。导入向导不应该因为一个链接解析不了就报红。
 */
export async function importFromLink(input: string, options: ImportOptions = {}): Promise<ImportResult> {
  const opts = { ...DEFAULTS, ...options };
  const trace: TraceEntry[] = [];
  const step = async <T>(name: string, fn: () => Promise<T> | T): Promise<T> => {
    const t0 = Date.now();
    try {
      const out = await fn();
      trace.push({ step: name, ok: true, ms: Date.now() - t0 });
      return out;
    } catch (err) {
      const e = toLinkImportError(err);
      trace.push({ step: name, ok: false, ms: Date.now() - t0, detail: `${e.code}: ${e.message}` });
      throw e;
    }
  };

  let link: ReturnType<typeof normalizeLink>;
  try {
    link = normalizeLink(input);
  } catch (err) {
    return failed(input, toLinkImportError(err), trace);
  }

  const base = { url: link.url, platform: link.platform, tier: link.tier };

  try {
    const page = await step('fetch-html', () =>
      httpGetText(link.url, {
        timeoutMs: opts.timeoutMs,
        maxBytes: opts.maxHtmlBytes,
        userAgent: opts.userAgent ?? DESKTOP_UA,
        ...(opts.fetchImpl ? { fetchImpl: opts.fetchImpl } : {}),
      }),
    );

    const extracted = await step('extract', () => extractFromHtml(page.body, page.finalUrl, link.platform));

    if (extracted.blocked) {
      trace.push({ step: 'blocked', ok: false, ms: 0, detail: extracted.blockedReason ?? '' });
      return {
        ...base,
        status: 'manual_required',
        product: { title: null, price: null, currency: null, itemId: link.itemId, shopName: null },
        images: [],
        image: null,
        commerce: buildCommerceSource({
          ...base, itemId: link.itemId, title: null, price: null, currency: null,
          shopName: null, imageUrl: null, entry: 'manual',
        }),
        error: {
          code: 'ANTIBOT_BLOCKED',
          message: ERROR_MESSAGES.ANTIBOT_BLOCKED,
          retryable: false,
        },
        trace,
      };
    }

    const product = { ...extracted.product, itemId: extracted.product.itemId ?? link.itemId };
    const images = extracted.images;

    if (images.length === 0) {
      return {
        ...base,
        status: 'manual_required',
        product,
        images: [],
        image: null,
        commerce: buildCommerceSource({
          ...base, itemId: product.itemId, title: product.title, price: product.price,
          currency: product.currency, shopName: product.shopName, imageUrl: null, entry: 'manual',
        }),
        error: { code: 'NO_IMAGE_FOUND', message: ERROR_MESSAGES.NO_IMAGE_FOUND, retryable: false },
        trace,
      };
    }

    let image: FetchedImage | null = null;
    let imageError: LinkImportError | null = null;
    if (opts.downloadImage) {
      try {
        image = await downloadFirstUsable(images, {
          timeoutMs: opts.timeoutMs,
          maxBytes: opts.maxImageBytes,
          minEdge: opts.minImageEdge,
          referer: link.url,
          ...(opts.downloadDir ? { dir: opts.downloadDir } : {}),
          ...(opts.userAgent ? { userAgent: opts.userAgent } : {}),
          ...(opts.fetchImpl ? { fetchImpl: opts.fetchImpl } : {}),
        }, trace);
      } catch (err) {
        imageError = toLinkImportError(err);
      }
    }

    const commerce = buildCommerceSource({
      ...base,
      itemId: product.itemId,
      title: product.title,
      price: product.price,
      currency: product.currency,
      shopName: product.shopName,
      imageUrl: image?.sourceUrl ?? images[0]?.url ?? null,
      entry: 'auto',
    });

    // 有图但没标题/价格 → partial：素材能进衣橱，元数据让用户补
    const missingMeta = !product.title || product.price == null;
    const status: ImportResult['status'] =
      opts.downloadImage && !image ? 'manual_required' : missingMeta ? 'partial' : 'ok';

    return {
      ...base,
      status,
      product,
      images,
      image,
      commerce,
      ...(imageError
        ? { error: { code: imageError.code, message: ERROR_MESSAGES[imageError.code], retryable: imageError.retryable } }
        : {}),
      trace,
    };
  } catch (err) {
    return failed(link.url, toLinkImportError(err), trace, link);
  }
}

/**
 * 依次试候选图，第一张通过校验的就用。
 * 单张失败不算失败：og:image 常常是分享用的小图或水印图，第二候选反而更好。
 */
async function downloadFirstUsable(
  images: ImageCandidate[],
  opts: Parameters<typeof downloadImage>[1],
  trace: TraceEntry[],
): Promise<FetchedImage> {
  let last: unknown;
  for (const cand of images.slice(0, 4)) {
    const t0 = Date.now();
    try {
      const img = await downloadImage(cand.url, opts);
      trace.push({
        step: 'download-image', ok: true, ms: Date.now() - t0,
        detail: `${img.width}x${img.height} ${cand.url}`,
      });
      return img;
    } catch (err) {
      last = err;
      const e = toLinkImportError(err);
      trace.push({ step: 'download-image', ok: false, ms: Date.now() - t0, detail: `${e.code} ${cand.url}` });
    }
  }
  throw last ?? new LinkImportError('IMAGE_DOWNLOAD_FAILED', '所有候选图都下载失败');
}

function failed(
  url: string,
  err: LinkImportError,
  trace: TraceEntry[],
  link?: ReturnType<typeof normalizeLink>,
): ImportResult {
  const platform = link?.platform ?? 'unknown';
  return {
    status: 'manual_required',
    url: link?.url ?? url,
    platform,
    tier: link?.tier ?? 'manual',
    product: { title: null, price: null, currency: null, itemId: link?.itemId ?? null, shopName: null },
    images: [],
    image: null,
    commerce: buildCommerceSource({
      url: link?.url ?? url, platform, itemId: link?.itemId ?? null, title: null, price: null,
      currency: null, shopName: null, imageUrl: null, entry: 'manual',
    }),
    error: { code: err.code, message: ERROR_MESSAGES[err.code], retryable: err.retryable },
    trace,
  };
}

/**
 * 兜底路径 2 / 3：用户拖进来一张图，或在应用内浏览器里点了「用这张图」。
 * 与自动路径返回同一种结构，UI 后续流程完全一致，不需要写两套。
 */
export function importFromUserImage(params: {
  image: FetchedImage;
  sourceUrl?: string;
  title?: string | null;
  price?: number | null;
  /** 'assisted' = 应用内浏览器取的图；'manual' = 用户自己拖/粘的 */
  entry: 'assisted' | 'manual';
}): ImportResult {
  const link = params.sourceUrl ? safeNormalize(params.sourceUrl) : null;
  const platform = link?.platform ?? 'unknown';
  return {
    status: params.title ? 'ok' : 'partial',
    url: link?.url ?? params.sourceUrl ?? '',
    platform,
    tier: link?.tier ?? 'manual',
    product: {
      title: params.title ?? null,
      price: params.price ?? null,
      currency: params.price != null ? 'CNY' : null,
      itemId: link?.itemId ?? null,
      shopName: null,
    },
    images: [{ url: params.image.sourceUrl, from: 'user', score: 100 }],
    image: params.image,
    commerce: buildCommerceSource({
      url: link?.url ?? params.sourceUrl ?? '',
      platform,
      itemId: link?.itemId ?? null,
      title: params.title ?? null,
      price: params.price ?? null,
      currency: null,
      shopName: null,
      imageUrl: params.image.sourceUrl,
      entry: params.entry,
    }),
    trace: [{ step: 'user-supplied-image', ok: true, ms: 0 }],
  };
}

function safeNormalize(url: string): ReturnType<typeof normalizeLink> | null {
  try {
    return normalizeLink(url);
  } catch {
    return null;
  }
}

export { normalizeLink, extractUrl, listPlatformRules, downloadImage, LinkImportError, ERROR_MESSAGES };
export { extractFromHtml } from './extract/index.js';
export { probeImage } from './image/probeImage.js';
export { buildCommerceSource, toAssetSourcePatch, dedupeKey } from './asset/metadata.js';
export type { AssetSourcePatch } from './asset/metadata.js';
export * from './types.js';
