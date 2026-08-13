using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Abyssal.EditorTools;
using Overclock.Core;
using UnityEngine.UI;

namespace Overclock.EditorTools
{
    /// <summary>
    /// 从命令行构建 OVERCLOCK 场景并截图。
    ///
    ///   xvfb-run -a -s "-screen 0 1920x1080x24" $UNITY -batchmode -quit \
    ///     -projectPath Unity/Abyssal \
    ///     -executeMethod Overclock.EditorTools.OverclockSceneTools.BuildAndCapture
    ///
    /// 和 ABYSSAL 那边一样，不能加 -nographics，否则 Camera.Render 得到全黑。
    /// </summary>
    public static class OverclockSceneTools
    {
        const int GridWidth = 22;
        const int GridHeight = 14;

        static string OutDir =>
            Environment.GetEnvironmentVariable("OVERCLOCK_SHOTS") ?? "/tmp/overclock/shots";

        struct Benchmark
        {
            public string Id;
            public Vector3 Position;
            public Vector3 Euler;
            public float Fov;
        }

        static readonly Benchmark[] Benchmarks =
        {
            // 主视角：俯角 42°，能同时读出平面布局和元件的立体轮廓。
            new Benchmark { Id = "01-overview", Position = new Vector3(0f, 15.5f, -14.0f),
                            Euler = new Vector3(42f, 0f, 0f), Fov = 35f },

            // 更低的俯角，强调元件的高度差和发光的层次。
            new Benchmark { Id = "02-low-angle", Position = new Vector3(-6.5f, 6.2f, -12.5f),
                            Euler = new Vector3(23f, 22f, 0f), Fov = 38f },

            // 特写：单个热点附近，看颜色从青到红的过渡。
            new Benchmark { Id = "03-hotspot", Position = new Vector3(2.5f, 4.4f, -3.2f),
                            Euler = new Vector3(38f, -14f, 0f), Fov = 32f },

            // 俯视：接近正交，读整张网的拓扑。
            new Benchmark { Id = "04-topdown", Position = new Vector3(0f, 20.0f, -3.0f),
                            Euler = new Vector3(72f, 0f, 0f), Fov = 36f },
        };

        [MenuItem("Overclock/Build And Capture")]
        public static void BuildAndCapture()
        {
            var cam = BuildScene();
            Directory.CreateDirectory(OutDir);
            foreach (var b in Benchmarks) Capture(cam, b);
            Debug.Log($"OVERCLOCK: {Benchmarks.Length} shots written to {OutDir}");
        }

        [MenuItem("Overclock/Build Scene")]
        public static Camera BuildScene()
        {
            SetupUrp.Run();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var layer = BuildShowcaseLayer(out int layerIndex);

            var view = new LayerView();
            view.Build(layer, null);

            // 再跑一段，让热量在这个布局上稳定下来。
            for (int i = 0; i < 400; i++) layer.Step(0.05);
            view.Refresh();
            view.TickPackets(2.4f);

            var cam = CreateCamera();
            CreatePostProcessing();

            // HUD 挂在相机上。有没有 HUD 是「一段技术演示」和「一个游戏」的分水岭。
            var hud = new GameHud();
            hud.Build(null, cam);
            hud.Refresh(layer, layerIndex, 2);
            Canvas.ForceUpdateCanvases();

            Debug.Log($"OVERCLOCK: layer built {layer.Width}x{layer.Height}, " +
                      $"throughput={layer.CurrentThroughput:F1}/{layer.TargetThroughput:F1}, " +
                      $"peak={layer.PeakTemperature():F0}C, burned={layer.BurnedCount}");
            return cam;
        }

