import { LinkImportError } from '../errors.js';
import type { Platform, AutomationTier } from '../types.js';

/**
 * 平台识别表。顺序有意义：先匹配到的先算。
 * tier 是 docs/platform-support.md 里实测结论的代码化，UI 用它给用户正确预期。
 */
interface PlatformRule {
  platform: Platform;
  tier: AutomationTier;
  hosts: RegExp;
  /** 从 URL 里抽平台内商品 id */
  itemId?: (u: URL) => string | null;
}

const RULES: PlatformRule[] = [
  {
    platform: 'tmall',
    tier: 'assisted',
    hosts: /(^|\.)(detail\.tmall\.com|tmall\.hk|detail\.tmall\.hk)$/i,
    itemId: (u) => u.searchParams.get('id'),
  },
  {
    platform: 'taobao',
    tier: 'assisted',
    hosts: /(^|\.)(taobao\.com|tb\.cn|m\.tb\.cn|e\.tb\.cn)$/i,
    itemId: (u) => u.searchParams.get('id') || u.searchParams.get('itemId'),
  },
  {
    platform: 'jd',
    tier: 'assisted',
    hosts: /(^|\.)(jd\.com|jd\.hk|3\.cn)$/i,
    // 桌面版 /<sku>.html，移动版 /product/<sku>.html
    itemId: (u) => u.pathname.match(/(\d{6,})\.html$/)?.[1] ?? u.searchParams.get('sku') ?? u.searchParams.get('wareId'),
  },
  {
    platform: 'pdd',
    tier: 'assisted',
    hosts: /(^|\.)(yangkeduo\.com|pinduoduo\.com|pdd\.cn)$/i,
    itemId: (u) => u.searchParams.get('goods_id'),
  },
  {
    platform: 'xiaohongshu',
    tier: 'assisted',
    hosts: /(^|\.)(xiaohongshu\.com|xhslink\.com)$/i,
    itemId: (u) => u.pathname.match(/\/(?:explore|discovery\/item)\/([0-9a-f]{16,32})/)?.[1] ?? null,
  },
  {
    platform: 'dewu',
    tier: 'manual',
    hosts: /(^|\.)(poizon\.com|dewu\.com)$/i,
    itemId: (u) => u.searchParams.get('spuId'),
  },
  {
    platform: 'vip',
    tier: 'assisted',
    hosts: /(^|\.)vip\.com$/i,
    itemId: (u) => u.pathname.match(/detail-\d+-(\d+)\.html/)?.[1] ?? null,
  },
  {
    platform: 'yanxuan',
    tier: 'auto',
    hosts: /(^|\.)(you\.163\.com|yanxuan\.163\.com)$/i,
    itemId: (u) => u.searchParams.get('id'),
  },
  {
    platform: 'uniqlo',
    tier: 'assisted',
    hosts: /(^|\.)uniqlo\.(cn|com)$/i,
    itemId: (u) => u.searchParams.get('productCode'),
  },
];

/** 跟踪参数：留着只会让「同一件商品」在衣橱里去重失败，统一剥掉 */
const TRACKING_PARAMS = [
  /^utm_/i, /^spm$/i, /^scm$/i, /^pvid$/i, /^ali_/i, /^_u$/i, /^share_/i, /^from$/i,
  /^sourceType$/i, /^suid$/i, /^ut_sk$/i, /^un$/i, /^share_crt_v$/i, /^sp_tk$/i,
  /^cpp$/i, /^shortkey$/i, /^app$/i, /^bxsign$/i, /^tk$/i, /^cv$/i, /^ptag$/i,
  /^refer/i, /^track/i, /^xhsshare$/i, /^appuid$/i, /^apptime$/i, /^gclid$/i, /^fbclid$/i,
];

export interface NormalizedLink {
  url: string;
  host: string;
  platform: Platform;
  tier: AutomationTier;
  itemId: string | null;
}

/**
 * 从一段任意文本里揪出第一个 http(s) 链接。
 * 用户从 App 分享出来的往往是「【标题】xx元 http://xxx 点击链接...」这种整段文案，
 * 强迫用户自己剪出纯 URL 是很差的体验。
 */
export function extractUrl(text: string): string | null {
  const m = text.match(/https?:\/\/[^\s'"<>，。、）)】\]]+/i);
  return m ? m[0] : null;
}

export function normalizeLink(input: string): NormalizedLink {
  const raw = extractUrl(input.trim()) ?? input.trim();

  let u: URL;
  try {
    u = new URL(raw);
  } catch {
    throw new LinkImportError('INVALID_URL', `无法解析成链接：${raw.slice(0, 120)}`);
  }
  if (u.protocol !== 'http:' && u.protocol !== 'https:') {
    throw new LinkImportError('INVALID_URL', `只支持 http/https 链接，收到 ${u.protocol}`);
  }

  for (const key of [...u.searchParams.keys()]) {
    if (TRACKING_PARAMS.some((re) => re.test(key))) u.searchParams.delete(key);
  }
  u.hash = '';

  const host = u.hostname.toLowerCase();
  const rule = RULES.find((r) => r.hosts.test(host));

  return {
    url: u.toString(),
    host,
    platform: rule?.platform ?? 'unknown',
    tier: rule?.tier ?? 'assisted',
    itemId: rule?.itemId?.(u) ?? null,
  };
}

/** 供 UI 展示「支持度」用，与 docs/platform-support.md 同源 */
export function listPlatformRules(): ReadonlyArray<{ platform: Platform; tier: AutomationTier }> {
  return RULES.map((r) => ({ platform: r.platform, tier: r.tier }));
}
