using System.Collections.Generic;
using Maner.Controls;
using UnityEngine;

namespace Maner.Cabin
{
    /// <summary>
    /// 表盘面贴图的运行时生成。刻度、数字、红区、单位全部现画，
    /// 因此改一个量程不需要重做任何美术资产，改常量即可。
    /// </summary>
    public static class GaugeTextureFactory
    {
        const int Size = 384;
        const float SweepDegrees = 132f;

        static readonly Dictionary<ControlId, Texture2D> Cache = new Dictionary<ControlId, Texture2D>();

        struct FaceSpec
        {
            public string Title;
            public string Unit;
            public int MajorTicks;
            public string[] Numbers;
        }

        static FaceSpec Spec(ControlId id) => id switch
        {
            ControlId.GaugeBusVoltage => new FaceSpec { Title = "BUS", Unit = "VOLT", MajorTicks = 5, Numbers = new[] { "0", "110", "225", "340", "450" } },
            ControlId.GaugeLoad => new FaceSpec { Title = "LOAD", Unit = "KW", MajorTicks = 5, Numbers = new[] { "0", "350", "700", "1050", "1400" } },
            ControlId.GaugeFrequency => new FaceSpec { Title = "FREQ", Unit = "HZ", MajorTicks = 4, Numbers = new[] { "40", "45", "50", "55" } },
            ControlId.GaugeCoolantTemp => new FaceSpec { Title = "COOL", Unit = "DEG C", MajorTicks = 5, Numbers = new[] { "0", "32", "65", "97", "130" } },
            ControlId.GaugeFuel => new FaceSpec { Title = "FUEL", Unit = "", MajorTicks = 5, Numbers = new[] { "E", "1/4", "1/2", "3/4", "F" } },
            ControlId.GaugeGas => new FaceSpec { Title = "CH4", Unit = "PCT", MajorTicks = 6, Numbers = new[] { "0", ".5", "1.0", "1.5", "2.0", "2.5" } },
            ControlId.GaugeAirflow => new FaceSpec { Title = "AIR", Unit = "M3/S", MajorTicks = 5, Numbers = new[] { "0", "25", "50", "75", "100" } },
            ControlId.GaugeFanSpeed => new FaceSpec { Title = "FAN", Unit = "PCT", MajorTicks = 5, Numbers = new[] { "0", "25", "50", "75", "100" } },
            ControlId.GaugeDepth => new FaceSpec { Title = "DEPTH", Unit = "METER", MajorTicks = 5, Numbers = new[] { "0", "500", "1000", "1500", "2000" } },
            ControlId.GaugeCageSpeed => new FaceSpec { Title = "CAGE", Unit = "M/S", MajorTicks = 5, Numbers = new[] { "-14", "-7", "0", "7", "14" } },
            ControlId.GaugePayload => new FaceSpec { Title = "LOAD", Unit = "KG", MajorTicks = 5, Numbers = new[] { "0", "875", "1750", "2625", "3500" } },
            ControlId.GaugeRopeTension => new FaceSpec { Title = "ROPE", Unit = "KN", MajorTicks = 5, Numbers = new[] { "0", "300", "600", "900", "1200" } },
            _ => new FaceSpec { Title = "", Unit = "", MajorTicks = 5, Numbers = new[] { "0", "", "", "", "1" } },
        };

