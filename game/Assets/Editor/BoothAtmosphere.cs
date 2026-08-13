using System.IO;
using UnityEditor;
using UnityEngine;

namespace Monster.EditorTools
{
    /// <summary>The cheap atmospheric layer: the visible shaft of the desk lamp, the dust
    /// hanging in it, and the phosphor overlay on the monitors.
    ///
    /// None of this is real volumetrics. A machine with no GPU cannot afford ray-marched
    /// fog and a game this small does not need it. A cone of additive geometry and a few
    /// hundred particles produce the same read for a rounding error of the cost, which is
    /// the same trade the rest of this scene makes.</summary>
    public static class BoothAtmosphere
    {
        private const string TexturesFolder = "Assets/Textures";

        public static void Build(Transform booth, Vector3 lampPosition, Vector3 lampTarget,
            Transform[] screenAnchors)
        {
            BuildLightShaft(booth, lampPosition, lampTarget);
            BuildDust(booth, lampPosition, lampTarget);

            foreach (var anchor in screenAnchors)
            {
                BuildScreenOverlay(anchor);
            }
        }

        /// <summary>A billboard of additive glow for a lamp seen through fog.
        ///
        /// Unity's fog attenuates geometry but does not scatter light, so a headlamp two
        /// hundred metres down a foggy road is two hard pixels rather than a glare. This
        /// puts the glare in by hand, which is what makes an approaching vehicle read as
        /// approaching rather than as a pair of dots.</summary>
        public static GameObject Glare(string name, Transform parent, Vector3 localPosition,
            float size, Color tint)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            go.transform.localScale = new Vector3(size, size, 1f);

            Object.DestroyImmediate(go.GetComponent<Collider>());

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = AdditiveMaterial($"Glare_{name}", tint,
                GradientTexture("GlareSprite", 1f, radial: true));
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        // ----------------------------------------------------------------- light shaft --

        private static void BuildLightShaft(Transform booth, Vector3 apex, Vector3 target)
        {
            var direction = target - apex;
            var length = direction.magnitude;
            if (length < 0.05f)
            {
                return;
            }

            // Narrower than the light's own 104-degree cone. A shaft matching the cone
            // exactly is 2.7 m across at the desk, larger than the booth, so its bright end
            // sits off the left edge of the frame and nothing reads as a beam at all. What
            // is drawn here is the visible core of the shaft, not its full extent.
            var radius = length * Mathf.Tan(20f * Mathf.Deg2Rad);

            var go = new GameObject("LightShaft");
            go.transform.SetParent(booth, false);
            go.transform.SetPositionAndRotation(apex, Quaternion.LookRotation(direction, Vector3.up));

            go.AddComponent<MeshFilter>().sharedMesh = ConeMesh(length, radius, 20);

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = AdditiveMaterial("LightShaft",
                new Color(1.00f, 0.66f, 0.34f), GradientTexture("ShaftGradient", 0.42f));
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        /// <summary>A cone with its apex at the origin opening along +Z, with V running
        /// from 0 at the apex to 1 at the mouth so the gradient can fade it out.</summary>
        private static Mesh ConeMesh(float length, float radius, int segments)
        {
            var vertices = new Vector3[segments * 2 + 2];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[segments * 6];

            for (var i = 0; i <= segments; i++)
            {
                var angle = i / (float)segments * Mathf.PI * 2f;
                var offset = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, length);

                vertices[i] = Vector3.zero;
                uvs[i] = new Vector2(i / (float)segments, 0f);

                vertices[segments + 1 + i] = offset;
                uvs[segments + 1 + i] = new Vector2(i / (float)segments, 1f);
            }

            for (var i = 0; i < segments; i++)
            {
                triangles[i * 6 + 0] = i;
                triangles[i * 6 + 1] = segments + 1 + i;
                triangles[i * 6 + 2] = segments + 2 + i;

                // Doubled the other way round so the shaft is visible from inside it too.
                triangles[i * 6 + 3] = i;
                triangles[i * 6 + 4] = segments + 2 + i;
                triangles[i * 6 + 5] = segments + 1 + i;
            }

            var mesh = new Mesh { name = "LightShaftCone" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // ------------------------------------------------------------------------ dust --

        private static void BuildDust(Transform booth, Vector3 apex, Vector3 target)
        {
            var go = new GameObject("Dust");
            go.transform.SetParent(booth, false);
            go.transform.position = Vector3.Lerp(apex, target, 0.45f);

            var particles = go.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = 14f;
            main.startSpeed = 0.012f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.0055f, 0.0130f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1.00f, 0.86f, 0.62f, 0.45f), new Color(1.00f, 0.94f, 0.80f, 0.95f));
            main.maxParticles = 200;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.0015f;

            // Prewarmed, or the system is still nearly empty when a screenshot is taken
            // half a second after the scene loads.
            main.prewarm = true;

            var emission = particles.emission;
            emission.rateOverTime = 10f;

            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(0.85f, 0.42f, 0.72f);

            // Motes drift; they do not fly. A visible direction of travel reads as smoke.
            var noise = particles.noise;
            noise.enabled = true;
            noise.strength = 0.03f;
            noise.frequency = 0.25f;
            noise.scrollSpeed = 0.05f;

            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = AdditiveMaterial("DustMote",
                new Color(0.85f, 0.72f, 0.50f), GradientTexture("DustSprite", 1f, radial: true));
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingOrder = 1;
        }

