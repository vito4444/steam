using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Maner.SelfCheck
{
    /// <summary>
    /// 无人值守自检运行器。构建产物在虚拟显示环境中启动后，按预设时间点自动截图，
    /// 记录帧时间统计，然后退出。这是本项目在没有人工操作的情况下观察实机画面、
    /// 并与目标概念图做逐版本对比的唯一手段。
    ///
    /// 命令行参数：
    ///   -manerShots 0.6,2.0,4.0   截图时间点（秒），逗号分隔
    ///   -manerOut &lt;dir&gt;           截图输出目录
    ///   -manerOrbit               启用相机环绕，使各时间点的截图覆盖不同视角
    ///   -manerNoQuit              截图完成后不退出
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AutoScreenshotRunner : MonoBehaviour
    {
        static readonly float[] DefaultShotTimes = { 0.8f, 2.4f, 4.0f };

        [SerializeField] float[] shotTimes = DefaultShotTimes;
        [SerializeField] string outputDirectory = "";
        [SerializeField] bool orbitCamera;
        [SerializeField] bool quitWhenDone = true;
        [SerializeField] float orbitDegreesPerSecond = 9f;

        readonly List<float> frameTimes = new List<float>(4096);
        Camera targetCamera;
        Vector3 orbitPivot;
        float orbitRadius;
        float orbitHeight;

        void Awake()
        {
            ParseCommandLine();
            Application.runInBackground = true;
            targetCamera = GetComponent<Camera>() ?? Camera.main;

            if (targetCamera != null)
            {
                var p = targetCamera.transform.position;
                orbitPivot = new Vector3(0f, 0.8f, 0f);
                orbitHeight = p.y;
                orbitRadius = new Vector2(p.x - orbitPivot.x, p.z - orbitPivot.z).magnitude;
            }

            if (string.IsNullOrEmpty(outputDirectory))
            {
                outputDirectory = Path.Combine(Directory.GetCurrentDirectory(), "screenshots");
            }
            Directory.CreateDirectory(outputDirectory);

            Debug.Log($"[SelfCheck] 输出目录 {outputDirectory}");
            Debug.Log($"[SelfCheck] 截图时间点 {string.Join(",", Array.ConvertAll(shotTimes, t => t.ToString("0.##", CultureInfo.InvariantCulture)))}");
            Debug.Log($"[SelfCheck] 图形设备 {SystemInfo.graphicsDeviceName} / {SystemInfo.graphicsDeviceType} / {SystemInfo.graphicsDeviceVersion}");
        }

        void Start()
        {
            StartCoroutine(RunSchedule());
        }

        void Update()
        {
            frameTimes.Add(Time.unscaledDeltaTime);

            if (orbitCamera && targetCamera != null)
            {
                float angle = Time.time * orbitDegreesPerSecond * Mathf.Deg2Rad;
                var pos = new Vector3(
                    orbitPivot.x + Mathf.Sin(angle) * orbitRadius,
                    orbitHeight,
                    orbitPivot.z - Mathf.Cos(angle) * orbitRadius);
                targetCamera.transform.position = pos;
                targetCamera.transform.LookAt(orbitPivot);
            }
        }

        IEnumerator RunSchedule()
        {
            Array.Sort(shotTimes);
            for (int i = 0; i < shotTimes.Length; i++)
            {
                float target = shotTimes[i];
                while (Time.time < target)
                {
                    yield return null;
                }

                yield return new WaitForEndOfFrame();
                CaptureTo(Path.Combine(outputDirectory, $"shot_{i:D2}_t{target.ToString("0.00", CultureInfo.InvariantCulture)}s.png"));
            }

            ReportFrameStats();

            if (quitWhenDone)
            {
                Debug.Log("[SelfCheck] 全部截图完成，退出");
                yield return null;
                Application.Quit(0);
            }
        }

        void CaptureTo(string path)
        {
            Texture2D tex = null;
            try
            {
                tex = ScreenCapture.CaptureScreenshotAsTexture();
                byte[] png = tex.EncodeToPNG();
                File.WriteAllBytes(path, png);
                Debug.Log($"[SelfCheck] 已截图 {path} ({tex.width}x{tex.height}, {png.Length / 1024} KB)");
            }
            catch (Exception e)
            {
                Debug.LogError($"[SelfCheck] 截图失败 {path}: {e.Message}");
            }
            finally
            {
                if (tex != null)
                {
                    Destroy(tex);
                }
            }
        }

        void ReportFrameStats()
        {
            if (frameTimes.Count == 0)
            {
                return;
            }

            var sorted = frameTimes.ToArray();
            Array.Sort(sorted);
            float sum = 0f;
            foreach (var t in sorted)
            {
                sum += t;
            }

            float mean = sum / sorted.Length;
            float p50 = sorted[sorted.Length / 2];
            float p95 = sorted[Mathf.Min(sorted.Length - 1, Mathf.RoundToInt(sorted.Length * 0.95f))];
            float p99 = sorted[Mathf.Min(sorted.Length - 1, Mathf.RoundToInt(sorted.Length * 0.99f))];

            Debug.Log(
                $"[SelfCheck] 帧统计 frames={sorted.Length} " +
                $"mean={mean * 1000f:0.00}ms ({1f / mean:0.0} fps) " +
                $"p50={p50 * 1000f:0.00}ms p95={p95 * 1000f:0.00}ms p99={p99 * 1000f:0.00}ms");
        }

        void ParseCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-manerShots" when i + 1 < args.Length:
                        var parts = args[i + 1].Split(',');
                        var times = new List<float>(parts.Length);
                        foreach (var p in parts)
                        {
                            if (float.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                            {
                                times.Add(v);
                            }
                        }
                        if (times.Count > 0)
                        {
                            shotTimes = times.ToArray();
                        }
                        break;

                    case "-manerOut" when i + 1 < args.Length:
                        outputDirectory = args[i + 1];
                        break;

                    case "-manerOrbit":
                        orbitCamera = true;
                        break;

                    case "-manerNoQuit":
                        quitWhenDone = false;
                        break;
                }
            }
        }
    }
}
