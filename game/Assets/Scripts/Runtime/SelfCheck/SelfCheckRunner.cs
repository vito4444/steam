using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Monster.Interaction;
using Monster.Presentation;
using Monster.Rules;
using Monster.Shift;
using UnityEngine;
using UnityEngine.Profiling;

namespace Monster.SelfCheck
{
    /// <summary>
    /// Drives the built player through a fixed list of named checkpoints, capturing a
    /// screenshot and a metrics record at each one, then quits.
    ///
    /// This is how the project sees itself: the captured frames are compared against the
    /// previous run to catch regressions, and against the concept art in
    /// docs/concepts/art/ to measure how far the build is from the target.
    ///
    /// Activated with -selfcheck on the player's command line. Without it this component
    /// disables itself immediately and costs nothing.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public sealed class SelfCheckRunner : MonoBehaviour
    {
        [Serializable]
        public sealed class Checkpoint
        {
            public string name = "checkpoint";

            [Tooltip("Seconds of real time to settle before capturing.")]
            public float settleSeconds = 0.5f;

            [Tooltip("Optional camera to capture from. Falls back to Camera.main.")]
            public Camera camera;

            [Tooltip("Optional point in the booth to turn towards before capturing.")]
            public Transform lookAt;

            [Tooltip("Queue position to put at the window, or -1 to leave the shift alone.")]
            public int subjectIndex = -1;

            [Tooltip("Lean over the target, as the player does when reading a document.")]
            public bool leanIn;
        }

        [SerializeField] private List<Checkpoint> checkpoints = new();

        [Tooltip("Frames sampled for the frame-time statistic at each checkpoint.")]
        [SerializeField] private int frameSampleCount = 30;

        [Tooltip("What to lean over when photographing the morning report.")]
        [SerializeField] private Transform reportAnchor;

        private string _outputDirectory;
        private readonly List<string> _logLines = new();

        private void Awake()
        {
            var args = Environment.GetCommandLineArgs();
            if (!args.Contains("-selfcheck"))
            {
                enabled = false;
                return;
            }

            _outputDirectory = ArgValue(args, "-selfcheck-out")
                               ?? Path.Combine(Application.persistentDataPath, "selfcheck");
            Directory.CreateDirectory(_outputDirectory);

            Application.logMessageReceived += OnLog;

            // A fixed timestep and a fixed frame rate make run-to-run comparison meaningful.
            // Without this, screenshots differ because time-dependent effects have advanced
            // by different amounts, and the image diff becomes pure noise.
            Application.targetFrameRate = 60;
            Time.captureFramerate = 60;
            QualitySettings.vSyncCount = 0;
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type is LogType.Error or LogType.Exception or LogType.Assert)
            {
                _logLines.Add($"{type}: {condition}");
            }
        }

