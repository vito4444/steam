using System.Collections.Generic;
using UnityEngine;
using Undertown.Core.Buildings;

namespace Undertown.Game.Presentation
{
    /// <summary>
    /// Draws each building type into a sprite at runtime. The goal is not beauty but
    /// silhouette: from a screenshot the player must be able to tell a brewery from a
    /// warehouse, and an illicit still from either, without a label.
    /// </summary>
    public static class ProceduralBuildingArt
    {
        private static readonly Dictionary<BuildingKind, Sprite> Cache = new Dictionary<BuildingKind, Sprite>();

        private const int Ppu = ProceduralTileArt.PixelsPerTile;

        public static Sprite For(BuildingKind kind)
        {
            if (Cache.TryGetValue(kind, out var cached) && cached != null) return cached;

            var def = BuildingCatalog.Get(kind);
            int w = Mathf.Max(1, def?.Width ?? 1) * Ppu;
            int h = Mathf.Max(1, def?.Height ?? 1) * Ppu;

            var pixels = new Color32[w * h];
            Paint(kind, pixels, w, h);

            var texture = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = $"building_{kind}",
            };
            texture.SetPixels32(pixels);
            texture.Apply(false);

            var sprite = Sprite.Create(texture, new Rect(0, 0, w, h), Vector2.zero, Ppu, 0, SpriteMeshType.FullRect);
            Cache[kind] = sprite;
            return sprite;
        }

        private static void Paint(BuildingKind kind, Color32[] px, int w, int h)
        {
            var clear = new Color32(0, 0, 0, 0);
            for (int i = 0; i < px.Length; i++) px[i] = clear;

            switch (kind)
            {
                case BuildingKind.TownHall:
                    Structure(px, w, h, wall: C(0xB8, 0xAE, 0x96), roof: C(0x8C, 0x3B, 0x2E), trim: C(0x5A, 0x4A, 0x36));
                    Banner(px, w, h, C(0xC9, 0xA2, 0x3A));
                    break;

                case BuildingKind.Warehouse:
                    Structure(px, w, h, wall: C(0x6B, 0x50, 0x33), roof: C(0x44, 0x3A, 0x2C), trim: C(0x35, 0x2A, 0x1E));
                    Crates(px, w, h);
                    break;

                case BuildingKind.Brewery:
                    Structure(px, w, h, wall: C(0x8A, 0x66, 0x3E), roof: C(0x4E, 0x5E, 0x45), trim: C(0x3B, 0x2C, 0x1C));
                    Barrels(px, w, h, C(0x77, 0x4E, 0x28));
                    Chimney(px, w, h);
                    break;

                case BuildingKind.Sawpit:
                    Yard(px, w, h, C(0x77, 0x63, 0x44));
                    Logs(px, w, h);
                    break;

                case BuildingKind.ClayPit:
                    Yard(px, w, h, C(0x8E, 0x55, 0x3C));
                    Pit(px, w, h, C(0x63, 0x38, 0x26));
                    break;

                case BuildingKind.Field:
                    Yard(px, w, h, C(0x8F, 0x7A, 0x35));
                    Furrows(px, w, h, C(0xB6, 0x9C, 0x44));
                    break;

                case BuildingKind.House:
                    Structure(px, w, h, wall: C(0x9A, 0x7A, 0x52), roof: C(0xB5, 0x93, 0x4C), trim: C(0x4A, 0x38, 0x24));
                    break;

                case BuildingKind.Still:
                    Chamber(px, w, h, floor: C(0x33, 0x2A, 0x20), rim: C(0x1A, 0x14, 0x0E));
                    Vessel(px, w, h, C(0xB9, 0x7A, 0x33));
                    break;

                case BuildingKind.UnderStore:
                    Chamber(px, w, h, floor: C(0x2E, 0x27, 0x1E), rim: C(0x1A, 0x14, 0x0E));
                    Crates(px, w, h);
                    break;

                case BuildingKind.Tunnel:
                    Chamber(px, w, h, floor: C(0x2A, 0x23, 0x1B), rim: C(0x16, 0x12, 0x0D));
                    break;

                case BuildingKind.FalseWall:
                    // Reads as earth from above; only the faint seam gives it away up close.
                    Fill(px, w, h, 0, 0, w, h, C(0x4A, 0x3E, 0x30));
                    Fill(px, w, h, w / 2 - 1, 2, 2, h - 4, C(0x40, 0x35, 0x29));
                    break;

                case BuildingKind.HiddenEntrance:
                    Chamber(px, w, h, floor: C(0x30, 0x28, 0x1E), rim: C(0x16, 0x12, 0x0D));
                    Ladder(px, w, h, C(0x9A, 0x77, 0x40));
                    break;
            }
        }

        private static Color32 C(byte r, byte g, byte b) => new Color32(r, g, b, 0xFF);

        private static void Fill(Color32[] px, int w, int h, int x0, int y0, int rw, int rh, Color32 color)
        {
            for (int y = y0; y < y0 + rh; y++)
            for (int x = x0; x < x0 + rw; x++)
            {
                if (x < 0 || y < 0 || x >= w || y >= h) continue;
                px[y * w + x] = color;
            }
        }