        // -------------------------------------------------------------- screen overlay --

        /// <summary>Scanlines and corner falloff on the monitor glass, as one transparent
        /// quad with a generated texture. This replaced eleven separate scanline boxes per
        /// monitor: fewer draw calls, and it can darken the corners, which loose geometry
        /// cannot.</summary>
        private static void BuildScreenOverlay(Transform screenAnchor)
        {
            if (screenAnchor == null)
            {
                return;
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Phosphor";
            go.transform.SetParent(screenAnchor, false);
            go.transform.localPosition = new Vector3(0f, 0f, -0.004f);
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            go.transform.localScale = new Vector3(0.520f, 0.280f, 1f);

            Object.DestroyImmediate(go.GetComponent<Collider>());

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = ScreenOverlayMaterial();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static Material ScreenOverlayMaterial()
        {
            const string path = "Assets/Materials/PhosphorOverlay.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                return existing;
            }

            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var material = new Material(shader) { name = "PhosphorOverlay" };
            material.SetTexture("_BaseMap", ScreenOverlayTexture());
            material.SetColor("_BaseColor", new Color(0f, 0f, 0f, 1f));
            SetTransparent(material);

            MonsterSetup.EnsureFolder("Assets/Materials");
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>Black with a varying alpha: scanlines, corner falloff and a little
        /// noise. Blended over the phosphor it darkens rather than tints.</summary>
        private static Texture2D ScreenOverlayTexture()
        {
            const string path = TexturesFolder + "/ScreenOverlay.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
            {
                return existing;
            }

            MonsterSetup.EnsureFolder(TexturesFolder);

            const int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true);
            var pixels = new Color32[size * size];
            var random = new System.Random(4711);

            for (var y = 0; y < size; y++)
            {
                var scanline = y % 3 == 0 ? 0.34f : 0f;

                for (var x = 0; x < size; x++)
                {
                    var u = x / (float)(size - 1) * 2f - 1f;
                    var v = y / (float)(size - 1) * 2f - 1f;

                    // Corner falloff follows the rounded rectangle a CRT tube actually is,
                    // not a circle, so the middle of each edge stays clear.
                    var corner = Mathf.Pow(Mathf.Abs(u), 4f) + Mathf.Pow(Mathf.Abs(v), 4f);
                    var vignette = Mathf.Clamp01((corner - 0.45f) / 0.85f) * 0.92f;

                    var grain = (float)random.NextDouble() * 0.05f;
                    var alpha = Mathf.Clamp01(scanline + vignette + grain);

                    pixels[y * size + x] = new Color32(0, 0, 0, (byte)(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), path), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // -------------------------------------------------------------------- materials --

        private static Material AdditiveMaterial(string name, Color tint, Texture2D texture)
        {
            var path = $"Assets/Materials/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                return existing;
            }

            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = name };
            material.SetColor("_BaseColor", tint);
            if (texture != null)
            {
                material.SetTexture("_BaseMap", texture);
            }

            SetTransparent(material, additive: true);

            MonsterSetup.EnsureFolder("Assets/Materials");
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>URP needs six properties and a keyword set together for transparency.
        ///
        /// _Blend is an enum, not a boolean: 0 alpha, 1 premultiply, 2 additive, 3
        /// multiply. Passing 1 for "additive" selects premultiplied, and URP's material
        /// validator then rewrites _SrcBlend to One on import -- so the colour is added at
        /// full strength no matter what its alpha says, and the shaft rendered as an opaque
        /// wedge that ignored every intensity change made to it.</summary>
        private static void SetTransparent(Material material, bool additive = false)
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", additive ? 2f : 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", additive
                ? (float)UnityEngine.Rendering.BlendMode.One
                : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_AlphaClip", 0f);

            // Billboards and the light shaft are viewed from whichever side happens to face
            // the camera; backface culling on them is only ever a way to lose them.
            material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            material.doubleSidedGI = true;
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        private static Texture2D GradientTexture(string name, float peakAlpha, bool radial = false)
        {
            var path = $"{TexturesFolder}/{name}.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
            {
                return existing;
            }

            MonsterSetup.EnsureFolder(TexturesFolder);

            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true);
            var pixels = new Color32[size * size];

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    float alpha;
                    if (radial)
                    {
                        var u = x / (float)(size - 1) * 2f - 1f;
                        var v = y / (float)(size - 1) * 2f - 1f;
                        alpha = Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v));
                        alpha *= alpha;
                    }
                    else
                    {
                        // V runs from the cone's apex to its mouth; the shaft fades as it
                        // gets further from the bulb.
                        var v = y / (float)(size - 1);
                        alpha = Mathf.Pow(1f - v, 3.4f);
                    }

                    var level = (byte)(Mathf.Clamp01(alpha * peakAlpha) * 255f);
                    pixels[y * size + x] = new Color32(level, level, level, level);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), path), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
