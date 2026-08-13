using System;
using System.Collections.Generic;
using System.IO;
using Decoder.Capture;
using Decoder.Gameplay;
using Decoder.Interaction;
using Decoder.Rendering;
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
            public LightCfg panelWorkLight = new() { intensity = 5.5f, range = 3.4f, spotAngle = 104f };
            public LightCfg grazingSide = new() { intensity = 3.0f, range = 3.2f, spotAngle = 62f };
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
        private static bool _playableRig;

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
        private static Material _deskSurface;

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
            UvMeshCache.Clear();
            _playableRig = playable;
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

            // CRT 示波器。它是房间里最亮的东西，也是听障玩家读电码的唯一途径。
            var crtScreen = GameObject.Find("CrtScreen");
            if (crtScreen == null)
            {
                throw new InvalidOperationException("未找到 CRT 屏幕物件 CrtScreen");
            }

            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-noScope") < 0)
            {
                var scope = crtScreen.AddComponent<OscilloscopeDisplay>();
                scope.receiver = receiver;
                scope.targetRenderer = crtScreen.GetComponent<Renderer>();
            }

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

        private const string BakedMaterialDir = "Assets/Materials/Procedural";

        private static void CreateMaterials()
        {
            const string dir = "Assets/Materials/Probe";
            Directory.CreateDirectory(dir);

            // 优先用烘焙好的程序化 PBR 材质。它们带法线、粗糙度变化、磨损和污渍，
            // 是画面细节密度的主要来源。找不到时回落到纯色材质，
            // 这样在没跑过烘焙的干净检出上场景生成仍然能跑通，只是画面朴素一些。
            _steelDark = LoadBaked("SteelPanel")
                         ?? MakeMaterial(dir, "SteelDark", new Color(0.10f, 0.11f, 0.11f), 0.55f, 0.45f);
            _steelOlive = LoadBaked("OlivePaint")
                          ?? MakeMaterial(dir, "SteelOlive", new Color(0.19f, 0.21f, 0.16f), 0.35f, 0.60f);
            _bakelite = LoadBaked("Bakelite")
                        ?? MakeMaterial(dir, "Bakelite", new Color(0.045f, 0.045f, 0.05f), 0.05f, 0.35f);
            _concrete = LoadBaked("Concrete")
                        ?? MakeMaterial(dir, "Concrete", new Color(0.17f, 0.17f, 0.16f), 0.0f, 0.92f);
            _paper = LoadBaked("Paper")
                     ?? MakeMaterial(dir, "Paper", new Color(0.80f, 0.75f, 0.62f), 0.0f, 0.85f);
            _brassKnob = LoadBaked("Brass")
                         ?? MakeMaterial(dir, "Brass", new Color(0.52f, 0.40f, 0.16f), 0.85f, 0.32f);
            _deskSurface = LoadBaked("DeskSurface")
                           ?? MakeMaterial(dir, "DeskSurface", new Color(0.09f, 0.10f, 0.09f), 0.02f, 0.70f);
        }

        private static Material LoadBaked(string name)
        {
            return AssetDatabase.LoadAssetAtPath<Material>($"{BakedMaterialDir}/{name}.mat");
        }

        /// <summary>不同材质的贴图密度不一样：墙面要疏，旋钮的滚花要密。</summary>
        private static float TilesPerMeterFor(Material material)
        {
            if (material == _concrete) return 0.55f;
            if (material == _steelOlive) return 1.0f;
            if (material == _steelDark) return 1.6f;
            if (material == _bakelite) return 7.0f;
            if (material == _brassKnob) return 4.0f;
            if (material == _paper) return 1.8f;
            if (material == _deskSurface) return 1.1f;
            return 1.2f;
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

            // 主机架：三层堆叠的设备箱。
            //
            // 每一层都按真实机柜的做法分成三个深度层：机箱本体最深，
            // 前面板凹进去，四周边框再凸出来。这个高低差是画面里绝大部分
            // 硬边缘和自阴影的来源——把它做成一块平板，无论贴图多细
            // 画面都还是平的。
            // 三个深度层的 Z 必须自前向后排开，否则后面的层会把前面的埋掉。
            // 之前机箱是一整块实心方块，前表面比面板还靠前，
            // 于是玩家看到的一直是机箱侧壁的材质，军绿面板根本没露出来过。
            const float boxDepth = 0.30f;
            const float boxCenterZ = 0.08f;      // 机箱前表面 0.23
            const float panelThickness = 0.030f;
            const float panelCenterZ = 0.245f;   // 面板前表面 0.26
            const float frameProud = 0.016f;
            const float frameCenterZ = 0.268f;   // 边框前表面 0.276，比面板高出一截
            const float frameWidth = 0.032f;

            for (var row = 0; row < 3; row++)
            {
                var y = 0.95f + row * 0.52f;
                var frameZ = frameCenterZ;
                var panelZ = panelCenterZ + panelThickness * 0.5f;

                // 机箱本体，退到最后面
                AddBox(root, $"Rack_{row}", new Vector3(0, y, boxCenterZ),
                    new Vector3(2.60f, 0.48f, boxDepth), _steelDark);

                // 前面板。它比四周的边框矮一截，视觉上就成了凹进去的。
                AddBox(root, $"Panel_{row}", new Vector3(0, y, panelCenterZ),
                    new Vector3(2.52f, 0.42f, panelThickness), _steelOlive);

                // 四周凸出的边框。上下两条更宽，模仿机柜的安装耳。
                AddBox(root, $"FrameTop_{row}", new Vector3(0, y + 0.225f, frameZ),
                    new Vector3(2.60f, frameWidth, frameProud), _steelOlive);
                AddBox(root, $"FrameBottom_{row}", new Vector3(0, y - 0.225f, frameZ),
                    new Vector3(2.60f, frameWidth, frameProud), _steelOlive);
                AddBox(root, $"FrameLeft_{row}", new Vector3(-1.285f, y, frameZ),
                    new Vector3(frameWidth, 0.48f, frameProud), _steelOlive);
                AddBox(root, $"FrameRight_{row}", new Vector3(1.285f, y, frameZ),
                    new Vector3(frameWidth, 0.48f, frameProud), _steelOlive);

                // 层与层之间的缝。留出黑缝比画一条黑线有效得多，
                // 因为它是真的没有光进得去。
                if (row < 2)
                {
                    AddBox(root, $"RackGap_{row}", new Vector3(0, y + 0.26f, boxCenterZ),
                        new Vector3(2.56f, 0.035f, boxDepth * 0.9f), _bakelite);
                }

                // 面板固定螺丝：沉头螺丝有一圈凹陷的座
                for (var sx = 0; sx < 6; sx++)
                {
                    for (var sy = 0; sy < 2; sy++)
                    {
                        var sxPos = -1.24f + sx * 0.496f;
                        var syPos = y - 0.185f + sy * 0.37f;
                        AddCylinder(root, $"ScrewSeat_{row}_{sx}_{sy}",
                            new Vector3(sxPos, syPos, panelZ - 0.002f),
                            new Vector3(0.020f, 0.003f, 0.020f),
                            Quaternion.Euler(90, 0, 0), _bakelite);
                        AddCylinder(root, $"Screw_{row}_{sx}_{sy}",
                            new Vector3(sxPos, syPos, panelZ + 0.002f),
                            new Vector3(0.012f, 0.003f, 0.012f),
                            Quaternion.Euler(90, 0, 0), _brassKnob);
                    }
                }

                // 两侧提手：真实机架设备都有，凸出面板一截，能投下明确的阴影
                foreach (var side in new[] { -1f, 1f })
                {
                    var hx = side * 1.16f;
                    AddBox(root, $"HandleBracket_{row}_{(side < 0 ? "L" : "R")}_a",
                        new Vector3(hx, y + 0.10f, panelZ + 0.018f),
                        new Vector3(0.026f, 0.030f, 0.045f), _steelDark);
                    AddBox(root, $"HandleBracket_{row}_{(side < 0 ? "L" : "R")}_b",
                        new Vector3(hx, y - 0.10f, panelZ + 0.018f),
                        new Vector3(0.026f, 0.030f, 0.045f), _steelDark);
                    AddBox(root, $"HandleBar_{row}_{(side < 0 ? "L" : "R")}",
                        new Vector3(hx, y, panelZ + 0.038f),
                        new Vector3(0.022f, 0.22f, 0.018f), _brassKnob);
                }

                // 铭牌：每台设备的型号牌
                AddBox(root, $"NamePlate_{row}", new Vector3(-0.90f, y + 0.168f, panelZ + 0.003f),
                    new Vector3(0.30f, 0.042f, 0.003f), _brassKnob);

                // 每层面板上的旋钮阵列
                var knobCount = row == 1 ? 7 : 5;
                for (var i = 0; i < knobCount; i++)
                {
                    var x = Mathf.Lerp(-1.12f, 1.12f, knobCount == 1 ? 0.5f : i / (float)(knobCount - 1));
                    // 旋钮底座刻度环
                    AddCylinder(root, $"KnobRing_{row}_{i}",
                        new Vector3(x, y - 0.13f, 0.269f),
                        new Vector3(0.072f, 0.004f, 0.072f),
                        Quaternion.Euler(90, 0, 0), _steelDark);
                    AddCylinder(root, $"Knob_{row}_{i}",
                        new Vector3(x, y - 0.13f, 0.275f),
                        new Vector3(0.052f, 0.022f, 0.052f),
                        Quaternion.Euler(90, 0, 0), _bakelite);
                    AddBox(root, $"KnobMark_{row}_{i}",
                        new Vector3(x, y - 0.09f, 0.298f),
                        new Vector3(0.006f, 0.028f, 0.004f), _brassKnob);
                    // 旋钮下方的标签牌
                    AddBox(root, $"KnobLabel_{row}_{i}",
                        new Vector3(x, y - 0.196f, 0.271f),
                        new Vector3(0.088f, 0.020f, 0.003f), labelMat);
                }

                // 拨杆开关排 + 指示灯：小面积高对比，同时补暖色
                for (var i = 0; i < 8; i++)
                {
                    var x = -1.18f + i * 0.338f;
                    AddBox(root, $"ToggleBase_{row}_{i}", new Vector3(x, y + 0.115f, 0.271f),
                        new Vector3(0.030f, 0.030f, 0.006f), _steelDark);
                    AddCylinder(root, $"ToggleStick_{row}_{i}",
                        new Vector3(x, y + 0.132f, 0.283f),
                        new Vector3(0.006f, 0.020f, 0.006f),
                        Quaternion.Euler(i % 3 == 0 ? -28f : 22f, 0, 0), _brassKnob);
                    AddSphere(root, $"Indicator_{row}_{i}",
                        new Vector3(x + 0.052f, y + 0.115f, 0.278f),
                        Vector3.one * 0.017f, indicatorMats[(row * 3 + i) % indicatorMats.Length]);
                }

                // 通风格栅：密集平行线，边缘密度贡献大
                for (var g = 0; g < 9; g++)
                {
                    AddBox(root, $"Vent_{row}_{g}",
                        new Vector3(1.14f, y - 0.09f + g * 0.019f, 0.270f),
                        new Vector3(0.30f, 0.008f, 0.008f), _bakelite);
                }
            }

            // 机架之间的线缆：从设备墙垂下来，打断大块平面
            for (var c = 0; c < 5; c++)
            {
                var x = -0.95f + c * 0.48f;
                AddCylinder(root, $"Cable_{c}",
                    new Vector3(x, 0.55f + (c % 2) * 0.12f, 0.28f),
                    new Vector3(0.010f, 0.22f + (c % 3) * 0.05f, 0.010f),
                    Quaternion.Euler(0, 0, (c - 2) * 5f), _bakelite);
            }

            // 顶部横贯管道。它悬在仪表墙前方，是画面里最有效的一个投影体：
            // 一根管子能在整面墙上拉出一条贯穿的暗带，把大片均匀的亮面切开。
            AddCylinder(root, "OverheadConduit",
                new Vector3(0f, 2.56f, 0.40f), new Vector3(0.042f, 1.34f, 0.042f),
                Quaternion.Euler(0, 0, 90f), _steelDark);
            // 两端各加一个法兰和一段向后拐的弯头。没有它们，管子的端面会在
            // 视野边缘变成一个孤零零浮在黑暗里的椭圆，看着像穿帮。
            foreach (var side in new[] { -1f, 1f })
            {
                var ex = side * 1.34f;
                AddCylinder(root, $"ConduitFlange_{(side < 0 ? "L" : "R")}",
                    new Vector3(ex, 2.56f, 0.40f), new Vector3(0.062f, 0.020f, 0.062f),
                    Quaternion.Euler(0, 0, 90f), _brassKnob);
                AddCylinder(root, $"ConduitElbow_{(side < 0 ? "L" : "R")}",
                    new Vector3(ex + side * 0.02f, 2.56f, 0.30f),
                    new Vector3(0.040f, 0.11f, 0.040f),
                    Quaternion.Euler(90f, 0, 0), _steelDark);
            }
            for (var b = 0; b < 5; b++)
            {
                AddBox(root, $"ConduitClamp_{b}",
                    new Vector3(-1.05f + b * 0.525f, 2.56f, 0.40f),
                    new Vector3(0.052f, 0.052f, 0.020f), _brassKnob);
                AddCylinder(root, $"ConduitDrop_{b}",
                    new Vector3(-1.05f + b * 0.525f, 2.66f, 0.40f),
                    new Vector3(0.012f, 0.10f, 0.012f), Quaternion.identity, _steelDark);
            }

            // 同轴接头排。小而密的金属件，凑近看每个都有独立的轮廓。
            for (var j = 0; j < 6; j++)
            {
                var jx = -1.16f + j * 0.088f;
                AddCylinder(root, $"CoaxBody_{j}", new Vector3(jx, 0.80f, 0.276f),
                    new Vector3(0.026f, 0.014f, 0.026f), Quaternion.Euler(90, 0, 0), _brassKnob);
                AddCylinder(root, $"CoaxPin_{j}", new Vector3(jx, 0.80f, 0.292f),
                    new Vector3(0.010f, 0.008f, 0.010f), Quaternion.Euler(90, 0, 0), _steelDark);
            }

            // 保险丝座与总电源开关
            for (var f = 0; f < 3; f++)
            {
                AddCylinder(root, $"FuseHolder_{f}", new Vector3(0.86f + f * 0.075f, 0.80f, 0.278f),
                    new Vector3(0.030f, 0.018f, 0.030f), Quaternion.Euler(90, 0, 0), _bakelite);
                AddCylinder(root, $"FuseCap_{f}", new Vector3(0.86f + f * 0.075f, 0.80f, 0.298f),
                    new Vector3(0.022f, 0.006f, 0.022f), Quaternion.Euler(90, 0, 0), _brassKnob);
            }

            // 中央 CRT 示波器：画面的绿色光源本体
            AddBox(root, "CrtBezel", new Vector3(0, 1.98f, 0.14f), new Vector3(0.74f, 0.60f, 0.30f), _steelDark);

            // 遮光罩：真实示波器都带一个，挡住环境光让屏幕可读。
            // 对画面而言它更重要的作用是在面板上投下一圈硬阴影。
            AddBox(root, "CrtHoodTop", new Vector3(0, 2.262f, 0.312f),
                new Vector3(0.80f, 0.014f, 0.072f), _steelDark, Quaternion.Euler(-26f, 0, 0));
            AddBox(root, "CrtHoodBottom", new Vector3(0, 1.698f, 0.312f),
                new Vector3(0.80f, 0.014f, 0.072f), _steelDark, Quaternion.Euler(26f, 0, 0));
            AddBox(root, "CrtHoodLeft", new Vector3(-0.422f, 1.98f, 0.312f),
                new Vector3(0.014f, 0.58f, 0.072f), _steelDark, Quaternion.Euler(0, 26f, 0));
            AddBox(root, "CrtHoodRight", new Vector3(0.422f, 1.98f, 0.312f),
                new Vector3(0.014f, 0.58f, 0.072f), _steelDark, Quaternion.Euler(0, -26f, 0));
            AddBox(root, "CrtScreen", new Vector3(0, 1.98f, 0.273f), new Vector3(0.58f, 0.44f, 0.012f), crtMat);

            // 两侧模拟表盘
            for (var i = 0; i < 4; i++)
            {
                var x = i < 2 ? -1.02f + i * 0.42f : 0.60f + (i - 2) * 0.42f;
                AddCylinder(root, $"Gauge_{i}", new Vector3(x, 1.98f, 0.273f),
                    new Vector3(0.15f, 0.012f, 0.15f), Quaternion.Euler(90, 0, 0), _steelDark);
                AddCylinder(root, $"GaugeFace_{i}", new Vector3(x, 1.98f, 0.288f),
                    new Vector3(0.125f, 0.008f, 0.125f), Quaternion.Euler(90, 0, 0), meterMat);
                AddBox(root, $"GaugeNeedle_{i}", new Vector3(x, 2.02f, 0.298f),
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
            AddBox(root, "DialStrip", new Vector3(0, 1.62f, 0.276f), new Vector3(1.90f, 0.10f, 0.010f),
                MakeEmissive("DialStripFace", new Color(0.85f, 0.78f, 0.45f), _cfg.emission.dialStrip));
            AddBox(root, "DialCursor", new Vector3(0.24f, 1.62f, 0.290f), new Vector3(0.008f, 0.13f, 0.004f), _brassKnob);
        }

        // ---------- 桌面 ----------

        private static void BuildDesk()
        {
            var root = new GameObject("Desk").transform;

            AddBox(root, "DeskTop", new Vector3(0, 0.74f, -0.95f), new Vector3(2.30f, 0.05f, 0.80f), _deskSurface);
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
                Quaternion.Euler(64f, 104f, 0f), _steelOlive);

            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-noProps") < 0)
            {
                BuildDeskProps(root);
            }
        }

        /// <summary>
        /// 桌面上的零碎。
        ///
        /// 这些东西没有一件参与玩法，但桌面是玩家低头时的整个视野——
        /// 一张空桌子会让人立刻意识到自己在看一个搭出来的场景。
        /// 概念图里的密度才是目标：铅笔、烟灰缸、气动罐、码表册、搪瓷缸、镇纸，
        /// 每一件都得有自己的轮廓，不能是几个方块凑数。
        /// </summary>
        private static void BuildDeskProps(Transform root)
        {
            const float top = 0.767f;

            // 铅笔：六棱杆用细圆柱近似，笔尖用一小段更细的圆柱，末端一小截金属箍
            var pencil = new Vector3(0.16f, top + 0.005f, -0.78f);
            AddCylinder(root, "PencilBody", pencil, new Vector3(0.0070f, 0.098f, 0.0070f),
                Quaternion.Euler(0, 0, 90f), _bakelite);
            AddCylinder(root, "PencilTip", pencil + new Vector3(0.088f, 0f, 0f),
                new Vector3(0.0030f, 0.010f, 0.0030f), Quaternion.Euler(0, 0, 90f), _paper);
            AddCylinder(root, "PencilFerrule", pencil + new Vector3(-0.082f, 0f, 0f),
                new Vector3(0.0058f, 0.008f, 0.0058f), Quaternion.Euler(0, 0, 90f), _brassKnob);

            // 烟灰缸：一圈壁加一个底，里面几个烟头。夜班的工位上不可能没有这个。
            var ashtray = new Vector3(0.72f, top, -0.72f);
            AddCylinder(root, "AshtrayBowl", ashtray + new Vector3(0f, 0.014f, 0f),
                new Vector3(0.090f, 0.016f, 0.090f), Quaternion.identity, _steelDark);
            AddCylinder(root, "AshtrayWell", ashtray + new Vector3(0f, 0.026f, 0f),
                new Vector3(0.062f, 0.006f, 0.062f), Quaternion.identity, _bakelite);
            for (var b = 0; b < 4; b++)
            {
                var angle = b * 74f + 20f;
                var offset = Quaternion.Euler(0, angle, 0) * new Vector3(0.030f, 0f, 0f);
                AddCylinder(root, $"CigaretteButt_{b}",
                    ashtray + offset + new Vector3(0f, 0.031f, 0f),
                    new Vector3(0.0042f, 0.014f, 0.0042f),
                    Quaternion.Euler(78f, angle, 0f), _paper);
            }

            // 气动管道投递罐：黄铜圆筒加两道加强箍。上报单就是塞进它送走的。
            var canister = new Vector3(0.98f, top + 0.030f, -1.02f);
            AddCylinder(root, "TubeCanister", canister, new Vector3(0.038f, 0.085f, 0.038f),
                Quaternion.Euler(0, 12f, 90f), _brassKnob);
            AddCylinder(root, "TubeCanisterBand1", canister + new Vector3(-0.048f, 0f, -0.010f),
                new Vector3(0.041f, 0.006f, 0.041f), Quaternion.Euler(0, 12f, 90f), _steelDark);
            AddCylinder(root, "TubeCanisterBand2", canister + new Vector3(0.048f, 0f, 0.010f),
                new Vector3(0.041f, 0.006f, 0.041f), Quaternion.Euler(0, 12f, 90f), _steelDark);

            // 中文电码表：一本立着摊开的册子，两片纸页加一条书脊
            var book = new Vector3(0.40f, top, -1.12f);
            AddBox(root, "CodeTableSpine", book + new Vector3(0f, 0.006f, 0f),
                new Vector3(0.014f, 0.012f, 0.170f), _bakelite, Quaternion.Euler(0, -6f, 0));
            AddBox(root, "CodeTablePageL", book + new Vector3(-0.062f, 0.004f, 0f),
                new Vector3(0.118f, 0.008f, 0.166f), _paper, Quaternion.Euler(0, -6f, -3f));
            AddBox(root, "CodeTablePageR", book + new Vector3(0.062f, 0.004f, 0f),
                new Vector3(0.118f, 0.008f, 0.166f), _paper, Quaternion.Euler(0, -6f, 3f));

            // 搪瓷缸：杯身、杯口的一圈厚边、一个把手
            var mug = new Vector3(-0.62f, top, -0.70f);
            AddCylinder(root, "MugBody", mug + new Vector3(0f, 0.048f, 0f),
                new Vector3(0.056f, 0.048f, 0.056f), Quaternion.identity, _paper);
            AddCylinder(root, "MugRim", mug + new Vector3(0f, 0.096f, 0f),
                new Vector3(0.059f, 0.005f, 0.059f), Quaternion.identity, _steelDark);
            AddBox(root, "MugHandle", mug + new Vector3(0.052f, 0.048f, 0f),
                new Vector3(0.010f, 0.036f, 0.008f), _paper);

            // 铁尺与印章：抄报台上的常备物
            AddBox(root, "SteelRuler", new Vector3(-0.34f, top + 0.002f, -0.70f),
                new Vector3(0.30f, 0.003f, 0.026f), _brassKnob, Quaternion.Euler(0, 8f, 0));
            AddCylinder(root, "StampHandle", new Vector3(0.58f, top + 0.034f, -1.14f),
                new Vector3(0.020f, 0.026f, 0.020f), Quaternion.identity, _bakelite);
            AddCylinder(root, "StampBase", new Vector3(0.58f, top + 0.008f, -1.14f),
                new Vector3(0.032f, 0.008f, 0.032f), Quaternion.identity, _steelDark);

            // 耳机：挂在桌沿的挂钩上，头梁与两个耳罩
            var phones = new Vector3(-0.98f, top + 0.052f, -1.10f);
            AddCylinder(root, "HeadphoneBand", phones, new Vector3(0.070f, 0.008f, 0.070f),
                Quaternion.Euler(90f, 0, 0), _bakelite);
            AddCylinder(root, "HeadphoneCupL", phones + new Vector3(-0.068f, -0.018f, 0f),
                new Vector3(0.032f, 0.014f, 0.032f), Quaternion.Euler(0, 0, 90f), _bakelite);
            AddCylinder(root, "HeadphoneCupR", phones + new Vector3(0.068f, -0.018f, 0f),
                new Vector3(0.032f, 0.014f, 0.032f), Quaternion.Euler(0, 0, 90f), _bakelite);
            for (var c = 0; c < 4; c++)
            {
                AddCylinder(root, $"HeadphoneCord_{c}",
                    phones + new Vector3(0.02f + c * 0.03f, -0.052f - c * 0.004f, 0.03f + c * 0.02f),
                    new Vector3(0.0045f, 0.026f, 0.0045f),
                    Quaternion.Euler(72f, 24f * c, 18f), _bakelite);
            }
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
            lamp.transform.localRotation = Quaternion.Euler(26f, 104f, 0f);
            lamp.spotAngle = lampCfg.spotAngle;
            lamp.innerSpotAngle = 26f;
            lamp.shadows = LightShadows.Soft;

            // 2. CRT：绿色，只照亮设备墙前一小段距离。范围压到 1.5m 以内，
            //    否则整间混凝土房都会被染绿，失去三色分区。
            var crtCfg = _cfg.lights.fillCrtGreen;
            var crt = NewLight(root, "FillLight_CrtGreen", LightType.Point, CrtGreen, crtCfg.intensity, crtCfg.range);
            crt.transform.localPosition = new Vector3(0f, 1.96f, -1.36f);
            crt.shadows = LightShadows.Soft;
            crt.shadowBias = 0.02f;
            crt.shadowNormalBias = 0.12f;

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

            // 顶部面板工作灯。真实机房的机柜上方都装检修灯，
            // 没有它整面仪表墙只剩 CRT 的余光，刻度和标签都读不出来。
            // 它还负责让顶部管道在墙上投出一条贯穿的暗带。
            var workCfg = _cfg.lights.panelWorkLight;
            var work = NewLight(root, "Practical_PanelWork", LightType.Spot,
                new Color(1f, 0.88f, 0.70f), workCfg.intensity, workCfg.range);
            work.transform.localPosition = new Vector3(0f, 2.60f, -1.02f);
            work.transform.localRotation = Quaternion.Euler(56f, 180f, 0f);
            work.spotAngle = workCfg.spotAngle;
            work.innerSpotAngle = workCfg.spotAngle * 0.45f;
            work.shadows = LightShadows.Soft;
            work.shadowBias = 0.015f;
            work.shadowNormalBias = 0.10f;

            // 掠射侧光。这是让面板上的凸起物显形最有效的一招：
            // 光几乎贴着面板扫过去，每个旋钮、螺丝、提手、接头都会拖出一道长影，
            // 参考作品里那种"一屏全是可读机械结构"的观感主要就来自这个。
            // 正面来光反而会把所有起伏抹平。
            var grazeCfg = _cfg.lights.grazingSide;
            var graze = NewLight(root, "Practical_GrazingSide", LightType.Spot,
                new Color(0.98f, 0.86f, 0.62f), grazeCfg.intensity, grazeCfg.range);
            graze.transform.localPosition = new Vector3(1.42f, 1.72f, -1.42f);
            graze.transform.localRotation = Quaternion.Euler(4f, -104f, 0f);
            graze.spotAngle = grazeCfg.spotAngle;
            graze.innerSpotAngle = grazeCfg.spotAngle * 0.3f;
            graze.shadows = LightShadows.Soft;
            graze.shadowBias = 0.012f;
            graze.shadowNormalBias = 0.06f;

            // 补：氖灯排的余光，避免机架上沿死黑
            var neonCfg = _cfg.lights.practicalNeon;
            var neon = NewLight(root, "Practical_NeonSpill", LightType.Point, NeonAmber, neonCfg.intensity, neonCfg.range);
            neon.transform.localPosition = new Vector3(0f, 2.40f, -1.32f);
            neon.shadows = LightShadows.Soft;
            neon.shadowBias = 0.02f;
            neon.shadowNormalBias = 0.12f;
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

            AddShot(root, "probe_front", seat, new Vector3(-4f, 180f, 0f), 82f,
                "docs/research/refshots/iron_nest_heavy_turret_simulator_0.jpg", isMain: true);
            AddShot(root, "probe_desk", seat, new Vector3(50f, 180f, 0f), 82f,
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

            // 后处理挂在每个机位上，这样编辑器截图和实际运行看到的是同一套成像。
            // 着色器用序列化引用而不是运行时查找，否则构建包里不会带上它。
            // 后处理目前只挂在美术探针场景上。
            //
            // 实测：只要这个后处理着色器进入构建包，产出的 level0 在运行时就会报
            // corrupted 并崩溃，而 shader 编译本身没有任何报错。对照实验很明确——
            // 同一个场景挂组件但不让着色器进包则完全正常。根因未定位，
            // 怀疑与无 GPU 的 batchmode 下着色器变体序列化有关，需要在有显卡的机器上复核。
            //
            // 在查清之前，编辑器截图这条路径仍然走完整后处理（CaptureHarness 会显式调用），
            // 所以画面自检和对外展示看到的成像是完整的；可玩场景暂时不挂，保证游戏能跑。
            StationPostProcess post = null;
            if (!_playableRig)
            {
                post = go.AddComponent<StationPostProcess>();
            }
            // 截图机位关掉颗粒动画，否则同一场景每次截出来都不一样，画面比对就没有基准了。
            if (post != null)
            {
                // 截图机位关掉颗粒动画，否则同一场景每次截出来都不一样，画面比对就没有基准了。
                post.animateGrain = false;
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

            // 自发光件（屏幕、指示灯、标签）是纯色的，铺贴图只会把它们弄脏。
            if (mat != null && !mat.IsKeywordEnabled("_EMISSION"))
            {
                ApplyUvTiling(go, size, TilesPerMeterFor(mat));
            }
        }

        private static readonly Dictionary<string, Mesh> UvMeshCache = new Dictionary<string, Mesh>();

        /// <summary>
        /// 把贴图平铺次数直接烘进网格的 UV。
        ///
        /// 场景里的几何全是缩放过的图元，UV 一律 0 到 1。直接贴图的话，
        /// 一块两米多宽的面板和一个五厘米的旋钮会各自铺满一整张贴图，
        /// 前者糊成一片，后者细到看不见。必须按世界尺寸换算平铺次数。
        ///
        /// 早先的做法是挂一个 ExecuteAlways 组件在编辑器里改 Renderer 的
        /// MaterialPropertyBlock，结果保存出来的场景在运行时报 level0 corrupted
        /// 直接崩溃。改成生成期烘进网格之后，运行时不再有任何组件参与，
        /// 编辑器截图和实际运行看到的也保证是同一套 UV。
        /// </summary>
        private static void ApplyUvTiling(GameObject go, Vector3 size, float tilesPerMeter)
        {
            var filter = go.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                return;
            }

            // 取最薄的那一维作为厚度方向，另外两维决定贴图铺开的比例。
            var a = Mathf.Abs(size.x);
            var b = Mathf.Abs(size.y);
            var c = Mathf.Abs(size.z);
            var smallest = Mathf.Min(a, Mathf.Min(b, c));

            float u, v;
            if (Mathf.Approximately(smallest, a)) { u = c; v = b; }
            else if (Mathf.Approximately(smallest, b)) { u = a; v = c; }
            else { u = a; v = b; }

            // 平铺量量化到 0.25 的整数倍。
            //
            // 不量化的话，每个尺寸略有不同的物件都会生成一份独立网格。
            // 补上桌面道具之后这个数量翻了上去，叠加可玩场景的那些组件，
            // 构建出的 level0 就会损坏——运行时报 corrupted 直接崩溃，
            // 而构建过程毫无提示。量化之后网格被大量复用，
            // 视觉上的差别在这个尺度下看不出来。
            const float step = 0.25f;
            var tileU = Mathf.Max(step, Mathf.Round(u * tilesPerMeter / step) * step);
            var tileV = Mathf.Max(step, Mathf.Round(v * tilesPerMeter / step) * step);

            var source = filter.sharedMesh;
            var key = $"{source.name}|{tileU:F2}|{tileV:F2}";
            if (!UvMeshCache.TryGetValue(key, out var mesh))
            {
                mesh = UnityEngine.Object.Instantiate(source);
                mesh.name = $"{source.name}_UV{tileU:F2}x{tileV:F2}";
                var uvs = mesh.uv;
                for (var i = 0; i < uvs.Length; i++)
                {
                    uvs[i] = new Vector2(uvs[i].x * tileU, uvs[i].y * tileV);
                }

                mesh.uv = uvs;
                mesh.UploadMeshData(false);
                UvMeshCache[key] = mesh;
            }

            filter.sharedMesh = mesh;
        }

        private static void Log(string message)
        {
            Debug.Log($"[ProbeSceneBuilder] {message}");
            Console.WriteLine($"[ProbeSceneBuilder] {message}");
        }
    }
}
