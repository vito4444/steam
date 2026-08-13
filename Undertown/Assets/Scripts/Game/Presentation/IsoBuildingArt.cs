using System.Collections.Generic;
using UnityEngine;
using Undertown.Core.Buildings;

namespace Undertown.Game.Presentation
{
    /// <summary>
    /// Buildings drawn as solids in the isometric projection: two visible wall faces, a
    /// pitched roof with two slopes, and a cast shadow on the ground.
    ///
    /// This is the part the flat top-down version could not do at all. A building seen from
    /// directly above is a rectangle with a pattern on it; seen at 2:1 it has a silhouette,
    /// and silhouette is most of what makes a town readable.
    /// </summary>
    public static class IsoBuildingArt
    {
        private static readonly Dictionary<BuildingKind, Sprite> Cache = new Dictionary<BuildingKind, Sprite>();

        private struct Scheme
        {
            public Color32 Wall;
            public Color32 Roof;
            public Color32 Trim;
            public int WallHeight;
            public int RoofHeight;
            public bool Thatch;
        }

        public static Sprite For(BuildingKind kind)
        {
            if (Cache.TryGetValue(kind, out var cached) && cached != null) return cached;
            var sprite = Build(kind);
            Cache[kind] = sprite;
            return sprite;
        }

        private static Sprite Build(BuildingKind kind)
        {
            var def = BuildingCatalog.Get(kind);
            int cellsW = Mathf.Max(1, def?.Width ?? 1);
            int cellsH = Mathf.Max(1, def?.Height ?? 1);

            var scheme = SchemeFor(kind);
            int footW = Iso.FootprintWidth(cellsW, cellsH);
            int footH = Iso.FootprintHeight(cellsW, cellsH);
            int total = footH + scheme.WallHeight + scheme.RoofHeight + 6;

            var px = new Color32[footW * total];
            var clear = new Color32(0, 0, 0, 0);
            for (int i = 0; i < px.Length; i++) px[i] = clear;

            int offsetX = cellsH * Iso.HalfWidth;
            int offsetY = footH;

            if (IsOpenGround(kind))
                PaintYard(px, footW, total, cellsW, cellsH, offsetX, offsetY, kind);
            else
                PaintSolid(px, footW, total, cellsW, cellsH, offsetX, offsetY, scheme, kind);

            var pivot = Iso.OriginPivot(cellsW, cellsH, total);
            return ToSprite(px, footW, total, $"iso_{kind}", pivot);
        }

        private static bool IsOpenGround(BuildingKind kind) =>
            kind == BuildingKind.Field || kind == BuildingKind.ClayPit || kind == BuildingKind.Sawpit ||
            kind == BuildingKind.Tunnel;

        /// <summary>A walled structure: shadow, ground slab, two wall faces, then a pitched roof.</summary>
        private static void PaintSolid(Color32[] px, int w, int h, int cw, int ch, int ox, int oy,
            Scheme scheme, BuildingKind kind)
        {
            Shadow(px, w, h, cw, ch, ox, oy);

            var wallLeft = scheme.Wall;
            var wallRight = Darken(scheme.Wall, 34);
            int wallTop = scheme.WallHeight;

            // Wall faces are the two lower edges of the footprint diamond extruded upwards.
            for (int lift = 0; lift < wallTop; lift++)
            {
                // South-west face: the edge from the west corner down to the south corner.
                for (int t = 0; t <= ch * Iso.HalfWidth; t++)
                {
                    float v = ch - t / (float)Iso.HalfWidth / ch * ch;
                    var p = Iso.Project(0f, ch - t / (float)(ch * Iso.HalfWidth) * ch, ox, oy);
                    Plot(px, w, h, p.x - t, p.y - t / 2 + lift, wallLeft);
                }

                // South-east face.
                for (int t = 0; t <= cw * Iso.HalfWidth; t++)
                {
                    var p = Iso.Project(cw, ch, ox, oy);
                    Plot(px, w, h, p.x - t, p.y + t / 2 + lift, wallRight);
                }
            }

            FillFace(px, w, h, cw, ch, ox, oy, wallTop, wallLeft, wallRight, scheme.Trim);
            PaintRoof(px, w, h, cw, ch, ox, oy, wallTop, scheme);
            Details(px, w, h, cw, ch, ox, oy, wallTop, kind, scheme);
        }

