/**
 * 遮挡规则求解器。
 *
 * 输入：当前穿着的槽位 + 底图实测（锚点、肩宽）+ 规则表。
 * 输出：每件衣物一份「怎么裁」的指令，交给合成器执行。
 *
 * 这里**只算不画**，合成器**只画不判**。规则表长什么样、加了什么品类，
 * 合成器一个字都不用改。
 */

import {
  DEFAULT_OCCLUSION, defaultTuck,
  type Band, type BandEdge, type OcclusionConfig, type Tuck,
} from '@shared/occlusion';
import type { Slot } from '@shared/spec';

import type { BaseMetrics } from './metrics';

/** 一件衣物的裁切指令。所有 y 与长度都是**画布坐标**，未乘显示缩放 */
export interface LayerOcclusion {
  /** 区间守卫：只保留 [keepFrom, keepTo] 这一段 */
  keepFrom?: number;
  keepTo?: number;
  keepFeather: number;
  /** 身体遮罩裁切；不裁时为 undefined */
  mask?: {
    /** 轮廓外扩量（画布坐标单位） */
    grow: number;
    from?: number;
    to?: number;
    feather: number;
  };
  /** 被哪些层挖掉 */
  erasedBy: {
    /** 遮挡方的层 key（= assetId） */
    key: string;
    from?: number;
    to?: number;
    grow: number;
    feather: number;
    ruleId: string;
  }[];
}

export interface WornRef {
  key: string;
  slot: Slot;
  tuck: Tuck;
}

function edgeY(e: BandEdge | undefined, m: BaseMetrics): number | undefined {
  if (!e) return undefined;
  const a = m.anchors[e.anchor];
  if (!a) return undefined;
  return a.y + (e.offsetK ?? 0) * m.shoulderW;
}

function bandY(b: Band | undefined, m: BaseMetrics): { from?: number; to?: number } {
  if (!b) return {};
  return { from: edgeY(b.from, m), to: edgeY(b.to, m) };
}

/** 取交集：两个都给了取更紧的那个 */
function tighten(a: number | undefined, b: number | undefined, pick: 'max' | 'min'): number | undefined {
  if (a === undefined) return b;
  if (b === undefined) return a;
  return pick === 'max' ? Math.max(a, b) : Math.min(a, b);
}

/**
 * 求解整套搭配的遮挡关系。
 *
 * 挖除分两步是有原因的：先各自按守卫与身体遮罩裁好，再互相挖。如果边裁边挖，
 * 结果就依赖处理顺序 —— 连衣裙（z 高）要挖打底裤（z 低），裤子（z 低）要挖鞋
 * （z 高），两个方向都存在，没有一个顺序能同时满足。
 */
export function resolveOcclusion(
  worn: WornRef[],
  m: BaseMetrics,
  config: OcclusionConfig = DEFAULT_OCCLUSION,
): Map<string, LayerOcclusion> {
  const out = new Map<string, LayerOcclusion>();
  const bySlot = new Map<Slot, WornRef>();
  for (const w of worn) bySlot.set(w.slot, w);

  for (const w of worn) {
    const item: LayerOcclusion = { keepFeather: 0, erasedBy: [] };

    // ---- 区间守卫（多条同时命中就取交集）
    for (const g of config.guards) {
      if (!g.slots.includes(w.slot)) continue;
      const { from, to } = bandY(g.keep, m);
      item.keepFrom = tighten(item.keepFrom, from, 'max');
      item.keepTo = tighten(item.keepTo, to, 'min');
      item.keepFeather = Math.max(item.keepFeather, g.featherK * m.shoulderW);
    }

    // ---- 身体遮罩
    const policy = config.mask[w.slot];
    if (policy && policy.mode === 'silhouette') {
      const { from, to } = bandY(policy.band, m);
      item.mask = {
        grow: policy.dilateK * m.shoulderW,
        from,
        to,
        feather: policy.featherK * m.shoulderW,
      };
    }

    // ---- 成对挖除
    for (const rule of config.occlusions) {
      if (rule.under !== w.slot) continue;
      const over = bySlot.get(rule.over);
      if (!over) continue;
      if (rule.when === 'tucked' && w.tuck !== 'in') continue;
      if (rule.when === 'untucked' && w.tuck !== 'out') continue;
      const { from, to } = bandY(rule.band, m);
      item.erasedBy.push({
        key: over.key,
        from,
        to,
        grow: (rule.growK ?? 0) * m.shoulderW,
        feather: (rule.featherK ?? 0) * m.shoulderW,
        ruleId: rule.id,
      });
    }

    out.set(w.key, item);
  }

  return out;
}

/** 一件素材默认塞不塞衣角，用户没手动改过时用这个 */
export { defaultTuck };
