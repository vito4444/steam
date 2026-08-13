using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Undertown.EditorTools
{
    /// <summary>
    /// Headless entry points for CI. Every build runs from a Linux host, so the Windows
    /// player uses the Mono scripting backend - IL2CPP for Windows requires an MSVC
    /// toolchain and therefore a Windows machine.
    /// </summary>
    public static class BuildScript
    {
        private const string ProductName = "Undertown";

        [MenuItem("Undertown/Build/Windows x64")]
        public static void BuildWindows() => Run(BuildTarget.StandaloneWindows64, "build/windows/Undertown.exe");

        [MenuItem("Undertown/Build/Linux x64")]
        public static void BuildLinux() => Run(BuildTarget.StandaloneLinux64, "build/linux/Undertown.x86_64");

        private static void Run(BuildTarget target, string relativeOutput)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)!.Parent!.FullName;
            string output = Path.Combine(projectRoot, relativeOutput);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);

            string[] scenes = EnabledScenes();
            if (scenes.Length == 0)
                throw new BuildFailedException("no scenes are enabled in the build settings");

            var group = BuildPipeline.GetBuildTargetGroup(target);
            var named = NamedBuildTarget.FromBuildTargetGroup(group);
            PlayerSettings.SetScriptingBackend(named, ScriptingImplementation.Mono2x);
            PlayerSettings.productName = ProductName;
            PlayerSettings.companyName = "Undertown";

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = target,
                targetGroup = group,
                options = BuildOptions.StrictMode,
            };

            Debug.Log($"[build] {target} -> {output}");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            Debug.Log($"[build] result={summary.result} size={summary.totalSize / (1024 * 1024)}MB " +
                      $"errors={summary.totalErrors} warnings={summary.totalWarnings} time={summary.totalTime}");

            if (summary.result != BuildResult.Succeeded)
                throw new BuildFailedException($"{target} build finished with {summary.result}");

            if (!File.Exists(output))
                throw new BuildFailedException($"build reported success but {output} is missing");
        }

        private static string[] EnabledScenes() =>
            EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
    }
}
