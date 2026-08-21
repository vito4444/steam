import { useMemo, useState } from 'react';

import { PANEL_SLOTS, SLOT_LABEL, SLOT_Z, type Slot } from '@shared/spec';
import type { Tuck } from '@shared/occlusion';
import { useStore, useWorn } from '@/state/store';
import {
  IconCompare, IconDownload, IconEye, IconEyeOff, IconMove, IconSparkle, IconTrash,
} from '@/ui/icons';
import { capture } from '../stage/handles';

const TABS: { key: 'try' | 'layers' | 'fit'; label: string }[] = [
  { key: 'try', label: '试穿' },
  { key: 'layers', label: '图层' },
  { key: 'fit', label: '贴合' },
];

export function OutfitPanel() {
  const {
    outfit, dispatch, stageTab, setStageTab, selectedSlot, setSelectedSlot,
    saveLook, addToCompare, notify,
  } = useStore();
  const worn = useWorn();
  const [saving, setSaving] = useState(false);
  const [lookName, setLookName] = useState('');

  const bySlot = useMemo(() => new Map(worn.map((w) => [w.slot, w])), [worn]);
  const wornCount = worn.length;

  const palette = useMemo(() => {
    const out: string[] = [];
    for (const w of worn) {
      if (w.hidden) continue;
      for (const c of w.asset.palette.colors.slice(0, 2)) {
        if (!out.includes(c.hex)) out.push(c.hex);
      }
    }
    return out.slice(0, 8);
  }, [worn]);

  const exportPng = async () => {
    const dataUrl = await capture(1, outfit.background === 'none');
    if (!dataUrl) return;
    const res = await window.pixelfit.exportPng({
      dataUrl,
      suggestedName: `pixelfit-look-${Date.now()}.png`,
    });
    if (res.ok) notify(`已导出到 ${res.path}`);
    else if (!res.canceled) notify(`导出失败：${res.error}`);
  };

  const doSave = async () => {
    const cover = await capture(0.32, false);
    await saveLook(lookName.trim() || `搭配 ${new Date().toLocaleDateString('zh-CN')}`, cover);
    setSaving(false);
    setLookName('');
  };

  return (
    <section className="panel outfit">
      <div className="panel-head">
        <span className="panel-title">当前搭配</span>
        <span className="count-pill">{wornCount} 件</span>
        <div style={{ flex: 1 }} />
        <button
          className="icon-btn"
          title="全部脱下"
          onClick={() => dispatch({ type: 'clear' })}
        >
          <IconTrash size={15} />
        </button>
      </div>

      <div className="filter-row" style={{ paddingTop: 0 }}>
        <div className="seg" style={{ width: '100%' }}>
          {TABS.map((t) => (
            <button
              key={t.key}
              style={{ flex: 1 }}
              className={stageTab === t.key ? 'active' : ''}
              onClick={() => setStageTab(t.key)}
            >
              {t.label}
            </button>
          ))}
        </div>
      </div>

      {stageTab === 'fit' ? (
        <FitEditor />
      ) : (
        <div className="slot-list">
          {PANEL_SLOTS.map((slot) => {
            const w = bySlot.get(slot);
            const showEmpty = stageTab === 'layers' || !!w;
            if (!showEmpty) return null;
            const occupiedByDress = !w && slot !== 'dress' && bySlot.has('dress') &&
              (slot === 'top' || slot === 'bottom');
            return (
              <div
                key={slot}
                className={`slot-row${w ? '' : ' vacant'}${selectedSlot === slot ? ' active' : ''}`}
                onClick={() => setSelectedSlot(selectedSlot === slot ? null : slot)}
              >
                <span className="z-tag">z{w ? w.z : SLOT_Z[slot]}</span>
                <span className="slot-thumb">
                  {w ? <img src={w.asset.thumbUrl} alt="" /> : null}
                </span>
                <span className="slot-name">{SLOT_LABEL[slot]}</span>
                <span className="slot-item">
                  {w ? w.asset.name : occupiedByDress ? '已被连衣裙占用' : '未选择'}
                </span>
                {w && (
                  <>
                    <button
                      className={`icon-btn${w.hidden ? '' : ' on'}`}
                      title={w.hidden ? '显示这一层' : '临时隐藏这一层'}
                      onClick={(e) => {
                        e.stopPropagation();
                        dispatch({ type: 'toggleHidden', slot });
                      }}
                    >
                      {w.hidden ? <IconEyeOff size={14} /> : <IconEye size={14} />}
                    </button>
                    <button
                      className="icon-btn"
                      title="脱下"
                      onClick={(e) => {
                        e.stopPropagation();
                        dispatch({ type: 'takeOff', slot });
                      }}
                    >
                      <IconTrash size={14} />
                    </button>
                  </>
                )}
              </div>
            );
          })}

          {stageTab === 'try' && wornCount === 0 && (
            <div className="empty" style={{ padding: '32px 16px' }}>
              <div className="empty-art" style={{ width: 72, height: 72 }}>
                <IconSparkle size={26} />
              </div>
              <h3>还没穿任何东西</h3>
              <p>点左侧衣橱里的单品，或者直接把卡片拖到模特身上。</p>
            </div>
          )}
        </div>
      )}

      <div className="panel-foot">
        {palette.length > 0 && (
          <div className="palette-row">
            <span>本套主色</span>
            {palette.map((c) => (
              <i key={c} className="card-dot" style={{ background: c, width: 12, height: 12 }} />
            ))}
          </div>
        )}
        <div className="row">
          <button className="btn sm" style={{ flex: 1 }} onClick={addToCompare}>
            <IconCompare size={14} />
            加入对比
          </button>
          <button className="btn sm" style={{ flex: 1 }} onClick={() => void exportPng()}>
            <IconDownload size={14} />
            导出 PNG
          </button>
        </div>
        <button
          className="btn primary wide"
          disabled={wornCount === 0}
          onClick={() => setSaving(true)}
        >
          保存为 Look
        </button>
      </div>

      {saving && (
        <div className="modal-backdrop" onClick={() => setSaving(false)}>
          <div className="modal" onClick={(e) => e.stopPropagation()}>
            <h3>保存这套搭配</h3>
            <div className="input">
              <input
                autoFocus
                value={lookName}
                placeholder="给它起个名字，比如「周三通勤」"
                onChange={(e) => setLookName(e.target.value)}
                onKeyDown={(e) => e.key === 'Enter' && void doSave()}
              />
            </div>
            <div className="row" style={{ justifyContent: 'flex-end' }}>
              <button className="btn ghost" onClick={() => setSaving(false)}>
                取消
              </button>
              <button className="btn primary" onClick={() => void doSave()}>
                保存
              </button>
            </div>
          </div>
        </div>
      )}
    </section>
  );
}

