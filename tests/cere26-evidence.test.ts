import fsp from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';

import { describe, expect, it, vi } from 'vitest';

import {
  EVIDENCE_ACKNOWLEDGEMENT,
  parseEvidenceCases,
  runEvidence,
  writePngAtomically,
} from '../scripts/cere26-ai-evidence';
import { AliyunProvider } from '../src/main/tryon/aliyun';
import {
  electronTriptychArguments,
  parseTriptychRecipes,
  validateTriptychEvidence,
} from '../scripts/cere26-triptychs';
import { ProviderGenerationFailure } from '../src/main/tryon/provider';

const cases = [
  {
    id: 'top',
    person: 'fixtures/person.png',
    garments: [{ id: 'top-1', name: 'Top', category: 'top', image: 'fixtures/top.png' }],
  },
  {
    id: 'separates',
    person: 'fixtures/person.png',
    garments: [
      { id: 'top-2', name: 'Top', category: 'top', image: 'fixtures/top.png' },
      { id: 'bottom-1', name: 'Bottom', category: 'bottom', image: 'fixtures/bottom.png' },
    ],
  },
  {
    id: 'dress',
    person: 'fixtures/person.png',
    garments: [{ id: 'dress-1', name: 'Dress', category: 'dress', image: 'fixtures/dress.png' }],
  },
];

