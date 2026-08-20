import { Canvas2DCompositor } from '@/render/canvas2d';
import { baseMetrics } from '@/render/metrics';
import { hitTest } from '@/render/pick';
import { buildScene } from '@/render/scene';
import type { ExportOptions, RenderInput } from '@/render/types';

import type { TryOnEngine } from './types';

/**
 * 分层贴图引擎：底图 + 按锚点贴上去的真实衣物照片，加羽化与接触阴影。
 * 离线、即时、零成本，效果上限见 `docs/render-contract.md`。
 */
export class LayeredTryOn implements TryOnEngine {
  readonly id = 'layered';
  readonly name = '分层贴图';
  readonly kind = 'layered' as const;
  readonly description = '真实衣物照片按人体锚点贴合，离线即时渲染';

  private readonly compositor = new Canvas2DCompositor();

  status(): { available: boolean } {
    return { available: true };
  }

  mount(canvas: HTMLCanvasElement): void {
    this.compositor.mount(canvas);
  }

  async render(input: RenderInput, displayScale: number): Promise<void> {
    const m = await baseMetrics(input.base, input.tone);
    await this.compositor.render(buildScene(input, m, displayScale));
  }

  async toDataURL(input: RenderInput, opts: ExportOptions): Promise<string> {
    const m = await baseMetrics(input.base, input.tone);
    // 导出时先按 1:1 建场景，再交给合成器按倍数放大，避免两次缩放叠加
    return this.compositor.toDataURL(buildScene(input, m, 1), opts);
  }

  async hitTest(input: RenderInput, displayScale: number, x: number, y: number): Promise<string | null> {
    const m = await baseMetrics(input.base, input.tone);
    return hitTest(buildScene(input, m, displayScale), x, y);
  }

  async fitUnit(input: RenderInput, displayScale: number): Promise<number | null> {
    const m = await baseMetrics(input.base, input.tone);
    return m.shoulderW * displayScale;
  }

  destroy(): void {
    this.compositor.destroy();
  }
}