/**
 * 贴合微调。
 *
 * 自动贴合已经按底图锚点与实测身体宽度解过一遍，这里给的是**相对自动解**
 * 的微调：缩放是倍数，位移的单位是肩宽 —— 换一版底图不会失准。
 */
function FitEditor() {
  const { outfit, dispatch, selectedSlot, setSelectedSlot, bases, occlusion } = useStore();
  const worn = useWorn();
  const target = worn.find((w) => w.slot === selectedSlot) ?? worn[worn.length - 1];

  if (!target) {
    return (
      <div className="empty" style={{ padding: '32px 16px' }}>
        <div className="empty-art" style={{ width: 72, height: 72 }}>
          <IconMove size={26} />
        </div>
        <h3>先穿一件</h3>
        <p>穿上任意单品后，在这里微调它的缩放、位置和横向宽度。</p>
      </div>
    );
  }

  const fit = target.fit;
  const set = (patch: Partial<typeof fit>) =>
    dispatch({ type: 'setFit', assetId: target.asset.id, patch });

  const rows: { key: keyof typeof fit; label: string; min: number; max: number; step: number; fmt: (v: number) => string }[] = [
    { key: 'scale', label: '缩放', min: 0.6, max: 1.6, step: 0.01, fmt: (v) => `自动 × ${v.toFixed(2)}` },
    { key: 'dx', label: '左右', min: -0.5, max: 0.5, step: 0.005, fmt: (v) => `${(v * 100).toFixed(1)}% 肩宽` },
    { key: 'dy', label: '上下', min: -0.6, max: 0.6, step: 0.005, fmt: (v) => `${(v * 100).toFixed(1)}% 肩宽` },
    { key: 'stretch_x', label: '横向形变', min: 0.88, max: 1.12, step: 0.005, fmt: (v) => `${Math.round(v * 100)}%` },
  ];

  return (
    <div className="slot-list">
      <div className="slot-row active" style={{ marginBottom: 4 }}>
        <span className="slot-thumb">
          <img src={target.asset.thumbUrl} alt="" />
        </span>
        <span className="slot-item">{target.asset.name}</span>
        <span className="z-tag">z{target.z}</span>
      </div>

      {rows.map((r) => (
        <div className="field" key={r.key}>
          <label>
            <span>{r.label}</span>
            <span className="val">{r.fmt(fit[r.key] as number)}</span>
          </label>
          <input
            type="range"
            min={r.min}
            max={r.max}
            step={r.step}
            value={fit[r.key] as number}
            onChange={(e) => set({ [r.key]: Number(e.target.value) } as never)}
          />
        </div>
      ))}

      <div className="field">
        <label>
          <span>层内微调 z_offset</span>
          <span className="val">
            {outfit.zOverrides[target.asset.id] ?? target.asset.z_offset ?? 0}
          </span>
        </label>
        <input
          type="range"
          min={-4}
          max={4}
          step={1}
          value={outfit.zOverrides[target.asset.id] ?? target.asset.z_offset ?? 0}
          onChange={(e) =>
            dispatch({ type: 'setZ', assetId: target.asset.id, value: Number(e.target.value) })
          }
        />
      </div>

      {(target.slot === 'top' || target.slot === 'underlayer') && (
        <div className="field">
          <label>
            <span>下摆</span>
            <span className="val">{target.tuck === 'in' ? '压在下装腰线之下' : '盖在下装腰线之上'}</span>
          </label>
          <div className="seg" style={{ width: '100%' }}>
            {([['in', '塞进腰里'], ['out', '放下来']] as [Tuck, string][]).map(([v, label]) => (
              <button
                key={v}
                style={{ flex: 1 }}
                className={target.tuck === v ? 'active' : ''}
                onClick={() => dispatch({ type: 'setTuck', assetId: target.asset.id, value: v })}
              >
                {label}
              </button>
            ))}
          </div>
        </div>
      )}

      <ActiveRules slot={target.slot} tuck={target.tuck} worn={worn.map((w) => w.slot)} config={occlusion} />

      <div className="row" style={{ padding: '8px 10px' }}>
        <button
          className="btn sm ghost"
          style={{ flex: 1 }}
          onClick={() => dispatch({ type: 'resetFit', assetId: target.asset.id })}
        >
          恢复默认贴合
        </button>
        <button className="btn sm ghost" onClick={() => setSelectedSlot(null)}>
          取消选中
        </button>
      </div>

      <p className="hint">
        画布 {bases[outfit.body]?.canvas.w} × {bases[outfit.body]?.canvas.h}，
        自动贴合按底图锚点与实测身体宽度解出。也可以直接在模特身上拖动这一件、
        滚轮缩放。微调只作用于当前搭配，不写回素材，保存 Look 时一起存下来。
      </p>
    </div>
  );
}

