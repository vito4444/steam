using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// Verification harness for the headless Linux pipeline:
// URP rendering -> offscreen capture -> Windows x64 player build.
public static class Probe
{
    const string ScenePath = "Assets/Scenes/Probe.unity";
    const string UrpAssetPath = "Assets/Settings/ProbeURP.asset";
    const string RendererPath = "Assets/Settings/ProbeRenderer.asset";

    [MenuItem("Probe/Setup")]
    public static void Setup()
    {
        var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
        AssetDatabase.CreateAsset(renderer, RendererPath);

        var urp = UniversalRenderPipelineAsset.Create(renderer);
        AssetDatabase.CreateAsset(urp, UrpAssetPath);
        AssetDatabase.SaveAssets();

        GraphicsSettings.defaultRenderPipeline = urp;
        QualitySettings.renderPipeline = urp;

        BuildScene();
        AssetDatabase.SaveAssets();
        Debug.Log("PROBE_SETUP_OK");
    }

    static void BuildScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Fog is the art direction and the draw-distance budget at the same time,
        // so the probe scene has to render it rather than an empty grey box.
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.028f;
        RenderSettings.fogColor = new Color(0.30f, 0.25f, 0.14f);
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.20f, 0.18f, 0.13f);
        RenderSettings.ambientEquatorColor = new Color(0.13f, 0.13f, 0.14f);
        RenderSettings.ambientGroundColor = new Color(0.06f, 0.06f, 0.07f);

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(12f, 1f, 12f);
        // Wet stone: smoothness lets the lantern smear a highlight across the floor,
        // which is what carries the mood in the concept frame.
        ground.GetComponent<Renderer>().sharedMaterial =
            MakeMaterial(new Color(0.13f, 0.14f, 0.15f), smoothness: 0.55f);

        var stone = MakeMaterial(new Color(0.24f, 0.25f, 0.26f), smoothness: 0.20f);
        var gold = MakeMaterial(new Color(0.95f, 0.72f, 0.22f),
            emission: new Color(1.0f, 0.68f, 0.14f) * 3.5f, smoothness: 0.75f);

        // Two rows of pillars receding into the fog: the clearest readable test of
        // whether the fog curve actually kills the far plane the way the concept art does.
        for (int i = 0; i < 10; i++)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                var pillar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pillar.name = $"Pillar_{i}_{side}";
                pillar.transform.position = new Vector3(side * 4.5f, 3f, 6f + i * 7f);
                pillar.transform.localScale = new Vector3(1.6f, 6f, 1.6f);
                pillar.GetComponent<Renderer>().sharedMaterial = stone;
            }
        }

        var chest = GameObject.CreatePrimitive(PrimitiveType.Cube);
        chest.name = "Loot";
        chest.transform.position = new Vector3(2.6f, 0.6f, 9.5f);
        chest.transform.localScale = new Vector3(1.4f, 1.2f, 1.0f);
        chest.GetComponent<Renderer>().sharedMaterial = gold;

        var lootGlowGo = new GameObject("LootGlow");
        lootGlowGo.transform.position = new Vector3(2.6f, 1.0f, 9.5f);
        var lootGlow = lootGlowGo.AddComponent<Light>();
        lootGlow.type = LightType.Point;
        lootGlow.color = new Color(1f, 0.72f, 0.25f);
        lootGlow.intensity = 4f;
        lootGlow.range = 9f;

        // Rival hunters reading as pure silhouettes in the fog is the single most
        // important readability property of the art direction, so the probe tests it.
        var silhouette = MakeMaterial(new Color(0.04f, 0.04f, 0.05f), smoothness: 0.05f);
        var rivalPositions = new[]
        {
            new Vector3(-2.6f, 1f, 17f),
            new Vector3(-3.4f, 1f, 20f),
            new Vector3(2.8f, 1f, 25f)
        };
        for (int i = 0; i < rivalPositions.Length; i++)
        {
            var rival = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            rival.name = $"RivalHunter_{i}";
            rival.transform.position = rivalPositions[i];
            rival.GetComponent<Renderer>().sharedMaterial = silhouette;
        }

        var hunter = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        hunter.name = "Hunter";
        hunter.transform.position = new Vector3(-0.55f, 1f, 0.6f);
        hunter.GetComponent<Renderer>().sharedMaterial =
            MakeMaterial(new Color(0.12f, 0.12f, 0.14f), smoothness: 0.12f);

        // Lantern sits at hand height ahead of the hunter so its pool of light reads as
        // carried, not as an unexplained glow on the floor.
        var lanternPos = new Vector3(0.35f, 1.15f, 1.9f);
        var lanternGo = new GameObject("Lantern");
        lanternGo.transform.position = lanternPos;
        var lantern = lanternGo.AddComponent<Light>();
        lantern.type = LightType.Point;
        lantern.color = new Color(1f, 0.76f, 0.40f);
        lantern.intensity = 26f;
        lantern.range = 34f;

        var lanternBulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        lanternBulb.name = "LanternBulb";
        lanternBulb.transform.position = lanternPos;
        lanternBulb.transform.localScale = Vector3.one * 0.18f;
        lanternBulb.GetComponent<Renderer>().sharedMaterial =
            MakeMaterial(new Color(1f, 0.85f, 0.55f), emission: new Color(1f, 0.8f, 0.45f) * 6f);

        var sunGo = new GameObject("Sun");
        var sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(0.62f, 0.58f, 0.44f);
        sun.intensity = 0.75f;
        sunGo.transform.rotation = Quaternion.Euler(28f, 155f, 0f);

        // Backlight is what separates the player and the pillars from the fog plane.
        var rimGo = new GameObject("RimLight");
        var rim = rimGo.AddComponent<Light>();
        rim.type = LightType.Directional;
        rim.color = new Color(0.85f, 0.70f, 0.35f);
        rim.intensity = 1.1f;
        rimGo.transform.rotation = Quaternion.Euler(12f, 8f, 0f);

        // Camera matches the over-the-shoulder rig documented in docs/01-game-concepts.md:
        // 2.8m back, 0.4m above chest, 0.55m right shoulder offset, 60 degree FOV.
        var camGo = new GameObject("ProbeCamera");
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 60f;
        cam.backgroundColor = new Color(0.26f, 0.22f, 0.13f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        camGo.transform.position = new Vector3(0.55f, 2.25f, -3.9f);
        camGo.transform.rotation = Quaternion.Euler(7f, 0f, 0f);
        camGo.tag = "MainCamera";

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
    }

    static Material MakeMaterial(Color color, Color? emission = null, float smoothness = 0.3f)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        var mat = new Material(shader);
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", smoothness);
        if (emission.HasValue)
        {
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.SetColor("_EmissionColor", emission.Value);
        }
        return mat;
    }

    [MenuItem("Probe/Capture")]
    public static void Capture()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var cam = Object.FindFirstObjectByType<Camera>();
        if (cam == null)
        {
            Debug.LogError("PROBE_CAPTURE_FAIL no camera");
            EditorApplication.Exit(1);
            return;
        }

        const int width = 1920;
        const int height = 1080;
        var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        rt.antiAliasing = 1;
        cam.targetTexture = rt;
        cam.Render();

        RenderTexture.active = rt;
        var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        cam.targetTexture = null;

        var outDir = System.Environment.GetEnvironmentVariable("PROBE_OUT") ?? "/tmp/probe-out";
        var tag = System.Environment.GetEnvironmentVariable("PROBE_TAG") ?? "probe-urp-fog";
        Directory.CreateDirectory(outDir);
        var path = Path.Combine(outDir, tag + ".png");
        File.WriteAllBytes(path, tex.EncodeToPNG());

        // Non-black pixel ratio guards against the classic headless failure where the
        // capture "succeeds" but the software renderer produced an empty frame.
        var pixels = tex.GetPixels32();
        int lit = 0;
        foreach (var p in pixels)
        {
            if (p.r > 12 || p.g > 12 || p.b > 12) lit++;
        }
        float ratio = (float)lit / pixels.Length;

        Debug.Log($"PROBE_CAPTURE_OK path={path} bytes={new FileInfo(path).Length} litRatio={ratio:F4}");
        if (ratio < 0.05f)
        {
            Debug.LogError($"PROBE_CAPTURE_BLANK litRatio={ratio:F4}");
            EditorApplication.Exit(2);
        }
    }

    [MenuItem("Probe/BuildWindows")]
    public static void BuildWindows()
    {
        var outDir = System.Environment.GetEnvironmentVariable("PROBE_BUILD") ?? "/tmp/probe-build";
        Directory.CreateDirectory(outDir);

        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = Path.Combine(outDir, "HunterProbe.exe"),
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.None
        };

        var report = BuildPipeline.BuildPlayer(options);
        var summary = report.summary;
        Debug.Log($"PROBE_BUILD result={summary.result} size={summary.totalSize} errors={summary.totalErrors} time={summary.totalTime}");
        if (summary.result != BuildResult.Succeeded)
        {
            Debug.LogError("PROBE_BUILD_FAIL");
            EditorApplication.Exit(3);
        }
    }
}
