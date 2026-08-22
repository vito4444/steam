/**
 * 遮挡规则表（CERE-14）。
 *
 * 这个文件是**数据**，不是逻辑。渲染函数只负责执行这里声明的规则，
 * 一条 if 都不许写死在合成器里 —— 以后加品类、改穿法，只动这张表。
 *
 * 五张表，各管一件事：
 *
 *   LAYER_ORDER      谁画在谁上面（z）。能靠画序表达的遮挡就不做像素运算。
 *   EXCLUSION_RULES  互斥：穿上这件，哪些槽位必须清空（连衣裙 vs 上下装）。
 *   OCCLUSION_RULES  成对挖除：z 表达不了的遮挡，用下层减上层的 alpha 实现。
 *                    典型是「裤脚盖鞋帮」—— 裤子在鞋之下，却要盖住鞋筒。
 *   REGION_GUARDS    区间守卫：一件衣服只允许出现在身上的哪一段。
 *                    围巾不许爬到下巴以上，帽子不许垂到下巴以下。
 *   MASK_POLICY      身体遮罩：超出身体轮廓多少要被裁掉，以及哪一段不裁
 *                    （裙摆、大衣下摆本来就该外扩，裁了反而假）。
 *
 * 所有长度单位都是**肩宽的倍数**，不是画布像素 —— 换一版底图、换一种画布
 * 尺寸，规则不用重标。
 *
 * 运行期还能被 `<素材库>/occlusion.json` 覆盖，见 `mergeOcclusionConfig`。
 */

import type { AnchorName, Slot } from './spec';
import { SLOT_Z } from './spec';

/** 区间端点：某个锚点 + 肩宽倍数的偏移 */
export interface BandEdge {
  anchor: AnchorName;
  offsetK?: number;
}

export interface Band {
  from?: BandEdge;
  to?: BandEdge;
}

// ---------------------------------------------------------------- 1. 画序

/**
 * 画序即最基础的遮挡：z 大的后画，压在 z 小的上面。
 *
 * 三条硬要求写在这里（CERE-14 issue §1）：
 *   外套盖上装        outer(70) > top(55)
 *   袜子在裤之上      legwear(44) > bottom(40)
 *   袜子在鞋之下      shoe_base(46) > legwear(44)
 *
 * 「裤脚盖鞋帮」与上面第三条方向相反，z 表达不了，交给 OCCLUSION_RULES。
 */
export const LAYER_ORDER: Record<Slot, number> = { ...SLOT_Z };

// ---------------------------------------------------------------- 2. 互斥

export interface ExclusionRule {
  id: string;
  /** 穿上这个槽位 */
  slot: Slot;
  /** 就清空这些槽位 */
  clears: Slot[];
  note: string;
}

export const EXCLUSION_RULES: ExclusionRule[] = [
  { id: 'dress_clears_separates', slot: 'dress', clears: ['top', 'bottom'], note: '连衣裙上身，上装与下装同时卸下' },
  { id: 'top_clears_dress', slot: 'top', clears: ['dress'], note: '穿上装即脱连衣裙' },
  { id: 'bottom_clears_dress', slot: 'bottom', clears: ['dress'], note: '穿下装即脱连衣裙' },
];

// ---------------------------------------------------------------- 3. 成对挖除

/** 什么时候生效 */
export type OcclusionWhen = 'always' | 'tucked' | 'untucked';

export interface OcclusionRule {
  id: string;
  /** 遮挡方：用它的轮廓去挖 */
  over: Slot;
  /** 被遮方：alpha 被挖掉 */
  under: Slot;
  /** 只在这一段 y 区间内挖；缺省 = 整幅 */
  band?: Band;
  /** 挖之前把遮挡方的轮廓外扩多少（肩宽倍数），让交界不留一线残边 */
  growK?: number;
  /** 交界羽化（肩宽倍数） */
  featherK?: number;
  when: OcclusionWhen;
  note: string;
}

export const OCCLUSION_RULES: OcclusionRule[] = [
  {
    id: 'hem_over_shoe',
    over: 'bottom',
    under: 'shoe_base',
    band: { to: { anchor: 'ankle_l', offsetK: 0.06 } },
    growK: 0.004,
    featherK: 0.006,
    when: 'always',
    note: '裤脚盖鞋帮：鞋在裤之上（z），但踝线以上被裤子挖掉，靴筒才不会穿出裤腿',
  },
  {
    id: 'hem_over_boot_shaft',
    over: 'bottom',
    under: 'shoe_shaft',
    band: { to: { anchor: 'ankle_l', offsetK: 0.06 } },
    growK: 0.004,
    featherK: 0.006,
    when: 'always',
    note: '同上，单独出的靴筒层一并处理',
  },
  {
    id: 'tuck_top_into_bottom',
    over: 'bottom',
    under: 'top',
    band: { from: { anchor: 'waist', offsetK: -0.015 } },
    growK: 0.002,
    featherK: 0.01,
    when: 'tucked',
    note: '上装塞进腰里：腰线以下的上装被下装挖掉，露出腰线',
  },
  {
    id: 'tuck_underlayer_into_bottom',
    over: 'bottom',
    under: 'underlayer',
    band: { from: { anchor: 'waist', offsetK: -0.015 } },
    growK: 0.002,
    featherK: 0.01,
    when: 'tucked',
    note: '内层跟着上装一起塞',
  },
  {
    id: 'dress_over_legwear_top',
    over: 'dress',
    under: 'legwear',
    band: { to: { anchor: 'hip', offsetK: 0.1 } },
    growK: 0.002,
    featherK: 0.01,
    when: 'always',
    note: '打底裤/裤袜的腰头被裙子盖住，不该从裙腰上方冒出来',
  },
];

