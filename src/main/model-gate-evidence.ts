/**
 * CERE-64：模型缺失 → 就地下载 → 自动续跑导入 这条链路的证据模式
 * （`PixelFit.exe --model-gate-evidence`）。
 *
 * 和 `--update-evidence` 同一个原则：截的是**真窗口**，走的是**真流程**。
 * 这里不模拟任何状态 —— 模型资源包目录是空的，点的是界面上那几个真按钮，
 * 382 MB 是真的从 GitHub Releases 下下来的，续跑的那次导入是真的把照片
 * 喂给本地管线抠出来入库的。唯一的区别是「谁来点」。
 *
 * 输出：
 *   01-first-run.png     首启引导（一次性 382 MB 的说明写在这里）
 *   02-gate.png          点「选择照片并自动识别」当场弹出的下载说明
 *   03-downloading.png   下载进度（速度 / 剩余时间 / 可取消）
 *   04-auto-continue.png 下完自动继续原本的导入，导入完成回执
 *   05-wardrobe.png      素材真的进了衣橱
 *   report.json          前后素材数、模型资源包状态、版本号
 */
import type { BrowserWindow } from 'electron';
import fsp from 'node:fs/promises';
import path from 'node:path';

const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

async function waitFor(
  win: BrowserWindow,
  label: string,
  expression: string,
  attempts = 2_400,
): Promise<void> {
  for (let attempt = 0; attempt < attempts; attempt++) {
    const ok = await win.webContents.executeJavaScript(`!!(${expression})`).catch(() => false);
    if (ok) return;
    await sleep(500);
  }
  throw new Error(`model-gate-evidence: timed out waiting for ${label}`);
}

/** 点界面上那个真按钮。找不到就报错，不静默跳过。 */
async function click(win: BrowserWindow, text: string): Promise<void> {
  const clicked = await win.webContents.executeJavaScript(`(() => {
    const button = [...document.querySelectorAll('button')]
      .find((b) => b.textContent && b.textContent.includes(${JSON.stringify(text)}) && !b.disabled);
    if (!button) return false;
    button.click();
    return true;
  })()`);
  if (!clicked) throw new Error(`model-gate-evidence: no enabled button matching "${text}"`);
}

async function shoot(win: BrowserWindow, dir: string, name: string): Promise<void> {
  const image = await win.webContents.capturePage();
  await fsp.writeFile(path.join(dir, name), image.toPNG());
  console.log('[model-gate-evidence]', name);
}

export async function runModelGateEvidence(win: BrowserWindow, outDir: string): Promise<void> {
  await fsp.mkdir(outDir, { recursive: true });
  await waitFor(win, 'app shell', "document.querySelector('.rail-btn')");
  await waitFor(
    win,
    'library ready',
    "!(document.querySelector('.titlebar .meta')?.textContent || '').includes('正在载入')",
  );
  await sleep(1_500);

  // 首启引导：382 MB 一次性下载这件事必须在这里就说清楚，而不是用到才发现。
  await waitFor(win, 'first-run guide', "document.querySelector('[data-testid=\"first-run-guide\"]')");
  await shoot(win, outDir, '01-first-run.png');

  const before = await win.webContents.executeJavaScript('window.pixelfit.library.stats()');

  // 用户点的是「导入这张照片」—— 模型缺失就在这里当场解决，不把他赶去设置页。
  await click(win, '选择照片并自动识别');
  await waitFor(win, 'model gate', "document.querySelector('[data-testid=\"model-gate\"]')");
  await sleep(600);
  await shoot(win, outDir, '02-gate.png');

  await click(win, '立即下载并继续导入');
  await waitFor(
    win,
    'download progress',
    "document.querySelector('[data-testid=\"model-gate-progress\"]')",
  );
  await sleep(4_000);
  await shoot(win, outDir, '03-downloading.png');

  /*
   * 下完之后弹窗自己关掉、原本那次导入自己接着跑 —— 这一步没有任何用户动作。
   * 所以这里只等结果出现，不点任何东西：等到了就证明「自动续跑」是真的。
   * 382 MB 加上一次本地抠图，给足 40 分钟。
   */
  await waitFor(
    win,
    'auto-continued import result',
    "document.querySelector('[data-testid=\"import-feedback\"]')",
    4_800,
  );
  await sleep(800);
  await shoot(win, outDir, '04-auto-continue.png');

  await click(win, '去衣橱试穿');
  await waitFor(win, 'wardrobe grid', "document.querySelector('.grid')");
  await sleep(1_200);
  await shoot(win, outDir, '05-wardrobe.png');

  const after = await win.webContents.executeJavaScript('window.pixelfit.library.stats()');
  const modelPack = await win.webContents.executeJavaScript('window.pixelfit.modelPack.state()');
  const pipeline = await win.webContents.executeJavaScript('window.pixelfit.pipeline.status()');
  const version = await win.webContents.executeJavaScript('window.pixelfit.app.version()');
  const report = {
    version,
    assetsBefore: before?.assets ?? null,
    assetsAfter: after?.assets ?? null,
    modelPack,
    pipeline,
  };
  await fsp.writeFile(path.join(outDir, 'report.json'), `${JSON.stringify(report, null, 2)}\n`, 'utf8');
  console.log('[model-gate-evidence] report', JSON.stringify(report.pipeline));
}
