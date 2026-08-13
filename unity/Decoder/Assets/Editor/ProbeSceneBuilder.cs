using System;
using System.Collections.Generic;
using System.IO;
using Decoder.Capture;
using Decoder.Gameplay;
using Decoder.Interaction;
using Decoder.Signal;
using Decoder.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

namespace Decoder.EditorTools
{
    /// <summary>
    /// 程序化生成美术探针场景。目的不是做最终关卡，而是用零美术资源验证
    /// 方案 A 的画面方向能否成立：封闭工位 + 高密度可交互仪表 + 三光源配色
    /// （暖黄台灯 / 绿色 CRT / 冷蓝窗光）。
    ///
    /// 生成结果完全由代码决定，因此可以反复重建、逐版比对，
    /// 这是无 GPU 环境下做画面迭代的基础。
    /// </summary>
    public static class ProbeSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/ArtProbe.unity";
        private const string PlayableScenePath = "Assets/Scenes/Station.unity";
        private const string ConfigPath = "Assets/Config/lighting-probe.json";

        /// <summary>生成可玩场景时，主调频旋钮用哪一个。</summary>
        private const string TuningKnobName = "Knob_1_3";

        [Serializable]
        private class LightCfg
        {
            public float intensity = 1f;
            public float range = 2f;
            public float spotAngle = 60f;
        }

        [Serializable]
        private class LightsCfg
        {
            public LightCfg keyLampWarm = new();
            public LightCfg fillCrtGreen = new();
            public LightCfg rimColdWindow = new();
            public LightCfg bounceDeskWarm = new();
            public LightCfg practicalNeon = new();
        }

        [Serializable]
        private class EmissionCfg
        {
            public float crtScreen = 1f;
            public float neonLamp = 3f;
            public float meterFace = 0.3f;
            public float dialStrip = 0.45f;
            public float frostedGlass = 0.85f;
            public float labelPlate = 0.16f;
            public float indicatorLamp = 2f;
        }

        [Serializable]
        private class ProbeLightingConfig
        {
            public float[] ambientSky = { 0.0016f, 0.0020f, 0.0028f };
            public float[] ambientEquator = { 0.0010f, 0.0012f, 0.0016f };
            public float[] ambientGround = { 0.0006f, 0.0006f, 0.0008f };
            public float fogDensity = 0.05f;
            public LightsCfg lights = new();
            public EmissionCfg emission = new();
        }

        private static ProbeLightingConfig _cfg;

        private static void LoadConfig()
        {
            var full = Path.Combine(Application.dataPath, "..", ConfigPath);
            if (File.Exists(full))
            {
                _cfg = JsonUtility.FromJson<ProbeLightingConfig>(File.ReadAllText(full))
                       ?? new ProbeLightingConfig();
                Log($"已加载光照配置 {ConfigPath}");
            }
            else
            {
                _cfg = new ProbeLightingConfig();
                Log($"未找到 {ConfigPath}，使用内置默认光照配置");
            }
        }

        private static Color Rgb(IReadOnlyList<float> v, Color fallback)
        {
            return v is { Count: >= 3 } ? new Color(v[0], v[1], v[2]) : fallback;
        }

        private static readonly Color WarmLamp = new(1.0f, 0.72f, 0.36f);
        private static readonly Color CrtGreen = new(0.30f, 1.0f, 0.45f);
        private static readonly Color ColdWindow = new(0.45f, 0.62f, 1.0f);
        private static readonly Color NeonAmber = new(1.0f, 0.45f, 0.12f);

        private static Material _steelDark;
        private static Material _steelOlive;
        private static Material _bakelite;
        private static Material _concrete;
        private static Material _paper;
        private static Material _brassKnob;

        public static void Build()
        {
            Generate(playable: false, ScenePath, "PROBE_SCENE_BUILT");
        }

        /// <summary>
        /// 生成可玩场景。几何、材质、光照与探针场景完全一致，
        /// 额外挂上接收机、交互控件、玩家视角和界面。
        /// 共用同一套生成代码，美术调整会同时反映到两个场景上。
        /// </summary>
        public static void BuildPlayable()
        {
            Generate(playable: true, PlayableScenePath, "STATION_SCENE_BUILT");
        }

