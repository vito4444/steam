/**
 * 画板左侧的衣橱选品面板。
 *
 * 没有复用模特视图的 `WardrobePanel`：那个面板每次点击都会 `wear()` 到模特身上、
 * 按槽位互斥（穿了裙子就顶掉上下装），画板的语义是「加到画板」，
 * 同槽位的两件也允许并排摆出来对比。共用的是衣橱数据本身，不是那套穿脱规则。
 */

import { useMemo, useState } from 'react';

import {
  ACCESSORY_CATEGORIES, CATEGORY_LABEL, WARDROBE_TABS, type Category,
} from '@shared/spec';
import { useStore } from '@/state/store';
import { IconImport, IconSearch } from '@/ui/icons';
import { useBoard } from './boardStore';

export function BoardPicker() {
  const { assets, importFiles, setView } = useStore();
  const { state, add } = useBoard();
  const [tab, setTab] = useState<Category | 'all' | 'accessory'>('all');
  const [query, setQuery] = useState('');

  const onBoard = useMemo(
    () => new Set(state.items.map((i) => i.assetId).filter(Boolean) as string[]),
    [state.items],
  );

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    return assets.filter((a) => {
      if (tab === 'accessory' && !ACCESSORY_CATEGORIES.includes(a.category)) return false;
      if (tab !== 'all' && tab !== 'accessory' && a.category !== tab) return false;
      if (!q) return true;
      return (
        a.name.toLowerCase().includes(q) ||
        a.tags.some((t) => t.toLowerCase().includes(q)) ||
        CATEGORY_LABEL[a.category].includes(q)
      );
    });
  }, [assets, tab, query]);

  return (
    <section className="panel wardrobe">
      <div className="panel-head">
        <span className="panel-title">衣橱</span>
        <span className="count-pill">{assets.length} 件</span>
        <div style={{ flex: 1 }} />
        <button className="btn sm ghost" onClick={() => void importFiles()}>
          <IconImport size={14} />
          导入图片
        </button>
      </div>

      <div className="search">
        <IconSearch size={15} />
        <input
          value={query}
          placeholder="搜索名称、标签、品类"
          onChange={(e) => setQuery(e.target.value)}
        />
      </div>

      <div className="tabs">
        {WARDROBE_TABS.map((t) => (
          <button
            key={t.key}
            className={`tab${tab === t.key ? ' active' : ''}`}
            onClick={() => setTab(t.key)}
          >
            {t.label}
          </button>
        ))}
      </div>

      <div className="board-hint">点单品加入画板，再点一次移出。加入后自动排到互不遮挡的位置。</div>

      {filtered.length === 0 ? (
        <div className="empty">
          <div className="empty-art">
            <IconSearch size={30} />
          </div>
          {assets.length === 0 ? (
            <>
              <h3>衣橱还是空的</h3>
              <p>先导入几件透明底 PNG，画板才有东西可摆。</p>
              <div className="row">
                <button className="btn primary" onClick={() => void importFiles()}>导入图片</button>
                <button className="btn ghost" onClick={() => setView('import')}>了解导入流程</button>
              </div>
            </>
          ) : (
            <>
              <h3>没有匹配的单品</h3>
              <p>换个品类或清空搜索词试试。</p>
            </>
          )}
        </div>
      ) : (
        <div className="grid">
          {filtered.map((a) => (
            <button
              key={a.id}
              className={`card${onBoard.has(a.id) ? ' worn' : ''}`}
              onClick={() => add(a)}
              title={`${a.name} · ${CATEGORY_LABEL[a.category]}`}
            >
              <div className="card-thumb">
                <img src={a.thumbUrl} alt="" draggable={false} />
                {onBoard.has(a.id) && <span className="badge">画板中</span>}
              </div>
              <div className="card-body">
                <div className="card-name">{a.name}</div>
                <div className="card-sub">
                  <span>{CATEGORY_LABEL[a.category]}</span>
                  <span className="card-dots">
                    {a.palette.colors.slice(0, 3).map((c, i) => (
                      <i key={i} className="card-dot" style={{ background: c.hex }} />
                    ))}
                  </span>
                </div>
              </div>
            </button>
          ))}
        </div>
      )}
    </section>
  );
}
