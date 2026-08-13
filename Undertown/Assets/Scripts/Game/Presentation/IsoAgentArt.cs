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

            var skin = new Color32(0xC9, 0xA0, 0x78, 0xFF);
            var skinShade = new Color32(0xA2, 0x7C, 0x58, 0xFF);
            var hair = new Color32(0x3E, 0x2C, 0x1C, 0xFF);
            var hose = new Color32(0x50, 0x3E, 0x2A, 0xFF);
            var boot = new Color32(0x2A, 0x20, 0x16, 0xFF);
            var belt = new Color32(0x33, 0x26, 0x1A, 0xFF);
            var outline = new Color32(0x12, 0x0E, 0x0A, 0xFF);
            var coatLit = Lighten(coat, 18);
            var coatDark = Darken(coat, 24);
            var sleeve = Darken(coat, 40);

            int cx = Width / 2;
            int feet = 5;

            // Legs, then a tunic falling to mid-thigh, then head. Drawn as separate parts
            // rather than one block: at this size a figure is four or five pixels wide, and a
            // solid rectangle of coat colour reads as a crate rather than a person. The gap
            // between the legs and the flare of the hem are what make the silhouette legible.
            int legTop = feet + (tall ? 6 : 5);
            int shoulder = legTop + (tall ? 10 : 9);

            var shade = new Color32(0x12, 0x0E, 0x0A, 0x66);
            for (int y = -3; y <= 3; y++)
            for (int x = -7; x <= 7; x++)
                if (x * x + y * y * 6 <= 49) Blend(px, cx + x, feet - 1 + y, shade);

            for (int y = feet; y < legTop; y++)
            {
                var tone = y < feet + 2 ? boot : hose;
                Plot(px, cx - 3, y, tone);
                Plot(px, cx - 2, y, tone);
                Plot(px, cx + 1, y, Darken(tone, 10));
                Plot(px, cx + 2, y, Darken(tone, 10));
            }

            int hem = legTop - 2;
            for (int y = hem; y <= shoulder; y++)
            {
                float t = (y - hem) / (float)Mathf.Max(1, shoulder - hem);
                int half = Mathf.RoundToInt(Mathf.Lerp(4.4f, 3.2f, t));
                for (int x = -half; x <= half; x++)
                {
                    var tone = x <= -half + 1 ? coatLit : x >= half - 1 ? coatDark : coat;
                    Plot(px, cx + x, y, tone);
                }
            }

            for (int x = -4; x <= 4; x++) Plot(px, cx + x, hem + 3, belt);

            // Arms hang outside the tunic, one lit and one in shadow, ending in a hand.
            for (int y = hem + 2; y <= shoulder - 1; y++)
            {
                Plot(px, cx - 5, y, y == hem + 2 ? skinShade : Lighten(sleeve, 22));
                Plot(px, cx + 5, y, y == hem + 2 ? skinShade : sleeve);
            }

            for (int x = -3; x <= 3; x++)
            {
                Plot(px, cx + x, shoulder, trim);
                Plot(px, cx + x, shoulder - 1, trim);
            }

            Plot(px, cx - 1, shoulder + 1, skinShade);
            Plot(px, cx, shoulder + 1, skinShade);
            Plot(px, cx + 1, shoulder + 1, skinShade);

            int headBase = shoulder + 2;
            for (int y = headBase; y < headBase + 5; y++)
            for (int x = -2; x <= 2; x++)
            {
                if ((x == -2 || x == 2) && y == headBase) continue;
                Plot(px, cx + x, y, x >= 1 ? skinShade : skin);
            }

            for (int x = -2; x <= 2; x++) Plot(px, cx + x, headBase + 5, hair);
            Plot(px, cx - 3, headBase + 3, hair);
            Plot(px, cx - 3, headBase + 4, hair);
            Plot(px, cx + 3, headBase + 3, hair);
            Plot(px, cx + 3, headBase + 4, hair);
            Plot(px, cx - 2, headBase + 4, hair);
            Plot(px, cx + 2, headBase + 4, hair);

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
