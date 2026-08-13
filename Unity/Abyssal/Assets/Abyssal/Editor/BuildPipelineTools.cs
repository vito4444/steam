using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Abyssal.EditorTools
{
    /// <summary>
    /// 从命令行产出可发行构建。
    ///
    ///   xvfb-run -a -s "-screen 0 1920x1080x24" $UNITY -batchmode -nographics -quit \
    ///     -projectPath Unity/Abyssal -executeMethod Abyssal.EditorTools.BuildPipelineTools.BuildWindows
    ///
    /// Windows 用 Mono 后端：Linux 版编辑器无法交叉编译 IL2CPP（那需要 MSVC）。
    /// Mono 完全可以正式发行，代价是运行时性能略低、程序集更容易被反编译。
    /// </summary>
    public static class BuildPipelineTools
    {
        const string SceneDir = "Assets/Abyssal/Scenes";
        const string BootScenePath = SceneDir + "/Boot.unity";
        const string ResourcesDir = "Assets/Abyssal/Resources";

        static string OutputRoot =>
            Environment.GetEnvironmentVariable("ABYSSAL_BUILD") ?? "/tmp/abyssal/build";

        /// <summary>
        /// 生成启动场景。场景里只有 CabinBootstrap，舱室在运行时搭建。
        /// 这样场景文件只有几 KB，而不是把上百张程序化贴图序列化进去的五十兆。
        /// </summary>
        [MenuItem("Abyssal/Build/Create Boot Scene")]
        public static void CreateBootScene()
        {
            SetupUrp.Run();
            EnsureProfileInResources();

            Directory.CreateDirectory(SceneDir);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var go = new GameObject("Bootstrap");
            go.AddComponent<Abyssal.CabinBootstrap>();

            EditorSceneManager.SaveScene(scene, BootScenePath);

            var buildScenes = new[] { new EditorBuildSettingsScene(BootScenePath, true) };
            EditorBuildSettings.scenes = buildScenes;

            Debug.Log($"ABYSSAL: boot scene saved to {BootScenePath} " +
                      $"({new FileInfo(BootScenePath).Length} bytes)");
        }

        /// <summary>把后处理配置放进 Resources，让运行时能加载到和编辑器一致的调色。</summary>
        static void EnsureProfileInResources()
        {
            Directory.CreateDirectory(ResourcesDir);
            const string src = "Assets/Abyssal/Settings/AbyssalProfile.asset";
            const string dst = ResourcesDir + "/AbyssalProfile.asset";

            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(dst) != null) return;
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(src) == null)
            {
                CabinSceneTools.BuildScene();
            }

            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(src) != null)
            {
                AssetDatabase.CopyAsset(src, dst);
                AssetDatabase.SaveAssets();
            }
        }

        [MenuItem("Abyssal/Build/Windows x64")]
        public static void BuildWindows()
            => Build(BuildTarget.StandaloneWindows64, "windows-x64", "Abyssal.exe");

        [MenuItem("Abyssal/Build/Linux x64")]
        public static void BuildLinux()
            => Build(BuildTarget.StandaloneLinux64, "linux-x64", "Abyssal.x86_64");

        /// <summary>两个平台一起出。Linux 版只用于本机自动化测试，不对外发行。</summary>
        public static void BuildAll()
        {
            BuildWindows();
            BuildLinux();
        }

        static void Build(BuildTarget target, string folder, string executable)
        {
            CreateBootScene();
            ConfigurePlayerSettings();

            string location = Path.Combine(OutputRoot, folder, executable);
            Directory.CreateDirectory(Path.GetDirectoryName(location) ?? OutputRoot);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { BootScenePath },
                locationPathName = location,
                target = target,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;

            Debug.Log($"ABYSSAL: {target} build result={s.result} " +
                      $"size={s.totalSize} errors={s.totalErrors} warnings={s.totalWarnings} " +
                      $"time={s.totalTime.TotalSeconds:F1}s output={location}");

            if (s.result != BuildResult.Succeeded)
            {
                Debug.LogError($"ABYSSAL: {target} build failed");
                EditorApplication.Exit(3);
            }
        }

        static void ConfigurePlayerSettings()
        {
            PlayerSettings.companyName = "Coder Studio";
            PlayerSettings.productName = "ABYSSAL";
            PlayerSettings.bundleVersion = "0.1.0";

            // Mono 是 Linux 编辑器唯一能交叉编译到 Windows 的后端。
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Standalone,
                ApiCompatibilityLevel.NET_Standard);

            // 玩家机器上优先走 D3D11，兼容性最好；OpenGLCore 作为后备。
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[]
            {
                UnityEngine.Rendering.GraphicsDeviceType.Direct3D11,
                UnityEngine.Rendering.GraphicsDeviceType.OpenGLCore,
            });

            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneLinux64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneLinux64, new[]
            {
                UnityEngine.Rendering.GraphicsDeviceType.OpenGLCore,
            });

            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;

            // 舱内是密闭空间，不需要动态分辨率或多显示器支持。
            PlayerSettings.allowFullscreenSwitch = true;
            PlayerSettings.captureSingleScreen = false;
            PlayerSettings.usePlayerLog = true;
            PlayerSettings.stripEngineCode = false;
        }

        /// <summary>
        /// 构建产物的完整性检查。
        /// 本机没有 wine，Windows 产物无法实际运行验证，所以至少要确认
        /// 该有的文件都在、体积合理、没有把调试符号打进去。
        /// </summary>
        [MenuItem("Abyssal/Build/Verify Windows Artifact")]
        public static void VerifyWindowsArtifact()
        {
            string dir = Path.Combine(OutputRoot, "windows-x64");
            if (!Directory.Exists(dir))
            {
                Debug.LogError($"ABYSSAL: 构建目录不存在 {dir}");
                EditorApplication.Exit(4);
                return;
            }

            string[] required =
            {
                "Abyssal.exe",
                "UnityPlayer.dll",
                "Abyssal_Data/globalgamemanagers",
                "Abyssal_Data/Managed/Abyssal.Core.dll",
                "Abyssal_Data/Managed/Abyssal.Runtime.dll",
                "MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll",
            };

            bool ok = true;
            foreach (var rel in required)
            {
                string full = Path.Combine(dir, rel);
                bool exists = File.Exists(full);
                long size = exists ? new FileInfo(full).Length : 0;
                Debug.Log($"ABYSSAL: {(exists ? "OK  " : "缺失")} {rel} {(exists ? size + " bytes" : "")}");
                if (!exists) ok = false;
            }

            long total = 0;
            int fileCount = 0;
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                total += new FileInfo(f).Length;
                fileCount++;
            }
            Debug.Log($"ABYSSAL: 产物合计 {fileCount} 个文件，{total / 1024 / 1024} MB");

            // 发行版里不该出现调试符号。
            int pdbCount = 0;
            foreach (var f in Directory.EnumerateFiles(dir, "*.pdb", SearchOption.AllDirectories)) pdbCount++;
            if (pdbCount > 0)
            {
                Debug.LogWarning($"ABYSSAL: 产物里有 {pdbCount} 个 .pdb 调试符号文件");
            }

            if (!ok)
            {
                Debug.LogError("ABYSSAL: 产物完整性检查未通过");
                EditorApplication.Exit(5);
            }
            else
            {
                Debug.Log("ABYSSAL: 产物完整性检查通过");
            }
        }
    }
}
