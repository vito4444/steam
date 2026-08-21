import fsp from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';

import { describe, expect, it, vi } from 'vitest';

import manifest from '../assets/wardrobe/index.json';
import * as shots from '../src/main/shots';
import { runShots } from '../src/main/shots';
import { resolveShotAssets } from '../src/renderer/src/shotAssets';
import type { Asset } from '../src/shared/types';

type ShotOutputResolver = (options: {
  appPath: string;
  libraryRoot: string;
  packaged: boolean;
  override?: string;
}) => string;

describe('shot output directory', () => {
  it('keeps packaged screenshots outside app.asar', () => {
    const resolveShotOutputDir = (shots as unknown as {
      resolveShotOutputDir?: ShotOutputResolver;
    }).resolveShotOutputDir;

    expect(resolveShotOutputDir).toBeTypeOf('function');
    expect(resolveShotOutputDir?.({
      appPath: 'C:\\Program Files\\PixelFit\\resources\\app.asar',
      libraryRoot: 'C:\\PixelFit Acceptance\\fresh-root',
      packaged: true,
    })).toBe('C:\\PixelFit Acceptance\\fresh-root\\shots');
  });

  it('honors an explicit writable screenshot directory', () => {
    const resolveShotOutputDir = shots.resolveShotOutputDir as ShotOutputResolver;

    expect(resolveShotOutputDir({
      appPath: 'C:\\Program Files\\PixelFit\\resources\\app.asar',
      libraryRoot: 'C:\\PixelFit Acceptance\\fresh-root',
      packaged: true,
      override: 'C:\\PixelFit Acceptance\\captures',
    })).toBe('C:\\PixelFit Acceptance\\captures');
  });
});

interface Check {
  label: string;
  passed: boolean;
  detail?: string;
}

