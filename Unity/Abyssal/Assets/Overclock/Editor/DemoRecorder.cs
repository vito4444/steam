using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Abyssal.EditorTools;
using Overclock.Core;

namespace Overclock.EditorTools
{
    /// <summary>
    /// 录制一段脚本化的演示序列，逐帧输出 PNG，再用 ffmpeg 合成视频。
    ///
    /// 静态截图判断不了一个自动化游戏好不好玩——它的乐趣全在过程里：
    /// 数据开始流动的那一刻、热量沿着主干爬上去的那几秒、
    /// 某一格终于烧掉时整条线断开、以及抢着补散热片把它救回来。
    /// 这些必须动起来才看得出来。
    ///
    ///   xvfb-run -a -s "-screen 0 1280x720x24" $UNITY -batchmode -quit \
    ///     -projectPath Unity/Abyssal \
    ///     -executeMethod Overclock.EditorTools.DemoRecorder.Record
    /// </summary>
    public static class DemoRecorder
    {
        const int Width = 1280;
        const int Height = 720;
        const int Fps = 24;

        /// <summary>仿真时间相对真实时间的倍率。热量累积在真实速度下太慢，撑不成一段演示。</summary>
        const double SimulationSpeed = 9.0;

        const int GridWidth = 16;
        const int GridHeight = 10;

        static string OutDir =>
            Environment.GetEnvironmentVariable("OVERCLOCK_FRAMES") ?? "/tmp/overclock/frames";

        /// <summary>演示时间轴上的一个动作。</summary>
        struct Beat
        {
            public float Time;
            public Action<SiliconLayer> Action;
            public string Caption;
        }

