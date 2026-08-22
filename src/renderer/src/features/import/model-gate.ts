/**
 * CERE-64：模型缺失时，导入动作该怎么走。
 *
 * 0.4.4 把 382 MB 的 ONNX 权重从安装包里拆了出去，代价是自动抠图从「开箱即用」
 * 变成了「先去设置页点一次下载」。用户不知道有这一步，于是点了「从照片导入」
 * 什么也没发生 —— 反馈原话是「目前还是识别不到，无法自动抠图」。
 *
 * 这里把判断收成一个纯函数：**缺模型不是把按钮变灰的理由**，而是就地补一次
 * 下载、下完继续用户原本的动作。真正该拦下来的只有一种情况：本地识别运行时
 * 根本没装 —— 那时候下多少模型都没用，得如实说明并指向手动透明底入口。
 */
import type { PipelineStatus } from '@shared/ipc';
import type { ModelPackState } from '@shared/update';

export type GateDecision =
  /** 模型就绪，直接干活 */
  | 'run'
  /** 只差模型：弹下载说明，下完自动继续 */
  | 'gate'
  /** 运行时没装或状态还没读出来：下载解决不了，不要给用户假希望 */
  | 'unavailable';

export function gateDecision(
  pipeline: PipelineStatus | null,
  pack: ModelPackState | null,
): GateDecision {
  if (!pipeline) return 'unavailable';
  if (pipeline.automatic) return 'run';
  if (!pipeline.installed) return 'unavailable';
  // 运行时在、自动识别不可用，几乎只可能是模型没就位；资源包状态能读到就更确定。
  if (!pack || pack.bundled) return 'gate';
  return pack.phase === 'ready' ? 'run' : 'gate';
}

export type ModelStatusTone = 'ok' | 'warn' | 'busy' | 'error';

export interface ModelStatusView {
  tone: ModelStatusTone;
  /** 一眼可见的短标签：已就绪 / 未下载 / 下载中 63% */
  label: string;
  detail: string;
}

/**
 * 衣橱空态和导入页都要显示这个。
 *
 * 「未下载」必须写成一件**能立刻解决**的事，而不是一句故障描述 ——
 * 用户看到的下一步是点按钮，不是去别处找开关。
 */
export function modelStatus(
  pipeline: PipelineStatus | null,
  pack: ModelPackState | null,
): ModelStatusView {
  if (!pipeline) return { tone: 'warn', label: '正在检测…', detail: '正在读取本地识别状态。' };
  if (pipeline.automatic) {
    return {
      tone: 'ok',
      label: '识别模型已就绪',
      detail: '选图后在本机识别衣物、自动抠图，不上传、不联网。',
    };
  }
  if (!pipeline.installed) {
    return { tone: 'error', label: '本地识别不可用', detail: pipeline.message };
  }
  if (pack?.phase === 'downloading' || pack?.phase === 'verifying') {
    return {
      tone: 'busy',
      label: pack.phase === 'verifying' ? '正在校验模型…' : `正在下载模型 ${percentOf(pack)}%`,
      detail: '下载完成后自动抠图立刻可用，这一步只需要做一次。',
    };
  }
  if (pack?.phase === 'error' && pack.error) {
    return { tone: 'error', label: '模型下载失败', detail: pack.error };
  }
  return {
    tone: 'warn',
    label: '识别模型未下载',
    detail: '自动抠图需要一次性下载 382 MB 模型；点「从照片导入」时会当场提示下载，下完自动继续。',
  };
}

export function percentOf(pack: ModelPackState | null): number {
  if (!pack || pack.totalBytes <= 0) return 0;
  return Math.min(100, Math.round((pack.downloadedBytes / pack.totalBytes) * 100));
}

/** 剩余时间。估不出来就不显示，不编一个假的数字。 */
export function etaText(seconds: number | null | undefined): string {
  if (seconds == null || !Number.isFinite(seconds) || seconds <= 0) return '';
  if (seconds < 60) return `约还需 ${Math.max(1, Math.round(seconds))} 秒`;
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `约还需 ${minutes} 分钟`;
  const hours = Math.floor(minutes / 60);
  return `约还需 ${hours} 小时 ${minutes % 60} 分钟`;
}

export function speedText(bytesPerSecond: number | null | undefined): string {
  if (bytesPerSecond == null || !Number.isFinite(bytesPerSecond) || bytesPerSecond <= 0) return '';
  return `${(bytesPerSecond / 1024 / 1024).toFixed(1)} MB/s`;
}
