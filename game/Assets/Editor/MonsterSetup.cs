using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Monster.EditorTools
{
    /// <summary>
    /// Creates and configures the project's render pipeline assets.
    ///
    /// Everything here is generated from code rather than checked in as binary assets, so
    /// that the rendering configuration is reviewable in a diff and reproducible from a
    /// clean clone.
    /// </summary>
    public static class MonsterSetup
    {
        public const string SettingsFolder = "Assets/Settings";
        public const string PipelineAssetPath = SettingsFolder + "/MonsterURP.asset";
        public const string RendererAssetPath = SettingsFolder + "/MonsterURP_Renderer.asset";
        public const string VolumeProfilePath = SettingsFolder + "/MonsterPostProcess.asset";

        [MenuItem("MONSTER/Setup/Configure Render Pipeline")]
        public static void ConfigureRenderPipeline()
        {
            EnsureFolder(SettingsFolder);

            // Always recreated rather than reused. Reusing an existing asset means the
            // effective configuration depends on which settings previous versions of this
            // method happened to write, so a clean clone and a working tree can render
            // differently from identical source. That actually happened: additional-light
            // shadows were inactive on a carried-over asset and became active on a freshly
            // created one, which changed the lighting without any source change.
            AssetDatabase.DeleteAsset(PipelineAssetPath);
            AssetDatabase.DeleteAsset(RendererAssetPath);

            var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(rendererData, RendererAssetPath);

            var pipeline = UniversalRenderPipelineAsset.Create(rendererData);
            AssetDatabase.CreateAsset(pipeline, PipelineAssetPath);

            // These are the settings a software rasteriser can actually keep up with.
            // HDR stays on because bloom on the CRT screens and taillights is doing real
            // work for the art direction; MSAA goes off because it is the single most
            // expensive thing we could enable here for the least visible benefit.
            pipeline.supportsHDR = true;
            pipeline.msaaSampleCount = 1;
            pipeline.renderScale = 1.0f;
            pipeline.shadowDistance = 22f;
            pipeline.shadowCascadeCount = 1;
            pipeline.supportsCameraDepthTexture = true;
            pipeline.supportsCameraOpaqueTexture = false;

            ApplySerialized(pipeline, new (string, object)[]
            {
                ("m_MainLightShadowsSupported", true),
                ("m_MainLightShadowmapResolution", 1024),
                ("m_AdditionalLightsSupportType", 1),
                ("m_AdditionalLightShadowsSupported", true),
                ("m_AdditionalLightsShadowmapResolution", 512),
                ("m_AdditionalLightsPerObjectLimit", 6),
                ("m_SoftShadowsSupported", false),
                ("m_SupportsTerrainHoles", false),
            });

            EditorUtility.SetDirty(rendererData);
            EditorUtility.SetDirty(pipeline);

            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;

            // Colour space matters more than almost any other single setting for how the
            // lighting reads. Gamma would wash out the single-lamp look entirely.
            PlayerSettings.colorSpace = ColorSpace.Linear;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[MonsterSetup] render pipeline configured: {PipelineAssetPath}");
        }

        /// <summary>Builds the post-process stack. Post is doing most of the visual
        /// identity work here, which is the right trade on a machine with no GPU: screen
        /// space effects are cheap, geometry and lights are not.</summary>
        public static VolumeProfile CreatePostProcessProfile()
        {
            EnsureFolder(SettingsFolder);

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumeProfilePath);
            }

            profile.components.Clear();

            var tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.Neutral);

            var colorAdjustments = profile.Add<ColorAdjustments>(true);
            colorAdjustments.postExposure.Override(-0.40f);
            colorAdjustments.contrast.Override(26f);
            colorAdjustments.saturation.Override(-20f);
            colorAdjustments.colorFilter.Override(new Color(1.0f, 0.95f, 0.86f));

            var whiteBalance = profile.Add<WhiteBalance>(true);
            whiteBalance.temperature.Override(8f);
            whiteBalance.tint.Override(-6f);

            // Bloom is kept tight. At the first pass's settings the CRT faces bloomed into
            // flat white blobs and took the rest of the frame's contrast with them.
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.15f);
            bloom.intensity.Override(0.34f);
            bloom.scatter.Override(0.55f);
            bloom.tint.Override(new Color(1f, 0.90f, 0.76f));

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.60f);
            vignette.smoothness.Override(0.40f);
            vignette.color.Override(new Color(0.015f, 0.015f, 0.022f));

            var grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Medium3);
            grain.intensity.Override(0.72f);
            grain.response.Override(0.80f);

            var aberration = profile.Add<ChromaticAberration>(true);
            aberration.intensity.Override(0.14f);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            var leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, leaf);
        }

        /// <summary>URP exposes only some of its settings as public properties, and which
        /// ones varies between versions. Anything not publicly settable is written through
        /// SerializedObject, skipping fields that do not exist in this URP version rather
        /// than failing the whole setup.</summary>
        private static void ApplySerialized(Object target, (string field, object value)[] values)
        {
            var serialized = new SerializedObject(target);
            foreach (var (field, value) in values)
            {
                var property = serialized.FindProperty(field);
                if (property == null)
                {
                    Debug.Log($"[MonsterSetup] URP field '{field}' not present in this version, skipped");
                    continue;
                }

                switch (value)
                {
                    case bool b:
                        property.boolValue = b;
                        break;
                    case int i:
                        property.intValue = i;
                        break;
                    case float f:
                        property.floatValue = f;
                        break;
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
