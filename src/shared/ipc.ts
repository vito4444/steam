/**
 * 主进程 ↔ 渲染进程的 IPC 契约。
 * preload 把这套接口挂到 window.pixelfit，渲染进程只认这个接口，不碰 Node。
 */

import type { Asset, AssetMeta, BaseBodySet, LibraryStats, Look, LookRecord } from './types';
import type { BodyType } from './spec';
import type {
  TryOnGenerateRequest,
  TryOnGenerateResult,
  TryOnProviderStatus,
  TryOnSettingsState,
  TryOnSettingsUpdate,
} from './tryon';
import type { OcclusionOverride } from './occlusion';

export interface ExportRequest {
  /** PNG dataURL */
  dataUrl: string;
  suggestedName: string;
}

export interface ExportResult {
  ok: boolean;
  path?: string;
  canceled?: boolean;
  error?: string;
}

export interface BackupResult {
  ok: boolean;
  path?: string;
  files?: number;
  error?: string;
}

export interface PipelineStatus {
  installed: boolean;
  version: string | null;
  message: string;
  qualityGate: boolean;
  automatic: boolean;
  provider: 'CPUExecutionProvider' | null;
}

export interface PhotoImportResult {
  imported: number;
  rejected: number;
  assets: Asset[];
  canceled?: boolean;
  message?: string;
}

export interface ManualImportFailure {
  file: string;
  message: string;
}

export interface ManualImportResult {
  canceled: boolean;
  imported: number;
  assets: Asset[];
  failures: ManualImportFailure[];
}

export interface LinkImportResult {
  status: 'ok' | 'partial' | 'manual_required' | 'failed';
  imported: number;
  assets: Asset[];
  platform: string;
  message: string;
  title?: string | null;
}

export interface PixelFitApi {
  library: {
    stats(): Promise<LibraryStats>;
    listAssets(): Promise<Asset[]>;
    updateAsset(id: string, patch: Partial<AssetMeta>): Promise<Asset>;
    deleteAsset(id: string): Promise<{ ok: boolean }>;
    /** 从任意 PNG/JPG 文件导入一件素材（管线未接入时的手动入口） */
    importFiles(): Promise<ManualImportResult>;
    /** 导入一个素材包目录（CERE-10 交付的真实素材走这条路） */
    importPack(): Promise<{ imported: number; failed: number; pack: string; canceled?: boolean }>;
    resetLibrary(): Promise<{ ok: boolean }>;
    backup(): Promise<BackupResult>;
    revealRoot(): Promise<void>;
  };
  looks: {
    list(): Promise<LookRecord[]>;
    save(look: Look, coverDataUrl: string | null): Promise<LookRecord>;
    delete(id: string): Promise<{ ok: boolean }>;
  };
  base: {
    get(body: BodyType): Promise<BaseBodySet>;
  };
  pipeline: {
    status(): Promise<PipelineStatus>;
    importPhotos(): Promise<PhotoImportResult>;
  };
  link: {
    import(url: string): Promise<LinkImportResult>;
  };
  tryOn: {
    status(): Promise<TryOnProviderStatus>;
    settings(): Promise<TryOnSettingsState>;
    saveSettings(update: TryOnSettingsUpdate): Promise<TryOnSettingsState>;
    generate(request: TryOnGenerateRequest): Promise<TryOnGenerateResult>;
    cancel(clientRequestId: string): void;
  };
  rules: {
    /**
     * 素材库根目录下的 `occlusion.json`（遮挡规则覆盖）。没有这个文件就返回
     * null，用内置默认表 —— 规则表要能被非开发者改，所以它是一份可选的
     * 外部文件，而不是编译进去的常量。
     */
    occlusion(): Promise<{ override: OcclusionOverride | null; path: string; error?: string }>;
  };
  exportPng(req: ExportRequest): Promise<ExportResult>;
  window: {
    minimize(): void;
    toggleMaximize(): void;
    close(): void;
    isMaximized(): Promise<boolean>;
  };
}

declare global {
  interface Window {
    pixelfit: PixelFitApi;
  }
}
