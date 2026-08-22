/**
 * CERE-64：模型缺失时导入流程该怎么走。
 *
 * 这一版的回归点很具体 —— 成员反馈「识别不到，无法自动抠图」，根因是
 * 0.4.4 把模型拆出安装包后，唯一的下载入口藏在设置页里。所以这里钉死的
 * 是「缺模型不等于按钮变灰」，以及残缺文件必须被自动清掉。
 */
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';

import { afterEach, describe, expect, it, vi } from 'vitest';

import type { PipelineStatus } from '../src/shared/ipc';
import type { ModelPackState } from '../src/shared/update';
import { MODEL_PACK_HINT } from '../src/shared/update';

const userData = { dir: '' };

vi.mock('electron', () => ({
  app: { getPath: () => userData.dir, getVersion: () => '0.4.9' },
  shell: { openExternal: vi.fn(), showItemInFolder: vi.fn() },
}));

const { classifyFailure, sweepStale } = await import('../src/main/update/model-pack');
const { etaText, gateDecision, modelStatus, percentOf, speedText } = await import(
  '../src/renderer/src/features/import/model-gate'
);

const roots: string[] = [];
afterEach(async () => {
  await Promise.all(roots.splice(0).map((root) => fs.rm(root, { recursive: true, force: true })));
});

async function tempRoot(): Promise<string> {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'pixelfit-gate-'));
  roots.push(root);
  return root;
}

const pipeline = (patch: Partial<PipelineStatus> = {}): PipelineStatus => ({
  installed: true,
  version: '1.0.0',
  message: '',
  qualityGate: true,
  automatic: false,
  provider: 'CPUExecutionProvider',
  ...patch,
});

const pack = (patch: Partial<ModelPackState> = {}): ModelPackState => ({
  phase: 'missing',
  dir: 'C:\\Users\\x\\AppData\\Roaming\\PixelFit\\model-pack',
  files: [],
  totalBytes: 400_199_653,
  downloadedBytes: 0,
  error: null,
  failure: null,
  bytesPerSecond: null,
  etaSeconds: null,
  swept: [],
  bundled: false,
  ...patch,
});

describe('import gate', () => {
  it('runs straight through once the models are on disk', () => {
    expect(gateDecision(pipeline({ automatic: true }), pack({ phase: 'ready' }))).toBe('run');
  });

  /**
   * 这条是这个 issue 的全部意义：模型缺失时导入按钮**不能**是灰的。
   * 灰按钮只是把失败提前，用户仍然不知道该做什么 —— 0.4.4 到 0.4.8 就是这样。
   */
  it('gates instead of disabling when only the models are missing', () => {
    expect(gateDecision(pipeline(), pack())).toBe('gate');
    expect(gateDecision(pipeline(), null)).toBe('gate');
  });

  /** 运行时本身没装的时候下模型也没用，别给假希望。 */
  it('reports unavailable when the local runtime is not installed at all', () => {
    expect(gateDecision(pipeline({ installed: false }), pack())).toBe('unavailable');
    expect(gateDecision(null, pack())).toBe('unavailable');
  });

  /** 老布局（模型仍打在安装包里）不该被这套逻辑拖去下载。 */
  it('still gates on a bundled pack only when the pipeline says automatic is off', () => {
    expect(gateDecision(pipeline({ automatic: true }), pack({ bundled: true }))).toBe('run');
  });
});

describe('model status badge', () => {
  it('says ready when automatic cutout works', () => {
    expect(modelStatus(pipeline({ automatic: true }), pack({ phase: 'ready' })).tone).toBe('ok');
  });

  it('turns "missing" into a next step, not a fault report', () => {
    const view = modelStatus(pipeline(), pack());
    expect(view.tone).toBe('warn');
    expect(view.label).toContain('未下载');
    expect(view.detail).toContain('自动继续');
  });

  it('shows live progress while downloading', () => {
    const view = modelStatus(
      pipeline(),
      pack({ phase: 'downloading', downloadedBytes: 200_099_826 }),
    );
    expect(view.tone).toBe('busy');
    expect(view.label).toContain('50%');
  });

  it('keeps the runtime failure visible instead of blaming the model pack', () => {
    const view = modelStatus(pipeline({ installed: false, message: '本地识别启动失败：X' }), pack());
    expect(view.tone).toBe('error');
    expect(view.detail).toContain('本地识别启动失败');
  });
});

