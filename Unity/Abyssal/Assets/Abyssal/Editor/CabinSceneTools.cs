using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Abyssal.Core;
using Abyssal.Visual;

namespace Abyssal.EditorTools
{
    /// <summary>
    /// 从命令行构建控制舱场景并拍定点截图。
    ///
    /// 这是「边跑边自检」流水线的第 2 层。每个基准点的摄像机参数完全固定，
    /// 所以任意两个版本之间的截图可以逐像素对比，画面变了就是有东西改了。
    ///
    ///   xvfb-run -a -s "-screen 0 1920x1080x24" $UNITY -batchmode -quit \
    ///     -projectPath Unity/Abyssal -executeMethod Abyssal.EditorTools.CabinSceneTools.BuildAndCapture
    ///
    /// 注意不能加 -nographics，否则 Camera.Render 得到全黑。
    /// </summary>
    public static class CabinSceneTools
    {
        const string SceneDir = "Assets/Abyssal/Scenes";

        static CabinBuilder _lastBuilder;

        static string OutDir =>
            System.Environment.GetEnvironmentVariable("ABYSSAL_SHOTS") ?? "/tmp/abyssal/shots";

        /// <summary>
        /// 视觉基准点。每一条都对应一个玩家真实会长时间盯着的构图。
        /// 加新基准点只能往后追加，不要改已有条目的参数，否则历史截图就没法对比了。
        /// </summary>
        struct Benchmark
        {
            public string Id;
            public Vector3 Position;
            public Vector3 Euler;
            public float Fov;
        }

        static readonly Benchmark[] Benchmarks =
        {
            // 主视角：站姿眼高 1.68 m，FOV 55°，这是玩家 90% 的时间看到的画面。
            // 俯角 15° 让控制台和台面占据画面下三分之二，视觉重心随之下移到仪表排上，
            // 和参考构图对齐。俯角小于这个值时画面重心会飘到上方的空舱壁上。
            new Benchmark { Id = "01-station", Position = new Vector3(0f, 1.68f, -0.55f),
                            Euler = new Vector3(15f, 0f, 0f), Fov = 55f },

            // 专注视角：靠近主仪表排，FOV 收到 40°，模拟操作时的镜头吸附。
            new Benchmark { Id = "02-focus-gauges", Position = new Vector3(0f, 1.52f, 0.24f),
                            Euler = new Vector3(14f, 0f, 0f), Fov = 40f },

            // 操作件特写：下排旋钮和拨杆。
            new Benchmark { Id = "03-focus-controls", Position = new Vector3(-0.28f, 1.44f, 0.30f),
                            Euler = new Vector3(34f, 8f, 0f), Fov = 44f },

            // 告警屏：上方垂直面板，检查告警灯在暗环境里的辨识度。
            new Benchmark { Id = "04-alarm-row", Position = new Vector3(0f, 1.70f, 0.30f),
                            Euler = new Vector3(-6f, 0f, 0f), Fov = 48f },

            // 侧台：检查侧面板有没有被照到。
            new Benchmark { Id = "05-port-console", Position = new Vector3(-0.55f, 1.60f, -0.20f),
                            Euler = new Vector3(16f, -42f, 0f), Fov = 52f },

            // 舷窗：检查窗外的深海——探照灯、悬浮颗粒、钢结构剪影。
            new Benchmark { Id = "06-porthole", Position = new Vector3(0f, 1.78f, 0.55f),
                            Euler = new Vector3(2f, 0f, 0f), Fov = 52f },

            // 广角全景：不是玩家视角，用于快速看清整个舱室布局有没有破绽。
            new Benchmark { Id = "07-wide", Position = new Vector3(-1.15f, 2.05f, -1.05f),
                            Euler = new Vector3(16f, 26f, 0f), Fov = 72f },
        };

        // ------------------------------------------------------------------ 入口

        [MenuItem("Abyssal/Build Cabin Scene")]
        public static Camera BuildScene()
        {
            SetupUrp.Run();

            Directory.CreateDirectory(SceneDir);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var builder = new CabinBuilder();
            builder.Build();
            _lastBuilder = builder;

            SeedInstrumentReadings(builder);
            var cam = CreateCamera(builder);
            CreatePostProcessing();

            // 这个场景刻意不保存。舱内所有贴图和材质都是运行时对象，
            // 存下来会把上百张程序化贴图序列化进场景文件（实测约 49 MB），
            // 而且重新打开时这些引用会失效、渲染出全黑。
            // 真正入库的启动场景由 BuildPipelineTools.CreateBootScene 生成，只有几 KB。
            Debug.Log($"ABYSSAL: cabin built in memory ({builder.Gauges.Count} gauges, " +
                      $"{builder.Lamps.Count} lamps, {builder.Knobs.Count} knobs)");
            return cam;
        }

        /// <summary>
        /// 建场景并立刻截图。
        ///
        /// 关键在于「立刻」：贴图和材质都是运行时 new 出来的，不是磁盘上的资产。
        /// 一旦重新打开场景，这些引用就断了，渲染结果会是全黑。
        /// 所以截图必须复用刚构建出来的那个相机，不能走 OpenScene。
        /// </summary>
        [MenuItem("Abyssal/Capture Benchmarks")]
        public static void BuildAndCapture()
        {
            var cam = BuildScene();
            Capture(cam);
        }

