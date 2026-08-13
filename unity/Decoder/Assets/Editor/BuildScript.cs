using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Decoder.EditorTools
{
    /// <summary>
    /// 命令行构建入口。所有参数通过 -buildTarget / -buildOutput / -buildScenes 传入，
    /// 缺省时使用项目约定值，保证 CI 与本地行为一致。
    /// </summary>
    public static class BuildScript
    {
        private const string DefaultOutputRoot = "artifacts/build";

        public static void BuildWindows64()
        {
            Build(BuildTarget.StandaloneWindows64, "Decoder.exe");
        }

        /// <summary>
        /// 构建可玩场景。
        ///
        /// 场景必须由前一个编辑器进程生成好，这里只负责构建。曾经试过在同一进程里
        /// 先生成场景再构建，结果构建出的 level0 在运行时报 corrupted 直接崩溃：
        /// 生成期在内存里新建的网格与刚写盘的场景文件之间状态不一致，
        /// 而 BuildPlayer 读的是内存里那一份。构建过程不会报任何错。
        /// 拆成两个进程，第二个进程从磁盘干净地加载场景，问题消失。
        /// </summary>
        public static void BuildStationWindows64()
        {
            Build(BuildTarget.StandaloneWindows64, "Decoder.exe",
                new[] { ProbeSceneBuilder.StationScenePath });
        }

        public static void BuildStationLinux64()
        {
            Build(BuildTarget.StandaloneLinux64, "Decoder",
                new[] { ProbeSceneBuilder.StationScenePath });
        }

        public static void BuildLinux64()
        {
            Build(BuildTarget.StandaloneLinux64, "Decoder");
        }

        private static void Build(BuildTarget target, string executableName,
            string[] explicitScenes = null)
        {
            var scenes = explicitScenes ?? ResolveScenes();
            if (scenes.Length == 0)
            {
                Fail("没有可构建的场景。请在 Build Settings 中启用场景，或通过 -buildScenes 指定。");
                return;
            }

            var outputDir = ResolveOutputDir(target);
            Directory.CreateDirectory(outputDir);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(outputDir, executableName),
                target = target,
                targetGroup = BuildPipeline.GetBuildTargetGroup(target),
                options = ResolveBuildOptions(),
            };

            Log($"开始构建 target={target} scenes={scenes.Length} output={options.locationPathName}");
            foreach (var scene in scenes)
            {
                Log($"  场景: {scene}");
            }

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            Log($"构建结束 result={summary.result} size={summary.totalSize} bytes " +
                $"time={summary.totalTime} errors={summary.totalErrors} warnings={summary.totalWarnings}");

            if (summary.result != BuildResult.Succeeded)
            {
                foreach (var step in report.steps)
                {
                    foreach (var message in step.messages.Where(m =>
                                 m.type == LogType.Error || m.type == LogType.Exception))
                    {
                        Log($"  [{step.name}] {message.content}");
                    }
                }

                Fail($"构建失败: {summary.result}");
                return;
            }

            WriteBuildManifest(outputDir, target, summary);
            Log("BUILD_SUCCESS");
            EditorApplication.Exit(0);
        }

        private static string[] ResolveScenes()
        {
            var explicitScenes = GetArg("-buildScenes");
            if (!string.IsNullOrEmpty(explicitScenes))
            {
                return explicitScenes.Split(',')
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 0)
                    .ToArray();
            }

            var enabled = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            if (enabled.Length > 0)
            {
                return enabled;
            }

            // Build Settings 为空时回落到工程内全部场景，避免 CI 因未提交 EditorBuildSettings 而空转。
            return AssetDatabase.FindAssets("t:Scene", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToArray();
        }

        private static string ResolveOutputDir(BuildTarget target)
        {
            var explicitOutput = GetArg("-buildOutput");
            if (!string.IsNullOrEmpty(explicitOutput))
            {
                return Path.GetFullPath(explicitOutput);
            }

            var projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            var repoRoot = Directory.GetParent(Directory.GetParent(projectRoot)!.FullName)!.FullName;
            return Path.Combine(repoRoot, DefaultOutputRoot, target.ToString());
        }

        private static BuildOptions ResolveBuildOptions()
        {
            var options = BuildOptions.None;
            if (HasFlag("-developmentBuild"))
            {
                options |= BuildOptions.Development;
            }

            return options;
        }

        private static void WriteBuildManifest(string outputDir, BuildTarget target, BuildSummary summary)
        {
            var manifest =
                $"target={target}\n" +
                $"unityVersion={Application.unityVersion}\n" +
                $"buildTimeUtc={DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}\n" +
                $"totalSizeBytes={summary.totalSize}\n" +
                $"totalErrors={summary.totalErrors}\n" +
                $"totalWarnings={summary.totalWarnings}\n";
            File.WriteAllText(Path.Combine(outputDir, "build-manifest.txt"), manifest);
        }

        private static string GetArg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name)
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        private static bool HasFlag(string name)
        {
            return Environment.GetCommandLineArgs().Contains(name);
        }

        private static void Log(string message)
        {
            Debug.Log($"[BuildScript] {message}");
            Console.WriteLine($"[BuildScript] {message}");
        }

        private static void Fail(string message)
        {
            Debug.LogError($"[BuildScript] {message}");
            Console.Error.WriteLine($"[BuildScript] {message}");
            EditorApplication.Exit(1);
        }
    }
}
