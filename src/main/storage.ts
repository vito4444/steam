/**
 * 本地存储层。
 *
 * 落盘结构（CERE-2 §技术选型：文件是唯一真相，索引可全量重建）：
 *
 *   %APPDATA%/PixelFit/
 *     library/
 *       assets/<itm_xxx>/{meta.json, cutout.png, thumb.png, original.jpg?}
 *       looks/<look_xxx>/{look.json, cover.png?}
 *       base/<body>/{layer png...}, anchors.json      ← CERE-6 底图包解压于此
 *     backups/<timestamp>/…
 *     exports/
 *     settings.json
 *
 * 写入一律「先写临时目录 → 原子 rename」，失败整件回滚，不留孤儿目录。
 */

import { app } from 'electron';
import fs from 'node:fs';
import fsp from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';

import type { AssetMeta, BaseBodySet, Look } from '../shared/types';
import type { BodyType } from '../shared/spec';

const CROCKFORD = '0123456789ABCDEFGHJKMNPQRSTVWXYZ';

export function newId(prefix: 'itm' | 'look'): string {
  const bytes = crypto.randomBytes(16);
  let out = '';
  for (let i = 0; i < 16; i++) out += CROCKFORD[bytes[i] % 32];
  return `${prefix}_${out}`;
}

export class Store {
  readonly root: string;
  readonly assetsDir: string;
  readonly looksDir: string;
  readonly baseDir: string;
  readonly backupsDir: string;
  readonly exportsDir: string;
  readonly tmpDir: string;

  constructor(rootOverride?: string) {
    this.root = rootOverride ?? path.join(app.getPath('appData'), 'PixelFit');
    this.assetsDir = path.join(this.root, 'library', 'assets');
    this.looksDir = path.join(this.root, 'library', 'looks');
    this.baseDir = path.join(this.root, 'library', 'base');
    this.backupsDir = path.join(this.root, 'backups');
    this.exportsDir = path.join(this.root, 'exports');
    this.tmpDir = path.join(this.root, '.tmp');
  }

  init(): void {
    for (const d of [this.assetsDir, this.looksDir, this.baseDir, this.backupsDir, this.exportsDir, this.tmpDir]) {
      fs.mkdirSync(d, { recursive: true });
    }
    // 上次崩溃留下的半成品
    for (const entry of fs.readdirSync(this.tmpDir)) {
      fs.rmSync(path.join(this.tmpDir, entry), { recursive: true, force: true });
    }
  }

  // ---------- assets ----------

  async listAssets(): Promise<AssetMeta[]> {
    const out: AssetMeta[] = [];
    let entries: string[];
    try {
      entries = await fsp.readdir(this.assetsDir);
    } catch {
      return out;
    }
    for (const id of entries) {
      const metaPath = path.join(this.assetsDir, id, 'meta.json');
      try {
        const raw = await fsp.readFile(metaPath, 'utf8');
        const meta = JSON.parse(raw) as AssetMeta;
        if (meta && meta.id) out.push(meta);
      } catch {
        // 单件损坏不拖垮整个衣橱：跳过并继续
        console.warn('[store] skip unreadable asset', id);
      }
    }
    out.sort((a, b) => (a.created_at < b.created_at ? 1 : -1));
    return out;
  }

  assetDir(id: string): string {
    return path.join(this.assetsDir, id);
  }

  /**
   * 事务性写入一件素材：内容先落到 .tmp/<id>，全部成功后原子 rename。
   */
  async writeAsset(meta: AssetMeta, files: { cutout: Buffer; thumb: Buffer; original?: Buffer }): Promise<AssetMeta> {
    const staging = path.join(this.tmpDir, `${meta.id}-${Date.now()}`);
    await fsp.mkdir(staging, { recursive: true });
    try {
      await fsp.writeFile(path.join(staging, 'cutout.png'), files.cutout);
      await fsp.writeFile(path.join(staging, 'thumb.png'), files.thumb);
      if (files.original) await fsp.writeFile(path.join(staging, 'original.jpg'), files.original);
      await fsp.writeFile(path.join(staging, 'meta.json'), JSON.stringify(meta, null, 2), 'utf8');

      const target = this.assetDir(meta.id);
      await fsp.rm(target, { recursive: true, force: true });
      await fsp.rename(staging, target);
      return meta;
    } catch (err) {
      await fsp.rm(staging, { recursive: true, force: true });
      throw err;
    }
  }

  async patchAsset(id: string, patch: Partial<AssetMeta>): Promise<AssetMeta> {
    const metaPath = path.join(this.assetDir(id), 'meta.json');
    const meta = JSON.parse(await fsp.readFile(metaPath, 'utf8')) as AssetMeta;
    const next: AssetMeta = { ...meta, ...patch, id: meta.id, updated_at: new Date().toISOString() };
    const tmp = `${metaPath}.tmp`;
    await fsp.writeFile(tmp, JSON.stringify(next, null, 2), 'utf8');
    await fsp.rename(tmp, metaPath);
    return next;
  }

  async deleteAsset(id: string): Promise<void> {
    await fsp.rm(this.assetDir(id), { recursive: true, force: true });
  }

  // ---------- looks ----------

