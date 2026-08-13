using System;
using System.IO;
using UnityEngine;

namespace Monster.Shift
{
    /// <summary>Reads and writes the campaign save on disk.
    ///
    /// Writes go to a temporary file which then replaces the real one, because a save is
    /// written at the end of every night and a player who quits the game at the wrong
    /// moment should lose that night rather than the whole campaign.</summary>
    public static class CampaignStore
    {
        public const string FileName = "campaign.json";

        public static string Path => System.IO.Path.Combine(Application.persistentDataPath, FileName);

        public static bool Exists => File.Exists(Path);

        public static void Write(CampaignSave save)
        {
            if (save == null)
            {
                throw new ArgumentNullException(nameof(save));
            }

            var path = Path;
            var directory = System.IO.Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporary = path + ".tmp";
            File.WriteAllText(temporary, save.ToJson());

            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(temporary, path);
        }

        /// <summary>Loads the save, or returns null if there is nothing to load or what is
        /// there cannot be resumed. A save this build cannot read is renamed rather than
        /// deleted: it is somebody's thirty nights, and it costs nothing to keep.</summary>
        public static CampaignSave Read()
        {
            if (!Exists)
            {
                return null;
            }

            try
            {
                var save = CampaignSave.FromJson(File.ReadAllText(Path));

                if (!save.IsConsistent(out var problem))
                {
                    Debug.LogWarning($"[Campaign] the save on disk cannot be resumed: {problem}");
                    SetAside();
                    return null;
                }

                return save;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Campaign] the save on disk could not be read: {exception.Message}");
                SetAside();
                return null;
            }
        }

        public static void Delete()
        {
            if (Exists)
            {
                File.Delete(Path);
            }
        }

        private static void SetAside()
        {
            try
            {
                var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                File.Move(Path, Path + "." + stamp + ".unreadable");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Campaign] the unreadable save could not be set aside: {exception.Message}");
            }
        }
    }
}
