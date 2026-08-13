using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Maner.EditorTools
{
    /// <summary>
    /// 命令行构建入口。
    ///   -executeMethod Maner.EditorTools.ManerBuild.Windows64
    ///   -executeMethod Maner.EditorTools.ManerBuild.Linux64
    /// 可选参数：-manerBuildPath &lt;dir&gt;
    /// </summary>
    public static class ManerBuild
    {
        public static void Windows64() => Build(BuildTarget.StandaloneWindows64, "MANER.exe", "build/windows");

        public static void Linux64() => Build(BuildTarget.StandaloneLinux64, "MANER", "build/linux");

        static void Build(BuildTarget target, string executableName, string defaultDir)
        {
            string dir = ReadArg("-manerBuildPath") ?? Path.Combine(Directory.GetCurrentDirectory(), defaultDir);
            Directory.CreateDirectory(dir);

            string[] scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                Console.WriteLine("[ManerBuild] 构建设置里没有启用的场景");
                EditorApplication.Exit(2);
                return;
            }

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(dir, executableName),
                target = target,
                targetGroup = BuildPipeline.GetBuildTargetGroup(target),
                options = BuildOptions.None,
            };

            Console.WriteLine($"[ManerBuild] 目标 {target}，输出 {options.locationPathName}，场景 {scenes.Length} 个");

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            Console.WriteLine(
                $"[ManerBuild] 结果 {summary.result}，" +
                $"用时 {summary.totalTime.TotalSeconds:0.0}s，" +
                $"体积 {summary.totalSize / (1024f * 1024f):0.0} MB，" +
                $"错误 {summary.totalErrors}，警告 {summary.totalWarnings}");

            if (summary.result != BuildResult.Succeeded)
            {
                foreach (var step in report.steps)
                {
                    foreach (var msg in step.messages.Where(m => m.type is LogType.Error or LogType.Exception))
                    {
                        Console.WriteLine($"[ManerBuild] {step.name}: {msg.content}");
                    }
                }
                EditorApplication.Exit(1);
                return;
            }

            EditorApplication.Exit(0);
        }

        static string ReadArg(string key)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == key)
                {
                    return args[i + 1];
                }
            }
            return null;
        }
    }
}
