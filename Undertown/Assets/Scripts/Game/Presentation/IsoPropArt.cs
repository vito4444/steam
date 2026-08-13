using System.Collections.Generic;
using UnityEngine;
using Undertown.Core.World;

namespace Undertown.Game.Presentation
{
    /// <summary>
    /// Scenery that stands up off the ground: trees, boulders, clay heaps, mine mouths.
    ///
    /// These cannot live inside a ground tile. A tile is clipped to its own diamond and drawn
    /// in grid order, so anything tall painted into one gets sliced off by the cells in front
    /// of it - a tree ends up as a stump with a smear where its canopy should be. Standing
    /// scenery has to sort against buildings and people, which means it has to be a sprite in
    /// the same ordering scheme they use.
    /// </summary>
    public static class IsoPropArt
    {
        private const int Width = 48;
        private const int Height = 56;

        private static readonly Dictionary<int, Sprite> Cache = new Dictionary<int, Sprite>();

        public static bool HasProp(TileKind kind) =>
            kind == TileKind.Forest || kind == TileKind.Rock ||
            kind == TileKind.ClayDeposit || kind == TileKind.DisusedMine;

        public static Sprite For(TileKind kind, int variant)
        {
            int key = (int)kind * 16 + (variant & 3);
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var px = new Color32[Width * Height];
            var clear = new Color32(0, 0, 0, 0);
            for (int i = 0; i < px.Length; i++) px[i] = clear;

            int cx = Width / 2;
            int cy = 10;

            switch (kind)
            {
                case TileKind.Forest:
                    // Two trees per cell at different heights, jittered by variant, so a wood
                    // has an uneven canopy instead of a repeated stamp.
                    Conifer(px, cx - 9 + variant * 2, cy - 2, 13 + (variant & 1) * 4);
                    Conifer(px, cx + 8 - variant, cy + 3, 16 - (variant & 1) * 3);
                    break;

                case TileKind.Rock:
                    Boulder(px, cx - 6 + variant, cy + 2, 8, C(0x56, 0x56, 0x50), C(0x92, 0x92, 0x88));
                    Boulder(px, cx + 8, cy - 2, 5, C(0x4A, 0x4A, 0x45), C(0x80, 0x80, 0x76));
                    break;

                case TileKind.ClayDeposit:
                    Boulder(px, cx, cy, 11, C(0x7A, 0x44, 0x2E), C(0xBC, 0x7C, 0x58));
                    break;

                case TileKind.DisusedMine:
                    MineMouth(px, cx, cy);
                    break;
            }

            return Cache[key] = ToSprite(px, $"iso_prop_{kind}_{variant}");
        }

        private static void Conifer(Color32[] px, int cx, int baseY, int canopy)
        {
            var dark = C(0x25, 0x38, 0x1A);
            var mid = C(0x33, 0x4C, 0x22);
            var lit = C(0x4C, 0x6B, 0x2E);
            var trunk = C(0x3C, 0x2C, 0x1A);
            var shadow = new Color32(0x14, 0x18, 0x0A, 0x4C);

            for (int dy = -3; dy <= 2; dy++)
            for (int dx = 0; dx <= 9; dx++)
                if (dx + Mathf.Abs(dy) * 3 < 11) Blend(px, cx + dx, baseY + dy, shadow);

            for (int dy = 0; dy < 5; dy++)
            {
                Plot(px, cx, baseY + dy, trunk);
                Plot(px, cx + 1, baseY + dy, trunk);
            }

            for (int row = 0; row < canopy; row++)
            {
                int half = Mathf.Max(0, 7 - row * 7 / canopy);
                int y = baseY + 4 + row;

                // A slight notch every few rows reads as separate boughs rather than a cone.
                if (row % 4 == 3) half = Mathf.Max(0, half - 1);

                for (int dx = -half; dx <= half + 1; dx++)
                    Plot(px, cx + dx, y, dx < -half / 3 ? lit : dx > half / 3 ? dark : mid);
            }
        }

        private static void Boulder(Color32[] px, int cx, int cy, int radius, Color32 dark, Color32 lit)
        {
            var shadow = new Color32(0x14, 0x10, 0x0A, 0x4C);
            for (int y = -radius / 2; y <= radius / 2; y++)
            for (int x = -radius - 3; x <= radius + 3; x++)
                if (x * x + y * y * 9 <= (radius + 3) * (radius + 3)) Blend(px, cx + x + 3, cy + y - 2, shadow);

            for (int y = 0; y <= radius; y++)
            for (int x = -radius; x <= radius; x++)
            {
                if (x * x + (y - radius / 2) * (y - radius / 2) * 2 > radius * radius) continue;
                bool sunlit = x < 0 && y > radius / 3;
                Plot(px, cx + x, cy + y, sunlit ? lit : dark);
            }
        }

        private static void MineMouth(Color32[] px, int cx, int cy)
        {
            var timber = C(0x5A, 0x42, 0x28);
            var timberLit = C(0x7C, 0x5E, 0x3A);
            var dark = C(0x0E, 0x0A, 0x08);
            var spoil = C(0x4A, 0x3C, 0x2A);

            for (int x = -12; x <= 12; x++)
            for (int y = 0; y < 3; y++)
                Plot(px, cx + x, cy + y - 2, spoil);

            for (int y = 0; y < 16; y++)
            for (int x = -8; x <= 8; x++)
                if (x * x + y * y / 4 < 70) Plot(px, cx + x, cy + y, dark);

            for (int y = 0; y < 18; y++)
            {
                Plot(px, cx - 10, cy + y, timberLit);
                Plot(px, cx - 9, cy + y, timber);
                Plot(px, cx + 9, cy + y, timber);
                Plot(px, cx + 10, cy + y, C(0x40, 0x2E, 0x1C));
            }
            for (int x = -11; x <= 11; x++)
            {
                Plot(px, cx + x, cy + 18, timberLit);
                Plot(px, cx + x, cy + 19, timber);
            }
        }

        private static Color32 C(byte r, byte g, byte b) => new Color32(r, g, b, 0xFF);

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

            // Pivoted where the scenery meets the ground, level with the cell's centre.
            return Sprite.Create(
                texture, new Rect(0, 0, Width, Height), new Vector2(0.5f, 10f / Height),
                Iso.PixelsPerUnit, 0, SpriteMeshType.FullRect);
        }
    }
}
