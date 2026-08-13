using System.Collections.Generic;
using UnityEngine;

namespace Abyssal.Visual
{
    /// <summary>
    /// 控制舱所有贴图的程序化生成。
    ///
    /// 整个项目不引入任何位图美术资产：表盘刻度、丝印标签、金属磨损、
    /// 警示条纹、CRT 波形全部在运行时画出来。这样做有三个实际好处：
    /// 构建体积极小、任何一处标注改动都只是改一行字符串、
    /// 在没有显卡的开发机上也能一秒钟重生成全部贴图看效果。
    /// </summary>
    public static class ProceduralTextures
    {
        // 控制舱的配色。整体明度压得很低，让屏幕上任何一个亮点都变成注意力焦点。
        //
        // 钢材本身偏暖褐而不是中性灰：这是长期在含盐湿气里工作的设备该有的样子，
        // 也让整个画面能和概念图那种被钨丝灯烤出来的锈橙调对上。
        public static readonly Color32 SteelDark = new Color32(0x24, 0x20, 0x1B, 255);
        public static readonly Color32 SteelMid = new Color32(0x3C, 0x35, 0x2C, 255);
        public static readonly Color32 SteelLight = new Color32(0x5C, 0x51, 0x42, 255);
        public static readonly Color32 RustOrange = new Color32(0xB8, 0x5C, 0x1E, 255);
        public static readonly Color32 WarnYellow = new Color32(0xF2, 0xC1, 0x4E, 255);
        public static readonly Color32 FaultRed = new Color32(0xD9, 0x3A, 0x2B, 255);
        public static readonly Color32 DialFaceColor = new Color32(0x0C, 0x12, 0x13, 255);
        public static readonly Color32 InkWhite = new Color32(0xD8, 0xDE, 0xDC, 255);
        public static readonly Color32 InkDim = new Color32(0x76, 0x84, 0x82, 255);
        public static readonly Color32 PhosphorGreen = new Color32(0x5B, 0xE8, 0x9A, 255);
        public static readonly Color32 SignalCyan = new Color32(0x4E, 0xC8, 0xD4, 255);

        // 表盘指针的扫描范围：从左下 225° 顺时针到右下 -45°，共 270°。
        public const float DialStartAngle = 225f;
        public const float DialSweep = 270f;

        static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();

        static Texture2D Cached(string key, System.Func<Texture2D> build)
        {
            if (Cache.TryGetValue(key, out var tex) && tex != null) return tex;
            tex = build();
            Cache[key] = tex;
            return tex;
        }

        public static void ClearCache()
        {
            foreach (var tex in Cache.Values)
                if (tex != null) Object.DestroyImmediate(tex);
            Cache.Clear();
        }

        /// <summary>把 0–1 的表盘读数换算成指针角度（度，数学约定）。</summary>
        public static float DialAngle(float t) => DialStartAngle - DialSweep * Mathf.Clamp01(t);

        // ------------------------------------------------------------------ 表盘

        public struct DialSpec
        {
            public string Label;
            public string Unit;
            public float RangeMin;
            public float RangeMax;
            /// <summary>红色危险区的起点，0–1。大于 1 表示没有危险区。</summary>
            public float DangerFrom;
            /// <summary>绿色正常区间，两个值都在 0–1。相等表示不画。</summary>
            public float NormalFrom;
            public float NormalTo;
            public int MajorTicks;
            public int MinorPerMajor;
            public int Size;

            public static DialSpec Default(string label, string unit, float min, float max) => new DialSpec
            {
                Label = label,
                Unit = unit,
                RangeMin = min,
                RangeMax = max,
                DangerFrom = 2f,
                NormalFrom = 0f,
                NormalTo = 0f,
                MajorTicks = 6,
                MinorPerMajor = 5,
                Size = 512,
            };
        }

        public static Texture2D DialFace(DialSpec spec)
        {
            string key = $"dial:{spec.Label}:{spec.RangeMin}:{spec.RangeMax}:{spec.DangerFrom}:" +
                         $"{spec.NormalFrom}:{spec.NormalTo}:{spec.MajorTicks}:{spec.Size}";
            return Cached(key, () => BuildDialFace(spec));
        }

