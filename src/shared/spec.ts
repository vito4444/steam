/**
 * PixelFit 渲染规范。
 *
 * 保留：槽位、z-index、锚点语义、互斥规则。
 * 作废：调色板量化、像素描边、最近邻整数缩放（方向变更，CERE-4）。
 * CERE-11 又去掉了两处写死：
 *   1. 画布尺寸不再是常量 —— 由底图包的 manifest 决定（CERE-6 写实底图是
 *      1152×2304，CERE-10 的真人底图还可能不同），常量只作为回落。
 *   2. 参考框（按插画娃娃比例定的绝对像素框）作废，改成由底图锚点与
 *      底图轮廓实测宽度推导贴合，见 `renderer/render/fit.ts`。
 */

/** 底图包没给 canvas 时的回落尺寸 */
export const CANVAS = { w: 1152, h: 2304 } as const;

export type AnchorName =
  | 'head_top' | 'eye_line' | 'chin' | 'neck' | 'shoulder_line'
  | 'shoulder_l' | 'shoulder_r' | 'chest' | 'waist' | 'hip' | 'crotch'
  | 'wrist_l' | 'wrist_r' | 'knee' | 'ankle_l' | 'ankle_r' | 'foot_base';

export interface Point { x: number; y: number }

/**
 * 基底锚点回落值（画布坐标）—— CERE-6 `models/anchors.json` 按写实人体
 * 解剖标定的一套，画布 1152×2304。底图包自带 anchors 时以底图包为准，
 * 这里只在底图包缺字段时兜底。
 */
export const BASE_ANCHORS: Record<AnchorName, Point> = {
  head_top:      { x: 576, y: 96 },
  eye_line:      { x: 576, y: 250 },
  chin:          { x: 576, y: 376 },
  neck:          { x: 576, y: 442 },
  shoulder_line: { x: 576, y: 500 },
  shoulder_l:    { x: 380, y: 500 },
  shoulder_r:    { x: 772, y: 500 },
  chest:         { x: 576, y: 656 },
  waist:         { x: 576, y: 846 },
  hip:           { x: 576, y: 1010 },
  crotch:        { x: 576, y: 1174 },
  wrist_l:       { x: 258, y: 1132 },
  wrist_r:       { x: 894, y: 1132 },
  knee:          { x: 576, y: 1700 },
  ankle_l:       { x: 492, y: 2140 },
  ankle_r:       { x: 660, y: 2140 },
  foot_base:     { x: 576, y: 2232 },
};

export type Slot =
  | 'hair_back' | 'cape' | 'underlayer' | 'legwear' | 'shoe_base'
  | 'bottom' | 'shoe_shaft' | 'dress' | 'top' | 'belt' | 'outer'
  | 'neckwear' | 'gloves' | 'bag' | 'hair_front' | 'headwear' | 'eyewear';

/**
 * 图层顺序。CERE-2 §3 那版按 CERE-14 §1 的硬要求调过三处：
 *   legwear 30 → 44   袜/打底要在**裤之上**
 *   shoe_base 35 → 46 鞋要在**袜之上**
 * 「裤脚盖鞋帮」和上面第二条方向相反，画序表达不了，由 `occlusion.ts` 的
 * 成对挖除规则处理（踝线以上的鞋被裤子挖掉）。
 *
 * 这张表是遮挡规则表的一部分，运行期可被 `occlusion.json` 覆盖。
 */
export const SLOT_Z: Record<Slot, number> = {
  hair_back: 10,
  cape: 15,
  underlayer: 25,
  bottom: 40,
  legwear: 44,
  shoe_base: 46,
  shoe_shaft: 48,
  dress: 50,
  top: 55,
  belt: 65,
  outer: 70,
  neckwear: 72,
  gloves: 80,
  bag: 85,
  hair_front: 90,
  headwear: 95,
  eyewear: 100,
};

/** 基底自身的层，不来自衣橱 */
export const BASE_Z = { background: 0, hair_back: 10, body: 20, face: 22, hair_front: 90 } as const;

export type Category =
  | 'top' | 'bottom' | 'dress' | 'outer' | 'shoe' | 'bag'
  | 'headwear' | 'eyewear' | 'neckwear' | 'belt' | 'gloves'
  | 'legwear' | 'underlayer' | 'other';

