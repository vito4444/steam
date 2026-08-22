import { app, BrowserWindow, dialog, ipcMain, nativeImage, net, protocol, safeStorage, shell } from 'electron';
import fs from 'node:fs';
import fsp from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

import { Store, newId, toAppUrl, fromAppUrl } from './storage';
import { assetsDir, baseAssetsDir } from './assets';
import { analyzePhoto, cutoutSubject, pipelineStatus } from './pipeline';
import {
  automaticImportTags,
  collectPipelineCandidates,
  decideLinkImport,
  manualImportFailure,
  parseShotPhotoFixtures,
  settlePipelineCandidateImports,
  toPhotoImportCandidate,
  type PipelineCandidate,
} from './photo-import';
import { importBasePhoto, readSilhouette, resetBasePhoto } from './base-import';
import { importFromLink } from './link-import/index.js';
import { alphaBoundsFromBgra, measureLandmarksFromBgra } from './image-geometry';
import { importPack } from './pack';
import { ensureBundledWardrobe } from './bundled';
import { parseFigureRecipes, parseShotWindowSize, resolveShotOutputDir, runFigures, runShots, runTriptychs } from './shots';
import { TryOnDiskCache } from './tryon/cache';
import { createProvider, createSettingsState } from './tryon/provider-registry';
import { TryOnService } from './tryon/service';
import { TryOnSettingsRepository } from './tryon/settings';
import type { Asset, AssetMeta, CommerceSource, Look, LookRecord } from '../shared/types';
import type { TryOnGenerateRequest, TryOnSettingsUpdate } from '../shared/tryon';
import { CATEGORY_LABEL, CATEGORY_SLOT, SLOT_ANCHOR, SLOT_PLACEMENT, CANVAS } from '../shared/spec';
import type { BodyType, Category, Slot } from '../shared/spec';

const SHOT_MODE = process.argv.includes('--shots');
const FIGURE_MODE = process.argv.includes('--figures');
const TRIPTYCH_MODE = process.argv.includes('--cere26-triptychs');
/** `--ingest ... --exit`：只导素材，不开界面 */
const INGEST_ONLY = process.argv.includes('--exit');

/*
 * 截图跑在一个独立的 Electron profile 上（CERE-28）。
 *
 * `onboarding` 场景断言 localStorage 是空的，但 localStorage 存在 Electron 的
 * userData 里，跟 PIXELFIT_ROOT 无关。于是同一台机器跑第二次时，上一次
 * 「先看看示例」写下的 key 还在，首次引导直接不显示 —— 截图结果取决于机器状态，
 * 这种证据不算数。把 profile 也钉到 PIXELFIT_ROOT 下面，每次跑都是干净的。
 */
if (SHOT_MODE && process.env['PIXELFIT_ROOT']) {
  app.setPath('userData', path.join(process.env['PIXELFIT_ROOT'], 'electron-profile'));
}

protocol.registerSchemesAsPrivileged([
  { scheme: 'pf', privileges: { standard: true, secure: true, supportFetchAPI: true, bypassCSP: true } },
]);

let store: Store;
let tryOnService: TryOnService;
let tryOnCache: TryOnDiskCache;
let tryOnSettings: TryOnSettingsRepository;
let mainWindow: BrowserWindow | null = null;
const tryOnControllers = new Map<string, AbortController>();

function toAsset(meta: AssetMeta): Asset {
  const dir = store.assetDir(meta.id);
  return {
    ...meta,
    cutoutUrl: toAppUrl(path.join(dir, meta.files.cutout)),
    thumbUrl: toAppUrl(path.join(dir, meta.files.thumb)),
  };
}

function toLookRecord(look: Look): LookRecord {
  const cover = path.join(store.lookDir(look.id), 'cover.png');
  return { ...look, coverUrl: fs.existsSync(cover) ? `${toAppUrl(cover)}?v=${Date.parse(look.updated_at)}` : null };
}

