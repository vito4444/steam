using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Maner.Cabin
{
    /// <summary>
    /// 控制舱的照明与后处理。目标画面是概念图定下的基调：
    /// 大面积深色、单一暖色钨丝灯做主光、仪表指示灯做点缀、暗部保持真正的黑。
    /// 这些数字会随视觉回归对比的结果反复调整，因此集中在这一处。
    /// </summary>
    public static class CabinLighting
    {
        // 这一组数字通过「构建 → 无头截图 → 与概念图做直方图比对」的循环标定，
        // 目标是逼近概念图的影调分布：平均亮度约 0.09、暗部占比约 0.94、
        // 少量明确高光集中在仪表面与灯泡上。改动任意一项都应重跑一次视觉回归。
        // 照明架构说明：
        // 舱内主照明由两盏平行光承担，而不是由那盏顶灯的点光源承担。原因是实测下来，
        // 点光源在本项目的渲染配置里衰减极重——把强度提到 12000 candela 才勉强让画面
        // 平均亮度翻一倍，而一盏 22 单位的平行光就能把面板照到位。在这间 5 米见方的
        // 屋子里，光源与物体的距离差本来就不足以产生明显衰减，用平行光模拟顶灯的
        // 主要投射方向在视觉上没有损失，还省掉了附加光源的开销。
        // 点光源保留下来，负责灯泡周围那一小圈暖色光晕。
        const float KeyDirectionalIntensity = 0.34f;
        const float FillDirectionalIntensity = 0.055f;
        const float BulbGlowIntensity = 7.0f;
        const float PanelWashIntensity = 4.2f;
        const float PostExposure = 0.10f;
        const float AmbientFillLux = 22f;

        /// <summary>
        /// 照明配置的运行时开关。无头环境下没有 Inspector 可以试错，
        /// 只能靠命令行参数在一次构建里做 A/B 二分，快速定位「哪一层把画面吃黑了」。
        /// </summary>
        static bool HasArg(string name)
        {
            foreach (var arg in System.Environment.GetCommandLineArgs())
            {
                if (arg == name)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 读取一个浮点命令行参数。视觉标定需要在同一份构建里反复试不同的光强组合，
        /// 每改一个常数就重新构建一次太慢，所以把标定量做成可从命令行覆盖的。
        /// </summary>
        static float ArgFloat(string name, float fallback)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name &&
                    float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float v))
                {
                    return v;
                }
            }
            return fallback;
        }

        public static void Apply(Transform cabinRoot, CabinMaterials materials)
        {
            bool noPostFx = HasArg("-manerNoPostFX");
            bool noFill = HasArg("-manerNoFill");
            bool noWash = HasArg("-manerNoWash");
            Debug.Log($"[CabinLight] 构建标记 lighting-rev13-postfx-calibrated key={KeyDirectionalIntensity} " +
                      $"fill={FillDirectionalIntensity} bulb={BulbGlowIntensity} wash={PanelWashIntensity} " +
                      $"noPostFX={noPostFx} noFill={noFill} noWash={noWash}");

            SetupAmbient();
            if (!noFill)
            {
                BuildAmbientFill(cabinRoot);
            }
            BuildCagedBulb(cabinRoot, materials, new Vector3(0f, CabinBuilder.RoomHeight - 0.20f, -0.35f));
            if (!noWash)
            {
                BuildPanelWashLights(cabinRoot);
            }
            BuildShaftGlow(cabinRoot);
            if (!noPostFx)
            {
                BuildVolume(cabinRoot);
            }
            LogDiagnostics();
        }

        /// <summary>
        /// 一盏很弱的平行光作为整体底子。URP 把平行光当作主光源单独处理，
        /// 它不受附加光源数量限制，也不依赖点光源的衰减，因此同时充当照明兜底。
        /// </summary>
        static void BuildAmbientFill(Transform parent)
        {
            // 主光：模拟顶灯投向控制台的方向。面板法线大致朝 (0, 0.24, -0.97)，
            // 光线自玩家侧上方压向面板，pitch 取 22 度既能照亮面板又能在上沿留出层次。
            var keyGo = new GameObject("KeyDirectional");
            keyGo.transform.SetParent(parent, false);
            keyGo.transform.rotation = Quaternion.Euler(22f, -6f, 0f);

            var key = keyGo.AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = new Color(1f, 0.80f, 0.56f);
            key.intensity = ArgFloat("-manerKey", KeyDirectionalIntensity);
            key.shadows = HasArg("-manerNoShadow") ? LightShadows.None : LightShadows.Soft;
            key.shadowStrength = 0.85f;
            key.shadowBias = 0.02f;
            key.shadowNormalBias = 0.35f;

            // 补光：从舷窗方向渗进来的冷色天光，只负责把暗部从死黑里拉出一点点。
            var fillGo = new GameObject("FillDirectional");
            fillGo.transform.SetParent(parent, false);
            fillGo.transform.rotation = Quaternion.Euler(-14f, 172f, 0f);

            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.62f, 0.72f, 0.95f);
            fill.intensity = ArgFloat("-manerFill", FillDirectionalIntensity);
            fill.shadows = LightShadows.None;
        }

        /// <summary>
        /// 舱内照明全靠附加光源，任何一处配置不对都会表现为「一片漆黑」，
        /// 而无头环境下没有 Inspector 可看，所以把关键参数直接打进日志。
        /// </summary>
        static void LogDiagnostics()
        {
            var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            foreach (var l in lights)
            {
                Debug.Log($"[CabinLight] {l.name} type={l.type} intensity={l.intensity} range={l.range} " +
                          $"enabled={l.enabled} shadows={l.shadows} lux={l.lightUnit}");
            }

            var rp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (rp != null)
            {
                Debug.Log($"[CabinLight] URP additionalLights={rp.additionalLightsRenderingMode} " +
                          $"perObjectLimit={rp.maxAdditionalLightsCount} " +
                          $"addShadows={rp.supportsAdditionalLightShadows} hdr={rp.supportsHDR}");
            }
            else
            {
                Debug.LogError("[CabinLight] 当前渲染管线不是 URP 资产");
            }

            Debug.Log($"[CabinLight] ambientMode={RenderSettings.ambientMode} " +
                      $"sky={RenderSettings.ambientSkyColor} intensity={RenderSettings.ambientIntensity}");

            var cam = Camera.main;
            if (cam != null)
            {
                var data = cam.GetUniversalAdditionalCameraData();
                Debug.Log($"[CabinLight] camera postFX={data.renderPostProcessing} aa={data.antialiasing} " +
                          $"hdr={cam.allowHDR} volumeLayer={data.volumeLayerMask.value}");
            }
        }

        public static void SetupAmbient()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            // 环境光刻意压得很低：这间屋子里除了那盏灯，没有别的光源。
            RenderSettings.ambientSkyColor = new Color(0.026f, 0.026f, 0.030f);
            RenderSettings.ambientEquatorColor = new Color(0.022f, 0.020f, 0.018f);
            RenderSettings.ambientGroundColor = new Color(0.016f, 0.014f, 0.012f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.055f;
            RenderSettings.fogColor = new Color(0.030f, 0.028f, 0.026f);
            RenderSettings.skybox = null;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.reflectionIntensity = 0.12f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = null;

            // 改完 RenderSettings 的环境项必须显式刷新，否则引擎继续沿用上一次烘焙的
            // 环境探针。场景默认是 Skybox 环境光，不刷新的话整间屋子会被天空的蓝白冲亮，
            // 所有金属面反射出一片惨白——这正是第一版实机截图的症状。
            DynamicGI.UpdateEnvironment();
        }

        /// <summary>头顶那盏带铁笼的钨丝灯，是舱内唯一的主光源。</summary>
        static void BuildCagedBulb(Transform parent, CabinMaterials materials, Vector3 position)
        {
            var holder = new GameObject("CagedBulb");
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = position;

            var b = new MeshBuilder();
            b.AddCylinder(new Vector3(0f, 0.06f, 0f), 0.028f, 0.12f, 10);
            b.AddCylinder(new Vector3(0f, -0.02f, 0f), 0.075f, 0.05f, 14);
            for (int i = 0; i < 6; i++)
            {
                float a = i / 6f * Mathf.PI * 2f;
                b.AddBox(
                    new Vector3(Mathf.Cos(a) * 0.085f, -0.13f, Mathf.Sin(a) * 0.085f),
                    new Vector3(0.012f, 0.22f, 0.012f));
            }
            b.AddTorus(new Vector3(0f, -0.24f, 0f), 0.078f, 0.010f, 16, 5);
            Spawn("Cage", holder.transform, b.ToMesh("Bulb_Cage"), materials.FrameSteel);

            // 灯泡玻璃必须关掉投影：光源就在这颗球体的球心，一旦它参与投影，
            // 整间屋子会被灯泡自己的影子完全罩住，表现为「光源存在但一片漆黑」。
            var glass = new MeshBuilder();
            glass.AddSphere(new Vector3(0f, -0.13f, 0f), 0.055f, 8, 12);
            var bulb = Spawn("Bulb", holder.transform, glass.ToMesh("Bulb_Glass"), materials.LampAmber);
            bulb.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var lightGo = new GameObject("KeyLight");
            lightGo.transform.SetParent(holder.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, -0.13f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.70f, 0.38f);
            light.intensity = ArgFloat("-manerBulb", BulbGlowIntensity);
            light.range = 3.2f;
            light.shadows = LightShadows.None;
        }

        /// <summary>
        /// 面板上方的三盏小射灯。它们的存在是为了让仪表可读，
        /// 同时在面板金属上压出一条明确的高光带，避免大片死黑。
        /// </summary>
        static void BuildPanelWashLights(Transform parent)
        {
            float[] yaw = { -40f, 0f, 40f };
            for (int i = 0; i < yaw.Length; i++)
            {
                var go = new GameObject($"PanelWash_{i}");
                go.transform.SetParent(parent, false);
                Vector3 dir = Quaternion.Euler(0f, yaw[i], 0f) * Vector3.forward;
                go.transform.position = new Vector3(0f, 0f, -0.58f) + dir * 1.15f + Vector3.up * 2.24f;
                go.transform.rotation = Quaternion.LookRotation(Quaternion.Euler(0f, yaw[i], 0f) * new Vector3(0f, -0.82f, 0.57f));

                var light = go.AddComponent<Light>();
                light.type = LightType.Spot;
                light.spotAngle = 96f;
                light.innerSpotAngle = 12f;
                light.color = new Color(1f, 0.84f, 0.62f);
                light.intensity = ArgFloat("-manerWash", PanelWashIntensity);
                light.range = 3.6f;
                light.shadows = LightShadows.None;
            }
        }

        /// <summary>
        /// 井筒里那点微光。它不照亮舱内任何东西，只是让舷窗外不至于是一块纯黑的圆——
        /// 你能看见井壁的一圈支架轮廓，知道那里有很深的东西，但看不清。
        /// 光源刻意放得很远且强度很低，罐笼经过时会先亮起再暗下去。
        /// </summary>
        public static void BuildShaftGlow(Transform parent)
        {
            var go = new GameObject("ShaftGlow");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 1.10f, CabinBuilder.RoomDepth * 0.5f + 1.9f);

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.62f, 0.70f, 0.86f);
            light.intensity = ArgFloat("-manerShaftGlow", 5.5f);
            light.range = 4.2f;
            light.shadows = LightShadows.None;
        }

        static void BuildVolume(Transform parent)
        {
            var go = new GameObject("PostProcessing");
            go.transform.SetParent(parent, false);

            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1f;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "Cabin_PostFX";

            var tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.ACES);

            var colorAdjust = profile.Add<ColorAdjustments>(true);
            colorAdjust.postExposure.Override(ArgFloat("-manerExposure", PostExposure));
            colorAdjust.contrast.Override(ArgFloat("-manerContrast", 13f));
            colorAdjust.saturation.Override(ArgFloat("-manerSaturation", 11f));
            colorAdjust.colorFilter.Override(new Color(1f, 0.94f, 0.86f));

            var whiteBalance = profile.Add<WhiteBalance>(true);
            whiteBalance.temperature.Override(11f);
            whiteBalance.tint.Override(-4f);

            var bloom = profile.Add<Bloom>(true);
            // 泛光压得很克制：软件光栅化下大范围扩散会在高对比边缘留下彩色伪影，
            // 而这个场景里真正该发光的只有灯泡、指示灯与背光表盘。
            bloom.threshold.Override(ArgFloat("-manerBloomThreshold", 1.38f));
            bloom.intensity.Override(ArgFloat("-manerBloom", 0.15f));
            bloom.scatter.Override(0.55f);
            bloom.tint.Override(new Color(1f, 0.86f, 0.66f));

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.42f);
            vignette.smoothness.Override(0.45f);
            vignette.color.Override(new Color(0.02f, 0.018f, 0.016f));

            var grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Medium2);
            grain.intensity.Override(0.32f);
            grain.response.Override(0.72f);

            var curves = profile.Add<ShadowsMidtonesHighlights>(true);
            curves.shadows.Override(new Vector4(0.92f, 0.94f, 1.02f, -0.03f));
            curves.midtones.Override(new Vector4(1.02f, 1.0f, 0.97f, 0f));
            curves.highlights.Override(new Vector4(1.04f, 0.99f, 0.92f, 0f));

            volume.sharedProfile = profile;
        }

        static GameObject Spawn(string name, Transform parent, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }
    }
}
