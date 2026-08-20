/**
 * 截图脚本的渲染侧钩子（只在 `electron . --shots` 下被调用）。
 *
 * 每个场景把界面摆成一个真实可达的状态，主进程随后对真窗口 capturePage。
 * 不做任何「示意图」式的假渲染 —— 截出来的必须是应用真的长这样。
 *
 * 场景按品类挑素材而不是按名字：衣橱里装的是哪批真实素材，截图脚本不该关心。
 */

import type { Category, Slot } from '@shared/spec';
import type { Tuck } from '@shared/occlusion';
import {
  SHOT_RECIPES,
  resolveRecipeAssetId,
  type ShotCheck,
} from '@shared/shots';
import { capture, stageHandles } from '@/features/stage/handles';
import { boardShotHooks } from '@/features/board/shotHooks';
import { ONBOARDING_KEY } from '@/features/import/onboarding';
import { resolveShotAssets } from '@/shotAssets';

type Store = ReturnType<typeof import('@/state/store').useStore>;

let latest: Store | null = null;
let registered = false;

const wait = (ms: number) => new Promise((r) => setTimeout(r, ms));

/** 按素材 id 精确穿上（对比图要可复现，不能靠「第一件」碰运气） */
async function wearIds(ids: string[]): Promise<void> {
  const s = latest;
  if (!s) throw new Error('shot store is not ready');
  for (const asset of resolveShotAssets(ids, s.assets)) {
    s.dispatch({ type: 'wear', asset });
    const worn = await waitUntil(() => latest?.outfit.slots[asset.slot] === asset.id);
    if (!worn) throw new Error(`required recipe asset did not reach slot ${asset.slot}: ${asset.id}`);
  }
}

/** 场景里的断言结果：渲染进程的 console 不会进主进程日志，攒起来让主进程收走 */
function assertCheck(label: string, condition: boolean, detail?: string): void {
  const result: ShotCheck = { label, passed: condition, ...(detail ? { detail } : {}) };
  const w = window as unknown as { __pixelfitChecks?: ShotCheck[] };
  (w.__pixelfitChecks ??= []).push(result);
  console.log('[check]', result);
}

const buttonWithText = (text: string): HTMLButtonElement | undefined =>
  [...document.querySelectorAll('button')]
    .find((button) => button.textContent?.includes(text)) as HTMLButtonElement | undefined;

async function waitUntil(predicate: () => boolean, attempts = 30): Promise<boolean> {
  for (let attempt = 0; attempt < attempts; attempt++) {
    if (predicate()) return true;
    await wait(100);
  }
  return false;
}

const importedFixture = () => latest?.assets.find(
  (asset) => !asset.source.demo && asset.name === '黑白条纹上衣 + 皮夹克',
);

function fixtureCard(): HTMLButtonElement | undefined {
  return [...document.querySelectorAll<HTMLButtonElement>('.wardrobe .card')]
    .find((card) => card.title.includes('黑白条纹上衣 + 皮夹克') && card.querySelector('img'));
}

function filterWardrobeForImportEvidence(): void {
  const input = document.querySelector<HTMLInputElement>('.wardrobe .search input');
  if (!input) return;
  const valueSetter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set;
  valueSetter?.call(input, '黑');
  input.dispatchEvent(new Event('input', { bubbles: true }));
}

/** AI 预览和画板不依赖某一版素材包的固定 id，按品类挑第一件。 */
async function wearOnePer(categories: Category[]): Promise<void> {
  const s = latest;
  if (!s) return;
  for (const category of categories) {
    const asset = s.assets.find((candidate) => candidate.category === category);
    if (asset) {
      s.dispatch({ type: 'wear', asset });
      await wait(80);
    }
  }
}

/** CERE-24 accepts one isolated real-window outfit per garment relationship. */
async function resetFitAcceptance(): Promise<boolean> {
  const s = latest;
  if (!s) return false;
  s.setView('wardrobe');
  s.setStageTab('try');
  s.setSelectedSlot(null);
  s.clearCompare();
  s.dispatch({ type: 'clear' });
  s.dispatch({ type: 'set', patch: { noOcclusion: false, rawCompositing: false } });
  await wait(120);
  return true;
}

