import type { ErrorCode } from './types.js';

export class LinkImportError extends Error {
  readonly code: ErrorCode;
  readonly retryable: boolean;

  constructor(code: ErrorCode, message: string, retryable = false) {
    super(message);
    this.name = 'LinkImportError';
    this.code = code;
    this.retryable = retryable;
  }
}

/** 未知异常统一收口成 LinkImportError，调用方只需要处理一种错误类型 */
export function toLinkImportError(err: unknown): LinkImportError {
  if (err instanceof LinkImportError) return err;
  const msg = err instanceof Error ? err.message : String(err);
  // undici 把超时/断连都塞进 cause，name 才是可靠信号
  if (err instanceof Error && (err.name === 'AbortError' || err.name === 'TimeoutError')) {
    return new LinkImportError('NETWORK_ERROR', `请求超时：${msg}`, true);
  }
  if (/fetch failed|ENOTFOUND|ECONNRESET|ECONNREFUSED|EAI_AGAIN|socket hang up/i.test(msg)) {
    return new LinkImportError('NETWORK_ERROR', `网络请求失败：${msg}`, true);
  }
  return new LinkImportError('INTERNAL_ERROR', msg, false);
}

/** 面向用户的中文文案。UI 直接显示这个，不要显示 code。 */
export const ERROR_MESSAGES: Record<ErrorCode, string> = {
  INVALID_URL: '这不像一个网页链接，请检查后重新粘贴。',
  UNSUPPORTED_PLATFORM: '这个网站还没适配，可以直接把商品图拖进来。',
  NETWORK_ERROR: '网络没连上，检查网络后重试。',
  HTTP_ERROR: '对方网站没能正常返回，稍后再试或直接拖图。',
  ANTIBOT_BLOCKED: '该平台需要登录后才能看到商品图。可以在应用内打开页面手动取图，或直接把图片拖进来。',
  NO_IMAGE_FOUND: '页面里没找到商品主图，请手动拖图或粘贴截图。',
  IMAGE_DOWNLOAD_FAILED: '商品图下载失败，请重试或手动拖图。',
  IMAGE_REJECTED: '这张图太小或体积异常，做成素材效果会很差，换一张更大的图。',
  INTERNAL_ERROR: '导入出错了，请重试。',
};