        private static void Generate(bool playable, string scenePath, string successMarker)
        {
            try
            {
                GenerateScene(playable, scenePath);
                Log($"{successMarker} {scenePath}");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ProbeSceneBuilder] 生成失败: {e}");
                Console.Error.WriteLine($"[ProbeSceneBuilder] 生成失败: {e}");
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// 生成并保存场景，不退出编辑器。
        ///
        /// 单独暴露出来是为了让"生成场景"和"构建播放器"能在同一个编辑器进程里连着做。
        /// 分成两次进程跑会踩到资源导入状态不同步的坑：场景里的材质引用指向
        /// 上一进程刚写盘、本进程还没导入完的资源，构建出来的 level0 会损坏，
        /// 而构建过程本身一句警告都不会给，只有运行时才崩。
        /// </summary>
        public static string GenerateScene(bool playable, string scenePath)
        {
            Random.InitState(20260813);
            LoadConfig();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateMaterials();

            BuildLightingEnvironment();
            BuildRoomShell();
            BuildInstrumentWall();
            BuildDesk();
            BuildWindowWall();
            BuildArchiveWall();
            BuildLights();
            BuildCameras();

            if (playable)
            {
                BuildPlayableRig();
            }

            Directory.CreateDirectory(Path.GetDirectoryName(scenePath)!);
            EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            return scenePath;
        }

        public const string StationScenePath = PlayableScenePath;
        public const string ProbeScenePath = ScenePath;

        /// <summary>把静态布景接成可玩的工位。</summary>
        private static void BuildPlayableRig()
        {
            var systems = new GameObject("Systems").transform;

            var receiverGo = new GameObject("RadioReceiver",
                typeof(AudioSource), typeof(RadioReceiver));
            receiverGo.transform.SetParent(systems, false);
            var receiver = receiverGo.GetComponent<RadioReceiver>();

            var hudGo = new GameObject("StationHud", typeof(StationHud));
            hudGo.transform.SetParent(systems, false);
            var hud = hudGo.GetComponent<StationHud>();
            hud.receiver = receiver;

            // 自动演练驱动。默认不启动，加 -playtest 参数才会跑，
            // 这样它既能用于自动化验证，又不会打扰真正的玩家。
            var driverGo = new GameObject("PlaytestDriver", typeof(PlaytestDriver));
            driverGo.transform.SetParent(systems, false);
            var driver = driverGo.GetComponent<PlaytestDriver>();
            driver.receiver = receiver;
            driver.hud = hud;

            // 主调频旋钮：恢复被布景流程删掉的碰撞体，并放大成便于点中的尺寸。
            var knob = GameObject.Find(TuningKnobName);
            if (knob == null)
            {
                throw new InvalidOperationException(
                    $"未找到用作调频旋钮的物件 {TuningKnobName}，仪表墙布局可能改过了");
            }

            knob.name = "TuningKnob";
            var collider = knob.AddComponent<CapsuleCollider>();
            collider.direction = 1;
            collider.radius = 1.6f;
            collider.height = 4f;

            var tuner = knob.AddComponent<TuningKnob>();
            tuner.receiver = receiver;
            tuner.hoverLabel = "主调谐";
            // 旋钮模型是躺倒的圆柱，绕自身 Y 轴转才是面向玩家的转动。
            tuner.rotationAxis = Vector3.up;

            // 电源拨杆
            var powerSwitch = GameObject.Find("ToggleBase_0_0");
            if (powerSwitch != null)
            {
                powerSwitch.name = "PowerSwitch";
                var switchCollider = powerSwitch.AddComponent<BoxCollider>();
                switchCollider.size = new Vector3(2.6f, 2.6f, 6f);
                var toggle = powerSwitch.AddComponent<PowerSwitch>();
                toggle.hoverLabel = "电源";
                toggle.receiver = receiver;
                toggle.rotationAxis = Vector3.right;
            }

            // 玩家视角接管主机位
            var mainShot = GameObject.Find("probe_front");
            if (mainShot == null)
            {
                throw new InvalidOperationException("未找到主机位 probe_front");
            }

            mainShot.name = "PlayerCamera";
            mainShot.AddComponent<StationInteractor>();
            mainShot.AddComponent<AudioListener>();

            // 其余机位在可玩场景里只作为截图用，保持关闭。
            Log("可玩场景已接线：接收机、调频旋钮、电源拨杆、玩家视角、界面");
        }

        // ---------- 材质 ----------

        private static void CreateMaterials()
        {
            const string dir = "Assets/Materials/Probe";
            Directory.CreateDirectory(dir);

            _steelDark = MakeMaterial(dir, "SteelDark", new Color(0.10f, 0.11f, 0.11f), 0.55f, 0.45f);
            _steelOlive = MakeMaterial(dir, "SteelOlive", new Color(0.19f, 0.21f, 0.16f), 0.35f, 0.60f);
            _bakelite = MakeMaterial(dir, "Bakelite", new Color(0.045f, 0.045f, 0.05f), 0.05f, 0.35f);
            _concrete = MakeMaterial(dir, "Concrete", new Color(0.17f, 0.17f, 0.16f), 0.0f, 0.92f);
            _paper = MakeMaterial(dir, "Paper", new Color(0.80f, 0.75f, 0.62f), 0.0f, 0.85f);
            _brassKnob = MakeMaterial(dir, "Brass", new Color(0.52f, 0.40f, 0.16f), 0.85f, 0.32f);
        }

        private static Material MakeMaterial(string dir, string name, Color albedo, float metallic, float smoothnessInverse)
        {
            var mat = new Material(Shader.Find("Standard"));
            mat.SetColor("_Color", albedo);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Glossiness", 1f - smoothnessInverse);
            AssetDatabase.CreateAsset(mat, $"{dir}/{name}.mat");
            return mat;
        }

        private static Material MakeEmissive(string name, Color color, float intensity)
        {
            const string dir = "Assets/Materials/Probe";
            var mat = new Material(Shader.Find("Standard"));
            mat.SetColor("_Color", color * 0.15f);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Glossiness", 0.6f);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.SetColor("_EmissionColor", color * intensity);
            AssetDatabase.CreateAsset(mat, $"{dir}/{name}.mat");
            return mat;
        }

        // ---------- 环境 ----------

        private static void BuildLightingEnvironment()
        {
            // 环境光压到接近零。目标参考图（IRON NEST）有近 40% 的像素低于亮度 0.06，
            // 那种"只有光源照到的地方才亮"的层次感来自极低的环境光，而不是后期调色。
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Rgb(_cfg.ambientSky, new Color(0.0016f, 0.0020f, 0.0028f));
            RenderSettings.ambientEquatorColor = Rgb(_cfg.ambientEquator, new Color(0.0010f, 0.0012f, 0.0016f));
            RenderSettings.ambientGroundColor = Rgb(_cfg.ambientGround, new Color(0.0006f, 0.0006f, 0.0008f));
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.0012f, 0.0016f, 0.0020f);
            RenderSettings.fogDensity = _cfg.fogDensity;
            RenderSettings.skybox = null;
        }

