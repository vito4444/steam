using System;
using UnityEngine;

namespace Abyssal.Visual
{
    /// <summary>
    /// 直接在像素缓冲上作画的最小工具集。
    ///
    /// 所有形状都用距离场加一像素羽化来抗锯齿。控制台上的刻度线又细又多，
    /// 没有抗锯齿的话在近距离下会闪成一片，那正是玩家会一直盯着的地方。
    /// </summary>
    public sealed class Painter
    {
        public readonly int Width;
        public readonly int Height;
        public readonly Color32[] Pixels;

        public Painter(int width, int height, Color32 fill)
        {
            Width = width;
            Height = height;
            Pixels = new Color32[width * height];
            for (int i = 0; i < Pixels.Length; i++) Pixels[i] = fill;
        }

        public void Blend(int x, int y, Color32 color, float alpha)
        {
            if (alpha <= 0f || x < 0 || y < 0 || x >= Width || y >= Height) return;
            if (alpha > 1f) alpha = 1f;

            int i = y * Width + x;
            var dst = Pixels[i];
            Pixels[i] = new Color32(
                (byte)(dst.r + (color.r - dst.r) * alpha),
                (byte)(dst.g + (color.g - dst.g) * alpha),
                (byte)(dst.b + (color.b - dst.b) * alpha),
                (byte)Mathf.Max(dst.a, color.a * alpha));
        }

