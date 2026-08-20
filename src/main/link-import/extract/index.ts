import type { ImageCandidate, Platform, ProductMeta } from '../types.js';
import {
  decodeEntities, findProductLd, ldImageUrls, ldPrice, parseJsonLd, parseMetaTags, parseTitle,
} from './html.js';

export interface ExtractResult {
  product: ProductMeta;
  images: ImageCandidate[];
  /** 页面看起来是反爬 / 登录墙而不是真正的商品页 */
  blocked: boolean;
  blockedReason?: string;
}

/**
 * 反爬 / 登录墙特征。
 * 全部来自 probe-logs 里的实测响应，不是猜的：
 * - 淘宝/天猫：`x5secdata` + `_____tmd_____`（阿里 TMD 风控），HTTP 仍是 200
 * - 京东：标题「京东验证」，正文 2704 字节的空壳
 * - 通用：跳到 login/passport 域
 */
const BLOCK_SIGNATURES: Array<{ re: RegExp; reason: string }> = [
  { re: /x5secdata|_____tmd_____|punish\?/i, reason: '淘宝系风控页（x5sec）' },
  { re: /<title>\s*京东验证\s*<\/title>/i, reason: '京东验证页' },
  { re: /<title>[^<]*(欢迎登录|登录注册|请登录)[^<]*<\/title>/i, reason: '登录页' },
  { re: /captcha-?(challenge|verify)|geetest|nc_iconfont|slidetounlock/i, reason: '验证码挑战页' },
  { re: /<title>\s*(登录|Login)\s*<\/title>/i, reason: '登录页' },
];

/** 首页壳子：拿到 200 但根本不是商品页（京东桌面端未登录时就是这样） */
const HOMEPAGE_TITLES = [
  /^京东\(JD\.COM\)/,
  /^淘宝网\s*-\s*淘！我喜欢/,
  /^拼多多$/,
  /^唯品会$/,
];

function isBlocked(html: string, title: string | null): { blocked: boolean; reason?: string } {
  for (const sig of BLOCK_SIGNATURES) {
    if (sig.re.test(html)) return { blocked: true, reason: sig.reason };
  }
  if (title && HOMEPAGE_TITLES.some((re) => re.test(title))) {
    return { blocked: true, reason: `返回的是站点首页/通用壳子（title=${title}），不是商品页` };
  }
  return { blocked: false };
}

/** 明显不是商品主图的：图标、占位、埋点、二维码、分享 logo */
const JUNK_IMAGE = /(logo|sprite|placeholder|blank|spacer|icon|avatar|qrcode|share_logo|1x1|pixel\.gif|default)/i;

function scoreImage(url: string, from: ImageCandidate['from'], w?: number, h?: number): number {
  let score = { 'json-ld': 90, og: 80, 'platform-rule': 85, twitter: 70, 'link-image_src': 60, dom: 50, user: 100 }[from];
  if (JUNK_IMAGE.test(url)) score -= 60;
  // 尺寸后缀是国内 CDN 的通用约定（如 _430x430q90.jpg / thumbnail=430x430）
  const dim = url.match(/(\d{2,4})x(\d{2,4})/);
  const width = w ?? (dim ? Number(dim[1]) : undefined);
  const height = h ?? (dim ? Number(dim[2]) : undefined);
  if (width && height) {
    if (width >= 600 && height >= 600) score += 15;
    else if (width < 200 || height < 200) score -= 40;
    // 商品主图基本都是方图或竖图，极端横图多半是 banner
    const ratio = width / height;
    if (ratio > 2.5 || ratio < 0.3) score -= 30;
  }
  return score;
}

function pushCandidate(
  list: ImageCandidate[], seen: Set<string>, url: string | undefined | null,
  from: ImageCandidate['from'], base: string, w?: number, h?: number,
): void {
  if (!url) return;
  let abs: string;
  try {
    // 国内站点大量使用 `//host/path` 协议相对地址
    abs = new URL(url.startsWith('//') ? `https:${url}` : url, base).toString();
  } catch {
    return;
  }
  if (!/^https?:/.test(abs) || seen.has(abs)) return;
  seen.add(abs);
  list.push({ url: abs, from, score: scoreImage(abs, from, w, h), ...(w ? { width: w } : {}), ...(h ? { height: h } : {}) });
}

/** 从「¥129.00」「129.00元」这类文本里取数值 */
export function parsePriceText(text: string | null | undefined): number | null {
  if (!text) return null;
  const m = text.match(/(\d+(?:[.,]\d{1,2})?)/);
  if (!m?.[1]) return null;
  const n = Number.parseFloat(m[1].replace(',', '.'));
  return Number.isFinite(n) ? n : null;
}

