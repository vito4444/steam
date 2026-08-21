import { execFile } from 'node:child_process';
import crypto from 'node:crypto';
import fsp from 'node:fs/promises';
import path from 'node:path';
import { promisify } from 'node:util';

import { parseEvidenceCases, type EvidenceCase } from './cere26-ai-evidence';

const executeFile = promisify(execFile);

export interface TriptychRecipe {
  id: string;
  baseImage: string;
  localImage: string;
  aliyunImage: string;
  outputFile: string;
}

export function electronTriptychArguments(ingest: string): string[] {
  return ['--disable-gpu', '.', '--ingest', ingest, '--cere26-triptychs'];
}

interface EvidenceResult {
  status: unknown;
  id: unknown;
  input: unknown;
  output: unknown;
}

/** Uses the shared paid-evidence parser so triptych recipes cannot drift from billed cases. */
export function parseTriptychRecipes(raw: string, evidenceDir: string): TriptychRecipe[] {
  const cases = parseEvidenceCases(raw);
  return cases.map((item) => recipeFor(item, evidenceDir, path.join(evidenceDir, 'aliyun', `${item.id}.png`)));
}

/**
 * Verifies the exact succeeded accounting rows and bytes before Electron receives any image path.
 * This binds the visual evidence to results.json instead of guessed provider filenames.
 */
export async function validateTriptychEvidence(
  manifestRaw: string,
  resultsRaw: string,
  manifestDir: string,
  evidenceDir: string,
): Promise<TriptychRecipe[]> {
  const cases = parseEvidenceCases(manifestRaw);
  const rows = parseSucceededResults(resultsRaw, cases);
  const recipes: TriptychRecipe[] = [];
  for (let index = 0; index < cases.length; index++) {
    const item = cases[index];
    const row = rows[index];
    const input = parseInput(row.input, item.id);
    await assertHash(path.resolve(manifestDir, item.person), input.personSha256, `CERE-26 ${item.id} base image`);
    if (input.garments.length !== item.garments.length) throw new Error(`CERE-26 ${item.id} results input garments do not match the manifest`);
    for (let garmentIndex = 0; garmentIndex < item.garments.length; garmentIndex++) {
      const expected = item.garments[garmentIndex];
      const actual = input.garments[garmentIndex];
      if (actual.id !== expected.id || actual.category !== expected.category) {
        throw new Error(`CERE-26 ${item.id} results input garment order does not match the manifest`);
      }
      await assertHash(path.resolve(manifestDir, expected.image), actual.sha256, `CERE-26 ${item.id} garment ${expected.id}`);
    }
    const output = parseOutput(row.output, item.id);
    const aliyunImage = exactAliyunOutputPath(evidenceDir, item.id, output.file);
    await assertHash(aliyunImage, output.sha256, `CERE-26 ${item.id} Aliyun output`);
    const recipe = recipeFor(item, evidenceDir, aliyunImage);
    recipe.baseImage = path.resolve(manifestDir, item.person);
    await assertNonEmptyFile(recipe.localImage);
    recipes.push(recipe);
  }
  return recipes;
}

function recipeFor(item: EvidenceCase, evidenceDir: string, aliyunImage: string): TriptychRecipe {
  return {
    id: item.id,
    baseImage: item.person,
    localImage: path.join(evidenceDir, 'local', `${item.id}-on.png`),
    aliyunImage,
    outputFile: path.join(evidenceDir, 'triptychs', `${item.id}.png`),
  };
}

function parseSucceededResults(raw: string, cases: EvidenceCase[]): EvidenceResult[] {
  let parsed: unknown;
  try { parsed = JSON.parse(raw); } catch { throw new Error('CERE-26 results.json must be valid JSON'); }
  const rows = parsed && typeof parsed === 'object' ? (parsed as { results?: unknown }).results : undefined;
  if (!Array.isArray(rows) || rows.length !== cases.length) throw new Error('CERE-26 results.json must contain exactly three succeeded rows');
  return rows.map((row, index) => {
    if (!row || typeof row !== 'object') throw new Error('CERE-26 results.json contains an invalid row');
    const result = row as EvidenceResult;
    if (result.status !== 'succeeded' || result.id !== cases[index].id) {
      throw new Error('CERE-26 results.json must contain exactly ordered top, separates, dress succeeded rows');
    }
    return result;
  });
}

