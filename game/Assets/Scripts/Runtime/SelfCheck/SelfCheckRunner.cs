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

            [Tooltip("Pose the road outside before capturing. -1 leaves it alone.")]
            public int stagePhase = -1;

            [Tooltip("Put every question through the intercom, not just the first.")]
            public bool askEverything;

            [Tooltip("Put this question through the intercom and wait for the reply. -1 asks nothing.")]
            public int askQuestion = -1;

            [Tooltip("Turn to the next page of the binder before capturing.")]
            public bool turnPage;

            [Tooltip("Which night's binder and queue to set up. -1 leaves the current one.")]
            public int night = -1;
        }

        [SerializeField] private List<Checkpoint> checkpoints = new();

        [Tooltip("Frames sampled for the frame-time statistic at each checkpoint.")]
        [SerializeField] private int frameSampleCount = 30;

        [Tooltip("What to lean over when photographing the morning report.")]
        [SerializeField] private Transform reportAnchor;

        [Tooltip("What to lean over when photographing the post.")]
        [SerializeField] private Transform mailAnchor;

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
                // A late night is worth photographing because the binder only contains
                // amendments by then; on night one every page is an original and the
                // supersession the game is built around is invisible.
                if (presenter != null && checkpoint.night >= 0)
                {
                    presenter.BeginShift(checkpoint.night);
                }

                if (presenter != null && checkpoint.subjectIndex >= 0)
                {
                    presenter.ShowSubject(checkpoint.subjectIndex);
                }

                if (presenter != null && checkpoint.turnPage)
                {
                    presenter.LeafThroughManual();
                }

                var stage = presenter != null ? presenter.Stage : null;
                if (stage != null && checkpoint.stagePhase >= 0)
                {
                    stage.SetPhase((CheckpointStage.Phase)checkpoint.stagePhase, immediate: true);
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

                // Asking is the one checkpoint that has to wait on the game rather than on
                // a fixed settle: the whole point of the intercom is that the pause before
                // an answer is however long this bearer takes.
                if (presenter != null && checkpoint.askQuestion >= 0)
                {
                    // Every question when the checkpoint wants the transcript, so the shot
                    // shows what a player who spent four of them actually sees.
                    var last = checkpoint.askEverything
                        ? System.Enum.GetValues(typeof(Question)).Length - 1
                        : checkpoint.askQuestion;

                    for (var q = checkpoint.askQuestion; q <= last; q++)
                    {
                        var replied = false;

                        void OnReply(Reply _) => replied = true;

                        presenter.ReplyReceived += OnReply;
                        if (!presenter.Ask((Question)q))
                        {
                            _logLines.Add($"Error: checkpoint '{checkpoint.name}' could not put " +
                                          $"question {q} through the intercom");
                            replied = true;
                        }

                        var giveUp = Time.realtimeSinceStartup + 12f;
                        while (!replied && Time.realtimeSinceStartup < giveUp)
                        {
                            yield return null;
                        }

                        presenter.ReplyReceived -= OnReply;

                        if (!replied)
                        {
                            _logLines.Add($"Error: no reply arrived for checkpoint '{checkpoint.name}'");
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

            yield return ExerciseInput(boothCamera, records);
            yield return PlayOutShifts(presenter, boothCamera, records);

            WriteReport(records);

            Debug.Log($"[SelfCheck] done, {_logLines.Count} error(s) logged");
            Application.Quit(_logLines.Count == 0 ? 0 : 3);
        }

        /// <summary>Drives the game the way a player does: look until something is under
        /// the centre of the view, then click it.
        ///
        /// Everything else in this file reaches past the input layer and calls the camera
        /// and the presenter directly. That is why a build shipped in which looking and
        /// clicking did nothing whatsoever -- the interactor's input source is a plain
        /// property, the scene generator assigned it at edit time, and properties are not
        /// serialised. Nothing here failed, because nothing here went through it.
        ///
        /// This does, and asserts the two things that were silently broken: that a look
        /// delta turns the camera, and that a click on something reaches it.</summary>
        private IEnumerator ExerciseInput(BoothCamera boothCamera, ICollection<string> records)
        {
            var interactor = FindFirstObjectByType<DeskInteractor>();

            if (interactor == null || boothCamera == null)
            {
                _logLines.Add("Error: no desk interactor in the scene, the input path was not exercised");
                yield break;
            }

            // Checked before anything is installed. Everything below runs through a
            // scripted source, so without this the whole exercise would keep passing in
            // exactly the build that shipped with no input at all -- which is the bug this
            // was written for.
            if (interactor.Input is NullInputSource)
            {
                _logLines.Add("Error: the desk interactor has no input source of its own, so the " +
                              "built player cannot be looked around or clicked in");
            }

            var scripted = new ScriptedInputSource();
            var previous = interactor.Input;
            interactor.Input = scripted;

            boothCamera.ResetToHome();
            yield return null;

            var startRotation = boothCamera.transform.rotation;

            // Sweep the view across the desk. Something interactable has to pass under the
            // centre on the way.
            var hovered = 0;
            var seen = new HashSet<string>();

            foreach (var look in new[] { new Vector2(-1.4f, 0f), new Vector2(1.1f, -0.5f), new Vector2(0.9f, 0.6f) })
            {
                scripted.Look = look;

                for (var frame = 0; frame < 45; frame++)
                {
                    yield return null;

                    if (interactor.Hovered != null && seen.Add(interactor.Hovered.name))
                    {
                        hovered++;
                    }
                }
            }

            scripted.Look = Vector2.zero;
            yield return null;

            var turned = Quaternion.Angle(startRotation, boothCamera.transform.rotation);

            if (turned < 5f)
            {
                _logLines.Add($"Error: the camera turned {turned:F1} degrees under a scripted look " +
                              "delta; the input path is not connected");
            }

            if (hovered == 0)
            {
                _logLines.Add("Error: sweeping the view across the desk never put anything under the " +
                              "centre of the view");
            }

            // Now click whatever is under the centre and check it heard.
            var clicked = false;

            foreach (var look in new[] { new Vector2(-0.8f, -0.3f), new Vector2(0.5f, 0.2f) })
            {
                scripted.Look = look;

                for (var frame = 0; frame < 60 && !clicked; frame++)
                {
                    yield return null;

                    if (interactor.Hovered == null)
                    {
                        continue;
                    }

                    var target = interactor.Hovered;
                    scripted.Look = Vector2.zero;
                    scripted.Click();
                    yield return null;
                    yield return null;

                    // An Inspect or Leaf target becomes the focused one; an Operate target
                    // fires and leaves nothing focused, so both are checked.
                    clicked = interactor.Focused == target
                              || target.Mode == DeskInteractable.Behaviour.Operate;

                    if (clicked)
                    {
                        Debug.Log($"[SelfCheck] clicked '{target.name}' ({target.Mode}) through the " +
                                  "input path");
                    }
                }
            }

            if (!clicked)
            {
                _logLines.Add("Error: a click through the input path never reached an interactable");
            }

            // The generic sweep proves the path is connected but not what it reached. This
            // aims at a named verdict switch and asserts the decision actually landed,
            // which is the one interaction the whole game is made of and the only one a
            // person has still never performed by hand.
            var threw = false;
            var presenter = FindFirstObjectByType<BoothPresenter>();
            var verdictSwitch = FindObjectsByType<DeskInteractable>(FindObjectsSortMode.None)
                .FirstOrDefault(d => d.Mode == DeskInteractable.Behaviour.Operate
                                     && Enum.TryParse<Verdict>(d.Payload, true, out _));

            if (presenter == null || verdictSwitch == null)
            {
                _logLines.Add("Error: no verdict switch in the scene to aim at");
            }
            else
            {
                presenter.BeginShift(0);
                yield return null;

                var before = presenter.Director.Position;

                boothCamera.SnapLookAt(verdictSwitch.transform.position);
                yield return null;
                yield return null;

                if (interactor.Hovered != verdictSwitch)
                {
                    _logLines.Add($"Error: looking straight at '{verdictSwitch.name}' did not put it " +
                                  $"under the centre of the view (hovering " +
                                  $"'{(interactor.Hovered == null ? "nothing" : interactor.Hovered.name)}')");
                }
                else
                {
                    scripted.Click();
                    yield return null;
                    yield return null;

                    threw = presenter.Director.Position > before;

                    if (!threw)
                    {
                        _logLines.Add($"Error: clicking '{verdictSwitch.name}' through the input path " +
                                      "did not record a decision");
                    }
                    else
                    {
                        Debug.Log($"[SelfCheck] threw '{verdictSwitch.Payload}' through the input path; " +
                                  $"the queue advanced from {before} to {presenter.Director.Position}");
                    }
                }
            }

            interactor.Release();
            interactor.Input = previous;
            boothCamera.ResetToHome();
            yield return null;

            records.Add(string.Format(CultureInfo.InvariantCulture,
                "    {{\n" +
                "      \"name\": \"input\",\n" +
                "      \"camera_turned_degrees\": {0:F1},\n" +
                "      \"interactables_hovered\": {1},\n" +
                "      \"click_reached_target\": {2},\n" +
                "      \"switch_thrown_by_click\": {3}\n" +
                "    }}",
                turned, hovered, clicked ? "true" : "false", threw ? "true" : "false"));
        }

        /// <summary>Plays several nights to their end by throwing switches directly, then
        /// photographs the morning report and whatever the post brought.
        ///
        /// This is the only end-to-end exercise of the loop that runs in a real build on
        /// this machine, and it caught the switches being dead in the build: the scene
        /// generator had subscribed to their events at edit time, and event subscriptions
        /// do not serialise.
        ///
        /// The first night is played correctly and the rest carelessly, because the
        /// consequences the game is built around only arrive several nights after the
        /// decision that caused them. A single perfect night produces nothing to photograph.
        /// </summary>
        private IEnumerator PlayOutShifts(BoothPresenter presenter, BoothCamera boothCamera,
            ICollection<string> records)
        {
            if (presenter == null || presenter.Director == null)
            {
                _logLines.Add("Error: no booth presenter in the scene, the shift loop was not exercised");
                yield break;
            }

            const int nights = 6;
            var processed = 0;
            var withheld = 0;
            NightlyStatement lastStatement = null;

            void OnEnded(NightlyStatement statement) => lastStatement = statement;

            CampaignStore.Delete();

            presenter.ShiftEnded += OnEnded;
            presenter.BeginShift(0);

            for (var night = 0; night < nights; night++)
            {
                // NextShift rather than BeginShift: the consequences this run exists to
                // photograph are queued by one night and delivered several nights later, and
                // restarting the campaign each time would throw them away.
                if (night > 0 && !presenter.NextShift())
                {
                    _logLines.Add($"Error: the campaign would not advance to night {night + 1}");
                    break;
                }

                lastStatement = null;
                var guard = 0;

                while (presenter.Director != null && !presenter.Director.IsFinished && guard++ < 200)
                {
                    var correct = RuleEvaluator.Evaluate(presenter.Director.Current.Attributes,
                        presenter.Director.Manual).CorrectVerdict;

                    // Night one is played properly; after that, carelessly, so the post has
                    // something to report.
                    var chosen = night == 0 ? correct : Verdict.Pass;
                    if (!presenter.Submit(chosen))
                    {
                        break;
                    }
                }

                if (lastStatement == null)
                {
                    _logLines.Add($"Error: night {night + 1} never produced a morning report");
                    break;
                }

                processed += lastStatement.Processed;
                withheld += lastStatement.Deductions;
            }

            presenter.ShiftEnded -= OnEnded;

            // Written by the last EndShift. Proving it round-trips here is the only place
            // the save path is exercised in a real build rather than in the editor.
            var resumable = false;
            try
            {
                var save = CampaignStore.Read();
                resumable = save != null && Campaign.Restore(save, out _).ShiftIndex == nights;
            }
            catch (Exception exception)
            {
                _logLines.Add($"Error: the campaign written during the run could not be resumed: " +
                              $"{exception.Message}");
            }

            if (!resumable)
            {
                _logLines.Add("Error: the campaign written during the run did not resume to night " +
                              nights.ToString(CultureInfo.InvariantCulture));
            }

            CampaignStore.Delete();

            Debug.Log($"[SelfCheck] played {nights} nights: {processed} vehicles processed, " +
                      $"{withheld} credits withheld");

            if (processed <= 0)
            {
                _logLines.Add("Error: no vehicles were processed across the whole run");
            }

            if (boothCamera != null)
            {
                boothCamera.ResetToHome();
                if (reportAnchor != null)
                {
                    boothCamera.SnapFocus(reportAnchor, 0.40f, new Vector3(0f, 1f, -0.34f));
                }
            }

            yield return new WaitForSecondsRealtime(0.4f);
            yield return CaptureTo(Camera.main, Path.Combine(_outputDirectory, "morning_report.png"));

            if (boothCamera != null && mailAnchor != null)
            {
                boothCamera.ResetToHome();
                boothCamera.SnapFocus(mailAnchor, 0.40f, new Vector3(0f, 1f, -0.34f));
            }

            yield return new WaitForSecondsRealtime(0.4f);
            yield return CaptureTo(Camera.main, Path.Combine(_outputDirectory, "consequence.png"));

            // The window, with something in it that has more arms than it should. Found by
            // looking rather than by a hardcoded index, so it keeps working when the
            // generator changes.
            var manyLimbed = false;

            for (var night = 0; night < 6 && !manyLimbed; night++)
            {
                presenter.BeginShift(night);

                for (var i = 0; i < presenter.Director.QueueLength && !manyLimbed; i++)
                {
                    presenter.ShowSubject(i);
                    manyLimbed = presenter.Director.Current.Attributes.VisibleLimbCount > 4;
                }
            }

            if (!manyLimbed)
            {
                _logLines.Add("Error: no subject in the first six nights had more than four limbs, " +
                              "so C-13 is a criterion the player can never see fire");
            }
            else if (boothCamera != null)
            {
                boothCamera.ResetToHome();
                boothCamera.SnapLookAt(new Vector3(0.52f, 1.5f, 4.95f));

                yield return new WaitForSecondsRealtime(0.4f);
                yield return CaptureTo(Camera.main, Path.Combine(_outputDirectory, "the_window.png"));
            }

            // The last night, so the letter that closes a run gets photographed like
            // everything else. Skipped to rather than played, because thirty nights is five
            // hundred vehicles and the ending does not depend on the ones in between.
            var ended = false;
            presenter.CampaignEnded += () => ended = true;
            presenter.BeginShift(Campaign.TotalShifts - 1);

            var lastGuard = 0;
            while (presenter.Director != null && !presenter.Director.IsFinished && lastGuard++ < 200)
            {
                if (!presenter.Submit(Verdict.Refer))
                {
                    break;
                }
            }

            if (!ended)
            {
                _logLines.Add("Error: playing out the last night did not end the campaign");
            }

            if (boothCamera != null && mailAnchor != null)
            {
                boothCamera.ResetToHome();
                boothCamera.SnapFocus(mailAnchor, 0.40f, new Vector3(0f, 1f, -0.34f));
            }

            yield return new WaitForSecondsRealtime(0.4f);
            yield return CaptureTo(Camera.main, Path.Combine(_outputDirectory, "final_notice.png"));

            records.Add(string.Format(CultureInfo.InvariantCulture,
                "    {{\n" +
                "      \"name\": \"campaign\",\n" +
                "      \"nights_played\": {0},\n" +
                "      \"vehicles_processed\": {1},\n" +
                "      \"credits_withheld\": {2},\n" +
                "      \"save_resumed\": {3},\n" +
                "      \"campaign_ended\": {4}\n" +
                "    }}",
                nights, processed, withheld, resumable ? "true" : "false", ended ? "true" : "false"));
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
