import React, {
  createContext, useCallback, useContext, useEffect, useMemo, useReducer, useRef, useState,
} from 'react';

import {
  BODY_TYPES, CATEGORY_SLOT, HAIR_STYLES, SKIN_TONES, SLOT_Z,
  type BodyType, type Slot,
} from '@shared/spec';
import {
  DEFAULT_OCCLUSION, EXCLUSION_RULES, defaultTuck, mergeOcclusionConfig,
  type OcclusionConfig, type Tuck,
} from '@shared/occlusion';
import { DEFAULT_FIT, type Asset, type BaseBodySet, type Fit, type Look, type LookRecord } from '@shared/types';
import type { PipelineStatus } from '@shared/ipc';
import {
  manualFeedback, runManualImport, type ManualImportOutcome,
} from '@/features/import/importFeedback';
import { createEngine, type EngineId } from '@/tryon/registry';
import type { TryOnEngine } from '@/tryon/types';

// ------------------------------------------------------------------ outfit

export interface OutfitState {
  body: BodyType;
  skin: number;
  hair: string;
  hairColor: string;
  slots: Partial<Record<Slot, string | null>>;
  hidden: Slot[];
  zOverrides: Record<string, number>;
  fitOverrides: Record<string, Partial<Fit>>;
  /** 逐件的塞衣角覆盖；没有就按品类推 */
  tuckOverrides: Record<string, Tuck>;
  background: 'none' | 'solid' | 'studio_warm' | 'studio_cool';
  /** 关掉羽化与接触阴影，用来出「处理前 / 处理后」对照图 */
  rawCompositing: boolean;
  /** 关掉遮挡与身体遮罩裁切，用来出「有遮挡 / 无遮挡」对照图 */
  noOcclusion: boolean;
}

export const EMPTY_OUTFIT: OutfitState = {
  body: 'base_f02',
  skin: 2,
  hair: 'h01',
  hairColor: '#4A3428',
  slots: {},
  hidden: [],
  zOverrides: {},
  fitOverrides: {},
  tuckOverrides: {},
  background: 'studio_warm',
  rawCompositing: false,
  noOcclusion: false,
};

type Action =
  | { type: 'wear'; asset: Asset }
  | { type: 'takeOff'; slot: Slot }
  | { type: 'set'; patch: Partial<OutfitState> }
  | { type: 'toggleHidden'; slot: Slot }
  | { type: 'setZ'; assetId: string; value: number }
  | { type: 'setFit'; assetId: string; patch: Partial<Fit> }
  | { type: 'setTuck'; assetId: string; value: Tuck }
  | { type: 'resetFit'; assetId: string }
  | { type: 'clear' }
  | { type: 'replace'; state: OutfitState };

function outfitReducer(state: OutfitState, action: Action): OutfitState {
  switch (action.type) {
    case 'wear': {
      const slot = action.asset.slot;
      const slots = { ...state.slots };
      if (slots[slot] === action.asset.id) {
        // 再点一次 = 脱下
        delete slots[slot];
        if (action.asset.companion) delete slots[companionSlot(action.asset)];
        return { ...state, slots };
      }
      slots[slot] = action.asset.id;
      // 互斥来自规则表，不写死在这里 —— 加品类只改 occlusion.ts
      for (const rule of EXCLUSION_RULES) {
        if (rule.slot !== slot) continue;
        for (const other of rule.clears) delete slots[other];
      }
      if (action.asset.companion) slots[companionSlot(action.asset)] = action.asset.companion;
      return { ...state, slots };
    }
    case 'takeOff': {
      const slots = { ...state.slots };
      delete slots[action.slot];
      if (action.slot === 'shoe_base') delete slots.shoe_shaft;
      if (action.slot === 'shoe_shaft') delete slots.shoe_shaft;
      return { ...state, slots };
    }
    case 'set':
      return { ...state, ...action.patch };
    case 'toggleHidden':
      return {
        ...state,
        hidden: state.hidden.includes(action.slot)
          ? state.hidden.filter((s) => s !== action.slot)
          : [...state.hidden, action.slot],
      };
    case 'setZ':
      return { ...state, zOverrides: { ...state.zOverrides, [action.assetId]: action.value } };
    case 'setFit':
      return {
        ...state,
        fitOverrides: {
          ...state.fitOverrides,
          [action.assetId]: { ...state.fitOverrides[action.assetId], ...action.patch },
        },
      };
    case 'setTuck':
      return {
        ...state,
        tuckOverrides: { ...state.tuckOverrides, [action.assetId]: action.value },
      };
    case 'resetFit': {
      const next = { ...state.fitOverrides };
      delete next[action.assetId];
      const tucks = { ...state.tuckOverrides };
      delete tucks[action.assetId];
      return { ...state, fitOverrides: next, tuckOverrides: tucks };
    }
    case 'clear':
      return {
        ...EMPTY_OUTFIT,
        body: state.body, skin: state.skin, hair: state.hair,
        hairColor: state.hairColor, background: state.background,
        rawCompositing: state.rawCompositing, noOcclusion: state.noOcclusion,
      };
    case 'replace':
      return action.state;
    default:
      return state;
  }
}