        // ---------- 房间 ----------

        private static void BuildRoomShell()
        {
            var root = new GameObject("Room").transform;

            // 内向盒子：把六个面各自摆成一块厚板，避免用反转法线的立方体。
            AddBox(root, "Floor", new Vector3(0, -0.05f, 0), new Vector3(4.6f, 0.1f, 3.6f), _concrete);
            AddBox(root, "Ceiling", new Vector3(0, 2.75f, 0), new Vector3(4.6f, 0.1f, 3.6f), _concrete);
            AddBox(root, "WallBack", new Vector3(0, 1.35f, -1.8f), new Vector3(4.6f, 2.8f, 0.1f), _concrete);
            AddBox(root, "WallLeft", new Vector3(-2.3f, 1.35f, 0), new Vector3(0.1f, 2.8f, 3.6f), _concrete);
            AddBox(root, "WallRight", new Vector3(2.3f, 1.35f, 0), new Vector3(0.1f, 2.8f, 3.6f), _concrete);
            AddBox(root, "WallFrontLeft", new Vector3(-1.55f, 1.35f, 1.8f), new Vector3(1.5f, 2.8f, 0.1f), _concrete);
            AddBox(root, "WallFrontRight", new Vector3(1.55f, 1.35f, 1.8f), new Vector3(1.5f, 2.8f, 0.1f), _concrete);

            // 暴露的线管，给混凝土墙面加工业细节。
            for (var i = 0; i < 4; i++)
            {
                AddCylinder(root, $"Conduit_{i}",
                    new Vector3(-2.18f, 2.30f - i * 0.11f, 0f),
                    new Vector3(0.022f, 1.75f, 0.022f),
                    Quaternion.Euler(90, 0, 0), _steelDark);
            }
        }

