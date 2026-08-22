/**
 * CERE-59：把 382 MB 的 ONNX 权重从安装包里拆出来，做成按需下载的资源包。
 *
 * 为什么只拆模型、不把整个 pipeline 运行时也拆走：
 *  - 两个 .onnx 合计 382 MB，而且是**已压缩过的二进制**，NSIS 再压也压不动，
 *    它们几乎 1:1 地变成安装包体积——500 MB 里最大的一块就是它们。
 *  - 它们由 `models.lock.json` 钉死（字节数 + MD5），版本之间**根本不变**，
 *    每次随包重下一遍是纯浪费。
 *  - PyInstaller 运行时（约 308 MB 未压缩）会随 Python 依赖变，但变化很小，
 *    blockmap 差分下载对它很有效，留在安装包里反而更省事。
 *
 * 结果：常规版本更新只碰安装包，模型一辈子只下一次。
 */
import { app } from 'electron';
import crypto from 'node:crypto';
import fs from 'node:fs';
import fsp from 'node:fs/promises';
import path from 'node:path';

import type { ModelPackFile, ModelPackState } from '../../shared/update';

export interface LockedModel {
  route: string;
  runtime_filename: string;
  bytes: number;
  md5: string;
  /** 上游原始地址，作为镜像不可用时的回落 */
  download_url: string;
}

interface ModelLock {
  models?: LockedModel[];
}

/** 我们自己托管的模型镜像。和应用版本解耦，只有 models.lock.json 变了才发新 tag。 */
export const MODEL_PACK_TAG = 'models-v1';

export function mirrorUrl(filename: string): string {
  return `https://github.com/vito4444/steam/releases/download/${MODEL_PACK_TAG}/${filename}`;
}

export function readModelLock(pipelineDir: string): LockedModel[] {
  try {
    const raw = JSON.parse(
      fs.readFileSync(path.join(pipelineDir, 'models.lock.json'), 'utf8'),
    ) as ModelLock;
    return (raw.models ?? []).filter((model) => model.runtime_filename && model.bytes > 0);
  } catch {
    return [];
  }
}

/** 资源包的落盘位置：用户数据目录，安装目录通常没有写权限。 */
export function externalModelsDir(): string {
  return path.join(app.getPath('userData'), 'model-pack');
}

function complete(dir: string, models: LockedModel[]): boolean {
  if (models.length === 0) return false;
  return models.every((model) => {
    try {
      return fs.statSync(path.join(dir, model.runtime_filename)).size === model.bytes;
    } catch {
      return false;
    }
  });
}

/**
 * 决定这次运行用哪个目录里的模型。
 *
 * 先看安装包自带的 `pipeline/models`——0.4.3 及更早的布局、以及开发机上
 * 跑 `prepare-windows-pipeline.ps1` 之后都是这种情况，用户不需要下载任何东西。
 * 自带的不完整才回到外部资源包。
 */
export function resolveModelsDir(pipelineDir: string): { dir: string; bundled: boolean; ready: boolean } {
  const models = readModelLock(pipelineDir);
  const bundledDir = path.join(pipelineDir, 'models');
  if (complete(bundledDir, models)) return { dir: bundledDir, bundled: true, ready: true };
  const external = externalModelsDir();
  return { dir: external, bundled: false, ready: complete(external, models) };
}

async function md5(file: string): Promise<string> {
  const hash = crypto.createHash('md5');
  const stream = fs.createReadStream(file);
  for await (const chunk of stream) hash.update(chunk as Buffer);
  return hash.digest('hex');
}

export interface ModelPackEvents {
  onState(state: ModelPackState): void;
}

export class ModelPackService {
  private phase: ModelPackState['phase'] = 'unknown';
  private error: string | null = null;
  private downloadedBytes = 0;
  private busy = false;

  constructor(
    private readonly pipelineDir: () => string | null,
    private readonly events: ModelPackEvents,
  ) {}

