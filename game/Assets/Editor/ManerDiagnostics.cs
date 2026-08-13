using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Maner.EditorTools
{
    /// <summary>
    /// 渲染问题的最小可复现场景。当实机画面出现「灯在但不亮」这类无头环境下
    /// 很难定位的现象时，用它把变量降到最低：一个地面、一个盒子、一盏灯。
    /// </summary>
    public static class ManerDiagnostics
    {
        const string ProbeScenePath = "Assets/Scenes/LightProbeDiagnostic.unity";

        /// <summary>只有点光源，没有平行光。用于判断附加光源是否真的参与渲染。</summary>
        public static void BuildPointLightProbe() => BuildProbe(LightType.Point);

        /// <summary>只有平行光，作为对照组。</summary>
        public static void BuildDirectionalProbe() => BuildProbe(LightType.Directional);

        static void BuildProbe(LightType lightType)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.02f, 0.02f, 0.02f);
            RenderSettings.ambientEquatorColor = new Color(0.02f, 0.02f, 0.02f);
            RenderSettings.ambientGroundColor = new Color(0.02f, 0.02f, 0.02f);
            RenderSettings.fog = false;
            RenderSettings.skybox = null;
            DynamicGI.UpdateEnvironment();

            var probeGo = new GameObject("ProbeSpawner");
            var probe = probeGo.AddComponent<Maner.Cabin.LightProbeDiagnostic>();
            probe.lightType = lightType;

            var cameraGo = new GameObject("ProbeCamera");
            cameraGo.tag = "MainCamera";
            var camera = cameraGo.AddComponent<Camera>();
            camera.fieldOfView = 60f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            cameraGo.transform.position = new Vector3(0f, 2.2f, -4.5f);
            cameraGo.transform.rotation = Quaternion.Euler(14f, 0f, 0f);
            cameraGo.AddComponent<Maner.SelfCheck.AutoScreenshotRunner>();

            EditorSceneManager.SaveScene(scene, ProbeScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ProbeScenePath, true) };
            AssetDatabase.SaveAssets();

            Console.WriteLine($"[ManerDiagnostics] 已生成 {lightType} 探针场景，并设为唯一构建场景");
            EditorApplication.Exit(0);
        }

        /// <summary>把构建场景恢复为正常的两个场景。</summary>
        public static void RestoreScenes()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene("Assets/Scenes/Cabin.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/RenderValidation.unity", true),
            };
            AssetDatabase.SaveAssets();
            Console.WriteLine("[ManerDiagnostics] 已恢复正常构建场景");
            EditorApplication.Exit(0);
        }
    }
}
