import fs from 'node:fs/promises';
import fss from 'node:fs';
import os from 'node:os';
import path from 'node:path';

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const userData = { dir: '' };

vi.mock('electron', () => ({
  app: { getPath: () => userData.dir, getVersion: () => '0.4.4' },
  shell: { openExternal: vi.fn(), showItemInFolder: vi.fn() },
}));

const { mirrorUrl, readModelLock, resolveModelsDir } = await import('../src/main/update/model-pack');
const { detectInstallKind, portableAssetUrl, releaseUrl, describeError } = await import(
  '../src/main/update/service'
);

const roots: string[] = [];

async function tempRoot(): Promise<string> {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'pixelfit-modelpack-'));
  roots.push(root);
  return root;
}

const LOCK = {
  models: [
    {
      route: 'birefnet-general-lite',
      runtime_filename: 'birefnet-general-lite.onnx',
      bytes: 8,
      md5: '0'.repeat(32),
      download_url: 'https://upstream.example/birefnet.onnx',
    },
    {
      route: 'u2net_cloth_seg',
      runtime_filename: 'u2net_cloth_seg.onnx',
      bytes: 4,
      md5: '1'.repeat(32),
      download_url: 'https://upstream.example/u2net.onnx',
    },
  ],
};

async function pipelineFixture(): Promise<string> {
  const root = await tempRoot();
  const pipeline = path.join(root, 'pipeline');
  await fs.mkdir(pipeline, { recursive: true });
  await fs.writeFile(path.join(pipeline, 'models.lock.json'), JSON.stringify(LOCK), 'utf8');
  return pipeline;
}

beforeEach(async () => {
  userData.dir = await tempRoot();
});

afterEach(async () => {
  await Promise.all(roots.splice(0).map((root) => fs.rm(root, { recursive: true, force: true })));
});

describe('model pack location', () => {
  it('reads the pinned model list', async () => {
    const pipeline = await pipelineFixture();
    expect(readModelLock(pipeline).map((m) => m.runtime_filename)).toEqual([
      'birefnet-general-lite.onnx',
      'u2net_cloth_seg.onnx',
    ]);
  });

  it('returns an empty list instead of throwing when the lock file is missing', async () => {
    const root = await tempRoot();
    expect(readModelLock(root)).toEqual([]);
  });

  /**
   * 0.4.3 及更早的安装包把模型带在 `pipeline/models` 里，开发机跑完
   * prepare-windows-pipeline.ps1 也是这个布局。这些情况**一个字节都不该下载**，
   * 否则拆包这件事就变成了给老用户凭空加 382 MB。
   */
  it('prefers the bundled models when they are complete', async () => {
    const pipeline = await pipelineFixture();
    const bundled = path.join(pipeline, 'models');
    await fs.mkdir(bundled, { recursive: true });
    await fs.writeFile(path.join(bundled, 'birefnet-general-lite.onnx'), Buffer.alloc(8));
    await fs.writeFile(path.join(bundled, 'u2net_cloth_seg.onnx'), Buffer.alloc(4));

    const resolved = resolveModelsDir(pipeline);
    expect(resolved.bundled).toBe(true);
    expect(resolved.ready).toBe(true);
    expect(resolved.dir).toBe(bundled);
  });

  it('falls back to the external pack when the bundled copy is incomplete', async () => {
    const pipeline = await pipelineFixture();
    const bundled = path.join(pipeline, 'models');
    await fs.mkdir(bundled, { recursive: true });
    // 只有一个模型，另一个缺 —— 半套模型不算就绪。
    await fs.writeFile(path.join(bundled, 'birefnet-general-lite.onnx'), Buffer.alloc(8));

    const resolved = resolveModelsDir(pipeline);
    expect(resolved.bundled).toBe(false);
    expect(resolved.ready).toBe(false);
    expect(resolved.dir).toBe(path.join(userData.dir, 'model-pack'));
  });

  /** 字节数不对的残缺文件必须被当作「没有」，不能拿去喂给 onnxruntime。 */
  it('treats a truncated model as missing', async () => {
    const pipeline = await pipelineFixture();
    const external = path.join(userData.dir, 'model-pack');
    await fs.mkdir(external, { recursive: true });
    await fs.writeFile(path.join(external, 'birefnet-general-lite.onnx'), Buffer.alloc(3));
    await fs.writeFile(path.join(external, 'u2net_cloth_seg.onnx'), Buffer.alloc(4));
    expect(resolveModelsDir(pipeline).ready).toBe(false);

    await fs.writeFile(path.join(external, 'birefnet-general-lite.onnx'), Buffer.alloc(8));
    expect(resolveModelsDir(pipeline).ready).toBe(true);
  });

  it('points the mirror at the model-pack tag, not at an app release', () => {
    expect(mirrorUrl('u2net_cloth_seg.onnx')).toBe(
      'https://github.com/vito4444/steam/releases/download/models-v1/u2net_cloth_seg.onnx',
    );
  });
});

describe('install kind', () => {
  it('detects the portable build from the launcher variable', () => {
    expect(detectInstallKind({ PORTABLE_EXECUTABLE_FILE: 'D:\\PixelFit.exe' }, true)).toBe('portable');
    expect(detectInstallKind({}, true)).toBe('nsis');
    expect(detectInstallKind({ PORTABLE_EXECUTABLE_FILE: 'D:\\PixelFit.exe' }, false)).toBe('dev');
  });

  /** 免安装版的下载地址必须和 electron-builder.yml 的 artifactName 对得上。 */
  it('builds the portable asset url from the published artifact name', () => {
    expect(portableAssetUrl('0.4.5')).toBe(
      'https://github.com/vito4444/steam/releases/download/v0.4.5/PixelFit-Portable-0.4.5-x64.exe',
    );
    expect(releaseUrl('0.4.5')).toBe('https://github.com/vito4444/steam/releases/tag/v0.4.5');
  });

  it('keeps the artifact name in sync with electron-builder.yml', () => {
    const yml = fss.readFileSync(
      path.join(__dirname, '..', 'electron-builder.yml'),
      'utf8',
    );
    expect(yml).toContain('${productName}-Portable-${version}-${arch}.${ext}');
    // 模型有意不进安装包；这一行回来了就说明拆包被改回去了。
    expect(yml).not.toMatch(/^\s*-\s*models\/\*\*\s*$/m);
    expect(yml).toContain('models.lock.json');
  });
});

describe('error messages', () => {
  it('turns network failures into something a user can act on', () => {
    expect(describeError(new Error('getaddrinfo ENOTFOUND github.com'), false)).toContain('检查网络');
    expect(describeError(new Error('HTTP 404: Not Found'), false)).toContain('latest.yml');
  });
});
