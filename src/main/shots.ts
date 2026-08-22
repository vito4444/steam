/**
 * `electron . --shots`：真实渲染窗口截图。
 *
 * 用真窗口 + capturePage，而不是任何形式的「示意图」——交付要求截图是
 * 真的跑出来的。渲染进程通过 window.__pixelfitShot(name) 摆好状态，
 * 主进程等它 resolve 后再抓帧。
 */

import type { BrowserWindow } from 'electron';
import fsp from 'node:fs/promises';
import path from 'node:path';
import {
  FIGURE_RECIPES,
  type FigureRecipe,
  type ShotCheck,
} from '../shared/shots';

export { ASSET_ALIAS, FIGURE_RECIPES, SHOT_RECIPES, resolveRecipeAssetId } from '../shared/shots';

export interface ShotWindowSize {
  width: number;
  height: number;
}

/**
 * `PIXELFIT_SHOT_SIZE=1120x860`：截图时把窗口摆到指定内容尺寸。
 *
 * CERE-28 要求同一场景出窄 / 宽两档对比图，改动前后必须是同一个尺寸，
 * 否则「窄窗口修好了」这句话没有证据。
 */
export function parseShotWindowSize(raw: string | undefined): ShotWindowSize | undefined {
  if (!raw?.trim()) return undefined;
  const match = /^\s*(\d{3,5})\s*[x*×]\s*(\d{3,5})\s*$/i.exec(raw);
  if (!match) throw new Error('PIXELFIT_SHOT_SIZE must look like 1120x860');
  const width = Number(match[1]);
  const height = Number(match[2]);
  if (width < 320 || height < 320) throw new Error('PIXELFIT_SHOT_SIZE is too small to render the app');
  return { width, height };
}

export function parseFigureRecipes(raw: string | undefined): readonly FigureRecipe[] {
  if (!raw?.trim()) return FIGURE_RECIPES;
  let parsed: unknown;
  try {
    parsed = JSON.parse(raw);
  } catch {
    throw new Error('PIXELFIT_FIGURE_RECIPES must be valid JSON');
  }
  if (!Array.isArray(parsed) || parsed.length === 0) {
    throw new Error('PIXELFIT_FIGURE_RECIPES must be a non-empty recipe array');
  }
  const names = new Set<string>();
  return parsed.map((value) => {
    if (!value || typeof value !== 'object') throw new Error('PIXELFIT_FIGURE_RECIPES contains an invalid recipe');
    const record = value as Record<string, unknown>;
    const name = requiredRecipeText(record.name, 'name');
    if (!/^[a-z0-9][a-z0-9_-]*$/i.test(name) || names.has(name)) {
      throw new Error(`PIXELFIT_FIGURE_RECIPES has an invalid or duplicate name: ${name}`);
    }
    names.add(name);
    if (!Array.isArray(record.ids) || record.ids.length === 0 || record.ids.some((id) => typeof id !== 'string' || !id.trim())) {
      throw new Error(`PIXELFIT_FIGURE_RECIPES recipe ${name} must include image IDs`);
    }
    if (!Array.isArray(record.variants) || record.variants.length === 0 || record.variants.some((variant) => typeof variant !== 'string')) {
      throw new Error(`PIXELFIT_FIGURE_RECIPES recipe ${name} has invalid variants`);
    }
    const ids = record.ids.map((id) => (id as string).trim());
    if (new Set(ids).size !== ids.length) throw new Error(`PIXELFIT_FIGURE_RECIPES recipe ${name} has duplicate IDs`);
    const variants = record.variants.map((variant) => (variant as string).trim());
    if (variants.some((variant) => variant !== 'off' && variant !== 'on')) {
      throw new Error(`PIXELFIT_FIGURE_RECIPES recipe ${name} has invalid variants`);
    }
    if (new Set(variants).size !== variants.length) throw new Error(`PIXELFIT_FIGURE_RECIPES recipe ${name} has duplicate variants`);
    const tuck = parseRecipeTuck(record.tuck, name);
    return {
      name,
      ids,
      ...(tuck ? { tuck } : {}),
      variants: variants as Array<'off' | 'on'>,
    };
  });
}

function requiredRecipeText(value: unknown, label: string): string {
  if (typeof value !== 'string' || !value.trim()) throw new Error(`PIXELFIT_FIGURE_RECIPES recipe ${label} is required`);
  return value.trim();
}

function parseRecipeTuck(value: unknown, recipeName: string): Record<string, 'in' | 'out'> | undefined {
  if (value === undefined) return undefined;
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    throw new Error(`PIXELFIT_FIGURE_RECIPES recipe ${recipeName} has invalid tuck settings`);
  }
  const tuck: Record<string, 'in' | 'out'> = {};
  for (const [id, setting] of Object.entries(value)) {
    if (!id.trim() || (setting !== 'in' && setting !== 'out')) {
      throw new Error(`PIXELFIT_FIGURE_RECIPES recipe ${recipeName} has invalid tuck settings`);
    }
    tuck[id.trim()] = setting;
  }
  return tuck;
}