export const CATEGORY_SLOT: Record<Category, Slot> = {
  top: 'top',
  bottom: 'bottom',
  dress: 'dress',
  outer: 'outer',
  shoe: 'shoe_base',
  bag: 'bag',
  headwear: 'headwear',
  eyewear: 'eyewear',
  neckwear: 'neckwear',
  belt: 'belt',
  gloves: 'gloves',
  legwear: 'legwear',
  underlayer: 'underlayer',
  other: 'top',
};

export const CATEGORY_LABEL: Record<Category, string> = {
  top: '上装', bottom: '下装', dress: '连衣裙', outer: '外套', shoe: '鞋',
  bag: '包', headwear: '帽饰', eyewear: '眼镜', neckwear: '围巾', belt: '腰带',
  gloves: '手套', legwear: '袜/打底', underlayer: '内层', other: '其他',
};

export const SLOT_LABEL: Record<Slot, string> = {
  hair_back: '发型(后)', cape: '披风', underlayer: '内层', legwear: '袜/打底',
  shoe_base: '鞋', bottom: '下装', shoe_shaft: '靴筒', dress: '连衣裙',
  top: '上装', belt: '腰带', outer: '外套', neckwear: '围巾', gloves: '手套',
  bag: '包', hair_front: '发型(前)', headwear: '帽饰', eyewear: '眼镜',
};

/** 当前搭配面板从上到下的展示顺序（z 从高到低） */
export const PANEL_SLOTS: Slot[] = [
  'eyewear', 'headwear', 'bag', 'gloves', 'neckwear', 'outer',
  'belt', 'top', 'dress', 'shoe_shaft', 'shoe_base', 'legwear',
  'bottom', 'underlayer', 'cape',
];

/** 每个槽位对齐到哪个基底锚点（CERE-2 §2.3） */
export const SLOT_ANCHOR: Record<Slot, AnchorName> = {
  hair_back: 'head_top',
  cape: 'shoulder_line',
  underlayer: 'chest',
  legwear: 'hip',
  shoe_base: 'foot_base',
  bottom: 'waist',
  shoe_shaft: 'ankle_l',
  dress: 'neck',
  top: 'shoulder_line',
  belt: 'waist',
  outer: 'shoulder_line',
  neckwear: 'chin',
  gloves: 'wrist_l',
  bag: 'shoulder_r',
  hair_front: 'head_top',
  headwear: 'head_top',
  eyewear: 'eye_line',
};

/**
 * 贴合规则（CERE-11）。
 *
 * 旧的绝对像素参考框是照插画娃娃比例定的，换成写实模特后整套失准，已删除。
 * 现在每个槽位只声明三件事，具体像素由底图锚点 + 底图轮廓实测宽度算出来：
 *
 *   widthRef  这件衣服的宽度该参照身体哪一段（实测轮廓宽度）
 *   widthK    相对该段的宽度系数（版型宽松量，>1 表示衣服比身体宽）
 *   attach    素材的哪条边贴到哪个锚点上
 *
 * 真实照片素材的绝对尺寸毫无规律（一件毛衣可能是 900px 也可能是 3000px），
 * 所以缩放必须由「身体宽度 ÷ 素材宽度」决定，不能由素材自身尺寸决定。
 */
export type WidthRef =
  | 'shoulder' | 'chest' | 'waist' | 'hip' | 'thigh' | 'knee' | 'ankle' | 'head';

export type AttachEdge = 'top' | 'bottom' | 'center';
export type GarmentPair = 'shoulder' | 'waist';

/**
 * 这一槽位的缩放由什么驱动。
 *
 * `width`  按宽度：适合下装、鞋、配饰 —— 它们的包围盒宽度就是「这件东西有多宽」。
 * `length` 按衣长：适合上装、外套、连衣裙 —— 它们的包围盒**宽度不可靠**。
 *          摊平拍的毛衣，包围盒宽度是**袖展**（比肩宽大一倍多）；挂着拍的
 *          卫衣，包围盒宽度只有胸宽的一半。两种都很常见，按宽度缩必错一头。
 *          衣长（上沿→下摆）在两种拍法下都还算稳。
 *
 * 两种都不是万能的，所以按衣长解出来的还要被按宽度解出来的夹一道
 * （见 `fit.ts` 的 WIDTH_GUARD），谁离谱都不至于离谱到底。
 */
export type FitDriver = 'width' | 'length';