        static Texture2D BuildDialFace(DialSpec spec)
        {
            int size = spec.Size;
            var p = new Painter(size, size, new Color32(0, 0, 0, 0));
            float c = size * 0.5f;
            float outer = size * 0.47f;

            // 外壳：金属圈加内凹的表盘面。
            p.Disc(c, c, outer, SteelMid);
            p.Ring(c, c, outer - size * 0.012f, size * 0.020f, SteelLight);
            p.Disc(c, c, outer - size * 0.035f, new Color32(0x06, 0x0A, 0x0B, 255));

            float tickOuter = outer - size * 0.058f;
            float tickMajorInner = tickOuter - size * 0.070f;
            float tickMinorInner = tickOuter - size * 0.038f;

            // 正常区间用暗绿弧，危险区用暗红弧。玩家在余光里就能判断指针在不在该在的地方。
            if (spec.NormalTo > spec.NormalFrom)
            {
                p.Arc(c, c, tickOuter + size * 0.018f, size * 0.020f,
                      DialAngle(spec.NormalTo), DialAngle(spec.NormalFrom),
                      new Color32(0x2E, 0x6B, 0x45, 255));
            }

            if (spec.DangerFrom <= 1f)
            {
                p.Arc(c, c, tickOuter + size * 0.018f, size * 0.022f,
                      DialAngle(1f), DialAngle(spec.DangerFrom),
                      new Color32(0x8E, 0x24, 0x1B, 255));
            }

            int majors = Mathf.Max(2, spec.MajorTicks);
            int minorPer = Mathf.Max(1, spec.MinorPerMajor);

            for (int i = 0; i < majors; i++)
            {
                float t = i / (float)(majors - 1);
                float ang = DialAngle(t) * Mathf.Deg2Rad;
                float cos = Mathf.Cos(ang), sin = Mathf.Sin(ang);

                p.Line(c + cos * tickMajorInner, c + sin * tickMajorInner,
                       c + cos * tickOuter, c + sin * tickOuter,
                       size * 0.022f, InkWhite);

                float value = Mathf.Lerp(spec.RangeMin, spec.RangeMax, t);
                string text = Mathf.Abs(value) >= 100f
                    ? Mathf.RoundToInt(value).ToString()
                    : value.ToString(Mathf.Abs(value % 1f) < 0.05f ? "0" : "0.0");

                float labelRadius = tickMajorInner - size * 0.052f;
                int lx = Mathf.RoundToInt(c + cos * labelRadius);
                int ly = Mathf.RoundToInt(c + sin * labelRadius);
                int scale = Mathf.Max(1, size / 210);
                BitmapFont.DrawCentered(p.Pixels, size, size, text, lx,
                                        ly - BitmapFont.GlyphHeight * scale / 2, scale, InkWhite);

                if (i == majors - 1) continue;
                for (int m = 1; m < minorPer; m++)
                {
                    float mt = (i + m / (float)minorPer) / (majors - 1);
                    float ma = DialAngle(mt) * Mathf.Deg2Rad;
                    float mc = Mathf.Cos(ma), ms = Mathf.Sin(ma);
                    p.Line(c + mc * tickMinorInner, c + ms * tickMinorInner,
                           c + mc * tickOuter, c + ms * tickOuter,
                           size * 0.012f, InkWhite);
                }
            }

            int textScale = Mathf.Max(1, size / 150);
            p.TextCentered(spec.Label, (int)c, (int)(c + size * 0.155f), textScale, InkWhite);
            p.TextCentered(spec.Unit, (int)c, (int)(c - size * 0.235f), Mathf.Max(1, textScale - 1), InkDim);

            // 中心轴帽。指针会从这里长出来，没有它指针看起来像浮在表面上。
            p.Disc(c, c, size * 0.045f, SteelLight);
            p.Disc(c, c, size * 0.030f, SteelDark);

            p.AddWear(spec.Label.GetHashCode(), 0.035f, 0.03f);
            return p.ToTexture($"Dial_{spec.Label}", mipmaps: false);
        }

        // ------------------------------------------------------------------ 面板