// 顺序有意义：'empty' 会真的清空素材库来截真实空状态，所以必须排在最后。
export const SHOT_SCENES = [
  'onboarding',
  'import',
  'cere53-candidates-top',
  'cere53-candidates-bottom',
  'cere53-wardrobe',
  'cere53-worn',
  'imported-wardrobe',
  'imported-worn',
  'main',
  'dressing',
  'raw',
  'processed',
  'layers',
  'fit',
  'fit_dress',
  'fit_layered',
  'fit_accessory',
  'fit_shoes',
  'fit_separates',
  'occlusion_off',
  'occlusion_on',
  'compare',
  'board',
  'board_edit',
  'board_drag',
  'board_dark',
  'board_export',
  'looks',
  'looks_filter',
  'base',
  'settings',
  'ai-settings',
  'ai-preview',
  'ai-preview-open',
  'ai-consent',
  'empty',
] as const;

const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));

interface ShotOutputOptions {
  appPath: string;
  libraryRoot: string;
  packaged: boolean;
  override?: string;
}

interface TriptychRecipe {
  id: string;
  baseImage: string;
  localImage: string;
  aliyunImage: string;
  outputFile: string;
}

export function resolveShotOutputDir(options: ShotOutputOptions): string {
  const override = options.override?.trim();
  if (override) return path.resolve(override);
  return options.packaged
    ? path.join(options.libraryRoot, 'shots')
    : path.join(options.appPath, 'shots');
}

interface ReadyWindow {
  webContents: {
    executeJavaScript(script: string): Promise<unknown>;
  };
}

interface ReadyOptions {
  attempts?: number;
  intervalMs?: number;
}

export interface ShotWaitOptions {
  attempts?: number;
  delayMs?: number;
}

export interface ShotRunOptions {
  requestedScenes?: string;
  readyAttempts?: number;
  readyDelayMs?: number;
  settleDelayMs?: number;
}

export async function waitForShotReady(
  win: ReadyWindow,
  options: ShotWaitOptions = {},
): Promise<void> {
  const attempts = options.attempts ?? 160;
  const delayMs = options.delayMs ?? 250;
  // 等渲染进程把钩子挂上，并且素材库真的载完 —— 否则第一张拍到空衣橱
  for (let i = 0; i < attempts; i++) {
    const state = await win.webContents
      .executeJavaScript('window.__pixelfitState ? window.__pixelfitState() : null')
      .catch(() => null);
    if (
      state
      && typeof state === 'object'
      && 'ready' in state
      && 'assets' in state
      && state.ready
      && typeof state.assets === 'number'
      && state.assets > 0
    ) return;
    if (i + 1 < attempts) await sleep(delayMs);
  }
  throw new Error(`Shot renderer not ready after ${attempts} attempts`);
}

/** Compatibility wrapper for the CERE-25 readiness seam. */
export function waitReady(win: ReadyWindow, options: ReadyOptions = {}): Promise<void> {
  return waitForShotReady(win, { attempts: options.attempts, delayMs: options.intervalMs });
}

export function selectShotScenes(requestedRaw?: string | string[]): string[] {
  const names = (Array.isArray(requestedRaw) ? requestedRaw : requestedRaw?.split(','))
    ?.map((name) => name.trim())
    .filter(Boolean) ?? [];
  if (names.length === 0) {
    return SHOT_SCENES.filter((scene) => !scene.startsWith('cere53-'));
  }
  const unknown = names.filter((name) => !SHOT_SCENES.includes(name as (typeof SHOT_SCENES)[number]));
  if (unknown.length) throw new Error(`Unknown shot scene(s): ${unknown.join(', ')}`);
  const wanted = new Set(names);
  return SHOT_SCENES.filter((scene) => wanted.has(scene));
}

export function collectCheckFailures(scene: string, checks: ShotCheck[]): string[] {
  return checks.flatMap((check) => {
    if (!check || typeof check.label !== 'string' || typeof check.passed !== 'boolean') {
      return [`${scene}：renderer returned a malformed check`];
    }
    if (check.passed) return [];
    return [`${scene}：${check.label}${check.detail ? `（${check.detail}）` : ''}`];
  });
}

export function assertRunSucceeded(kind: string, failures: string[]): void {
  if (failures.length === 0) return;
  throw new Error(`${kind} failed ${failures.length} required check(s):\n${failures.join('\n')}`);
}

