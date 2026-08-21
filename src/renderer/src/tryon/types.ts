import type { ExportOptions, RenderInput } from '@/render/types';

/**
 * 换装渲染引擎。
 *
 * 分层贴图（把真实衣服照片按锚点贴到模特身上）有一个跑不掉的天花板：
 * 肩宽、透视、褶皱对不齐时仍然会有贴纸感。CERE-8 正在验证生成式试穿
 * （VTON）—— 真人模特不变、原地换装 —— 如果成立，最终渲染会整体换成
 * 模型生成。
 *
 * 所以渲染必须是**可替换的一层**：界面只依赖这个接口，不依赖分层贴图。
 * 换引擎 = 注册一个新的实现，界面与素材库一个字不用改。
 */
export interface TryOnEngine {
  readonly id: string;
  readonly name: string;
  readonly kind: 'layered' | 'generative';
  readonly description: string;

  /** 引擎当前能不能用；不能用要说清楚为什么，界面会原样显示 */
  status(): { available: boolean; reason?: string };

  mount(canvas: HTMLCanvasElement): void;
  render(input: RenderInput, displayScale: number): Promise<void>;
  toDataURL(input: RenderInput, opts: ExportOptions): Promise<string>;
  destroy(): void;

  /**
   * 手动微调用的两个可选能力。生成式引擎给不出「某个像素属于哪一件」，
   * 所以是可选的：给不出就返回 null，界面自动收起画布上的拖拽微调，
   * 而不是给一个点了没反应的假交互。
   */

  /** 命中测试：显示坐标 (x, y) 上最靠前的衣物层 assetId */
  hitTest?(input: RenderInput, displayScale: number, x: number, y: number): Promise<string | null>;

  /**
   * 一个肩宽等于多少显示像素。微调量的单位是肩宽倍数，拖动时要靠它把
   * 鼠标位移换算回去。
   */
  fitUnit?(input: RenderInput, displayScale: number): Promise<number | null>;
}