const SCENES: Record<string, () => Promise<void>> = {
  async onboarding() {
    latest?.setView('import');
    await wait(300);

    const guide = document.querySelector('[data-testid="first-run-guide"]');
    const photoCta = buttonWithText('拍照 / 选图导入');
    const linkCta = buttonWithText('粘贴商品链接');
    const skipCta = buttonWithText('先看看示例');
    assertCheck('首次引导使用真实空 localStorage', window.localStorage.getItem(ONBOARDING_KEY) === null);
    assertCheck('首次引导容器可见', !!guide);
    assertCheck('照片导入 CTA 可见', !!photoCta);
    assertCheck('商品链接 CTA 可见', !!linkCta);
    assertCheck('先看看示例操作可用', !!skipCta && !skipCta.disabled);
  },

  async import() {
    latest?.setView('import');
    await wait(300);
    const heading = [...document.querySelectorAll('.view h2')]
      .find((element) => element.textContent?.includes('导入素材'));
    assertCheck('已完成 onboarding 后导入流程可见', !!heading);
    assertCheck('稳定导入流程不再显示首次引导', !document.querySelector('[data-testid="first-run-guide"]'));
  },

  async 'imported-wardrobe'() {
    const s = latest;
    if (!s) {
      assertCheck('真实导入素材已加载', false, 'store 未就绪');
      return;
    }
    const fixture = importedFixture();
    assertCheck('真实导入素材已加载', !!fixture, '黑白条纹上衣 + 皮夹克');
    const demoCount = s.assets.filter((asset) => asset.source.demo).length;
    assertCheck('内置示例数量保持 8 件', demoCount === 8, `实际 ${demoCount} 件`);

    s.setView('wardrobe');
    s.setStageTab('try');
    s.dispatch({ type: 'clear' });
    await wait(200);
    filterWardrobeForImportEvidence();
    await wait(300);
    const card = fixture ? fixtureCard() : undefined;
    const visibleDemoBadges = document.querySelectorAll('.wardrobe .card .demo-badge');
    assertCheck('真实导入素材卡片可见', !!card);
    assertCheck('衣橱中至少一个示例徽标可见', visibleDemoBadges.length > 0, `实际 ${visibleDemoBadges.length} 个`);
    assertCheck('导入衣橱场景已清空穿着状态', Object.keys(latest?.outfit.slots ?? {}).length === 0);
  },

  async 'imported-worn'() {
    const s = latest;
    const fixture = importedFixture();
    if (!s || !fixture) {
      assertCheck('真实导入素材可试穿', false, '黑白条纹上衣 + 皮夹克缺失');
      return;
    }
    s.setView('wardrobe');
    s.setStageTab('try');
    s.dispatch({ type: 'clear' });
    await wait(120);
    s.wear(fixture);
    const worn = await waitUntil(() => latest?.outfit.slots[fixture.slot] === fixture.id);
    await wait(500);
    assertCheck('真实导入素材已通过正常 store action 试穿', worn);
    assertCheck(
      `预期槽位 ${fixture.slot} 包含真实导入 ID`,
      latest?.outfit.slots[fixture.slot] === fixture.id,
      fixture.id,
    );
    const card = fixtureCard();
    assertCheck('真实导入素材卡片显示穿着中', !!card?.querySelector('.badge'));
  },

  async main() {
    latest?.setView('wardrobe');
    latest?.setStageTab('try');
    latest?.dispatch({ type: 'clear' });
    await wait(120);
    await wearIds([...SHOT_RECIPES.main]);
    await wait(700);
  },

  async dressing() {
    latest?.setView('wardrobe');
    latest?.dispatch({ type: 'clear' });
    await wait(120);
    await wearIds([...SHOT_RECIPES.dressing]);
    latest?.setStageTab('layers');
    latest?.setSelectedSlot('outer' as Slot);
    await wait(700);
  },

  /** 处理前 / 处理后：关掉羽化与接触阴影的原始叠图 */
  async raw() {
    latest?.dispatch({ type: 'set', patch: { rawCompositing: true } });
    await wait(500);
  },

  async processed() {
    latest?.dispatch({ type: 'set', patch: { rawCompositing: false } });
    await wait(500);
  },

  async layers() {
    latest?.setStageTab('layers');
    latest?.setSelectedSlot('top' as Slot);
    await wait(400);
  },

  async fit() {
    latest?.setStageTab('fit');
    latest?.setSelectedSlot('top' as Slot);
    await wait(500);
  },

  /** 连衣裙与鞋：不混入上下装、外套或配饰。 */
  async fit_dress() {
    if (!await resetFitAcceptance()) return;
    await wearIds(['dress_1087', 'shoes_269']);
    await wait(700);
  },

  /** 上装、下装和外套：只验证三层衣物的结构顺序。 */
  async fit_layered() {
    if (!await resetFitAcceptance()) return;
    await wearIds(['top_014', 'bottom_102', 'outerwear_075', 'shoes_268']);
    await wait(700);
  },

  /** 上下装加围脖：单独验证颈部配饰的安全区域。 */
  async fit_accessory() {
    if (!await resetFitAcceptance()) return;
    await wearIds(['top_014', 'bottom_102', 'accessory_401', 'shoes_268']);
    await wait(700);
  },

  /** 简单上下装配黑色短靴：保留双靴重叠这一源图限制。 */
  async fit_shoes() {
    if (!await resetFitAcceptance()) return;
    await wearIds(['top_014', 'bottom_102', 'shoes_269']);
    await wait(700);
  },

  /** 上下装：不混入连衣裙、外套或配饰。 */
  async fit_separates() {
    if (!await resetFitAcceptance()) return;
    await wearIds(['top_393', 'bottom_102', 'shoes_295']);
    await wait(700);
  },

  /** 遮挡关 / 开：右侧面板停在贴合页，能同时看到生效中的规则清单 */
  async occlusion_off() {
    const s = latest;
    if (!s) return;
    s.clearCompare();
    s.dispatch({ type: 'clear' });
    await wait(120);
    await wearIds([...SHOT_RECIPES.occlusion]);
    s.setStageTab('fit');
    s.setSelectedSlot('neckwear' as Slot);
    s.dispatch({ type: 'set', patch: { noOcclusion: true } });
    await wait(700);
  },

  async occlusion_on() {
    latest?.dispatch({ type: 'set', patch: { noOcclusion: false } });
    await wait(700);
  },

  async compare() {
    latest?.setStageTab('try');
    latest?.setSelectedSlot(null);
    latest?.dispatch({ type: 'clear' });
    await wait(120);
    await wearIds([...SHOT_RECIPES.compareFirst]);
    await wait(500);
    latest?.addToCompare();
    await wait(200);
    latest?.dispatch({ type: 'clear' });
    await wait(120);
    await wearIds([...SHOT_RECIPES.compareSecond]);
    await wait(700);
  },

  /** 画板：自动布局后的默认状态 —— 每件单品互不遮挡 */
  async board() {
    const s = latest;
    if (!s) return;
    s.setView('board');
    await wait(150);
    boardShotHooks.reset?.();
    await wait(120);
    boardShotHooks.fill?.(['top', 'bottom', 'shoe', 'bag'], s.assets);
    await wait(700);
  },

  /** 画板编辑态：加标题、选中一件，露出选择框 / 删除 / 旋转手柄 */
  async board_edit() {
    const s = latest;
    if (!s) return;
    s.setView('board');
    boardShotHooks.addTitle?.();
    await wait(300);
    boardShotHooks.selectFirst?.('top');
    await wait(600);
  },

  /**
   * 真窗口里跑一遍拖拽：合成 pointer 事件打到画板元素上，
   * 拖完看坐标有没有真的变。截图只能证明「画出来了」，证明不了「拖得动」。
   */
  async board_drag() {
    latest?.setView('board');
    await wait(200);
    const el = document.querySelector('.board-item') as HTMLElement | null;
    if (!el) {
      assertCheck('画板拖拽元素存在', false, '画板上没有元素');
      return;
    }
    const r = el.getBoundingClientRect();
    const from = { x: r.left + r.width / 2, y: r.top + r.height / 2 };
    const before = el.getBoundingClientRect().left;
    const opts = (x: number, y: number) => ({ clientX: x, clientY: y, bubbles: true, pointerId: 1 });
    el.dispatchEvent(new PointerEvent('pointerdown', opts(from.x, from.y)));
    await wait(60);
    window.dispatchEvent(new PointerEvent('pointermove', opts(from.x - 90, from.y + 60)));
    await wait(80);
    window.dispatchEvent(new PointerEvent('pointermove', opts(from.x - 130, from.y + 90)));
    await wait(80);
    window.dispatchEvent(new PointerEvent('pointerup', opts(from.x - 130, from.y + 90)));
    await wait(200);
    const moved = el.getBoundingClientRect().left - before;
    assertCheck('画板元素可拖拽', Math.abs(moved + 130) <= 12, `实际位移 ${moved.toFixed(1)}px`);
    // 撤销一次，验证撤销栈接住了这次拖拽
    latest && (window as unknown as { __pfUndo?: () => void }).__pfUndo?.();
    await wait(300);
    const afterUndo = el.getBoundingClientRect().left - before;
    assertCheck('画板拖拽可撤销', Math.abs(afterUndo) <= 2, `残余位移 ${afterUndo.toFixed(1)}px`);
    await wait(300);
  },

  /** 换背景，顺带证明背景是可换的 */
  async board_dark() {
    boardShotHooks.setBackground?.('linen');
    await wait(500);
  },

  /** 导出成图：把画板真的渲成一张图，贴在界面上截 */
  async board_export() {
    boardShotHooks.setBackground?.('paper_warm');
    await wait(200);
    await boardShotHooks.preview?.();
    await wait(700);
  },

  async looks() {
    const s = latest;
    if (!s) return;
    s.clearCompare();
    s.dispatch({ type: 'clear' });
    await wait(100);
    await wearIds([...SHOT_RECIPES.looksFirst]);
    await wait(500);
    // 必须用 latest 而不是场景开头抓到的 s：saveLook 闭包里带着当时的 outfit
    await latest?.saveLook('周三通勤', await capture(0.3, false));
    latest?.dispatch({ type: 'clear' });
    await wait(100);
    await wearIds([...SHOT_RECIPES.looksSecond]);
    await wait(500);
    await latest?.saveLook('周末约会', await capture(0.3, false));
    // 画板 Look：封面就是拼贴成图，带场景标签
    await boardShotHooks.saveLook?.('周三通勤画板', ['work']);
    await wait(300);
    boardShotHooks.reset?.();
    await wait(120);
    boardShotHooks.fill?.(['dress', 'shoe', 'bag'], latest?.assets ?? []);
    await wait(500);
    await boardShotHooks.saveLook?.('周末约会画板', ['date', 'casual']);
    await wait(300);
    await latest?.refresh();
    latest?.setView('looks');
    await wait(600);
  },

  /** Look 库按场景筛选 */
  async looks_filter() {
    latest?.setView('looks');
    await wait(200);
    const chip = [...document.querySelectorAll('.view .filter-row .chip')]
      .find((el) => el.textContent?.startsWith('工作')) as HTMLButtonElement | undefined;
    chip?.click();
    await wait(500);
  },

  async base() {
    latest?.setView('base');
    await wait(400);
  },

  async settings() {
    latest?.setView('settings');
    await wait(400);
  },

  async 'ai-settings'() {
    latest?.setView('settings');
    latest?.setEngineId('vton');
    await wait(500);
    const select = document.querySelector('.provider-controls select') as HTMLSelectElement | null;
    if (select) {
      select.value = 'aliyun';
      select.dispatchEvent(new Event('change', { bubbles: true }));
    }
    await wait(300);
  },

  async 'ai-preview'() {
    latest?.setView('wardrobe');
    latest?.clearCompare();
    latest?.setEngineId('vton');
    latest?.dispatch({ type: 'clear' });
    await wait(120);
    await wearOnePer(['top', 'bottom', 'outer', 'shoe']);
    await wait(500);
  },

  async 'ai-consent'() {
    latest?.setView('wardrobe');
    latest?.setEngineId('vton');
    await wait(300);
    (window as unknown as { __pixelfitCloudConsent?: () => void }).__pixelfitCloudConsent?.();
    await wait(300);
  },

  /** 真的清空素材库，截真实空状态 */
  async empty() {
    const s = latest;
    if (!s) return;
    buttonWithText('取消')?.click();
    const consentClosed = await waitUntil(() => !document.querySelector('.cloud-consent'));
    assertCheck('空衣橱截图不继承 AI 上传确认框', consentClosed);
    s.setView('wardrobe');
    s.setEngineId('layered');
    s.dispatch({ type: 'clear' });
    await window.pixelfit.library.resetLibrary();
    await s.refresh();
    await wait(600);
  },
};