  state(): ModelPackState {
    const dir = this.pipelineDir();
    if (!dir) {
      return {
        phase: 'error',
        dir: '',
        files: [],
        totalBytes: 0,
        downloadedBytes: 0,
        error: '本地识别运行时没有安装，模型资源包无处可用。',
        bundled: false,
      };
    }
    const models = readModelLock(dir);
    const resolved = resolveModelsDir(dir);
    const files: ModelPackFile[] = models.map((model) => ({
      name: model.runtime_filename,
      bytes: model.bytes,
      present: fileMatches(path.join(resolved.dir, model.runtime_filename), model.bytes),
    }));
    const phase = this.busy
      ? this.phase
      : resolved.ready
        ? 'ready'
        : this.phase === 'error'
          ? 'error'
          : 'missing';
    return {
      phase,
      dir: resolved.dir,
      files,
      totalBytes: models.reduce((sum, model) => sum + model.bytes, 0),
      downloadedBytes: this.busy
        ? this.downloadedBytes
        : files.reduce((sum, file) => sum + (file.present ? file.bytes : 0), 0),
      error: this.error,
      bundled: resolved.bundled,
    };
  }

  /**
   * 下载缺失的模型。
   *
   * 顺序：先试我们自己的 Release 镜像，失败再回上游地址。两个来源都用
   * `models.lock.json` 里钉死的字节数 + MD5 校验，校验不过就删掉重来，
   * 绝不把一个半截文件留在磁盘上当成「已就绪」。
   */
  async download(): Promise<ModelPackState> {
    if (this.busy) return this.state();
    const pipelineDir = this.pipelineDir();
    if (!pipelineDir) return this.state();
    const models = readModelLock(pipelineDir);
    if (models.length === 0) {
      this.error = 'models.lock.json 缺失或为空，无法确认要下载哪些模型。';
      this.phase = 'error';
      this.events.onState(this.state());
      return this.state();
    }

    this.busy = true;
    this.error = null;
    this.phase = 'downloading';
    const target = externalModelsDir();
    await fsp.mkdir(target, { recursive: true });
    const already = models
      .filter((model) => fileMatches(path.join(target, model.runtime_filename), model.bytes))
      .reduce((sum, model) => sum + model.bytes, 0);
    this.downloadedBytes = already;
    this.events.onState(this.state());

    try {
      for (const model of models) {
        const destination = path.join(target, model.runtime_filename);
        if (fileMatches(destination, model.bytes)) continue;
        await this.fetchOne(model, destination);
        this.phase = 'verifying';
        this.events.onState(this.state());
        const digest = await md5(destination);
        if (digest !== model.md5.toLowerCase()) {
          await fsp.rm(destination, { force: true });
          throw new Error(`${model.runtime_filename} 校验不通过（MD5 ${digest}），已删除，请重试。`);
        }
        this.phase = 'downloading';
      }
      this.phase = 'ready';
    } catch (err) {
      this.phase = 'error';
      this.error = err instanceof Error ? err.message : String(err);
    } finally {
      this.busy = false;
    }
    const next = this.state();
    this.events.onState(next);
    return next;
  }

  private async fetchOne(model: LockedModel, destination: string): Promise<void> {
    const sources = [mirrorUrl(model.runtime_filename), model.download_url];
    let lastError: unknown = null;
    for (const source of sources) {
      try {
        await this.stream(source, destination, model);
        return;
      } catch (err) {
        lastError = err;
        await fsp.rm(`${destination}.part`, { force: true });
      }
    }
    throw new Error(
      `下载 ${model.runtime_filename} 失败：${lastError instanceof Error ? lastError.message : String(lastError)}`,
    );
  }

  private async stream(url: string, destination: string, model: LockedModel): Promise<void> {
    const response = await fetch(url);
    if (!response.ok || !response.body) throw new Error(`HTTP ${response.status} @ ${url}`);
    const temporary = `${destination}.part`;
    const handle = await fsp.open(temporary, 'w');
    const base = this.downloadedBytes;
    let written = 0;
    try {
      for await (const chunk of response.body as unknown as AsyncIterable<Uint8Array>) {
        await handle.write(chunk);
        written += chunk.byteLength;
        this.downloadedBytes = base + written;
        this.events.onState(this.state());
      }
    } finally {
      await handle.close();
    }
    if (written !== model.bytes) {
      await fsp.rm(temporary, { force: true });
      throw new Error(`字节数不符：期望 ${model.bytes}，实际 ${written}`);
    }
    await fsp.rename(temporary, destination);
  }
}

function fileMatches(file: string, bytes: number): boolean {
  try {
    return fs.statSync(file).size === bytes;
  } catch {
    return false;
  }
}