describe('CERE-26 evidence manifest', () => {
  it('launches triptych evidence with software rendering before the app path', () => {
    expect(electronTriptychArguments('C:\\fixtures\\dress')).toEqual([
      '--disable-gpu',
      '.',
      '--ingest',
      'C:\\fixtures\\dress',
      '--cere26-triptychs',
    ]);
  });

  it('accepts the literal top, separates, and dress evidence cases', () => {
    expect(parseEvidenceCases(JSON.stringify({ cases }))).toEqual(cases);
  });

  it('rejects reordered or mismatched exact paid cases', () => {
    expect(() => parseEvidenceCases(JSON.stringify({ cases: [cases[1], cases[0], cases[2]] }))).toThrow(/top.*separates.*dress/i);
    expect(() => parseEvidenceCases(JSON.stringify({ cases: [
      { ...cases[0], garments: [...cases[0].garments, cases[1].garments[1]] },
      cases[1],
      cases[2],
    ] }))).toThrow(/exactly one top/i);
    expect(() => parseEvidenceCases(JSON.stringify({ cases: [
      cases[0],
      { ...cases[1], garments: [cases[1].garments[1], cases[1].garments[0]] },
      cases[2],
    ] }))).toThrow(/top then bottom/i);
  });

  it('rejects duplicate evidence case IDs', () => {
    expect(() => parseEvidenceCases(JSON.stringify({ cases: [...cases, { ...cases[0] }] }))).toThrow(/duplicate/i);
  });

  it('rejects a manifest without top, bottom, and dress coverage', () => {
    const missingDress = cases.map((item) => item.id === 'dress'
      ? { ...item, garments: [{ id: 'top-3', name: 'Top', category: 'top', image: 'fixtures/top.png' }] }
      : item);
    expect(() => parseEvidenceCases(JSON.stringify({ cases: missingDress }))).toThrow(/coverage|dress/i);
  });

  it('uses the same exact validation for triptych inputs', () => {
    expect(() => parseTriptychRecipes(JSON.stringify({ cases: [cases[0], cases[2], cases[1]] }), 'evidence'))
      .toThrow(/top.*separates.*dress/i);
  });

  it('validates every input before calling the injected provider', async () => {
    const root = await fixtureRoot();
    const generate = vi.fn();
    await fsp.rm(path.join(root, 'fixtures', 'dress.png'));
    try {
      await expect(runEvidence({
        acknowledgement: EVIDENCE_ACKNOWLEDGEMENT,
        manifestPath: path.join(root, 'cases.json'),
        outDir: path.join(root, 'out'),
        generate,
      })).rejects.toThrow(/ENOENT|no such file/i);
      expect(generate).not.toHaveBeenCalled();
    } finally {
      await fsp.rm(root, { recursive: true, force: true });
    }
  });

  it('rejects missing acknowledgement before any injected provider call', async () => {
    const root = await fixtureRoot();
    const generate = vi.fn();
    try {
      await expect(runEvidence({
        acknowledgement: undefined,
        manifestPath: path.join(root, 'cases.json'),
        outDir: path.join(root, 'out'),
        generate,
      })).rejects.toThrow(/Refusing paid calls/i);
      expect(generate).not.toHaveBeenCalled();
    } finally {
      await fsp.rm(root, { recursive: true, force: true });
    }
  });

  it('stops after the first injected generation failure without retrying or advancing', async () => {
    const root = await fixtureRoot();
    const seen: string[] = [];
    const generate = vi.fn(async (_person: string, plan: { steps: Array<{ id: string }> }) => {
      seen.push(plan.steps[0].id);
      throw new ProviderGenerationFailure('first case failed', {
        providerRequestIds: ['task-1'], usageImageCount: 1, providerResponseRequestId: 'request-1',
      });
    });
    try {
      await expect(runEvidence({
        acknowledgement: EVIDENCE_ACKNOWLEDGEMENT,
        manifestPath: path.join(root, 'cases.json'),
        outDir: path.join(root, 'out'),
        generate,
      })).rejects.toThrow(/first case failed/i);
      expect(seen).toEqual(['top-1']);
      expect(generate).toHaveBeenCalledTimes(1);
    } finally {
      await fsp.rm(root, { recursive: true, force: true });
    }
  });

  it('records a secret-free failure row when billed output download fails', async () => {
    const root = await fixtureRoot();
    const secret = 'provider-secret-not-to-be-persisted';
    const generate = vi.fn(async () => {
      throw new ProviderGenerationFailure('download failed', {
        providerRequestIds: ['task-1'], usageImageCount: 1, providerResponseRequestId: 'request-1',
      });
    });
    try {
      await expect(runEvidence({
        acknowledgement: EVIDENCE_ACKNOWLEDGEMENT,
        manifestPath: path.join(root, 'cases.json'),
        outDir: path.join(root, 'out'),
        apiKey: secret,
        generate,
      })).rejects.toThrow(/download failed/i);
      const results = await fsp.readFile(path.join(root, 'out', 'results.json'), 'utf8');
      expect(results).toContain('"status": "failed"');
      expect(results).toContain('"providerResponseRequestId": "request-1"');
      expect(results).not.toContain(secret);
      expect(await tempFiles(path.join(root, 'out'))).toEqual([]);
    } finally {
      await fsp.rm(root, { recursive: true, force: true });
    }
  });

  it('records billed evidence before surfacing local PNG persistence failure', async () => {
    const root = await fixtureRoot();
    try {
      await expect(runEvidence({
        acknowledgement: EVIDENCE_ACKNOWLEDGEMENT,
        manifestPath: path.join(root, 'cases.json'),
        outDir: path.join(root, 'out'),
        generate: async () => successfulGeneration(),
        writeOutput: (file, bytes) => writePngAtomically(file, bytes, {
          ...fsp,
          rename: async () => { throw new Error('disk full'); },
        }),
      })).rejects.toThrow(/disk full/i);
      const results = await fsp.readFile(path.join(root, 'out', 'results.json'), 'utf8');
      expect(results).toContain('"status": "failed"');
      expect(results).toContain('"failure": "local_persistence"');
      expect(results).toContain('"usageImageCount": 1');
      expect(await tempFiles(path.join(root, 'out'))).toEqual([]);
      await expect(fsp.stat(path.join(root, 'out', 'aliyun', 'top.png'))).rejects.toThrow();
    } finally {
      await fsp.rm(root, { recursive: true, force: true });
    }
  });

  it.each([
    ['missing usage', { providerRequestIds: ['task-1'], usageImageCount: null, providerResponseRequestId: 'request-1' }],
    ['invalid request ID', { providerRequestIds: ['task-1'], usageImageCount: 1, providerResponseRequestId: null }],
  ])('writes one allowlisted partial billing failure row when Aliyun evidence has %s', async (_label, billingEvidence) => {
    const root = await fixtureRoot();
    try {
      await expect(runEvidence({
        acknowledgement: EVIDENCE_ACKNOWLEDGEMENT,
        manifestPath: path.join(root, 'cases.json'),
        outDir: path.join(root, 'out'),
        generate: async () => { throw new ProviderGenerationFailure('billing invalid', billingEvidence, 'billing_evidence'); },
      })).rejects.toThrow(/billing invalid/i);
      const results = JSON.parse(await fsp.readFile(path.join(root, 'out', 'results.json'), 'utf8')) as { results: unknown[] };
      expect(results.results).toHaveLength(1);
      expect(results.results[0]).toMatchObject({ status: 'failed', failure: 'billing_evidence', ...billingEvidence });
    } finally {
      await fsp.rm(root, { recursive: true, force: true });
    }
  });

  it('does not retain an unpersisted success when the first results write fails', async () => {
    const root = await fixtureRoot();
    let writes = 0;
    try {
      await expect(runEvidence({
        acknowledgement: EVIDENCE_ACKNOWLEDGEMENT,
        manifestPath: path.join(root, 'cases.json'),
        outDir: path.join(root, 'out'),
        generate: async () => successfulGeneration(),
        writeResults: async (file, results) => {
          writes += 1;
          if (writes === 1) throw new Error('results disk full');
          await fsp.writeFile(file, JSON.stringify(results), 'utf8');
        },
      })).rejects.toThrow(/results disk full/i);
      const results = JSON.parse(await fsp.readFile(path.join(root, 'out', 'results.json'), 'utf8')) as { results: Array<{ status: string; cnyCharge: number; failure?: string }> };
      expect(results.results).toHaveLength(1);
      expect(results.results[0]).toMatchObject({ status: 'failed', cnyCharge: 0.5, failure: 'local_persistence' });
      expect(writes).toBe(2);
    } finally {
      await fsp.rm(root, { recursive: true, force: true });
    }
  });

  it('persists exact JPEG output bytes with their actual extension and MIME', async () => {
    const root = await fixtureRoot();
    const jpeg = Buffer.from([0xff, 0xd8, 0xff, 0xd9]);
    try {
      await runEvidence({
        acknowledgement: EVIDENCE_ACKNOWLEDGEMENT,
        manifestPath: path.join(root, 'cases.json'),
        outDir: path.join(root, 'out'),
        generate: async () => ({ ...successfulGeneration(), imageDataUrl: `data:image/jpeg;base64,${jpeg.toString('base64')}` }),
      });
      expect(await fsp.readFile(path.join(root, 'out', 'aliyun', 'top.jpg'))).toEqual(jpeg);
      const results = JSON.parse(await fsp.readFile(path.join(root, 'out', 'results.json'), 'utf8')) as { results: Array<{ id: string; output: { file: string; mime: string } }> };
      expect(results.results[0]).toMatchObject({ id: 'top', output: { file: 'aliyun/top.jpg', mime: 'image/jpeg' } });
    } finally {
      await fsp.rm(root, { recursive: true, force: true });
    }
  });

  it('binds non-PNG provider bytes and paths from results.json before triptyching', async () => {
    const root = await fixtureRoot();
    const evidenceDir = path.join(root, 'out');
    await fsp.mkdir(path.join(evidenceDir, 'aliyun'), { recursive: true });
    const jpeg = Buffer.from([0xff, 0xd8, 0xff, 0xd9]);
    await fsp.writeFile(path.join(evidenceDir, 'aliyun', 'top.jpg'), jpeg);
    const results = await completedResults(root, evidenceDir, 'aliyun/top.jpg', jpeg);
    try {
      const recipes = await validateTriptychEvidence(JSON.stringify({ cases }), JSON.stringify(results), root, evidenceDir);
      expect(recipes.map((recipe) => recipe.aliyunImage)).toContain(path.join(evidenceDir, 'aliyun', 'top.jpg'));
    } finally {
      await fsp.rm(root, { recursive: true, force: true });
    }
  });

  it('fails closed when a results-bound Aliyun file is replaced', async () => {
    const root = await fixtureRoot();
    const evidenceDir = path.join(root, 'out');
    await fsp.mkdir(path.join(evidenceDir, 'aliyun'), { recursive: true });
    const jpeg = Buffer.from([0xff, 0xd8, 0xff, 0xd9]);
    const file = path.join(evidenceDir, 'aliyun', 'top.jpg');
    await fsp.writeFile(file, jpeg);
    const results = await completedResults(root, evidenceDir, 'aliyun/top.jpg', jpeg);
    await fsp.writeFile(file, Buffer.from([1, 2, 3]));
    try {
      await expect(validateTriptychEvidence(JSON.stringify({ cases }), JSON.stringify(results), root, evidenceDir)).rejects.toThrow(/hash/i);
    } finally {
      await fsp.rm(root, { recursive: true, force: true });
    }
  });

  it('rejects a results output outside its exact Aliyun case path instead of rewriting the rendered path', async () => {
    const root = await fixtureRoot();
    const evidenceDir = path.join(root, 'out');
    const declared = Buffer.from([1, 2, 3]);
    const rewritten = Buffer.from([4, 5, 6]);
    await fsp.mkdir(path.join(evidenceDir, 'aliyun', 'local'), { recursive: true });
    await fsp.mkdir(path.join(evidenceDir, 'local'), { recursive: true });
    await fsp.writeFile(path.join(evidenceDir, 'local', 'top.jpg'), declared);
    await fsp.writeFile(path.join(evidenceDir, 'aliyun', 'local', 'top.jpg'), rewritten);
    const results = await completedResults(root, evidenceDir, 'local/top.jpg', declared);
    try {
      await expect(validateTriptychEvidence(JSON.stringify({ cases }), JSON.stringify(results), root, evidenceDir)).rejects.toThrow(/aliyun.*top/i);
    } finally {
      await fsp.rm(root, { recursive: true, force: true });
    }
  });

  it.each([0, 2])('records exactly one charged failure when Aliyun reports usage.image_count %i', async (imageCount) => {
    const root = await fixtureRoot();
    const provider = new AliyunProvider({
      apiKey: 'test',
      fetchFn: succeededBillingFetch({ usage: { image_count: imageCount }, request_id: 'request-1' }) as typeof fetch,
    });
    try {
      await expect(runEvidence({
        acknowledgement: EVIDENCE_ACKNOWLEDGEMENT,
        manifestPath: path.join(root, 'cases.json'),
        outDir: path.join(root, 'out'),
        generate: (person, plan) => provider.generate(person, plan, new AbortController().signal),
      })).rejects.toMatchObject({ name: ProviderGenerationFailure.name, stage: 'billing_evidence' });
      const results = JSON.parse(await fsp.readFile(path.join(root, 'out', 'results.json'), 'utf8')) as { results: Array<{ status: string; failure: string; cnyCharge: number; providerRequestIds: string[]; providerResponseRequestId: string | null }> };
      expect(results.results).toEqual([expect.objectContaining({
        status: 'failed', failure: 'billing_evidence', cnyCharge: 0.5,
        providerRequestIds: ['task-1'], providerResponseRequestId: 'request-1',
      })]);
    } finally {
      await fsp.rm(root, { recursive: true, force: true });
    }
  });

  it('records a billed failure row for an Aliyun SUCCEEDED response without image_url', async () => {
    const root = await fixtureRoot();
    const provider = new AliyunProvider({ apiKey: 'test', fetchFn: missingImageFetch as typeof fetch });
    try {
      await expect(runEvidence({
        acknowledgement: EVIDENCE_ACKNOWLEDGEMENT,
        manifestPath: path.join(root, 'cases.json'),
        outDir: path.join(root, 'out'),
        generate: (person, plan) => provider.generate(person, plan, new AbortController().signal),
      })).rejects.toMatchObject({ name: ProviderGenerationFailure.name });
      const results = await fsp.readFile(path.join(root, 'out', 'results.json'), 'utf8');
      expect(results).toContain('"status": "failed"');
      expect(results).toContain('"providerResponseRequestId": "request-1"');
    } finally {
      await fsp.rm(root, { recursive: true, force: true });
    }
  });
});