        // ---------- 仪表墙（正前方，玩家 70% 时间面对的画面）----------

        private static void BuildInstrumentWall()
        {
            var root = new GameObject("InstrumentWall").transform;
            root.position = new Vector3(0, 0, -1.7f);

            var crtMat = MakeEmissive("CrtScreen", CrtGreen, _cfg.emission.crtScreen);
            var meterMat = MakeEmissive("MeterFace", new Color(0.75f, 0.85f, 0.55f), _cfg.emission.meterFace);
            var neonMat = MakeEmissive("NeonLamp", NeonAmber, _cfg.emission.neonLamp);

            var labelMat = MakeEmissive("LabelPlate", new Color(0.80f, 0.78f, 0.66f), _cfg.emission.labelPlate);
            var lampRed = MakeEmissive("LampRed", new Color(1.0f, 0.20f, 0.12f), _cfg.emission.indicatorLamp * 1.1f);
            var lampAmber = MakeEmissive("LampAmberSmall", NeonAmber, _cfg.emission.indicatorLamp);
            var lampGreen = MakeEmissive("LampGreenSmall", new Color(0.35f, 1.0f, 0.40f), _cfg.emission.indicatorLamp * 0.9f);
            var indicatorMats = new[] { lampRed, lampAmber, lampGreen };

            // 主机架：三层堆叠的设备箱。每层都做面板缝、螺丝、标签牌和指示灯，
            // 因为自检显示细节密度是与目标差距最大的维度之一。
            for (var row = 0; row < 3; row++)
            {
                var y = 0.95f + row * 0.52f;
                AddBox(root, $"Rack_{row}", new Vector3(0, y, 0.12f),
                    new Vector3(2.60f, 0.48f, 0.34f), _steelOlive);

                // 面板上下缘的凹缝：制造硬边缘，是提升可读结构最便宜的手段
                AddBox(root, $"RackSeamTop_{row}", new Vector3(0, y + 0.235f, 0.292f),
                    new Vector3(2.58f, 0.012f, 0.010f), _bakelite);
                AddBox(root, $"RackSeamBottom_{row}", new Vector3(0, y - 0.235f, 0.292f),
                    new Vector3(2.58f, 0.012f, 0.010f), _bakelite);

                // 面板固定螺丝
                for (var sx = 0; sx < 6; sx++)
                {
                    for (var sy = 0; sy < 2; sy++)
                    {
                        AddCylinder(root, $"Screw_{row}_{sx}_{sy}",
                            new Vector3(-1.24f + sx * 0.496f, y - 0.205f + sy * 0.41f, 0.293f),
                            new Vector3(0.011f, 0.004f, 0.011f),
                            Quaternion.Euler(90, 0, 0), _brassKnob);
                    }
                }

                // 每层面板上的旋钮阵列
                var knobCount = row == 1 ? 7 : 5;
                for (var i = 0; i < knobCount; i++)
                {
                    var x = Mathf.Lerp(-1.12f, 1.12f, knobCount == 1 ? 0.5f : i / (float)(knobCount - 1));
                    // 旋钮底座刻度环
                    AddCylinder(root, $"KnobRing_{row}_{i}",
                        new Vector3(x, y - 0.13f, 0.291f),
                        new Vector3(0.072f, 0.004f, 0.072f),
                        Quaternion.Euler(90, 0, 0), _steelDark);
                    AddCylinder(root, $"Knob_{row}_{i}",
                        new Vector3(x, y - 0.13f, 0.297f),
                        new Vector3(0.052f, 0.022f, 0.052f),
                        Quaternion.Euler(90, 0, 0), _bakelite);
                    AddBox(root, $"KnobMark_{row}_{i}",
                        new Vector3(x, y - 0.09f, 0.320f),
                        new Vector3(0.006f, 0.028f, 0.004f), _brassKnob);
                    // 旋钮下方的标签牌
                    AddBox(root, $"KnobLabel_{row}_{i}",
                        new Vector3(x, y - 0.196f, 0.293f),
                        new Vector3(0.088f, 0.020f, 0.003f), labelMat);
                }

                // 拨杆开关排 + 指示灯：小面积高对比，同时补暖色
                for (var i = 0; i < 8; i++)
                {
                    var x = -1.18f + i * 0.338f;
                    AddBox(root, $"ToggleBase_{row}_{i}", new Vector3(x, y + 0.115f, 0.293f),
                        new Vector3(0.030f, 0.030f, 0.006f), _steelDark);
                    AddCylinder(root, $"ToggleStick_{row}_{i}",
                        new Vector3(x, y + 0.132f, 0.305f),
                        new Vector3(0.006f, 0.020f, 0.006f),
                        Quaternion.Euler(i % 3 == 0 ? -28f : 22f, 0, 0), _brassKnob);
                    AddSphere(root, $"Indicator_{row}_{i}",
                        new Vector3(x + 0.052f, y + 0.115f, 0.300f),
                        Vector3.one * 0.017f, indicatorMats[(row * 3 + i) % indicatorMats.Length]);
                }

                // 通风格栅：密集平行线，边缘密度贡献大
                for (var g = 0; g < 9; g++)
                {
                    AddBox(root, $"Vent_{row}_{g}",
                        new Vector3(1.14f, y - 0.09f + g * 0.019f, 0.292f),
                        new Vector3(0.30f, 0.008f, 0.008f), _bakelite);
                }
            }

            // 机架之间的线缆：从设备墙垂下来，打断大块平面
            for (var c = 0; c < 5; c++)
            {
                var x = -0.95f + c * 0.48f;
                AddCylinder(root, $"Cable_{c}",
                    new Vector3(x, 0.55f + (c % 2) * 0.12f, 0.30f),
                    new Vector3(0.010f, 0.22f + (c % 3) * 0.05f, 0.010f),
                    Quaternion.Euler(0, 0, (c - 2) * 5f), _bakelite);
            }

            // 中央 CRT 示波器：画面的绿色光源本体
            AddBox(root, "CrtBezel", new Vector3(0, 1.98f, 0.14f), new Vector3(0.74f, 0.60f, 0.30f), _steelDark);
            AddBox(root, "CrtScreen", new Vector3(0, 1.98f, 0.295f), new Vector3(0.58f, 0.44f, 0.012f), crtMat);

            // 两侧模拟表盘
            for (var i = 0; i < 4; i++)
            {
                var x = i < 2 ? -1.02f + i * 0.42f : 0.60f + (i - 2) * 0.42f;
                AddCylinder(root, $"Gauge_{i}", new Vector3(x, 1.98f, 0.295f),
                    new Vector3(0.15f, 0.012f, 0.15f), Quaternion.Euler(90, 0, 0), _steelDark);
                AddCylinder(root, $"GaugeFace_{i}", new Vector3(x, 1.98f, 0.310f),
                    new Vector3(0.125f, 0.008f, 0.125f), Quaternion.Euler(90, 0, 0), meterMat);
                AddBox(root, $"GaugeNeedle_{i}", new Vector3(x, 2.02f, 0.320f),
                    new Vector3(0.006f, 0.075f, 0.003f), _brassKnob);
            }

            // 氖灯指示灯排：小面积高亮度，负责画面的暖色高光点
            for (var i = 0; i < 6; i++)
            {
                AddSphere(root, $"NeonLamp_{i}",
                    new Vector3(-0.62f + i * 0.25f, 2.42f, 0.30f),
                    Vector3.one * 0.030f, neonMat);
            }

            // 频率刻度盘：横贯机架的长条
            AddBox(root, "DialStrip", new Vector3(0, 1.62f, 0.298f), new Vector3(1.90f, 0.10f, 0.010f),
                MakeEmissive("DialStripFace", new Color(0.85f, 0.78f, 0.45f), _cfg.emission.dialStrip));
            AddBox(root, "DialCursor", new Vector3(0.24f, 1.62f, 0.312f), new Vector3(0.008f, 0.13f, 0.004f), _brassKnob);
        }

