using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Abyssal.EditorTools
{
    /// <summary>
    /// 一次性配置 URP 管线资产。
    ///
    /// 参数是按「无 GPU 的开发机上要能跑、玩家机器上要好看」这两个目标同时调的：
    /// 关掉所有昂贵的屏幕空间效果，把预算全部留给少量高质量的点光源和 Bloom。
    /// 控制舱是个封闭小空间，这个取舍在视觉上几乎没有损失。
    /// </summary>
    public static class SetupUrp
    {
        const string Dir = "Assets/Abyssal/Settings";
        const string RendererPath = Dir + "/AbyssalRenderer.asset";
        const string PipelinePath = Dir + "/AbyssalPipeline.asset";

        [MenuItem("Abyssal/Setup URP Pipeline")]
        public static void Run()
        {
            Directory.CreateDirectory(Dir);

            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rendererData, RendererPath);
            }

            // Bloom 是这个方案的核心视觉手段：舱内环境光被压到近乎为零，
            // 全靠仪表和告警灯溢出的辉光把体积感撑起来。后处理绝对不能关。
            // 手工创建的 UniversalRendererData 不会自动填 postProcessData，
            // 必须从 URP 包里把默认资产挂上去，否则整条后处理链路静默失效。
            if (rendererData.postProcessData == null)
            {
                const string defaults =
                    "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset";
                rendererData.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(defaults);
                if (rendererData.postProcessData == null)
                    Debug.LogError($"ABYSSAL: 找不到默认后处理资源 {defaults}，Bloom 不会生效");
            }
            rendererData.shadowTransparentReceive = false;
            EditorUtility.SetDirty(rendererData);

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }

            var so = new SerializedObject(pipeline);

            // 舱内只有一盏手电级别的投影光，其余告警灯全部不投影。
            SetIfPresent(so, "m_MainLightShadowsSupported", true);
            SetIfPresent(so, "m_MainLightShadowmapResolution", 1024);
            SetIfPresent(so, "m_AdditionalLightsPerObjectLimit", 8);
            SetIfPresent(so, "m_AdditionalLightShadowsSupported", false);
            SetIfPresent(so, "m_ShadowDistance", 18f);
            SetIfPresent(so, "m_SoftShadowsSupported", true);

            // 小场景不需要 HDR 之外的额外精度，也不需要 MSAA 之外的抗锯齿。
            SetIfPresent(so, "m_SupportsHDR", true);
            SetIfPresent(so, "m_MSAA", 2);
            SetIfPresent(so, "m_RenderScale", 1f);

            // 屏幕空间效果在低多边形封闭空间里性价比极低，全部关掉。
            SetIfPresent(so, "m_SupportsCameraDepthTexture", true);
            SetIfPresent(so, "m_SupportsCameraOpaqueTexture", false);

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline);

            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"ABYSSAL: URP pipeline configured at {PipelinePath}");
        }

        static void SetIfPresent(SerializedObject so, string property, bool value)
        {
            var p = so.FindProperty(property);
            if (p != null) p.boolValue = value;
        }

        static void SetIfPresent(SerializedObject so, string property, int value)
        {
            var p = so.FindProperty(property);
            if (p != null) p.intValue = value;
        }

        static void SetIfPresent(SerializedObject so, string property, float value)
        {
            var p = so.FindProperty(property);
            if (p != null) p.floatValue = value;
        }
    }
}