        /// <summary>
        /// 控制台面板贴图：拉丝钢板 + 分区框 + 铆钉行 + 四角螺丝 + 丝印标题。
        ///
        /// 分区框和铆钉不是装饰。真实控制台的面板是分块加工再拼装的，
        /// 每一块之间都有接缝和固定件。少了这些，大块面板会显得空得不真实，
        /// 画面的细节密度也会明显低于参考图。
        /// </summary>
        public static Texture2D Panel(string title, int width = 512, int height = 256, int seed = 0)
        {
            string key = $"panel:{title}:{width}:{height}:{seed}";
            return Cached(key, () =>
            {
                var p = new Painter(width, height, SteelDark);
                p.AddWear(seed * 7919 + title.GetHashCode(), 0.055f, 0.14f);

                int edge = Mathf.Max(2, height / 90);

                // 顶部一条稍亮的横带，模拟面板边缘的高光。
                p.Rect(0, height - Mathf.Max(3, height / 42), width, Mathf.Max(3, height / 42), SteelMid);
                p.RectOutline(0, 0, width, height, edge, SteelMid);

                // 纵向分区。面板按功能分块，块与块之间有一条压出来的凹槽。
                int sections = Mathf.Clamp(width / 170, 2, 6);
                for (int i = 1; i < sections; i++)
                {
                    int x = width * i / sections;
                    p.Rect(x - 1, edge * 2, 2, height - edge * 4, new Color32(
                        (byte)(SteelDark.r * 0.55f), (byte)(SteelDark.g * 0.55f),
                        (byte)(SteelDark.b * 0.55f), 255));
                    p.Rect(x + 1, edge * 2, 1, height - edge * 4, SteelMid);
                }

                // 上下边缘的铆钉行。
                float rivetR = Mathf.Max(2.0f, width / 220f);
                int rivetStep = Mathf.Max(24, width / 18);
                for (int x = rivetStep; x < width - rivetStep / 2; x += rivetStep)
                {
                    foreach (int y in new[] { Mathf.Max(6, height / 26), height - Mathf.Max(6, height / 26) })
                    {
                        p.Disc(x, y, rivetR, SteelLight);
                        p.Disc(x, y + rivetR * 0.22f, rivetR * 0.62f, SteelMid);
                    }
                }

                p.Screws(Mathf.Max(8, width / 40), Mathf.Max(4f, width / 90f), SteelLight, SteelDark);

                if (!string.IsNullOrEmpty(title))
                {
                    int scale = Mathf.Max(1, height / 60);
                    int ty = height - Mathf.Max(20, height / 8);
                    p.TextCentered(title, width / 2, ty, scale, InkDim);

                    // 标题两侧的短横线，工业设备的丝印通常是这个格式。
                    int halfText = BitmapFont.MeasureWidth(title, scale) / 2;
                    int lineY = ty + BitmapFont.GlyphHeight * scale / 2;
                    p.Rect(width / 2 - halfText - width / 8, lineY, width / 11, Mathf.Max(1, scale / 2), InkDim);
                    p.Rect(width / 2 + halfText + width / 40, lineY, width / 11, Mathf.Max(1, scale / 2), InkDim);
                }

                // 角落的设备编号。近距离看会注意到，远看只是增加密度。
                int smallScale = Mathf.Max(1, height / 110);
                p.Text($"P-{(seed * 37 % 90) + 10:00}", edge * 3, edge * 3, smallScale, InkDim);

                return p.ToTexture($"Panel_{title}");
            });
        }

        /// <summary>黄黑斜纹警示带。贴在危险设备和舱口边缘。</summary>
        public static Texture2D HazardStripes(int width = 256, int height = 64, int stripeWidth = 24)
        {
            string key = $"hazard:{width}:{height}:{stripeWidth}";
            return Cached(key, () =>
            {
                var p = new Painter(width, height, new Color32(0x1B, 0x18, 0x10, 255));
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int band = Mathf.FloorToInt((x + y) / (float)stripeWidth);
                    if ((band & 1) == 0) p.Pixels[y * width + x] = WarnYellow;
                }
                p.AddWear(4242, 0.14f, 0.06f);
                return p.ToTexture("HazardStripes");
            });
        }

