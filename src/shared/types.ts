/**
 * 素材 / Look 的落盘数据结构。
 *
 * 沿用 CERE-2 `schemas/asset.schema.json` 的字段名与语义，按 CERE-4 的
 * 方向变更做了三处改动（已在 CERE-4 issue 说明）：
 *   1. 删除 `pixelization` 整块 —— 不再有像素化 / 量化参数。
 *   2. `canvas` 由 const 192×384 改为 768×1536；`bitmap` 尺寸上限放开。
 *   3. 新增 `source_resolution`（原图分辨率）与 `fit`（贴合变换）。
 *
 * `files.pixel` 更名为 `files.cutout`（透明底原样素材），schema_version 升到 2。
 */

import type {
  AnchorName, BodyType, Category, ColorFamily, Slot,
} from './spec';
import type { BoardLook } from './board';

export interface PaletteColor {
  hex: string;
  ratio: number;
  role?: 'light' | 'mid' | 'shadow' | 'deep' | 'accent';
}

export interface Palette {
  dominant: string;
  color_family: ColorFamily;
  colors: PaletteColor[];
}

export interface CommerceSource {
  source_url: string;
  platform: string;
  item_id: string | null;
  title: string | null;
  price: number | null;
  currency: string | null;
  shop_name: string | null;
  fetched_at: string;
  thumbnail: string | null;
  image_url: string | null;
  entry: 'auto' | 'assisted' | 'manual';
}

/**
 * 贴合微调（CERE-11 起语义变了）。
 *
 * 自动贴合由「底图锚点 + 底图轮廓实测宽度」解出（`render/fit.ts`），
 * 这里四个值全部是**在自动解之上的相对微调**：
 *   scale     相对自动解的倍数，1 = 用自动解
 *   dx / dy   额外平移，单位是肩宽的倍数（与画布尺寸无关，换底图不失准）
 *   stretch_x 横向形变，限制 [0.88, 1.12]
 *
 * 旧版 schema_version 2 的素材里 scale 存的是绝对缩放，载入时会被归一
 * 化成 1（见 `normalizeFit`），否则换成写实底图后全部错位。
 */
export interface Fit {
  scale: number;
  dx: number;
  dy: number;
  stretch_x: number;
}

export const DEFAULT_FIT: Fit = { scale: 1, dx: 0, dy: 0, stretch_x: 1 };

/**
 * 素材位图内的关键点（位图自身像素坐标，未缩放）。
 *
 * CERE-10 的真实素材如果给得出这几个点，贴合就不靠轮廓估计而是直接对齐；
 * 给不出就留空，`render/fit.ts` 退回按槽位规则估。
 */
export type GarmentLandmark =
  | 'shoulder_l' | 'shoulder_r' | 'neck' | 'waist_l' | 'waist_r' | 'hem' | 'top_edge';

export interface AssetMeta {
  schema_version: 2 | 3;
  id: string;
  name: string;
  category: Category;
  subcategory?: string;
  slot: Slot;
  z_offset: number;
  occupies: Slot[];
  companion: string | null;
  pose: 'front_idle';

  canvas: { w: number; h: number };
  /** 实际像素尺寸（已裁到 alpha 包围盒） */
  bitmap: { file: string; w: number; h: number };
  /** 原图分辨率，来源照片的像素尺寸；占位素材为 null */
  source_resolution: { w: number; h: number } | null;

  /** 锚点在素材位图自身坐标系内的位置（未缩放前） */
  anchor: { base: AnchorName; x: number; y: number };
  landmarks?: Partial<Record<GarmentLandmark, { x: number; y: number }>>;
  /**
   * 上面哪些关键点是**素材包明确给出**的（其余是导入时自己量的）。
   *
   * 这个区分很要紧：自己量的「肩点」只是某一行的 alpha 跨度，对摊平拍的
   * 毛衣是袖展、对手提包是提手，拿它当贴合宽度会错得离谱。所以贴合只信
   * 这里列出来的那几个，自己量的仅用于找上沿与下摆。
   */
  landmarks_given?: GarmentLandmark[];
  fit: Fit;

  palette: Palette;
  attributes: {
    sleeve?: 'none' | 'cap' | 'short' | 'half' | 'long';
    length?: 'crop' | 'waist' | 'hip' | 'thigh' | 'knee' | 'midi' | 'ankle' | 'floor';
    pattern?: 'solid' | 'stripe' | 'check' | 'floral' | 'dot' | 'print' | 'logo' | 'other';
    fit?: 'slim' | 'regular' | 'loose' | 'oversize';
    material_guess?: string;
  };

  tags: string[];
  /** Automatic candidates with visible defects stay usable but retain this review marker. */
  review_status?: 'ready' | 'needs_optimization';
  season: ('spring' | 'summer' | 'autumn' | 'winter')[];