/**
 * 当前这一件身上正在生效的遮挡规则。
 *
 * 摆出来是为了让「为什么这块被裁掉了」有处可查 —— 遮挡一旦看不见就会被当成
 * 渲染 bug。列的是规则 id，跟 `docs/occlusion-rules.md` 里的表一一对得上。
 */
function ActiveRules({
  slot, tuck, worn, config,
}: {
  slot: Slot;
  tuck: Tuck;
  worn: Slot[];
  config: import('@shared/occlusion').OcclusionConfig;
}) {
  const hits: { id: string; note: string }[] = [];
  for (const g of config.guards) {
    if (g.slots.includes(slot)) hits.push({ id: g.id, note: g.note });
  }
  const mask = config.mask[slot];
  if (mask?.mode === 'silhouette') {
    hits.push({ id: 'body_mask', note: `身体遮罩：外扩 ${(mask.dilateK * 100).toFixed(0)}% 肩宽` });
  }
  for (const r of config.occlusions) {
    if (r.under !== slot || !worn.includes(r.over)) continue;
    if (r.when === 'tucked' && tuck !== 'in') continue;
    if (r.when === 'untucked' && tuck !== 'out') continue;
    hits.push({ id: r.id, note: r.note });
  }

  return (
    <div className="rule-list">
      <div className="rule-head">生效中的遮挡规则 · {hits.length} 条</div>
      {hits.length === 0
        ? <p className="hint" style={{ padding: '2px 10px 8px' }}>这一件当前没有任何裁切。</p>
        : hits.map((h) => (
          <div className="rule-row" key={h.id}>
            <code>{h.id}</code>
            <span>{h.note}</span>
          </div>
        ))}
    </div>
  );
}