export interface PlacementRule {
  widthRef: WidthRef;
  widthK: number;
  /** 素材上的成对语义点；可信时用它对齐，而不是拿整张包围盒猜位置 */
  alignPair?: GarmentPair;
  /** 缺省 = 'width' */
  driver?: FitDriver;
  /** driver='length' 时，衣长该覆盖身上哪一段：[起点锚点, 终点锚点, 终点再加几个肩宽] */
  lengthSpan?: [AnchorName, AnchorName, number];
  /** 水平方向偏移，单位同样是肩宽的倍数（包这类挂在体侧的件用） */
  offsetXK?: number;
  /** 竖直方向贴到哪个锚点 */
  anchor: AnchorName;
  /** 左右成对锚点默认取中点；包等单侧挂件必须钉到指定侧。 */
  singleAnchor?: boolean;
  /** 素材的哪条边贴上去 */
  edge: AttachEdge;
  /** 同一 landmark 同时提供横纵挂载点；包用手柄顶点，而不是位图中心。 */
  attachLandmark?: 'top_edge' | 'hem';
  /** 贴合后再沿 y 偏移，单位是「肩宽的倍数」，与画布尺寸无关 */
  offsetK?: number;
  /** 素材 top_edge → hem 要覆盖的身体区间：[起点锚点, 起点偏移K, 终点锚点, 终点偏移K] */
  verticalSpan?: [AnchorName, number, AnchorName, number];
}

export const SLOT_PLACEMENT: Record<Slot, PlacementRule> = {
  top: {
    widthRef: 'shoulder', widthK: 1.06, alignPair: 'shoulder', driver: 'length',
    lengthSpan: ['shoulder_line', 'hip', 0], anchor: 'shoulder_line', edge: 'top', offsetK: -0.05,
  },
  underlayer: {
    widthRef: 'chest', widthK: 1.0, driver: 'length',
    lengthSpan: ['shoulder_line', 'hip', -0.05], anchor: 'shoulder_line', edge: 'top', offsetK: 0.02,
  },
  outer: {
    widthRef: 'shoulder', widthK: 1.16, alignPair: 'shoulder', driver: 'length',
    lengthSpan: ['shoulder_line', 'hip', 0.3], anchor: 'shoulder_line', edge: 'top', offsetK: -0.08,
  },
  dress: {
    widthRef: 'shoulder', widthK: 1.04, alignPair: 'shoulder', driver: 'length',
    lengthSpan: ['shoulder_line', 'knee', 0], anchor: 'shoulder_line', edge: 'top', offsetK: -0.04,
  },
  cape: {
    widthRef: 'shoulder', widthK: 1.24, driver: 'length',
    lengthSpan: ['shoulder_line', 'hip', 0], anchor: 'shoulder_line', edge: 'top', offsetK: -0.06,
  },
  bottom:     { widthRef: 'hip',      widthK: 1.06, alignPair: 'waist', anchor: 'waist', edge: 'top', offsetK: -0.02 },
  legwear:    { widthRef: 'hip',      widthK: 0.99, anchor: 'hip',           edge: 'top', offsetK: -0.12 },
  belt:       { widthRef: 'waist',    widthK: 1.05, anchor: 'waist',         edge: 'center' },
  // 鞋的宽度参照是**双脚站距**（两只脚踝外缘之间的整幅），不是单只脚踝 ——
  // 一张鞋子素材里通常是两只鞋并排，按单脚踝缩会小掉一半。
  shoe_base:  {
    widthRef: 'ankle', widthK: 1.12, anchor: 'foot_base', edge: 'bottom',
    verticalSpan: ['ankle_l', -0.16, 'foot_base', 0],
  },
  shoe_shaft: {
    widthRef: 'ankle', widthK: 1.12, anchor: 'foot_base', edge: 'bottom',
    verticalSpan: ['ankle_l', -0.16, 'foot_base', 0],
  },
  neckwear:   { widthRef: 'shoulder', widthK: 0.66, anchor: 'chin',          edge: 'top', offsetK: 0.1 },
  gloves:     { widthRef: 'shoulder', widthK: 1.18, anchor: 'wrist_l',       edge: 'center' },

  bag:        { widthRef: 'shoulder', widthK: 0.5, anchor: 'hip', edge: 'center', offsetXK: 0.58 },
  headwear:   { widthRef: 'head',     widthK: 1.22, anchor: 'eye_line',      edge: 'bottom', offsetK: 0.02 },
  eyewear:    { widthRef: 'head',     widthK: 0.94, anchor: 'eye_line',      edge: 'center' },
  hair_back:  { widthRef: 'head',     widthK: 1.12, anchor: 'head_top',      edge: 'top' },
  hair_front: { widthRef: 'head',     widthK: 1.06, anchor: 'head_top',      edge: 'top' },
};