        // ---------- 桌面 ----------

        private static void BuildDesk()
        {
            var root = new GameObject("Desk").transform;

            AddBox(root, "DeskTop", new Vector3(0, 0.74f, -0.95f), new Vector3(2.30f, 0.05f, 0.80f), _steelOlive);
            AddBox(root, "DeskLegL", new Vector3(-1.05f, 0.37f, -0.95f), new Vector3(0.06f, 0.74f, 0.70f), _steelDark);
            AddBox(root, "DeskLegR", new Vector3(1.05f, 0.37f, -0.95f), new Vector3(0.06f, 0.74f, 0.70f), _steelDark);

            // 摊开的电报纸：桌面视觉中心，接受台灯的暖光
            for (var i = 0; i < 5; i++)
            {
                var angle = Random.Range(-14f, 14f);
                AddBox(root, $"Paper_{i}",
                    new Vector3(Random.Range(-0.34f, 0.30f), 0.767f + i * 0.0016f, Random.Range(-1.14f, -0.86f)),
                    new Vector3(0.21f, 0.001f, 0.29f), _paper, Quaternion.Euler(0, angle, 0));
            }

            // 一次性密码本：合起来的小册子
            AddBox(root, "CodeBook", new Vector3(0.62f, 0.775f, -1.02f), new Vector3(0.15f, 0.026f, 0.21f), _paper,
                Quaternion.Euler(0, -8f, 0));
            AddBox(root, "CodeBookCover", new Vector3(0.62f, 0.789f, -1.02f), new Vector3(0.155f, 0.003f, 0.215f),
                _bakelite, Quaternion.Euler(0, -8f, 0));

            // 打字机：一块斜面加一排键
            AddBox(root, "TypewriterBody", new Vector3(-0.72f, 0.815f, -1.05f), new Vector3(0.40f, 0.10f, 0.30f), _steelDark);
            for (var r = 0; r < 3; r++)
            {
                for (var c = 0; c < 9; c++)
                {
                    AddCylinder(root, $"Key_{r}_{c}",
                        new Vector3(-0.88f + c * 0.040f, 0.872f + r * 0.012f, -1.14f + r * 0.045f),
                        new Vector3(0.014f, 0.006f, 0.014f), Quaternion.identity, _bakelite);
                }
            }

            // 台灯：暖黄光源的物理载体
            AddCylinder(root, "LampBase", new Vector3(-1.00f, 0.785f, -0.72f), new Vector3(0.09f, 0.012f, 0.09f),
                Quaternion.identity, _steelDark);
            AddCylinder(root, "LampArm", new Vector3(-0.96f, 0.98f, -0.75f), new Vector3(0.012f, 0.20f, 0.012f),
                Quaternion.Euler(0, 0, 12f), _steelDark);
            AddCylinder(root, "LampShade", new Vector3(-0.86f, 1.18f, -0.78f), new Vector3(0.11f, 0.09f, 0.11f),
                Quaternion.Euler(28f, 0, 22f), _steelOlive);
        }

