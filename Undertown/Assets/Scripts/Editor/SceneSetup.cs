using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using Undertown.Game.Bootstrap;

namespace Undertown.EditorTools
{
    /// <summary>
    /// Rebuilds the playable scene and the player settings from scratch. On a headless
    /// machine there is no editor window to drag objects into, so the scene is an output of
    /// this script rather than a hand-authored asset - which also means it can be recreated
    /// identically at any time.
    /// </summary>
    public static class SceneSetup
    {
        private const string ScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("Undertown/Setup/Rebuild Project")]
        public static void RebuildProject()
        {
            ApplyPlayerSettings();
            RebuildMainScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[setup] project rebuilt");
        }

        public static void RebuildMainScene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var bootstrap = new GameObject("GameBootstrap");
            bootstrap.AddComponent<GameBootstrap>();
            bootstrap.AddComponent<ScreenshotHarness>();

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new BuildFailedException($"failed to save {ScenePath}");

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log($"[setup] scene rebuilt at {ScenePath}");
        }

        public static void ApplyPlayerSettings()
        {
            PlayerSettings.productName = "Undertown";
            PlayerSettings.companyName = "Undertown";
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;

            // Unity 6 lets a Personal licence turn the splash off; a paid storefront product
            // should not open with an engine advertisement.
            PlayerSettings.SplashScreen.show = false;

            PlayerSettings.colorSpace = ColorSpace.Linear;
            QualitySettings.vSyncCount = 1;

            Debug.Log("[setup] player settings applied");
        }
    }
}
