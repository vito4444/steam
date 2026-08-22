import { describe, expect, it } from 'vitest';

import { completeOnboarding, onboardingPending, ONBOARDING_KEY } from '../src/renderer/src/features/import/onboarding';
import * as importFeedback from '../src/renderer/src/features/import/importFeedback';
import * as candidateReview from '../src/renderer/src/features/import/candidateReview';

const { errorMessage, linkFeedback, photoFeedback } = importFeedback;
const { candidateReasonLine, candidateStateLabel, summarizeCandidates } = candidateReview;

type ManualOutcome =
  | {
    status: 'result';
    result: {
      canceled: boolean;
      imported: number;
      assets: [];
      failures: { file: string; message: string }[];
    };
    refreshFailed: boolean;
  }
  | { status: 'bridge-error'; message: string };

type ManualFeedback = (outcome: ManualOutcome) => {
  tone: 'success' | 'warning' | 'error';
  message: string;
  repair: boolean;
  canOpenWardrobe: boolean;
};

type RunManualImport = (
  invoke: () => Promise<ManualOutcome extends { status: 'result'; result: infer R } ? R : never>,
  refresh: () => Promise<void>,
) => Promise<ManualOutcome>;

class MemoryStorage {
  private readonly values = new Map<string, string>();

  getItem(key: string) {
    return this.values.get(key) ?? null;
  }

  setItem(key: string, value: string) {
    this.values.set(key, value);
  }
}

describe('import-first onboarding', () => {
  it('keeps onboarding pending until this import-first version is completed', () => {
    const storage = new MemoryStorage();

    expect(onboardingPending(storage)).toBe(true);
    completeOnboarding(storage);
    expect(storage.getItem(ONBOARDING_KEY)).toBe('complete');
    expect(onboardingPending(storage)).toBe(false);
  });
});

describe('photo import feedback', () => {
  it('keeps optimization candidates in the wardrobe without calling them rejected', () => {
    expect(photoFeedback({
      imported: 3,
      needsOptimization: 2,
      rejected: 0,
      assets: [],
      candidates: [],
    })).toEqual({
      tone: 'warning',
      message: '已入库 3 件，其中 2 件标记为待优化；可以在下方查看每项指标。',
      repair: false,
      canOpenWardrobe: true,
    });
  });

  it('opens the wardrobe after a successful photo import', () => {
    expect(photoFeedback({ imported: 2, rejected: 0, assets: [] })).toEqual({
      tone: 'success', message: '已导入 2 件到衣橱。', repair: false, canOpenWardrobe: true,
    });
  });

  it('fails closed when every photo candidate is rejected', () => {
    expect(photoFeedback({ imported: 0, rejected: 2, assets: [] })).toEqual({
      tone: 'warning',
      message: '2 个候选未通过质量门，没有静默入库。没通过的没有入库；可以换一张再试，或用页面下方的透明底入口。',
      repair: true,
      canOpenWardrobe: false,
    });
  });

  it('turns thrown photo-import errors into a repairable error', () => {
    expect(photoFeedback(new Error('管线不可用'))).toEqual({
      tone: 'error', message: '管线不可用', repair: true, canOpenWardrobe: false,
    });
  });

  it('preserves a non-empty string error from the producer', () => {
    expect(errorMessage('照片分析进程退出')).toBe('照片分析进程退出');
  });

  it('keeps the producer message and repair guidance when every photo file fails', () => {
    expect(photoFeedback({
      imported: 0,
      rejected: 0,
      assets: [],
      message: 'coat.png：照片分析进程退出',
    })).toEqual({
      tone: 'error',
      message: 'coat.png：照片分析进程退出。没通过的没有入库；可以换一张再试，或用页面下方的透明底入口。',
      repair: true,
      canOpenWardrobe: false,
    });
  });

  it('keeps producer failures alongside mixed accepted and rejected photo results', () => {
    expect(photoFeedback({
      imported: 1,
      rejected: 2,
      assets: [],
      message: 'coat.png：图片损坏',
    })).toEqual({
      tone: 'warning',
      message: '已导入 1 件；2 个候选未通过质量门。coat.png：图片损坏。没通过的没有入库；可以换一张再试，或用页面下方的透明底入口。',
      repair: true,
      canOpenWardrobe: true,
    });
  });
});

describe('candidate quality details', () => {
  it('states the measured value, threshold, and exact excess', () => {
    expect(candidateReasonLine({
      code: 'MASK_STRUCTURE_UNRELIABLE',
      metric: 'input_contour_roughness',
      value: 0.24,
      threshold: 0.05,
      message: 'input mask has structural bites',
    })).toBe('边缘结构：实测 0.240，门槛 ≤ 0.050，超出 0.190');
  });

  it('summarizes all three states without hiding retry candidates', () => {
    const candidates = [
      { state: 'ready' },
      { state: 'needs_optimization' },
      { state: 'retry' },
    ] as Parameters<typeof summarizeCandidates>[0];

    expect(candidateStateLabel('ready')).toBe('合格 · 已入库');
    expect(candidateStateLabel('needs_optimization')).toBe('待优化 · 已入库');
    expect(candidateStateLabel('retry')).toBe('严重失败 · 需重试');
    expect(summarizeCandidates(candidates)).toBe(
      '3 个候选：1 件合格，1 件待优化，1 件需重试',
    );
  });
});