        // ---------- 右侧窗墙 ----------

        private static void BuildWindowWall()
        {
            var root = new GameObject("WindowWall").transform;

            var frostMat = MakeEmissive("FrostedGlass", ColdWindow, _cfg.emission.frostedGlass);
            AddBox(root, "WindowGlass", new Vector3(2.24f, 1.62f, 0.35f), new Vector3(0.02f, 0.85f, 1.15f), frostMat);
            AddBox(root, "WindowFrameT", new Vector3(2.22f, 2.08f, 0.35f), new Vector3(0.05f, 0.07f, 1.25f), _steelDark);
            AddBox(root, "WindowFrameB", new Vector3(2.22f, 1.16f, 0.35f), new Vector3(0.05f, 0.07f, 1.25f), _steelDark);
            AddBox(root, "WindowMullion", new Vector3(2.21f, 1.62f, 0.35f), new Vector3(0.05f, 0.85f, 0.05f), _steelDark);

            // 传真机
            AddBox(root, "FaxBody", new Vector3(1.92f, 0.86f, -0.30f), new Vector3(0.36f, 0.22f, 0.44f), _steelOlive);
            AddBox(root, "FaxPaper", new Vector3(1.92f, 0.98f, -0.10f), new Vector3(0.26f, 0.002f, 0.34f), _paper,
                Quaternion.Euler(-28f, 0, 0));
        }