  source: {
    origin?: 'photo' | 'link' | 'manual' | 'bundle';
    demo?: boolean;
    photo_id: string | null;
    photo_file: string | null;
    bbox: [number, number, number, number] | null;
    imported_at: string;
    commerce?: CommerceSource;
  };

  provenance: {
    model: string;
    model_version: string;
    confidence: number;
    edited_by_user: boolean;
    edit_ops: string[];
  };

  files: {
    original: string | null;
    /** 透明底原样素材（不做像素化） */
    cutout: string;
    thumb: string;
  };

  favorite: boolean;
  wear_count: number;
  created_at: string;
  updated_at: string;
}

/** 渲染 / 列表用的素材视图：meta + 可直接给 <img> 的 URL */
export interface Asset extends AssetMeta {
  cutoutUrl: string;
  thumbUrl: string;
}

export interface LookBase {
  body: BodyType;
  /** 1–6 */
  skin: number;
  hair: string;
  hair_color: string;
}

export type LookSlots = Partial<Record<Slot, string | null>>;

export interface Look {
  /** 3 起新增 `tuck_overrides`；2 的存档照常读得进来（缺字段按默认规则算） */
  schema_version: 2 | 3;
  id: string;
  name: string;
  /**
   * 这套搭配是怎么摆出来的（CERE-21）。
   *   model —— 穿在模特身上，看上身效果（缺省，老 Look 都是这种）
   *   board —— 摊在画板上，看清每一件
   * 两种共用同一份 slots，所以一套画板 Look 也能被模特视图载入试穿。
   */
  kind?: 'model' | 'board';
  /** kind === 'board' 时的画板布局 */
  board?: BoardLook;
  base: LookBase;
  slots: LookSlots;
  z_overrides: Record<string, number>;
  hidden_slots: Slot[];
  fit_overrides: Record<string, Partial<Fit>>;
  /**
   * 逐件的「塞衣角」覆盖。默认由品类推（见 `occlusion.ts` 的 `defaultTuck`），
   * 用户在贴合面板里改过的才落到这里 —— 分层贴图相对生成式试穿唯一的优势
   * 就是这类手动微调可复现，所以它必须进存档。
   */
  tuck_overrides?: Record<string, 'in' | 'out'>;
  occasion: string[];
  tags: string[];
  notes?: string;
  background: 'none' | 'solid' | 'studio_warm' | 'studio_cool';
  background_color?: string;
  cover: string | null;
  favorite: boolean;
  created_at: string;
  updated_at: string;
}

export interface LookRecord extends Look {
  coverUrl: string | null;
}

export interface LibraryStats {
  assets: number;
  looks: number;
  root: string;
}

/**
 * 模特底图集合。CERE-6 交付真实底图前，由 app 自带的占位底图填充。
 *
 * 三种绘制模式：
 *   fill     —— 位图当作 alpha 掩膜，填 tint 指定的颜色（肤色 / 发色）
 *   multiply —— 灰度明暗图，multiply 叠在上一层填色结果上
 *   normal   —— 原样绘制（五官等已经带颜色的层）
 */
export interface BaseTone {
  id: string;
  name: string;
  swatch: string;
  layers: BaseBodyLayer[];
}

export interface BaseBodyLayer {
  /** 相对底图目录的文件名 */
  file: string;
  url: string;
  z: number;
  mode: 'fill' | 'multiply' | 'normal';
  tint?: 'skin' | 'hair';
}

export interface BaseHairSet {
  back: BaseBodyLayer[];
  front: BaseBodyLayer[];
}

export interface BaseBodySet {
  body: BodyType;
  /** 底图包自己的画布尺寸 —— 应用不再假设固定画布 */
  canvas: { w: number; h: number };
  /** 与肤色无关的公共层（打底层等） */
  layers: BaseBodyLayer[];
  /** 肤色档位，每档一套烘焙好的底图层；空数组表示底图不支持换肤色 */
  tones: BaseTone[];
  hair: Record<string, BaseHairSet>;
  /** 底图自带的锚点表；缺省时用 spec 里的 BASE_ANCHORS */
  anchors?: Record<string, { x: number; y: number }>;
  /**
   * 身体遮罩：人体轮廓的 alpha 图，画布坐标系与底图一致。
   * 渲染层用它裁掉衣物超出身体的部分（`render/mask.ts`）。
   * 底图包没给时回落到人体层自己的 alpha —— 精度差一点，但链路照样跑得通。
   */
  mask?: BaseBodyLayer;
  /** 底图包标识，界面上要如实显示当前用的是哪一版底图 */
  pack: string;
  source: 'builtin' | 'library';
}
