/** CERE-59：更新相关的本地开关。独立成文件，不混进云试穿的 settings.json。 */
import fs from 'node:fs';
import fsp from 'node:fs/promises';
import path from 'node:path';

export interface UpdatePreferences {
  /**
   * 启动时静默检查一次。
   *
   * 默认**开**：成员提这个需求就是为了不再手动去 Releases 页面下载，
   * 默认关等于功能不存在。检查只发一个 GET 取版本信息，不上传任何本地数据，
   * 也不会自动下载或自动安装——发现新版只是弹一个提示，下不下载由用户点。
   * 不想让它联网的人在设置页一键关掉，关掉之后应用不会因为更新访问网络。
   */
  checkOnLaunch: boolean;
  /** 上次检查完成时间，ISO 字符串 */
  lastCheckedAt: string | null;
}

export const DEFAULT_UPDATE_PREFERENCES: UpdatePreferences = {
  checkOnLaunch: true,
  lastCheckedAt: null,
};

function coerce(raw: unknown): UpdatePreferences {
  if (!raw || typeof raw !== 'object') return { ...DEFAULT_UPDATE_PREFERENCES };
  const record = raw as Record<string, unknown>;
  return {
    checkOnLaunch:
      typeof record['checkOnLaunch'] === 'boolean'
        ? record['checkOnLaunch']
        : DEFAULT_UPDATE_PREFERENCES.checkOnLaunch,
    lastCheckedAt: typeof record['lastCheckedAt'] === 'string' ? record['lastCheckedAt'] : null,
  };
}

export class UpdatePreferencesRepository {
  private cache: UpdatePreferences | null = null;

  constructor(private readonly file: string) {}

  load(): UpdatePreferences {
    if (this.cache) return this.cache;
    try {
      this.cache = coerce(JSON.parse(fs.readFileSync(this.file, 'utf8')));
    } catch {
      // 文件不存在或损坏都不该拦住启动：回到默认值，下次保存时自愈。
      this.cache = { ...DEFAULT_UPDATE_PREFERENCES };
    }
    return this.cache;
  }

  async save(patch: Partial<UpdatePreferences>): Promise<UpdatePreferences> {
    const next = { ...this.load(), ...patch };
    this.cache = next;
    await fsp.mkdir(path.dirname(this.file), { recursive: true });
    await fsp.writeFile(this.file, `${JSON.stringify(next, null, 2)}\n`, 'utf8');
    return next;
  }
}