        // ---------- 左侧档案墙 ----------

        private static void BuildArchiveWall()
        {
            var root = new GameObject("ArchiveWall").transform;

            AddBox(root, "CabinetBody", new Vector3(-1.94f, 0.62f, -0.20f), new Vector3(0.55f, 1.24f, 0.72f), _steelOlive);
            for (var i = 0; i < 4; i++)
            {
                AddBox(root, $"DrawerFace_{i}", new Vector3(-1.66f, 0.20f + i * 0.29f, -0.20f),
                    new Vector3(0.02f, 0.26f, 0.68f), _steelDark);
                AddBox(root, $"DrawerHandle_{i}", new Vector3(-1.645f, 0.20f + i * 0.29f, -0.20f),
                    new Vector3(0.015f, 0.03f, 0.18f), _brassKnob);
            }

            // 墙上的边境地图
            AddBox(root, "WallMap", new Vector3(-2.23f, 1.75f, -0.30f), new Vector3(0.01f, 0.72f, 1.05f), _paper);
            for (var i = 0; i < 5; i++)
            {
                AddSphere(root, $"MapPin_{i}",
                    new Vector3(-2.21f, 1.52f + Random.Range(0f, 0.44f), -0.66f + Random.Range(0f, 0.72f)),
                    Vector3.one * 0.016f, _brassKnob);
            }
        }

        // ---------- 三光源 ----------

        private static void BuildLights()
        {
            var root = new GameObject("Lighting").transform;

            // 1. 台灯：暖黄，锥形，画面的视觉中心。锥角收紧，让光斑只落在桌面纸张上。
            var lampCfg = _cfg.lights.keyLampWarm;
            var lamp = NewLight(root, "KeyLamp_Warm", LightType.Spot, WarmLamp, lampCfg.intensity, lampCfg.range);
            lamp.transform.localPosition = new Vector3(-0.84f, 1.16f, -0.78f);
            lamp.transform.localRotation = Quaternion.Euler(58f, 202f, 0f);
            lamp.spotAngle = lampCfg.spotAngle;
            lamp.innerSpotAngle = 26f;
            lamp.shadows = LightShadows.Soft;

            // 2. CRT：绿色，只照亮设备墙前一小段距离。范围压到 1.5m 以内，
            //    否则整间混凝土房都会被染绿，失去三色分区。
            var crtCfg = _cfg.lights.fillCrtGreen;
            var crt = NewLight(root, "FillLight_CrtGreen", LightType.Point, CrtGreen, crtCfg.intensity, crtCfg.range);
            crt.transform.localPosition = new Vector3(0f, 1.94f, -1.30f);
            crt.shadows = LightShadows.None;

            // 3. 窗光：冷蓝，从右侧斜入，负责把右半边从死黑里拉出来
            var winCfg = _cfg.lights.rimColdWindow;
            var window = NewLight(root, "RimLight_ColdWindow", LightType.Spot, ColdWindow, winCfg.intensity, winCfg.range);
            window.transform.localPosition = new Vector3(2.10f, 1.74f, 0.30f);
            window.transform.localRotation = Quaternion.Euler(12f, -112f, 0f);
            window.spotAngle = winCfg.spotAngle;
            window.innerSpotAngle = 12f;
            window.shadows = LightShadows.Soft;

            // 桌面反弹光：真实房间里台灯照亮桌面后会把暖色反射到面前的设备上。
            // 没有这一盏，机架下半部分会完全被 CRT 的绿色吃掉，画面失去冷暖对比。
            var bounceCfg = _cfg.lights.bounceDeskWarm;
            var bounce = NewLight(root, "Bounce_DeskWarm", LightType.Spot, WarmLamp, bounceCfg.intensity, bounceCfg.range);
            bounce.transform.localPosition = new Vector3(-0.30f, 0.86f, -1.05f);
            bounce.transform.localRotation = Quaternion.Euler(-32f, 186f, 0f);
            bounce.spotAngle = bounceCfg.spotAngle;
            bounce.innerSpotAngle = 40f;
            bounce.shadows = LightShadows.None;

            // 补：氖灯排的余光，避免机架上沿死黑
            var neonCfg = _cfg.lights.practicalNeon;
            var neon = NewLight(root, "Practical_NeonSpill", LightType.Point, NeonAmber, neonCfg.intensity, neonCfg.range);
            neon.transform.localPosition = new Vector3(0f, 2.40f, -1.32f);
            neon.shadows = LightShadows.None;
        }

