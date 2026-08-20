import type { ExportOptions, RenderInput } from '@/render/types';

import { LayeredTryOn } from './layered';
import type { TryOnEngine } from './types';

/**
 * 生成式试穿（VTON）引擎的接入点。
 *
 * CERE-8 正在验证选型：真人模特照片 + 衣物照片 → 模型生成一张「穿上了」的图。
 * 结论出来后，只要实现下面这个 backend 接口并注册进来，界面不用改一行。
 *
 * 在 backend 缺席期间：状态如实报「未接入」，渲染回落到分层贴图，
 * 不假装能用，也不把界面卡死。
 */
export interface VtonBackend {
  readonly name: string;
  /** 一次生成一张成品图（dataURL）。分辨率由后端决定 */
  generate(req: {
    baseImageUrl: string;
    garments: { url: string; category: string }[];
    signal?: AbortSignal;
  }): Promise<string>;
}

let backend: VtonBackend | null = null;

/** CERE-8 结论落地时调用这一个函数即可接管渲染 */
export function registerVtonBackend(impl: VtonBackend | null): void {
  backend = impl;
}

export class GenerativeTryOn implements TryOnEngine {
  readonly id = 'vton';
  readonly name = 'AI 试穿（VTON）';
  readonly kind = 'generative' as const;
  readonly description = '真人模特原地换装，由生成模型输出整图';

  private readonly fallback = new LayeredTryOn();

  status(): { available: boolean; reason?: string } {
    return backend
      ? { available: true }
      : { available: false, reason: '后端未接入（CERE-8 选型验证中），当前回落到分层贴图' };
  }

  mount(canvas: HTMLCanvasElement): void {
    this.fallback.mount(canvas);
  }

  async render(input: RenderInput, displayScale: number): Promise<void> {
    // backend 接入后这里换成 generate() + 贴整图；在那之前老老实实回落
    await this.fallback.render(input, displayScale);
  }

  toDataURL(input: RenderInput, opts: ExportOptions): Promise<string> {
    return this.fallback.toDataURL(input, opts);
  }

  // backend 接管后整图由模型生成，「这个像素属于哪一件」无从谈起 —— 如实返回
  // null，界面会收起画布上的拖拽微调。回落到分层贴图期间照常可用。
  hitTest(input: RenderInput, displayScale: number, x: number, y: number): Promise<string | null> {
    return backend ? Promise.resolve(null) : this.fallback.hitTest(input, displayScale, x, y);
  }

  fitUnit(input: RenderInput, displayScale: number): Promise<number | null> {
    return backend ? Promise.resolve(null) : this.fallback.fitUnit(input, displayScale);
  }

  destroy(): void {
    this.fallback.destroy();
  }
}