function companionSlot(asset: Asset): Slot {
  return asset.slot === 'shoe_base' ? 'shoe_shaft' : 'shoe_base';
}

// ------------------------------------------------------------------ context

export type View = 'wardrobe' | 'board' | 'import' | 'looks' | 'base' | 'settings';
export type StageTab = 'try' | 'layers' | 'fit';

interface Ctx {
  ready: boolean;
  assets: Asset[];
  looks: LookRecord[];
  bases: Record<BodyType, BaseBodySet | undefined>;
  /** 上传 / 重置模特底图后重读（CERE-28） */
  reloadBases: () => Promise<void>;
  pipeline: PipelineStatus | null;
  /** CERE-64：模型下完之后重新问一次主进程，自动抠图这时候才真的可用 */
  refreshPipeline: () => Promise<void>;
  /** 应用版本号；标题栏和设置页都显示它，成员报问题时能一眼念出来 */
  version: string;
  root: string;

  /** 生效中的遮挡规则表（内置默认表 + 素材库里的 occlusion.json 覆盖） */
  occlusion: OcclusionConfig;
  occlusionSource: { path: string; custom: boolean; error?: string };

  engineId: EngineId;
  setEngineId: (id: EngineId) => void;
  engine: TryOnEngine;

  view: View;
  setView: (v: View) => void;
  stageTab: StageTab;
  setStageTab: (t: StageTab) => void;

  outfit: OutfitState;
  dispatch: React.Dispatch<Action>;
  wear: (asset: Asset) => void;
  undo: () => void;
  canUndo: boolean;

  compare: OutfitState[];
  addToCompare: () => void;
  clearCompare: () => void;

  selectedSlot: Slot | null;
  setSelectedSlot: (s: Slot | null) => void;

  refresh: () => Promise<void>;
  saveLook: (name: string, cover: string | null) => Promise<void>;
  applyLook: (look: LookRecord) => void;
  deleteLook: (id: string) => Promise<void>;
  updateAsset: (id: string, patch: Partial<Asset>) => Promise<void>;
  deleteAsset: (id: string) => Promise<void>;
  importFiles: () => Promise<ManualImportOutcome>;

  toast: string | null;
  notify: (msg: string) => void;
}

const StoreContext = createContext<Ctx | null>(null);

export function useStore(): Ctx {
  const ctx = useContext(StoreContext);
  if (!ctx) throw new Error('useStore outside provider');
  return ctx;
}