        /// <summary>A walled building seen from above: wall block, roof slab on top, dark trim.</summary>
        private static void Structure(Color32[] px, int w, int h, Color32 wall, Color32 roof, Color32 trim)
        {
            Fill(px, w, h, 1, 1, w - 2, h - 2, wall);
            Fill(px, w, h, 1, h / 2, w - 2, h / 2 - 1, roof);

            // Outline.
            Fill(px, w, h, 0, 0, w, 1, trim);
            Fill(px, w, h, 0, h - 1, w, 1, trim);
            Fill(px, w, h, 0, 0, 1, h, trim);
            Fill(px, w, h, w - 1, 0, 1, h, trim);

            // Ridge line down the middle of the roof reads as a pitched roof from above.
            Fill(px, w, h, 1, h - h / 4, w - 2, 1, trim);

            // A doorway on the south face.
            Fill(px, w, h, w / 2 - 2, 1, 4, 5, trim);
        }

        private static void Yard(Color32[] px, int w, int h, Color32 ground)
        {
            Fill(px, w, h, 0, 0, w, h, ground);
            var fence = C(0x5A, 0x46, 0x2C);
            Fill(px, w, h, 0, 0, w, 1, fence);
            Fill(px, w, h, 0, h - 1, w, 1, fence);
            Fill(px, w, h, 0, 0, 1, h, fence);
            Fill(px, w, h, w - 1, 0, 1, h, fence);
        }

        private static void Chamber(Color32[] px, int w, int h, Color32 floor, Color32 rim)
        {
            Fill(px, w, h, 0, 0, w, h, rim);
            Fill(px, w, h, 2, 2, w - 4, h - 4, floor);
        }

        private static void Banner(Color32[] px, int w, int h, Color32 color)
        {
            Fill(px, w, h, w / 2 - 1, h - 10, 2, 9, C(0x4A, 0x38, 0x24));
            Fill(px, w, h, w / 2 + 1, h - 9, 7, 5, color);
        }

        private static void Crates(Color32[] px, int w, int h)
        {
            var crate = C(0x8A, 0x6A, 0x3E);
            var edge = C(0x5A, 0x42, 0x24);
            for (int i = 0; i < 4; i++)
            {
                int x = 4 + (i % 2) * 12;
                int y = 4 + (i / 2) * 12;
                Fill(px, w, h, x, y, 9, 9, crate);
                Fill(px, w, h, x, y + 4, 9, 1, edge);
            }
        }

        private static void Barrels(Color32[] px, int w, int h, Color32 color)
        {
            var hoop = C(0x3C, 0x2A, 0x16);
            for (int i = 0; i < 3; i++)
            {
                int x = 5 + i * 11;
                int y = 4;
                Fill(px, w, h, x, y, 8, 8, color);
                Fill(px, w, h, x, y + 3, 8, 1, hoop);
            }
        }

        private static void Chimney(Color32[] px, int w, int h)
        {
            Fill(px, w, h, w - 12, h - 12, 6, 10, C(0x6E, 0x5A, 0x44));
            Fill(px, w, h, w - 12, h - 4, 6, 3, C(0x2A, 0x22, 0x1A));
        }

        private static void Logs(Color32[] px, int w, int h)
        {
            var bark = C(0x5E, 0x44, 0x28);
            var cut = C(0xB0, 0x8E, 0x5C);
            for (int i = 0; i < 4; i++)
            {
                int y = 5 + i * 6;
                Fill(px, w, h, 4, y, w - 12, 4, bark);
                Fill(px, w, h, w - 8, y, 3, 4, cut);
            }
        }

        private static void Pit(Color32[] px, int w, int h, Color32 color)
        {
            for (int ring = 0; ring < 3; ring++)
                Fill(px, w, h, 5 + ring * 2, 5 + ring * 2, w - 10 - ring * 4, h - 10 - ring * 4,
                    new Color32((byte)(color.r - ring * 8), (byte)(color.g - ring * 6), (byte)(color.b - ring * 4), 0xFF));
        }

        private static void Furrows(Color32[] px, int w, int h, Color32 color)
        {
            for (int y = 4; y < h - 3; y += 5) Fill(px, w, h, 3, y, w - 6, 2, color);
        }

        private static void Vessel(Color32[] px, int w, int h, Color32 copper)
        {
            int cx = w / 2, cy = h / 2;
            for (int y = cy - 8; y <= cy + 8; y++)
            for (int x = cx - 8; x <= cx + 8; x++)
            {
                if (x < 0 || y < 0 || x >= w || y >= h) continue;
                int dx = x - cx, dy = y - cy;
                if (dx * dx + dy * dy > 64) continue;
                px[y * w + x] = copper;
            }
            // Condenser pipe running off to one side.
            Fill(px, w, h, cx + 6, cy, 10, 2, C(0x8C, 0x5C, 0x28));
            Fill(px, w, h, cx - 2, cy + 8, 4, 6, C(0x8C, 0x5C, 0x28));
        }

        private static void Ladder(Color32[] px, int w, int h, Color32 color)
        {
            Fill(px, w, h, w / 2 - 5, 4, 2, h - 8, color);
            Fill(px, w, h, w / 2 + 3, 4, 2, h - 8, color);
            for (int y = 6; y < h - 6; y += 5) Fill(px, w, h, w / 2 - 5, y, 10, 2, color);
        }
    }
}