describe('progress wording', () => {
  it('never invents a remaining time it does not have', () => {
    expect(etaText(null)).toBe('');
    expect(etaText(0)).toBe('');
    expect(speedText(0)).toBe('');
  });

  it('reads in the unit that matches the magnitude', () => {
    expect(etaText(45)).toBe('约还需 45 秒');
    expect(etaText(150)).toBe('约还需 3 分钟');
    expect(etaText(3_900)).toBe('约还需 1 小时 5 分钟');
    expect(speedText(3 * 1024 * 1024)).toBe('3.0 MB/s');
  });

  it('clamps the percentage instead of overflowing the bar', () => {
    expect(percentOf(pack({ downloadedBytes: 999_999_999 }))).toBe(100);
    expect(percentOf(pack({ totalBytes: 0 }))).toBe(0);
    expect(percentOf(null)).toBe(0);
  });
});

describe('failure classification', () => {
  /** 三类失败对应三种完全不同的下一步，混成一句「下载失败」等于没说。 */
  it('separates network, disk, checksum and cancel', () => {
    expect(classifyFailure(new Error('getaddrinfo ENOTFOUND github.com'))).toBe('network');
    expect(classifyFailure(new Error('HTTP 403 @ https://github.com/...'))).toBe('network');
    expect(classifyFailure(Object.assign(new Error('write failed'), { code: 'ENOSPC' }))).toBe('disk');
    expect(classifyFailure(new Error('u2net_cloth_seg.onnx 校验不通过（MD5 abc），已删除，请重试。')))
      .toBe('checksum');
    expect(classifyFailure(new Error('字节数不符：期望 8，实际 3'))).toBe('checksum');
    expect(classifyFailure(Object.assign(new Error('The operation was aborted'), { name: 'AbortError' })))
      .toBe('canceled');
  });

  /**
   * 取消要排在网络之前：abort 掉的 fetch 报的是 AbortError，
   * 按关键字容易被归成网络故障 —— 但用户自己点的取消不是故障。
   */
  it('does not call a user cancel a network problem', () => {
    const aborted = Object.assign(new Error('fetch failed'), { name: 'AbortError' });
    expect(classifyFailure(aborted)).toBe('canceled');
  });

  it('gives every class an actionable hint', () => {
    expect(MODEL_PACK_HINT.network).toContain('网络');
    expect(MODEL_PACK_HINT.disk).toContain('400 MB');
    expect(MODEL_PACK_HINT.checksum).toContain('不会留下半个文件');
    expect(MODEL_PACK_HINT.canceled).toContain('保留');
  });
});

describe('stale file sweep', () => {
  const models = [
    { route: 'a', runtime_filename: 'a.onnx', bytes: 8, md5: '0'.repeat(32), download_url: 'https://x/a' },
    { route: 'b', runtime_filename: 'b.onnx', bytes: 4, md5: '1'.repeat(32), download_url: 'https://x/b' },
  ];

  /**
   * 成员那台机器上很可能就是这个状态：下到一半失败，`.part` 留在盘上，
   * 之后每次都从这个坏状态出发。清理必须是自动的，用户不该知道 .part 是什么。
   */
  it('removes half-downloaded parts and truncated models', async () => {
    const dir = await tempRoot();
    await fs.writeFile(path.join(dir, 'a.onnx.part'), Buffer.alloc(1_024));
    await fs.writeFile(path.join(dir, 'b.onnx.download'), Buffer.alloc(16));
    await fs.writeFile(path.join(dir, 'a.onnx'), Buffer.alloc(3));

    const removed = await sweepStale(dir, models);
    expect(removed.sort()).toEqual(['a.onnx', 'a.onnx.part', 'b.onnx.download']);
    expect(await fs.readdir(dir)).toEqual([]);
  });

  /** 已经下好的文件一个字节都不能碰 —— 否则清理本身就成了新的 382 MB。 */
  it('leaves complete models and unrelated files alone', async () => {
    const dir = await tempRoot();
    await fs.writeFile(path.join(dir, 'a.onnx'), Buffer.alloc(8));
    await fs.writeFile(path.join(dir, 'b.onnx'), Buffer.alloc(4));
    await fs.writeFile(path.join(dir, 'notes.txt'), 'keep me');

    expect(await sweepStale(dir, models)).toEqual([]);
    expect((await fs.readdir(dir)).sort()).toEqual(['a.onnx', 'b.onnx', 'notes.txt']);
  });

  it('does not throw when the pack directory has never been created', async () => {
    const dir = await tempRoot();
    await expect(sweepStale(path.join(dir, 'nope'), models)).resolves.toEqual([]);
  });
});

// ------------------------------------------------------------ 下载服务本身

const { ModelPackService } = await import('../src/main/update/model-pack');