export function StoreProvider({ children }: { children: React.ReactNode }) {
  const [ready, setReady] = useState(false);
  const [assets, setAssets] = useState<Asset[]>([]);
  const [looks, setLooks] = useState<LookRecord[]>([]);
  const [bases, setBases] = useState<Record<BodyType, BaseBodySet | undefined>>({
    base_f02: undefined, base_m02: undefined,
  });
  const [pipeline, setPipeline] = useState<PipelineStatus | null>(null);
  const [version, setVersion] = useState('');
  const [root, setRoot] = useState('');
  const [occlusion, setOcclusion] = useState<OcclusionConfig>(DEFAULT_OCCLUSION);
  const [occlusionSource, setOcclusionSource] =
    useState<{ path: string; custom: boolean; error?: string }>({ path: '', custom: false });
  const [view, setView] = useState<View>('wardrobe');
  const [engineId, setEngineId] = useState<EngineId>('layered');
  const engine = useMemo(() => createEngine(engineId), [engineId]);
  const [stageTab, setStageTab] = useState<StageTab>('try');
  const [outfit, dispatch] = useReducer(outfitReducer, EMPTY_OUTFIT);
  const [compare, setCompare] = useState<OutfitState[]>([]);
  const [selectedSlot, setSelectedSlot] = useState<Slot | null>(null);
  const [toast, setToast] = useState<string | null>(null);
  const history = useRef<OutfitState[]>([]);
  const toastTimer = useRef<number | null>(null);

  const notify = useCallback((msg: string) => {
    setToast(msg);
    if (toastTimer.current) window.clearTimeout(toastTimer.current);
    toastTimer.current = window.setTimeout(() => setToast(null), 2600);
  }, []);

  const refresh = useCallback(async () => {
    const [list, lookList, stats] = await Promise.all([
      window.pixelfit.library.listAssets(),
      window.pixelfit.looks.list(),
      window.pixelfit.library.stats(),
    ]);
    setAssets(list);
    setLooks(lookList);
    setRoot(stats.root);
  }, []);

  const refreshPipeline = useCallback(async () => {
    setPipeline(await window.pixelfit.pipeline.status());
  }, []);

  const reloadBases = useCallback(async () => {
    const loaded: Record<string, BaseBodySet> = {};
    for (const b of BODY_TYPES) loaded[b] = await window.pixelfit.base.get(b);
    setBases(loaded as Record<BodyType, BaseBodySet>);
  }, []);

  useEffect(() => {
    (async () => {
      await reloadBases();
      setVersion(await window.pixelfit.app.version().catch(() => ''));
      await refreshPipeline();
      const rules = await window.pixelfit.rules.occlusion();
      setOcclusion(mergeOcclusionConfig(DEFAULT_OCCLUSION, rules.override));
      setOcclusionSource({ path: rules.path, custom: !!rules.override, error: rules.error });
      await refresh();
      setReady(true);
    })().catch((err) => {
      console.error(err);
      notify('初始化失败，请查看日志');
      setReady(true);
    });
  }, [refresh, notify, reloadBases, refreshPipeline]);

  const wear = useCallback((asset: Asset) => {
    history.current = [...history.current.slice(-24), outfit];
    dispatch({ type: 'wear', asset });
  }, [outfit]);

  const undo = useCallback(() => {
    const prev = history.current.pop();
    if (prev) dispatch({ type: 'replace', state: prev });
  }, []);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'z') {
        e.preventDefault();
        undo();
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [undo]);

  const addToCompare = useCallback(() => {
    setCompare((c) => [...c.slice(-1), outfit]);
    notify('已加入对比');
  }, [outfit, notify]);

  const saveLook = useCallback(async (name: string, cover: string | null) => {
    const now = new Date().toISOString();
    const look: Look = {
      schema_version: 3,
      id: `look_${Math.random().toString(36).slice(2, 12).toUpperCase()}`,
      name,
      base: { body: outfit.body, skin: outfit.skin, hair: outfit.hair, hair_color: outfit.hairColor },
      slots: outfit.slots,
      z_overrides: outfit.zOverrides,
      hidden_slots: outfit.hidden,
      fit_overrides: outfit.fitOverrides,
      tuck_overrides: outfit.tuckOverrides,
      occasion: [],
      tags: [],
      background: outfit.background,
      cover: cover ? 'cover.png' : null,
      favorite: false,
      created_at: now,
      updated_at: now,
    };
    await window.pixelfit.looks.save(look, cover);
    await refresh();
    notify(`已保存 Look「${name}」`);
  }, [outfit, refresh, notify]);

  const applyLook = useCallback((look: LookRecord) => {
    history.current = [...history.current.slice(-24), outfit];
    dispatch({
      type: 'replace',
      state: {
        body: look.base.body,
        skin: look.base.skin,
        hair: look.base.hair,
        hairColor: look.base.hair_color,
        slots: look.slots,
        hidden: look.hidden_slots ?? [],
        zOverrides: look.z_overrides ?? {},
        fitOverrides: look.fit_overrides ?? {},
        tuckOverrides: look.tuck_overrides ?? {},
        background: look.background ?? 'studio_warm',
        rawCompositing: false,
        noOcclusion: false,
      },
    });
    setView('wardrobe');
    notify(`已载入「${look.name}」`);
  }, [outfit, notify]);

  const deleteLook = useCallback(async (id: string) => {
    await window.pixelfit.looks.delete(id);
    await refresh();
    notify('已删除 Look');
  }, [refresh, notify]);

  const updateAsset = useCallback(async (id: string, patch: Partial<Asset>) => {
    await window.pixelfit.library.updateAsset(id, patch);
    await refresh();
  }, [refresh]);

  const deleteAsset = useCallback(async (id: string) => {
    await window.pixelfit.library.deleteAsset(id);
    dispatch({
      type: 'replace',
      state: {
        ...outfit,
        slots: Object.fromEntries(
          Object.entries(outfit.slots).filter(([, v]) => v !== id),
        ) as OutfitState['slots'],
      },
    });
    await refresh();
    notify('已删除素材');
  }, [outfit, refresh, notify]);

  const importFiles = useCallback(async () => {
    const outcome = await runManualImport(
      () => window.pixelfit.library.importFiles(),
      refresh,
    );
    notify(manualFeedback(outcome).message);
    return outcome;
  }, [refresh, notify]);

  const value = useMemo<Ctx>(() => ({
    ready, assets, looks, bases, reloadBases, pipeline, refreshPipeline, version, root,
    occlusion, occlusionSource,
    engineId, setEngineId, engine,
    view, setView, stageTab, setStageTab,
    outfit, dispatch, wear, undo, canUndo: history.current.length > 0,
    compare, addToCompare, clearCompare: () => setCompare([]),
    selectedSlot, setSelectedSlot,
    refresh, saveLook, applyLook, deleteLook, updateAsset, deleteAsset, importFiles,
    toast, notify,
  }), [
    ready, assets, looks, bases, reloadBases, pipeline, refreshPipeline, version, root,
    occlusion, occlusionSource,
    engineId, engine, view, stageTab, outfit, wear, undo,
    compare, addToCompare, selectedSlot, refresh, saveLook, applyLook, deleteLook,
    updateAsset, deleteAsset, importFiles, toast, notify,
  ]);

  return <StoreContext.Provider value={value}>{children}</StoreContext.Provider>;
}

