using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Worker.Editor
{
    /// <summary>
    /// Command line build entry points.
    ///
    /// Unity can only build one target per process, so each of these is invoked in its
    /// own Editor run with a matching -buildTarget. Every entry point exits the process
    /// with a non-zero code on failure, because a batch-mode build that fails quietly is
    /// worse than one that never ran.
    ///
    ///   Unity -batchmode -nographics -quit -projectPath . \
    ///         -buildTarget Win64 \
    ///         -executeMethod Worker.Editor.BuildPipelineEntry.BuildWindows64 \
    ///         -logFile -
    /// </summary>
    public static class BuildPipelineEntry
    {
        private const string ProductExecutableName = "Worker";

        private const string BootstrapScenePath = "Assets/Scenes/Bootstrap.unity";

        /// <summary>
        /// The player ships exactly one, deliberately empty, scene.
        /// <see cref="Worker.Game.Bootstrap"/> constructs the whole object graph at
        /// runtime from a <c>RuntimeInitializeOnLoadMethod</c>, so there is nothing to
        /// author in the scene asset. Unity still requires a scene to build, so this
        /// creates one when it is missing rather than depending on a file that has to be
        /// kept in sync by hand.
        ///
        /// Entries pointing at files that do not exist are dropped: a stale path in
        /// EditorBuildSettings fails the build with a confusing message.
        /// </summary>
        private static string[] ResolveScenes()
        {
            var configured = EditorBuildSettings.scenes
                .Where(scene => scene.enabled && File.Exists(scene.path))
                .Select(scene => scene.path)
                .ToArray();

            if (configured.Length > 0) return configured;

            return new[] { EnsureBootstrapScene() };
        }

        private static string EnsureBootstrapScene()
        {
            if (File.Exists(BootstrapScenePath)) return BootstrapScenePath;

            Directory.CreateDirectory(Path.GetDirectoryName(BootstrapScenePath));

            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);

            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, BootstrapScenePath);
            AssetDatabase.Refresh();

            Debug.Log("[worker] created empty bootstrap scene at " + BootstrapScenePath);
            return BootstrapScenePath;
        }

        [MenuItem("Worker/Build/Windows 64")]
        public static void BuildWindows64()
        {
            Run(BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone,
                Path.Combine(OutputRoot(), "Windows64", ProductExecutableName + ".exe"));
        }

        [MenuItem("Worker/Build/Linux 64")]
        public static void BuildLinux64()
        {
            Run(BuildTarget.StandaloneLinux64, BuildTargetGroup.Standalone,
                Path.Combine(OutputRoot(), "Linux64", ProductExecutableName + ".x86_64"));
        }

        /// <summary>
        /// Player used by the automated self-test.
        ///
        /// Deliberately not a development build: Unity's development console opens over
        /// the game on the first warning, and a machine with no audio device warns during
        /// engine startup, which would cover the bottom of every captured frame. The
        /// on-screen readout is enabled with --hud instead.
        /// </summary>
        [MenuItem("Worker/Build/Linux 64 (self-test)")]
        public static void BuildLinux64SelfTest()
        {
            Run(BuildTarget.StandaloneLinux64, BuildTargetGroup.Standalone,
                Path.Combine(OutputRoot(), "Linux64-selftest", ProductExecutableName + ".x86_64"));
        }

        private static string OutputRoot()
        {
            string fromArgs = ReadArgument("-workerBuildOutput");
            if (!string.IsNullOrEmpty(fromArgs)) return fromArgs;

            return Path.Combine(Directory.GetCurrentDirectory(), "Artifacts", "build");
        }

        private static void Run(BuildTarget target, BuildTargetGroup group, string outputPath,
            BuildOptions options = BuildOptions.None)
        {
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            // Mono is not a preference: Unity cannot cross-compile a Windows IL2CPP
            // player from a Linux Editor, so a Linux build machine has no other option.
            // Release builds are expected to come from a Windows runner with IL2CPP.
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);

            var buildPlayerOptions = new BuildPlayerOptions
            {
                scenes = ResolveScenes(),
                locationPathName = outputPath,
                target = target,
                targetGroup = group,
                options = options
            };

            Debug.Log("[worker] building " + target + " -> " + outputPath
                      + " (" + buildPlayerOptions.scenes.Length + " scenes)");

            var report = BuildPipeline.BuildPlayer(buildPlayerOptions);
            var summary = report.summary;

            Debug.Log("[worker] result " + summary.result
                      + ", size " + summary.totalSize + " bytes"
                      + ", errors " + summary.totalErrors
                      + ", warnings " + summary.totalWarnings
                      + ", duration " + summary.totalTime);

            if (summary.result == BuildResult.Succeeded) return;

            LogFailedSteps(report);
            EditorApplication.Exit(1);
        }

        private static void LogFailedSteps(BuildReport report)
        {
            foreach (var step in report.steps)
            {
                foreach (var message in step.messages)
                {
                    if (message.type != LogType.Error && message.type != LogType.Exception) continue;
                    Debug.LogError("[worker] " + step.name + ": " + message.content);
                }
            }
        }

        private static string ReadArgument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name) return args[i + 1];
            }
            return null;
        }
    }
}