/** 实测轮廓宽度取样的位置：锚点 y + 肩宽倍数偏移 */
export const WIDTH_PROBE: Record<WidthRef, { anchor: AnchorName; offsetK?: number }> = {
  head:     { anchor: 'eye_line' },
  shoulder: { anchor: 'shoulder_line', offsetK: 0.08 },
  chest:    { anchor: 'chest' },
  waist:    { anchor: 'waist' },
  hip:      { anchor: 'hip' },
  thigh:    { anchor: 'crotch', offsetK: 0.35 },
  knee:     { anchor: 'knee' },
  ankle:    { anchor: 'ankle_l' },
};

/** 连衣裙互斥：穿裙卸上下装，反之亦然 */
export const MUTEX: Partial<Record<Slot, Slot[]>> = {
  dress: ['top', 'bottom'],
  top: ['dress'],
  bottom: ['dress'],
};

export type BodyType = 'base_f02' | 'base_m02';
export const BODY_TYPES: BodyType[] = ['base_f02', 'base_m02'];
export const BODY_LABEL: Record<BodyType, string> = {
  base_f02: '女模特 · F02',
  base_m02: '男模特 · M02',
};

/**
 * 体型只改宽度、锚点不变（CERE-2 §1）。
 *
 * CERE-11 起衣物宽度直接量该体型底图的轮廓实测宽度，不再乘这张系数表，
 * 所以这里只留给「底图读不出轮廓」时兜底。
 */
export const BODY_WIDTH_FACTOR: Record<BodyType, number> = { base_f02: 1, base_m02: 1 };

/** 6 档肤色，OkLab 明度均匀分布（CERE-2 §4.1） */
export const SKIN_TONES = [
  '#F7DFCB', '#EFC9AC', '#DFAC83', '#C1885E', '#96603D', '#5F3F29',
] as const;

export const HAIR_STYLES = [
  { id: 'h01', name: '长直发' },
  { id: 'h02', name: '短发' },
  { id: 'h03', name: '丸子头' },
] as const;

export type ColorFamily =
  | 'neutral_warm' | 'neutral_cool' | 'black_grey' | 'white_cream'
  | 'red' | 'orange' | 'yellow' | 'green' | 'cyan' | 'blue'
  | 'purple' | 'pink' | 'brown' | 'multi';

export const COLOR_FAMILY_LABEL: Record<ColorFamily, string> = {
  white_cream: '白 / 米', black_grey: '黑 / 灰', neutral_warm: '暖中性', neutral_cool: '冷中性',
  red: '红', orange: '橙', yellow: '黄', green: '绿', cyan: '青', blue: '蓝',
  purple: '紫', pink: '粉', brown: '棕', multi: '多色',
};

export const COLOR_FAMILY_SWATCH: Record<ColorFamily, string> = {
  white_cream: '#F2EBE0', black_grey: '#4A4A50', neutral_warm: '#B9AC9A', neutral_cool: '#9AA3AC',
  red: '#C0392B', orange: '#D77A3A', yellow: '#D8B33F', green: '#5C8A4A', cyan: '#4A8A8A',
  blue: '#3F5F8F', purple: '#7A5A8F', pink: '#C98494', brown: '#8A6242', multi: '#8E7CC3',
};

/** 衣橱一级 tab */
export const WARDROBE_TABS: { key: Category | 'all' | 'accessory'; label: string }[] = [
  { key: 'all', label: '全部' },
  { key: 'top', label: '上装' },
  { key: 'bottom', label: '下装' },
  { key: 'dress', label: '连衣裙' },
  { key: 'outer', label: '外套' },
  { key: 'shoe', label: '鞋' },
  { key: 'bag', label: '包' },
  { key: 'accessory', label: '配饰' },
];

export const ACCESSORY_CATEGORIES: Category[] = [
  'headwear', 'eyewear', 'neckwear', 'belt', 'gloves', 'legwear', 'underlayer', 'other',
];
