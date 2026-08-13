using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Monster.EditorTools
{
    /// <summary>
    /// Headless build entry points, invoked from CI with -executeMethod.
    ///
    /// Unity's batch mode will happily exit with code 0 after a failed build, so
    /// every path here ends in an explicit EditorApplication.Exit with a code that
    /// reflects the actual result.
    /// </summary>
    public static class MonsterBuild
    {
        private const string ProductName = "MONSTER";

        public static void BuildWindows64() => RunBuild(BuildTarget.StandaloneWindows64);

        public static void BuildLinux64() => RunBuild(BuildTarget.StandaloneLinux64);

        /// <summary>Builds Windows and Linux in one editor session, which is much
        /// faster than two sessions because the asset database is only imported once.</summary>
        public static void BuildAll()
        {
            var failures = new List<string>();
            foreach (var target in new[] { BuildTarget.StandaloneWindows64, BuildTarget.StandaloneLinux64 })
            {
                if (!TryBuild(target, out var error))
                {
                    failures.Add($"{target}: {error}");
                }
            }

            if (failures.Count > 0)
            {
                Debug.LogError($"[MonsterBuild] {failures.Count} target(s) failed:\n  " +
                               string.Join("\n  ", failures));
                EditorApplication.Exit(1);
                return;
            }

            EditorApplication.Exit(0);
        }

        private static void RunBuild(BuildTarget target)
        {
            EditorApplication.Exit(TryBuild(target, out _) ? 0 : 1);
        }

        private static bool TryBuild(BuildTarget target, out string error)
        {
            error = null;
            var group = BuildPipeline.GetBuildTargetGroup(target);
            var named = UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(group);

            // Mono is the only backend the Linux editor can cross-compile to Windows.
            // The Steam release build has to be produced with IL2CPP on a Windows host;
            // that is a known release gate, documented in docs/tech/build-pipeline.md.
            PlayerSettings.SetScriptingBackend(named, ScriptingImplementation.Mono2x);
            PlayerSettings.SetApiCompatibilityLevel(named, ApiCompatibilityLevel.NET_Unity_4_8);
            PlayerSettings.productName = ProductName;
            PlayerSettings.companyName = "Project MONSTER";
            PlayerSettings.runInBackground = true;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;

            // Scenes, materials and textures are generated from code and deliberately not
            // committed, because regenerating them rewrites every fileID and buries real
            // changes under thousands of lines of churn. A clean clone therefore has no
            // scene until one is generated, so the build generates it rather than failing.
            // The build settings can still list a scene whose file is gone, which is exactly
            // the state a clean clone is in, so existence on disk is what is checked rather
            // than the entry being present.
            var scenes = EnabledScenes();
            if (scenes.Length == 0 || scenes.Any(path => !File.Exists(path)))
            {
                Debug.Log("[MonsterBuild] generated scenes are missing from disk, regenerating");
                NightShiftSceneBuilder.Build();
                scenes = EnabledScenes();
            }

            if (scenes.Length == 0 || scenes.Any(path => !File.Exists(path)))
            {
                error = "no usable scenes after regeneration";
                Debug.LogError($"[MonsterBuild] {error}");
                return false;
            }

            var outputDir = Path.Combine(ProjectRoot(), "Build", FolderFor(target));
            Directory.CreateDirectory(outputDir);
            var outputPath = Path.Combine(outputDir, ProductName + ExtensionFor(target));

            Debug.Log($"[MonsterBuild] building {target} -> {outputPath}\n" +
                      $"  version : {BuildVersion()}\n" +
                      $"  backend : Mono2x\n" +
                      $"  scenes  : {string.Join(", ", scenes.Select(Path.GetFileNameWithoutExtension))}");

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = target,
                targetGroup = group,
                options = BuildOptions.None,
            };

            // The version is stamped for the duration of the build and then put back.
            // Leaving it in ProjectSettings would make every build dirty the working tree
            // with a one-line change that carries no information.
            var previousVersion = PlayerSettings.bundleVersion;
            PlayerSettings.bundleVersion = BuildVersion();

            var stopwatch = Stopwatch.StartNew();
            BuildReport report;
            try
            {
                report = BuildPipeline.BuildPlayer(options);
            }
            finally
            {
                stopwatch.Stop();
                PlayerSettings.bundleVersion = previousVersion;
                AssetDatabase.SaveAssets();
            }

            var summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                error = $"{summary.result} with {summary.totalErrors} error(s)";
                foreach (var step in report.steps)
                {
                    foreach (var message in step.messages)
                    {
                        if (message.type == LogType.Error || message.type == LogType.Exception)
                        {
                            Debug.LogError($"[MonsterBuild]   {step.name}: {message.content}");
                        }
                    }
                }

                Debug.LogError($"[MonsterBuild] FAILED {target}: {error}");
                return false;
            }

            Debug.Log($"[MonsterBuild] OK {target} in {stopwatch.Elapsed.TotalSeconds:F1}s, " +
                      $"{summary.totalSize / (1024f * 1024f):F1} MB -> {outputPath}");
            return true;
        }

        private static string[] EnabledScenes()
        {
            return EditorBuildSettings.scenes
                .Where(s => s.enabled && !string.IsNullOrEmpty(s.path))
                .Select(s => s.path)
                .ToArray();
        }

        /// <summary>Stamps the build with the commit it came from, so any artefact or
        /// screenshot can be traced back to a revision.</summary>
        private static string BuildVersion()
        {
            var sha = RunGit("rev-parse --short HEAD") ?? "nogit";
            var dirty = string.IsNullOrEmpty(RunGit("status --porcelain")) ? "" : "+dirty";
            return $"0.1.0-{sha}{dirty}";
        }

        private static string RunGit(string arguments)
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo("git", arguments)
                    {
                        WorkingDirectory = ProjectRoot(),
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    },
                };
                process.Start();
                var stdout = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(5000);
                return process.ExitCode == 0 ? stdout : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string ProjectRoot() =>
            Path.GetDirectoryName(Application.dataPath);

        private static string FolderFor(BuildTarget target) => target switch
        {
            BuildTarget.StandaloneWindows64 => "Windows",
            BuildTarget.StandaloneLinux64 => "Linux",
            _ => target.ToString(),
        };

        private static string ExtensionFor(BuildTarget target) => target switch
        {
            BuildTarget.StandaloneWindows64 => ".exe",
            BuildTarget.StandaloneLinux64 => ".x86_64",
            _ => string.Empty,
        };
    }
}