function successfulGeneration() {
  return {
    imageDataUrl: 'data:image/png;base64,iVBORw==',
    providerRequestIds: ['task-1'],
    creditsUsed: null,
    usageImageCount: 1,
    providerResponseRequestId: 'request-1',
  };
}

async function completedResults(root: string, evidenceDir: string, topOutput: string, topBytes: Buffer) {
  const crypto = await import('node:crypto');
  const person = await fsp.readFile(path.join(root, 'fixtures', 'person.png'));
  const top = await fsp.readFile(path.join(root, 'fixtures', 'top.png'));
  const bottom = await fsp.readFile(path.join(root, 'fixtures', 'bottom.png'));
  const dress = await fsp.readFile(path.join(root, 'fixtures', 'dress.png'));
  const hash = (bytes: Buffer) => crypto.createHash('sha256').update(bytes).digest('hex');
  await fsp.mkdir(path.join(evidenceDir, 'local'), { recursive: true });
  for (const id of ['top', 'separates', 'dress']) await fsp.writeFile(path.join(evidenceDir, 'local', `${id}-on.png`), Buffer.from([1]));
  for (const id of ['separates', 'dress']) await fsp.writeFile(path.join(evidenceDir, 'aliyun', `${id}.png`), Buffer.from([id]));
  return {
    schemaVersion: 1,
    results: [
      { status: 'succeeded', id: 'top', input: { personSha256: hash(person), garments: [{ id: 'top-1', category: 'top', sha256: hash(top) }] }, output: { file: topOutput, sha256: hash(topBytes), mime: 'image/jpeg' } },
      { status: 'succeeded', id: 'separates', input: { personSha256: hash(person), garments: [{ id: 'top-2', category: 'top', sha256: hash(top) }, { id: 'bottom-1', category: 'bottom', sha256: hash(bottom) }] }, output: { file: 'aliyun/separates.png', sha256: hash(Buffer.from(['separates'])), mime: 'image/png' } },
      { status: 'succeeded', id: 'dress', input: { personSha256: hash(person), garments: [{ id: 'dress-1', category: 'dress', sha256: hash(dress) }] }, output: { file: 'aliyun/dress.png', sha256: hash(Buffer.from(['dress'])), mime: 'image/png' } },
    ],
  };
}