function parseInput(value: unknown, id: string): { personSha256: string; garments: Array<{ id: string; category: string; sha256: string }> } {
  if (!value || typeof value !== 'object') throw new Error(`CERE-26 ${id} results input is invalid`);
  const input = value as { personSha256?: unknown; garments?: unknown };
  if (!isSha256(input.personSha256) || !Array.isArray(input.garments)) throw new Error(`CERE-26 ${id} results input is invalid`);
  const garments = input.garments.map((garment) => {
    if (!garment || typeof garment !== 'object') throw new Error(`CERE-26 ${id} results garment is invalid`);
    const parsed = garment as { id?: unknown; category?: unknown; sha256?: unknown };
    if (typeof parsed.id !== 'string' || typeof parsed.category !== 'string' || !isSha256(parsed.sha256)) throw new Error(`CERE-26 ${id} results garment is invalid`);
    return { id: parsed.id, category: parsed.category, sha256: parsed.sha256 };
  });
  return { personSha256: input.personSha256, garments };
}

function parseOutput(value: unknown, id: string): { file: string; sha256: string } {
  if (!value || typeof value !== 'object') throw new Error(`CERE-26 ${id} results output is invalid`);
  const output = value as { file?: unknown; sha256?: unknown; mime?: unknown };
  if (typeof output.file !== 'string' || !output.file || !isSha256(output.sha256) || typeof output.mime !== 'string') {
    throw new Error(`CERE-26 ${id} results output is invalid`);
  }
  return { file: output.file, sha256: output.sha256 };
}

function exactAliyunOutputPath(evidenceDir: string, caseId: string, file: string): string {
  const escapedCaseId = caseId.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  if (!new RegExp(`^aliyun/${escapedCaseId}\\.(png|jpg|webp|bmp)$`).test(file)) {
    throw new Error(`CERE-26 ${caseId} results output must be exactly aliyun/${caseId}.<supported extension>`);
  }
  const root = path.resolve(evidenceDir);
  const resolved = path.resolve(root, file);
  if (!resolved.startsWith(root + path.sep)) throw new Error('CERE-26 results output path escapes evidence directory');
  return resolved;
}

function isSha256(value: unknown): value is string {
  return typeof value === 'string' && /^[a-f0-9]{64}$/i.test(value);
}

async function assertHash(file: string, expected: string, label: string): Promise<void> {
  const bytes = await fsp.readFile(file);
  if (bytes.length === 0) throw new Error(`${label} is missing or empty`);
  const actual = crypto.createHash('sha256').update(bytes).digest('hex');
  if (actual !== expected) throw new Error(`${label} hash does not match results.json`);
}

async function main(): Promise<void> {
  const root = requiredEnvironment('PIXELFIT_ROOT');
  const ingest = requiredEnvironment('PIXELFIT_CERE26_INGEST');
  const manifestPath = path.resolve(process.env.PIXELFIT_CERE26_CASES ?? 'evidence/cere26/cases.json');
  const evidenceDir = path.resolve(process.env.PIXELFIT_CERE26_OUT ?? path.dirname(manifestPath));
  const triptychDir = path.join(evidenceDir, 'triptychs');
  const recipes = await validateTriptychEvidence(
    await fsp.readFile(manifestPath, 'utf8'),
    await fsp.readFile(path.join(evidenceDir, 'results.json'), 'utf8'),
    path.dirname(manifestPath),
    evidenceDir,
  );
  await fsp.mkdir(triptychDir, { recursive: true });

  const electron = path.resolve('node_modules', 'electron', 'dist', process.platform === 'win32' ? 'electron.exe' : 'electron');
  const childEnvironment: NodeJS.ProcessEnv = { ...process.env, PIXELFIT_ROOT: root, PIXELFIT_TRIPTYCH_RECIPES: JSON.stringify(recipes) };
  delete childEnvironment.DASHSCOPE_API_KEY;
  await executeFile(electron, electronTriptychArguments(ingest), { cwd: process.cwd(), env: childEnvironment });
  console.log(`CERE-26 triptychs wrote ${recipes.length} exact comparison images.`);
}

async function assertNonEmptyFile(file: string): Promise<void> {
  const stat = await fsp.stat(file);
  if (!stat.isFile() || stat.size === 0) throw new Error(`CERE-26 triptych input is missing or empty: ${file}`);
}

function requiredEnvironment(name: string): string {
  const value = process.env[name]?.trim();
  if (!value) throw new Error(`Missing required environment variable: ${name}`);
  return value;
}

if (process.argv.some((argument) => argument.includes('cere26-triptychs'))) {
  main().catch((error: unknown) => {
    console.error(error instanceof Error ? error.message : String(error));
    process.exitCode = 1;
  });
}
