using System;
using System.Globalization;
using System.IO;
using UnityEngine;
using Worker.Core;

namespace Worker.Game
{
    /// <summary>
    /// Drives an unattended capture run: fast-forwards the simulation, saves numbered
    /// screenshots, writes a metrics file and exits.
    ///
    /// This is the Unity half of the self-test loop. The headless preview renderer in
    /// Tools/Preview is fast and needs nothing installed, but it draws the world with its
    /// own rasteriser; only this path proves what the actual shipping renderer puts on
    /// screen. Both exist on purpose, and their screenshots are meant to agree.
    ///
    /// Activated by --capture-dir on the command line; otherwise this component does
    /// nothing and the game runs normally.
    /// </summary>
    [RequireComponent(typeof(SimRunner))]
    public sealed class HeadlessCapture : MonoBehaviour
    {
        private SimRunner _runner;
        private string _captureDirectory;
        private float _captureIntervalSeconds = 2f;
        private int _captureLimit = 8;
        private float _runSeconds = 20f;

        private float _nextCaptureAt;
        private int _captured;
        private float _startedAt;
        private bool _finished;

        public bool Enabled => !string.IsNullOrEmpty(_captureDirectory);

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            ParseArguments();

            if (!Enabled)
            {
                enabled = false;
                return;
            }

            Directory.CreateDirectory(_captureDirectory);
            _startedAt = Time.realtimeSinceStartup;
            _nextCaptureAt = 0f;

            Debug.Log("[capture] writing to " + _captureDirectory
                      + ", interval " + _captureIntervalSeconds + "s"
                      + ", limit " + _captureLimit
                      + ", run " + _runSeconds + "s");
        }

        private void Update()
        {
            if (_finished) return;

            float elapsed = Time.realtimeSinceStartup - _startedAt;

            if (elapsed >= _nextCaptureAt && _captured < _captureLimit)
            {
                Capture();
                _nextCaptureAt = elapsed + _captureIntervalSeconds;
            }

            if (elapsed >= _runSeconds || _captured >= _captureLimit)
            {
                Finish();
            }
        }

        private void Capture()
        {
            var world = _runner.World;
            int tick = world?.Tick ?? 0;

            string fileName = "unity_tick_" + tick.ToString("D6", CultureInfo.InvariantCulture) + ".png";
            string path = Path.Combine(_captureDirectory, fileName);

            // CaptureScreenshot is asynchronous: the file lands at the end of the frame,
            // which is why Finish() waits a moment before quitting.
            ScreenCapture.CaptureScreenshot(path);
            _captured++;

            Debug.Log("[capture] frame " + _captured + " at tick " + tick + " -> " + fileName);
        }

        private void Finish()
        {
            _finished = true;
            WriteMetrics();

            Debug.Log("[capture] done, " + _captured + " frames");

            // Give the last asynchronous screenshot a frame or two to reach disk.
            Invoke(nameof(QuitNow), 1.5f);
        }

        private void QuitNow()
        {
            Application.Quit(0);
        }

        private void WriteMetrics()
        {
            var world = _runner.World;
            if (world == null) return;

            int idle = 0;
            for (int i = 0; i < world.Workers.Count; i++)
            {
                if (world.Workers[i].IsIdle) idle++;
            }

            string json = "{\n"
                          + "  \"source\": \"unity-player\",\n"
                          + "  \"unityVersion\": \"" + Application.unityVersion + "\",\n"
                          + "  \"graphicsDevice\": \"" + SystemInfo.graphicsDeviceName.Replace("\"", "'") + "\",\n"
                          + "  \"graphicsApi\": \"" + SystemInfo.graphicsDeviceType + "\",\n"
                          + "  \"screen\": \"" + Screen.width + "x" + Screen.height + "\",\n"
                          + "  \"frames\": " + _captured + ",\n"
                          + "  \"tick\": " + world.Tick + ",\n"
                          + "  \"unitsShipped\": " + world.TotalUnitsShipped + ",\n"
                          + "  \"craftsCompleted\": " + world.TotalCraftsCompleted + ",\n"
                          + "  \"balanceCents\": " + world.Ledger.Balance + ",\n"
                          + "  \"workers\": " + world.Workers.Count + ",\n"
                          + "  \"idleWorkers\": " + idle + ",\n"
                          + "  \"stateHash\": \"" + StateHash.ToHex(StateHash.Compute(world)) + "\"\n"
                          + "}\n";

            File.WriteAllText(Path.Combine(_captureDirectory, "unity-metrics.json"), json);
        }

        private void ParseArguments()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--capture-dir":
                        _captureDirectory = Value(args, i);
                        break;
                    case "--capture-interval":
                        _captureIntervalSeconds = ParseFloat(Value(args, i), _captureIntervalSeconds);
                        break;
                    case "--capture-count":
                        _captureLimit = ParseInt(Value(args, i), _captureLimit);
                        break;
                    case "--run-seconds":
                        _runSeconds = ParseFloat(Value(args, i), _runSeconds);
                        break;
                }
            }
        }

        private static string Value(string[] args, int index)
            => index + 1 < args.Length ? args[index + 1] : null;

        private static int ParseInt(string text, int fallback)
            => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;

        private static float ParseFloat(string text, float fallback)
            => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback;
    }
}
