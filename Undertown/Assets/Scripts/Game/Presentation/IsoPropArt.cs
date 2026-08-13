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

        /// <summary>
        /// Clutter placed on trodden ground inside the town: a well, a woodpile, crates, a
        /// cart. None of it is simulated. It is there because a settlement with nothing in
        /// the gaps between its buildings looks like a diagram of a settlement.
        /// </summary>
        public enum Clutter { Well, Woodpile, Crates, Barrels, Cart, Fence }

        public static Sprite ForClutter(Clutter clutter)
        {
            int key = 1000 + (int)clutter;
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var px = Blank();
            int cx = Width / 2;
            int cy = 10;

            switch (clutter)
            {
                case Clutter.Well: Well(px, cx, cy); break;
                case Clutter.Woodpile: Woodpile(px, cx, cy); break;
                case Clutter.Crates: Crates(px, cx, cy); break;
                case Clutter.Barrels: Barrels(px, cx, cy); break;
                case Clutter.Cart: Cart(px, cx, cy); break;
                case Clutter.Fence: Fence(px, cx, cy); break;
            }

            return Cache[key] = ToSprite(px, $"iso_clutter_{clutter}");
        }

        public static Sprite For(TileKind kind, int variant)
        {
            int key = (int)kind * 16 + (variant & 3);
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var px = Blank();
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

        private static void Well(Color32[] px, int cx, int cy)
        {
            var stone = C(0x77, 0x74, 0x6C);
            var stoneLit = C(0x9A, 0x97, 0x8E);
            var stoneDark = C(0x54, 0x52, 0x4C);
            var water = C(0x1E, 0x30, 0x3C);
            var post = C(0x5A, 0x42, 0x28);
            var roof = C(0x7A, 0x5A, 0x34);

            GroundShadow(px, cx + 3, cy - 1, 11, 4);

            for (int y = 0; y < 9; y++)
            for (int x = -9; x <= 9; x++)
            {
                if (x * x + (y - 4) * (y - 4) * 4 > 81) continue;
                Plot(px, cx + x, cy + y, x < -3 ? stoneLit : x > 4 ? stoneDark : stone);
            }
            for (int x = -5; x <= 5; x++)
                if (x * x < 26) Plot(px, cx + x, cy + 8, water);

            for (int y = 9; y < 24; y++) { Plot(px, cx - 7, cy + y, post); Plot(px, cx + 7, cy + y, post); }
            for (int x = -10; x <= 10; x++)
            {
                int lift = 24 + (5 - Mathf.Abs(x) / 2);
                Plot(px, cx + x, cy + lift, x < 0 ? C(0x96, 0x72, 0x44) : roof);
                Plot(px, cx + x, cy + lift - 1, x < 0 ? roof : C(0x5C, 0x42, 0x26));
            }
        }

        private static void Woodpile(Color32[] px, int cx, int cy)
        {
            var bark = C(0x5A, 0x42, 0x28);
            var cut = C(0xBE, 0x9A, 0x62);

            GroundShadow(px, cx + 3, cy - 1, 14, 5);

            for (int layer = 0; layer < 4; layer++)
            for (int log = 0; log < 4 - layer / 2; log++)
            {
                int y = cy + 1 + layer * 4;
                int x0 = cx - 12 + layer + log * 7;
                for (int dx = 0; dx < 6; dx++)
                for (int dy = 0; dy < 4; dy++)
                    Plot(px, x0 + dx, y + dy, dy > 2 ? C(0x74, 0x58, 0x36) : bark);
                for (int dy = 0; dy < 4; dy++) Plot(px, x0 + 5, y + dy, cut);
            }
        }

        private static void Crates(Color32[] px, int cx, int cy)
        {
            GroundShadow(px, cx + 3, cy - 1, 11, 4);
            Crate(px, cx - 7, cy + 1, 9);
            Crate(px, cx + 3, cy + 3, 7);
            Crate(px, cx - 5, cy + 11, 7);
        }

        private static void Crate(Color32[] px, int x, int y, int size)
        {
            var wood = C(0x8A, 0x66, 0x3C);
            var lit = C(0xA8, 0x80, 0x50);
            var dark = C(0x5E, 0x44, 0x28);

            for (int dy = 0; dy < size; dy++)
            for (int dx = 0; dx < size; dx++)
            {
                bool edge = dx == 0 || dy == 0 || dx == size - 1 || dy == size - 1;
                Plot(px, x + dx, y + dy, edge ? dark : dx < size / 3 ? lit : wood);
            }
        }

        private static void Barrels(Color32[] px, int cx, int cy)
        {
            GroundShadow(px, cx + 3, cy - 1, 10, 4);
            Barrel(px, cx - 6, cy + 1);
            Barrel(px, cx + 4, cy + 3);
            Barrel(px, cx - 2, cy + 10);
        }

        private static void Barrel(Color32[] px, int x, int y)
        {
            var stave = C(0x7E, 0x56, 0x30);
            var lit = C(0xA0, 0x74, 0x44);
            var hoop = C(0x4A, 0x44, 0x3C);

            for (int dy = 0; dy < 13; dy++)
            for (int dx = -4; dx <= 4; dx++)
            {
                if (Mathf.Abs(dx) == 4 && (dy < 2 || dy > 10)) continue;
                bool banded = dy == 3 || dy == 9;
                Plot(px, x + dx, y + dy, banded ? hoop : dx < -1 ? lit : stave);
            }
            for (int dx = -3; dx <= 3; dx++) Plot(px, x + dx, y + 13, C(0x8E, 0x66, 0x3C));
        }

        private static void Cart(Color32[] px, int cx, int cy)
        {
            var wood = C(0x7A, 0x58, 0x34);
            var lit = C(0x9E, 0x76, 0x48);
            var iron = C(0x3A, 0x36, 0x32);

            GroundShadow(px, cx + 3, cy - 1, 13, 4);

            for (int dy = 0; dy < 7; dy++)
            for (int dx = -13; dx <= 9; dx++)
                Plot(px, cx + dx, cy + 4 + dy, dy > 4 ? lit : wood);

            for (int dy = 0; dy < 4; dy++)
            for (int dx = -13; dx <= 9; dx++)
                if ((dx + dy) % 5 != 0) Plot(px, cx + dx, cy + 11 + dy, C(0x8E, 0x6A, 0x40));

            for (int a = 0; a < 360; a += 8)
            {
                int wx = cx - 7 + Mathf.RoundToInt(Mathf.Cos(a * Mathf.Deg2Rad) * 6f);
                int wy = cy + 4 + Mathf.RoundToInt(Mathf.Sin(a * Mathf.Deg2Rad) * 3f);
                Plot(px, wx, wy, iron);
                Plot(px, wx + 12, wy, iron);
            }
        }

        private static void Fence(Color32[] px, int cx, int cy)
        {
            var post = C(0x5E, 0x46, 0x2A);
            var rail = C(0x7A, 0x5C, 0x38);

            for (int i = -1; i <= 1; i++)
            {
                int x = cx + i * 14;
                int y = cy + i * 7;
                for (int dy = 0; dy < 13; dy++) { Plot(px, x, y + dy, post); Plot(px, x + 1, y + dy, rail); }
            }

            for (int t = -14; t <= 14; t++)
            {
                int y = cy + t / 2;
                Plot(px, cx + t, y + 10, rail);
                Plot(px, cx + t, y + 5, rail);
            }
        }

        private static void GroundShadow(Color32[] px, int cx, int cy, int rx, int ry)
        {
            var shadow = new Color32(0x14, 0x10, 0x0A, 0x4C);
            for (int y = -ry; y <= ry; y++)
            for (int x = -rx; x <= rx; x++)
                if (x * x * ry * ry + y * y * rx * rx <= rx * rx * ry * ry) Blend(px, cx + x, cy + y, shadow);
        }

        private static Color32[] Blank()
        {
            var px = new Color32[Width * Height];
            var clear = new Color32(0, 0, 0, 0);
            for (int i = 0; i < px.Length; i++) px[i] = clear;
            return px;
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