        /// <summary>
        /// 拍同一个机位在四种事故状态下的画面。
        ///
        /// 这组图回答的是「单场景会不会看腻」这个问题：如果正常、警报、井喷、断电
        /// 四张图放在一起看起来像四个不同的地方，那这个方案的画面就撑得住整局游戏。
        /// </summary>
        [MenuItem("Abyssal/Capture Event States")]
        public static void CaptureEventStates()
        {
            var cam = BuildScene();
            var builder = _lastBuilder;
            if (builder?.Atmosphere == null)
            {
                Debug.LogError("ABYSSAL: atmosphere not built");
                EditorApplication.Exit(6);
                return;
            }

            (string id, CabinAtmosphere.State state, float severity)[] states =
            {
                ("state-1-normal", CabinAtmosphere.State.Normal, 0f),
                ("state-2-alarm", CabinAtmosphere.State.Alarm, 0.7f),
                ("state-3-blowout", CabinAtmosphere.State.Blowout, 1f),
                ("state-4-blackout", CabinAtmosphere.State.BlackOut, 1f),
            };

            Directory.CreateDirectory(OutDir);

            foreach (var (id, state, severity) in states)
            {
                builder.Atmosphere.Apply(state, severity);

                // 旋转警报灯的亮度取决于扫描角度。批处理下时间不流动，
                // 手工推进到光束正对舱内的角度，否则拍到的可能是它背对的那一瞬间。
                builder.Atmosphere.Tick(0.42f);
                builder.Atmosphere.PrewarmForCapture(2.5f);

                // 断电时窗外的探照灯也会跟着灭——那是平台自己的电。
                builder.Underwater?.SetFloodlight(
                    state == CabinAtmosphere.State.BlackOut ? 0.18f : 1f);

                CaptureSingle(cam, Benchmarks[0], id);
            }

            Debug.Log($"ABYSSAL: {states.Length} event-state shots written to {OutDir}");
        }

        [MenuItem("Abyssal/Build And Diagnose")]
        public static void BuildAndDiagnose()
        {
            BuildScene();
            RenderDiagnostics.Run();
            RenderDiagnostics.DiagnoseMaterials();
        }

        // ------------------------------------------------------------------ 场景内容

        /// <summary>
        /// 把仪表打到一组「正在正常钻进」的读数上。
        /// 全零的仪表盘会让截图看起来像未完成的工程场景，而不是运行中的设备。
        /// </summary>
        static void SeedInstrumentReadings(CabinBuilder builder)
        {
            var sim = new DrillSimulation(WellProfiles.ShiftOne(),
                new DrillState { Depth = 1985.0, MudDensity = 1265.0, BitWear = 0.31 });

            var controls = DrillControls.NominalDrilling;
            controls.TargetMudDensity = 1265.0;
            for (int i = 0; i < 900; i++) sim.Step(controls, 0.2);

            var s = sim.State;

            void Set(string key, float t)
            {
                if (builder.Gauges.TryGetValue(key, out var g)) g.SetNormalized(t);
            }

            Set("wob", (float)(controls.WeightOnBit / 300.0));
            Set("rpm", (float)(controls.RotarySpeed / 200.0));
            Set("torque", (float)(s.Torque / 50.0));
            Set("ecd", (float)((s.EquivalentCirculatingDensity - 1000.0) / 1000.0));
            Set("pump", (float)(controls.PumpRate / 60.0));
            Set("rop", (float)(s.RateOfPenetration / 40.0));
            Set("pit", (float)((s.PitVolume - 50.0) / 25.0));
            Set("temp", (float)(s.BottomholeTemperature / 200.0));
            Set("wear", (float)s.BitWear);
            Set("depth", (float)((s.Depth - 1800.0) / 1600.0));
            Set("p1", 0.58f); Set("p2", 0.44f); Set("p3", 0.22f);
            Set("s1", 0.61f); Set("s2", 0.52f); Set("s3", 0.71f);

            void Knob(string key, float t)
            {
                if (builder.Knobs.TryGetValue(key, out var k)) k.SetNormalized(t);
            }

            Knob("wob", (float)(controls.WeightOnBit / 300.0));
            Knob("rpm", (float)(controls.RotarySpeed / 200.0));
            Knob("mud", (float)((controls.TargetMudDensity - 1000.0) / 1200.0));
            Knob("pump", (float)(controls.PumpRate / 60.0));
            Knob("choke", 1f);
            Knob("pk1", 0.42f); Knob("pk2", 0.66f);
            Knob("sk1", 0.55f); Knob("sk2", 0.30f);

            void Lever(string key, float p)
            {
                if (builder.Levers.TryGetValue(key, out var l)) l.SetPosition(p);
            }

            Lever("bit", 1f);
            Lever("bop", -1f);
            Lever("degas", -1f);
            Lever("pl1", -1f);
            Lever("sl1", 1f);

            // 绝大多数告警灯是暗的。只有电源、通讯、运行这三盏常亮，
            // 这样任何一盏红灯亮起来都会立刻抓住注意力。
            var lit = new Dictionary<string, float>
            {
                ["pwr"] = 1.0f, ["comm"] = 0.85f, ["run"] = 1.0f,
                ["plamp"] = 0.9f, ["slamp"] = 0.9f,
                ["torq"] = 0.0f, ["kick"] = 0.0f, ["loss"] = 0.0f, ["gas"] = 0.0f,
                ["stuck"] = 0.0f, ["temp"] = 0.0f, ["wear"] = 0.22f, ["pump"] = 0.75f,
                ["hold"] = 0.0f,
                ["esd"] = 0.0f, ["muster"] = 0.0f, ["fire"] = 0.0f, ["abandon"] = 0.0f,
            };

            foreach (var pair in builder.Lamps)
                pair.Value.SetIntensity(lit.TryGetValue(pair.Key, out float v) ? v : 0.0f);
        }