// ------------------------------------------------------------------ helpers

export interface WornEntry {
  slot: Slot;
  asset: Asset;
  z: number;
  fit: Fit;
  hidden: boolean;
  /** 塞衣角：用户改过就用改过的，否则按品类推 */
  tuck: Tuck;
}

/** 当前搭配里实际穿着的素材，按 z 排好 */
export function useWorn(): WornEntry[] {
  const { assets, outfit } = useStore();
  return useMemo(() => {
    const byId = new Map(assets.map((a) => [a.id, a]));
    const out: WornEntry[] = [];
    for (const [slot, id] of Object.entries(outfit.slots)) {
      if (!id) continue;
      const asset = byId.get(id);
      if (!asset) continue; // 素材被删掉了，槽位留空而不是崩掉
      const s = slot as Slot;
      out.push({
        slot: s,
        asset,
        z: SLOT_Z[s] + (outfit.zOverrides[id] ?? asset.z_offset ?? 0),
        fit: { ...DEFAULT_FIT, ...asset.fit, ...outfit.fitOverrides[id] },
        hidden: outfit.hidden.includes(s),
        tuck: outfit.tuckOverrides[id] ?? defaultTuck(asset.attributes, s),
      });
    }
    return out.sort((a, b) => a.z - b.z);
  }, [assets, outfit]);
}

/** Look 里的 skin 是 1 起的档位号；底图包的肤色数组是 0 起的下标 */
export function toneIndex(skin: number, base?: BaseBodySet): number {
  const n = base?.tones.length ?? SKIN_TONES.length;
  return Math.min(Math.max(skin - 1, 0), Math.max(n - 1, 0));
}

export function toneSwatches(base?: BaseBodySet): string[] {
  if (base?.tones.length) return base.tones.map((t) => t.swatch);
  return [...SKIN_TONES];
}

/**
 * 这套底图到底能不能换肤色。
 *
 * CERE-28：`toneSwatches` 在底图包没有烘焙肤色时会回落到内置的 6 档色板，
 * 于是界面上排出 6 个可点的色块 —— 但渲染层画的是 `base.tones[i].layers`，
 * 数组是空的就一层都不画，点了没任何变化。内置的 F02 / M02 包本来就是
 * `tones: []`，上传的照片底图也是。摆一排点了不动的控件比不摆更糟糕，
 * 所以界面用这个判断决定要不要显示肤色行。
 */
export function hasBakedTones(base?: BaseBodySet): boolean {
  return (base?.tones.length ?? 0) > 0;
}

export function hairName(id: string): string {
  return HAIR_STYLES.find((h) => h.id === id)?.name ?? id;
}

export function slotOfCategory(category: string): Slot {
  return CATEGORY_SLOT[category as keyof typeof CATEGORY_SLOT] ?? 'top';
}