  async listLooks(): Promise<Look[]> {
    const out: Look[] = [];
    let entries: string[];
    try {
      entries = await fsp.readdir(this.looksDir);
    } catch {
      return out;
    }
    for (const id of entries) {
      try {
        const raw = await fsp.readFile(path.join(this.looksDir, id, 'look.json'), 'utf8');
        out.push(JSON.parse(raw) as Look);
      } catch {
        console.warn('[store] skip unreadable look', id);
      }
    }
    out.sort((a, b) => (a.updated_at < b.updated_at ? 1 : -1));
    return out;
  }

  lookDir(id: string): string {
    return path.join(this.looksDir, id);
  }

  async writeLook(look: Look, cover: Buffer | null): Promise<Look> {
    const staging = path.join(this.tmpDir, `${look.id}-${Date.now()}`);
    await fsp.mkdir(staging, { recursive: true });
    try {
      if (cover) await fsp.writeFile(path.join(staging, 'cover.png'), cover);
      else {
        // 覆盖保存时保留旧封面
        const old = path.join(this.lookDir(look.id), 'cover.png');
        if (fs.existsSync(old)) await fsp.copyFile(old, path.join(staging, 'cover.png'));
      }
      await fsp.writeFile(path.join(staging, 'look.json'), JSON.stringify(look, null, 2), 'utf8');
      const target = this.lookDir(look.id);
      await fsp.rm(target, { recursive: true, force: true });
      await fsp.rename(staging, target);
      return look;
    } catch (err) {
      await fsp.rm(staging, { recursive: true, force: true });
      throw err;
    }
  }

  async deleteLook(id: string): Promise<void> {
    await fsp.rm(this.lookDir(id), { recursive: true, force: true });
  }

  // ---------- base bodies ----------

  /**
   * 读取模特底图。优先用 library/base（外部底图包解压位置，CERE-10 的真人
   * 底图落在这里就自动接管），找不到再用应用内置的写实底图包。
   *
   * 画布尺寸与锚点一律以底图包的 manifest 为准 —— 换一版底图不需要改代码。
   */
  async getBase(body: BodyType, builtinDir: string): Promise<BaseBodySet> {
    const external = path.join(this.baseDir, body);
    const useExternal = fs.existsSync(path.join(external, 'manifest.json'));
    const dir = useExternal ? external : path.join(builtinDir, body);
    type RawLayer = { file: string; z: number; mode: 'fill' | 'multiply' | 'normal'; tint?: 'skin' | 'hair' };
    type RawTone = { id: string; name: string; swatch: string; layers: RawLayer[] };
    const manifest = JSON.parse(await fsp.readFile(path.join(dir, 'manifest.json'), 'utf8')) as {
      pack?: string;
      canvas?: { w: number; h: number };
      layers?: RawLayer[];
      tones?: RawTone[];
      hair?: Record<string, { back: RawLayer[]; front: RawLayer[] }>;
      anchors?: Record<string, { x: number; y: number }>;
      /** 身体遮罩（CERE-13 的贴身底图会带上）；给了就用它裁衣物 */
      mask?: RawLayer | string;
    };
    const hydrate = (l: RawLayer) => ({ ...l, url: toAppUrl(path.join(dir, l.file)) });
    const hair: BaseBodySet['hair'] = {};
    for (const [style, set] of Object.entries(manifest.hair ?? {})) {
      hair[style] = { back: (set.back ?? []).map(hydrate), front: (set.front ?? []).map(hydrate) };
    }
    return {
      body,
      canvas: manifest.canvas ?? { w: 1152, h: 2304 },
      pack: manifest.pack ?? (useExternal ? 'external' : 'builtin'),
      source: useExternal ? 'library' : 'builtin',
      anchors: manifest.anchors,
      // mask 允许直接写成文件名字符串，底图包那边少填几个没意义的字段
      mask: manifest.mask
        ? hydrate(typeof manifest.mask === 'string'
          ? { file: manifest.mask, z: 0, mode: 'normal' }
          : manifest.mask)
        : undefined,
      layers: (manifest.layers ?? []).map(hydrate),
      tones: (manifest.tones ?? []).map((t) => ({ ...t, layers: t.layers.map(hydrate) })),
      hair,
    };
  }

  // ---------- backup ----------

  async backup(): Promise<{ path: string; files: number }> {
    const stamp = new Date().toISOString().replace(/[:.]/g, '-');
    const target = path.join(this.backupsDir, stamp);
    const src = path.join(this.root, 'library');
    let files = 0;
    const copyDir = async (from: string, to: string) => {
      await fsp.mkdir(to, { recursive: true });
      for (const entry of await fsp.readdir(from, { withFileTypes: true })) {
        const f = path.join(from, entry.name);
        const t = path.join(to, entry.name);
        if (entry.isDirectory()) await copyDir(f, t);
        else {
          await fsp.copyFile(f, t);
          files++;
        }
      }
    };
    await copyDir(src, target);
    return { path: target, files };
  }

  async reset(): Promise<void> {
    await fsp.rm(this.assetsDir, { recursive: true, force: true });
    await fsp.rm(this.looksDir, { recursive: true, force: true });
    await fsp.mkdir(this.assetsDir, { recursive: true });
    await fsp.mkdir(this.looksDir, { recursive: true });
  }
}

/** 把本地绝对路径转成渲染进程可加载的自定义协议 URL */
export function toAppUrl(absPath: string): string {
  return `pf://local/${encodeURI(absPath.replace(/\\/g, '/'))}`;
}

/** 反向解析 pf:// URL */
export function fromAppUrl(url: string): string {
  const u = new URL(url);
  return decodeURI(u.pathname).replace(/^\//, '');
}