        static Camera CreateCamera(CabinBuilder builder)
        {
            var go = new GameObject("MainCamera") { tag = "MainCamera" };
            var cam = go.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(
                builder.CameraAnchor.position, builder.CameraAnchor.rotation);
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 60f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.004f, 0.008f, 0.010f);
            cam.allowHDR = true;
            cam.allowMSAA = true;

            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            data.renderShadows = true;

            return cam;
        }

        /// <summary>
        /// 后处理配置。参数是按「让暗部真的黑、让亮点真的刺眼」这一条来调的，
        /// 对应概念图里那种只有仪表在发光的观感。
        /// </summary>
        static void CreatePostProcessing()
        {
            var go = new GameObject("PostProcessing");
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1f;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "AbyssalProfile";

            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.05f);
            bloom.intensity.Override(0.95f);
            bloom.scatter.Override(0.74f);
            bloom.tint.Override(new Color(1.00f, 0.86f, 0.68f));
            bloom.highQualityFiltering.Override(true);

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.44f);
            vignette.smoothness.Override(0.52f);
            vignette.color.Override(new Color(0.02f, 0.014f, 0.008f));

            // 整条调色链都在往暖里推：滤镜偏橙、饱和度上提、对比度加大。
            // 目标是概念图那种「一盏钨丝灯把整个舱室烤成锈色」的观感，
            // 而不是默认渲染出来的那种中性青灰工业风。
            var grading = profile.Add<ColorAdjustments>(true);
            grading.postExposure.Override(0.10f);
            grading.contrast.Override(26f);
            grading.saturation.Override(9f);
            grading.colorFilter.Override(new Color(1.03f, 0.94f, 0.84f));

            // 阴影往冷里压一点点，和暖高光形成色温分离，这是让暗部不发灰的关键。
            var curves = profile.Add<ShadowsMidtonesHighlights>(true);
            curves.shadows.Override(new Vector4(0.88f, 0.94f, 1.06f, -0.08f));
            curves.midtones.Override(new Vector4(1.03f, 0.99f, 0.94f, 0.02f));
            curves.highlights.Override(new Vector4(1.06f, 0.96f, 0.86f, 0.05f));

            var grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Medium2);
            grain.intensity.Override(0.28f);
            grain.response.Override(0.75f);

            var aberration = profile.Add<ChromaticAberration>(true);
            aberration.intensity.Override(0.16f);

            var tonemap = profile.Add<Tonemapping>(true);
            tonemap.mode.Override(TonemappingMode.ACES);

            volume.sharedProfile = profile;

            AssetDatabase.CreateAsset(profile, "Assets/Abyssal/Settings/AbyssalProfile.asset");
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------ 截图

        public static void Capture(Camera cam)
        {
            if (cam == null)
            {
                Debug.LogError("ABYSSAL: no camera to capture with");
                EditorApplication.Exit(2);
                return;
            }

            Directory.CreateDirectory(OutDir);
            int width = 1920, height = 1080;

            foreach (var b in Benchmarks)
            {
                CaptureSingle(cam, b, b.Id);
            }

            Debug.Log($"ABYSSAL: {Benchmarks.Length} benchmark shots written to {OutDir}");
        }

        /// <summary>
        /// 渲染一个基准点。
        ///
        /// 相机渲染到开了 MSAA 的 HDR 目标，但 ReadPixels 读不了多重采样的表面，
        /// 必须先 Blit 到一张单采样 RT 完成 resolve。
        /// 少了这一步拿到的是一张纯黑图，而且不会有任何报错。
        /// </summary>
        static void CaptureSingle(Camera cam, Benchmark b, string id)
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

            string path = Path.Combine(OutDir, $"{id}.png");
            File.WriteAllBytes(path, tex.EncodeToPNG());

            Object.DestroyImmediate(tex);
            msaa.Release();
            resolved.Release();
            Object.DestroyImmediate(msaa);
            Object.DestroyImmediate(resolved);

            Debug.Log($"ABYSSAL: captured {id} -> {path} ({new FileInfo(path).Length} bytes)");
        }
    }
}
