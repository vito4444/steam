/**
 * CERE-59：算出「从版本 A 更新到版本 B，差分下载实际要取多少字节」。
 *
 * 为什么要有这个脚本：验收要的是**实测字节数**，不是「支持差分」四个字。
 * 应用内那个数字来自 electron-updater 运行时打的一行日志，只有真的点了更新
 * 才看得到；这个脚本让任何人拿两个 .blockmap 就能离线复算同一个数字。
 *
 * 关键点：它不自己实现 diff 算法，而是直接调用 electron-updater 内部的
 * `computeOperations` —— 和应用运行时用的是同一份代码，所以算出来的
 * DOWNLOAD 字节数就是真实会走网络的字节数（不含 HTTP range 请求头开销）。
 *
 * 用法：
 *   vite-node --script scripts/blockmap-diff.ts <old.blockmap> <new.blockmap>
 *   vite-node --script scripts/blockmap-diff.ts <old-url> <new-url>
 */
import fs from 'node:fs/promises';
import { gunzipSync } from 'node:zlib';

import {
  OperationKind,
  computeOperations,
} from 'electron-updater/out/differentialDownloader/downloadPlanBuilder.js';

interface BlockMap {
  files: Array<{ name: string; offset: number; checksums: string[]; sizes: number[] }>;
}

async function readBlockMap(source: string): Promise<BlockMap> {
  const raw = /^https?:\/\//.test(source)
    ? Buffer.from(await (await fetchOrThrow(source)).arrayBuffer())
    : await fs.readFile(source);
  // electron-builder 产出的 .blockmap 是 gzip 过的 JSON。
  return JSON.parse(gunzipSync(raw).toString('utf8')) as BlockMap;
}

async function fetchOrThrow(url: string): Promise<Response> {
  const response = await fetch(url);
  if (!response.ok) throw new Error(`HTTP ${response.status} @ ${url}`);
  return response;
}

function mb(bytes: number): string {
  return `${(bytes / 1024 / 1024).toFixed(2)} MB`;
}

async function main(): Promise<void> {
  const [oldSource, newSource] = process.argv.slice(2);
  if (!oldSource || !newSource) {
    console.error('usage: blockmap-diff <old.blockmap|url> <new.blockmap|url>');
    process.exit(2);
    return;
  }

  const [oldMap, newMap] = await Promise.all([readBlockMap(oldSource), readBlockMap(newSource)]);
  const logger = {
    info: () => undefined,
    warn: (message: string) => console.warn(`warn: ${message}`),
    error: (message: string) => console.error(`error: ${message}`),
  };
  const operations = computeOperations(oldMap, newMap, logger);

  let downloadBytes = 0;
  let copyBytes = 0;
  let downloadRanges = 0;
  for (const operation of operations) {
    const length = operation.end - operation.start;
    if (operation.kind === OperationKind.DOWNLOAD) {
      downloadBytes += length;
      downloadRanges++;
    } else {
      copyBytes += length;
    }
  }

  const file = newMap.files[0]!;
  const fullBytes = file.offset + file.sizes.reduce((sum, size) => sum + size, 0);
  const percent = fullBytes > 0 ? (downloadBytes / fullBytes) * 100 : 0;

  console.log(`old blockmap   : ${oldSource}`);
  console.log(`new blockmap   : ${newSource}`);
  console.log(`full installer : ${mb(fullBytes)} (${fullBytes} bytes)`);
  console.log(`reused locally : ${mb(copyBytes)} (${copyBytes} bytes)`);
  console.log(`downloaded     : ${mb(downloadBytes)} (${downloadBytes} bytes, ${percent.toFixed(2)}%)`);
  console.log(`http ranges    : ${downloadRanges}`);
  console.log(`blocks         : ${file.checksums.length} total`);
}

void main().catch((error) => {
  console.error(error);
  process.exit(1);
});
