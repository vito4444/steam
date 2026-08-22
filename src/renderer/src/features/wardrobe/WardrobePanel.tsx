import { useMemo, useRef, useState } from 'react';

import {
  ACCESSORY_CATEGORIES, CATEGORY_LABEL, COLOR_FAMILY_LABEL, COLOR_FAMILY_SWATCH,
  WARDROBE_TABS, type Category, type ColorFamily,
} from '@shared/spec';
import type { Asset } from '@shared/types';
import { useStore } from '@/state/store';
import {
  IconImport, IconSearch, IconStar, IconStarFill, IconTrash,
} from '@/ui/icons';

type Sort = 'recent' | 'worn' | 'color' | 'name';

const SORTS: { key: Sort; label: string }[] = [
  { key: 'recent', label: '最近导入' },
  { key: 'worn', label: '最常穿' },
  { key: 'color', label: '按颜色' },
  { key: 'name', label: '按名称' },
];

export function WardrobePanel() {
  const {
    assets, outfit, pipeline, wear, updateAsset, deleteAsset, importFiles, setView,
  } = useStore();
  const [tab, setTab] = useState<Category | 'all' | 'accessory'>('all');
  const [query, setQuery] = useState('');
  const [family, setFamily] = useState<ColorFamily | null>(null);
  const [sort, setSort] = useState<Sort>('recent');
  const [favOnly, setFavOnly] = useState(false);
  const [dragId, setDragId] = useState<string | null>(null);
  const searchRef = useRef<HTMLInputElement>(null);

  const wornIds = useMemo(
    () => new Set(Object.values(outfit.slots).filter(Boolean) as string[]),
    [outfit.slots],
  );
  const demoCount = useMemo(
    () => assets.filter((asset) => asset.source.demo).length,
    [assets],
  );

  const families = useMemo(() => {
    const seen = new Map<ColorFamily, number>();
    for (const a of assets) {
      const f = a.palette.color_family;
      seen.set(f, (seen.get(f) ?? 0) + 1);
    }
    return [...seen.keys()].sort();
  }, [assets]);

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    let list = assets.filter((a) => {
      if (tab === 'accessory' && !ACCESSORY_CATEGORIES.includes(a.category)) return false;
      if (tab !== 'all' && tab !== 'accessory' && a.category !== tab) return false;
      if (family && a.palette.color_family !== family) return false;
      if (favOnly && !a.favorite) return false;
      if (!q) return true;
      return (
        a.name.toLowerCase().includes(q) ||
        a.subcategory?.toLowerCase().includes(q) ||
        a.tags.some((t) => t.toLowerCase().includes(q)) ||
        CATEGORY_LABEL[a.category].includes(q)
      );
    });
    list = [...list];
    if (sort === 'worn') list.sort((a, b) => b.wear_count - a.wear_count);
    else if (sort === 'name') list.sort((a, b) => a.name.localeCompare(b.name, 'zh'));
    else if (sort === 'color') list.sort((a, b) => a.palette.dominant.localeCompare(b.palette.dominant));
    return list;
  }, [assets, tab, query, family, favOnly, sort]);

  const onWear = (asset: Asset) => {
    wear(asset);
    if (!wornIds.has(asset.id)) void updateAsset(asset.id, { wear_count: asset.wear_count + 1 });
  };

  return (
    <section className="panel wardrobe">
      <div className="panel-head">
        <span className="panel-title">我的衣橱</span>
        <span className="count-pill demo-pill">{demoCount} 件示例素材</span>
        <span className="count-pill">{assets.length} 件</span>
        <div style={{ flex: 1 }} />
        <button className="btn sm ghost" onClick={() => void importFiles()}>
          <IconImport size={14} />
          导入图片
        </button>
      </div>

      <div className="search" onClick={() => searchRef.current?.focus()}>
        <IconSearch size={15} />
        <input
          ref={searchRef}
          value={query}
          placeholder="搜索名称、标签、品类"
          onChange={(e) => setQuery(e.target.value)}
        />
        <span className="kbd">Ctrl K</span>
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

      <div className="filter-row">
        <button
          className={`chip${favOnly ? ' active' : ''}`}
          onClick={() => setFavOnly((v) => !v)}
        >
          收藏
        </button>
        {SORTS.map((s) => (
          <button
            key={s.key}
            className={`chip${sort === s.key ? ' active' : ''}`}
            onClick={() => setSort(s.key)}
          >
            {s.label}
          </button>
        ))}
      </div>

      <div className="filter-row" style={{ paddingTop: 0 }}>
        <span style={{ fontSize: 11, color: 'var(--text-3)', marginRight: 2 }}>颜色</span>
        {families.map((f) => (
          <button
            key={f}
            title={COLOR_FAMILY_LABEL[f]}
            className={`swatch${family === f ? ' active' : ''}`}
            style={{ background: COLOR_FAMILY_SWATCH[f] }}
            onClick={() => setFamily((cur) => (cur === f ? null : f))}
          />
        ))}
        {family && (
          <button className="chip" onClick={() => setFamily(null)}>
            清除
          </button>
        )}
      </div>

      {filtered.length === 0 ? (
        <div className="empty">
          <div className="empty-art">
            <IconSearch size={30} />
          </div>
          {assets.length === 0 ? (
            <>
              <h3>衣橱还是空的</h3>
              <p>
                {pipeline?.automatic
                  ? '选择一张衣物照片即可在本地自动识别，通过质量检查后会进入衣橱。'
                  : '当前自动识别不可用，请先准备透明 PNG/WebP，再通过手动入口导入。'}
              </p>
              <div className="row">
                <button
                  className="btn primary"
                  onClick={() => {
                    if (pipeline?.automatic) setView('import');
                    else void importFiles();
                  }}
                >
                  {pipeline?.automatic ? '选择照片导入' : '导入透明素材'}
                </button>
                <button className="btn ghost" onClick={() => setView('import')}>
                  了解导入流程
                </button>
              </div>
            </>
          ) : (
            <>
              <h3>没有匹配的单品</h3>
              <p>换个品类或颜色筛选试试，也可以清空搜索词。</p>
              <button
                className="btn ghost"
                onClick={() => {
                  setQuery('');
                  setFamily(null);
                  setTab('all');
                  setFavOnly(false);
                }}
              >
                重置筛选
              </button>
            </>
          )}
        </div>
      ) : (
        <div className="grid">
          {filtered.map((a) => (
            <button
              key={a.id}
              className={`card${wornIds.has(a.id) ? ' worn' : ''}${dragId === a.id ? ' dragging' : ''}`}
              draggable
              onDragStart={(e) => {
                setDragId(a.id);
                e.dataTransfer.setData('text/pixelfit-asset', a.id);
                e.dataTransfer.effectAllowed = 'copy';
              }}
              onDragEnd={() => setDragId(null)}
              onClick={() => onWear(a)}
              title={`${a.name} · ${CATEGORY_LABEL[a.category]}${a.subcategory ? ` · ${a.subcategory}` : ''}`}
            >
              <div className="card-thumb">
                <img src={a.thumbUrl} alt="" draggable={false} />
                {wornIds.has(a.id) && <span className="badge">穿着中</span>}
                {a.source.demo && <span className="demo-badge">示例</span>}
                {a.review_status === 'needs_optimization' && (
                  <span className="review-badge">待优化</span>
                )}
                <span
                  role="button"
                  tabIndex={-1}
                  className={`fav${a.favorite ? ' on' : ''}`}
                  onClick={(e) => {
                    e.stopPropagation();
                    void updateAsset(a.id, { favorite: !a.favorite });
                  }}
                >
                  {a.favorite ? <IconStarFill size={13} /> : <IconStar size={13} />}
                </span>
                <span
                  role="button"
                  tabIndex={-1}
                  className="fav"
                  style={{ top: 'auto', bottom: 5 }}
                  onClick={(e) => {
                    e.stopPropagation();
                    void deleteAsset(a.id);
                  }}
                >
                  <IconTrash size={13} />
                </span>
              </div>
              <div className="card-body">
                <div className="card-name">{a.name}</div>
                <div className="card-sub">
                  <span>
                    {CATEGORY_LABEL[a.category]}
                    {a.subcategory ? ` · ${a.subcategory}` : ''}
                  </span>
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