        private static Light NewLight(Transform parent, string name, LightType type, Color color,
            float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var light = go.AddComponent<Light>();
            light.type = type;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.renderMode = LightRenderMode.ForcePixel;
            return light;
        }

        // ---------- 机位 ----------

        private static void BuildCameras()
        {
            var root = new GameObject("Cameras").transform;

            // 工位坐姿眼高约 1.24m，坐在 z = 0.35 处面朝 -Z 的仪表墙。
            // Unity 相机默认朝 +Z，所以主视角的 yaw 是 180。
            var seat = new Vector3(0f, 1.24f, -0.16f);

            AddShot(root, "probe_front", seat, new Vector3(2f, 180f, 0f), 68f,
                "docs/research/refshots/iron_nest_heavy_turret_simulator_0.jpg", isMain: true);
            AddShot(root, "probe_desk", seat, new Vector3(42f, 180f, 0f), 62f,
                "docs/research/refshots/papers_please_0.jpg");
            AddShot(root, "probe_left", seat, new Vector3(4f, 250f, 0f), 58f, "");
            AddShot(root, "probe_right", seat, new Vector3(4f, 105f, 0f), 58f, "");

            // 诊断机位：俯视全景，用来确认场景布局本身是否正确，不参与画面对标。
            AddShot(root, "probe_overview", new Vector3(0f, 2.40f, 1.55f), new Vector3(32f, 180f, 0f), 72f, "");
        }

        private static void AddShot(Transform parent, string name, Vector3 pos, Vector3 euler, float fov,
            string reference, bool isMain = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(euler));

            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 40f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.0f, 0.0f, 0.0f);
            cam.allowHDR = true;
            cam.enabled = isMain;
            if (isMain)
            {
                go.tag = "MainCamera";
            }

            var shot = go.AddComponent<CaptureShot>();
            shot.shotName = name;
            shot.referenceImagePath = reference;
        }

        // ---------- 图元工具 ----------

        private static void AddBox(Transform parent, string name, Vector3 pos, Vector3 size, Material mat,
            Quaternion? rot = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Configure(go, parent, name, pos, size, mat, rot);
        }

        private static void AddCylinder(Transform parent, string name, Vector3 pos, Vector3 size,
            Quaternion rot, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Configure(go, parent, name, pos, size, mat, rot);
        }

        private static void AddSphere(Transform parent, string name, Vector3 pos, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Configure(go, parent, name, pos, size, mat, null);
        }

        private static void Configure(GameObject go, Transform parent, string name, Vector3 pos,
            Vector3 size, Material mat, Quaternion? rot)
        {
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        }

        private static void Log(string message)
        {
            Debug.Log($"[ProbeSceneBuilder] {message}");
            Console.WriteLine($"[ProbeSceneBuilder] {message}");
        }
    }
}
