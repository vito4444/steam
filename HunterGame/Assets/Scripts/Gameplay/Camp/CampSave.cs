using System;
using System.IO;
using UnityEngine;

namespace Hunter.Gameplay.Camp
{
    /// Save/load for camp progress.
    ///
    /// Writes through a temporary file and then moves it into place. A raid ends the moment
    /// a player dies, and that is exactly when someone is most likely to alt-F4 in disgust;
    /// a half-written save at that moment would cost them everything they had banked over
    /// hours, which is a far worse failure than losing one run.
    public static class CampSave
    {
        public const string FileName = "camp.json";

        public static string DefaultPath => Path.Combine(Application.persistentDataPath, FileName);

        public static void Save(CampState state, string path = null)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            path ??= DefaultPath;

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var json = JsonUtility.ToJson(state.ToSaveData(), prettyPrint: true);
            var temporary = path + ".tmp";

            File.WriteAllText(temporary, json);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temporary, path);
        }

        public static bool TryLoad(CampState state, string path = null)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            path ??= DefaultPath;

            if (!File.Exists(path)) return false;

            try
            {
                var data = JsonUtility.FromJson<CampState.SaveData>(File.ReadAllText(path));
                if (data == null) return false;
                state.LoadFrom(data);
                return true;
            }
            catch (Exception e)
            {
                // A corrupt save must not brick the game. Losing progress is bad; being
                // unable to launch at all is worse.
                Debug.LogWarning($"camp save at {path} could not be read: {e.Message}");
                return false;
            }
        }

        public static void Delete(string path = null)
        {
            path ??= DefaultPath;
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
