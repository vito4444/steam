using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Worker.Editor
{
    /// <summary>
    /// Creates and installs a 3D URP asset.
    ///
    /// The project was bootstrapped from Unity's 2D template, whose renderer is
    /// Renderer2D. That renderer has no directional lighting and no shadow casting, so
    /// an isometric view built on it would be flat no matter how the geometry is
    /// authored. Shadows and a single strong key light are most of what separates a
    /// readable management-game camera from a grid of coloured rectangles.
    ///
    /// Run from the menu, or from the command line with -executeMethod.
    /// </summary>
    public static class RenderPipelineSetup
    {
        private const string SettingsDirectory = "Assets/Settings";
        private const string RendererPath = SettingsDirectory + "/Renderer3D.asset";
        private const string PipelinePath = SettingsDirectory + "/UniversalRP3D.asset";

        [MenuItem("Worker/Rendering/Create 3D pipeline asset")]
        public static void CreateAndInstall()
        {
            Directory.CreateDirectory(SettingsDirectory);

            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rendererData, RendererPath);
                Debug.Log("[worker] created " + RendererPath);
            }

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
                Debug.Log("[worker] created " + PipelinePath);
            }

            ConfigureForIsometric(pipeline);
            EnsureAmbientOcclusion(rendererData);

            EditorUtility.SetDirty(pipeline);
            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssets();

            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;

            EnsureAlwaysIncludedShaders();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[worker] 3D pipeline installed as the default");
        }

        /// <summary>
        /// Adds screen space ambient occlusion to the renderer.
        ///
        /// A single directional light leaves every inside corner as flat as every open
        /// face, which is most of why untextured geometry reads as plastic. Contact
        /// darkening where a machine meets the floor, or where a roof meets a wall, is
        /// what makes the objects look like they are actually touching each other.
        /// </summary>
        private static void EnsureAmbientOcclusion(UniversalRendererData rendererData)
        {
            foreach (var feature in rendererData.rendererFeatures)
            {
                if (feature is ScreenSpaceAmbientOcclusion) return;
            }

            var ssao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
            ssao.name = "ScreenSpaceAmbientOcclusion";

            var settingsField = typeof(ScreenSpaceAmbientOcclusion)
                .GetField("m_Settings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (settingsField != null)
            {
                var settings = settingsField.GetValue(ssao);
                var type = settings.GetType();
                SetField(settings, type, "Intensity", 1.6f);
                SetField(settings, type, "Radius", 0.35f);
                SetField(settings, type, "Falloff", 60f);
                SetField(settings, type, "SampleCount", 8);
                settingsField.SetValue(ssao, settings);
            }

            AssetDatabase.AddObjectToAsset(ssao, rendererData);
            rendererData.rendererFeatures.Add(ssao);

            var serialized = new SerializedObject(rendererData);
            serialized.Update();
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(rendererData);
            Debug.Log("[worker] added screen space ambient occlusion");
        }

        private static void SetField(object target, System.Type type, string name, object value)
        {
            var field = type.GetField(name,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance);
            field?.SetValue(target, value);
        }

        /// <summary>
        /// Forces the shaders the runtime looks up by name into the build.
        ///
        /// All geometry is created at runtime with <c>Shader.Find</c>, so nothing in the
        /// project references these shaders as an asset and the build strips them. The
        /// symptom is brutal and non-obvious: the player runs, the interface draws, and
        /// the entire world renders black because every <c>new Material(null)</c> threw.
        /// </summary>
        private static void EnsureAlwaysIncludedShaders()
        {
            string[] required =
            {
                "Universal Render Pipeline/Lit",
                "Universal Render Pipeline/Unlit",
                "Universal Render Pipeline/Simple Lit"
            };

            var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (settings == null || settings.Length == 0)
            {
                Debug.LogWarning("[worker] could not open GraphicsSettings to register shaders");
                return;
            }

            var serialized = new SerializedObject(settings[0]);
            var included = serialized.FindProperty("m_AlwaysIncludedShaders");
            if (included == null)
            {
                Debug.LogWarning("[worker] GraphicsSettings has no m_AlwaysIncludedShaders array");
                return;
            }

            foreach (var name in required)
            {
                var shader = Shader.Find(name);
                if (shader == null)
                {
                    Debug.LogWarning("[worker] shader not found in project: " + name);
                    continue;
                }

                bool present = false;
                for (int i = 0; i < included.arraySize; i++)
                {
                    if (included.GetArrayElementAtIndex(i).objectReferenceValue != shader) continue;
                    present = true;
                    break;
                }

                if (present) continue;

                included.InsertArrayElementAtIndex(included.arraySize);
                included.GetArrayElementAtIndex(included.arraySize - 1).objectReferenceValue = shader;
                Debug.Log("[worker] always-include shader: " + name);
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Tuned for a fixed orthographic camera looking down at a small scene, and for
        /// a build machine with no GPU. A single cascade is enough when the whole world
        /// is inside a few dozen metres, and it keeps software rasterisation viable.
        /// </summary>
        private static void ConfigureForIsometric(UniversalRenderPipelineAsset pipeline)
        {
            // Must exceed the camera's pull-back distance. Shadow distance is measured
            // from the camera, and an orthographic rig sitting 50 units back with a 60
            // unit shadow distance culls the shadows of everything it is looking at,
            // which presents as a scene that is lit but casts nothing.
            pipeline.shadowDistance = 160f;
            pipeline.shadowCascadeCount = 1;
            pipeline.shadowDepthBias = 0.6f;
            pipeline.shadowNormalBias = 0.6f;
            pipeline.msaaSampleCount = 2;
            pipeline.supportsHDR = true;

            // Soft shadows are exposed read-only on the asset, so the flag has to be set
            // through the serialised object instead.
            var serialized = new SerializedObject(pipeline);
            var softShadows = serialized.FindProperty("m_SoftShadowsSupported");
            if (softShadows != null)
            {
                softShadows.boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }
}
