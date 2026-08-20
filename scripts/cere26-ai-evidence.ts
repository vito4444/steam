import crypto from 'node:crypto';
import fsp from 'node:fs/promises';
import path from 'node:path';

import { AliyunProvider } from '../src/main/tryon/aliyun';
import {
  ProviderGenerationFailure,
  type ProviderBillingEvidence,
  type ProviderGeneration,
} from '../src/main/tryon/provider';
import type { TryOnCategory, TryOnPlan } from '../src/shared/tryon';

export const EVIDENCE_ACKNOWLEDGEMENT = 'I_ACCEPT_CERE26_THREE_ALIYUN_IMAGES_AT_Y0_50_EACH';
const ALIYUN_CNY_PER_IMAGE = 0.5;
const REQUIRED_CASES = [
  { id: 'top', categories: ['top'] },
  { id: 'separates', categories: ['top', 'bottom'] },
  { id: 'dress', categories: ['dress'] },
] as const;

export interface EvidenceGarment {
  id: string;
  name: string;
  category: TryOnCategory;
  image: string;
}

export interface EvidenceCase {
  id: string;
  person: string;
  garments: EvidenceGarment[];
}

interface PreparedEvidenceCase {
  definition: EvidenceCase;
  person: { dataUrl: string; sha256: string };
  garments: Array<EvidenceGarment & { dataUrl: string; sha256: string }>;
}

export interface EvidenceRunOptions {
  acknowledgement: string | undefined;
  manifestPath: string;
  outDir: string;
  apiKey?: string;
  generate?: (person: string, plan: TryOnPlan) => Promise<ProviderGeneration>;
  writeOutput?: (file: string, bytes: Buffer) => Promise<void>;
  writeResults?: (file: string, results: EvidenceResults) => Promise<void>;
}

export type AtomicFileOperations = Pick<typeof fsp, 'writeFile' | 'rename' | 'rm'>;

export function parseEvidenceCases(raw: string): EvidenceCase[] {
  let parsed: unknown;
  try {
    parsed = JSON.parse(raw);
  } catch {
    throw new Error('CERE-26 evidence cases must be valid JSON');
  }
  if (!parsed || typeof parsed !== 'object' || !Array.isArray((parsed as { cases?: unknown }).cases)) {
    throw new Error('CERE-26 evidence cases must contain a cases array');
  }
  const cases = (parsed as { cases: unknown[] }).cases.map(parseEvidenceCase);
  const caseIds = new Set<string>();
  for (const item of cases) {
    if (caseIds.has(item.id)) throw new Error(`CERE-26 evidence cases contain duplicate ID: ${item.id}`);
    caseIds.add(item.id);
  }
  assertExactCases(cases);
  return cases;
}

export async function runEvidence(options: EvidenceRunOptions): Promise<void> {
  if (options.acknowledgement !== EVIDENCE_ACKNOWLEDGEMENT) {
    throw new Error(`Refusing paid calls. Set PIXELFIT_CERE26_ACK=${EVIDENCE_ACKNOWLEDGEMENT} after confirming exactly three ¥0.50 Aliyun image charges.`);
  }
  const manifestPath = path.resolve(options.manifestPath);
  const outDir = path.resolve(options.outDir);
  const cases = parseEvidenceCases(await fsp.readFile(manifestPath, 'utf8'));
  // Every local input is decoded before creating or calling the provider.
  const prepared = await Promise.all(cases.map((item) => prepareEvidenceCase(item, path.dirname(manifestPath))));
  const generate = options.generate ?? createAliyunGenerator(requiredApiKey(options.apiKey));
  const writeOutput = options.writeOutput ?? writePngAtomically;
  const writeResults = options.writeResults ?? writeResultsAtomically;
  const results: EvidenceResults = { schemaVersion: 1, results: [] };
  const appendResult = async (row: EvidenceResult): Promise<void> => {
    const candidate: EvidenceResults = { ...results, results: [...results.results, row] };
    await writeResults(path.join(outDir, 'results.json'), candidate);
    results.results.push(row);
  };

  await fsp.mkdir(path.join(outDir, 'aliyun'), { recursive: true });
  for (const item of prepared) {
    const startedAt = Date.now();
    const input = accountingInput(item);
    let billing: BilledEvidence | null = null;
    try {
      const generated = await generate(item.person.dataUrl, planFor(item));
      billing = requiredBillingEvidence(generated);
      const output = dataUrlImage(generated.imageDataUrl);
      const outputFile = path.join(outDir, 'aliyun', `${item.definition.id}.${output.extension}`);
      await writeOutput(outputFile, output.bytes);
      await appendResult({
        status: 'succeeded',
        id: item.definition.id,
        input,
        output: {
          file: path.join('aliyun', `${item.definition.id}.${output.extension}`).replace(/\\/g, '/'),
          sha256: sha256(output.bytes),
          mime: output.mime,
        },
        elapsedMs: Math.max(Date.now() - startedAt, 1),
        ...billing,
        cnyCharge: ALIYUN_CNY_PER_IMAGE,
      });
    } catch (error) {
      const knownBilling = billing ?? billingEvidenceFrom(error);
      if (knownBilling) {
        await appendResult({
          status: 'failed',
          id: item.definition.id,
          input,
          output: null,
          elapsedMs: Math.max(Date.now() - startedAt, 1),
          ...knownBilling,
          cnyCharge: ALIYUN_CNY_PER_IMAGE,
          failure: error instanceof ProviderGenerationFailure ? error.stage : 'local_persistence',
        });
      }
      throw error;
    }
  }
}

