/** CERE-12 local CPU pipeline bridge (UTF-8 JSON-lines over stdio). */
import { app } from 'electron';
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import type { PipelineMetadata } from './photo-import';
import { resolveModelsDir } from './update/model-pack';

export interface PipelineStatus {
  installed: boolean;
  version: string | null;
  message: string;
  qualityGate: boolean;
  automatic: boolean;
  provider: 'CPUExecutionProvider' | null;
}

interface PipelineManifest {
  version?: string;
  entry: string;
  args?: string[];
  models?: Array<{ file: string; bytes: number }>;
}

interface Runtime {
  dir: string;
  manifest: PipelineManifest;
  entry: string;
}

interface PipelineEnvelope<T> {
  ok: boolean;
  result?: T;
  error?: { code?: string; message?: string; details?: unknown };
}

function candidateDirs(): string[] {
  return [
    process.env['PIXELFIT_PIPELINE'] ?? '',
    path.join(process.resourcesPath ?? '', 'pipeline'),
    path.join(app.getAppPath(), 'pipeline'),
    path.join(app.getAppPath(), '..', 'pipeline'),
  ].filter(Boolean);
}

function resolveRuntime(): Runtime | null {
  for (const dir of candidateDirs()) {
    const manifestPath = path.join(dir, 'manifest.json');
    try {
      const manifest = JSON.parse(fs.readFileSync(manifestPath, 'utf8')) as PipelineManifest;
      if (!manifest.entry) continue;
      const entry = path.resolve(dir, manifest.entry);
      const relative = path.relative(path.resolve(dir), entry);
      if (relative.startsWith('..') || path.isAbsolute(relative) || !fs.existsSync(entry)) continue;
      return { dir, manifest, entry };
    } catch {
      // Try the next packaged/development candidate.
    }
  }
  return null;
}

/**
 * CERE-59：模型不再一定躺在 `runtime.dir/models` 里。
 *
 * 拆包之后，安装包只带 `models.lock.json`，权重在用户数据目录的资源包中。
 * `resolveModelsDir` 两个位置都认——旧布局（自带）优先，缺了才看资源包，
 * 所以 0.4.3 装出来的目录结构和开发机上的 `prepare-windows-pipeline.ps1`
 * 都照常工作。Python 侧完全不用改：`models_dir` 本来就是请求参数。
 */
export function modelsDirFor(runtime: Runtime): string {
  return resolveModelsDir(runtime.dir).dir;
}

/** 供 IPC 层查询模型资源包状态用；没装运行时就没有 pipeline 目录。 */
export function pipelineDir(): string | null {
  return resolveRuntime()?.dir ?? null;
}

function modelsReady(runtime: Runtime): boolean {
  const dir = modelsDirFor(runtime);
  return (runtime.manifest.models ?? []).length > 0 && (runtime.manifest.models ?? []).every((model) => {
    try {
      // manifest 里写的是 `models/xxx.onnx`，解析后的目录本身就是 models 目录。
      return fs.statSync(path.join(dir, path.basename(model.file))).size === model.bytes;
    } catch {
      return false;
    }
  });
}

/**
 * 把请求编成**纯 ASCII** 的 JSON。
 *
 * CERE-28：打包后的管线用 ANSI 代码页解 stdin，不是 UTF-8 —— PyInstaller 的
 * bootloader 先把解释器配置好了，`PYTHONUTF8` / `PYTHONIOENCODING` 都改不动它
 * （两个都试过，无效）。于是任何带非 ASCII 字符的路径都会被解错，比如 Windows
 * 中文版默认的截图文件名 `屏幕截图 2026-07-30 005149.png`，管线拿到的是乱码
 * 路径，报 `ASSET_NOT_FOUND: source image was not found`。
 *
 * 更阴的是错误信息里回显的路径**看着是对的**：错解一次、再错编一次，字节又变
 * 回原样，所以这个 bug 一直没被发现。
 *
 * 把非 ASCII 字符全部转成 `\uXXXX` 之后，请求在任何代码页下都解成同一个字符
 * 串，不依赖运行时是哪一版构建 —— 旧运行时也一样能用。
 */
export function encodePipelineRequest(request: Record<string, unknown>): string {
  const escaped = JSON.stringify(request).replace(
    /[^\x20-\x7e]/g,
    (char) => '\\u' + char.charCodeAt(0).toString(16).padStart(4, '0'),
  );
  return `${escaped}\n`;
}