/** 把一段字节做成 fetch 能消费的响应体，用来在测试里跑真的 stream 分支。 */
function bodyOf(chunks: Uint8Array[]): AsyncIterable<Uint8Array> {
  return {
    async *[Symbol.asyncIterator]() {
      for (const chunk of chunks) {
        await new Promise((resolve) => setTimeout(resolve, 1));
        yield chunk;
      }
    },
  };
}

async function serviceFixture(models: unknown[]): Promise<string> {
  userData.dir = await tempRoot();
  const pipelineDir = path.join(await tempRoot(), 'pipeline');
  await fs.mkdir(pipelineDir, { recursive: true });
  await fs.writeFile(path.join(pipelineDir, 'models.lock.json'), JSON.stringify({ models }), 'utf8');
  return pipelineDir;
}

describe('ModelPackService', () => {
  const ONE = {
    route: 'a',
    runtime_filename: 'a.onnx',
    bytes: 6,
    // md5('abcdef')
    md5: 'e80b5017098950fc58aad83c8c14978e',
    download_url: 'https://upstream.example/a.onnx',
  };

  /** 下完要能真的就绪，而且校验走的是 lock 文件里钉死的 MD5，不是「大小差不多」。 */
  it('downloads, verifies and reports ready', async () => {
    const pipelineDir = await serviceFixture([ONE]);
    vi.stubGlobal('fetch', async () => ({
      ok: true,
      status: 200,
      body: bodyOf([new Uint8Array([0x61, 0x62, 0x63]), new Uint8Array([0x64, 0x65, 0x66])]),
    }));
    const seen: string[] = [];
    const service = new ModelPackService(() => pipelineDir, {
      onState: (state) => seen.push(state.phase),
    });
    const state = await service.download();
    expect(state.phase).toBe('ready');
    expect(state.failure).toBeNull();
    expect(seen).toContain('verifying');
    vi.unstubAllGlobals();
  });

  /**
   * 校验不过时**不能**把半个文件留在盘上 —— 留下来的话，之后每次
   * `resolveModelsDir` 都看到一个字节数对不上的文件，用户永远修不好。
   */
  it('leaves nothing behind when the checksum does not match', async () => {
    const pipelineDir = await serviceFixture([{ ...ONE, md5: 'f'.repeat(32) }]);
    vi.stubGlobal('fetch', async () => ({
      ok: true,
      status: 200,
      body: bodyOf([new Uint8Array([0x61, 0x62, 0x63, 0x64, 0x65, 0x66])]),
    }));
    const service = new ModelPackService(() => pipelineDir, { onState: () => {} });
    const state = await service.download();
    expect(state.phase).toBe('error');
    expect(state.failure).toBe('checksum');
    expect(await fs.readdir(state.dir)).toEqual([]);
    vi.unstubAllGlobals();
  });

  /** 取消是用户的决定，不是故障：不留 `.part`，也不报成下载失败。 */
  it('treats a user cancel as a cancel, not an error', async () => {
    const pipelineDir = await serviceFixture([{ ...ONE, bytes: 6_000 }]);
    vi.stubGlobal('fetch', async (_url: string, init?: { signal?: AbortSignal }) => {
      if (init?.signal?.aborted) throw Object.assign(new Error('aborted'), { name: 'AbortError' });
      return {
        ok: true,
        status: 200,
        body: bodyOf(Array.from({ length: 60 }, () => new Uint8Array(100))),
      };
    });
    const service = new ModelPackService(() => pipelineDir, { onState: () => {} });
    const running = service.download();
    await new Promise((resolve) => setTimeout(resolve, 15));
    service.cancel();
    const state = await running;
    expect(state.failure).toBe('canceled');
    expect(state.phase).toBe('missing');
    expect(state.error).toBeNull();
    expect((await fs.readdir(state.dir)).filter((f) => f.endsWith('.part'))).toEqual([]);
    vi.unstubAllGlobals();
  });

  /** 启动时的清理是自动的，并且如实报出清掉了什么。 */
  it('sweeps stale parts on init and reports them', async () => {
    const pipelineDir = await serviceFixture([ONE]);
    const external = path.join(userData.dir, 'model-pack');
    await fs.mkdir(external, { recursive: true });
    await fs.writeFile(path.join(external, 'a.onnx.part'), Buffer.alloc(3));

    const service = new ModelPackService(() => pipelineDir, { onState: () => {} });
    await service.init();
    expect(service.state().swept).toEqual(['a.onnx.part']);
    expect(await fs.readdir(external)).toEqual([]);
  });
});
