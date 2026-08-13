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