describe('manual import feedback', () => {
  const manualFeedback = (importFeedback as unknown as {
    manualFeedback?: ManualFeedback;
  }).manualFeedback;

  it('reports cancellation without offering a repair loop', () => {
    expect(manualFeedback).toBeTypeOf('function');
    expect(manualFeedback?.({
      status: 'result',
      result: { canceled: true, imported: 0, assets: [], failures: [] },
      refreshFailed: false,
    })).toEqual({
      tone: 'warning', message: '已取消手动导入。', repair: false, canOpenWardrobe: false,
    });
  });

  it('reports full and partial success with wardrobe navigation', () => {
    expect(manualFeedback).toBeTypeOf('function');
    expect(manualFeedback?.({
      status: 'result',
      result: { canceled: false, imported: 2, assets: [], failures: [] },
      refreshFailed: false,
    })).toEqual({
      tone: 'success', message: '已导入 2 件透明素材到衣橱。', repair: false, canOpenWardrobe: true,
    });
    expect(manualFeedback?.({
      status: 'result',
      result: {
        canceled: false,
        imported: 1,
        assets: [],
        failures: [{ file: 'opaque.png', message: '图片没有透明背景' }],
      },
      refreshFailed: false,
    })).toEqual({
      tone: 'warning',
      message: '已导入 1 件；1 个文件未导入（opaque.png：图片没有透明背景）。没通过的没有入库；可以换一张再试，或用页面下方的透明底入口。',
      repair: true,
      canOpenWardrobe: true,
    });
  });

  it('reports full rejection and thrown bridge errors as repairable', () => {
    expect(manualFeedback).toBeTypeOf('function');
    expect(manualFeedback?.({
      status: 'result',
      result: {
        canceled: false,
        imported: 0,
        assets: [],
        failures: [{ file: 'opaque.png', message: '图片没有透明背景' }],
      },
      refreshFailed: false,
    })).toEqual({
      tone: 'error',
      message: '未导入任何文件。opaque.png：图片没有透明背景。没通过的没有入库；可以换一张再试，或用页面下方的透明底入口。',
      repair: true,
      canOpenWardrobe: false,
    });
    expect(manualFeedback?.({ status: 'bridge-error', message: '文件选择器不可用' })).toEqual({
      tone: 'error',
      message: '文件选择器不可用。没通过的没有入库；可以换一张再试，或用页面下方的透明底入口。',
      repair: true,
      canOpenWardrobe: false,
    });
  });

  it('keeps accepted assets truthful and navigable when the visible list refresh fails', async () => {
    const runManualImport = (importFeedback as unknown as {
      runManualImport?: RunManualImport;
    }).runManualImport;
    expect(runManualImport).toBeTypeOf('function');

    const outcome = await runManualImport!(
      async () => ({ canceled: false, imported: 1, assets: [], failures: [] }),
      async () => { throw new Error('list failed'); },
    );

    expect(outcome).toEqual({
      status: 'result',
      result: { canceled: false, imported: 1, assets: [], failures: [] },
      refreshFailed: true,
    });
    expect(manualFeedback?.(outcome)).toEqual({
      tone: 'warning',
      message: '已导入的 1 件素材已经存储，但可见衣橱列表刷新失败；请重启应用后查看。',
      repair: false,
      canOpenWardrobe: true,
    });
  });
});

describe('link import feedback', () => {
  it('opens the wardrobe after a successful link import', () => {
    expect(linkFeedback({
      status: 'ok', imported: 1, assets: [], platform: 'Shopify', message: '已解析商品主图',
    })).toEqual({
      tone: 'success', message: '已从商品链接导入 1 件到衣橱。', repair: false, canOpenWardrobe: true,
    });
  });

  it('fails closed when an ok link import produces no wardrobe assets', () => {
    expect(linkFeedback({
      status: 'ok', imported: 0, assets: [], platform: 'Shopify',
      message: '0 个候选未通过 CERE-12 质量门。',
    })).toEqual({
      tone: 'warning',
      message: '0 个候选未通过 CERE-12 质量门。可以存下商品主图，再用页面下方的透明底入口导入。',
      repair: true,
      canOpenWardrobe: false,
    });
  });

  it('keeps a partial link import repairable while retaining imported assets', () => {
    expect(linkFeedback({
      status: 'partial', imported: 1, assets: [], platform: 'Shopify', message: '部分候选未完成导入',
    })).toEqual({
      tone: 'warning',
      message: '部分候选未完成导入。可以存下商品主图，再用页面下方的透明底入口导入。',
      repair: true,
      canOpenWardrobe: true,
    });
  });

  it('makes failed link imports repairable', () => {
    expect(linkFeedback({
      status: 'failed', imported: 0, assets: [], platform: 'Shopify', message: '下载商品主图失败',
    })).toEqual({
      tone: 'error',
      message: '下载商品主图失败。可以存下商品主图，再用页面下方的透明底入口导入。',
      repair: true,
      canOpenWardrobe: false,
    });
  });

  it('routes manual-required links to the transparent-image repair path', () => {
    expect(linkFeedback({
      status: 'manual_required', imported: 0, assets: [], platform: '淘宝', message: '需要登录后手动选择商品主图',
    })).toEqual({
      tone: 'warning',
      message: '需要登录后手动选择商品主图。可以存下商品主图，再用页面下方的透明底入口导入。',
      repair: true,
      canOpenWardrobe: false,
    });
  });
});