// ---------------------------------------------------------------- 4. 区间守卫

export interface RegionGuard {
  id: string;
  slots: Slot[];
  /** 只保留这一段里的像素 */
  keep: Band;
  featherK: number;
  note: string;
}

export const REGION_GUARDS: RegionGuard[] = [
  {
    id: 'no_garment_on_face',
    slots: ['top', 'underlayer', 'dress', 'outer', 'cape', 'belt', 'bottom', 'legwear'],
    keep: { from: { anchor: 'chin', offsetK: -0.02 } },
    featherK: 0.012,
    note: '下巴以上不许出现衣服 —— 领口/帽兜贴过头会直接盖住脸',
  },
  {
    id: 'scarf_below_chin',
    slots: ['neckwear'],
    keep: { from: { anchor: 'chin', offsetK: 0.005 } },
    featherK: 0.016,
    note: '围巾只能从下巴往下 —— fig-05 里围巾糊了半张脸就是缺这一条',
  },
  {
    id: 'headwear_above_chin',
    slots: ['headwear'],
    keep: { to: { anchor: 'chin', offsetK: 0.02 } },
    featherK: 0.02,
    note: '帽子不许垂到下巴以下',
  },
  {
    id: 'eyewear_on_face',
    slots: ['eyewear'],
    keep: { from: { anchor: 'head_top' }, to: { anchor: 'chin' } },
    featherK: 0.008,
    note: '眼镜只在头部范围内',
  },
  {
    id: 'shoes_on_ground',
    slots: ['shoe_base'],
    keep: { from: { anchor: 'crotch', offsetK: 0.05 } },
    featherK: 0.02,
    note: '鞋只能出现在裆线以下 —— 抠图带上裤腿时不至于整条腿都变成鞋；'
      + '放到裆线而不是膝盖，是为了给过膝长靴留出靴筒',
  },
];

// ---------------------------------------------------------------- 5. 身体遮罩

export interface MaskPolicy {
  /** none = 不裁；silhouette = 裁到（外扩后的）身体轮廓 */
  mode: 'none' | 'silhouette';
  /** 轮廓外扩量（肩宽倍数）= 版型宽松量。宽松款给大一点 */
  dilateK: number;
  /** 只在这一段裁；缺省 = 全高 */
  band?: Band;
  featherK: number;
  note: string;
}

const NO_MASK: MaskPolicy = { mode: 'none', dilateK: 0, featherK: 0, note: '不裁' };

/**
 * 裁多少是有代价的：裁得紧，飘在空中的袖子没了，但宽松版型也被削成紧身；
 * 裁得松，宽松版型保住了，飘在体侧的抠图残留也留下了。下面这组值是按
 * 「宁可让宽松款略微贴身，也不要让衣服悬空」定的。
 *
 * 下摆会自然外扩的件（裙、大衣、裤）只裁到胯/裆附近，再往下放开。
 */