export async function runPipeline<T>(request: Record<string, unknown>, timeoutMs = 30_000): Promise<T> {
  const runtime = resolveRuntime();
  if (!runtime) throw new Error('PIPELINE_NOT_INSTALLED: CERE-12 runtime executable was not found');
  return new Promise<T>((resolve, reject) => {
    const child = spawn(runtime.entry, runtime.manifest.args ?? [], {
      cwd: runtime.dir,
      windowsHide: true,
      stdio: ['pipe', 'pipe', 'pipe'],
      env: {
        ...process.env,
        CUDA_VISIBLE_DEVICES: '',
        // 打包运行时会忽略这两个，但开发时直接跑 python 的话它们有用。
        PYTHONUTF8: '1',
        PYTHONIOENCODING: 'utf-8',
        OMP_NUM_THREADS: String(Math.max(1, Math.min(os.cpus().length, 4))),
        U2NET_HOME: modelsDirFor(runtime),
      },
    });
    let stdout = '';
    let stderr = '';
    let settled = false;
    const timer = setTimeout(() => {
      child.kill();
      if (!settled) reject(new Error(`PIPELINE_TIMEOUT: command exceeded ${timeoutMs}ms`));
      settled = true;
    }, timeoutMs);
    child.stdout.setEncoding('utf8');
    child.stderr.setEncoding('utf8');
    child.stdout.on('data', (chunk: string) => { stdout += chunk; });
    child.stderr.on('data', (chunk: string) => { stderr += chunk; });
    child.on('error', (error) => {
      clearTimeout(timer);
      if (!settled) reject(error);
      settled = true;
    });
    child.on('close', (code) => {
      clearTimeout(timer);
      if (settled) return;
      settled = true;
      const line = stdout.split(/\r?\n/).find((candidate) => candidate.trim().startsWith('{'));
      if (!line) {
        reject(new Error(`PIPELINE_TRANSPORT: exit ${code ?? 'unknown'}; ${stderr.trim() || 'no JSON response'}`));
        return;
      }
      try {
        const response = JSON.parse(line) as PipelineEnvelope<T>;
        if (!response.ok || response.result === undefined) {
          const detail = response.error?.message ?? 'pipeline command failed';
          reject(new Error(`${response.error?.code ?? 'PIPELINE_ERROR'}: ${detail}`));
          return;
        }
        resolve(response.result);
      } catch (error) {
        reject(new Error(`PIPELINE_TRANSPORT: invalid JSON response (${String(error)})`));
      }
    });
    child.stdin.end(encodePipelineRequest(request), 'ascii');
  });
}

export async function pipelineStatus(): Promise<PipelineStatus> {
  const runtime = resolveRuntime();
  if (!runtime) {
    return {
      installed: false,
      version: null,
      qualityGate: false,
      automatic: false,
      provider: null,
      message: '本地识别没有安装，暂时只能导入已经抠好的透明底图片。',
    };
  }
  try {
    await runPipeline<{ service: string; schema_version: string }>({ command: 'ping' });
    const automatic = modelsReady(runtime);
    return {
      installed: true,
      version: runtime.manifest.version ?? 'unknown',
      qualityGate: true,
      automatic,
      provider: 'CPUExecutionProvider',
      message: automatic
        ? '已就绪：选图后在本机识别衣物、自动抠图，不上传、不联网。'
        : '识别模型还没下载（约 382 MB，只需下载一次）。在设置页点「下载识别模型」即可开启自动抠图；已经抠好的透明底图片现在也能导入。',
    };
  } catch (error) {
    return {
      installed: false,
      version: runtime.manifest.version ?? null,
      qualityGate: false,
      automatic: false,
      provider: null,
      message: `本地识别启动失败：${error instanceof Error ? error.message : String(error)}`,
    };
  }
}

export async function analyzePhoto(
  imagePath: string,
  outputDir: string,
  importId: string,
): Promise<PipelineMetadata> {
  const runtime = resolveRuntime();
  if (!runtime || !modelsReady(runtime)) throw new Error('MODEL_MISSING: 离线自动识别模型不可用');
  return runPipeline<PipelineMetadata>({
    command: 'analyze',
    image_path: imagePath,
    import_id: importId,
    output_dir: outputDir,
    models_dir: modelsDirFor(runtime),
  }, 10 * 60_000);
}

export interface SubjectCutoutResult {
  file: string;
  width: number;
  height: number;
  visible_pixels: number;
  visible_ratio: number;
}

/** 把一张照片里的人抠出来，写成透明底 PNG（CERE-28 的模特底图入口）。 */
export async function cutoutSubject(imagePath: string, destination: string): Promise<SubjectCutoutResult> {
  const runtime = resolveRuntime();
  if (!runtime || !modelsReady(runtime)) throw new Error('MODEL_MISSING: 离线抠图模型不可用');
  return runPipeline<SubjectCutoutResult>({
    command: 'cutout-subject',
    image_path: imagePath,
    destination,
    models_dir: modelsDirFor(runtime),
  }, 10 * 60_000);
}
