using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Hunter.EditorTools
{
    /// Windows x64 player build. The Linux editor can only produce the Mono backend for
    /// Windows; IL2CPP for Windows needs MSVC and therefore a Windows host. See
    /// docs/02-technical-constraints.md section 3.3 for the release-time options.
    public static class GameBuild
    {
        [MenuItem("Hunter/Build Windows x64")]
        public static void BuildWindows()
        {
            var outDir = Environment.GetEnvironmentVariable("HUNTER_BUILD_DIR") ?? "/workspace/Builds/Windows";
            Directory.CreateDirectory(outDir);

            PlayerSettings.productName = "Hunter: Aurum Mist";
            PlayerSettings.companyName = "Hunter Project";
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;

            var scenes = new[] { "Assets/Scenes/AurumMist.unity" };
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(outDir, "HunterAurumMist.exe"),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };

            var report = UnityEditor.BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            Debug.Log($"GAME_BUILD result={summary.result} sizeBytes={summary.totalSize} " +
                      $"errors={summary.totalErrors} warnings={summary.totalWarnings} time={summary.totalTime}");

            if (summary.result != BuildResult.Succeeded)
            {
                foreach (var step in report.steps)
                {
                    foreach (var msg in step.messages)
                    {
                        if (msg.type == LogType.Error || msg.type == LogType.Exception)
                            Debug.LogError($"GAME_BUILD_ERR {step.name}: {msg.content}");
                    }
                }
                EditorApplication.Exit(4);
            }
        }
    }
}