function createAliyunGenerator(apiKey: string): NonNullable<EvidenceRunOptions['generate']> {
  const provider = new AliyunProvider({ apiKey });
  return (person, plan) => provider.generate(person, plan, new AbortController().signal);
}

function assertExactCases(cases: EvidenceCase[]): void {
  if (cases.length !== REQUIRED_CASES.length) {
    throw new Error('CERE-26 paid evidence requires exactly top, separates, dress cases in that order');
  }
  for (let index = 0; index < REQUIRED_CASES.length; index++) {
    const expected = REQUIRED_CASES[index];
    const actual = cases[index];
    if (actual.id !== expected.id) {
      throw new Error('CERE-26 paid evidence requires exactly top, separates, dress cases in that order');
    }
    const categories = actual.garments.map((garment) => garment.category);
    if (categories.length !== expected.categories.length || categories.some((category, itemIndex) => category !== expected.categories[itemIndex])) {
      const expectedText = expected.categories.length === 1 ? `exactly one ${expected.categories[0]}` : 'top then bottom';
      throw new Error(`CERE-26 ${expected.id} case must contain ${expectedText}`);
    }
  }
}

function parseEvidenceCase(value: unknown): EvidenceCase {
  if (!value || typeof value !== 'object') throw new Error('CERE-26 evidence case must be an object');
  const record = value as Record<string, unknown>;
  const id = requiredText(record.id, 'case id');
  if (!/^[a-z0-9][a-z0-9_-]*$/i.test(id)) throw new Error(`CERE-26 evidence case ID is invalid: ${id}`);
  const person = requiredText(record.person, `case ${id} person`);
  if (!Array.isArray(record.garments) || record.garments.length === 0) {
    throw new Error(`CERE-26 evidence case ${id} must contain garments`);
  }
  const garments = record.garments.map((garment) => parseEvidenceGarment(garment, id));
  const garmentIds = new Set<string>();
  for (const garment of garments) {
    if (garmentIds.has(garment.id)) throw new Error(`CERE-26 evidence case ${id} contains duplicate garment ID: ${garment.id}`);
    garmentIds.add(garment.id);
  }
  return { id, person, garments };
}

function parseEvidenceGarment(value: unknown, caseId: string): EvidenceGarment {
  if (!value || typeof value !== 'object') throw new Error(`CERE-26 evidence case ${caseId} has an invalid garment`);
  const record = value as Record<string, unknown>;
  const category = requiredText(record.category, `case ${caseId} garment category`) as TryOnCategory;
  if (!['top', 'bottom', 'dress'].includes(category)) {
    throw new Error(`CERE-26 evidence case ${caseId} has unsupported garment category: ${category}`);
  }
  return {
    id: requiredText(record.id, `case ${caseId} garment id`),
    name: requiredText(record.name, `case ${caseId} garment name`),
    category,
    image: requiredText(record.image, `case ${caseId} garment image`),
  };
}

function planFor(item: PreparedEvidenceCase): TryOnPlan {
  return {
    steps: item.garments.map(({ dataUrl, id, name, category }) => ({ id, name, category, imageDataUrl: dataUrl })),
    unsupported: [],
  };
}

function accountingInput(item: PreparedEvidenceCase): AccountingInput {
  return {
    personSha256: item.person.sha256,
    garments: item.garments.map((garment) => ({ id: garment.id, category: garment.category, sha256: garment.sha256 })),
  };
}

async function prepareEvidenceCase(item: EvidenceCase, manifestDir: string): Promise<PreparedEvidenceCase> {
  const [person, ...garments] = await Promise.all([
    readEvidenceImage(path.resolve(manifestDir, item.person)),
    ...item.garments.map(async (garment) => ({ ...garment, ...await readEvidenceImage(path.resolve(manifestDir, garment.image)) })),
  ]);
  return { definition: item, person, garments: garments as PreparedEvidenceCase['garments'] };
}

function requiredBillingEvidence(generated: ProviderGeneration): BilledEvidence {
  if (generated.usageImageCount !== 1 || generated.providerRequestIds.length !== 1 || !generated.providerResponseRequestId) {
    throw new Error('CERE-26 provider response is missing required billing evidence');
  }
  return {
    usageImageCount: generated.usageImageCount,
    providerRequestIds: generated.providerRequestIds,
    providerResponseRequestId: generated.providerResponseRequestId,
  };
}

