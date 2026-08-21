import crypto from 'node:crypto';
import fsp from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';

import { afterEach, describe, expect, test } from 'vitest';

const temporaryDirectories: string[] = [];

afterEach(async () => {
  await Promise.all(temporaryDirectories.splice(0).map((directory) => fsp.rm(directory, { recursive: true, force: true })));
});

describe('prepare-windows-pipeline.ps1', () => {
  test('verify-only accepts model bytes that match the lock file', async () => {
    const fixture = await createFixture('abc');

    const result = runPrepareScript(fixture.lockFile, fixture.modelsDirectory);

    expect(result.status, result.stderr).toBe(0);
    expect(result.stdout).toContain('Verified 1 locked model file.');
  });

  test('verify-only rejects model bytes that do not match the lock file', async () => {
    const fixture = await createFixture('xyz', 'abc');

    const result = runPrepareScript(fixture.lockFile, fixture.modelsDirectory);

    expect(result.status).not.toBe(0);
    expect(`${result.stdout}\n${result.stderr}`).toContain('MD5 mismatch');
  });
});

async function createFixture(actualBytes: string, lockedBytes = actualBytes): Promise<{ lockFile: string; modelsDirectory: string }> {
  const directory = await fsp.mkdtemp(path.join(os.tmpdir(), 'pixelfit-pipeline-prep-'));
  temporaryDirectories.push(directory);
  const modelsDirectory = path.join(directory, 'models');
  await fsp.mkdir(modelsDirectory);
  await fsp.writeFile(path.join(modelsDirectory, 'fixture.onnx'), actualBytes);
  const lockFile = path.join(directory, 'models.lock.json');
  await fsp.writeFile(lockFile, JSON.stringify({
    models: [{
      runtime_filename: 'fixture.onnx',
      bytes: Buffer.byteLength(lockedBytes),
      md5: crypto.createHash('md5').update(lockedBytes).digest('hex'),
      download_url: 'https://example.invalid/fixture.onnx',
    }],
  }));
  return { lockFile, modelsDirectory };
}

function runPrepareScript(lockFile: string, modelsDirectory: string) {
  return spawnSync('powershell.exe', [
    '-NoProfile',
    '-ExecutionPolicy', 'Bypass',
    '-File', path.resolve('scripts/prepare-windows-pipeline.ps1'),
    '-LockFile', lockFile,
    '-ModelsDirectory', modelsDirectory,
    '-VerifyOnly',
  ], { encoding: 'utf8' });
}
