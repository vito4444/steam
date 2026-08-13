using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Maner.EditorTools
{
    /// <summary>
    /// 一次性工程初始化：建立 URP 资产、写入 Player 设置、生成渲染验证场景。
    /// 通过 -executeMethod Maner.EditorTools.ManerBootstrap.Run 调用。
    /// </summary>
    public static class ManerBootstrap
    {
        const string SettingsDir = "Assets/Settings";
        const string ScenesDir = "Assets/Scenes";
        const string RendererPath = SettingsDir + "/Maner-UniversalRenderer.asset";
        const string PipelinePath = SettingsDir + "/Maner-UniversalRenderPipeline.asset";
        const string ValidationScenePath = ScenesDir + "/RenderValidation.unity";
        const string CabinScenePath = ScenesDir + "/Cabin.unity";

        public static void Run()
        {
            try
            {
                EnsureFolders();
                var pipeline = EnsurePipelineAsset();
                ApplyPipeline(pipeline);
                EnsureAlwaysIncludedShaders(
                    "Universal Render Pipeline/Lit",
                    "Universal Render Pipeline/Unlit",
                    "Universal Render Pipeline/Simple Lit");
                ApplyPlayerSettings();
                BuildValidationScene();
                BuildCabinScene();
                EditorBuildSettings.scenes = new[]
                {
                    new EditorBuildSettingsScene(CabinScenePath, true),
                    new EditorBuildSettingsScene(ValidationScenePath, true),
                };
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Console.WriteLine("[ManerBootstrap] 工程初始化完成");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Console.WriteLine($"[ManerBootstrap] 失败: {e}");
                EditorApplication.Exit(1);
            }
        }

        static void EnsureFolders()
        {
            foreach (var dir in new[] { SettingsDir, ScenesDir })
            {
                if (!AssetDatabase.IsValidFolder(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }
            AssetDatabase.Refresh();
        }

        static UniversalRenderPipelineAsset EnsurePipelineAsset()
        {
            var existing = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (existing != null)
            {
                return existing;
            }

            var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
            rendererData.name = "Maner-UniversalRenderer";
            AssetDatabase.CreateAsset(rendererData, RendererPath);

            var pipeline = UniversalRenderPipelineAsset.Create(rendererData);
            pipeline.name = "Maner-UniversalRenderPipeline";
            AssetDatabase.CreateAsset(pipeline, PipelinePath);
            AssetDatabase.SaveAssets();
            return pipeline;
        }

        static void ApplyPipeline(UniversalRenderPipelineAsset pipeline)
        {
            ConfigurePipelineAsset(pipeline);

            GraphicsSettings.defaultRenderPipeline = pipeline;
            for (int i = 0; i < QualitySettings.count; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
        }

        /// <summary>
        /// 控制舱里没有任何平行光，全部照明都是点光源与聚光灯，也就是 URP 口中的
        /// 「附加光源」。URP 资产默认只允许很少的附加光源且不投附加光源阴影，
        /// 不显式放开的话整间屋子会是全黑的。
        /// </summary>
        static void ConfigurePipelineAsset(UniversalRenderPipelineAsset pipeline)
        {
            var so = new SerializedObject(pipeline);
            var names = new List<string>();
            var iterator = so.GetIterator();
            while (iterator.NextVisible(true))
            {
                names.Add(iterator.propertyPath);
            }
            Console.WriteLine($"[ManerBootstrap] URP 资产字段: {string.Join(",", names)}");

            SetInt(so, "m_AdditionalLightsRenderingMode", 1);   // PerPixel
            SetInt(so, "m_AdditionalLightsPerObjectLimit", 8);
            SetBool(so, "m_AdditionalLightShadowsSupported", true);
            SetInt(so, "m_AdditionalLightsShadowmapResolution", 1024);
            SetBool(so, "m_MainLightShadowsSupported", true);
            SetBool(so, "m_SoftShadowsSupported", true);
            SetBool(so, "m_SupportsHDR", true);
            SetInt(so, "m_ShadowCascadeCount", 1);
            SetFloat(so, "m_ShadowDistance", 24f);
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(pipeline);

            ConfigureRendererData();
            DisableShaderVariantStripping();
        }

        /// <summary>
        /// 关闭 URP 的着色器变体剥离。
        ///
        /// URP 会根据「资产里用到了什么」来裁剪着色器变体，而本项目的材质、光源、
        /// 后处理全部在运行时用代码创建，资产扫描阶段看不到任何使用痕迹，
        /// 于是附加光源与自发光的变体被整批裁掉。表现是极具迷惑性的：
        /// 灯在场景里、强度也对、日志一切正常，但它们对画面的贡献恒为零，
        /// 而平行光却完好无损。编辑器里同样看不出来，因为编辑器不做剥离。
        /// </summary>
        static void DisableShaderVariantStripping()
        {
            string[] guids = AssetDatabase.FindAssets("t:UniversalRenderPipelineGlobalSettings");
            if (guids.Length == 0)
            {
                Console.WriteLine("[ManerBootstrap] 找不到 URP Global Settings 资产");
                return;
            }

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (asset == null)
                {
                    continue;
                }

                var so = new SerializedObject(asset);
                string[] flags =
                {
                    "m_StripUnusedVariants",
                    "m_StripUnusedPostProcessingVariants",
                    "m_StripScreenCoordOverrideVariants",
                    "m_StripDebugVariants",
                };

                foreach (var flag in flags)
                {
                    var prop = so.FindProperty(flag);
                    if (prop != null && prop.propertyType == SerializedPropertyType.Boolean)
                    {
                        prop.boolValue = false;
                    }
                }

                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(asset);
                Console.WriteLine($"[ManerBootstrap] 已关闭着色器变体剥离: {path}");
            }
        }

        /// <summary>
        /// 把渲染器切回传统 Forward。Unity 6 的 URP 默认使用 Forward+，它靠 cluster
        /// 光照剔除来处理附加光源；在本项目的软件光栅化开发环境里，这条路径下的
        /// 点光源与聚光灯完全不出光，表现为「灯亮着但屋子全黑」，而平行光正常。
        /// 传统 Forward 没有这个问题，对低端硬件的兼容性也更好。
        /// </summary>
        static void ConfigureRendererData()
        {
            var rendererData = AssetDatabase.LoadAssetAtPath<ScriptableObject>(RendererPath);
            if (rendererData == null)
            {
                Console.WriteLine($"[ManerBootstrap] 读不到渲染器资产 {RendererPath}");
                return;
            }

            var so = new SerializedObject(rendererData);
            var mode = so.FindProperty("m_RenderingMode");
            if (mode == null)
            {
                var names = new List<string>();
                var it = so.GetIterator();
                while (it.NextVisible(true))
                {
                    names.Add(it.propertyPath);
                }
                Console.WriteLine($"[ManerBootstrap] 渲染器没有 m_RenderingMode，可用字段: {string.Join(",", names)}");
                return;
            }

            mode.intValue = 0; // Forward
            var depthPriming = so.FindProperty("m_DepthPrimingMode");
            if (depthPriming != null)
            {
                depthPriming.intValue = 0; // Disabled
            }
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(rendererData);
            Console.WriteLine("[ManerBootstrap] 渲染器已切换为传统 Forward");
        }

        static void SetInt(SerializedObject so, string path, int value)
        {
            var p = so.FindProperty(path);
            if (p != null)
            {
                p.intValue = value;
            }
            else
            {
                Console.WriteLine($"[ManerBootstrap] URP 资产没有字段 {path}");
            }
        }

        static void SetBool(SerializedObject so, string path, bool value)
        {
            var p = so.FindProperty(path);
            if (p != null)
            {
                p.boolValue = value;
            }
            else
            {
                Console.WriteLine($"[ManerBootstrap] URP 资产没有字段 {path}");
            }
        }

        static void SetFloat(SerializedObject so, string path, float value)
        {
            var p = so.FindProperty(path);
            if (p != null)
            {
                p.floatValue = value;
            }
            else
            {
                Console.WriteLine($"[ManerBootstrap] URP 资产没有字段 {path}");
            }
        }

        /// <summary>
        /// 把运行时靠 Shader.Find 取用的 shader 写进 Always Included Shaders。
        /// 本项目的材质全部由代码在运行时创建，没有任何资产引用这些 shader，
        /// 不显式包含的话构建后会全部退化成白色默认材质。
        /// </summary>
        static void EnsureAlwaysIncludedShaders(params string[] shaderNames)
        {
            var graphicsSettings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (graphicsSettings == null || graphicsSettings.Length == 0)
            {
                Console.WriteLine("[ManerBootstrap] 读不到 GraphicsSettings.asset");
                return;
            }

            var so = new SerializedObject(graphicsSettings[0]);
            var arrayProp = so.FindProperty("m_AlwaysIncludedShaders");
            if (arrayProp == null)
            {
                Console.WriteLine("[ManerBootstrap] GraphicsSettings 里找不到 m_AlwaysIncludedShaders");
                return;
            }

            foreach (var name in shaderNames)
            {
                var shader = Shader.Find(name);
                if (shader == null)
                {
                    Console.WriteLine($"[ManerBootstrap] 找不到 shader {name}");
                    continue;
                }

                bool present = false;
                for (int i = 0; i < arrayProp.arraySize; i++)
                {
                    if (arrayProp.GetArrayElementAtIndex(i).objectReferenceValue == shader)
                    {
                        present = true;
                        break;
                    }
                }

                if (present)
                {
                    continue;
                }

                int index = arrayProp.arraySize;
                arrayProp.InsertArrayElementAtIndex(index);
                arrayProp.GetArrayElementAtIndex(index).objectReferenceValue = shader;
                Console.WriteLine($"[ManerBootstrap] 已加入 Always Included Shader: {name}");
            }

            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }

        static void ApplyPlayerSettings()
        {
            PlayerSettings.companyName = "Maner Works";
            PlayerSettings.productName = "MANER";
            PlayerSettings.bundleVersion = "0.0.1";
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.runInBackground = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Standalone, ApiCompatibilityLevel.NET_Unity_4_8);
        }

        /// <summary>
        /// 生成一个纯程序化的渲染验证场景。用途是确认在无 GPU 的云端环境下
        /// URP 能正确出图、截图管线可用，并作为逐版本视觉回归的基准。
        /// 场景内容刻意保持中性，不预设任何一个游戏方案。
        /// </summary>
        static void BuildValidationScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraGo = new GameObject("MainCamera");
            cameraGo.tag = "MainCamera";
            var camera = cameraGo.AddComponent<Camera>();
            camera.fieldOfView = 60f;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 200f;
            cameraGo.transform.SetPositionAndRotation(new Vector3(0f, 2.4f, -7.5f), Quaternion.Euler(12f, 0f, 0f));
            cameraGo.AddComponent<Maner.SelfCheck.AutoScreenshotRunner>();

            var sunGo = new GameObject("KeyLight");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.6f;
            sun.color = new Color(1f, 0.94f, 0.86f);
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(38f, 145f, 0f);

            var fillGo = new GameObject("FillLight");
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.35f;
            fill.color = new Color(0.55f, 0.66f, 0.85f);
            fill.shadows = LightShadows.None;
            fillGo.transform.rotation = Quaternion.Euler(20f, -40f, 0f);

            var litShader = Shader.Find("Universal Render Pipeline/Lit");

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(4f, 1f, 4f);
            floor.GetComponent<Renderer>().sharedMaterial = MakeMaterial(litShader, "M_Floor", new Color(0.22f, 0.22f, 0.24f), 0.85f, 0.05f);

            // 一排材质递变的球体，用于检查光照、粗糙度与金属度在软件渲染下的表现。
            for (int i = 0; i < 7; i++)
            {
                float t = i / 6f;
                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.name = $"MatProbe_{i}";
                sphere.transform.position = new Vector3(-4.5f + i * 1.5f, 0.75f, 0f);
                sphere.transform.localScale = Vector3.one * 1.5f;
                sphere.GetComponent<Renderer>().sharedMaterial =
                    MakeMaterial(litShader, $"M_Probe_{i}", Color.Lerp(new Color(0.75f, 0.35f, 0.15f), new Color(0.35f, 0.62f, 0.72f), t), 1f - t, t);
            }

            // 一组不同高度的方块，用于检查阴影投射与深度。
            for (int i = 0; i < 5; i++)
            {
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = $"ShadowCaster_{i}";
                float h = 0.6f + i * 0.55f;
                box.transform.position = new Vector3(-3.6f + i * 1.8f, h * 0.5f, 3.2f);
                box.transform.localScale = new Vector3(0.9f, h, 0.9f);
                box.transform.rotation = Quaternion.Euler(0f, 18f * i, 0f);
                box.GetComponent<Renderer>().sharedMaterial =
                    MakeMaterial(litShader, $"M_Box_{i}", new Color(0.58f, 0.56f, 0.5f), 0.6f, 0f);
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.32f, 0.36f, 0.44f);
            RenderSettings.ambientEquatorColor = new Color(0.22f, 0.22f, 0.24f);
            RenderSettings.ambientGroundColor = new Color(0.1f, 0.09f, 0.08f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.012f;
            RenderSettings.fogColor = new Color(0.18f, 0.2f, 0.24f);

            EditorSceneManager.SaveScene(scene, ValidationScenePath);
            Console.WriteLine($"[ManerBootstrap] 已生成验证场景 {ValidationScenePath}");
        }

        /// <summary>
        /// 主场景。场景文件本身几乎是空的——控制舱的每一块钢板都在运行时由
        /// CabinRuntime 生成。这样布局改动只需要改代码，不需要重做场景资产，
        /// 也让整个视觉产出可以纳入代码审查。
        /// </summary>
        static void BuildCabinScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var runtimeGo = new GameObject("CabinRuntime");
            runtimeGo.AddComponent<Maner.Cabin.CabinRuntime>();

            var cameraGo = new GameObject("OperatorCamera");
            cameraGo.tag = "MainCamera";
            var camera = cameraGo.AddComponent<Camera>();
            camera.fieldOfView = 70f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.012f, 0.011f, 0.010f);
            camera.nearClipPlane = 0.04f;
            camera.farClipPlane = 60f;

            // 站姿眼高 1.62 m，视线接近水平：低头是仪表，抬眼是舷窗。
            // 与方案文档 2.5 节的相机规格一致。
            cameraGo.transform.position = new Vector3(0f, 1.62f, -0.98f);
            cameraGo.transform.rotation = Quaternion.LookRotation(new Vector3(0f, -0.10f, 1.99f).normalized);

            // 不显式打开的话，Volume 里的色调映射、曝光与 bloom 全部不会执行。
            var cameraData = cameraGo.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cameraData.antialiasingQuality = AntialiasingQuality.Medium;
            cameraData.renderShadows = true;

            cameraGo.AddComponent<Maner.SelfCheck.AutoScreenshotRunner>();

            var listener = new GameObject("AudioListener");
            listener.transform.position = cameraGo.transform.position;
            listener.AddComponent<AudioListener>();

            // 场景自身的光照设置也要落成暗调，否则从加载到 Awake 之间会闪一帧亮白，
            // 并且编辑器里打开场景看到的与实机不一致。
            Maner.Cabin.CabinLighting.SetupAmbient();

            EditorSceneManager.SaveScene(scene, CabinScenePath);
            Console.WriteLine($"[ManerBootstrap] 已生成控制舱场景 {CabinScenePath}");
        }

        static Material MakeMaterial(Shader shader, string name, Color baseColor, float smoothnessInverse, float metallic)
        {
            var mat = new Material(shader) { name = name };
            mat.SetColor("_BaseColor", baseColor);
            mat.SetFloat("_Smoothness", Mathf.Clamp01(1f - smoothnessInverse));
            mat.SetFloat("_Metallic", Mathf.Clamp01(metallic));
            AssetDatabase.CreateAsset(mat, $"{SettingsDir}/{name}.mat");
            return mat;
        }
    }
}