async function fixtureRoot(): Promise<string> {
  const root = await fsp.mkdtemp(path.join(os.tmpdir(), 'pixelfit-cere26-'));
  const fixtures = path.join(root, 'fixtures');
  await fsp.mkdir(fixtures);
  await Promise.all(['person.png', 'top.png', 'bottom.png', 'dress.png']
    .map((name) => fsp.writeFile(path.join(fixtures, name), Buffer.from([137, 80, 78, 71]))));
  await fsp.writeFile(path.join(root, 'cases.json'), JSON.stringify({ cases }), 'utf8');
  return root;
}

async function tempFiles(root: string): Promise<string[]> {
  const entries = await fsp.readdir(root, { recursive: true });
  return entries.filter((entry) => typeof entry === 'string' && entry.endsWith('.tmp'));
}

async function missingImageFetch(input: string | URL | Request): Promise<Response> {
  const url = String(input);
  if (url.includes('/uploads?')) {
    return new Response(JSON.stringify({ data: {
      policy: 'policy', signature: 'signature', upload_dir: 'job', upload_host: 'https://upload.example',
      oss_access_key_id: 'access', x_oss_object_acl: 'private', x_oss_forbid_overwrite: 'true',
    } }), { headers: { 'content-type': 'application/json' } });
  }
  if (url === 'https://upload.example') return new Response('', { status: 200 });
  if (url.includes('/image-synthesis')) {
    return new Response(JSON.stringify({ output: { task_id: 'task-1' } }), { headers: { 'content-type': 'application/json' } });
  }
  return new Response(JSON.stringify({
    output: { task_status: 'SUCCEEDED' }, usage: { image_count: 1 }, request_id: 'request-1',
  }), { headers: { 'content-type': 'application/json' } });
}

function succeededBillingFetch(partial: Record<string, unknown>) {
  return async (input: string | URL | Request) => {
    const url = String(input);
    if (url.includes('/uploads?')) return new Response(JSON.stringify({ data: uploadPolicy() }), { headers: { 'content-type': 'application/json' } });
    if (url === 'https://upload.example') return new Response('', { status: 200 });
    if (url.includes('/image-synthesis')) return new Response(JSON.stringify({ output: { task_id: 'task-1' } }), { headers: { 'content-type': 'application/json' } });
    return new Response(JSON.stringify({
      output: { task_status: 'SUCCEEDED', image_url: 'https://output.example/result.png' },
      ...partial,
    }), { headers: { 'content-type': 'application/json' } });
  };
}

function uploadPolicy() {
  return {
    policy: 'policy', signature: 'signature', upload_dir: 'job', upload_host: 'https://upload.example',
    oss_access_key_id: 'access', x_oss_object_acl: 'private', x_oss_forbid_overwrite: 'true',
  };
}