export function figurePng(dataUrl: string | null): Buffer {
  if (!dataUrl) throw new Error('empty required figure render');
  const match = /^data:image\/png;base64,(.+)$/.exec(dataUrl);
  if (!match) throw new Error('invalid or empty required figure PNG data');
  const png = Buffer.from(match[1], 'base64');
  if (png.length === 0) throw new Error('empty required figure PNG data');
  return png;
}

export async function runShots(win: BrowserWindow, outDir: string, options: ShotRunOptions = {}): Promise<void> {
  const scenes = selectShotScenes(options.requestedScenes ?? process.env.PIXELFIT_SHOT_SCENES);
  await fsp.mkdir(outDir, { recursive: true });
  await waitForShotReady(win, { attempts: options.readyAttempts, delayMs: options.readyDelayMs });
  const failures: string[] = [];

  for (const scene of scenes) {
    try {
      await win.webContents.executeJavaScript('(window.__pixelfitChecks || []).splice(0)');
      const dispatchedAt = Date.now();
      await win.webContents.executeJavaScript(`window.__pixelfitShot(${JSON.stringify(scene)})`);
      console.log('[shots:dispatch]', scene, `${Date.now() - dispatchedAt}ms`);
      await sleep(options.settleDelayMs ?? 900);
      const visible = await win.webContents.executeJavaScript(`({
        activeRail: document.querySelector('.rail-btn.active span')?.textContent,
        heading: document.querySelector('.body h2')?.textContent,
        modal: document.querySelector('[role="dialog"]')?.textContent?.slice(0, 40)
      })`);
      console.log('[shots:state]', scene, visible);
      const img = await win.webContents.capturePage();
      await fsp.writeFile(path.join(outDir, `${scene}.png`), img.toPNG());
      console.log('[shots]', scene);
      await win.webContents.executeJavaScript(
        `window.__pixelfitShotAfter ? window.__pixelfitShotAfter(${JSON.stringify(scene)}) : undefined`,
      );
    } catch (err) {
      console.error('[shots] failed', scene, err);
      failures.push(`${scene}：${err instanceof Error ? err.message : String(err)}`);
    } finally {
      const checks = await win.webContents
        .executeJavaScript('(window.__pixelfitChecks || []).splice(0)')
        .catch((error) => {
          failures.push(`${scene}：failed to collect renderer checks (${String(error)})`);
          return [];
        }) as ShotCheck[];
      for (const check of checks) {
        console.log(
          '  [check]',
          `${check.label}：${check.passed ? '通过' : '失败'}${check.detail ? `：${check.detail}` : ''}`,
        );
      }
      failures.push(...collectCheckFailures(scene, checks));
    }
  }
  assertRunSucceeded('shots', failures);
}


/**
 * `electron . --figures`：出遮挡对比图的素材。
 *
 * 每一格都由**应用自己的渲染引擎**画出来（同一份规则表、同一条合成路径），
 * 只是把舞台单独导成透明底 PNG 交给拼图脚本，不是另画一套示意图。
 */
export async function runFigures(
  win: BrowserWindow,
  outDir: string,
  recipes = parseFigureRecipes(process.env.PIXELFIT_FIGURE_RECIPES),
): Promise<void> {
  await fsp.mkdir(outDir, { recursive: true });
  await waitForShotReady(win);
  const failures: string[] = [];

  for (const c of recipes) {
    for (const v of c.variants) {
      const spec = { ids: c.ids, tuck: c.tuck ?? {}, noOcc: v === 'off' };
      try {
        const dataUrl: string | null = await win.webContents.executeJavaScript(
          `window.__pixelfitFigure(${JSON.stringify(spec)})`,
        );
        await fsp.writeFile(
          path.join(outDir, `${c.name}-${v}.png`),
          figurePng(dataUrl),
        );
        console.log('[figures]', c.name, v);
      } catch (err) {
        console.error('[figures] failed', c.name, v, err);
        failures.push(`${c.name}-${v}：${err instanceof Error ? err.message : String(err)}`);
      }
    }
  }
  assertRunSucceeded('figures', failures);
}