        private IEnumerator Start()
        {
            if (!enabled)
            {
                yield break;
            }

            Debug.Log($"[SelfCheck] starting, {checkpoints.Count} checkpoint(s), output: {_outputDirectory}");

            // The first frames of a player include shader warmup and asset loading; capturing
            // during them produces images that do not represent the steady state.
            for (var i = 0; i < 10; i++)
            {
                yield return new WaitForEndOfFrame();
            }

            var records = new List<string>();
            var boothCamera = FindFirstObjectByType<BoothCamera>();
            var presenter = FindFirstObjectByType<BoothPresenter>();

            foreach (var checkpoint in checkpoints)
            {
                // Each checkpoint starts from the home pose and a known subject, so no
                // capture depends on the one before it and the images stay comparable
                // between runs even if a checkpoint is inserted or removed.
                if (presenter != null && checkpoint.subjectIndex >= 0)
                {
                    presenter.ShowSubject(checkpoint.subjectIndex);
                }

                if (boothCamera != null)
                {
                    boothCamera.ResetToHome();
                    if (checkpoint.lookAt != null)
                    {
                        if (checkpoint.leanIn)
                        {
                            boothCamera.SnapFocus(checkpoint.lookAt, 0.46f, new Vector3(0f, 1f, -0.34f));
                        }
                        else
                        {
                            boothCamera.SnapLookAt(checkpoint.lookAt.position);
                        }
                    }
                }

                var settleUntil = Time.realtimeSinceStartup + Mathf.Max(0f, checkpoint.settleSeconds);
                while (Time.realtimeSinceStartup < settleUntil)
                {
                    yield return null;
                }

                var frameMs = 0f;
                for (var i = 0; i < frameSampleCount; i++)
                {
                    yield return new WaitForEndOfFrame();
                    frameMs += Time.unscaledDeltaTime * 1000f;
                }

                frameMs /= Mathf.Max(1, frameSampleCount);

                var camera = checkpoint.camera != null ? checkpoint.camera : Camera.main;
                var imagePath = Path.Combine(_outputDirectory, checkpoint.name + ".png");
                yield return CaptureTo(camera, imagePath);

                var stats = CollectSceneStats();
                records.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "    {{\n" +
                    "      \"name\": \"{0}\",\n" +
                    "      \"image\": \"{1}\",\n" +
                    "      \"frame_ms\": {2:F2},\n" +
                    "      \"fps\": {3:F1},\n" +
                    "      \"visible_renderers\": {4},\n" +
                    "      \"triangles\": {5},\n" +
                    "      \"active_lights\": {6},\n" +
                    "      \"shadow_casting_lights\": {9},\n" +
                    "      \"mono_heap_mb\": {7:F1},\n" +
                    "      \"total_reserved_mb\": {8:F1}\n" +
                    "    }}",
                    checkpoint.name,
                    Path.GetFileName(imagePath),
                    frameMs,
                    frameMs > 0f ? 1000f / frameMs : 0f,
                    stats.renderers,
                    stats.triangles,
                    stats.activeLights,
                    GC.GetTotalMemory(false) / (1024f * 1024f),
                    Profiler.GetTotalReservedMemoryLong() / (1024f * 1024f),
                    stats.shadowCastingLights));

