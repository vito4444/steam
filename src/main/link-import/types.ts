/**
 * CERE-9 商品链接导入 —— 公共类型与接口契约。
 *
 * 本模块只负责「链接 → 一张可用的商品主图 + 商品元数据」，
 * 抠图 / 边缘清理 / 品类识别一律交给 CERE-5 的管线，这里不重复实现。
 */

/** 已识别的来源平台。`unknown` 表示走通用 OG/JSON-LD 解析。 */
export type Platform =
  | 'taobao'
  | 'tmall'
  | 'jd'
  | 'pdd'
  | 'xiaohongshu'
  | 'dewu'
  | 'vip'
  | 'yanxuan'
  | 'uniqlo'
  | 'unknown';

/** 各平台在「无登录、纯 HTTP」条件下的实测能力档位。依据见 docs/platform-support.md。 */
export type AutomationTier =
  /** 纯 HTTP 直接拿得到主图与标题 */
  | 'auto'
  /** HTTP 拿不到，但在应用内真实浏览器窗口里打开后可从 DOM 取图 */
  | 'assisted'
  /** 两条路都不通，只能让用户拖图/粘贴 */
  | 'manual';

/** 导入结果状态。UI 按这三个状态分支即可，不需要看 errorCode。 */
export type ImportStatus =
  /** 拿到了主图，元数据齐全或基本齐全 */
  | 'ok'
  /** 拿到了主图，但标题/价格等元数据缺失，需要用户补 */
  | 'partial'
  /** 没拿到图，必须走人工兜底（拖拽/粘贴/应用内浏览器取图） */
  | 'manual_required';

export type ErrorCode =
  /** 输入的不是合法 URL，或不是 http/https */
  | 'INVALID_URL'
  /** URL 合法但不在支持列表，且通用解析也没拿到图 */
  | 'UNSUPPORTED_PLATFORM'
  /** 网络层失败：DNS、连接、超时 */
  | 'NETWORK_ERROR'
  /** 服务端返回非 2xx */
  | 'HTTP_ERROR'
  /** 命中平台反爬 / 验证码 / 登录墙 */
  | 'ANTIBOT_BLOCKED'
  /** 页面拿到了，但页面里找不到可用主图 */
  | 'NO_IMAGE_FOUND'
  /** 图片地址拿到了，但下载失败或不是合法图片 */
  | 'IMAGE_DOWNLOAD_FAILED'
  /** 图片尺寸过小 / 体积超限，不适合做素材 */
  | 'IMAGE_REJECTED'
  /** 内部异常，见 message */
  | 'INTERNAL_ERROR';

export interface ProductMeta {
  /** 商品标题。抓不到时为 null，由用户手填。 */
  title: string | null;
  /** 价格数值。故意不带货币符号，便于排序与展示。 */
  price: number | null;
  /** ISO 4217，目前只会是 CNY，抓不到时为 null */
  currency: string | null;
  /** 平台内商品 id（淘宝 itemId / 京东 skuId / 拼多多 goodsId …） */
  itemId: string | null;
  /** 店铺名，能抓到才有 */
  shopName: string | null;
}

export interface ImageCandidate {
  url: string;
  /** 候选来源，用于排序与调试 */
  from: 'og' | 'json-ld' | 'twitter' | 'link-image_src' | 'platform-rule' | 'dom' | 'user';
  /** 已知宽高（从页面元数据/DOM 拿到时才有） */
  width?: number;
  height?: number;
  /** 排序分，越大越可能是商品主图 */
  score: number;
}

/** 下载到本地的图片。交给 CERE-5 抠图管线的就是这个。 */
export interface FetchedImage {
  /** 本地绝对路径（应用运行时的临时目录，不是交付物） */
  path: string;
  bytes: number;
  /** 由文件头判定，不信 Content-Type */
  mime: 'image/jpeg' | 'image/png' | 'image/webp' | 'image/gif';
  width: number;
  height: number;
  /** 原始远程地址，写进 provenance */
  sourceUrl: string;
}

/**
 * 写进素材 meta.json 的商用来源块。
 * 这是对 CERE-2 `asset.schema.json` 的**扩展**，不是另一套 schema：
 * 素材照旧走 `source` 字段，link 导入的额外信息挂在 `source.commerce` 下。
 * 完整定义见 schemas/commerce-source.schema.json。
 */
export interface CommerceSource {
  /** 用户粘贴的原始链接（已归一化、已去跟踪参数），点击用系统浏览器打开的就是它 */
  source_url: string;
  platform: Platform;
  /** 平台内商品 id，用于「同一件商品别导两次」的去重 */
  item_id: string | null;
  title: string | null;
  price: number | null;
  currency: string | null;
  shop_name: string | null;
  /** 抓取时刻。价格是快照，不做持续追踪，UI 上要显示「截至 X 时」 */
  fetched_at: string;
  /** 衣橱卡片用的缩略图文件名，与 asset.files 同目录 */
  thumbnail: string | null;
  /** 主图远程地址，仅作溯源；重导入时可复用 */
  image_url: string | null;
  /** 这条元数据是自动解析还是用户手填 */
  entry: 'auto' | 'assisted' | 'manual';
}

export interface ImportResult {
  status: ImportStatus;
  /** 归一化后的链接 */
  url: string;
  platform: Platform;
  /** 该平台的实测能力档位，UI 可据此提前给用户正确的预期 */
  tier: AutomationTier;
  product: ProductMeta;
  /** 按 score 降序，第 0 个是首选主图 */
  images: ImageCandidate[];
  /** 已下载的主图；`downloadImage: false` 或失败时为 null */
  image: FetchedImage | null;
  /** 可直接并进素材 meta.json 的来源块 */
  commerce: CommerceSource;
  error?: { code: ErrorCode; message: string; retryable: boolean };
  /** 排障用：每一步做了什么、花了多久 */
  trace: TraceEntry[];
}

export interface TraceEntry {
  step: string;
  ok: boolean;
  ms: number;
  detail?: string;
}

export interface ImportOptions {
  /** 单次 HTTP 超时（毫秒） */
  timeoutMs?: number;
  /** HTML 下载体积上限，防止拖垮内存 */
  maxHtmlBytes?: number;
  /** 图片体积上限 */
  maxImageBytes?: number;
  /** 主图最小边长，低于此值判为不适合做素材 */
  minImageEdge?: number;
  /** 是否真的把图下载到本地；只想预览元数据时传 false */
  downloadImage?: boolean;
  /** 图片落地目录，默认 os.tmpdir()/pixelfit-link-import */
  downloadDir?: string;
  /** 覆盖 UA，探测脚本会用到 */
  userAgent?: string;
  /** 注入 fetch，测试用 */
  fetchImpl?: typeof fetch;
}
