using System.IO;
using System.Linq;
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

            // A renderer created in code has a null postProcessData, and URP then silently
            // skips the entire post-processing stack. The editor's own asset-creation menu
            // wires this up; ScriptableObject.CreateInstance does not. Written through
            // SerializedObject so the field's accessibility cannot break this across URP
            // versions.
            const string postProcessDataPath =
                "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset";
            var postProcessData = AssetDatabase.LoadAssetAtPath<Object>(postProcessDataPath);
            if (postProcessData == null)
            {
                Debug.LogError($"[MonsterSetup] PostProcessData not found at {postProcessDataPath}; " +
                               "post-processing will not run");
            }
            else
            {
                var serializedRenderer = new SerializedObject(rendererData);
                var property = serializedRenderer.FindProperty("postProcessData");
                if (property == null)
                {
                    Debug.LogError("[MonsterSetup] UniversalRendererData has no postProcessData field");
                }
                else
                {
                    property.objectReferenceValue = postProcessData;
                    serializedRenderer.ApplyModifiedPropertiesWithoutUndo();
                    Debug.Log("[MonsterSetup] postProcessData assigned to the renderer");
                }
            }

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

            // Recreated from scratch, for the same reason the pipeline asset is.
            AssetDatabase.DeleteAsset(VolumeProfilePath);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, VolumeProfilePath);

            // VolumeProfile.Add creates the component but does not attach it to the profile's
            // asset file. Without AddObjectToAsset the references dangle the moment the asset
            // is serialised and every override is silently dropped -- which is exactly what
            // happened here: the profile shipped with zero components and none of the grading
            // was ever applied. Anything added must go through this helper.
            T Add<T>() where T : VolumeComponent
            {
                var component = profile.Add<T>(true);
                component.hideFlags = HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(component, profile);
                return component;
            }

            var tonemapping = Add<Tonemapping>();
            tonemapping.mode.Override(TonemappingMode.Neutral);

            var colorAdjustments = Add<ColorAdjustments>();
            colorAdjustments.postExposure.Override(-0.62f);
            colorAdjustments.contrast.Override(9f);
            colorAdjustments.saturation.Override(-9f);
            colorAdjustments.colorFilter.Override(new Color(1.0f, 0.97f, 0.93f));

            var whiteBalance = Add<WhiteBalance>();
            whiteBalance.temperature.Override(-5f);
            whiteBalance.tint.Override(-6f);

            // Bloom is kept tight. At the first pass's settings the CRT faces bloomed into
            // flat white blobs and took the rest of the frame's contrast with them.
            var shadows = Add<ShadowsMidtonesHighlights>();
            shadows.shadows.Override(new Vector4(1.06f, 1.05f, 1.10f, 0.035f));
            shadows.midtones.Override(new Vector4(1.0f, 1.0f, 1.0f, 0f));
            shadows.highlights.Override(new Vector4(1.0f, 0.99f, 0.97f, -0.02f));

            var bloom = Add<Bloom>();
            bloom.threshold.Override(1.15f);
            bloom.intensity.Override(0.34f);
            bloom.scatter.Override(0.55f);
            bloom.tint.Override(new Color(1f, 0.90f, 0.76f));

            var vignette = Add<Vignette>();
            vignette.intensity.Override(0.46f);
            vignette.smoothness.Override(0.52f);
            vignette.color.Override(new Color(0.015f, 0.015f, 0.022f));

            var grain = Add<FilmGrain>();
            grain.type.Override(FilmGrainLookup.Medium1);
            grain.intensity.Override(0.42f);
            grain.response.Override(0.80f);

            var aberration = Add<ChromaticAberration>();
            aberration.intensity.Override(0.09f);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(VolumeProfilePath, ImportAssetOptions.ForceUpdate);

            var written = AssetDatabase.LoadAllAssetsAtPath(VolumeProfilePath)
                .OfType<VolumeComponent>().Count();
            if (written != profile.components.Count)
            {
                Debug.LogError($"[MonsterSetup] post-process profile saved {written} of " +
                               $"{profile.components.Count} components; grading will not apply");
            }
            else
            {
                Debug.Log($"[MonsterSetup] post-process profile written with {written} components");
            }

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
