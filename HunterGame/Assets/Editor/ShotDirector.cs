using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hunter.EditorTools
{
    /// Renders the fixed comparison angles used to measure progress against the concept
    /// art. The angles never change, so screenshots stay comparable across revisions.
    public static class ShotDirector
    {
        const string ScenePath = "Assets/Scenes/AurumMist.unity";

        struct Shot
        {
            public string Name;
            public Vector3 Position;
            public Vector3 Euler;
            public float Fov;
        }

        static readonly Shot[] Shots =
        {
            new() { Name = "hero-over-shoulder", Position = new Vector3(0.46f, 1.72f, -1.85f), Euler = new Vector3(3.2f, 3.5f, 0f), Fov = 60f },
            new() { Name = "colonnade-depth",    Position = new Vector3(-1.2f, 2.6f, 5.5f),   Euler = new Vector3(4f, 6f, 0f),    Fov = 55f },
            new() { Name = "loot-approach",      Position = new Vector3(1.4f, 1.55f, 5.4f),   Euler = new Vector3(3f, 24f, 0f),   Fov = 58f },
        };

        [MenuItem("Hunter/Capture Shots")]
        public static void CaptureShots()
        {
            var outDir = Environment.GetEnvironmentVariable("HUNTER_SHOTS") ?? "/tmp/hunter-shots";
            var tag = Environment.GetEnvironmentVariable("HUNTER_TAG") ?? "shot";
            int width = ParseInt("HUNTER_WIDTH", 1920);
            int height = ParseInt("HUNTER_HEIGHT", 1080);
            int supersample = Mathf.Clamp(ParseInt("HUNTER_SS", 1), 1, 3);
            string only = Environment.GetEnvironmentVariable("HUNTER_SHOT_ONLY");

            Directory.CreateDirectory(outDir);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var camGo = GameObject.FindWithTag("MainCamera");
            if (camGo == null)
            {
                Debug.LogError("SHOT_FAIL no MainCamera in scene");
                EditorApplication.Exit(1);
                return;
            }
            var cam = camGo.GetComponent<Camera>();

            foreach (var particles in UnityEngine.Object.FindObjectsByType<ParticleSystem>(
                         FindObjectsSortMode.None))
            {
                // A freshly loaded system has emitted nothing, so a single rendered frame
                // would show an empty volume.
                particles.Simulate(14f, withChildren: true, restart: true);
            }

            var animator = UnityEngine.Object.FindFirstObjectByType<
                Hunter.Gameplay.Actors.ProceduralHunterAnimator>();
            if (animator != null)
            {
                animator.PoseForCapture(phase: 2.1f, speed01: 0.85f);
                Debug.Log("SHOT_MODE hunter posed mid-stride");
            }

            if (Environment.GetEnvironmentVariable("HUNTER_NO_HUD") != "1")
            {
                var hud = UnityEngine.Object.FindFirstObjectByType<Hunter.Gameplay.UI.RaidHud>();
                if (hud != null)
                {
                    hud.PopulateForCapture();
                    Canvas.ForceUpdateCanvases();
                    Debug.Log("SHOT_MODE hud populated");
                }
            }

            // Control renders for isolating which stage darkened a frame.
            if (Environment.GetEnvironmentVariable("HUNTER_NO_POST") == "1")
            {
                var extra = camGo.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                if (extra != null) extra.renderPostProcessing = false;
                Debug.Log("SHOT_MODE post-processing disabled");
            }
            if (Environment.GetEnvironmentVariable("HUNTER_NO_FOG") == "1")
            {
                SetFeatureActive("VolumetricFog", false);
                Debug.Log("SHOT_MODE volumetric fog disabled");
            }
            if (Environment.GetEnvironmentVariable("HUNTER_NO_SSAO") == "1")
            {
                SetFeatureActive("SSAO", false);
                Debug.Log("SHOT_MODE ssao disabled");
            }

            var originalPos = camGo.transform.position;
            var originalRot = camGo.transform.rotation;
            var originalFov = cam.fieldOfView;

            bool anyFailed = false;

            foreach (var shot in Shots)
            {
                if (!string.IsNullOrEmpty(only) && shot.Name != only) continue;

                camGo.transform.position = shot.Position;
                camGo.transform.rotation = Quaternion.Euler(shot.Euler);
                cam.fieldOfView = shot.Fov;

                var path = Path.Combine(outDir, $"{tag}_{shot.Name}.png");
                if (!Render(cam, width, height, supersample, path)) anyFailed = true;
            }

            camGo.transform.position = originalPos;
            camGo.transform.rotation = originalRot;
            cam.fieldOfView = originalFov;

            Debug.Log(anyFailed ? "SHOT_PARTIAL" : "SHOT_ALL_OK");
            if (anyFailed) EditorApplication.Exit(2);
        }

        static bool Render(Camera cam, int width, int height, int supersample, string path)
        {
            int rw = width * supersample;
            int rh = height * supersample;

            var rt = new RenderTexture(rw, rh, 32, RenderTextureFormat.DefaultHDR)
            {
                antiAliasing = 1,
                useMipMap = false,
            };
            rt.Create();

            var previousTarget = cam.targetTexture;
            cam.targetTexture = rt;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            cam.Render();
            sw.Stop();

            var full = new Texture2D(rw, rh, TextureFormat.RGBA32, false, false);
            RenderTexture.active = rt;
            full.ReadPixels(new Rect(0, 0, rw, rh), 0, 0);
            full.Apply();
            RenderTexture.active = null;

            cam.targetTexture = previousTarget;
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);

            Texture2D output = full;
            if (supersample > 1)
            {
                // Box-filter down from the supersampled render; cheaper and sharper than
                // relying on MSAA alone in a software rasteriser.
                output = Downsample(full, width, height, supersample);
                UnityEngine.Object.DestroyImmediate(full);
            }

            File.WriteAllBytes(path, output.EncodeToPNG());

            var stats = Analyse(output);
            UnityEngine.Object.DestroyImmediate(output);

            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "SHOT_OK name={0} ms={1} lit={2:F4} mean={3:F4} p99={4:F4} warmRatio={5:F4}",
                Path.GetFileName(path), sw.ElapsedMilliseconds,
                stats.LitRatio, stats.Mean, stats.P99, stats.WarmRatio));

            if (stats.LitRatio < 0.05f)
            {
                Debug.LogError($"SHOT_BLANK {Path.GetFileName(path)} lit={stats.LitRatio:F4}");
                return false;
            }
            return true;
        }

        static Texture2D Downsample(Texture2D source, int width, int height, int factor)
        {
            var result = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            var src = source.GetPixels32();
            var dst = new Color32[width * height];
            int sw = source.width;
            float inv = 1f / (factor * factor);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int r = 0, g = 0, b = 0, a = 0;
                    for (int dy = 0; dy < factor; dy++)
                    {
                        int row = (y * factor + dy) * sw;
                        for (int dx = 0; dx < factor; dx++)
                        {
                            var p = src[row + x * factor + dx];
                            r += p.r; g += p.g; b += p.b; a += p.a;
                        }
                    }
                    dst[y * width + x] = new Color32(
                        (byte)(r * inv), (byte)(g * inv), (byte)(b * inv), (byte)(a * inv));
                }
            }

            result.SetPixels32(dst);
            result.Apply();
            return result;
        }

        struct Stats
        {
            public float LitRatio;
            public float Mean;
            public float P99;
            public float WarmRatio;
        }

        /// Cheap objective read on the frame. WarmRatio in particular tracks the concept
        /// art's requirement that gold dominates the palette.
        static Stats Analyse(Texture2D tex)
        {
            var pixels = tex.GetPixels32();
            var histogram = new int[256];
            long sum = 0;
            int lit = 0, warm = 0;

            foreach (var p in pixels)
            {
                int luma = (p.r * 54 + p.g * 183 + p.b * 19) >> 8;
                histogram[luma]++;
                sum += luma;
                if (luma > 12) lit++;
                if (p.r > p.b + 18 && luma > 30) warm++;
            }

            int total = pixels.Length;
            int target = (int)(total * 0.99f);
            int running = 0, p99 = 255;
            for (int i = 0; i < 256; i++)
            {
                running += histogram[i];
                if (running >= target) { p99 = i; break; }
            }

            return new Stats
            {
                LitRatio = (float)lit / total,
                Mean = sum / (float)total / 255f,
                P99 = p99 / 255f,
                WarmRatio = (float)warm / total,
            };
        }

        static void SetFeatureActive(string featureName, bool active)
        {
            var data = AssetDatabase.LoadAllAssetsAtPath("Assets/Settings/HunterRenderer.asset");
            foreach (var obj in data)
            {
                if (obj is UnityEngine.Rendering.Universal.ScriptableRendererFeature feature
                    && feature.name == featureName)
                {
                    feature.SetActive(active);
                }
            }
        }

        static int ParseInt(string envVar, int fallback)
        {
            var raw = Environment.GetEnvironmentVariable(envVar);
            return int.TryParse(raw, out var value) ? value : fallback;
        }
    }
}