        /// <summary>
        /// 只跑三帧并打印诊断，用来定位「录出来全是黑」这类问题。
        /// 逐帧录制一次要跑几百帧，靠完整录制来试错太慢。
        /// </summary>
        [MenuItem("Overclock/Diagnose Demo Frame")]
        public static void DiagnoseFrame()
        {
            SetupUrp.Run();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var layer = BuildDemoLayer();
            LayerGenerator.LayNaiveRoute(layer, ComponentKind.Bus);

            var view = new LayerView();
            view.Build(layer, null);
            for (int i = 0; i < 200; i++) layer.Step(0.05);
            view.Refresh();
            view.TickPackets(1.0f);

            var cam = CreateCamera();
            CreatePostProcessing();

            var renderers = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
            Debug.Log($"DEMO| renderer 总数 = {renderers.Length}");
            Debug.Log($"DEMO| 活跃 renderer = {System.Linq.Enumerable.Count(renderers, r => r.gameObject.activeInHierarchy && r.enabled)}");

            for (int frame = 0; frame < 3; frame++)
            {
                MoveCamera(cam, frame * 6f, 18.5f);

                var planes = GeometryUtility.CalculateFrustumPlanes(cam);
                int visible = System.Linq.Enumerable.Count(renderers,
                    r => r.gameObject.activeInHierarchy && GeometryUtility.TestPlanesAABB(planes, r.bounds));

                Debug.Log($"DEMO| frame {frame}: pos={cam.transform.position} " +
                          $"euler={cam.transform.eulerAngles} fov={cam.fieldOfView:F1} 视锥内={visible}");

                var rt = new RenderTexture(320, 180, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = null;

                RenderTexture.active = rt;
                var tex = new Texture2D(320, 180, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 320, 180), 0, 0);
                tex.Apply();
                RenderTexture.active = null;

                long sum = 0;
                int maxV = 0;
                foreach (var px in tex.GetPixels32())
                {
                    int v = px.r + px.g + px.b;
                    sum += v;
                    if (v > maxV) maxV = v;
                }

                Debug.Log($"DEMO| frame {frame}: 平均亮度={sum / (double)(320 * 180) / 3.0:F2} 最亮={maxV / 3.0:F0}");

                UnityEngine.Object.DestroyImmediate(tex);
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
        }

        [MenuItem("Overclock/Record Demo")]
        public static void Record()
        {
            SetupUrp.Run();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var layer = BuildDemoLayer();
            var view = new LayerView();
            view.Build(layer, null);

            var cam = CreateCamera();
            CreatePostProcessing();

            var beats = BuildTimeline(layer);
            float duration = 18.5f;
            int totalFrames = Mathf.RoundToInt(duration * Fps);

            Directory.CreateDirectory(OutDir);
            foreach (var stale in Directory.GetFiles(OutDir, "*.png")) File.Delete(stale);

            int nextBeat = 0;
            float dt = 1f / Fps;

            for (int frame = 0; frame < totalFrames; frame++)
            {
                float t = frame * dt;

                // 触发到点的时间轴动作。
                while (nextBeat < beats.Count && beats[nextBeat].Time <= t)
                {
                    beats[nextBeat].Action?.Invoke(layer);
                    if (!string.IsNullOrEmpty(beats[nextBeat].Caption))
                        Debug.Log($"OVERCLOCK: t={t:F1}s  {beats[nextBeat].Caption}");
                    nextBeat++;
                }

                // 仿真按倍速推进，但每步不超过 0.1 秒，否则显式差分的热扩散会发散。
                double remaining = dt * SimulationSpeed;
                while (remaining > 1e-6)
                {
                    double step = Math.Min(0.08, remaining);
                    layer.Step(step);
                    remaining -= step;
                }

                view.Refresh();
                view.TickPackets(t);
                MoveCamera(cam, t, duration);

                CaptureFrame(cam, frame);
            }

            Debug.Log($"OVERCLOCK: recorded {totalFrames} frames to {OutDir}  " +
                      $"final throughput={layer.CurrentThroughput:F1}/{layer.TargetThroughput:F1} " +
                      $"peak={layer.PeakTemperature():F0}C burned={layer.BurnedCount}");
        }

        static SiliconLayer BuildDemoLayer()
        {
            var layer = LayerGenerator.Generate(23, GridWidth, GridHeight, 0.35);
            layer.Budget = 100000;
            layer.TargetThroughput = 14.0;
            return layer;
        }

        /// <summary>
        /// 演示的剧本。刻意安排成「贪心 → 出事 → 抢修」这条弧线，
        /// 因为那正是这个游戏每一层的真实节奏。
        /// </summary>
        static List<Beat> BuildTimeline(SiliconLayer layer)
        {
            int midY = GridHeight / 2;
            var beats = new List<Beat>();

            // 1.0–2.6 秒：铺主干总线。玩家追求吞吐，一上来就用最粗的元件。
            for (int x = 1; x < GridWidth - 1; x++)
            {
                int col = x;
                beats.Add(new Beat
                {
                    Time = 1.0f + (x - 1) * 0.11f,
                    Action = l => l.Place(col, midY, ComponentKind.Bus),
                    Caption = x == 1 ? "铺设主干总线" : null,
                });
            }

            // 2.8–5.2 秒：把上下每一排都接进来。
            // 画面要铺满才有「一整张网在运转」的感觉，稀疏的几条线在纯黑背景上撑不住构图。
            int[] rows = { midY - 3, midY - 2, midY + 2, midY + 3 };
            for (int r = 0; r < rows.Length; r++)
            {
                for (int x = 1; x < GridWidth - 1; x++)
                {
                    int col = x, row = rows[r];
                    beats.Add(new Beat
                    {
                        Time = 2.8f + r * 0.55f + (x - 1) * 0.035f,
                        Action = l => l.Place(col, row, ComponentKind.Trace),
                        Caption = (r == 0 && x == 1) ? "铺开数据网" : null,
                    });
                }
            }

            // 5.4–6.2 秒：竖向支路，把每一排都连到主干上。
            for (int i = 0; i < 5; i++)
            {
                int col = 2 + i * 3;
                beats.Add(new Beat
                {
                    Time = 5.4f + i * 0.16f,
                    Action = l =>
                    {
                        for (int y = midY - 3; y <= midY + 3; y++)
                            if (l.CellAt(col, y) == ComponentKind.Empty)
                                l.Place(col, y, ComponentKind.Trace);
                    },
                    Caption = i == 0 ? "接入竖向支路" : null,
                });
            }

            beats.Add(new Beat { Time = 7.0f, Caption = "满载运行，主干开始积热" });
            beats.Add(new Beat { Time = 9.5f, Caption = "总线逼近熔点" });

            // 11.0 秒起：抢修。先补散热片压温度，再补备份线消除单点故障。
            for (int x = 1; x < GridWidth - 1; x++)
            {
                int col = x;
                beats.Add(new Beat
                {
                    Time = 11.0f + (x - 1) * 0.10f,
                    Action = l =>
                    {
                        l.Place(col, midY - 1, ComponentKind.HeatSink);
                        l.Place(col, midY + 1, ComponentKind.HeatSink);
                    },
                    Caption = x == 1 ? "抢修：沿主干补散热片" : null,
                });
            }

            beats.Add(new Beat
            {
                Time = 13.6f,
                Action = l =>
                {
                    // 散热片把主干和上下两排隔开了，两端要重新打通。
                    foreach (int x in new[] { 1, GridWidth - 2 })
                    {
                        l.Place(x, midY - 1, ComponentKind.Trace);
                        l.Place(x, midY + 1, ComponentKind.Trace);
                    }
                },
                Caption = "两端重新打通",
            });

            beats.Add(new Beat { Time = 16.0f, Caption = "温度回落，系统稳住" });
            beats.Sort((a, b) => a.Time.CompareTo(b.Time));
            return beats;
        }

        /// <summary>
        /// 缓慢的推轨镜头。全程静止的机位会让观众以为画面卡住了，
        /// 一点持续的位移就能让整段演示读起来是活的。
        /// </summary>
        static void MoveCamera(Camera cam, float t, float duration)
        {
            float p = Mathf.Clamp01(t / duration);
            float ease = Mathf.SmoothStep(0f, 1f, p);

            float pitch = Mathf.Lerp(48f, 31f, ease);
            float yaw = Mathf.Lerp(-11f, 11f, ease);
            float dist = Mathf.Lerp(14.5f, 12.0f, ease);

            // 相机位置必须从注视点反推：先定朝向，再沿视线方向退开一段距离。
            // 反过来先定位置再定朝向的话，视线落点会随俯角漂走——
            // 之前那版就是这么把镜头对到了网格外十几米的空处，录出一整段纯黑。
            var target = new Vector3(0f, 0.4f, 0f);
            var rot = Quaternion.Euler(pitch, yaw, 0f);

            cam.transform.rotation = rot;
            cam.transform.position = target - rot * Vector3.forward * dist;
            cam.fieldOfView = Mathf.Lerp(36f, 40f, ease);
        }

        static Camera CreateCamera()
        {
            var go = new GameObject("MainCamera") { tag = "MainCamera" };
            var cam = go.AddComponent<Camera>();
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 120f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.allowHDR = true;
            cam.allowMSAA = true;

            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            data.renderShadows = false;

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.fog = false;
            return cam;
        }

        static void CreatePostProcessing()
        {
            var go = new GameObject("PostProcessing");
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1f;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();

            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(0.92f);
            bloom.intensity.Override(1.35f);
            bloom.scatter.Override(0.78f);
            bloom.tint.Override(new Color(0.86f, 0.96f, 1.00f));
            bloom.highQualityFiltering.Override(true);

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.34f);
            vignette.smoothness.Override(0.62f);
            vignette.color.Override(Color.black);

            var grading = profile.Add<ColorAdjustments>(true);
            grading.postExposure.Override(0.18f);
            grading.contrast.Override(16f);
            grading.saturation.Override(24f);

            var aberration = profile.Add<ChromaticAberration>(true);
            aberration.intensity.Override(0.20f);

            var tonemap = profile.Add<Tonemapping>(true);
            tonemap.mode.Override(TonemappingMode.ACES);

            volume.sharedProfile = profile;
        }

        static void CaptureFrame(Camera cam, int frame)
        {
            // 采样数必须用 4。llvmpipe 下 antiAliasing = 2 的多重采样目标
            // Blit 出来是全黑的，而且不报任何错——录了 444 帧才发现整段是黑的。
            var msaa = new RenderTexture(Width, Height, 24, RenderTextureFormat.DefaultHDR)
            {
                antiAliasing = 4,
            };
            var resolved = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 1,
            };

            cam.targetTexture = msaa;
            cam.Render();
            cam.targetTexture = null;

            Graphics.Blit(msaa, resolved);

            RenderTexture.active = resolved;
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            File.WriteAllBytes(Path.Combine(OutDir, $"frame_{frame:D5}.png"), tex.EncodeToPNG());

            UnityEngine.Object.DestroyImmediate(tex);
            msaa.Release();
            resolved.Release();
            UnityEngine.Object.DestroyImmediate(msaa);
            UnityEngine.Object.DestroyImmediate(resolved);
        }
    }
}
