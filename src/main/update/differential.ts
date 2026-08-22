/**
 * CERE-59：把 electron-updater 的差分下载日志翻成可以显示给用户、也可以写进
 * 验收报告的数字。
 *
 * 「支持差分」这句话没有意义，能落到验收上的只有「这次实际下了多少字节」。
 * electron-updater 的 DifferentialDownloader 在算完 block 计划后会打一行
 *
 *   Full: 118,238.55 KB, To download: 3,442.11 KB (3%)
 *
 * 这是它自己按 blockmap 算出来的、真正要走网络的字节数（不含 range 请求头
 * 开销）。我们不另造一套统计，直接认它这行——同一份数字既进界面也进报告。
 */
import type { DifferentialReport } from '../../shared/update';

const SIZE_LINE = /Full:\s*([\d,.]+)\s*KB,\s*To download:\s*([\d,.]+)\s*KB/i;

function parseKilobytes(raw: string): number {
  return Math.round(Number(raw.replace(/,/g, '')) * 1024);
}

/**
 * 从一行日志里解析差分结果。不是那一行就返回 null。
 */
export function parseDifferentialLine(line: string): { fullBytes: number; downloadedBytes: number } | null {
  const match = SIZE_LINE.exec(line);
  if (!match) return null;
  const fullBytes = parseKilobytes(match[1]!);
  const downloadedBytes = parseKilobytes(match[2]!);
  if (!Number.isFinite(fullBytes) || !Number.isFinite(downloadedBytes)) return null;
  return { fullBytes, downloadedBytes };
}

/** electron-updater 放弃差分、回落全量时打的那行。 */
const FALLBACK_LINE = /Cannot download differentially, fallback to full download/i;

export function isDifferentialFallback(line: string): boolean {
  return FALLBACK_LINE.test(line);
}

export function fullDownloadReport(fullBytes: number, reason: string): DifferentialReport {
  return { fullBytes, downloadedBytes: fullBytes, applied: false, reason };
}

export function describeSaving(report: DifferentialReport): string {
  if (!report.applied) return report.reason;
  const saved = report.fullBytes - report.downloadedBytes;
  const percent = report.fullBytes > 0 ? Math.round((saved / report.fullBytes) * 100) : 0;
  return `差分下载生效：完整包 ${formatMegabytes(report.fullBytes)}，实际只取了 ${formatMegabytes(
    report.downloadedBytes,
  )}，省下 ${percent}%。`;
}

export function formatMegabytes(bytes: number): string {
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}