function createWindow(): BrowserWindow {
  // CERE-28：成员实际是在一个窄窗口里用的，1280 的下限等于「窄了就没法用」。
  // 布局现在按断点自适应，所以下限跟着放到 1024。
  const shotSize = SHOT_MODE ? parseShotWindowSize(process.env['PIXELFIT_SHOT_SIZE']) : undefined;
  const win = new BrowserWindow({
    width: shotSize?.width ?? 1520,
    height: shotSize?.height ?? 950,
    minWidth: 1024,
    minHeight: 720,
    show: false,
    frame: false,
    backgroundColor: '#16151A',
    title: 'PixelFit',
    webPreferences: {
      preload: path.join(__dirname, '../preload/index.js'),
      sandbox: false,
      contextIsolation: true,
    },
  });

  if (shotSize) win.setContentSize(shotSize.width, shotSize.height);

  win.on('ready-to-show', () => win.show());
  win.webContents.setWindowOpenHandler(({ url }) => {
    shell.openExternal(url);
    return { action: 'deny' };
  });

  const devUrl = process.env['ELECTRON_RENDERER_URL'];
  if (devUrl) win.loadURL(devUrl);
  else win.loadFile(path.join(__dirname, '../renderer/index.html'));

  return win;
}

// ---------------------------------------------------------------- IPC

