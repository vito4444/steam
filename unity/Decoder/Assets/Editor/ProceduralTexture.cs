using System;
using UnityEngine;

namespace Decoder.EditorTools
{
    /// <summary>
    /// 程序化纹理的基础运算：噪声、遮罩、高度图转法线。
    ///
    /// 项目没有美术团队，也没有 GPU 可以跑烘焙工具，所以贴图只能算出来。
    /// 好处是可复现：改一个参数重跑一遍就得到新版本，不需要在外部软件里手工重做。
    /// </summary>
    public static class ProceduralTexture
    {
        // ---------- 噪声 ----------

        /// <summary>整数哈希。用乘法混合而不是查表，避免为不同尺寸维护置换表。</summary>
        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                var h = x * 374761393 + y * 668265263 + seed * 1442695040888963407L;
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0x7FFFFFFF) / (float)0x7FFFFFFF;
            }
        }

        private static float Smooth(float t)
        {
            // 五次平滑，一阶与二阶导数在端点都为零，避免噪声出现网格状条纹。
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        /// <summary>
        /// 可平铺的值噪声。periodX 与 periodY 分开传，是为了做各向异性纹理：
        /// 拉丝金属就是一个方向频率极高、另一个方向频率极低的噪声。
        /// </summary>
        public static float ValueNoise(float x, float y, int periodX, int periodY, int seed)
        {
            var x0 = Mathf.FloorToInt(x);
            var y0 = Mathf.FloorToInt(y);
            var fx = Smooth(x - x0);
            var fy = Smooth(y - y0);

            var xa = Wrap(x0, periodX);
            var xb = Wrap(x0 + 1, periodX);
            var ya = Wrap(y0, periodY);
            var yb = Wrap(y0 + 1, periodY);

            var v00 = Hash(xa, ya, seed);
            var v10 = Hash(xb, ya, seed);
            var v01 = Hash(xa, yb, seed);
            var v11 = Hash(xb, yb, seed);

            return Mathf.Lerp(Mathf.Lerp(v00, v10, fx), Mathf.Lerp(v01, v11, fx), fy);
        }

        private static int Wrap(int value, int period)
        {
            if (period <= 0)
            {
                return value;
            }

            var m = value % period;
            return m < 0 ? m + period : m;
        }

        /// <summary>
        /// 分形布朗运动。多个倍频叠加，低频给大形状，高频给表面细节。
        /// 细节密度基本就取决于最高那个倍频有没有做够。
        /// </summary>
        public static float Fbm(float u, float v, int baseFrequencyX, int baseFrequencyY,
            int octaves, float gain, int seed)
        {
            var sum = 0f;
            var amplitude = 1f;
            var total = 0f;
            var fx = baseFrequencyX;
            var fy = baseFrequencyY;

            for (var o = 0; o < octaves; o++)
            {
                sum += ValueNoise(u * fx, v * fy, fx, fy, seed + o * 7919) * amplitude;
                total += amplitude;
                amplitude *= gain;
                fx *= 2;
                fy *= 2;
            }

            return total > 0f ? sum / total : 0f;
        }

        /// <summary>脊状噪声。把 fBm 折起来，得到划痕和裂纹那样的锐利线条。</summary>
        public static float Ridged(float u, float v, int frequencyX, int frequencyY,
            int octaves, int seed)
        {
            var value = Fbm(u, v, frequencyX, frequencyY, octaves, 0.5f, seed);
            return 1f - Mathf.Abs(value * 2f - 1f);
        }

        /// <summary>
        /// 稀疏划痕。
        ///
        /// 不能直接对 Ridged 的结果卡阈值：折叠之后的值大量堆在高端，
        /// 卡 0.94 这种看着很高的阈值实际会选中一大片，铺满整个表面，
        /// 渲染出来是一层水波纹而不是几道划痕。先用幂函数把分布压到低端，
        /// 再卡阈值，才能得到真正稀疏的线条。
        /// </summary>
        public static float Scratches(float u, float v, int frequencyX, int frequencyY,
            int octaves, int seed, float sparsity = 10f)
        {
            var ridged = Ridged(u, v, frequencyX, frequencyY, octaves, seed);
            return Mathf.Pow(Saturate(ridged), sparsity);
        }

        /// <summary>
        /// 稀疏斑点遮罩。用于气孔、锈斑、掉漆这类离散分布的瑕疵。
        /// coverage 是覆盖率，softness 控制边缘过渡。
        /// </summary>
        public static float Blotches(float u, float v, int frequency, float coverage,
            float softness, int seed)
        {
            var n = Fbm(u, v, frequency, frequency, 4, 0.55f, seed);
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(coverage - softness, coverage + softness, n));
        }

        // ---------- 高度图转法线 ----------

        /// <summary>
        /// 用 Sobel 算子从高度图求切线空间法线。
        ///
        /// 采样时对坐标取模而不是钳制，这样生成的法线图可以无缝平铺——
        /// 边缘钳制会在贴图接缝处留下一圈可见的硬边。
        /// </summary>
        public static Color[] HeightToNormal(float[] height, int size, float strength)
        {
            var normals = new Color[size * size];

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    float H(int dx, int dy)
                    {
                        var sx = ((x + dx) % size + size) % size;
                        var sy = ((y + dy) % size + size) % size;
                        return height[sy * size + sx];
                    }

                    var gx = H(-1, -1) + 2f * H(-1, 0) + H(-1, 1)
                             - H(1, -1) - 2f * H(1, 0) - H(1, 1);
                    var gy = H(-1, -1) + 2f * H(0, -1) + H(1, -1)
                             - H(-1, 1) - 2f * H(0, 1) - H(1, 1);

                    var normal = new Vector3(gx * strength, gy * strength, 1f).normalized;
                    normals[y * size + x] = new Color(
                        normal.x * 0.5f + 0.5f,
                        normal.y * 0.5f + 0.5f,
                        normal.z * 0.5f + 0.5f,
                        1f);
                }
            }

            return normals;
        }

        // ---------- 纹理装配 ----------

        public static Texture2D Create(int size, Color[] pixels, string name, bool linear)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true, linear)
            {
                name = name,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
            };
            texture.SetPixels(pixels);
            texture.Apply(true);
            return texture;
        }

        /// <summary>
        /// Standard 着色器的金属光滑度贴图：R 通道是金属度，A 通道是光滑度。
        /// 其它通道不使用，但仍然写入以免压缩时出现意外。
        /// </summary>
        public static Color MetallicSmoothness(float metallic, float smoothness)
        {
            return new Color(metallic, metallic, metallic, smoothness);
        }

        public static float Saturate(float value)
        {
            return value < 0f ? 0f : value > 1f ? 1f : value;
        }

        /// <summary>把线性亮度按感知曲线抬一下，避免污渍与磨损在暗部糊成一团。</summary>
        public static float Contrast(float value, float amount)
        {
            return Saturate((value - 0.5f) * amount + 0.5f);
        }

        public static float Remap(float value, float fromLow, float fromHigh,
            float toLow, float toHigh)
        {
            if (Math.Abs(fromHigh - fromLow) < 1e-6f)
            {
                return toLow;
            }

            return toLow + (value - fromLow) / (fromHigh - fromLow) * (toHigh - toLow);
        }
    }
}