        /// <summary>
        /// Rasterises the two visible wall faces properly, by walking the footprint edges in
        /// cell space and extruding each point upward.
        /// </summary>
        private static void FillFace(Color32[] px, int w, int h, int cw, int ch, int ox, int oy,
            int wallTop, Color32 left, Color32 right, Color32 trim)
        {
            const int steps = 256;

            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;

                // West corner (0,ch) to south corner (cw,ch): the left-facing wall.
                var a = Iso.Project(t * cw, ch, ox, oy);
                for (int lift = 0; lift < wallTop; lift++)
                    Plot(px, w, h, a.x, a.y + lift, Shade(left, lift, wallTop));
                Plot(px, w, h, a.x, a.y + wallTop, trim);

                // South corner (cw,ch) to east corner (cw,0): the right-facing wall.
                var b = Iso.Project(cw, (1f - t) * ch, ox, oy);
                for (int lift = 0; lift < wallTop; lift++)
                    Plot(px, w, h, b.x, b.y + lift, Shade(right, lift, wallTop));
                Plot(px, w, h, b.x, b.y + wallTop, trim);
            }

            // Close the interior so the walls read as a solid block rather than two ribbons.
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                var edge = Iso.Project(t * cw, ch, ox, oy);
                var inner = Iso.Project(t * cw, 0f, ox, oy);
                for (int y = inner.y; y <= edge.y; y++)
                for (int lift = 0; lift < wallTop; lift++)
                    Plot(px, w, h, edge.x, y + lift, Shade(left, lift, wallTop));
            }
        }

        /// <summary>Two roof slopes meeting at a ridge, drawn over the top of the walls.</summary>
        private static void PaintRoof(Color32[] px, int w, int h, int cw, int ch, int ox, int oy,
            int wallTop, Scheme scheme)
        {
            var lit = Lighten(scheme.Roof, 22);
            var dark = Darken(scheme.Roof, 30);
            var ridgeColor = Lighten(scheme.Roof, 42);
            const int steps = 320;

            // The ridge runs along the longer axis so the roof reads as a real pitch.
            bool ridgeAlongX = cw >= ch;
            int peak = scheme.RoofHeight;

            for (int i = 0; i <= steps; i++)
            for (int j = 0; j <= steps; j++)
            {
                float u = i / (float)steps * cw;
                float v = j / (float)steps * ch;

                // Distance from the ridge line, normalised, drives both height and shading.
                float across = ridgeAlongX ? (v / ch) : (u / cw);
                float fromRidge = Mathf.Abs(across - 0.5f) * 2f;
                int lift = wallTop + Mathf.RoundToInt((1f - fromRidge) * peak);

                var p = Iso.Project(u, v, ox, oy);
                bool sunSide = ridgeAlongX ? v < ch * 0.5f : u > cw * 0.5f;
                var shade = sunSide ? lit : dark;

                // Courses of tile or thatch across the slope.
                int course = Mathf.RoundToInt(fromRidge * peak);
                if (!scheme.Thatch && course % 3 == 0) shade = Darken(shade, 14);
                if (scheme.Thatch && (i + j) % 7 == 0) shade = Darken(shade, 10);

                PlotOver(px, w, h, p.x, p.y + lift, shade);
                if (fromRidge < 0.03f) PlotOver(px, w, h, p.x, p.y + lift, ridgeColor);
            }
        }

        private static void PaintYard(Color32[] px, int w, int h, int cw, int ch, int ox, int oy, BuildingKind kind)
        {
            Shadow(px, w, h, cw, ch, ox, oy);

            Color32 ground;
            switch (kind)
            {
                case BuildingKind.Field: ground = new Color32(0x9A, 0x83, 0x3C, 0xFF); break;
                case BuildingKind.ClayPit: ground = new Color32(0x8E, 0x55, 0x3C, 0xFF); break;
                case BuildingKind.Tunnel: ground = new Color32(0x35, 0x2C, 0x22, 0xFF); break;
                default: ground = new Color32(0x7B, 0x66, 0x46, 0xFF); break;
            }

            const int steps = 320;
            for (int i = 0; i <= steps; i++)
            for (int j = 0; j <= steps; j++)
            {
                float u = i / (float)steps * cw;
                float v = j / (float)steps * ch;
                var p = Iso.Project(u, v, ox, oy);

                var shade = ground;
                if (kind == BuildingKind.Field && ((int)(u * 4) % 2 == 0))
                    shade = Lighten(ground, 18);
                if (kind == BuildingKind.ClayPit)
                {
                    float toCentre = Mathf.Abs(u / cw - 0.5f) + Mathf.Abs(v / ch - 0.5f);
                    shade = Darken(ground, Mathf.RoundToInt((1f - toCentre) * 40f));
                }

                PlotOver(px, w, h, p.x, p.y + 4, shade);
            }

            // Fence posts and rails around the perimeter.
            var rail = new Color32(0x6B, 0x53, 0x35, 0xFF);
            var post = new Color32(0x46, 0x35, 0x20, 0xFF);
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                Perimeter(px, w, h, cw, ch, ox, oy, t, rail, 4, 5);
                if (i % 26 != 0) continue;
                Perimeter(px, w, h, cw, ch, ox, oy, t, post, 4, 9);
            }

            if (kind == BuildingKind.Sawpit) LogPile(px, w, h, cw, ch, ox, oy);
            if (kind == BuildingKind.Field) Sheaves(px, w, h, cw, ch, ox, oy);
        }

        private static void Perimeter(Color32[] px, int w, int h, int cw, int ch, int ox, int oy,
            float t, Color32 color, int baseLift, int height)
        {
            var points = new[]
            {
                Iso.Project(t * cw, 0f, ox, oy),
                Iso.Project(t * cw, ch, ox, oy),
                Iso.Project(0f, t * ch, ox, oy),
                Iso.Project(cw, t * ch, ox, oy),
            };

            foreach (var p in points)
                for (int lift = baseLift; lift < baseLift + height; lift++)
                    PlotOver(px, w, h, p.x, p.y + lift, color);
        }

        private static void LogPile(Color32[] px, int w, int h, int cw, int ch, int ox, int oy)
        {
            var bark = new Color32(0x5E, 0x44, 0x28, 0xFF);
            var cut = new Color32(0xC0, 0x9C, 0x64, 0xFF);

            for (int log = 0; log < 3; log++)
            for (int i = 0; i <= 120; i++)
            {
                float t = i / 120f;
                var p = Iso.Project(0.3f + t * (cw - 0.6f), 0.5f + log * 0.35f, ox, oy);
                for (int lift = 5; lift < 11; lift++)
                    PlotOver(px, w, h, p.x, p.y + lift, lift > 8 ? Lighten(bark, 18) : bark);
                if (i > 116) for (int lift = 5; lift < 11; lift++) PlotOver(px, w, h, p.x, p.y + lift, cut);
            }
        }

        private static void Sheaves(Color32[] px, int w, int h, int cw, int ch, int ox, int oy)
        {
            var straw = new Color32(0xC6, 0xA8, 0x4E, 0xFF);
            for (int i = 0; i < 6; i++)
            {
                float u = 0.4f + (i % 3) * (cw - 0.8f) / 2f;
                float v = 0.4f + (i / 3) * (ch - 0.8f);
                var p = Iso.Project(u, v, ox, oy);
                for (int lift = 4; lift < 14; lift++)
                {
                    int spread = lift < 9 ? 2 : 1;
                    for (int dx = -spread; dx <= spread; dx++)
                        PlotOver(px, w, h, p.x + dx, p.y + lift, straw);
                }
            }
        }

        private static void Details(Color32[] px, int w, int h, int cw, int ch, int ox, int oy,
            int wallTop, BuildingKind kind, Scheme scheme)
        {
            var dark = new Color32(0x1A, 0x14, 0x0E, 0xFF);
            var glow = new Color32(0xE0, 0xB0, 0x50, 0xFF);

            // A door on the south-facing wall.
            var door = Iso.Project(cw * 0.5f, ch, ox, oy);
            for (int lift = 1; lift < Mathf.Min(wallTop - 1, 14); lift++)
            for (int dx = -4; dx <= 4; dx++)
                Plot(px, w, h, door.x + dx, door.y + lift, dark);

            // Windows, lit, spaced along the same wall.
            for (int i = 1; i <= cw; i++)
            {
                var wnd = Iso.Project(i - 0.5f, ch, ox, oy);
                if (Mathf.Abs(wnd.x - door.x) < 8) continue;
                for (int lift = wallTop - 12; lift < wallTop - 5; lift++)
                for (int dx = -3; dx <= 3; dx++)
                    Plot(px, w, h, wnd.x + dx, wnd.y + lift, glow);
            }

            if (kind == BuildingKind.Brewery || kind == BuildingKind.TownHall)
            {
                var stack = Iso.Project(cw * 0.75f, ch * 0.25f, ox, oy);
                int top = wallTop + scheme.RoofHeight + 10;
                for (int lift = wallTop; lift < top; lift++)
                for (int dx = -3; dx <= 3; dx++)
                    PlotOver(px, w, h, stack.x + dx, stack.y + lift, new Color32(0x6E, 0x5A, 0x44, 0xFF));
                for (int dx = -3; dx <= 3; dx++)
                    PlotOver(px, w, h, stack.x + dx, stack.y + top, new Color32(0x2A, 0x22, 0x1A, 0xFF));
            }
        }

        private static void Shadow(Color32[] px, int w, int h, int cw, int ch, int ox, int oy)
        {
            var shadow = new Color32(0x14, 0x10, 0x0C, 0x55);
            const int steps = 200;

            for (int i = 0; i <= steps; i++)
            for (int j = 0; j <= steps; j++)
            {
                float u = i / (float)steps * cw;
                float v = j / (float)steps * ch;
                var p = Iso.Project(u, v, ox, oy);
                Blend(px, w, h, p.x + 4, p.y - 2, shadow);
            }
        }

        private static Scheme SchemeFor(BuildingKind kind)
        {
            switch (kind)
            {
                case BuildingKind.TownHall:
                    return new Scheme { Wall = C(0xC0, 0xB4, 0x9A), Roof = C(0x93, 0x3E, 0x2E), Trim = C(0x4A, 0x3A, 0x28), WallHeight = 26, RoofHeight = 20 };
                case BuildingKind.Warehouse:
                    return new Scheme { Wall = C(0x74, 0x58, 0x38), Roof = C(0x4A, 0x3E, 0x2E), Trim = C(0x2E, 0x24, 0x18), WallHeight = 30, RoofHeight = 16 };
                case BuildingKind.Brewery:
                    return new Scheme { Wall = C(0x93, 0x6E, 0x44), Roof = C(0x4E, 0x60, 0x46), Trim = C(0x33, 0x26, 0x18), WallHeight = 24, RoofHeight = 18 };
                case BuildingKind.House:
                    return new Scheme { Wall = C(0xA6, 0x84, 0x58), Roof = C(0xBE, 0x9A, 0x50), Trim = C(0x46, 0x34, 0x20), WallHeight = 18, RoofHeight = 16, Thatch = true };
                case BuildingKind.Still:
                    return new Scheme { Wall = C(0x5E, 0x4C, 0x36), Roof = C(0xA8, 0x6E, 0x2E), Trim = C(0x22, 0x1A, 0x12), WallHeight = 16, RoofHeight = 10 };
                case BuildingKind.UnderStore:
                    return new Scheme { Wall = C(0x54, 0x46, 0x34), Roof = C(0x3E, 0x33, 0x26), Trim = C(0x20, 0x18, 0x10), WallHeight = 14, RoofHeight = 8 };
                case BuildingKind.FalseWall:
                    return new Scheme { Wall = C(0x4C, 0x40, 0x32), Roof = C(0x46, 0x3A, 0x2C), Trim = C(0x38, 0x2E, 0x22), WallHeight = 18, RoofHeight = 4 };
                case BuildingKind.HiddenEntrance:
                    return new Scheme { Wall = C(0x5A, 0x48, 0x30), Roof = C(0x8A, 0x6C, 0x3E), Trim = C(0x24, 0x1C, 0x12), WallHeight = 12, RoofHeight = 6 };
                default:
                    return new Scheme { Wall = C(0x8A, 0x70, 0x4E), Roof = C(0x6E, 0x50, 0x36), Trim = C(0x38, 0x2A, 0x1C), WallHeight = 20, RoofHeight = 14 };
            }
        }

        private static Color32 C(byte r, byte g, byte b) => new Color32(r, g, b, 0xFF);

        /// <summary>Walls darken towards their base, which is what gives a face its curve.</summary>
        private static Color32 Shade(Color32 c, int lift, int wallTop)
        {
            int delta = -14 + lift * 22 / Mathf.Max(1, wallTop);
            return new Color32(
                (byte)Mathf.Clamp(c.r + delta, 0, 255),
                (byte)Mathf.Clamp(c.g + delta, 0, 255),
                (byte)Mathf.Clamp(c.b + delta, 0, 255), c.a);
        }

        private static void Plot(Color32[] px, int w, int h, int x, int y, Color32 color)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            px[y * w + x] = color;
        }

        private static void PlotOver(Color32[] px, int w, int h, int x, int y, Color32 color) =>
            Plot(px, w, h, x, y, color);

        private static void Blend(Color32[] px, int w, int h, int x, int y, Color32 color)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            var under = px[y * w + x];
            int a = color.a;
            px[y * w + x] = new Color32(
                (byte)((color.r * a + under.r * (255 - a)) / 255),
                (byte)((color.g * a + under.g * (255 - a)) / 255),
                (byte)((color.b * a + under.b * (255 - a)) / 255),
                (byte)Mathf.Max(under.a, a));
        }

        private static Color32 Lighten(Color32 c, int amount) => new Color32(
            (byte)Mathf.Min(255, c.r + amount), (byte)Mathf.Min(255, c.g + amount),
            (byte)Mathf.Min(255, c.b + amount), c.a);

        private static Color32 Darken(Color32 c, int amount) => new Color32(
            (byte)Mathf.Max(0, c.r - amount), (byte)Mathf.Max(0, c.g - amount),
            (byte)Mathf.Max(0, c.b - amount), c.a);

        private static Sprite ToSprite(Color32[] pixels, int w, int h, string name, Vector2 pivot)
        {
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = name,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false);
            return Sprite.Create(texture, new Rect(0, 0, w, h), pivot, Iso.PixelsPerUnit, 0, SpriteMeshType.FullRect);
        }
    }
}
