import type { LinkImportResult, ManualImportResult, PhotoImportResult } from '@shared/ipc';

export interface ImportFeedback {
  tone: 'success' | 'warning' | 'error';
  message: string;
  repair: boolean;
  canOpenWardrobe: boolean;
}

const repairGuide = '请使用透明 PNG/WebP 手动修补入口。';
const linkRepairGuide = '请保存商品主图为透明 PNG/WebP 后，使用手动修补入口。';

export const errorMessage = (error: unknown) => {
  if (error instanceof Error && error.message.trim()) return error.message;
  if (typeof error === 'string' && error.trim()) return error;
  return '导入未完成，请重试或使用手动修补入口。';
};

const sentence = (message: string) => {
  const trimmed = message.trim();
  return trimmed && !/[。！？.!?]$/.test(trimmed) ? `${trimmed}。` : trimmed;
};

const withPhotoRepair = (...messages: (string | undefined)[]) =>
  `${messages.filter((message): message is string => !!message?.trim()).map(sentence).join('')}${repairGuide}`;

const errorFeedback = (error: unknown): ImportFeedback => ({
  tone: 'error',
  message: errorMessage(error),
  repair: true,
  canOpenWardrobe: false,
});

const repairMessage = (message: string) =>
  `${message}${message.endsWith('。') ? '' : '。'}${linkRepairGuide}`;

export const photoFeedback = (result: PhotoImportResult | unknown): ImportFeedback => {
  if (result instanceof Error || !result || typeof result !== 'object') return errorFeedback(result);

  const photo = result as PhotoImportResult;
  if (photo.canceled) {
    return { tone: 'warning', message: '已取消导入。', repair: false, canOpenWardrobe: false };
  }
  if (photo.imported > 0 && photo.rejected === 0 && !photo.message) {
    return {
      tone: 'success',
      message: `已导入 ${photo.imported} 件到衣橱。`,
      repair: false,
      canOpenWardrobe: true,
    };
  }
  if (photo.imported > 0) {
    const message = withPhotoRepair(
      photo.rejected > 0
        ? `已导入 ${photo.imported} 件；${photo.rejected} 个候选未通过质量门`
        : `已导入 ${photo.imported} 件到衣橱`,
      photo.message,
    );
    return { tone: 'warning', message, repair: true, canOpenWardrobe: true };
  }
  return {
    tone: photo.rejected > 0 ? 'warning' : 'error',
    message: withPhotoRepair(
      photo.rejected > 0
        ? `${photo.rejected} 个候选未通过质量门，没有静默入库`
        : undefined,
      photo.message,
    ),
    repair: true,
    canOpenWardrobe: false,
  };
};

export type ManualImportOutcome =
  | { status: 'result'; result: ManualImportResult; refreshFailed: boolean }
  | { status: 'bridge-error'; message: string };

export async function runManualImport(
  invoke: () => Promise<ManualImportResult>,
  refresh: () => Promise<void>,
): Promise<ManualImportOutcome> {
  let result: ManualImportResult;
  try {
    result = await invoke();
  } catch (error) {
    return { status: 'bridge-error', message: errorMessage(error) };
  }

  if (result.imported > 0) {
    try {
      await refresh();
    } catch {
      return { status: 'result', result, refreshFailed: true };
    }
  }
  return { status: 'result', result, refreshFailed: false };
}

const manualFailureDetails = (result: ManualImportResult) =>
  result.failures.map((failure) => `${failure.file}：${failure.message}`).join('；');

export function manualFeedback(outcome: ManualImportOutcome): ImportFeedback {
  if (outcome.status === 'bridge-error') {
    return {
      tone: 'error',
      message: withPhotoRepair(outcome.message),
      repair: true,
      canOpenWardrobe: false,
    };
  }

  const { result, refreshFailed } = outcome;
  if (result.canceled) {
    return { tone: 'warning', message: '已取消手动导入。', repair: false, canOpenWardrobe: false };
  }
  if (refreshFailed && result.imported > 0) {
    const partial = result.failures.length > 0
      ? `另有 ${result.failures.length} 个文件未导入（${manualFailureDetails(result)}）`
      : undefined;
    return {
      tone: 'warning',
      message: partial
        ? withPhotoRepair(
          `已导入的 ${result.imported} 件素材已经存储，但可见衣橱列表刷新失败；请重启应用后查看`,
          partial,
        )
        : `已导入的 ${result.imported} 件素材已经存储，但可见衣橱列表刷新失败；请重启应用后查看。`,
      repair: result.failures.length > 0,
      canOpenWardrobe: true,
    };
  }
  if (result.imported > 0 && result.failures.length === 0) {
    return {
      tone: 'success',
      message: `已导入 ${result.imported} 件透明素材到衣橱。`,
      repair: false,
      canOpenWardrobe: true,
    };
  }
  if (result.imported > 0) {
    return {
      tone: 'warning',
      message: withPhotoRepair(
        `已导入 ${result.imported} 件；${result.failures.length} 个文件未导入（${manualFailureDetails(result)}）`,
      ),
      repair: true,
      canOpenWardrobe: true,
    };
  }
  return {
    tone: 'error',
    message: withPhotoRepair(`未导入任何文件。${manualFailureDetails(result)}`),
    repair: true,
    canOpenWardrobe: false,
  };
}

export const linkFeedback = (result: LinkImportResult | unknown): ImportFeedback => {
  if (result instanceof Error || !result || typeof result !== 'object') return errorFeedback(result);

  const link = result as LinkImportResult;
  switch (link.status) {
    case 'ok':
      if (link.imported === 0) {
        return {
          tone: 'warning',
          message: repairMessage(link.message || '未导入任何素材，已拦截'),
          repair: true,
          canOpenWardrobe: false,
        };
      }
      return {
        tone: 'success',
        message: `已从商品链接导入 ${link.imported} 件到衣橱。`,
        repair: false,
        canOpenWardrobe: link.imported > 0,
      };
    case 'partial':
      return {
        tone: 'warning',
        message: repairMessage(link.message || `已导入 ${link.imported} 件，部分候选未完成导入`),
        repair: true,
        canOpenWardrobe: link.imported > 0,
      };
    case 'manual_required':
      return {
        tone: 'warning',
        message: repairMessage(link.message || '无法自动取得商品主图'),
        repair: true,
        canOpenWardrobe: false,
      };
    case 'failed':
      return {
        tone: 'error',
        message: repairMessage(link.message || '链接导入未完成'),
        repair: true,
        canOpenWardrobe: false,
      };
    default:
      return errorFeedback(link);
  }
};
