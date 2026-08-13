using System;
using System.Collections;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Undertown.Game.Bootstrap
{
    /// <summary>
    /// Captures frames from a real player run and then exits. Development happens on a
    /// machine with no monitor, so this is how the game's actual appearance gets compared
    /// against the visual targets in docs/target - straight from the renderer, rather than
    /// through a screen grab of a virtual X display.
    ///
    /// Enabled by command line only:
    ///   Undertown.x86_64 -autoshot &lt;dir&gt; [-autoshotFrames N] [-autoshotInterval SECONDS] [-autoshotLabel NAME]
    /// </summary>
    public sealed class ScreenshotHarness : MonoBehaviour
    {
        private string _directory;
        private string _label = "frame";
        private int _frames = 3;
        private float _interval = 1.5f;

        private int _width = 1920;
        private int _height = 1080;

        /// <summary>
        /// Simulated minutes to run before the first capture. An inspection lands on day 10,
        /// which is hours of real time away at normal speed; documenting that state needs the
        /// clock pushed forward rather than the capture waited out.
        /// </summary>
        private int _warmupMinutes;

        /// <summary>
        /// Starts the illicit works before the warm-up. They begin idle, so documenting what
        /// a running contraband operation looks like means making the same choice a player
        /// would rather than waiting for one that never comes.
        /// </summary>
        private bool _runTheStill;

        /// <summary>Layer to capture. Underground states cannot be reached from a headless
        /// run any other way, since there is no keyboard to press the layer key with.</summary>
        private int _depth;

        private void Start()
        {
            if (!ParseArguments()) { enabled = false; return; }

            // The player otherwise opens at whatever the window manager hands it, which on a
            // virtual display is not the resolution the captures are supposed to document.
            Screen.SetResolution(_width, _height, FullScreenMode.Windowed);

            StartCoroutine(CaptureSequence());
        }

        private bool ParseArguments()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-autoshot" when i + 1 < args.Length:
                        _directory = args[++i];
                        break;
                    case "-autoshotFrames" when i + 1 < args.Length:
                        int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out _frames);
                        break;
                    case "-autoshotInterval" when i + 1 < args.Length:
                        float.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out _interval);
                        break;
                    case "-autoshotLabel" when i + 1 < args.Length:
                        _label = args[++i];
                        break;
                    case "-autoshotSize" when i + 2 < args.Length:
                        int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out _width);
                        int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out _height);
                        break;
                    case "-autoshotWarmupMinutes" when i + 1 < args.Length:
                        int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out _warmupMinutes);
                        break;
                    case "-autoshotRunTheStill":
                        _runTheStill = true;
                        break;
                    case "-autoshotDepth" when i + 1 < args.Length:
                        int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out _depth);
                        break;
                }
            }
            return !string.IsNullOrEmpty(_directory);
        }

        private IEnumerator CaptureSequence()
        {
            Directory.CreateDirectory(_directory);

            // Let the first frame finish so the tilemaps have actually rendered.
            yield return new WaitForEndOfFrame();
            yield return new WaitForSecondsRealtime(0.5f);

            if (_runTheStill || _warmupMinutes > 0)
            {
                var bootstrap = FindFirstObjectByType<GameBootstrap>();
                if (bootstrap != null && _runTheStill)
                {
                    int started = bootstrap.StartIllicitWorks();
                    Debug.Log($"[SHOT] started {started} illicit workshop(s)");
                }

                if (bootstrap != null && _warmupMinutes > 0)
                {
                    bootstrap.FastForward(_warmupMinutes);
                    Debug.Log($"[SHOT] fast-forwarded {_warmupMinutes} simulated minutes");
                }

                if (bootstrap != null && _depth > 0)
                {
                    bootstrap.SwitchLayer();
                    Debug.Log("[SHOT] switched to the underground layer");
                }

                // The HUD reads the town in its own Update, which for this component has
                // already run by the time the coroutine resumes. Without giving it a couple
                // of whole frames the capture shows the state from before the jump.
                yield return null;
                yield return null;
                yield return new WaitForEndOfFrame();
            }

            for (int i = 1; i <= Mathf.Max(1, _frames); i++)
            {
                string path = Path.Combine(_directory, $"{_label}-{i:D2}.png");
                ScreenCapture.CaptureScreenshot(path);
                Debug.Log($"[SHOT] {path}");

                // CaptureScreenshot writes asynchronously; give it a frame plus the interval.
                yield return new WaitForEndOfFrame();
                yield return new WaitForSecondsRealtime(Mathf.Max(0.2f, _interval));

                OnFrameCaptured(i);
            }

            Debug.Log("[SHOT] sequence complete");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit(0);
        }

        /// <summary>
        /// Hook for varying the scene between shots so one run can document several states
        /// instead of three identical pictures.
        /// </summary>
        private void OnFrameCaptured(int frameIndex)
        {
            var bootstrap = FindFirstObjectByType<GameBootstrap>();
            if (bootstrap == null) return;

            // Alternate between the surface and the tunnels so both views get documented.
            bootstrap.SwitchLayer();
        }
    }
}
