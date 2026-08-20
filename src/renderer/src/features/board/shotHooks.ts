/**
 * 截图脚本要驱动画板，但画板状态在 BoardProvider 里。
 *
 * 和 `stage/handles.ts` 同一个套路：Provider 挂载时把几个动作登记到这个单例上，
 * `renderer/shots.ts` 只调用它，不需要拿到 context。截出来的仍然是应用真实状态 ——
 * 这些动作和界面上的按钮走的是同一份 reducer，不是给截图专门伪造的渲染路径。
 */

import type { Asset } from '@shared/types';
import type { Category } from '@shared/spec';
import type { BoardBackgroundId } from '@shared/board';

export const boardShotHooks: {
  reset?: () => void;
  /** 每个品类挑第一件加到画板 */
  fill?: (categories: Category[], assets: Asset[]) => void;
  addTitle?: () => void;
  selectFirst?: (category: Category) => void;
  setBackground?: (id: BoardBackgroundId) => void;
  /** 真的把画板渲成图并弹出预览 */
  preview?: () => Promise<void>;
  saveLook?: (name: string, occasion: string[]) => Promise<void>;
} = {};
