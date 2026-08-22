import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';

import { afterEach, describe, expect, it } from 'vitest';

import {
  describeSaving,
  formatMegabytes,
  fullDownloadReport,
  isDifferentialFallback,
  parseDifferentialLine,
} from '../src/main/update/differential';
import { releaseNotesToText } from '../src/main/update/release-notes';
import { UpdatePreferencesRepository } from '../src/main/update/settings';

const roots: string[] = [];

afterEach(async () => {
  await Promise.all(roots.splice(0).map((root) => fs.rm(root, { recursive: true, force: true })));
});

async function tempRoot(): Promise<string> {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'pixelfit-update-'));
  roots.push(root);
  return root;
}

/**
 * 差分下载的字节数是这条需求的**唯一硬验收**，而它只以一行日志的形式出现在
 * electron-updater 内部。解析错了，报告里的「实际下载 X MB」就是编的。
 * 这几个用例钉住那一行的真实格式（KB、千分位逗号、两位小数）。
 */
describe('differential download log parsing', () => {
  it('parses the real electron-updater size line', () => {
    const parsed = parseDifferentialLine(
      'Full: 118,238.55 KB, To download: 3,442.11 KB (3%)',
    );
    expect(parsed).not.toBeNull();
    expect(parsed!.fullBytes).toBe(Math.round(118238.55 * 1024));
    expect(parsed!.downloadedBytes).toBe(Math.round(3442.11 * 1024));
  });

  it('parses a line without thousands separators', () => {
    const parsed = parseDifferentialLine('Full: 512.00 KB, To download: 0.00 KB (0%)');
    expect(parsed).toEqual({ fullBytes: 512 * 1024, downloadedBytes: 0 });
  });

  it('ignores unrelated log lines', () => {
    expect(parseDifferentialLine('Checking for update')).toBeNull();
    expect(parseDifferentialLine('Full: not a number KB, To download: x KB')).toBeNull();
  });

  it('detects the fallback-to-full-download line', () => {
    expect(
      isDifferentialFallback(
        'Cannot download differentially, fallback to full download: Error: ENOENT',
      ),
    ).toBe(true);
    expect(isDifferentialFallback('Downloading update from ...')).toBe(false);
  });

  it('reports the saving honestly, and says why when there is none', () => {
    const applied = { fullBytes: 100 * 1024 * 1024, downloadedBytes: 5 * 1024 * 1024, applied: true, reason: '' };
    expect(describeSaving(applied)).toContain('95%');
    const full = fullDownloadReport(120 * 1024 * 1024, '本机没有可比对的上一版安装包');
    expect(full.downloadedBytes).toBe(full.fullBytes);
    expect(describeSaving(full)).toBe('本机没有可比对的上一版安装包');
  });

  it('formats megabytes for the UI', () => {
    expect(formatMegabytes(1024 * 1024)).toBe('1.0 MB');
  });
});

describe('update preferences', () => {
  it('defaults to checking on launch', async () => {
    const root = await tempRoot();
    const repository = new UpdatePreferencesRepository(path.join(root, 'update.json'));
    expect(repository.load().checkOnLaunch).toBe(true);
    expect(repository.load().lastCheckedAt).toBeNull();
  });

  it('persists the opt-out and survives a reload', async () => {
    const root = await tempRoot();
    const file = path.join(root, 'nested', 'update.json');
    await new UpdatePreferencesRepository(file).save({ checkOnLaunch: false });
    expect(new UpdatePreferencesRepository(file).load().checkOnLaunch).toBe(false);
  });

  it('falls back to defaults on a corrupt file instead of crashing the launch path', async () => {
    const root = await tempRoot();
    const file = path.join(root, 'update.json');
    await fs.writeFile(file, '{ not json', 'utf8');
    expect(new UpdatePreferencesRepository(file).load()).toEqual({
      checkOnLaunch: true,
      lastCheckedAt: null,
    });
  });
});

/**
 * GitHub provider 从 Releases 的 Atom feed 取 `<content>`，那是**渲染后的 HTML**。
 * 0.4.5 的更新弹窗里因此显示成了 `<p><strong>…</strong></p>` 标签原文。
 */
describe('release notes', () => {
  it('turns GitHub HTML into readable plain text', () => {
    const html =
      '<p><strong>这是第一个可以通过应用内更新装上的版本。</strong></p>\n' +
      '<h2>改了什么</h2>\n<ul><li>手动检查更新给出明确回执</li><li>修了 &amp; 转义</li></ul>' +
      '<p>第一行<br>第二行</p>';
    const text = releaseNotesToText(html);
    expect(text).not.toContain('<');
    expect(text).toContain('这是第一个可以通过应用内更新装上的版本。');
    expect(text).toContain('• 手动检查更新给出明确回执');
    expect(text).toContain('修了 & 转义');
    expect(text).toContain('第一行\n第二行');
    expect(text).not.toMatch(/\n{3,}/);
  });

  it('passes plain text through untouched and tolerates empty input', () => {
    expect(releaseNotesToText('只是一行普通说明')).toBe('只是一行普通说明');
    expect(releaseNotesToText('')).toBe('');
    expect(releaseNotesToText(null)).toBe('');
    expect(releaseNotesToText(undefined)).toBe('');
  });

  it('flattens the fullChangelog array form', () => {
    expect(
      releaseNotesToText([
        { version: '0.4.6', note: '<p>修了更新说明显示成 HTML 源码</p>' },
        { version: '0.4.5', note: '<p>手动检查有回执</p>' },
      ]),
    ).toBe('## 0.4.6\n修了更新说明显示成 HTML 源码\n\n## 0.4.5\n手动检查有回执');
  });

  it('decodes numeric and named entities', () => {
    expect(releaseNotesToText('<p>a&nbsp;b &#8212; &lt;tag&gt; &quot;q&quot;</p>')).toBe(
      'a b — <tag> "q"',
    );
  });
});