const AFTER_SCENES: Record<string, () => Promise<void>> = {
  async onboarding() {
    const skipCta = buttonWithText('先看看示例');
    skipCta?.click();
    const completed = await waitUntil(() => (
      window.localStorage.getItem(ONBOARDING_KEY) !== null
      && !document.querySelector('[data-testid="first-run-guide"]')
    ));
    assertCheck('应用操作写入版本化 onboarding key', completed);
  },
};

/**
 * 对比图取样口：摆好一套搭配，按指定开关渲染，回一张 1:1 的舞台 PNG。
 *
 * 走的是**应用真正的渲染路径**（同一个 TryOnEngine、同一份规则表），
 * 只是把画面单独导出来拼图，不是另画一套示意图。
 */
export interface FigureSpec {
  ids: string[];
  noOcc?: boolean;
  raw?: boolean;
  tuck?: Record<string, Tuck>;
}

async function renderFigure(spec: FigureSpec): Promise<string | null> {
  const s = latest;
  if (!s) throw new Error('figure store is not ready');
  s.setView('wardrobe');
  const stageReady = await waitUntil(() => (
    !!document.querySelector('.stage-wrap')
    && !!stageHandles.engine.current
    && !!stageHandles.input.current
  ));
  if (!stageReady) throw new Error('figure stage did not mount');
  s.clearCompare();
  s.setSelectedSlot(null);
  s.dispatch({ type: 'clear' });
  await wait(140);
  await wearIds(spec.ids);
  for (const [id, value] of Object.entries(spec.tuck ?? {})) {
    latest?.dispatch({ type: 'setTuck', assetId: resolveRecipeAssetId(id), value });
  }
  latest?.dispatch({
    type: 'set',
    patch: { noOcclusion: !!spec.noOcc, rawCompositing: !!spec.raw, background: 'none' },
  });
  const renderReady = await waitUntil(() => (stageHandles.input.current?.worn.length ?? 0) > 0);
  if (!renderReady) throw new Error('required figure render has no worn assets');
  await wait(400);
  return capture(1, true);
}

export function registerShotHook(store: Store): () => void {
  latest = store;
  if (!registered) {
    registered = true;
    // 主进程要等素材库真的载完再开拍，否则第一张会拍到空衣橱
    (window as unknown as { __pixelfitState: () => { ready: boolean; assets: number } })
      .__pixelfitState = () => ({ ready: !!latest?.ready, assets: latest?.assets.length ?? 0 });
    (window as unknown as { __pixelfitShot: (n: string) => Promise<void> }).__pixelfitShot =
      async (name: string) => {
        const fn = SCENES[name];
        if (!fn) throw new Error(`unknown shot scene: ${name}`);
        await fn();
      };
    (window as unknown as { __pixelfitShotAfter: (n: string) => Promise<void> }).__pixelfitShotAfter =
      async (name: string) => {
        const fn = AFTER_SCENES[name];
        if (fn) await fn();
      };
    (window as unknown as { __pixelfitFigure: (s: FigureSpec) => Promise<string | null> })
      .__pixelfitFigure = renderFigure;
  }
  return () => {
    /* store 更新时只替换引用，不注销钩子 */
  };
}
