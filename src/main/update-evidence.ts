/**
 * CERE-59：更新流程的证据模式（`PixelFit.exe --update-evidence`）。
 *
 * 和 `--shots` 同一个原则：截的是**真窗口**，走的是**真流程**。这里不模拟
 * 任何状态——它点的是界面上那几个真按钮（检查更新 / 下载更新 / 重启并安装），
 * 更新源是真的 Releases，下载的是真的安装包，进度条是真的在动。
 * 唯一的区别是「谁来点」：这里是脚本点，用户手点走的是同一条路径。
 *
 * 输出：
 *   01-settings.png     设置页（更新卡片 + 模型资源包卡片）
 *   02-detected.png     检测到新版本的提示（版本号 / 更新说明 / 包大小 / 未签名说明）
 *   03-downloading.png  下载进度
 *   04-ready.png        下载完成，等待重启安装
 *   report.json         这次更新的状态快照，含差分下载的实际字节数
 */
import type { BrowserWindow } from 'electron';
import fsp from 'node:fs/promises';
import path from 'node:path';

const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

async function waitFor(
  win: BrowserWindow,
  label: string,
  expression: string,
  attempts = 600,
): Promise<void> {
  for (let attempt = 0; attempt < attempts; attempt++) {
    const ok = await win.webContents.executeJavaScript(`!!(${expression})`).catch(() => false);
    if (ok) return;
    await sleep(500);
  }
  throw new Error(`update-evidence: timed out waiting for ${label}`);
}

/** 点界面上那个真按钮。找不到就报错，不静默跳过。 */
async function click(win: BrowserWindow, text: string, scope = 'document'): Promise<void> {
  const clicked = await win.webContents.executeJavaScript(`(() => {
    const button = [...${scope}.querySelectorAll('button')]
      .find((b) => b.textContent && b.textContent.includes(${JSON.stringify(text)}) && !b.disabled);
    if (!button) return false;
    button.click();
    return true;
  })()`);
  if (!clicked) throw new Error(`update-evidence: no enabled button matching "${text}"`);
}

/**
 * 首次引导会在素材库就绪后把界面强制切到「导入」页，抢在我们点「设置」之后。
 * 先把它标记成已完成再重载 —— 取证要的是设置页，不是引导页。
 */
async function dismissOnboarding(win: BrowserWindow): Promise<void> {
  const pending = await win.webContents.executeJavaScript(
    "window.localStorage.getItem('pixelfit:onboarding:import-first:v2') !== 'complete'",
  );
  if (!pending) return;
  await win.webContents.executeJavaScript(
    "window.localStorage.setItem('pixelfit:onboarding:import-first:v2', 'complete')",
  );
  win.webContents.reload();
  await waitFor(win, 'reloaded shell', "document.querySelector('.rail-btn')");
  await sleep(1_500);
}

async function openSettings(win: BrowserWindow): Promise<void> {
  await waitFor(win, 'app shell', "document.querySelector('.rail-btn')");
  // 素材库还在载入时截图，设置页的路径和计数是「…」和 0 —— 那不是应用的真实样子。
  await waitFor(
    win,
    'library ready',
    "!(document.querySelector('.titlebar .meta')?.textContent || '').includes('正在载入')",
  );
  await sleep(1_200);
  await dismissOnboarding(win);
  await click(win, '设置');
  await waitFor(win, 'settings view', "document.querySelector('[data-testid=\"update-card\"]')");
  // 引导切页是在素材库就绪之后才触发的，多等一拍再确认停在设置页。
  await sleep(1_200);
  const onSettings = await win.webContents.executeJavaScript(
    "!!document.querySelector('[data-testid=\"update-card\"]')",
  );
  if (!onSettings) throw new Error('update-evidence: settings view did not stay open');
}

async function shoot(win: BrowserWindow, dir: string, name: string): Promise<void> {
  const image = await win.webContents.capturePage();
  await fsp.writeFile(path.join(dir, name), image.toPNG());
  console.log('[update-evidence]', name);
}

export async function runUpdateEvidence(win: BrowserWindow, outDir: string): Promise<void> {
  await fsp.mkdir(outDir, { recursive: true });
  await openSettings(win);
  await shoot(win, outDir, '01-settings.png');

  // 检查更新 —— 真的向 GitHub Releases 要 latest.yml
  await click(win, '立即检查更新');
  await waitFor(win, 'update dialog', "document.querySelector('[data-testid=\"update-dialog\"]')");
  await sleep(500);
  await shoot(win, outDir, '02-detected.png');

  // 下载 —— 差分下载就发生在这一步
  await click(win, '下载更新');
  await waitFor(
    win,
    'download progress',
    "document.querySelector('[data-testid=\"update-progress\"]') || document.querySelector('[data-testid=\"update-ready\"]')",
  );
  await sleep(400);
  await shoot(win, outDir, '03-downloading.png');

  await waitFor(win, 'download complete', "document.querySelector('[data-testid=\"update-ready\"]')");
  await sleep(500);
  await shoot(win, outDir, '04-ready.png');

  const report = await win.webContents.executeJavaScript('window.pixelfit.update.state()');
  await fsp.writeFile(path.join(outDir, 'report.json'), `${JSON.stringify(report, null, 2)}\n`, 'utf8');
  console.log('[update-evidence] differential', JSON.stringify(report?.differential));

  if (process.env['PIXELFIT_UPDATE_INSTALL'] === '1') {
    console.log('[update-evidence] clicking 重启并安装');
    await click(win, '重启并安装');
    // quitAndInstall 会自己退出进程；这里只是别让脚本抢在前面 exit。
    await sleep(30_000);
  }
}

/**
 * 装完之后再跑一次，证明版本号真的变了。
 * 单独一个模式，因为它必须在**新装上的那个 exe** 里跑。
 */
export async function runVersionProof(win: BrowserWindow, outDir: string): Promise<void> {
  await fsp.mkdir(outDir, { recursive: true });
  await openSettings(win);
  await shoot(win, outDir, '05-installed.png');
  const state = await win.webContents.executeJavaScript('window.pixelfit.update.state()');
  await fsp.writeFile(
    path.join(outDir, 'installed.json'),
    `${JSON.stringify(state, null, 2)}\n`,
    'utf8',
  );
  console.log('[update-evidence] running version', state?.currentVersion);
}

/**
 * 模型资源包的取证：未下载 → 下载中 → 已就绪。
 * 这是「安装包为什么能从 496 MB 降到 184 MB」那半条需求的可见证据。
 */
export async function runModelPackEvidence(win: BrowserWindow, outDir: string): Promise<void> {
  await fsp.mkdir(outDir, { recursive: true });
  await openSettings(win);
  await win.webContents.executeJavaScript(
    "document.querySelector('[data-testid=\"model-pack-card\"]').scrollIntoView({block:'center'})",
  );
  await sleep(600);
  await shoot(win, outDir, '06-models-missing.png');

  await click(win, '下载识别模型');
  await waitFor(win, 'model download progress', "document.querySelector('[data-testid=\"model-pack-progress\"]')");
  await sleep(2_500);
  await shoot(win, outDir, '07-models-downloading.png');

  await waitFor(
    win,
    'model download complete',
    "[...document.querySelectorAll('button')].some((b) => b.textContent && b.textContent.includes('模型已就绪'))",
    1_200,
  );
  await sleep(800);
  await shoot(win, outDir, '08-models-ready.png');
  const state = await win.webContents.executeJavaScript('window.pixelfit.modelPack.state()');
  await fsp.writeFile(
    path.join(outDir, 'model-pack.json'),
    `${JSON.stringify(state, null, 2)}
`,
    'utf8',
  );
}
