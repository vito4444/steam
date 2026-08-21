/**
 * 搭配画板视图：左衣橱 / 中画板 / 右检查器。
 *
 * 与模特换装视图并存，互不覆盖：模特看「上身效果」，画板看「每一件长什么样」。
 */

import { useEffect } from 'react';

import { useStore } from '@/state/store';
import { IconSparkle } from '@/ui/icons';
import { BoardCanvas } from './BoardCanvas';
import { BoardInspector } from './BoardInspector';
import { BoardPicker } from './BoardPicker';
import { useBoard } from './boardStore';
import './board.css';

export function BoardView() {
  const { assets } = useStore();
  const { state, selected, remove, undo, redo, update, previewUrl, closePreview } = useBoard();

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const tag = (e.target as HTMLElement | null)?.tagName;
      if (tag === 'INPUT' || tag === 'TEXTAREA') return;
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'z') {
        e.preventDefault();
        if (e.shiftKey) redo();
        else undo();
        return;
      }
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'y') {
        e.preventDefault();
        redo();
        return;
      }
      if (!selected) return;
      const item = state.items.find((i) => i.id === selected);
      if (!item) return;
      if (e.key === 'Delete' || e.key === 'Backspace') {
        e.preventDefault();
        remove(selected);
        return;
      }
      const step = e.shiftKey ? 20 : 4;
      const nudge: Record<string, [number, number]> = {
        ArrowLeft: [-step, 0], ArrowRight: [step, 0], ArrowUp: [0, -step], ArrowDown: [0, step],
      };
      const d = nudge[e.key];
      if (d) {
        e.preventDefault();
        update(selected, { x: item.x + d[0], y: item.y + d[1] });
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [selected, state.items, remove, undo, redo, update]);

  const empty = state.items.length === 0;

  return (
    <>
      <BoardPicker />
      <section className="stage-wrap">
        <div className="stage-bar">
          <span className="board-stage-title">搭配画板</span>
          <span className="board-stage-sub">单品各自摊开，一眼看清每一件 · 画布 1400 × 1800</span>
        </div>
        {empty ? (
          <div className="board-empty">
            <div className="empty-art">
              <IconSparkle size={30} />
            </div>
            <h3>画板还是空的</h3>
            <p>从左边点几件单品，它们会自动摆成互不遮挡的版式：上装在上、下装在下、鞋在最下，包与配饰另起一列。</p>
          </div>
        ) : (
          <BoardCanvas assets={assets} />
        )}
      </section>
      <BoardInspector />

      {previewUrl && (
        <div className="board-preview" onClick={closePreview}>
          <div className="board-preview-inner" onClick={(e) => e.stopPropagation()}>
            <img src={previewUrl} alt="画板成图预览" />
            <div className="board-preview-bar">
              <span>成图预览 · 1400 × 1800（导出可选 2× / 3×）</span>
              <button className="btn sm ghost" onClick={closePreview}>关闭</button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
