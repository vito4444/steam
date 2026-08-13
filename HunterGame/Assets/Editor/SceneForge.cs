using System.IO;
using System.Reflection;
using Hunter.Audio;
using Hunter.Gameplay.AI;
using Hunter.Gameplay.Actors;
using Hunter.Gameplay.Combat;
using Hunter.Gameplay.Run;
using Hunter.Gameplay.UI;
using Hunter.Worldgen;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Hunter.EditorTools
{
    /// Builds the render pipeline, the procedural materials and the Aurum Mist scene from
    /// scratch. Everything is regenerated rather than stored so the look is reproducible
    /// from source and reviewable as a diff.
    public static class SceneForge
    {
        const string SettingsDir = "Assets/Settings";
        const string MaterialsDir = "Assets/Materials";
        const string ScenePath = "Assets/Scenes/AurumMist.unity";
        const string UrpAssetPath = SettingsDir + "/HunterURP.asset";
        const string RendererPath = SettingsDir + "/HunterRenderer.asset";
        const string VolumeProfilePath = SettingsDir + "/HunterPostProcess.asset";

        [MenuItem("Hunter/Forge Everything")]
        public static void ForgeEverything()
        {
            EnsureDirectories();
            var urp = BuildPipeline();
            var palette = BuildMaterials();
            BuildScene(palette, urp);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("FORGE_OK");
        }

        static void EnsureDirectories()
        {
            foreach (var dir in new[] { SettingsDir, MaterialsDir, "Assets/Scenes", "Assets/Textures" })
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            }
            AssetDatabase.Refresh();
        }

        // ---------- pipeline ----------

        static UniversalRenderPipelineAsset BuildPipeline()
        {
            var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
            rendererData.name = "HunterRenderer";
            AssetDatabase.CreateAsset(rendererData, RendererPath);

            ConfigureRenderer(rendererData);
            AddRendererFeature(rendererData, "ScreenSpaceAmbientOcclusion", "SSAO", ConfigureSsao);
            AddVolumetricFeature(rendererData);

            EditorUtility.SetDirty(rendererData);

            var urp = UniversalRenderPipelineAsset.Create(rendererData);
            urp.name = "HunterURP";
            AssetDatabase.CreateAsset(urp, UrpAssetPath);

            ConfigureQuality(urp);

            GraphicsSettings.defaultRenderPipeline = urp;
            QualitySettings.renderPipeline = urp;
            EditorUtility.SetDirty(urp);
            AssetDatabase.SaveAssets();
            return urp;
        }

        static void ConfigureRenderer(UniversalRendererData data)
        {
            var so = new SerializedObject(data);
            SetInt(so, "m_DepthPrimingMode", 0);
            SetEnumByName(so, "m_RenderingMode", 0);            // Forward
            SetBool(so, "m_AccurateGbufferNormals", true);
            SetInt(so, "m_IntermediateTextureMode", 0);          // Always, keeps blit passes valid
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// URP's SSAO and its settings type are internal, so the feature is created and
        /// tuned through reflection rather than a direct reference.
        static void AddRendererFeature(UniversalRendererData data, string typeName, string featureName,
            System.Action<ScriptableRendererFeature> configure)
        {
            var type = FindUrpType(typeName);
            if (type == null)
            {
                Debug.LogWarning($"FORGE_WARN renderer feature type not found: {typeName}");
                return;
            }

            var feature = (ScriptableRendererFeature)ScriptableObject.CreateInstance(type);
            feature.name = featureName;
            configure?.Invoke(feature);

            AssetDatabase.AddObjectToAsset(feature, data);
            AttachFeature(data, feature);
        }

        static void AddVolumetricFeature(UniversalRendererData data)
        {
            var feature = ScriptableObject.CreateInstance<Hunter.Rendering.VolumetricFogFeature>();
            feature.name = "VolumetricFog";
            feature.settings.density = 0.062f;
            feature.settings.steps = 48;
            feature.settings.maxDistance = 120f;
            feature.settings.anisotropy = 0.72f;
            feature.settings.scatterColor = new Color(1f, 0.90f, 0.62f);
            feature.settings.intensity = 0.5f;
            feature.settings.heightBase = -1f;
            feature.settings.heightFalloff = 0.062f;
            feature.settings.noiseScale = 0.038f;
            feature.settings.noiseStrength = 0.62f;
            feature.settings.sunBoost = 1.25f;
            feature.settings.ambientFloor = 0.035f;
            feature.settings.pointLightGain = 0.22f;

            AssetDatabase.AddObjectToAsset(feature, data);
            AttachFeature(data, feature);
        }

        static void AttachFeature(UniversalRendererData data, ScriptableRendererFeature feature)
        {
            var so = new SerializedObject(data);
            var list = so.FindProperty("m_RendererFeatures");
            var maps = so.FindProperty("m_RendererFeatureMap");

            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = feature;

            if (maps != null)
            {
                maps.arraySize++;
                var id = GlobalObjectId.GetGlobalObjectIdSlow(feature).targetObjectId;
                maps.GetArrayElementAtIndex(maps.arraySize - 1).longValue = (long)id;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void ConfigureSsao(ScriptableRendererFeature feature)
        {
            var so = new SerializedObject(feature);
            var settings = so.FindProperty("m_Settings");
            if (settings == null) return;

            SetChildFloat(settings, "Intensity", 0.72f);
            SetChildFloat(settings, "Radius", 0.28f);
            SetChildFloat(settings, "DirectLightingStrength", 0.32f);
            SetChildFloat(settings, "Falloff", 100f);
            SetChildInt(settings, "SampleCount", 12);
            SetChildBool(settings, "Downsample", false);
            SetChildBool(settings, "AfterOpaque", false);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void ConfigureQuality(UniversalRenderPipelineAsset urp)
        {
            var so = new SerializedObject(urp);

            SetBool(so, "m_SupportsHDR", true);
            SetInt(so, "m_MSAA", 4);
            SetFloat(so, "m_RenderScale", 1f);

            SetBool(so, "m_RequireDepthTexture", true);   // volumetric pass needs it
            SetBool(so, "m_RequireOpaqueTexture", true);

            SetInt(so, "m_MainLightRenderingMode", 1);    // MainLightRenderingMode.PerPixel
            SetBool(so, "m_MainLightShadowsSupported", true);
            SetInt(so, "m_MainLightShadowmapResolution", 4096);

            // LightRenderingMode.PerPixel is 2; PerVertex (1) makes point lights vanish on
            // large welded meshes such as the pavement.
            SetInt(so, "m_AdditionalLightsRenderingMode", 2);
            SetInt(so, "m_AdditionalLightsPerObjectLimit", 8);
            SetBool(so, "m_AdditionalLightShadowsSupported", true);
            SetInt(so, "m_AdditionalLightsShadowmapResolution", 2048);

            SetFloat(so, "m_ShadowDistance", 95f);
            SetInt(so, "m_ShadowCascadeCount", 4);
            SetFloat(so, "m_Cascade4Split.x", 0.045f);
            SetFloat(so, "m_Cascade4Split.y", 0.14f);
            SetFloat(so, "m_Cascade4Split.z", 0.36f);
            SetFloat(so, "m_ShadowDepthBias", 0.6f);
            SetFloat(so, "m_ShadowNormalBias", 0.55f);
            SetBool(so, "m_SoftShadowsSupported", true);
            SetInt(so, "m_SoftShadowQuality", 2);         // high

            SetInt(so, "m_ColorGradingMode", 1);          // HDR grading
            SetInt(so, "m_ColorGradingLutSize", 64);

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------- materials ----------

        static RuinSiteGenerator.Palette BuildMaterials()
        {
            var stoneAlbedo = SaveTexture(
                ProceduralTextures.StoneAlbedo(512, new Color(0.34f, 0.335f, 0.325f),
                    new Color(0.86f, 0.835f, 0.78f), seed: 7, scale: 5f),
                "Assets/Textures/StoneAlbedo.asset");
            var stoneNormal = SaveTexture(
                ProceduralTextures.StoneNormal(512, seed: 7, strength: 3.1f, scale: 5f),
                "Assets/Textures/StoneNormal.asset");
            var stoneMask = SaveTexture(
                ProceduralTextures.StoneMask(512, seed: 7, baseSmooth: 0.42f, scale: 5f),
                "Assets/Textures/StoneMask.asset");

            var groundAlbedo = SaveTexture(
                ProceduralTextures.StoneAlbedo(512, new Color(0.30f, 0.30f, 0.305f),
                    new Color(0.80f, 0.78f, 0.73f), seed: 21, scale: 7f),
                "Assets/Textures/GroundAlbedo.asset");
            var groundNormal = SaveTexture(
                ProceduralTextures.StoneNormal(512, seed: 21, strength: 3.6f, scale: 7f),
                "Assets/Textures/GroundNormal.asset");
            var groundMask = SaveTexture(
                ProceduralTextures.StoneMask(512, seed: 21, baseSmooth: 0.66f, scale: 7f),
                "Assets/Textures/GroundMask.asset");

            var palette = new RuinSiteGenerator.Palette
            {
                Stone = MakeLit("Stone", new Color(0.88f, 0.86f, 0.82f), stoneAlbedo, stoneNormal, stoneMask,
                    normalScale: 1.45f, tiling: 0.9f),
                Ground = MakeLit("Ground", new Color(0.95f, 0.92f, 0.86f), groundAlbedo, groundNormal, groundMask,
                    normalScale: 1.7f, tiling: 1.15f),
                Gold = MakeGold(),
                Silhouette = MakeLit("Silhouette", new Color(0.30f, 0.30f, 0.33f), null, null, null,
                    smoothness: 0.12f),
                Cloth = MakeLit("Cloth", new Color(0.36f, 0.35f, 0.38f), stoneAlbedo, stoneNormal, null,
                    normalScale: 0.5f, smoothness: 0.19f, tiling: 2.2f),
                // Near-black and near-mirror. The colour comes almost entirely from what it
                // reflects, which is the point.
                Water = MakeLit("Water", new Color(0.035f, 0.042f, 0.048f), null, null, null,
                    smoothness: 0.96f),
                Timber = MakeLit("Timber", new Color(0.62f, 0.46f, 0.30f), stoneAlbedo, stoneNormal, null,
                    normalScale: 0.9f, smoothness: 0.22f, tiling: 2.6f),
                Banner = MakeLit("Banner", new Color(1.35f, 0.42f, 0.26f), stoneAlbedo, null, null,
                    smoothness: 0.30f, tiling: 1.8f),
            };

            return palette;
        }

        static Texture2D SaveTexture(Texture2D texture, string path)
        {
            AssetDatabase.CreateAsset(texture, path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material MakeLit(string name, Color baseColor, Texture2D albedo, Texture2D normal,
            Texture2D mask, float normalScale = 1f, float smoothness = 0.35f, float tiling = 1f)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            mat.SetColor("_BaseColor", baseColor);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", 0f);
            mat.SetTextureScale("_BaseMap", Vector2.one * tiling);

            if (albedo != null) mat.SetTexture("_BaseMap", albedo);
            if (normal != null)
            {
                mat.SetTexture("_BumpMap", normal);
                mat.SetFloat("_BumpScale", normalScale);
                mat.EnableKeyword("_NORMALMAP");
            }
            if (mask != null)
            {
                mat.SetTexture("_MetallicGlossMap", mask);
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                mat.SetFloat("_SmoothnessTextureChannel", 0f);
            }

            AssetDatabase.CreateAsset(mat, $"{MaterialsDir}/{name}.mat");
            return mat;
        }

        static Material MakeGold()
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Gold" };
            mat.SetColor("_BaseColor", new Color(0.78f, 0.55f, 0.19f));
            mat.SetFloat("_Metallic", 0.92f);
            mat.SetFloat("_Smoothness", 0.62f);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.SetColor("_EmissionColor", new Color(1f, 0.62f, 0.18f) * 0.28f);
            AssetDatabase.CreateAsset(mat, $"{MaterialsDir}/Gold.mat");
            return mat;
        }

        // ---------- scene ----------

        static void BuildScene(RuinSiteGenerator.Palette palette, UniversalRenderPipelineAsset urp)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            RenderSettings.fog = false;   // the volumetric pass owns atmospherics now

            // A real sky serves three jobs at once: it is the ambient source, it is what
            // the wet pavement reflects, and it fills the gaps between the ruins. A solid
            // clear colour gave the floor nothing to mirror, which is why the foreground
            // stayed featureless no matter how much light was added.
            var sky = new Material(Shader.Find("Skybox/Procedural")) { name = "AurumSky" };
            sky.SetFloat("_SunSize", 0.045f);
            sky.SetFloat("_SunSizeConvergence", 3f);
            sky.SetFloat("_AtmosphereThickness", 1.35f);
            sky.SetColor("_SkyTint", new Color(0.60f, 0.585f, 0.44f));
            sky.SetColor("_GroundColor", new Color(0.145f, 0.135f, 0.105f));
            sky.SetFloat("_Exposure", 0.46f);
            AssetDatabase.CreateAsset(sky, SettingsDir + "/AurumSky.mat");

            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1.15f;
            RenderSettings.reflectionIntensity = 1f;

            // Ambient only reaches the shaders through the environment SH probe, and in
            // batch mode that probe is not refreshed automatically.
            DynamicGI.UpdateEnvironment();

            var root = new GameObject("AurumMist").transform;
            var handles = new RuinSiteGenerator(20260813, palette).Generate(root);

            BuildLighting(root);
            BuildAtmosphere(root);
            BuildReflectionProbe(root);
            var camera = BuildCamera(root);
            BuildPostProcessing(root);
            BuildGameplay(root, handles, camera);

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        static void BuildLighting(Transform root)
        {
            // Low, raking key light aimed almost down the camera axis. Back-lighting is what
            // turns the colonnade into silhouettes and gives the volumetric pass its shafts.
            var sunGo = new GameObject("KeyLight");
            sunGo.transform.SetParent(root, false);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.945f, 0.775f);
            sun.intensity = 2.8f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.68f;
            sun.shadowBias = 0.04f;
            sun.shadowNormalBias = 0.35f;
            sunGo.transform.rotation = Quaternion.Euler(15f, 191f, 0f);
            RenderSettings.sun = sun;

            // Cool fill from camera-left keeps the shadow side from going pure black and
            // creates the blue/gold split the concept frame relies on.
            var fillGo = new GameObject("FillLight");
            fillGo.transform.SetParent(root, false);
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.60f, 0.62f, 0.72f);
            fill.intensity = 1.1f;
            fill.shadows = LightShadows.None;
            fillGo.transform.rotation = Quaternion.Euler(58f, 34f, 0f);

            // Warm bounce pooling in the ruin floor.
            var bounceGo = new GameObject("GroundBounce");
            bounceGo.transform.SetParent(root, false);
            bounceGo.transform.position = new Vector3(0f, 1.4f, 15f);
            var bounce = bounceGo.AddComponent<Light>();
            bounce.type = LightType.Point;
            bounce.color = new Color(1f, 0.86f, 0.62f);
            bounce.intensity = 6.5f;
            bounce.range = 38f;
            bounce.shadows = LightShadows.None;

            // Dedicated rim light for the hunter. The key is occluded by the frame masses at
            // this camera distance, so the subject would otherwise be a flat black cutout.
            var rimGo = new GameObject("HeroRim");
            rimGo.transform.SetParent(root, false);
            rimGo.transform.position = new Vector3(0.85f, 2.15f, 2.9f);
            var rim = rimGo.AddComponent<Light>();
            rim.type = LightType.Point;
            rim.color = new Color(1f, 0.82f, 0.52f);
            rim.intensity = 7.5f;
            rim.range = 7.5f;
            rim.shadows = LightShadows.None;

            // Foreground fill. The frame masses were rendering as flat black shapes with no
            // surface detail; a weak cool light behind the camera recovers their texture
            // without lifting the midground.
            var foreGo = new GameObject("ForegroundFill");
            foreGo.transform.SetParent(root, false);
            foreGo.transform.position = new Vector3(0.4f, 3.2f, -4.2f);
            var fore = foreGo.AddComponent<Light>();
            fore.type = LightType.Point;
            fore.color = new Color(0.62f, 0.66f, 0.80f);
            fore.intensity = 11f;
            fore.range = 24f;
            fore.shadows = LightShadows.None;

            // Distant glow behind the tower, reading as the source of the gold mist.
            var mistCoreGo = new GameObject("MistCore");
            mistCoreGo.transform.SetParent(root, false);
            mistCoreGo.transform.position = new Vector3(1f, 13f, 71f);
            var mistCore = mistCoreGo.AddComponent<Light>();
            mistCore.type = LightType.Point;
            mistCore.color = new Color(1f, 0.90f, 0.66f);
            mistCore.intensity = 7f;
            mistCore.range = 70f;
            mistCore.shadows = LightShadows.None;
        }

        /// Airborne dust. A volumetric shaft with nothing floating in it reads as a flat
        /// gradient; motes crossing the beam are what tell the eye the light has substance
        /// and that the air itself is thick.
        static void BuildAtmosphere(Transform root)
        {
            var dustTexture = ProceduralTextures.RadialGlow(64, power: 2.2f);
            AssetDatabase.CreateAsset(dustTexture, "Assets/Textures/DustMote.asset");

            var material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"))
            {
                name = "DustMote",
            };
            material.SetTexture("_BaseMap", dustTexture);
            material.SetColor("_BaseColor", new Color(1f, 0.86f, 0.58f, 0.55f));
            material.SetFloat("_Surface", 1f);          // transparent
            material.SetFloat("_Blend", 1f);            // additive
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_ZWrite", 0f);
            material.renderQueue = 3000;
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            AssetDatabase.CreateAsset(material, MaterialsDir + "/DustMote.mat");

            var go = new GameObject("AirborneDust");
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(0f, 4.5f, 20f);

            var particles = go.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.duration = 20f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(9f, 20f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.14f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.006f, 0.022f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.88f, 0.62f, 0.16f), new Color(1f, 0.80f, 0.48f, 0.42f));
            main.maxParticles = 2600;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.006f;   // drifting down, not falling

            var emission = particles.emission;
            emission.rateOverTime = 190f;

            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(22f, 11f, 46f);

            // Slow turbulence keeps the motes from marching in parallel lines.
            var noise = particles.noise;
            noise.enabled = true;
            noise.strength = 0.16f;
            noise.frequency = 0.22f;
            noise.scrollSpeed = 0.09f;
            noise.damping = true;

            var colorOverLifetime = particles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.18f),
                    new GradientAlphaKey(1f, 0.78f), new GradientAlphaKey(0f, 1f),
                });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingFudge = -8f;
        }

        static void BuildReflectionProbe(Transform root)
        {
            var probeGo = new GameObject("SceneReflection");
            probeGo.transform.SetParent(root, false);
            probeGo.transform.position = new Vector3(0f, 4f, 14f);

            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
            probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.resolution = 256;
            probe.hdr = true;
            probe.boxProjection = true;
            probe.size = new Vector3(46f, 26f, 80f);
            probe.nearClipPlane = 0.3f;
            probe.farClipPlane = 160f;
            probe.intensity = 1f;
            probe.RenderProbe();
        }

        static OverShoulderCamera BuildCamera(Transform root)
        {
            var camGo = new GameObject("MainCamera");
            camGo.transform.SetParent(root, false);
            camGo.tag = "MainCamera";

            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.08f;
            cam.farClipPlane = 300f;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.allowHDR = true;
            cam.allowMSAA = true;

            // Over-the-shoulder rig from docs/01-game-concepts.md section A.3.
            camGo.transform.position = new Vector3(0.46f, 1.72f, -1.85f);
            camGo.transform.rotation = Quaternion.Euler(3.2f, 3.5f, 0f);

            var extra = camGo.AddComponent<UniversalAdditionalCameraData>();
            extra.renderPostProcessing = true;
            extra.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            extra.antialiasingQuality = AntialiasingQuality.High;
            extra.renderShadows = true;

            return camGo.AddComponent<OverShoulderCamera>();
        }

        /// Attaches behaviour to the generated site. Everything the raid loop needs is
        /// wired here rather than hand-placed in a scene file, so the whole playable state
        /// is reproducible from source and reviewable as a diff.
        static void BuildGameplay(Transform root, RuinSiteGenerator.SiteHandles handles,
            OverShoulderCamera camera)
        {
            var gameplayRoot = new GameObject("Gameplay");
            gameplayRoot.transform.SetParent(root, false);

            var player = BuildPlayerRig(handles.Hunter, camera);
            var bell = handles.BellTower.gameObject.AddComponent<BellTower>();

            var sovereignGo = new GameObject("MistSovereign");
            sovereignGo.transform.SetParent(gameplayRoot.transform, false);
            sovereignGo.transform.position = new Vector3(0f, 0f, 52f);
            var sovereignLight = new GameObject("SovereignAura");
            sovereignLight.transform.SetParent(sovereignGo.transform, false);
            var aura = sovereignLight.AddComponent<Light>();
            aura.type = LightType.Point;
            aura.color = new Color(1f, 0.34f, 0.14f);
            aura.intensity = 5f;
            aura.range = 22f;
            aura.shadows = LightShadows.None;
            var sovereign = sovereignGo.AddComponent<MistSovereign>();

            var runGo = new GameObject("RunController");
            runGo.transform.SetParent(gameplayRoot.transform, false);
            var run = runGo.AddComponent<RunController>();

            var runSo = new SerializedObject(run);
            runSo.FindProperty("player").objectReferenceValue = player.GetComponent<HunterController>();
            runSo.FindProperty("playerHealth").objectReferenceValue = player.GetComponent<Damageable>();
            runSo.FindProperty("bell").objectReferenceValue = bell;
            runSo.FindProperty("extractionPoint").objectReferenceValue = handles.BellTower;
            runSo.ApplyModifiedPropertiesWithoutUndo();

            foreach (var cache in handles.LootCaches)
            {
                var lootable = cache.gameObject.AddComponent<Lootable>();
                var so = new SerializedObject(lootable);
                // The deepest cache is the one worth the walk.
                bool elite = cache.position.z > 30f;
                so.FindProperty("tier").enumValueIndex = elite ? 1 : 0;
                so.FindProperty("minItems").intValue = elite ? 2 : 1;
                so.FindProperty("maxItems").intValue = elite ? 4 : 3;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // Rivals get different aggression values so a raid does not feel like it is
            // populated by one mind in three bodies.
            float[] aggressions = { 0.72f, 0.34f, 0.55f };
            for (int i = 0; i < handles.Rivals.Count; i++)
            {
                var rival = handles.Rivals[i];
                var health = rival.gameObject.AddComponent<Damageable>();
                var agent = rival.gameObject.AddComponent<RivalHunterAgent>();

                var collider = rival.gameObject.AddComponent<CapsuleCollider>();
                collider.height = 1.8f;
                collider.radius = 0.4f;
                collider.center = new Vector3(0f, 0.9f, 0f);

                rival.gameObject.AddComponent<MeleeCombatant>();

                var so = new SerializedObject(agent);
                so.FindProperty("aggression").floatValue = aggressions[i % aggressions.Length];
                so.ApplyModifiedPropertiesWithoutUndo();

                agent.Bind(run, player.transform, sovereign, bell, handles.PatrolPoints,
                    aggressions[i % aggressions.Length]);
            }

            sovereign.Bind(run, player.transform);

            var hudGo = new GameObject("RaidHud");
            hudGo.transform.SetParent(gameplayRoot.transform, false);
            var hud = hudGo.AddComponent<RaidHud>();
            var hudSo = new SerializedObject(hud);
            hudSo.FindProperty("run").objectReferenceValue = run;
            hudSo.FindProperty("hudCamera").objectReferenceValue = camera.GetComponent<Camera>();
            hudSo.FindProperty("playerHealth").objectReferenceValue = player.GetComponent<Damageable>();
            hudSo.ApplyModifiedPropertiesWithoutUndo();

            var audioGo = new GameObject("RaidAudio");
            audioGo.transform.SetParent(gameplayRoot.transform, false);
            var raidAudio = audioGo.AddComponent<RaidAudio>();
            var audioSo = new SerializedObject(raidAudio);
            audioSo.FindProperty("run").objectReferenceValue = run;
            audioSo.FindProperty("listenerTarget").objectReferenceValue = camera.transform;
            audioSo.FindProperty("player").objectReferenceValue = player.GetComponent<HunterController>();
            audioSo.ApplyModifiedPropertiesWithoutUndo();

            var bootstrapGo = new GameObject("RunBootstrap");
            bootstrapGo.transform.SetParent(gameplayRoot.transform, false);
            var bootstrap = bootstrapGo.AddComponent<RunBootstrap>();
            var bootSo = new SerializedObject(bootstrap);
            bootSo.FindProperty("run").objectReferenceValue = run;
            bootSo.FindProperty("playerCamera").objectReferenceValue = camera;
            bootSo.FindProperty("player").objectReferenceValue = player.GetComponent<HunterController>();
            bootSo.FindProperty("combat").objectReferenceValue = player.GetComponent<MeleeCombatant>();
            bootSo.FindProperty("hud").objectReferenceValue = hud;
            bootSo.ApplyModifiedPropertiesWithoutUndo();
        }

        static GameObject BuildPlayerRig(Transform hunterAnchor, OverShoulderCamera camera)
        {
            var go = hunterAnchor.gameObject;

            var controller = go.AddComponent<CharacterController>();
            controller.height = 1.75f;
            controller.radius = 0.32f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.slopeLimit = 50f;
            controller.stepOffset = 0.4f;

            go.AddComponent<Damageable>();
            var locomotion = go.AddComponent<HunterController>();
            var combat = go.AddComponent<MeleeCombatant>();
            combat.BindCamera(camera);

            // Procedural walk cycle. The hunter's mesh and lantern are separate children
            // of the anchor, which is exactly the split the animator needs.
            var animator = go.AddComponent<ProceduralHunterAnimator>();
            var animSo = new SerializedObject(animator);
            animSo.FindProperty("body").objectReferenceValue = go.transform.Find("Hunter");
            animSo.FindProperty("lantern").objectReferenceValue = go.transform.Find("Lantern");
            animSo.FindProperty("locomotion").objectReferenceValue = locomotion;
            animSo.FindProperty("combat").objectReferenceValue = combat;
            animSo.ApplyModifiedPropertiesWithoutUndo();

            camera.Target = go.transform;
            camera.SnapToTarget();

            return go;
        }

        static void BuildPostProcessing(Transform root)
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, VolumeProfilePath);

            var tonemap = profile.Add<Tonemapping>(true);
            tonemap.mode.overrideState = true;
            tonemap.mode.value = TonemappingMode.Neutral;

            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.overrideState = true; bloom.threshold.value = 1.25f;
            bloom.intensity.overrideState = true; bloom.intensity.value = 0.62f;
            bloom.scatter.overrideState = true; bloom.scatter.value = 0.72f;
            bloom.tint.overrideState = true; bloom.tint.value = new Color(1f, 0.86f, 0.62f);
            bloom.highQualityFiltering.overrideState = true; bloom.highQualityFiltering.value = true;

            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.overrideState = true; color.postExposure.value = -0.05f;
            color.contrast.overrideState = true; color.contrast.value = 26f;
            color.saturation.overrideState = true; color.saturation.value = -14f;
            color.colorFilter.overrideState = true; color.colorFilter.value = new Color(1f, 0.96f, 0.88f);

            // Gold highlights against cool shadows: the palette rule from the concept doc.
            var split = profile.Add<SplitToning>(true);
            split.shadows.overrideState = true; split.shadows.value = new Color(0.24f, 0.34f, 0.52f);
            split.highlights.overrideState = true; split.highlights.value = new Color(1f, 0.87f, 0.55f);
            split.balance.overrideState = true; split.balance.value = 6f;

            var smh = profile.Add<ShadowsMidtonesHighlights>(true);
            smh.shadows.overrideState = true; smh.shadows.value = new Vector4(0.92f, 0.96f, 1.08f, -0.02f);
            smh.highlights.overrideState = true; smh.highlights.value = new Vector4(1.06f, 0.98f, 0.86f, 0.02f);

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.overrideState = true; vignette.intensity.value = 0.26f;
            vignette.smoothness.overrideState = true; vignette.smoothness.value = 0.42f;
            vignette.color.overrideState = true; vignette.color.value = new Color(0.02f, 0.015f, 0.01f);

            var grain = profile.Add<FilmGrain>(true);
            grain.type.overrideState = true; grain.type.value = FilmGrainLookup.Medium1;
            grain.intensity.overrideState = true; grain.intensity.value = 0.24f;
            grain.response.overrideState = true; grain.response.value = 0.72f;

            var ca = profile.Add<ChromaticAberration>(true);
            ca.intensity.overrideState = true; ca.intensity.value = 0.11f;

            var dof = profile.Add<DepthOfField>(true);
            dof.mode.overrideState = true; dof.mode.value = DepthOfFieldMode.Bokeh;
            dof.focusDistance.overrideState = true; dof.focusDistance.value = 6.5f;
            dof.aperture.overrideState = true; dof.aperture.value = 8.5f;
            dof.focalLength.overrideState = true; dof.focalLength.value = 42f;

            var volumeGo = new GameObject("PostProcessVolume");
            volumeGo.transform.SetParent(root, false);
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1f;
            volume.sharedProfile = profile;

            EditorUtility.SetDirty(profile);
        }

        // ---------- serialized-property helpers ----------

        static System.Type FindUrpType(string name)
        {
            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.GetName().Name.Contains("Universal")) continue;
                foreach (var type in assembly.GetTypes())
                {
                    if (type.Name == name) return type;
                }
            }
            return null;
        }

        static void SetBool(SerializedObject so, string path, bool value)
        {
            var p = so.FindProperty(path);
            if (p != null) p.boolValue = value;
            else Debug.LogWarning($"FORGE_WARN missing property {path}");
        }

        static void SetInt(SerializedObject so, string path, int value)
        {
            var p = so.FindProperty(path);
            if (p != null) p.intValue = value;
            else Debug.LogWarning($"FORGE_WARN missing property {path}");
        }

        static void SetEnumByName(SerializedObject so, string path, int value)
        {
            var p = so.FindProperty(path);
            if (p != null) p.enumValueIndex = value;
        }

        static void SetFloat(SerializedObject so, string path, float value)
        {
            var p = so.FindProperty(path);
            if (p != null) p.floatValue = value;
            else Debug.LogWarning($"FORGE_WARN missing property {path}");
        }

        static void SetChildFloat(SerializedProperty parent, string name, float value)
        {
            var p = parent.FindPropertyRelative(name);
            if (p != null) p.floatValue = value;
        }

        static void SetChildInt(SerializedProperty parent, string name, int value)
        {
            var p = parent.FindPropertyRelative(name);
            if (p == null) return;
            if (p.propertyType == SerializedPropertyType.Enum) p.enumValueIndex = value;
            else p.intValue = value;
        }

        static void SetChildBool(SerializedProperty parent, string name, bool value)
        {
            var p = parent.FindPropertyRelative(name);
            if (p != null) p.boolValue = value;
        }
    }
}