export const MASK_POLICY: Record<Slot, MaskPolicy> = {
  top: {
    mode: 'silhouette', dilateK: 0.13, featherK: 0.014,
    note: '上装：肩宽的 13% 宽松量，全身裁 —— 摊平拍的袖子对不上手臂时直接削掉，好过悬在体侧',
  },
  underlayer: {
    mode: 'silhouette', dilateK: 0.06, featherK: 0.012,
    note: '内层贴身，宽松量最小',
  },
  outer: {
    mode: 'silhouette', dilateK: 0.17, band: { to: { anchor: 'hip', offsetK: 0.45 } }, featherK: 0.16,
    note: '外套宽松量最大；胯下 0.55 肩宽以后放开，长大衣下摆本来就外扩',
  },
  dress: {
    mode: 'silhouette', dilateK: 0.12, band: { to: { anchor: 'hip', offsetK: 0.04 } }, featherK: 0.15,
    note: '连衣裙只裁到胯，往下用 0.15 肩宽的长羽化慢慢放开 —— 裁切区间的边界'
      + '必须是渐变，硬边会在裙摆上留一条横切线，比不裁还显眼',
  },
  bottom: {
    mode: 'silhouette', dilateK: 0.14, band: { to: { anchor: 'crotch', offsetK: 0.2 } }, featherK: 0.13,
    note: '下装只裁腰胯段，裤腿/裙摆往下放开',
  },
  legwear: {
    mode: 'silhouette', dilateK: 0.07, band: { to: { anchor: 'ankle_l', offsetK: 0.05 } }, featherK: 0.06,
    note: '袜/打底贴身',
  },
  cape: {
    mode: 'silhouette', dilateK: 0.3, band: { to: { anchor: 'chest' } }, featherK: 0.14,
    note: '披风只在肩胸段收一下，往下完全放开',
  },
  belt: { mode: 'silhouette', dilateK: 0.1, featherK: 0.01, note: '腰带贴腰' },
  neckwear: NO_MASK,
  gloves: NO_MASK,
  bag: NO_MASK,
  shoe_base: NO_MASK,
  shoe_shaft: NO_MASK,
  headwear: NO_MASK,
  eyewear: NO_MASK,
  hair_back: NO_MASK,
  hair_front: NO_MASK,
};

// ---------------------------------------------------------------- 组装 & 覆盖

export interface OcclusionConfig {
  order: Record<Slot, number>;
  exclusions: ExclusionRule[];
  occlusions: OcclusionRule[];
  guards: RegionGuard[];
  mask: Record<Slot, MaskPolicy>;
}

export const DEFAULT_OCCLUSION: OcclusionConfig = {
  order: LAYER_ORDER,
  exclusions: EXCLUSION_RULES,
  occlusions: OCCLUSION_RULES,
  guards: REGION_GUARDS,
  mask: MASK_POLICY,
};

/** `occlusion.json` 里能覆盖什么 —— 全部可选，只写要改的那几条 */
export interface OcclusionOverride {
  order?: Partial<Record<Slot, number>>;
  /** 按 id 覆盖单条规则；`disabled: true` 表示停用这条 */
  occlusions?: Record<string, Partial<OcclusionRule> & { disabled?: boolean }>;
  guards?: Record<string, Partial<RegionGuard> & { disabled?: boolean }>;
  mask?: Partial<Record<Slot, Partial<MaskPolicy>>>;
  /** 追加的新规则（加品类时用） */
  addOcclusions?: OcclusionRule[];
  addGuards?: RegionGuard[];
}

/**
 * 把用户覆盖并进默认表。
 *
 * 覆盖文件放在素材库根目录 `occlusion.json`，改完重启应用生效。写坏了不会崩，
 * 认不出来的键直接忽略 —— 这张表要能被非开发者改，容错必须比校验重要。
 */
export function mergeOcclusionConfig(base: OcclusionConfig, ov?: OcclusionOverride | null): OcclusionConfig {
  if (!ov) return base;

  const order = { ...base.order, ...(ov.order ?? {}) };

  const occlusions = base.occlusions
    .map((r) => {
      const patch = ov.occlusions?.[r.id];
      if (!patch) return r;
      if (patch.disabled) return null;
      return { ...r, ...patch } as OcclusionRule;
    })
    .filter((r): r is OcclusionRule => !!r)
    .concat(ov.addOcclusions ?? []);

  const guards = base.guards
    .map((g) => {
      const patch = ov.guards?.[g.id];
      if (!patch) return g;
      if (patch.disabled) return null;
      return { ...g, ...patch } as RegionGuard;
    })
    .filter((g): g is RegionGuard => !!g)
    .concat(ov.addGuards ?? []);

  const mask = { ...base.mask };
  for (const [slot, patch] of Object.entries(ov.mask ?? {})) {
    const s = slot as Slot;
    if (mask[s] && patch) mask[s] = { ...mask[s], ...patch };
  }

  return { order, exclusions: base.exclusions, occlusions, guards, mask };
}

// ---------------------------------------------------------------- 塞衣角

export type Tuck = 'in' | 'out';

/**
 * 上装下摆压在下装腰线之上还是之下，**按品类决定**（issue §1）。
 *
 * 明确衣长优先；没有衣长的照片上装用“塞入”先验，避免遮住同照下装。
 * 其他未知素材仍默认外放。用户能在贴合面板里逐件改，改动记进 Look。
 */
export function defaultTuck(
  attrs: { length?: string; fit?: string } | undefined,
  slot: Slot,
  preferTuckedWhenUnknown = false,
): Tuck {
  if (slot !== 'top' && slot !== 'underlayer') return 'out';
  const len = attrs?.length;
  if (len === 'crop' || len === 'waist') return 'in';
  if (len) return 'out';
  if (attrs?.fit === 'slim' && !len) return 'in';
  if (preferTuckedWhenUnknown) return 'in';
  return 'out';
}