        /// <summary>
        /// 搭一个用来展示的布局：主干总线加分支导线，热点周围铺散热片，
        /// 再让几处刻意的失误烧掉，画面上才会有焦痕。
        ///
        /// 这不是随机生成的，而是手工摆出来的一个「玩得不错但也有代价」的中局状态——
        /// 宣传图要展示的是玩家玩出来的东西，不是一张空板子。
        /// </summary>
        /// <summary>
        /// 用自动玩家实际打一层出来当展示布局。
        ///
        /// 手工摆的布局是「我觉得游戏该长这样」，而自动玩家打出来的是
        /// 「按当前数值这游戏真会长成这样」。后者才有资格拿去当宣传素材，
        /// 也才能暴露出布局上真实存在的丑陋之处。
        /// </summary>
        static SiliconLayer BuildShowcaseLayer(out int layerIndex)
        {
            var run = new RunState(seed: 4242, totalLayers: 8);
            var player = new AutoPlayer(AutoPlayer.Strategy.Balanced, seed: 77);

            // 先打过前几层，让局内升级累积起来，展示的是中局而不是开局。
            SiliconLayer layer = null;
            for (int i = 0; i < 4; i++)
            {
                layer = run.CreateLayer(GridWidth, GridHeight);
                var result = player.PlayLayer(layer);
                if (!result.Cleared) break;

                var offers = run.DrawUpgrades();
                if (offers.Count > 0) run.TakeUpgrade(offers[0]);
                run.AdvanceLayer();
            }

            layerIndex = run.LayerIndex;
            return layer;
        }

        static Camera CreateCamera()
        {
            var go = new GameObject("MainCamera") { tag = "MainCamera" };
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 35f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 120f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            // 纯黑背景。整个方案的视觉前提就是「除了发光的东西，什么都看不见」。
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

            // Bloom 是这套视觉的核心。所有的「体积感」都来自发光线条的溢出，
            // 因为场景里根本没有光照。阈值压到 0.6 让中等亮度的线也参与发光。
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(0.92f);
            bloom.intensity.Override(1.35f);
            bloom.scatter.Override(0.78f);
            bloom.tint.Override(new Color(0.86f, 0.96f, 1.00f));
            bloom.highQualityFiltering.Override(true);

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.36f);
            vignette.smoothness.Override(0.62f);
            vignette.color.Override(Color.black);

            var grading = profile.Add<ColorAdjustments>(true);
            grading.postExposure.Override(0.20f);
            grading.contrast.Override(16f);
            grading.saturation.Override(22f);

            var aberration = profile.Add<ChromaticAberration>(true);
            aberration.intensity.Override(0.22f);

            var grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.16f);

            var tonemap = profile.Add<Tonemapping>(true);
            tonemap.mode.Override(TonemappingMode.ACES);

            volume.sharedProfile = profile;
        }

        /// <summary>
        /// 渲染一个基准点。MSAA 的目标不能直接 ReadPixels，
        /// 必须先 Blit 到单采样 RT 完成 resolve，否则拿到纯黑图且不会报错。
        /// </summary>
        static void Capture(Camera cam, Benchmark b)
        {
            const int width = 1920, height = 1080;

            cam.transform.position = b.Position;
            cam.transform.eulerAngles = b.Euler;
            cam.fieldOfView = b.Fov;

            var msaa = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR)
            {
                antiAliasing = 4,
            };
            var resolved = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 1,
            };

            cam.targetTexture = msaa;
            cam.Render();
            cam.targetTexture = null;

            Graphics.Blit(msaa, resolved);

            RenderTexture.active = resolved;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            string path = Path.Combine(OutDir, $"{b.Id}.png");
            File.WriteAllBytes(path, tex.EncodeToPNG());

            UnityEngine.Object.DestroyImmediate(tex);
            msaa.Release();
            resolved.Release();
            UnityEngine.Object.DestroyImmediate(msaa);
            UnityEngine.Object.DestroyImmediate(resolved);

            Debug.Log($"OVERCLOCK: captured {b.Id} -> {path} ({new FileInfo(path).Length} bytes)");
        }
    }
}
