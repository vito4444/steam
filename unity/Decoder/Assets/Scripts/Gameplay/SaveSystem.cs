using System;
using System.IO;
using UnityEngine;

namespace Decoder.Gameplay
{
    /// <summary>
    /// 存档的磁盘读写。
    ///
    /// 逻辑本身在 CampaignState 里，这一层只负责碰文件系统。
    /// 分开是为了让进度逻辑能在编辑器测试里完整跑一遍而不用真的写盘。
    /// </summary>
    public static class SaveSystem
    {
        private const string FileName = "nightwatch.sav";

        public static string SavePath =>
            Path.Combine(Application.persistentDataPath, FileName);

        /// <summary>
        /// 写盘。先写临时文件再替换，避免在写到一半时断电或崩溃，
        /// 把玩家原本完好的存档也毁掉。
        /// </summary>
        public static bool Save(CampaignState state)
        {
            if (state == null)
            {
                return false;
            }

            try
            {
                var path = SavePath;
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var temp = path + ".tmp";
                File.WriteAllText(temp, state.Serialize());

                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.Move(temp, path);
                return true;
            }
            catch (Exception e)
            {
                // 存档失败不该让游戏停下来。玩家这一班还在进行中，
                // 崩在这里比丢一次进度更糟。
                Debug.LogWarning($"[SaveSystem] 存档写入失败：{e.Message}");
                return false;
            }
        }

        /// <summary>读档。文件不存在或内容损坏都返回新的空进度。</summary>
        public static CampaignState Load()
        {
            try
            {
                var path = SavePath;
                if (!File.Exists(path))
                {
                    return new CampaignState();
                }

                var state = CampaignState.Deserialize(File.ReadAllText(path));
                if (state == null)
                {
                    Debug.LogWarning("[SaveSystem] 存档内容无法解析，按新档处理");
                    return new CampaignState();
                }

                return state;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveSystem] 存档读取失败：{e.Message}");
                return new CampaignState();
            }
        }

        /// <summary>删档。返回是否真的删掉了文件。</summary>
        public static bool Delete()
        {
            try
            {
                var path = SavePath;
                if (!File.Exists(path))
                {
                    return false;
                }

                File.Delete(path);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveSystem] 存档删除失败：{e.Message}");
                return false;
            }
        }
    }
}