                Debug.Log($"[SelfCheck] {checkpoint.name}: {frameMs:F1} ms/frame, " +
                          $"{stats.triangles} tris, {stats.renderers} renderers -> {imagePath}");
            }

            yield return PlayOutAShift(presenter, boothCamera, records);

            WriteReport(records);

            Debug.Log($"[SelfCheck] done, {_logLines.Count} error(s) logged");
            Application.Quit(_logLines.Count == 0 ? 0 : 3);
        }

        /// <summary>Plays an entire night to its end by throwing switches directly, then
        /// photographs the morning report.
        ///
        /// This is the only end-to-end exercise of the decision loop that runs in a real
        /// build on this machine. It caught the switches being dead in the build: the
        /// scene generator had subscribed to their events at edit time, and event
        /// subscriptions do not serialise.</summary>
        private IEnumerator PlayOutAShift(BoothPresenter presenter, BoothCamera boothCamera,
            ICollection<string> records)
        {
            if (presenter == null || presenter.Director == null)
            {
                _logLines.Add("Error: no booth presenter in the scene, the shift loop was not exercised");
                yield break;
            }

            presenter.BeginShift(0);
            var guard = 0;
            var submitted = 0;

            while (!presenter.Director.IsFinished && guard++ < 200)
            {
                // Deliberately the correct answer every time. The point is to prove the
                // loop runs to completion in a build, not to test the rules -- the edit
                // mode suite already does that far more thoroughly.
                var correct = RuleEvaluator.Evaluate(presenter.Director.Current.Attributes,
                    presenter.Director.Manual).CorrectVerdict;
                if (!presenter.Submit(correct))
                {
                    break;
                }

                submitted++;
            }

            if (!presenter.Director.IsFinished)
            {
                _logLines.Add($"Error: the shift did not finish after {submitted} decisions");
            }

            var report = presenter.Director.BuildReport();
            if (report.Correct != report.Processed)
            {
                _logLines.Add($"Error: playing every correct verdict scored {report.Correct} " +
                              $"of {report.Processed}");
            }

            Debug.Log($"[SelfCheck] played a full shift: {report.Processed} processed, " +
                      $"{report.Correct} correct, {report.NetPay} credits");

            if (boothCamera != null)
            {
                boothCamera.ResetToHome();
                if (reportAnchor != null)
                {
                    boothCamera.SnapFocus(reportAnchor, 0.40f, new Vector3(0f, 1f, -0.34f));
                }
            }

            yield return new WaitForSecondsRealtime(0.4f);

            var path = Path.Combine(_outputDirectory, "morning_report.png");
            yield return CaptureTo(Camera.main, path);

            records.Add(string.Format(CultureInfo.InvariantCulture,
                "    {{\n" +
                "      \"name\": \"morning_report\",\n" +
                "      \"image\": \"morning_report.png\",\n" +
                "      \"processed\": {0},\n" +
                "      \"correct\": {1},\n" +
                "      \"net_pay\": {2}\n" +
                "    }}",
                report.Processed, report.Correct, report.NetPay));
        }

        private static IEnumerator CaptureTo(Camera camera, string path)
        {
            yield return new WaitForEndOfFrame();

            var width = Mathf.Max(640, Screen.width);
            var height = Mathf.Max(360, Screen.height);

            // Rendering explicitly into a RenderTexture rather than using
            // ScreenCapture keeps the output size deterministic, which matters because the
            // whole point is to diff these images against each other.
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 1,
            };

            var previousTarget = camera != null ? camera.targetTexture : null;
            var previousActive = RenderTexture.active;

            try
            {
                if (camera != null)
                {
                    camera.targetTexture = target;
                    camera.Render();
                    camera.targetTexture = previousTarget;
                }

                RenderTexture.active = target;
                var image = new Texture2D(width, height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
                Destroy(image);
            }
            finally
            {
                RenderTexture.active = previousActive;
                target.Release();
                Destroy(target);
            }
        }

        private readonly struct SceneStats
        {
            public readonly int renderers;
            public readonly int triangles;
            public readonly int activeLights;
            public readonly int shadowCastingLights;

            public SceneStats(int renderers, int triangles, int activeLights, int shadowCastingLights)
            {
                this.renderers = renderers;
                this.triangles = triangles;
                this.activeLights = activeLights;
                this.shadowCastingLights = shadowCastingLights;
            }
        }

        private static SceneStats CollectSceneStats()
        {
            var triangles = 0;
            var visible = 0;

            foreach (var filter in FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                var meshRenderer = filter.GetComponent<MeshRenderer>();
                if (meshRenderer == null || !meshRenderer.enabled || !filter.gameObject.activeInHierarchy)
                {
                    continue;
                }

                visible++;
                var mesh = filter.sharedMesh;
                if (mesh != null)
                {
                    triangles += mesh.triangles.Length / 3;
                }
            }

            // Light.lightmapBakeType is editor-only, so the runtime signal is shadow
            // casting, which is the property that actually costs frame time anyway.
            var lights = FindObjectsByType<Light>(FindObjectsSortMode.None)
                .Where(l => l.enabled && l.gameObject.activeInHierarchy)
                .ToArray();

            return new SceneStats(visible, triangles, lights.Length,
                lights.Count(l => l.shadows != LightShadows.None));
        }

        private void WriteReport(IEnumerable<string> records)
        {
            var json = new StringBuilder();
            json.AppendLine("{");
            json.AppendLine($"  \"version\": \"{Application.version}\",");
            json.AppendLine($"  \"unity\": \"{Application.unityVersion}\",");
            json.AppendLine($"  \"platform\": \"{Application.platform}\",");
            json.AppendLine($"  \"graphics_device\": \"{Escape(SystemInfo.graphicsDeviceName)}\",");
            json.AppendLine($"  \"graphics_api\": \"{SystemInfo.graphicsDeviceType}\",");
            json.AppendLine($"  \"screen\": \"{Screen.width}x{Screen.height}\",");
            json.AppendLine("  \"checkpoints\": [");
            json.AppendLine(string.Join(",\n", records));
            json.AppendLine("  ],");
            json.AppendLine($"  \"error_count\": {_logLines.Count},");
            json.AppendLine("  \"errors\": [");
            json.AppendLine(string.Join(",\n", _logLines.Select(l => $"    \"{Escape(l)}\"")));
            json.AppendLine("  ]");
            json.AppendLine("}");

            File.WriteAllText(Path.Combine(_outputDirectory, "metrics.json"), json.ToString());
        }

        private static string Escape(string value) =>
            (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");

        private static string ArgValue(IReadOnlyList<string> args, string key)
        {
            for (var i = 0; i < args.Count - 1; i++)
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