        public static Texture2D Create(ControlId id)
        {
            if (Cache.TryGetValue(id, out var cached) && cached != null)
            {
                return cached;
            }

            var spec = Spec(id);
            var (min, max) = ConsoleState.GaugeRange(id);
            double? redline = ConsoleState.GaugeRedline(id);

            var buffer = new Color32[Size * Size];
            var faceColor = new Color32(226, 219, 199, 255);
            var edgeColor = new Color32(176, 168, 148, 255);
            var inkColor = new Color32(28, 26, 22, 255);
            var redColor = new Color32(168, 38, 26, 255);

            int center = Size / 2;
            float outerRadius = Size * 0.47f;

            // 底色与边缘渐暗，模拟老化的搪瓷表面。
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / outerRadius;
                    Color32 c = d > 1f ? new Color32(18, 17, 15, 255) : Color32.Lerp(faceColor, edgeColor, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.72f, 1f, d)));
                    buffer[y * Size + x] = c;
                }
            }

            // 红区弧：从红线值一直画到量程顶端。
            if (redline.HasValue)
            {
                float t0 = Mathf.InverseLerp((float)min, (float)max, (float)redline.Value);
                DrawArc(buffer, center, Size * 0.40f, Size * 0.445f, t0, 1f, redColor);
            }

            // 刻度盘外圈。
            DrawArc(buffer, center, Size * 0.435f, Size * 0.445f, 0f, 1f, inkColor);

            int majors = Mathf.Max(2, spec.MajorTicks);
            int minorPerMajor = 4;
            int totalMinor = (majors - 1) * minorPerMajor;

            for (int i = 0; i <= totalMinor; i++)
            {
                float t = (float)i / totalMinor;
                bool isMajor = i % minorPerMajor == 0;
                float inner = isMajor ? Size * 0.365f : Size * 0.400f;
                DrawRadialTick(buffer, center, inner, Size * 0.437f, t, isMajor ? 3 : 1, inkColor);
            }

            for (int i = 0; i < majors && i < spec.Numbers.Length; i++)
            {
                float t = (float)i / (majors - 1);
                var (tx, ty) = PolarToPixel(center, Size * 0.305f, t);
                BitmapFont.DrawCentered(buffer, Size, Size, spec.Numbers[i], tx, ty - 7, 2, inkColor);
            }

            BitmapFont.DrawCentered(buffer, Size, Size, spec.Title, center, center - Size / 8, 3, inkColor);
            if (!string.IsNullOrEmpty(spec.Unit))
            {
                BitmapFont.DrawCentered(buffer, Size, Size, spec.Unit, center, center + Size / 8, 2, inkColor);
            }

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true)
            {
                name = $"T_Gauge_{id}",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 4,
            };
            MirrorHorizontally(buffer);
            tex.SetPixels32(buffer);
            tex.Apply(true, false);
            Cache[id] = tex;


            return tex;
        }

        /// <summary>
        /// 把 0..1 的表值换算成贴图像素坐标。
        /// 指针零位在左下 132°，满位在右下 -132°，与 ControlVisual 的旋转范围一致。
        /// 贴图 V 轴与面板 Y 轴反向，所以这里的 y 直接按「向下为正」计算。
        /// </summary>
        static (int x, int y) PolarToPixel(int center, float radius, float t)
        {
            float deg = SweepDegrees - t * SweepDegrees * 2f;
            float rad = deg * Mathf.Deg2Rad;
            float lx = -Mathf.Sin(rad);
            float ly = Mathf.Cos(rad);
            return (Mathf.RoundToInt(center + lx * radius), Mathf.RoundToInt(center - ly * radius));
        }

        static void MirrorHorizontally(Color32[] buffer)
        {
            for (int y = 0; y < Size; y++)
            {
                int row = y * Size;
                for (int x = 0; x < Size / 2; x++)
                {
                    (buffer[row + x], buffer[row + Size - 1 - x]) = (buffer[row + Size - 1 - x], buffer[row + x]);
                }
            }
        }

        static void DrawRadialTick(Color32[] buffer, int center, float innerRadius, float outerRadius, float t, int halfWidth, Color32 color)
        {
            var (x0, y0) = PolarToPixel(center, innerRadius, t);
            var (x1, y1) = PolarToPixel(center, outerRadius, t);
            DrawThickLine(buffer, x0, y0, x1, y1, halfWidth, color);
        }

        static void DrawThickLine(Color32[] buffer, int x0, int y0, int x1, int y1, int halfWidth, Color32 color)
        {
            int steps = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0)) * 2 + 1;
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                int px = Mathf.RoundToInt(Mathf.Lerp(x0, x1, t));
                int py = Mathf.RoundToInt(Mathf.Lerp(y0, y1, t));
                for (int oy = -halfWidth; oy <= halfWidth; oy++)
                {
                    for (int ox = -halfWidth; ox <= halfWidth; ox++)
                    {
                        int x = px + ox;
                        int y = py + oy;
                        if (x >= 0 && x < Size && y >= 0 && y < Size)
                        {
                            buffer[y * Size + x] = color;
                        }
                    }
                }
            }
        }

        static void DrawArc(Color32[] buffer, int center, float innerRadius, float outerRadius, float t0, float t1, Color32 color)
        {
            int samples = 320;
            for (int i = 0; i <= samples; i++)
            {
                float t = Mathf.Lerp(t0, t1, (float)i / samples);
                for (float r = innerRadius; r <= outerRadius; r += 0.7f)
                {
                    var (x, y) = PolarToPixel(center, r, t);
                    if (x >= 0 && x < Size && y >= 0 && y < Size)
                    {
                        buffer[y * Size + x] = color;
                    }
                }
            }
        }
    }
}