describe('shot failure gates', () => {
  it('does not capture a triptych until the renderer confirms a committed paint', async () => {
    const root = await fsp.mkdtemp(path.join(os.tmpdir(), 'pixelfit-triptych-paint-'));
    const inputs = ['base.png', 'local.png', 'aliyun.png'];
    await Promise.all(inputs.map((file) => fsp.writeFile(path.join(root, file), 'image')));
    const outputFile = path.join(root, 'triptych.png');
    const previous = process.env.PIXELFIT_TRIPTYCH_RECIPES;
    process.env.PIXELFIT_TRIPTYCH_RECIPES = JSON.stringify([{
      id: 'paint-gate',
      baseImage: path.join(root, inputs[0]),
      localImage: path.join(root, inputs[1]),
      aliyunImage: path.join(root, inputs[2]),
      outputFile,
    }]);

    let confirmPaint!: () => void;
    const paintConfirmed = new Promise<void>((resolve) => { confirmPaint = resolve; });
    let scriptCalls = 0;
    const executeJavaScript = vi.fn(async () => {
      scriptCalls += 1;
      if (scriptCalls === 2) await paintConfirmed;
    });
    const staleFrame = {
      getSize: () => ({ width: 2280, height: 1280 }),
      toPNG: () => Buffer.from('stale'),
    };
    const committedFrame = {
      getSize: () => ({ width: 2280, height: 1280 }),
      toPNG: () => Buffer.from('committed'),
    };
    const capturePage = vi.fn()
      .mockResolvedValueOnce(staleFrame)
      .mockResolvedValueOnce(committedFrame);
    const run = shots.runTriptychs({
      setContentSize: vi.fn(),
      webContents: { executeJavaScript, capturePage },
    } as never);

    try {
      await vi.waitFor(() => expect(executeJavaScript).toHaveBeenCalledTimes(2), { timeout: 200 });
      expect(capturePage).not.toHaveBeenCalled();
      confirmPaint();
      await run;
      expect(executeJavaScript).toHaveBeenCalledTimes(3);
      expect(capturePage).toHaveBeenCalledTimes(2);
      expect(await fsp.readFile(outputFile, 'utf8')).toBe('committed');
    } finally {
      confirmPaint();
      await run.catch(() => undefined);
      if (previous === undefined) delete process.env.PIXELFIT_TRIPTYCH_RECIPES;
      else process.env.PIXELFIT_TRIPTYCH_RECIPES = previous;
      await fsp.rm(root, { recursive: true, force: true });
    }
  });

  it('rejects malformed PIXELFIT_FIGURE_RECIPES before opening a figure window', async () => {
    const parseFigureRecipes = (shots as unknown as {
      parseFigureRecipes?: (raw: string | undefined) => unknown;
    }).parseFigureRecipes;
    expect(parseFigureRecipes).toBeTypeOf('function');
    expect(() => parseFigureRecipes?.('{not valid json')).toThrow(/PIXELFIT_FIGURE_RECIPES/i);

    const previous = process.env.PIXELFIT_FIGURE_RECIPES;
    process.env.PIXELFIT_FIGURE_RECIPES = '{not valid json';
    const executeJavaScript = vi.fn();
    try {
      await expect(shots.runFigures({ webContents: { executeJavaScript } } as never, 'unused'))
        .rejects.toThrow(/PIXELFIT_FIGURE_RECIPES/i);
      expect(executeJavaScript).not.toHaveBeenCalled();
    } finally {
      if (previous === undefined) delete process.env.PIXELFIT_FIGURE_RECIPES;
      else process.env.PIXELFIT_FIGURE_RECIPES = previous;
    }
  });

  it('normalizes figure recipe fields and rejects duplicate output inputs', () => {
    expect(shots.parseFigureRecipes('  [{"name":" top ","ids":[" top_011 "],"variants":[" on "]}]  '))
      .toEqual([{ name: 'top', ids: ['top_011'], variants: ['on'] }]);
    expect(() => shots.parseFigureRecipes('[{"name":"top","ids":["top_011","top_011"],"variants":["on"]}]'))
      .toThrow(/duplicate.*ID/i);
    expect(() => shots.parseFigureRecipes('[{"name":"top","ids":["top_011"],"variants":["on","on"]}]'))
      .toThrow(/duplicate.*variant/i);
  });

  it('rejects unknown requested scenes before any capture can start', () => {
    const selectShotScenes = (shots as unknown as {
      selectShotScenes?: (requested: string[] | undefined) => string[];
    }).selectShotScenes;
    expect(selectShotScenes).toBeTypeOf('function');
    expect(() => selectShotScenes?.(['main', 'does-not-exist'])).toThrow(/does-not-exist/);
  });

  it('throws when renderer readiness never arrives within the injected bound', async () => {
    const waitReady = (shots as unknown as {
      waitReady?: (
        win: { webContents: { executeJavaScript: (script: string) => Promise<unknown> } },
        options: { attempts: number; intervalMs: number },
      ) => Promise<void>;
    }).waitReady;
    expect(waitReady).toBeTypeOf('function');

    await expect(waitReady!(
      { webContents: { executeJavaScript: async () => null } },
      { attempts: 2, intervalMs: 0 },
    )).rejects.toThrow(/ready.*2/i);
  });

  it('turns structured failed checks into aggregate run failures', () => {
    const collectCheckFailures = (shots as unknown as {
      collectCheckFailures?: (scene: string, checks: Check[]) => string[];
    }).collectCheckFailures;
    const assertRunSucceeded = (shots as unknown as {
      assertRunSucceeded?: (kind: string, failures: string[]) => void;
    }).assertRunSucceeded;
    expect(collectCheckFailures).toBeTypeOf('function');
    expect(assertRunSucceeded).toBeTypeOf('function');

    const failures = collectCheckFailures?.('main', [
      { label: '衣橱可见', passed: true },
      { label: '素材已穿上', passed: false, detail: 'top 槽为空' },
    ]) ?? [];
    expect(failures).toEqual(['main：素材已穿上（top 槽为空）']);
    expect(() => assertRunSucceeded?.('shots', failures)).toThrow(/素材已穿上/);
  });

  it('rejects missing or malformed required figure image data', () => {
    const figurePng = (shots as unknown as {
      figurePng?: (dataUrl: string | null) => Buffer;
    }).figurePng;
    expect(figurePng).toBeTypeOf('function');
    expect(() => figurePng?.(null)).toThrow(/empty/i);
    expect(() => figurePng?.('data:image/png;base64,')).toThrow(/empty/i);
  });
});