/**
 * 通用提取：OG → JSON-LD → twitter card → link[rel=image_src]。
 * 平台专用规则在这之后叠加（见 platformRules）。
 */
export function extractFromHtml(html: string, pageUrl: string, platform: Platform): ExtractResult {
  const title = parseTitle(html);
  const block = isBlocked(html, title);

  const meta = parseMetaTags(html);
  const images: ImageCandidate[] = [];
  const seen = new Set<string>();

  const ld = findProductLd(parseJsonLd(html));
  if (ld) for (const u of ldImageUrls(ld.image)) pushCandidate(images, seen, u, 'json-ld', pageUrl);

  pushCandidate(images, seen, meta.get('og:image:secure_url') ?? meta.get('og:image'), 'og', pageUrl);
  pushCandidate(images, seen, meta.get('twitter:image') ?? meta.get('twitter:image:src'), 'twitter', pageUrl);
  pushCandidate(images, seen, html.match(/<link[^>]+rel=["']image_src["'][^>]+href=["']([^"']+)["']/i)?.[1], 'link-image_src', pageUrl);

  for (const rule of platformRules(platform)) {
    for (const u of rule(html)) pushCandidate(images, seen, u, 'platform-rule', pageUrl);
  }

  const ldPriceInfo = ld ? ldPrice(ld.offers) : { price: null, currency: null };
  const ogTitle = meta.get('og:title');
  const product: ProductMeta = {
    title: block.blocked ? null : cleanTitle(ld?.name ?? ogTitle ?? title, platform),
    price: ldPriceInfo.price ?? parsePriceText(meta.get('og:price:amount') ?? meta.get('product:price:amount')),
    currency: ldPriceInfo.currency ?? meta.get('og:price:currency') ?? meta.get('product:price:currency') ?? null,
    itemId: null,
    shopName: typeof ld?.brand === 'string' ? ld.brand : (ld?.brand?.name ?? meta.get('og:site_name') ?? null),
  };

  images.sort((a, b) => b.score - a.score);
  return {
    product,
    images: block.blocked ? [] : images,
    blocked: block.blocked,
    ...(block.reason ? { blockedReason: block.reason } : {}),
  };
}

/** 标题里的站点后缀是噪音，衣橱卡片放不下 */
function cleanTitle(title: string | null | undefined, platform: Platform): string | null {
  if (!title) return null;
  let t = decodeEntities(title);
  t = t.replace(/\s*[-–—|｜]\s*(淘宝网|天猫|Tmall\.com[^|]*|京东|JD\.COM|拼多多|小红书|唯品会|网易严选|UNIQLO.*)\s*$/i, '');
  t = t.replace(/^【[^】]*】\s*/, '');
  if (platform === 'taobao' || platform === 'tmall') t = t.replace(/^\s*(【天猫[^】]*】|天猫\s*)/, '');
  t = t.replace(/\s+/g, ' ').trim();
  return t.length ? t.slice(0, 120) : null;
}

/**
 * 平台专用主图规则。
 * 只在「页面确实返回了商品 HTML」时才有意义——如果是风控页，前面已经短路。
 * 这些规则是低成本的加分项，不是主路径，失效了也不会让导入整体失败。
 */
function platformRules(platform: Platform): Array<(html: string) => string[]> {
  const all = (re: RegExp) => (html: string): string[] => {
    const out: string[] = [];
    let m: RegExpExecArray | null;
    const r = new RegExp(re.source, re.flags.includes('g') ? re.flags : `${re.flags}g`);
    while ((m = r.exec(html)) && out.length < 8) if (m[1]) out.push(m[1]);
    return out;
  };
  switch (platform) {
    case 'jd':
      return [all(/"(?:imagePath|mainImage|image)"\s*:\s*"((?:https?:)?\/\/img\d*\.360buyimg\.com\/[^"]+)"/i)];
    case 'taobao':
    case 'tmall':
      return [all(/"(?:picUrl|mainPic|images?)"\s*:\s*"((?:https?:)?\/\/(?:img|gw)\.alicdn\.com\/[^"]+)"/i)];
    case 'pdd':
      return [all(/"(?:hd_thumb_url|thumb_url|goods_img)"\s*:\s*"(https?:\/\/[^"]+)"/i)];
    case 'yanxuan':
      return [all(/"(?:primaryPicUrl|listPicUrl|picUrl)"\s*:\s*"(https?:\/\/[^"]+)"/i)];
    case 'xiaohongshu':
      return [all(/"urlDefault"\s*:\s*"(https?:\/\/[^"]+)"/i)];
    default:
      return [];
  }
}
