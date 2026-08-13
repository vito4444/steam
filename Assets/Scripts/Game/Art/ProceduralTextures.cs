using System.Collections.Generic;
using UnityEngine;

namespace Worker.Game
{
    /// <summary>
    /// Generates tiling surface textures in code.
    ///
    /// Untextured geometry reads as plastic no matter how well it is lit, because real
    /// surfaces vary at a scale below the shape. These are deliberately subtle: the goal
    /// is to break up flat fills so the eye stops reading "solid colour", not to draw
    /// visible patterns. At this camera distance a machine face is well under a hundred
    /// pixels, so anything stronger turns into noise.
    ///
    /// Everything is value noise from a seeded hash, so a texture is identical between
    /// runs and the screenshot regression still holds.
    /// </summary>
    public static class ProceduralTextures
    {
        private const int Size = 128;

        private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();

        public static void ClearCache()
        {
            foreach (var pair in Cache)
            {
                if (pair.Value != null) Object.Destroy(pair.Value);
            }
            Cache.Clear();
        }

        /// <summary>Rough cast surface: fine grain plus occasional darker aggregate.</summary>
        public static Texture2D Concrete()
        {
            return Build("concrete", (x, y) =>
            {
                float grain = Fbm(x * 0.09f, y * 0.09f, 4, 1234u);
                float aggregate = Fbm(x * 0.31f, y * 0.31f, 2, 77u);
                float stain = Fbm(x * 0.021f, y * 0.021f, 3, 991u);

                float value = 0.86f + grain * 0.16f - stain * 0.13f;
                if (aggregate > 0.78f) value -= 0.10f;

                return new Color(value, value * 0.995f, value * 0.985f);
            });
        }

        /// <summary>Sawn timber: directional grain with knots.</summary>
        public static Texture2D Wood()
        {
            return Build("wood", (x, y) =>
            {
                float wobble = Fbm(x * 0.03f, y * 0.11f, 3, 4242u);
                float rings = Mathf.Sin((y * 0.55f + wobble * 5.5f)) * 0.5f + 0.5f;
                float grain = Fbm(x * 0.6f, y * 0.16f, 2, 31u);

                float value = 0.80f + rings * 0.18f + grain * 0.09f;
                return new Color(value, value * 0.90f, value * 0.76f);
            });
        }

        /// <summary>Brushed metal: fine horizontal striations.</summary>
        public static Texture2D BrushedMetal()
        {
            return Build("metal", (x, y) =>
            {
                float brush = Fbm(x * 1.4f, y * 0.05f, 2, 555u);
                float dirt = Fbm(x * 0.04f, y * 0.04f, 3, 8080u);

                float value = 0.88f + brush * 0.14f - dirt * 0.10f;
                return new Color(value, value * 1.002f, value * 1.01f);
            });
        }

        /// <summary>Painted sheet metal: near flat, with wear at a large scale.</summary>
        public static Texture2D PaintedPanel()
        {
            return Build("painted", (x, y) =>
            {
                float wear = Fbm(x * 0.035f, y * 0.035f, 3, 606u);
                float speckle = Fbm(x * 0.55f, y * 0.55f, 1, 313u);

                float value = 0.93f + wear * 0.10f + speckle * 0.035f;

                // Occasional scuff, so panels do not look freshly moulded.
                if (wear > 0.80f && speckle > 0.62f) value -= 0.09f;

                return new Color(value, value, value);
            });
        }

        /// <summary>Ground cover: mottled organic variation.</summary>
        public static Texture2D Ground()
        {
            return Build("ground", (x, y) =>
            {
                float patches = Fbm(x * 0.05f, y * 0.05f, 4, 171u);
                float detail = Fbm(x * 0.34f, y * 0.34f, 2, 909u);

                float value = 0.80f + patches * 0.30f + detail * 0.10f;
                return new Color(value * 0.95f, value, value * 0.88f);
            });
        }

        // ------------------------------------------------------------------ noise

        private static Texture2D Build(string key, System.Func<int, int, Color> sample)
        {
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, mipChain: true)
            {
                name = "proc_" + key,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 4,
                hideFlags = HideFlags.HideAndDontSave
            };

            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    var color = sample(x, y);
                    pixels[y * Size + x] = new Color32(
                        (byte)(Mathf.Clamp01(color.r) * 255f),
                        (byte)(Mathf.Clamp01(color.g) * 255f),
                        (byte)(Mathf.Clamp01(color.b) * 255f),
                        255);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(true, false);

            Cache[key] = texture;
            return texture;
        }

        /// <summary>Fractal value noise. Wraps on the texture period so tiles are seamless.</summary>
        private static float Fbm(float x, float y, int octaves, uint seed)
        {
            float sum = 0f;
            float amplitude = 0.5f;
            float frequency = 1f;

            for (int i = 0; i < octaves; i++)
            {
                sum += ValueNoise(x * frequency, y * frequency, seed + (uint)i * 7919u) * amplitude;
                amplitude *= 0.5f;
                frequency *= 2f;
            }

            return Mathf.Clamp01(sum);
        }

        private static float ValueNoise(float x, float y, uint seed)
        {
            int xi = Mathf.FloorToInt(x);
            int yi = Mathf.FloorToInt(y);
            float xf = x - xi;
            float yf = y - yi;

            // Smoothstep interpolation; linear leaves visible grid creases.
            float u = xf * xf * (3f - 2f * xf);
            float v = yf * yf * (3f - 2f * yf);

            float a = Hash(xi, yi, seed);
            float b = Hash(xi + 1, yi, seed);
            float c = Hash(xi, yi + 1, seed);
            float d = Hash(xi + 1, yi + 1, seed);

            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }

        private static float Hash(int x, int y, uint seed)
        {
            uint h = (uint)(x * 374761393) + (uint)(y * 668265263) + seed * 2246822519u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0xFFFFFF;
        }
    }
}
