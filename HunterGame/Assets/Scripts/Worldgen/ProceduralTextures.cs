using UnityEngine;

namespace Hunter.Worldgen
{
    /// CPU-generated tiling textures. The project has no art team and no GPU, so surface
    /// detail has to come from noise fields rather than authored or scanned maps.
    public static class ProceduralTextures
    {
        public static float Fbm(float x, float y, int octaves, float lacunarity = 2.03f, float gain = 0.5f)
        {
            float sum = 0f, amp = 1f, freq = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += amp * Mathf.PerlinNoise(x * freq, y * freq);
                norm += amp;
                amp *= gain;
                freq *= lacunarity;
            }
            return sum / Mathf.Max(norm, 1e-5f);
        }

        /// Ridged noise produces sharp valleys instead of soft blobs, which is what makes
        /// cracks in stone read as cracks rather than as stains.
        static float Ridge(float x, float y, int octaves)
        {
            float sum = 0f, amp = 1f, freq = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                float n = 1f - Mathf.Abs(Mathf.PerlinNoise(x * freq, y * freq) * 2f - 1f);
                sum += amp * n * n;
                norm += amp;
                amp *= 0.55f;
                freq *= 2.07f;
            }
            return sum / Mathf.Max(norm, 1e-5f);
        }

        /// Height field shared by the albedo and the normal map so the lighting and the
        /// colour variation agree with each other.
        static float[,] StoneHeight(int size, float scale, int seed)
        {
            var h = new float[size, size];
            float o = seed * 37.13f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size * scale + o;
                    float v = (float)y / size * scale + o;

                    float bulk = Fbm(u, v, 5);
                    float grain = Fbm(u * 7.3f, v * 7.3f, 3) * 0.22f;
                    float cracks = Mathf.Pow(Ridge(u * 1.6f, v * 1.6f, 4), 3.2f);

                    h[x, y] = Mathf.Clamp01(bulk * 0.78f + grain - cracks * 0.55f);
                }
            }
            return h;
        }

        public static Texture2D StoneAlbedo(int size, Color dark, Color light, int seed, float scale = 4f)
        {
            var h = StoneHeight(size, scale, seed);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, false);
            var pixels = new Color32[size * size];
            float o = seed * 11.7f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size * scale + o;
                    float v = (float)y / size * scale + o;

                    float t = h[x, y];
                    var c = Color.Lerp(dark, light, t);

                    // Large-scale patchiness stops the tile from reading as one flat rock.
                    float patch = Fbm(u * 0.45f, v * 0.45f, 3);
                    c *= Mathf.Lerp(0.82f, 1.14f, patch);

                    // Damp moss settling in the recesses, tinted toward the fog's green-grey.
                    float moss = Mathf.Clamp01(Fbm(u * 1.1f + 30f, v * 1.1f + 30f, 4) - 0.42f) * 2.1f;
                    moss *= Mathf.Clamp01(1f - t * 1.5f);
                    c = Color.Lerp(c, new Color(0.22f, 0.26f, 0.19f), moss * 0.38f);

                    pixels[y * size + x] = c;
                }
            }

            tex.SetPixels32(pixels);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.anisoLevel = 8;
            tex.Apply(true);
            return tex;
        }

        public static Texture2D StoneNormal(int size, int seed, float strength = 2.4f, float scale = 4f)
        {
            var h = StoneHeight(size, scale, seed);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, true);
            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int xm = (x - 1 + size) % size, xp = (x + 1) % size;
                    int ym = (y - 1 + size) % size, yp = (y + 1) % size;

                    float dx = (h[xp, y] - h[xm, y]) * strength;
                    float dy = (h[x, yp] - h[x, ym]) * strength;
                    var n = new Vector3(-dx, -dy, 1f).normalized;

                    // Unity's DXT5nm-style layout: X in alpha, Y in green.
                    pixels[y * size + x] = new Color32(
                        255,
                        (byte)((n.y * 0.5f + 0.5f) * 255f),
                        255,
                        (byte)((n.x * 0.5f + 0.5f) * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.anisoLevel = 8;
            tex.Apply(true);
            return tex;
        }

        /// URP metallic-gloss map: RGB metallic, alpha smoothness. Recesses hold water and
        /// therefore read as glossier than exposed faces, which is what sells wet stone.
        public static Texture2D StoneMask(int size, int seed, float baseSmooth, float scale = 4f)
        {
            var h = StoneHeight(size, scale, seed);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, true);
            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float wet = Mathf.Clamp01(1f - h[x, y]);
                    float smooth = Mathf.Clamp01(baseSmooth * (0.45f + wet * 1.35f));
                    pixels[y * size + x] = new Color32(0, 0, 0, (byte)(smooth * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.Apply(true);
            return tex;
        }

        /// Radial falloff sprite used for lantern glow, dust motes and mist cards.
        public static Texture2D RadialGlow(int size, float power = 2.6f)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, true);
            var pixels = new Color32[size * size];
            float c = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Clamp01(Vector2.Distance(new Vector2(x, y), new Vector2(c, c)) / c);
                    float a = Mathf.Pow(1f - d, power);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }

            tex.SetPixels32(pixels);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.Apply(true);
            return tex;
        }
    }
}
