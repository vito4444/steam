import crypto from 'node:crypto';
import fsp from 'node:fs/promises';
import path from 'node:path';

import { AliyunProvider } from '../src/main/tryon/aliyun';
import { FashnProvider } from '../src/main/tryon/fashn';
import type { CloudTryOnProvider } from '../src/main/tryon/provider';
import type { TryOnPlan } from '../src/shared/tryon';

const ACK = 'I_ACCEPT_PROVIDER_CHARGES';

if (process.env.PIXELFIT_BENCHMARK_ACK !== ACK) {
  throw new Error('Refusing paid calls. Set PIXELFIT_BENCHMARK_ACK=' + ACK + ' after confirming account charges.');
}

const personPath = required('PIXELFIT_BENCHMARK_PERSON');
const topPath = required('PIXELFIT_BENCHMARK_TOP');
const bottomPath = required('PIXELFIT_BENCHMARK_BOTTOM');
const fashnKey = required('FASHN_API_KEY');
const aliyunKey = required('DASHSCOPE_API_KEY');
const outDir = path.resolve(process.env.PIXELFIT_BENCHMARK_OUT ?? 'benchmark-out');

const [person, top, bottom] = await Promise.all([
  imageDataUrl(personPath),
  imageDataUrl(topPath),
  imageDataUrl(bottomPath),
]);
const plan: TryOnPlan = {
  steps: [
    { id: 'bottom', name: path.basename(bottomPath), category: 'bottom', imageDataUrl: bottom },
    { id: 'top', name: path.basename(topPath), category: 'top', imageDataUrl: top },
  ],
  unsupported: [],
};

await fsp.mkdir(outDir, { recursive: true });
const providers: CloudTryOnProvider[] = [
  new AliyunProvider({ apiKey: aliyunKey }),
  new FashnProvider({
    apiKey: fashnKey,
    model: 'tryon-max',
    mode: 'fast',
    resolution: '1k',
  }),
];
const results: Record<string, unknown>[] = [];

for (const provider of providers) {
  const startedAt = Date.now();
  const generated = await provider.generate(person, plan, new AbortController().signal);
  const bytes = dataUrlBytes(generated.imageDataUrl);
  const outputFile = path.join(outDir, provider.id + '.png');
  await fsp.writeFile(outputFile, bytes);
  results.push({
    provider: provider.id,
    model: provider.name,
    elapsedMs: Date.now() - startedAt,
    outputFile: path.basename(outputFile),
    outputSha256: crypto.createHash('sha256').update(bytes).digest('hex'),
    providerRequestIds: generated.providerRequestIds,
    creditsUsed: generated.creditsUsed,
    estimate: provider.profile,
    manualQualityScores: {
      personIdentityPreservation: null,
      garmentShapeAndLogo: null,
      colorAndTexture: null,
      handsAndOcclusion: null,
      topBottomCompatibility: null,
      overallUsability: null,
    },
  });
}

await fsp.writeFile(path.join(outDir, 'benchmark.json'), JSON.stringify({
  generatedAt: new Date().toISOString(),
  rubric: 'Fill every manual score from 1 (unusable) to 5 (production-ready) while viewing both outputs side by side.',
  inputs: {
    personSha256: await fileSha256(personPath),
    topSha256: await fileSha256(topPath),
    bottomSha256: await fileSha256(bottomPath),
  },
  results,
}, null, 2) + '\n', 'utf8');

console.log('Benchmark outputs written to ' + outDir);

function required(name: string): string {
  const value = process.env[name]?.trim();
  if (!value) throw new Error('Missing required environment variable: ' + name);
  return value;
}

async function imageDataUrl(filePath: string): Promise<string> {
  const extension = path.extname(filePath).toLowerCase();
  const mime = extension === '.jpg' || extension === '.jpeg'
    ? 'image/jpeg'
    : extension === '.webp' ? 'image/webp' : extension === '.bmp' ? 'image/bmp' : 'image/png';
  return 'data:' + mime + ';base64,' + (await fsp.readFile(filePath)).toString('base64');
}

function dataUrlBytes(dataUrl: string): Buffer {
  const comma = dataUrl.indexOf(',');
  if (comma < 0) throw new Error('Provider returned an invalid data URL');
  return Buffer.from(dataUrl.slice(comma + 1), 'base64');
}

async function fileSha256(filePath: string): Promise<string> {
  return crypto.createHash('sha256').update(await fsp.readFile(filePath)).digest('hex');
}
