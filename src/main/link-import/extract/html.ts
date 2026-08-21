/**
 * 极小的 HTML 元数据解析器。
 *
 * 故意不引 cheerio / jsdom：这个模块要跑在 Electron 主进程里，
 * 每多一个原生/重量级依赖，打包体积和 rebuild 风险都要 CERE-4 承担。
 * 我们只需要 <meta>、<title>、<script type=ld+json> 三类节点，正则足够且可测。
 */

const ENTITIES: Record<string, string> = {
  amp: '&', lt: '<', gt: '>', quot: '"', apos: "'", nbsp: ' ', '#39': "'", '#34': '"',
};

export function decodeEntities(s: string): string {
  return s
    .replace(/&(#x[0-9a-f]+|#\d+|[a-z]+);/gi, (m, ent: string) => {
      const key = ent.toLowerCase();
      if (ENTITIES[key]) return ENTITIES[key]!;
      if (key.startsWith('#x')) return String.fromCodePoint(parseInt(key.slice(2), 16));
      if (key.startsWith('#')) return String.fromCodePoint(parseInt(key.slice(1), 10));
      return m;
    })
    .trim();
}

/** 解析出所有 <meta> 的 name/property → content 映射（key 小写） */
export function parseMetaTags(html: string): Map<string, string> {
  const out = new Map<string, string>();
  const metaRe = /<meta\b([^>]*)>/gi;
  let m: RegExpExecArray | null;
  while ((m = metaRe.exec(html))) {
    const attrs = m[1] ?? '';
    const key =
      attrs.match(/\bproperty\s*=\s*["']([^"']+)["']/i)?.[1] ??
      attrs.match(/\bname\s*=\s*["']([^"']+)["']/i)?.[1] ??
      attrs.match(/\bitemprop\s*=\s*["']([^"']+)["']/i)?.[1];
    const content = attrs.match(/\bcontent\s*=\s*["']([^"']*)["']/i)?.[1];
    if (!key || content === undefined) continue;
    const k = key.toLowerCase();
    // 首个出现的优先：og:image 常有多张，第一张才是主图
    if (!out.has(k)) out.set(k, decodeEntities(content));
  }
  return out;
}

export function parseTitle(html: string): string | null {
  const t = html.match(/<title[^>]*>([\s\S]{0,300}?)<\/title>/i)?.[1];
  return t ? decodeEntities(t.replace(/\s+/g, ' ')) : null;
}

/** 取出所有 JSON-LD 块，解析失败的静默跳过（很多站点的 ld+json 本身就是坏的） */
export function parseJsonLd(html: string): unknown[] {
  const out: unknown[] = [];
  const re = /<script\b[^>]*type\s*=\s*["']application\/ld\+json["'][^>]*>([\s\S]*?)<\/script>/gi;
  let m: RegExpExecArray | null;
  while ((m = re.exec(html))) {
    const raw = (m[1] ?? '').trim();
    if (!raw) continue;
    try {
      const parsed: unknown = JSON.parse(raw);
      if (Array.isArray(parsed)) out.push(...parsed);
      else out.push(parsed);
    } catch {
      /* 坏 JSON 不是错误，忽略即可 */
    }
  }
  return out;
}

interface ProductLd {
  name?: string;
  image?: string | string[] | { url?: string };
  offers?: { price?: string | number; priceCurrency?: string } | Array<{ price?: string | number; priceCurrency?: string }>;
  brand?: string | { name?: string };
}

/** 从 JSON-LD 图里找 @type=Product 的节点（含 @graph 嵌套） */
export function findProductLd(nodes: unknown[]): ProductLd | null {
  const flat: unknown[] = [];
  const visit = (n: unknown, depth: number) => {
    if (!n || typeof n !== 'object' || depth > 4) return;
    flat.push(n);
    const graph = (n as { '@graph'?: unknown })['@graph'];
    if (Array.isArray(graph)) graph.forEach((g) => visit(g, depth + 1));
  };
  nodes.forEach((n) => visit(n, 0));

  for (const n of flat) {
    const type = (n as { '@type'?: unknown })['@type'];
    const types = Array.isArray(type) ? type : [type];
    if (types.some((t) => typeof t === 'string' && t.toLowerCase() === 'product')) {
      return n as ProductLd;
    }
  }
  return null;
}

export function ldImageUrls(img: ProductLd['image']): string[] {
  if (!img) return [];
  if (typeof img === 'string') return [img];
  if (Array.isArray(img)) return img.filter((i): i is string => typeof i === 'string');
  if (typeof img === 'object' && typeof img.url === 'string') return [img.url];
  return [];
}

export function ldPrice(offers: ProductLd['offers']): { price: number | null; currency: string | null } {
  const first = Array.isArray(offers) ? offers[0] : offers;
  if (!first) return { price: null, currency: null };
  const raw = first.price;
  const price = typeof raw === 'number' ? raw : typeof raw === 'string' ? Number.parseFloat(raw) : NaN;
  return {
    price: Number.isFinite(price) ? price : null,
    currency: typeof first.priceCurrency === 'string' ? first.priceCurrency : null,
  };
}
