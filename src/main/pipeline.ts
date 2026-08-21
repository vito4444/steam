/** CERE-12 local CPU pipeline bridge (UTF-8 JSON-lines over stdio). */
import { app } from 'electron';
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import type { PipelineMetadata } from './photo-import';

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

function modelsReady(runtime: Runtime): boolean {
  return (runtime.manifest.models ?? []).length > 0 && (runtime.manifest.models ?? []).every((model) => {
    try {
      return fs.statSync(path.join(runtime.dir, model.file)).size === model.bytes;
    } catch {
      return false;
    }
  });
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
        OMP_NUM_THREADS: String(Math.max(1, Math.min(os.cpus().length, 4))),
        U2NET_HOME: path.join(runtime.dir, 'models'),
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
    child.stdin.end(`${JSON.stringify(request)}\n`, 'utf8');
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
      message: '本地照片管线未安装；仍可手动导入透明底 PNG。',
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
        ? 'CERE-12 本地 CPU 管线已就绪：两级衣物识别、闭式抠图与 fail-closed 质量门均可用。'
        : 'CERE-12 质量门已就绪；自动识别模型缺失或尺寸不匹配，透明底 PNG 仍可本地检查。',
    };
  } catch (error) {
    return {
      installed: false,
      version: runtime.manifest.version ?? null,
      qualityGate: false,
      automatic: false,
      provider: null,
      message: `本地照片管线启动失败：${error instanceof Error ? error.message : String(error)}`,
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
    models_dir: path.join(runtime.dir, 'models'),
  }, 10 * 60_000);
}