function registerIpc(): void {
  ipcMain.handle('library:stats', async () => {
    const [assets, looks] = await Promise.all([store.listAssets(), store.listLooks()]);
    return { assets: assets.length, looks: looks.length, root: store.root };
  });

  ipcMain.handle('library:listAssets', async () => (await store.listAssets()).map(toAsset));

  ipcMain.handle('library:updateAsset', async (_e, id: string, patch: Partial<AssetMeta>) =>
    toAsset(await store.patchAsset(id, patch)));

  ipcMain.handle('library:deleteAsset', async (_e, id: string) => {
    await store.deleteAsset(id);
    // 引用了这件素材的 Look 要把对应槽位清空，否则会留下悬空引用
    for (const look of await store.listLooks()) {
      let touched = false;
      const slots = { ...look.slots };
      for (const [slot, ref] of Object.entries(slots)) {
        if (ref === id) {
          slots[slot as Slot] = null;
          touched = true;
        }
      }
      if (touched) await store.writeLook({ ...look, slots, updated_at: new Date().toISOString() }, null);
    }
    return { ok: true };
  });

  ipcMain.handle('library:importPack', async () => {
    const res = await dialog.showOpenDialog(mainWindow!, {
      title: '选择素材包目录（含 index.json）',
      properties: ['openDirectory'],
    });
    if (res.canceled) return { imported: 0, failed: 0, pack: '', canceled: true };
    const r = await importPack(store, res.filePaths[0]);
    for (const f of r.failed) console.warn('[pack] failed', f.file, f.error);
    return { imported: r.imported, failed: r.failed.length, pack: r.pack };
  });

  ipcMain.handle('library:reset', async () => {
    await store.reset();
    return { ok: true };
  });

  ipcMain.handle('library:backup', async () => {
    try {
      const r = await store.backup();
      return { ok: true, path: r.path, files: r.files };
    } catch (err) {
      return { ok: false, error: String(err) };
    }
  });

  ipcMain.handle('library:revealRoot', async () => {
    await shell.openPath(store.root);
  });

  ipcMain.handle('library:importFiles', async () => {
    const res = await dialog.showOpenDialog(mainWindow!, {
      title: '手动导入透明底衣物素材',
      filters: [{ name: '透明图片', extensions: ['png', 'webp'] }],
      properties: ['openFile', 'multiSelections'],
    });
    if (res.canceled) return { canceled: true, imported: 0, assets: [], failures: [] };

    const out: Asset[] = [];
    const failures = [];
    for (const file of res.filePaths) {
      try {
        out.push(toAsset(await importOne(file, { requireTransparency: true })));
      } catch (err) {
        failures.push(manualImportFailure(file, err));
        console.warn('[import] failed', file, err);
      }
    }
    return { canceled: false, imported: out.length, assets: out, failures };
  });

  ipcMain.handle('looks:list', async () => (await store.listLooks()).map(toLookRecord));

  ipcMain.handle('looks:save', async (_e, look: Look, coverDataUrl: string | null) => {
    const cover = coverDataUrl ? Buffer.from(coverDataUrl.split(',')[1], 'base64') : null;
    const saved = await store.writeLook(look, cover);
    return toLookRecord(saved);
  });

  ipcMain.handle('looks:delete', async (_e, id: string) => {
    await store.deleteLook(id);
    return { ok: true };
  });

  ipcMain.handle('base:get', async (_e, body: BodyType) => store.getBase(body, baseAssetsDir()));

  // CERE-28：成员要的是「直接上传模特照片」，不是去找文件夹改 manifest。
  ipcMain.handle('base:importPhoto', async (_e, body: BodyType) => {
    const picked = await dialog.showOpenDialog(mainWindow!, {
      title: '选择一张全身模特照片',
      filters: [{ name: '照片', extensions: ['png', 'jpg', 'jpeg', 'webp'] }],
      properties: ['openFile'],
    });
    if (picked.canceled || !picked.filePaths[0]) return { ok: false, canceled: true };
    try {
      const result = await importBasePhoto(body, picked.filePaths[0], {
        cutoutSubject,
        readSilhouette,
        baseLibraryDir: store.baseDir,
        tmpDir: store.tmpDir,
      });
      return {
        ok: true,
        pack: result.pack,
        canvas: result.canvas,
        fallbackAnchors: result.fallbackAnchors,
        notes: result.notes,
      };
    } catch (error) {
      // 绝对路径不进渲染进程，错误信息只留原因。
      return { ok: false, error: manualImportFailure(picked.filePaths[0], error).message };
    }
  });

  ipcMain.handle('base:reset', async (_e, body: BodyType) => {
    const restored = await resetBasePhoto(body, store.baseDir);
    return { ok: true, restored };
  });

  ipcMain.handle('pipeline:status', async () => pipelineStatus());

  ipcMain.handle('pipeline:importPhotos', async () => {
    const shotFixtures = SHOT_MODE
      ? parseShotPhotoFixtures(process.env['PIXELFIT_PHOTO_IMPORT_FIXTURES'])
      : [];
    const picked = shotFixtures.length
      ? { canceled: false, filePaths: shotFixtures }
      : await dialog.showOpenDialog(mainWindow!, {
        title: '选择包含衣物的照片',
        filters: [{ name: '照片', extensions: ['png', 'jpg', 'jpeg', 'webp'] }],
        properties: ['openFile', 'multiSelections'],
      });
    if (picked.canceled) {
      return {
        imported: 0,
        needsOptimization: 0,
        rejected: 0,
        assets: [],
        candidates: [],
        canceled: true,
      };
    }
    const assets: Asset[] = [];
    const candidates: ReturnType<typeof toPhotoImportCandidate>[] = [];
    let needsOptimization = 0;
    let rejected = 0;
    const failures: string[] = [];
    for (const file of picked.filePaths) {
      try {
        const imported = await importAnalyzedPhoto(file);
        assets.push(...imported.assets.map(toAsset));
        candidates.push(...imported.candidates.map((candidate) =>
          toPhotoImportCandidate(candidate, toAppUrl)));
        needsOptimization += imported.needsOptimization;
        rejected += imported.rejected;
        failures.push(...imported.failures.map(
          (failure) => `${path.basename(file)} / ${failure}`,
        ));
      } catch (error) {
        failures.push(`${path.basename(file)}：${error instanceof Error ? error.message : String(error)}`);
      }
    }
    return {
      imported: assets.length,
      needsOptimization,
      rejected,
      assets,
      candidates,
      message: failures.length ? failures.join('\n') : undefined,
    };
  });

  ipcMain.handle('link:import', async (_event, input: string) => {
    const result = await importFromLink(input, {
      downloadDir: path.join(store.tmpDir, 'link-import'),
    });
    const decision = decideLinkImport(result);
    if (decision.action === 'manual') {
      return {
        status: 'manual_required', imported: 0, assets: [], platform: result.platform,
        title: result.product.title, message: decision.message,
      };
    }
    try {
      const imported = await importAnalyzedPhoto(decision.imagePath, {
        name: result.product.title,
        commerce: result.commerce,
      });
      const partialFailures = imported.failures.length > 0;
      return {
        status: partialFailures ? 'partial' : result.status,
        imported: imported.assets.length,
        assets: imported.assets.map(toAsset),
        platform: result.platform,
        title: result.product.title,
        message: partialFailures
          ? `已导入 ${imported.assets.length} 件；${imported.failures.join('；')}`
          : imported.rejected
          ? `已导入 ${imported.assets.length} 件；${imported.rejected} 个候选未通过 CERE-12 质量门。`
          : `已从 ${result.platform} 商品页导入 ${imported.assets.length} 件。`,
      };
    } catch (error) {
      return {
        status: 'failed', imported: 0, assets: [], platform: result.platform,
        title: result.product.title,
        message: error instanceof Error ? error.message : String(error),
      };
    }
  });

  ipcMain.handle('tryon:status', () => tryOnService.status());

  ipcMain.handle('tryon:settings', async () =>
    createSettingsState(await tryOnSettings.load()));

  ipcMain.handle('tryon:saveSettings', async (_e, update: TryOnSettingsUpdate) => {
    const settings = await tryOnSettings.update(update);
    tryOnService = new TryOnService(createProvider(process.env, {}, settings), tryOnCache);
    return createSettingsState(settings);
  });

  ipcMain.handle('tryon:generate', async (_e, request: TryOnGenerateRequest) => {
    const previous = tryOnControllers.get(request.clientRequestId);
    previous?.abort(new DOMException('Superseded by a newer request', 'AbortError'));
    const controller = new AbortController();
    tryOnControllers.set(request.clientRequestId, controller);
    const timeout = setTimeout(
      () => controller.abort(new DOMException('Cloud try-on timeout', 'TimeoutError')),
      tryOnTimeoutMs(),
    );
    try {
      return await tryOnService.generate(request, controller.signal);
    } finally {
      clearTimeout(timeout);
      if (tryOnControllers.get(request.clientRequestId) === controller) {
        tryOnControllers.delete(request.clientRequestId);
      }
    }
  });

  ipcMain.on('tryon:cancel', (_e, clientRequestId: string) => {
    tryOnControllers.get(clientRequestId)?.abort(new DOMException('Cancelled by user', 'AbortError'));
  });

  // 遮挡规则覆盖文件。读坏了不报错、不崩，退回内置默认表并把原因带回界面 ——
  // 这份文件是给人手改的，写错一个逗号不该让应用打不开。
  ipcMain.handle('rules:occlusion', async () => {
    const file = path.join(store.root, 'occlusion.json');
    if (!fs.existsSync(file)) return { override: null, path: file };
    try {
      return { override: JSON.parse(await fsp.readFile(file, 'utf8')), path: file };
    } catch (err) {
      return { override: null, path: file, error: String(err) };
    }
  });
  ipcMain.handle('export:png', async (_e, req: { dataUrl: string; suggestedName: string }) => {
    try {
      const res = await dialog.showSaveDialog(mainWindow!, {
        title: '导出图片',
        defaultPath: path.join(store.exportsDir, req.suggestedName),
        filters: [{ name: 'PNG', extensions: ['png'] }],
      });
      if (res.canceled || !res.filePath) return { ok: false, canceled: true };
      await fsp.writeFile(res.filePath, Buffer.from(req.dataUrl.split(',')[1], 'base64'));
      return { ok: true, path: res.filePath };
    } catch (err) {
      return { ok: false, error: String(err) };
    }
  });

  ipcMain.on('window:minimize', () => mainWindow?.minimize());
  ipcMain.on('window:toggleMaximize', () => {
    if (!mainWindow) return;
    if (mainWindow.isMaximized()) mainWindow.unmaximize();
    else mainWindow.maximize();
  });
  ipcMain.on('window:close', () => mainWindow?.close());
  ipcMain.handle('window:isMaximized', () => mainWindow?.isMaximized() ?? false);
}

