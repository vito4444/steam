using System.Collections.Generic;
using UnityEngine;

namespace Undertown.Game.Presentation
{
    /// <summary>
    /// Figures for the isometric view: standing, seen from the same three-quarter angle as
    /// the buildings, with a contact shadow so they sit on the ground instead of floating
    /// over it. Small, but the silhouette has to survive against both grass and stone.
    /// </summary>
    public static class IsoAgentArt
    {
        private const int Width = 24;
        private const int Height = 40;

        private static readonly Dictionary<int, Sprite> Cache = new Dictionary<int, Sprite>();

        public static Sprite Person(Color32 coat, Color32 trim, bool tall = false)
        {
            int key = (coat.r << 24) ^ (coat.g << 16) ^ (coat.b << 8) ^ (trim.r << 4) ^ trim.g ^ (tall ? 1 : 0);
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var px = new Color32[Width * Height];
            var clear = new Color32(0, 0, 0, 0);
            for (int i = 0; i < px.Length; i++) px[i] = clear;

            var skin = new Color32(0xD9, 0xB0, 0x88, 0xFF);
            var hair = new Color32(0x3E, 0x2C, 0x1C, 0xFF);
            var boot = new Color32(0x2E, 0x22, 0x18, 0xFF);
            var outline = new Color32(0x12, 0x0E, 0x0A, 0xFF);
            var coatLit = Lighten(coat, 18);
            var coatDark = Darken(coat, 22);

            int cx = Width / 2;
            int feet = 5;
            int bodyTop = feet + (tall ? 17 : 15);

            // Contact shadow, an isometric ellipse pressed into the ground.
            var shade = new Color32(0x12, 0x0E, 0x0A, 0x66);
            for (int y = -3; y <= 3; y++)
            for (int x = -7; x <= 7; x++)
                if (x * x + y * y * 6 <= 49) Blend(px, cx + x, feet - 1 + y, shade);

            for (int x = -3; x <= -1; x++) Fill(px, cx + x, feet - 1, 1, 3, boot);
            for (int x = 1; x <= 3; x++) Fill(px, cx + x, feet - 1, 1, 3, boot);

            // Coat, lit from the upper left as everything else is.
            for (int y = feet + 1; y < bodyTop; y++)
            for (int x = -4; x <= 4; x++)
            {
                var tone = x <= -2 ? coatLit : x >= 3 ? coatDark : coat;
                Plot(px, cx + x, y, tone);
            }

            // Shoulder trim carries rank or mood without a second sprite.
            for (int x = -4; x <= 4; x++)
            {
                Plot(px, cx + x, bodyTop - 1, trim);
                Plot(px, cx + x, bodyTop - 2, trim);
            }

            for (int y = bodyTop; y < bodyTop + 5; y++)
            for (int x = -3; x <= 3; x++)
            {
                if ((x == -3 || x == 3) && (y == bodyTop || y == bodyTop + 4)) continue;
                Plot(px, cx + x, y, skin);
            }

            for (int x = -3; x <= 3; x++)
            {
                Plot(px, cx + x, bodyTop + 4, hair);
                Plot(px, cx + x, bodyTop + 5, hair);
            }

            Outline(px, outline);
            return Cache[key] = ToSprite(px, $"iso_agent_{key:X}");
        }

        private static void Fill(Color32[] px, int x, int y, int w, int h, Color32 color)
        {
            for (int dy = 0; dy < h; dy++)
            for (int dx = 0; dx < w; dx++)
                Plot(px, x + dx, y + dy, color);
        }

        private static void Plot(Color32[] px, int x, int y, Color32 color)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) return;
            px[y * Width + x] = color;
        }

        private static void Blend(Color32[] px, int x, int y, Color32 color)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) return;
            var under = px[y * Width + x];
            int a = color.a;
            px[y * Width + x] = new Color32(
                (byte)((color.r * a + under.r * (255 - a)) / 255),
                (byte)((color.g * a + under.g * (255 - a)) / 255),
                (byte)((color.b * a + under.b * (255 - a)) / 255),
                (byte)Mathf.Max(under.a, a));
        }

        private static void Outline(Color32[] px, Color32 outline)
        {
            var original = (Color32[])px.Clone();
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                if (original[y * Width + x].a > 8) continue;

                bool touches = false;
                for (int dy = -1; dy <= 1 && !touches; dy++)
                for (int dx = -1; dx <= 1 && !touches; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= Width || ny >= Height) continue;
                    touches = original[ny * Width + nx].a > 200;
                }

                if (touches) px[y * Width + x] = outline;
            }
        }

        private static Color32 Lighten(Color32 c, int amount) => new Color32(
            (byte)Mathf.Min(255, c.r + amount), (byte)Mathf.Min(255, c.g + amount),
            (byte)Mathf.Min(255, c.b + amount), c.a);

        private static Color32 Darken(Color32 c, int amount) => new Color32(
            (byte)Mathf.Max(0, c.r - amount), (byte)Mathf.Max(0, c.g - amount),
            (byte)Mathf.Max(0, c.b - amount), c.a);

        private static Sprite ToSprite(Color32[] pixels, string name)
        {
            var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, mipChain: false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = name,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false);

            // Anchored at the feet, so a figure stands on the cell centre it is placed at.
            return Sprite.Create(
                texture, new Rect(0, 0, Width, Height), new Vector2(0.5f, 5f / Height),
                Iso.PixelsPerUnit, 0, SpriteMeshType.FullRect);
        }
    }
}