describe('CERE-24 shot runtime invariants', () => {
  it('keeps deterministic allowlist order for requested scenes', () => {
    expect(shots.selectShotScenes('fit_shoes,fit_dress')).toEqual(['fit_dress', 'fit_shoes']);
  });

  it('rejects a recipe when any aliased asset is missing', () => {
    const available = [{ id: 'c10n_top_011' }] as Asset[];
    expect(() => resolveShotAssets(['top_014', 'bottom_102'], available))
      .toThrow(/Missing shot asset.*bottom_102.*c10n_bottom_102/);
  });

  it('attempts every requested scene and reports all capture failures', async () => {
    const log = vi.spyOn(console, 'log').mockImplementation(() => undefined);
    const error = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const attempted: string[] = [];
    let current = '';
    const executeJavaScript = vi.fn(async (script: string) => {
      if (script.includes('__pixelfitState')) return { ready: true, assets: 20 };
      if (script.startsWith('window.__pixelfitShot')) {
        current = script.includes('fit_dress') ? 'fit_dress' : 'fit_shoes';
        attempted.push(current);
        if (current === 'fit_dress') throw new Error('recipe failed');
        return undefined;
      }
      if (script.includes('activeRail') || script.includes('__pixelfitChecks')) return [];
      return undefined;
    });
    const win = {
      webContents: {
        executeJavaScript,
        capturePage: vi.fn(async () => {
          if (current === 'fit_shoes') throw new Error('capture failed');
          return { toPNG: () => Buffer.from('png') };
        }),
      },
    };
    const outDir = await fsp.mkdtemp(path.join(os.tmpdir(), 'pixelfit-shots-'));
    try {
      await expect(runShots(win as never, outDir, {
        requestedScenes: 'fit_dress,fit_shoes', readyAttempts: 1, readyDelayMs: 0, settleDelayMs: 0,
      })).rejects.toThrow(/recipe failed[\s\S]*capture failed/);
      expect(attempted).toEqual(['fit_dress', 'fit_shoes']);
    } finally {
      log.mockRestore();
      error.mockRestore();
      await fsp.rm(outDir, { recursive: true, force: true });
    }
  });
});
describe('default shot and figure recipe assets', () => {
  it('resolves every recipe key and alias target to one of the retained manifest assets', () => {
    const api = shots as unknown as {
      ASSET_ALIAS?: Record<string, string>;
      SHOT_RECIPES?: Record<string, readonly string[]>;
      FIGURE_RECIPES?: readonly {
        ids: readonly string[];
        tuck?: Readonly<Record<string, 'in' | 'out'>>;
      }[];
      resolveRecipeAssetId?: (id: string) => string;
    };
    expect(api.ASSET_ALIAS).toBeTypeOf('object');
    expect(api.SHOT_RECIPES).toBeTypeOf('object');
    expect(api.FIGURE_RECIPES).toBeTypeOf('object');
    expect(api.resolveRecipeAssetId).toBeTypeOf('function');

    const retained = new Set(manifest.items.map((item) => item.id));
    for (const target of Object.values(api.ASSET_ALIAS ?? {})) {
      expect(retained.has(target), `retired alias target: ${target}`).toBe(true);
    }

    const keys = [
      ...Object.values(api.SHOT_RECIPES ?? {}).flat(),
      ...(api.FIGURE_RECIPES ?? []).flatMap((recipe) => [
        ...recipe.ids,
        ...Object.keys(recipe.tuck ?? {}),
      ]),
    ];
    expect(keys.length).toBeGreaterThan(0);
    for (const key of keys) {
      const resolved = api.resolveRecipeAssetId?.(key) ?? '';
      expect(retained.has(resolved), `unresolved recipe key: ${key} -> ${resolved}`).toBe(true);
    }
  });
});