/**
 * 手动导入一张图片作为素材。
 *
 * 这是 CERE-12 自动管线之外的人工兜底：只接受已有透明背景的素材，
 * 不做抠图、不做任何风格化，只写元数据。
 * 缩放与位置交给渲染期的自动贴合（`render/fit.ts`）按底图锚点解，所以这里
 * 不再算绝对 scale —— 底图换一版，已导入的素材不用重算。
 */
interface ImportOneOptions {
  category?: Category;
  name?: string | null;
  commerce?: CommerceSource;
  photoId?: string;
  originalFile?: string;
  provenanceModel?: string;
  reviewStatus?: 'ready' | 'needs_optimization';
  requireTransparency?: boolean;
}

async function importAnalyzedPhoto(
  file: string,
  context: Pick<ImportOneOptions, 'name' | 'commerce'> = {},
): Promise<{
  assets: AssetMeta[];
  candidates: PipelineCandidate[];
  rejected: number;
  needsOptimization: number;
  failures: string[];
}> {
  const importId = `import-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;
  const outputDir = path.join(store.root, 'pipeline-imports');
  const metadata = await analyzePhoto(file, outputDir, importId);
  const candidates = collectPipelineCandidates(metadata, path.join(outputDir, importId));
  const settled = await settlePipelineCandidateImports(candidates, async (candidate) => {
    const reviewStatus = candidate.state === 'needs_optimization'
      ? 'needs_optimization'
      : 'ready';
    return importOne(candidate.importFile!, {
      category: candidate.category,
      name: context.name
        ? `${context.name} · ${CATEGORY_LABEL[candidate.category]}`
        : `${path.basename(file, path.extname(file))} · ${CATEGORY_LABEL[candidate.category]}`,
      commerce: context.commerce,
      photoId: importId,
      originalFile: file,
      provenanceModel: 'cere12-cpu-pipeline',
      reviewStatus,
      requireTransparency: true,
    });
  });
  return {
    assets: settled.assets,
    candidates,
    rejected: candidates.filter((candidate) => candidate.state === 'retry').length,
    needsOptimization: settled.reviewStates.filter(
      (state) => state === 'needs_optimization',
    ).length,
    failures: settled.failures,
  };
}

async function importOne(file: string, options: ImportOneOptions = {}): Promise<AssetMeta> {
  const buf = await fsp.readFile(file);
  const img = nativeImage.createFromBuffer(buf);
  if (img.isEmpty()) throw new Error('unsupported image');
  const sourceSize = img.getSize();
  const sourceBitmap = img.toBitmap();

  if (options.requireTransparency) {
    let transparent = false;
    for (let i = 3; i < sourceBitmap.length; i += 4) {
      if (sourceBitmap[i] < 250) {
        transparent = true;
        break;
      }
    }
    if (!transparent) throw new Error('图片没有透明背景；请使用「照片自动识别」先抠图');
  }

  const box = alphaBoundsFromBgra(sourceBitmap, sourceSize.width, sourceSize.height);
  if (!box) throw new Error('fully transparent cutout');
  const cropped = img.crop(box);
  const size = cropped.getSize();
  const landmarks = measureLandmarksFromBgra(cropped.toBitmap(), size.width, size.height);

  const category: Category = options.category ?? guessCategory(path.basename(file));
  const slot: Slot = CATEGORY_SLOT[category];
  const rule = SLOT_PLACEMENT[slot];
  const anchorName = SLOT_ANCHOR[slot];

  // 锚点落在素材位图的哪条边上，由槽位的贴合规则决定
  const anchorY = rule.edge === 'top' ? 0 : rule.edge === 'bottom' ? size.height : size.height / 2;

  const thumbSrc = cropped.resize({
    width: Math.max(Math.round(256 * Math.min(1, size.width / Math.max(size.width, size.height))), 1),
    height: Math.max(Math.round(256 * Math.min(1, size.height / Math.max(size.width, size.height))), 1),
    quality: 'best',
  });

  const id = newId('itm');
  const now = new Date().toISOString();
  const meta: AssetMeta = {
    schema_version: 3,
    id,
    name: (options.name ?? path.basename(file, path.extname(file))).slice(0, 60) || '未命名素材',
    category,
    subcategory: '',
    slot,
    z_offset: 0,
    occupies: category === 'dress' ? ['top', 'bottom'] : [],
    companion: null,
    pose: 'front_idle',
    canvas: { w: CANVAS.w, h: CANVAS.h },
    bitmap: { file: 'cutout.png', w: size.width, h: size.height },
    source_resolution: { w: sourceSize.width, h: sourceSize.height },
    anchor: { base: anchorName, x: Math.round(size.width / 2), y: Math.round(anchorY) },
    landmarks,
    landmarks_given: [],
    fit: { scale: 1, dx: 0, dy: 0, stretch_x: 1 },
    palette: { dominant: '#8A8A90', color_family: 'multi', colors: [{ hex: '#8A8A90', ratio: 1, role: 'mid' }] },
    attributes: {},
    tags: options.provenanceModel
      ? automaticImportTags(options.reviewStatus ?? 'ready')
      : ['手动导入'],
    ...(options.provenanceModel
      ? { review_status: options.reviewStatus ?? 'ready' }
      : {}),
    season: [],
    source: {
      origin: options.commerce ? 'link' : options.photoId ? 'photo' : 'manual',
      photo_id: options.photoId ?? null,
      photo_file: options.originalFile ? 'original.jpg' : null,
      bbox: [box.x, box.y, box.width, box.height],
      imported_at: now,
      ...(options.commerce ? { commerce: options.commerce } : {}),
    },
    provenance: {
      model: options.provenanceModel ?? 'manual-import', model_version: options.provenanceModel ? '2.0' : '0.2.0',
      confidence: 1, edited_by_user: !options.provenanceModel,
      edit_ops: options.provenanceModel
        ? ['segment', 'closed_form_matting', 'quality_gate', 'crop_to_alpha']
        : ['category', 'crop_to_alpha'],
    },
    files: { original: options.originalFile ? 'original.jpg' : null, cutout: 'cutout.png', thumb: 'thumb.png' },
    favorite: false,
    wear_count: 0,
    created_at: now,
    updated_at: now,
  };

  let original: Buffer | undefined;
  if (options.originalFile) {
    const sourceImage = nativeImage.createFromBuffer(await fsp.readFile(options.originalFile));
    if (!sourceImage.isEmpty()) original = sourceImage.toJPEG(90);
  }
  return store.writeAsset(meta, { cutout: cropped.toPNG(), thumb: thumbSrc.toPNG(), original });
}

const CATEGORY_HINTS: [RegExp, Category][] = [
  [/dress|连衣裙|裙子/i, 'dress'],
  [/skirt|半裙|裤|pants|jean|trouser|short/i, 'bottom'],
  [/coat|jacket|outer|外套|风衣|大衣|开衫/i, 'outer'],
  [/shoe|boot|sneaker|鞋|靴/i, 'shoe'],
  [/bag|包/i, 'bag'],
  [/hat|cap|帽/i, 'headwear'],
  [/glass|镜/i, 'eyewear'],
  [/scarf|围巾/i, 'neckwear'],
  [/belt|腰带/i, 'belt'],
];

function guessCategory(filename: string): Category {
  for (const [re, cat] of CATEGORY_HINTS) if (re.test(filename)) return cat;
  return 'top';
}

// ---------------------------------------------------------------- boot

app.whenReady().then(async () => {
  // PIXELFIT_ROOT 指定素材库位置，缺省仍是 %APPDATA%/PixelFit。
  // 截图 / 演示要跑一套独立数据（`--shots` 的空状态场景会真的清库），
  // 没有这个开关就只能拿用户的真实素材库当试验田。
  store = new Store(process.env['PIXELFIT_ROOT'] || undefined);
  store.init();
  try {
    const seeded = await ensureBundledWardrobe(store, path.join(assetsDir(), 'wardrobe'), importPack);
    if (seeded.seeded) console.log(`[bundle] ${seeded.pack}: ${seeded.imported} real-photo assets seeded`);
    for (const failure of seeded.failed) console.warn('[bundle] failed', failure.file, failure.error);
  } catch (err) {
    // A damaged optional bundle must never turn into a launch crash. The manual
    // importer remains available and the next launch will retry the seed.
    console.warn('[bundle] bundled wardrobe unavailable', err);
  }
  tryOnSettings = new TryOnSettingsRepository(path.join(store.root, 'settings.json'), {
    encrypt(value) {
      if (!safeStorage.isEncryptionAvailable()) {
        throw new Error('当前系统无法使用 Windows 安全存储，拒绝明文保存 API Key');
      }
      return safeStorage.encryptString(value).toString('base64');
    },
    decrypt(value) {
      if (!safeStorage.isEncryptionAvailable()) throw new Error('Windows 安全存储不可用');
      return safeStorage.decryptString(Buffer.from(value, 'base64'));
    },
  });
  const settings = await tryOnSettings.load();
  tryOnCache = new TryOnDiskCache(path.join(store.root, 'cache', 'tryon'));
  tryOnService = new TryOnService(
    createProvider(process.env, {}, settings),
    tryOnCache,
  );

  protocol.handle('pf', (request) => {
    const abs = fromAppUrl(request.url.split('?')[0]);
    // 只允许读取素材库与应用自带素材，挡住任意路径读取
    const allowed = [store.root, assetsDir()].map((p) => path.resolve(p).replace(/\\/g, '/').toLowerCase());
    const norm = path.resolve(abs).replace(/\\/g, '/').toLowerCase();
    if (!allowed.some((root) => norm.startsWith(root))) {
      return new Response('forbidden', { status: 403 });
    }
    return net.fetch(pathToFileURL(abs).toString());
  });

  registerIpc();

  const ingestIdx = process.argv.indexOf('--ingest');
  if (ingestIdx >= 0 && process.argv[ingestIdx + 1]) {
    const r = await importPack(store, process.argv[ingestIdx + 1]);
    console.log(`[pack] ${r.pack}: ${r.imported} imported, ${r.failed.length} failed`);
    for (const f of r.failed) console.warn('[pack] failed', f.file, f.error);
  }

  let figureRecipes: ReturnType<typeof parseFigureRecipes> | undefined;
  if (FIGURE_MODE) {
    try {
      figureRecipes = parseFigureRecipes(process.env.PIXELFIT_FIGURE_RECIPES);
    } catch (error) {
      console.error('[figures] invalid recipe override', error);
      app.exit(1);
      return;
    }
  }

  mainWindow = createWindow();

  if (SHOT_MODE) {
    try {
      await runShots(mainWindow, resolveShotOutputDir({
        appPath: app.getAppPath(),
        libraryRoot: store.root,
        packaged: app.isPackaged,
        override: process.env['PIXELFIT_SHOT_DIR'],
      }));
      app.exit(0);
    } catch (err) {
      console.error('[shots] fatal', err);
      app.exit(1);
    }
    return;
  }

  if (FIGURE_MODE) {
    try {
      await runFigures(mainWindow, path.join(app.getAppPath(), 'figures'), figureRecipes);
    } catch (error) {
      console.error('[figures] run failed', error);
      app.exit(1);
      return;
    }
    app.quit();
    return;
  }

  if (TRIPTYCH_MODE) {
    try {
      await runTriptychs(mainWindow);
    } catch (error) {
      console.error('[triptych] run failed', error);
      app.exit(1);
      return;
    }
    app.quit();
    return;
  }

  // 只做导入、不需要界面时别把窗口留着（截图/对比图脚本会自己退出）
  if (INGEST_ONLY) app.quit();

  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) mainWindow = createWindow();
  });
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit();
});

function tryOnTimeoutMs(): number {
  const requested = Number(process.env.PIXELFIT_VTON_TIMEOUT_MS ?? '180000');
  if (!Number.isFinite(requested)) return 180_000;
  return Math.min(Math.max(Math.round(requested), 10_000), 600_000);
}