        /// <summary>实心圆盘。</summary>
        public void Disc(float cx, float cy, float radius, Color32 color)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - radius - 1));
            int x1 = Mathf.Min(Width - 1, Mathf.CeilToInt(cx + radius + 1));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - radius - 1));
            int y1 = Mathf.Min(Height - 1, Mathf.CeilToInt(cy + radius + 1));

            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                Blend(x, y, color, Mathf.Clamp01(radius - d + 0.5f));
            }
        }

        /// <summary>圆环。</summary>
        public void Ring(float cx, float cy, float radius, float thickness, Color32 color)
        {
            float outer = radius + thickness * 0.5f;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - outer - 1));
            int x1 = Mathf.Min(Width - 1, Mathf.CeilToInt(cx + outer + 1));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - outer - 1));
            int y1 = Mathf.Min(Height - 1, Mathf.CeilToInt(cy + outer + 1));

            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float d = Mathf.Abs(Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - radius);
                Blend(x, y, color, Mathf.Clamp01(thickness * 0.5f - d + 0.5f));
            }
        }

        /// <summary>圆弧。角度为度，数学约定（0° 指向右，逆时针为正）。</summary>
        public void Arc(float cx, float cy, float radius, float thickness,
                        float startDeg, float endDeg, Color32 color)
        {
            if (endDeg < startDeg) (startDeg, endDeg) = (endDeg, startDeg);

            float outer = radius + thickness * 0.5f;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - outer - 1));
            int x1 = Mathf.Min(Width - 1, Mathf.CeilToInt(cx + outer + 1));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - outer - 1));
            int y1 = Mathf.Min(Height - 1, Mathf.CeilToInt(cy + outer + 1));

            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float dx = x - cx, dy = y - cy;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                if (Mathf.Abs(dist - radius) > thickness * 0.5f + 1f) continue;

                float a = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                while (a < startDeg) a += 360f;
                if (a > endDeg) continue;

                Blend(x, y, color, Mathf.Clamp01(thickness * 0.5f - Mathf.Abs(dist - radius) + 0.5f));
            }
        }

        /// <summary>线段，端点为圆头。</summary>
        public void Line(float ax, float ay, float bx, float by, float thickness, Color32 color)
        {
            float half = thickness * 0.5f;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ax, bx) - half - 1));
            int x1 = Mathf.Min(Width - 1, Mathf.CeilToInt(Mathf.Max(ax, bx) + half + 1));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ay, by) - half - 1));
            int y1 = Mathf.Min(Height - 1, Mathf.CeilToInt(Mathf.Max(ay, by) + half + 1));

            float ex = bx - ax, ey = by - ay;
            float lenSq = Mathf.Max(1e-6f, ex * ex + ey * ey);

            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float t = Mathf.Clamp01(((x - ax) * ex + (y - ay) * ey) / lenSq);
                float px = ax + ex * t, py = ay + ey * t;
                float d = Mathf.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
                Blend(x, y, color, Mathf.Clamp01(half - d + 0.5f));
            }
        }

        /// <summary>轴对齐矩形。</summary>
        public void Rect(int x, int y, int w, int h, Color32 color)
        {
            for (int j = Mathf.Max(0, y); j < Mathf.Min(Height, y + h); j++)
            {
                int row = j * Width;
                for (int i = Mathf.Max(0, x); i < Mathf.Min(Width, x + w); i++)
                    Pixels[row + i] = color;
            }
        }

        public void RectOutline(int x, int y, int w, int h, int thickness, Color32 color)
        {
            Rect(x, y, w, thickness, color);
            Rect(x, y + h - thickness, w, thickness, color);
            Rect(x, y, thickness, h, color);
            Rect(x + w - thickness, y, thickness, h, color);
        }

        public void Text(string text, int x, int y, int scale, Color32 color)
            => BitmapFont.Draw(Pixels, Width, Height, text, x, y, scale, color);

        public void TextCentered(string text, int centerX, int y, int scale, Color32 color)
            => BitmapFont.DrawCentered(Pixels, Width, Height, text, centerX, y, scale, color);

        /// <summary>
        /// 叠一层带状噪声，模拟拉丝金属和使用痕迹。
        /// 没有这一层，程序化生成的面板会呈现出一种非常明显的「干净得不真实」的感觉。
        /// </summary>
        public void AddWear(int seed, float intensity, float scale = 0.05f)
        {
            var rng = new System.Random(seed);
            float ox = (float)rng.NextDouble() * 100f;
            float oy = (float)rng.NextDouble() * 100f;

            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                // 横向拉伸的噪声看起来像拉丝，再叠一层细噪声做颗粒。
                float n = Mathf.PerlinNoise(ox + x * scale * 0.18f, oy + y * scale * 6.0f);
                float grain = Mathf.PerlinNoise(ox + x * 0.7f, oy + y * 0.7f);
                float delta = ((n - 0.5f) * 0.75f + (grain - 0.5f) * 0.25f) * intensity;

                int i = y * Width + x;
                var c = Pixels[i];
                Pixels[i] = new Color32(
                    (byte)Mathf.Clamp(c.r + delta * 255f, 0, 255),
                    (byte)Mathf.Clamp(c.g + delta * 255f, 0, 255),
                    (byte)Mathf.Clamp(c.b + delta * 255f, 0, 255),
                    c.a);
            }
        }

        /// <summary>四角螺丝。工业设备上没有螺丝会显得很假。</summary>
        public void Screws(int inset, float radius, Color32 body, Color32 slot)
        {
            (float, float)[] spots =
            {
                (inset, inset), (Width - inset, inset),
                (inset, Height - inset), (Width - inset, Height - inset),
            };

            foreach (var (x, y) in spots)
            {
                Disc(x, y, radius, body);
                Disc(x, y, radius * 0.72f, new Color32(
                    (byte)(body.r * 0.7f), (byte)(body.g * 0.7f), (byte)(body.b * 0.7f), 255));
                Line(x - radius * 0.5f, y - radius * 0.5f, x + radius * 0.5f, y + radius * 0.5f,
                     Mathf.Max(1.5f, radius * 0.24f), slot);
            }
        }

        public Texture2D ToTexture(string name, bool mipmaps = true, FilterMode filter = FilterMode.Bilinear)
        {
            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, mipmaps, false)
            {
                name = name,
                filterMode = filter,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 8,
                mipMapBias = -0.85f,
            };
            tex.SetPixels32(Pixels);
            tex.Apply(mipmaps, false);
            return tex;
        }
    }
}