        /// <summary>
        /// CRT 屏幕贴图：磷绿网格加一条波形，带扫描线。
        /// 这是舱内除告警灯之外唯一的主动光源，所以它的亮度直接影响整体氛围。
        /// </summary>
        public static Texture2D CrtScreen(string caption, int seed = 0, int width = 512, int height = 384)
        {
            string key = $"crt:{caption}:{seed}:{width}:{height}";
            return Cached(key, () =>
            {
                var p = new Painter(width, height, new Color32(0x03, 0x0A, 0x07, 255));

                var grid = new Color32(0x10, 0x2C, 0x1E, 255);
                for (int x = 0; x < width; x += width / 16) p.Rect(x, 0, 1, height, grid);
                for (int y = 0; y < height; y += height / 12) p.Rect(0, y, width, 1, grid);

                var rng = new System.Random(seed);
                float phase = (float)rng.NextDouble() * 10f;
                float prevY = height * 0.5f;
                for (int x = 0; x < width; x++)
                {
                    float t = x / (float)width;
                    float v = Mathf.Sin(t * 18f + phase) * 0.22f
                              + Mathf.Sin(t * 47f + phase * 2.1f) * 0.09f
                              + (Mathf.PerlinNoise(t * 9f, phase) - 0.5f) * 0.16f;
                    float y = height * (0.52f + v);
                    if (x > 0) p.Line(x - 1, prevY, x, y, 2.4f, PhosphorGreen);
                    prevY = y;
                }

                if (!string.IsNullOrEmpty(caption))
                {
                    int scale = Mathf.Max(1, height / 90);
                    p.Text(caption, width / 22, height - height / 9, scale, PhosphorGreen);
                }

                // 扫描线。少了它就不像 CRT，只像一张绿色图片。
                for (int y = 0; y < height; y += 3)
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    var c = p.Pixels[i];
                    p.Pixels[i] = new Color32((byte)(c.r * 0.55f), (byte)(c.g * 0.55f), (byte)(c.b * 0.55f), c.a);
                }

                return p.ToTexture($"CRT_{caption}", true, FilterMode.Bilinear);
            });
        }

        /// <summary>丝印标签牌，贴在开关和阀门旁边。</summary>
        public static Texture2D Nameplate(string text, int width = 256, int height = 64)
        {
            string key = $"plate:{text}:{width}:{height}";
            return Cached(key, () =>
            {
                var p = new Painter(width, height, new Color32(0x14, 0x1C, 0x1D, 255));
                p.RectOutline(0, 0, width, height, 2, SteelLight);
                int scale = Mathf.Max(1, height / 20);
                p.TextCentered(text, width / 2,
                               height / 2 - BitmapFont.GlyphHeight * scale / 2, scale, InkWhite);
                p.AddWear(text.GetHashCode(), 0.05f, 0.08f);
                return p.ToTexture($"Plate_{text}");
            });
        }

        /// <summary>
        /// 方格记录纸，上面有手绘的趋势线和标注。
        ///
        /// 这张纸是整个方案的一个关键道具：玩家要在上面手工画钻压和扭矩的趋势，
        /// 靠自己的判断而不是靠游戏给的提示去识别地层变化。
        /// 在画面上它也是唯一的一块亮色，能把视线从仪表拉到台面上。
        /// </summary>
        public static Texture2D ChartPaper(int seed = 0, int width = 512, int height = 384)
        {
            string key = $"chart:{seed}:{width}:{height}";
            return Cached(key, () =>
            {
                var p = new Painter(width, height, new Color32(0xC9, 0xC0, 0xA6, 255));

                var fine = new Color32(0xA8, 0x9E, 0x82, 255);
                var coarse = new Color32(0x8A, 0x7E, 0x62, 255);
                int cell = Mathf.Max(8, width / 40);

                for (int x = 0; x < width; x += cell)
                    p.Rect(x, 0, 1, height, (x / cell) % 5 == 0 ? coarse : fine);
                for (int y = 0; y < height; y += cell)
                    p.Rect(0, y, width, 1, (y / cell) % 5 == 0 ? coarse : fine);

                // 手绘的两条趋势线，用不同颜色的铅笔画的。
                var rng = new System.Random(seed);
                DrawPencilTrace(p, rng, new Color32(0x24, 0x2A, 0x4E, 255), 0.62f, 3.0f);
                DrawPencilTrace(p, rng, new Color32(0x8E, 0x2A, 0x22, 255), 0.34f, 2.6f);

                int scale = Mathf.Max(1, height / 96);
                p.Text("WOB / TORQUE", cell, height - cell * 2, scale, new Color32(0x3A, 0x36, 0x2C, 255));
                p.Text($"SHIFT {(seed % 9) + 1}", cell, cell, scale, new Color32(0x3A, 0x36, 0x2C, 255));

                p.AddWear(seed + 991, 0.045f, 0.05f);
                return p.ToTexture($"Chart_{seed}");
            });
        }

        static void DrawPencilTrace(Painter p, System.Random rng, Color32 color,
                                    float baseline, float thickness)
        {
            float phase = (float)rng.NextDouble() * 12f;
            float prevY = p.Height * baseline;
            for (int x = 1; x < p.Width; x++)
            {
                float t = x / (float)p.Width;
                float v = Mathf.Sin(t * 7f + phase) * 0.055f
                          + (Mathf.PerlinNoise(t * 5.5f, phase) - 0.5f) * 0.11f;
                float y = p.Height * (baseline + v);
                p.Line(x - 1, prevY, x, y, thickness, color);
                prevY = y;
            }
        }

        /// <summary>
        /// 中心亮、边缘平滑淡出的圆点，用作粒子贴图。
        /// 衰减用的是平方而不是线性：线性衰减的点看起来像一个有边界的圆盘，
        /// 平方衰减才像一颗被光照亮的悬浮颗粒。
        /// </summary>
        public static Texture2D SoftDot(int size = 64)
        {
            return Cached($"softdot:{size}", () =>
            {
                var p = new Painter(size, size, new Color32(0, 0, 0, 0));
                float c = size * 0.5f;
                float r = size * 0.5f;

                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - c + 0.5f) * (x - c + 0.5f) +
                                         (y - c + 0.5f) * (y - c + 0.5f)) / r;
                    if (d >= 1f) continue;
                    float a = (1f - d) * (1f - d);
                    byte v = (byte)Mathf.Clamp(a * 255f, 0, 255);
                    p.Pixels[y * size + x] = new Color32(255, 255, 255, v);
                }

                return p.ToTexture("SoftDot");
            });
        }

        /// <summary>
        /// 纵向渐变，v=0 处最亮、v=1 处全透。贴在光柱锥体上，
        /// 让探照灯的光束从灯口向远端自然消散。
        /// 边缘也做了收窄，避免光柱看起来像一块硬边的塑料板。
        /// </summary>
        public static Texture2D BeamGradient(int width = 64, int height = 256)
        {
            return Cached($"beam:{width}:{height}", () =>
            {
                var p = new Painter(width, height, new Color32(0, 0, 0, 0));
                for (int y = 0; y < height; y++)
                {
                    float v = y / (float)(height - 1);
                    // 沿轴向的衰减比线性更快，近灯口浓、远端迅速淡掉。
                    float axial = Mathf.Pow(1f - v, 2.2f);
                    for (int x = 0; x < width; x++)
                    {
                        // 横向也收一点边，让光柱有柔和的侧缘。
                        float u = Mathf.Abs(x / (float)(width - 1) - 0.5f) * 2f;
                        float lateral = 1f - u * u * 0.55f;
                        byte a = (byte)Mathf.Clamp(axial * lateral * 255f, 0, 255);
                        p.Pixels[y * width + x] = new Color32(255, 255, 255, a);
                    }
                }
                return p.ToTexture("BeamGradient");
            });
        }

        /// <summary>指针贴图。单独一张，运行时靠旋转 Transform 驱动。</summary>
        public static Texture2D Needle(int size = 256)
        {
            return Cached($"needle:{size}", () =>
            {
                var p = new Painter(size, size, new Color32(0, 0, 0, 0));
                float c = size * 0.5f;
                // 尾短头长的经典指针形状，尾巴用来做配重的视觉暗示。
                p.Line(c, c - size * 0.10f, c, c + size * 0.42f, size * 0.030f, FaultRed);
                p.Line(c, c - size * 0.10f, c, c - size * 0.04f, size * 0.055f, FaultRed);
                p.Disc(c, c, size * 0.036f, new Color32(0x30, 0x32, 0x34, 255));
                return p.ToTexture("Needle");
            });
        }
    }
}
