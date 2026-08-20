import type { CommerceSource, ImportResult, Platform } from '../types.js';

/**
 * 把导入结果并进 CERE-5 的素材 meta.json。
 *
 * 契约要点（与 CERE-2 `asset.schema.json` 的关系）：
 * - **不新增顶层字段**。商品信息挂在既有的 `source` 下，作为 `source.commerce`。
 * - `source.origin` 区分素材是从照片来的还是从链接来的，衣橱据此显示价格角标。
 * - 抠图产物字段（files / palette / category / anchor…）一律由 CERE-5 填，本模块不碰。
 */
export interface AssetSourcePatch {
  source: {
    origin: 'photo' | 'link' | 'manual';
    photo_id: string | null;
    photo_file: string | null;
    imported_at: string;
    commerce: CommerceSource;
  };
}

export function buildCommerceSource(params: {
  url: string;
  platform: Platform;
  itemId: string | null;
  title: string | null;
  price: number | null;
  currency: string | null;
  shopName: string | null;
  imageUrl: string | null;
  entry: CommerceSource['entry'];
  now?: Date;
}): CommerceSource {
  return {
    source_url: params.url,
    platform: params.platform,
    item_id: params.itemId,
    title: params.title,
    price: params.price,
    // 页面没标货币但抓到了价格时，按国内电商默认 CNY —— 三个平台实测都不输出 priceCurrency
    currency: params.currency ?? (params.price != null ? 'CNY' : null),
    shop_name: params.shopName,
    fetched_at: (params.now ?? new Date()).toISOString(),
    thumbnail: null,
    image_url: params.imageUrl,
    entry: params.entry,
  };
}

export function toAssetSourcePatch(result: ImportResult, now = new Date()): AssetSourcePatch {
  return {
    source: {
      origin: result.commerce.entry === 'manual' ? 'manual' : 'link',
      photo_id: null,
      photo_file: null,
      imported_at: now.toISOString(),
      commerce: result.commerce,
    },
  };
}

/**
 * 衣橱去重键。同一件商品用户可能贴短链、贴带跟踪参数的长链、再贴一次分享文案，
 * 三次都应该被认成同一件。平台内 id 优先，没有 id 才退回归一化 URL。
 */
export function dedupeKey(c: Pick<CommerceSource, 'platform' | 'item_id' | 'source_url'>): string {
  return c.item_id ? `${c.platform}:${c.item_id}` : `url:${c.source_url}`;
}
