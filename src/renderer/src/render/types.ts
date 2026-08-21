import type { Asset, BaseBodySet, Fit } from '@shared/types';
import type { OcclusionConfig, Tuck } from '@shared/occlusion';
import type { BodyType, Slot } from '@shared/spec';

import type { BodyMask } from './mask';

/** 裁切指令，全部已换算成**显示像素**（画布坐标 × displayScale） */
export interface LayerClip {
  /** 区间守卫：只保留这一段 y */
  keepFrom?: number;
  keepTo?: number;
  keepFeather: number;
  /** 身体遮罩裁切 */
  mask?: {
    /** 外扩量，单位是画布坐标（遮罩自己按位图尺寸换算） */
    growCanvas: number;
    from?: number;
    to?: number;
    feather: number;
  };
  /** 被别的层挖掉 */
  erasedBy: {
    key: string;
    from?: number;
    to?: number;
    grow: number;
    feather: number;
  }[];
}

export interface StageLayer {
  key: string;
  url: string;
  z: number;
  mode: 'fill' | 'multiply' | 'normal';
  tint?: string;
  /** 画布坐标下的目标矩形 */
  dx: number;
  dy: number;
  dw: number;
  dh: number;
  opacity: number;
  /** 底图层还是衣物层 —— 只有衣物层做羽化与接触阴影 */
  kind: 'base' | 'garment';
  /** 抠图边缘羽化半径（输出像素）。0 = 不处理 */
  feather: number;
  /** 接触阴影：衣物压在下层上时交界处的那圈投影 */
  contact?: { blur: number; offset: number; alpha: number };
  /** 高亮描边（图层面板里点中某层时用） */
  highlight?: boolean;
  /** 遮挡与身体遮罩裁切；没有就是不裁 */
  clip?: LayerClip;
}

export interface StageScene {
  width: number;
  height: number;
  background: 'none' | 'solid' | 'studio_warm' | 'studio_cool';
  backgroundColor?: string;
  /** 落地投影，让人物不像浮在空中 */
  groundShadow: boolean;
  layers: StageLayer[];
  /**
   * 取一张按 `growCanvas`（画布坐标单位）外扩好的身体遮罩。
   * 底图读不出像素时返回 null —— 合成器要能接受「这次不裁」。
   */
  bodyMask: (growCanvas: number) => Promise<BodyMask | null>;
}

export interface WornInput {
  asset: Asset;
  slot: Slot;
  z: number;
  fit: Fit;
  hidden: boolean;
  highlight: boolean;
  /** 上装下摆塞进腰里还是放下来 */
  tuck: Tuck;
}

export interface RenderInput {
  base: BaseBodySet;
  body: BodyType;
  /** 肤色档位下标，对应 base.tones */
  tone: number;
  hairStyle: string;
  hairHex: string;
  worn: WornInput[];
  background: StageScene['background'];
  backgroundColor?: string;
  /** 关掉羽化与接触阴影，用来出「处理前 / 处理后」对照 */
  rawCompositing?: boolean;
  /** 关掉遮挡与身体遮罩裁切，用来出「有遮挡 / 无遮挡」对照 */
  noOcclusion?: boolean;
  /** 规则表；缺省用内置默认表 */
  occlusion?: OcclusionConfig;
}

export interface ExportOptions {
  transparent: boolean;
  /** 相对规范画布的倍数，1 = 原始分辨率 */
  scale: number;
}
