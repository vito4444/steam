/**
 * CERE-28：把「上传模特照片」整条链路真的跑一遍。
 *
 *   npx vite-node --script scripts/cere28-base-import.ts <photo> <library-root>
 *
 * 走的是应用里那一份 `importBasePhoto`，只把两个外部依赖换成脚本能用的实现：
 *   - 抠图：直接 spawn 打包好的管线 exe（和应用一模一样的命令）；
 *   - 读 alpha：用 scripts/png.ts，因为 Electron 的 nativeImage 只在应用里有。
 *
 * 跑完 `<library-root>/base/<体型>/` 下就是一个真的底图包，应用启动即接管。
 */

import { spawn } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';

import { PNG } from './png';
import { importBasePhoto } from '../src/main/base-import';
import type { BodyType } from '../src/shared/spec';

const runtimeDir = path.resolve('pipeline/runtime/pixelfit-pipeline');
const modelsDir = path.resolve('pipeline/models');

function callPipeline<T>(request: Record<string, unknown>): Promise<T> {
  return new Promise((resolve, reject) => {
    const child = spawn(path.join(runtimeDir, 'pixelfit-pipeline.exe'), [], {
      cwd: runtimeDir,
      windowsHide: true,
      env: { ...process.env, U2NET_HOME: modelsDir },
    });
    let out = '';
    let err = '';
    child.stdout.setEncoding('utf8');
    child.stderr.setEncoding('utf8');
    child.stdout.on('data', (d: string) => { out += d; });
    child.stderr.on('data', (d: string) => { err += d; });
    child.on('close', () => {
      const line = out.split(/\r?\n/).find((c) => c.trim().startsWith('{'));
      if (!line) { reject(new Error(`no JSON response; ${err.slice(0, 300)}`)); return; }
      const parsed = JSON.parse(line) as { ok: boolean; result?: T; error?: { code: string; message: string } };
      if (!parsed.ok || parsed.result === undefined) {
        reject(new Error(`${parsed.error?.code}: ${parsed.error?.message}`));
        return;
      }
      resolve(parsed.result);
    });
    // 和应用一样：非 ASCII 一律转义，不依赖子进程的代码页。
    const escaped = JSON.stringify(request).replace(
      /[^\x20-\x7e]/g,
      (ch) => '\\u' + ch.charCodeAt(0).toString(16).padStart(4, '0'),
    );
    child.stdin.end(`${escaped}\n`, 'ascii');
  });
}

async function main(): Promise<void> {
  const args = process.argv.slice(2).filter((value) => value !== '--');
  const [photo, libraryRoot, bodyArg] = args;
  if (!photo || !libraryRoot) throw new Error('usage: cere28-base-import <photo> <library-root> [body]');
  const body = (bodyArg ?? 'base_f02') as BodyType;

  const tmpDir = path.resolve('.enctest/base-staging');
  fs.mkdirSync(tmpDir, { recursive: true });

  const result = await importBasePhoto(body, path.resolve(photo), {
    cutoutSubject: (imagePath, destination) => callPipeline({
      command: 'cutout-subject',
      image_path: imagePath,
      destination,
      models_dir: modelsDir,
    }),
    readSilhouette: (file) => {
      const png = PNG.read(fs.readFileSync(file));
      const alpha = new Uint8Array(png.width * png.height);
      for (let i = 0; i < alpha.length; i++) alpha[i] = png.data[i * 4 + 3];
      return { width: png.width, height: png.height, alpha };
    },
    baseLibraryDir: path.resolve(libraryRoot, 'base'),
    tmpDir,
  });

  console.log('pack       :', result.pack);
  console.log('canvas     :', `${result.canvas.w} x ${result.canvas.h}`);
  console.log('fallback   :', result.fallbackAnchors);
  console.log('notes      :', result.notes.length ? result.notes.join(' / ') : '(none)');
  const written = path.resolve(libraryRoot, 'base', body);
  console.log('written to :', written);
  console.log('files      :', fs.readdirSync(written).join(', '));
}

void main();
