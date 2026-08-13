using System;
using System.Collections.Generic;
using System.IO;
using Decoder.Capture;
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
            try
            {
                Random.InitState(20260813);

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

                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
                EditorSceneManager.SaveScene(scene, ScenePath);
                AssetDatabase.SaveAssets();

                Log($"PROBE_SCENE_BUILT {ScenePath}");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ProbeSceneBuilder] 生成失败: {e}");
                Console.Error.WriteLine($"[ProbeSceneBuilder] 生成失败: {e}");
                EditorApplication.Exit(1);
            }
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
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.035f, 0.040f, 0.055f);
            RenderSettings.ambientEquatorColor = new Color(0.022f, 0.024f, 0.030f);
            RenderSettings.ambientGroundColor = new Color(0.012f, 0.012f, 0.014f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.020f, 0.026f, 0.032f);
            RenderSettings.fogDensity = 0.055f;
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

            var crtMat = MakeEmissive("CrtScreen", CrtGreen, 1.35f);
            var meterMat = MakeEmissive("MeterFace", new Color(0.75f, 0.85f, 0.55f), 0.30f);
            var neonMat = MakeEmissive("NeonLamp", NeonAmber, 3.0f);

            // 主机架：三层堆叠的设备箱
            for (var row = 0; row < 3; row++)
            {
                var y = 0.95f + row * 0.52f;
                AddBox(root, $"Rack_{row}", new Vector3(0, y, 0.12f),
                    new Vector3(2.60f, 0.48f, 0.34f), _steelOlive);

                // 每层面板上的旋钮阵列
                var knobCount = row == 1 ? 7 : 5;
                for (var i = 0; i < knobCount; i++)
                {
                    var x = Mathf.Lerp(-1.12f, 1.12f, knobCount == 1 ? 0.5f : i / (float)(knobCount - 1));
                    AddCylinder(root, $"Knob_{row}_{i}",
                        new Vector3(x, y - 0.13f, 0.295f),
                        new Vector3(0.052f, 0.022f, 0.052f),
                        Quaternion.Euler(90, 0, 0), _bakelite);
                    // 旋钮指示线
                    AddBox(root, $"KnobMark_{row}_{i}",
                        new Vector3(x, y - 0.09f, 0.318f),
                        new Vector3(0.006f, 0.028f, 0.004f), _brassKnob);
                }
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
                MakeEmissive("DialStripFace", new Color(0.85f, 0.78f, 0.45f), 0.45f));
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

            var frostMat = MakeEmissive("FrostedGlass", ColdWindow, 0.85f);
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
            var lamp = NewLight(root, "KeyLamp_Warm", LightType.Spot, WarmLamp, 4.2f, 2.3f);
            lamp.transform.localPosition = new Vector3(-0.84f, 1.16f, -0.78f);
            lamp.transform.localRotation = Quaternion.Euler(62f, 22f, 0f);
            lamp.spotAngle = 78f;
            lamp.innerSpotAngle = 26f;
            lamp.shadows = LightShadows.Soft;

            // 2. CRT：绿色，只照亮设备墙前一小段距离。范围压到 1.5m 以内，
            //    否则整间混凝土房都会被染绿，失去三色分区。
            var crt = NewLight(root, "FillLight_CrtGreen", LightType.Point, CrtGreen, 1.5f, 1.45f);
            crt.transform.localPosition = new Vector3(0f, 1.94f, -1.30f);
            crt.shadows = LightShadows.None;

            // 3. 窗光：冷蓝，从右侧斜入，负责把右半边从死黑里拉出来
            var window = NewLight(root, "RimLight_ColdWindow", LightType.Spot, ColdWindow, 3.6f, 4.2f);
            window.transform.localPosition = new Vector3(2.10f, 1.74f, 0.30f);
            window.transform.localRotation = Quaternion.Euler(12f, -112f, 0f);
            window.spotAngle = 76f;
            window.innerSpotAngle = 18f;
            window.shadows = LightShadows.Soft;

            // 补：氖灯排的余光，避免机架上沿死黑
            var neon = NewLight(root, "Practical_NeonSpill", LightType.Point, NeonAmber, 0.55f, 1.1f);
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

            AddShot(root, "probe_front", seat, new Vector3(-6f, 180f, 0f), 66f,
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
            cam.backgroundColor = new Color(0.010f, 0.013f, 0.017f);
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
