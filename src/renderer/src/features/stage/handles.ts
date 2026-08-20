/**
 * 舞台句柄单例。
 *
 * 导出 PNG 和 Look 封面都要「再画一遍当前场景」，需要拿到渲染引擎和当前
 * 渲染输入。用单例而不是层层传 props：需要它的地方（右侧面板、截图脚本）
 * 和产生它的地方（舞台画布）在组件树上离得很远，传下去只会污染中间层。
 */

import type { RenderInput } from '@/render/types';
import type { TryOnEngine } from '@/tryon/types';

export const stageHandles: {
  engine: { current: TryOnEngine | null };
  input: { current: RenderInput | null };
} = {
  engine: { current: null },
  input: { current: null },
};

/** 按给定倍率重画当前场景并返回 PNG dataURL；舞台还没就绪时返回 null */
export async function capture(scale: number, transparent: boolean): Promise<string | null> {
  const engine = stageHandles.engine.current;
  const input = stageHandles.input.current;
  if (!engine || !input) return null;
  return engine.toDataURL(input, { transparent, scale });
}

/** 云端试穿只需要人物底图；衣物作为独立输入发送，不能先把分层预览烘进去。 */
export async function captureBase(scale: number): Promise<string | null> {
  const engine = stageHandles.engine.current;
  const input = stageHandles.input.current;
  if (!engine || !input) return null;
  return engine.toDataURL({ ...input, worn: [], rawCompositing: false }, { transparent: false, scale });
}
