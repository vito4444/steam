/**
 * CERE-59：应用内更新与识别模型资源包的共享契约。
 *
 * 更新与模型包是两条独立的下载通道，故意不合并：
 * 应用本体每版都变、几十 MB，走 electron-updater 的差分下载；
 * 识别模型 382 MB 但几乎不变，只在 models.lock.json 改动时才重下一次。
 * 把它们放进同一个安装包，就是成员现在每次都要下 500 MB 的原因。
 */

/** 安装形态。portable 走不了 NSIS 的原地覆盖安装，必须单独给出路。 */
export type InstallKind = 'nsis' | 'portable' | 'dev';

export type UpdatePhase =
  | 'idle'
  /** 正在向 GitHub 要版本信息 */
  | 'checking'
  /** 已是最新 */
  | 'current'
  /** 有新版本，等用户决定 */
  | 'available'
  | 'downloading'
  /** 安装包已就位，等用户重启 */
  | 'ready'
  | 'error'
  /** 检查更新被用户关掉了 */
  | 'disabled';

export interface UpdateInfoView {
  version: string;
  /** Release 正文；GitHub provider 给的是 Markdown 原文 */
  notes: string | null;
  releasedAt: string | null;
  /** 安装包字节数；latest.yml 里没写就是 null */
  bytes: number | null;
  /** 这一版的 Release 页面，Portable 用户和「手动下载」都指向它 */
  releaseUrl: string;
}

export interface UpdateProgress {
  percent: number;
  transferred: number;
  total: number;
  bytesPerSecond: number;
}

export interface UpdateState {
  phase: UpdatePhase;
  currentVersion: string;
  install: InstallKind;
  /** 启动时静默检查一次；关掉之后应用不会因为更新访问网络 */
  checkOnLaunch: boolean;
  info: UpdateInfoView | null;
  progress: UpdateProgress | null;
  error: string | null;
  /** 上次检查完成时间（ISO），从没查过就是 null */
  lastCheckedAt: string | null;
  /**
   * 差分下载实际省了多少。electron-updater 只在**上一版也是通过更新装的**
   * 时候才有本地旧安装包可比对，首次接入这一版必然是全量。
   */
  differential: DifferentialReport | null;
}

export interface DifferentialReport {
  /** 新安装包完整大小 */
  fullBytes: number;
  /** 这次实际从网络取的字节数 */
  downloadedBytes: number;
  /** false = 没有可比对的旧安装包，退回全量下载 */
  applied: boolean;
  reason: string;
}

export interface UpdateSettingsPatch {
  checkOnLaunch?: boolean;
}

// ---------------------------------------------------------------- 模型资源包

export type ModelPackPhase = 'unknown' | 'missing' | 'downloading' | 'verifying' | 'ready' | 'error';

export interface ModelPackFile {
  name: string;
  bytes: number;
  /** 本机已就位并校验通过 */
  present: boolean;
}

export interface ModelPackState {
  phase: ModelPackPhase;
  /** 模型落盘位置，写在设置页里让用户知道这 382 MB 在哪 */
  dir: string;
  files: ModelPackFile[];
  totalBytes: number;
  downloadedBytes: number;
  error: string | null;
  /**
   * true = 模型仍然打在安装包里（旧版布局或开发环境），
   * 用户不需要下载任何东西。
   */
  bundled: boolean;
}
