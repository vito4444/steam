using System;
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

        public static void Run()
        {
            try
            {
                EnsureFolders();
                var pipeline = EnsurePipelineAsset();
                ApplyPipeline(pipeline);
                ApplyPlayerSettings();
                BuildValidationScene();
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
            GraphicsSettings.defaultRenderPipeline = pipeline;
            for (int i = 0; i < QualitySettings.count; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
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
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ValidationScenePath, true) };
            Console.WriteLine($"[ManerBootstrap] 已生成验证场景 {ValidationScenePath}");
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