/** CERE-26 comparison images are a literal three-panel Electron capture, never a retouched composite. */
export async function runTriptychs(win: BrowserWindow): Promise<void> {
  const recipes = parseTriptychRecipes(process.env.PIXELFIT_TRIPTYCH_RECIPES);
  win.setContentSize(2280, 1280);
  for (const recipe of recipes) {
    const [base, local, aliyun] = await Promise.all([
      imageFileDataUrl(recipe.baseImage),
      imageFileDataUrl(recipe.localImage),
      imageFileDataUrl(recipe.aliyunImage),
    ]);
    await win.webContents.executeJavaScript(`(async () => {
      const panels = ${JSON.stringify([
        { label: '原始人物', dataUrl: base },
        { label: '本地分层试穿', dataUrl: local },
        { label: 'Aliyun 生成试穿', dataUrl: aliyun },
      ])};
      document.documentElement.innerHTML = '<head><meta charset="utf-8"><style>'
        + '*{box-sizing:border-box}body{margin:0;background:#111;color:#f6f2eb;font-family:system-ui,sans-serif}'
        + 'main{height:1280px;padding:42px;display:grid;grid-template-columns:repeat(3,1fr);gap:24px}'
        + 'section{min-width:0;display:grid;grid-template-rows:56px 1fr;background:#201e24;border:1px solid #5e5962}'
        + 'h1{margin:0;padding:14px 18px;font-size:26px;letter-spacing:.02em}img{width:100%;height:100%;object-fit:contain;background:#fff}'
        + '</style></head><body><main></main></body>';
      const container = document.querySelector('main');
      if (!container) throw new Error('triptych container was not created');
      for (const panel of panels) {
        const section = document.createElement('section');
        const title = document.createElement('h1');
        title.textContent = panel.label;
        const image = document.createElement('img');
        image.alt = panel.label;
        image.src = panel.dataUrl;
        section.append(title, image);
        container.append(section);
      }
      await Promise.all([...document.images].map((image) => new Promise((resolve, reject) => {
        image.onload = () => resolve(undefined);
        image.onerror = () => reject(new Error('triptych image failed to load'));
      })));
      window.scrollTo(0, 0);
    })()`);
    await waitForTriptychPaint(win);
    // Windows capturePage can return the previous GPU/compositor surface on the first read.
    await win.webContents.capturePage();
    await waitForTriptychPaint(win);
    const image = await win.webContents.capturePage();
    const size = image.getSize();
    if (size.width < 2280 || size.height < 1280) {
      throw new Error(`CERE-26 triptych ${recipe.id} is too small: ${size.width}x${size.height}`);
    }
    await fsp.mkdir(path.dirname(recipe.outputFile), { recursive: true });
    await fsp.writeFile(recipe.outputFile, image.toPNG());
    console.log('[triptych]', recipe.id, `${size.width}x${size.height}`);
  }
}

/** Image decode completion precedes Chromium's committed paint; capture only after two rendered frames. */
export async function waitForTriptychPaint(win: BrowserWindow): Promise<void> {
  await win.webContents.executeJavaScript(`new Promise((resolve) => {
    requestAnimationFrame(() => requestAnimationFrame(() => resolve(undefined)));
  })`);
}

function parseTriptychRecipes(raw: string | undefined): TriptychRecipe[] {
  if (!raw?.trim()) throw new Error('PIXELFIT_TRIPTYCH_RECIPES is required');
  let parsed: unknown;
  try {
    parsed = JSON.parse(raw);
  } catch {
    throw new Error('PIXELFIT_TRIPTYCH_RECIPES must be valid JSON');
  }
  if (!Array.isArray(parsed) || parsed.length === 0) throw new Error('PIXELFIT_TRIPTYCH_RECIPES must be a non-empty array');
  const ids = new Set<string>();
  return parsed.map((value) => {
    if (!value || typeof value !== 'object') throw new Error('PIXELFIT_TRIPTYCH_RECIPES contains an invalid recipe');
    const record = value as Record<string, unknown>;
    const id = requiredTriptychText(record.id, 'id');
    if (ids.has(id)) throw new Error(`PIXELFIT_TRIPTYCH_RECIPES has duplicate ID: ${id}`);
    ids.add(id);
    return {
      id,
      baseImage: requiredTriptychText(record.baseImage, 'baseImage'),
      localImage: requiredTriptychText(record.localImage, 'localImage'),
      aliyunImage: requiredTriptychText(record.aliyunImage, 'aliyunImage'),
      outputFile: requiredTriptychText(record.outputFile, 'outputFile'),
    };
  });
}

function requiredTriptychText(value: unknown, field: string): string {
  if (typeof value !== 'string' || !value.trim()) throw new Error(`PIXELFIT_TRIPTYCH_RECIPES recipe ${field} is required`);
  return value;
}

async function imageFileDataUrl(file: string): Promise<string> {
  const image = await fsp.readFile(file);
  if (image.length === 0) throw new Error(`CERE-26 triptych input is missing or empty: ${file}`);
  const extension = path.extname(file).toLowerCase();
  const mime = extension === '.jpg' || extension === '.jpeg' ? 'image/jpeg'
    : extension === '.webp' ? 'image/webp'
      : extension === '.bmp' ? 'image/bmp' : extension === '.png' ? 'image/png' : null;
  if (!mime) throw new Error(`CERE-26 triptych input has unsupported image extension: ${file}`);
  return `data:${mime};base64,${image.toString('base64')}`;
}
