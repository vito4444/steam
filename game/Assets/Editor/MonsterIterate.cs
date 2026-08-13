using UnityEditor;
using UnityEngine;

namespace Monster.EditorTools
{
    /// <summary>
    /// Combined entry point for the visual iteration loop, so that regenerating the scene
    /// and building the player share one editor session. Importing the asset database is
    /// the expensive part of a batch-mode run; doing it once instead of twice roughly
    /// halves the cycle.
    /// </summary>
    public static class MonsterIterate
    {
        public static void RebuildSceneAndBuildLinux()
        {
            try
            {
                NightShiftSceneBuilder.Build();
            }
            catch (System.Exception exception)
            {
                Debug.LogError($"[MonsterIterate] scene generation threw: {exception}");
                EditorApplication.Exit(2);
                return;
            }

            Debug.Log("[MonsterIterate] scene regenerated, building Linux player");
            MonsterBuild.BuildLinux64();
        }

        public static void RebuildSceneAndBuildAll()
        {
            try
            {
                NightShiftSceneBuilder.Build();
            }
            catch (System.Exception exception)
            {
                Debug.LogError($"[MonsterIterate] scene generation threw: {exception}");
                EditorApplication.Exit(2);
                return;
            }

            Debug.Log("[MonsterIterate] scene regenerated, building all targets");
            MonsterBuild.BuildAll();
        }
    }
}