function billingEvidenceFrom(error: unknown): BilledEvidence | null {
  if (!(error instanceof ProviderGenerationFailure)) return null;
  const evidence = error.billingEvidence;
  if (evidence.providerRequestIds.length !== 1 || !evidence.providerRequestIds[0]) return null;
  return {
    usageImageCount: evidence.usageImageCount,
    providerRequestIds: evidence.providerRequestIds,
    providerResponseRequestId: evidence.providerResponseRequestId,
  };
}

async function readEvidenceImage(file: string): Promise<{ dataUrl: string; sha256: string }> {
  const bytes = await fsp.readFile(file);
  if (bytes.length === 0) throw new Error(`CERE-26 evidence input is empty: ${file}`);
  return { dataUrl: `data:${mimeFor(file)};base64,${bytes.toString('base64')}`, sha256: sha256(bytes) };
}

function mimeFor(file: string): string {
  const extension = path.extname(file).toLowerCase();
  if (extension === '.jpg' || extension === '.jpeg') return 'image/jpeg';
  if (extension === '.png') return 'image/png';
  if (extension === '.webp') return 'image/webp';
  if (extension === '.bmp') return 'image/bmp';
  throw new Error(`CERE-26 evidence input has unsupported image extension: ${file}`);
}

function dataUrlImage(dataUrl: string): { bytes: Buffer; mime: 'image/png' | 'image/jpeg' | 'image/webp' | 'image/bmp'; extension: 'png' | 'jpg' | 'webp' | 'bmp' } {
  const match = /^data:(image\/(?:png|jpeg|jpg|webp|bmp));base64,([A-Za-z0-9+/=]+)$/i.exec(dataUrl);
  if (!match) throw new Error('CERE-26 provider output is not a supported image data URL');
  const bytes = Buffer.from(match[2], 'base64');
  if (bytes.length === 0) throw new Error('CERE-26 provider output is empty');
  const mime = match[1].toLowerCase() === 'image/jpg' ? 'image/jpeg' : match[1].toLowerCase() as 'image/png' | 'image/jpeg' | 'image/webp' | 'image/bmp';
  return { bytes, mime, extension: mime === 'image/jpeg' ? 'jpg' : mime.slice('image/'.length) as 'png' | 'webp' | 'bmp' };
}

export async function writePngAtomically(
  file: string,
  bytes: Buffer,
  operations: AtomicFileOperations = fsp,
): Promise<void> {
  await writeFileAtomically(file, bytes, operations);
}

async function writeFileAtomically(
  file: string,
  bytes: Buffer,
  operations: AtomicFileOperations = fsp,
): Promise<void> {
  const temporary = `${file}.${process.pid}.tmp`;
  try {
    await operations.writeFile(temporary, bytes);
    await operations.rename(temporary, file);
  } finally {
    await operations.rm(temporary, { force: true });
  }
}

async function writeResultsAtomically(file: string, results: EvidenceResults): Promise<void> {
  await writeFileAtomically(file, Buffer.from(JSON.stringify(results, null, 2) + '\n', 'utf8'));
}

function sha256(value: Buffer): string {
  return crypto.createHash('sha256').update(value).digest('hex');
}

function requiredText(value: unknown, label: string): string {
  if (typeof value !== 'string' || !value.trim()) throw new Error(`CERE-26 ${label} is required`);
  return value.trim();
}

function requiredApiKey(value: string | undefined): string {
  if (!value?.trim()) throw new Error('Missing required environment variable: DASHSCOPE_API_KEY');
  return value.trim();
}

interface BilledEvidence {
  usageImageCount: 1 | null;
  providerRequestIds: string[];
  providerResponseRequestId: string | null;
}

interface AccountingInput {
  personSha256: string;
  garments: Array<{ id: string; category: TryOnCategory; sha256: string }>;
}

interface EvidenceResults {
  schemaVersion: 1;
  results: EvidenceResult[];
}

interface EvidenceResult {
    status: 'succeeded' | 'failed';
    id: string;
    input: AccountingInput;
    output: { file: string; sha256: string; mime: string } | null;
    elapsedMs: number;
    usageImageCount: 1 | null;
    providerRequestIds: string[];
    providerResponseRequestId: string | null;
    cnyCharge: number;
    failure?: 'billing_evidence' | 'output_download' | 'local_persistence';
}

async function main(): Promise<void> {
  await runEvidence({
    acknowledgement: process.env.PIXELFIT_CERE26_ACK,
    manifestPath: process.env.PIXELFIT_CERE26_CASES ?? 'evidence/cere26/cases.json',
    outDir: process.env.PIXELFIT_CERE26_OUT ?? 'evidence/cere26',
    apiKey: process.env.DASHSCOPE_API_KEY,
  });
  console.log('CERE-26 evidence accounting completed.');
}

if (process.argv.some((argument) => argument.includes('cere26-ai-evidence'))) {
  main().catch((error: unknown) => {
    console.error(error instanceof Error ? error.message : String(error));
    process.exitCode = 1;
  });
}
