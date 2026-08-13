using System.Collections.Generic;
using UnityEngine;
using Worker.Core;

namespace Worker.Game
{
    /// <summary>
    /// Generates every sprite the game draws, at runtime, from code.
    ///
    /// This exists so the project has a complete and consistent visual language before
    /// any hand-authored art is commissioned: shapes are defined by geometry rather than
    /// by files, so a palette or silhouette change is a one-line edit. Hand-drawn sprites
    /// can replace these later without touching the renderers, since everything is served
    /// through <see cref="Building"/>, <see cref="Worker"/> and <see cref="Item"/>.
    /// </summary>
    public static class ProceduralSprites
    {
        public const int PixelsPerUnit = 32;

        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        public static void ClearCache()
        {
            foreach (var pair in Cache)
            {
                if (pair.Value == null) continue;
                Object.Destroy(pair.Value.texture);
                Object.Destroy(pair.Value);
            }
            Cache.Clear();
        }

        /// <summary>Flat white square, used for UI bars and tinted overlays.</summary>
        public static Sprite White()
        {
            const string key = "white";
            if (Cache.TryGetValue(key, out var cached)) return cached;

            var texture = NewTexture(4, 4);
            Fill(texture, Color.white);
            return Store(key, texture, 4);
        }

        /// <summary>
        /// A station or storage tile: dark plate, coloured inner panel, and a role glyph.
        /// Footprint is given in tiles so a 2x2 bench renders at 64x64.
        /// </summary>
        public static Sprite Building(BuildingKind kind, int widthTiles, int heightTiles)
        {
            string key = "b_" + kind + "_" + widthTiles + "x" + heightTiles;
            if (Cache.TryGetValue(key, out var cached)) return cached;

            int w = widthTiles * PixelsPerUnit;
            int h = heightTiles * PixelsPerUnit;
            var texture = NewTexture(w, h);

            var accent = ColorConversion.ForBuilding(kind);
            Fill(texture, Color.clear);

            // Outer plate with a one pixel gap so neighbouring buildings stay distinct.
            FillRect(texture, 1, 1, w - 2, h - 2, Palette.BuildingEdge.ToUnity());
            FillRect(texture, 2, 2, w - 4, h - 4, Palette.BuildingBody.ToUnity());

            // Inner accent panel carries the role colour.
            int inset = 5;
            FillRect(texture, inset, inset, w - inset * 2, h - inset * 2, accent);

            // Top edge highlight reads as a light source from above.
            FillRect(texture, inset, h - inset - 2, w - inset * 2, 2, Lighten(accent, 0.22f));
            FillRect(texture, inset, inset, w - inset * 2, 1, Darken(accent, 0.25f));

            DrawGlyph(texture, kind, accent);

            return Store(key, texture, PixelsPerUnit);
        }

        /// <summary>
        /// One conveyor tile, authored pointing east; the view rotates it into place.
        /// Belts read as recessed infrastructure: a dark bed, two rails, and two solid
        /// chevrons whose tips lead in the direction of travel.
        /// </summary>
        public static Sprite Conveyor()
        {
            const string key = "conveyor";
            if (Cache.TryGetValue(key, out var cached)) return cached;

            const int size = PixelsPerUnit;
            var texture = NewTexture(size, size);
            Fill(texture, Palette.ConveyorBed.ToUnity());

            var rail = Palette.ConveyorRail.ToUnity();
            FillRect(texture, 0, 4, size, 2, rail);
            FillRect(texture, 0, size - 6, size, 2, rail);

            var arrow = Palette.ConveyorArrow.ToUnity();
            for (int index = 0; index < 2; index++)
            {
                int along = size / 4 + index * size / 2;
                const int span = 9;
                for (int i = 0; i < span; i++)
                {
                    int offset = i - span / 2;
                    int depth = span / 2 - Mathf.Abs(offset);
                    FillRect(texture, along + depth - 2, size / 2 + offset, 3, 1, arrow);
                }
            }

            return Store(key, texture, PixelsPerUnit);
        }

        /// <summary>Worker figure: a rounded body with a head, drawn bright against the floor.</summary>
        public static Sprite Worker(bool tired)
        {
            string key = "w_" + (tired ? "tired" : "ok");
            if (Cache.TryGetValue(key, out var cached)) return cached;

            const int size = PixelsPerUnit;
            var texture = NewTexture(size, size);
            Fill(texture, Color.clear);

            var body = tired ? Palette.WorkerTired.ToUnity() : Palette.WorkerBody.ToUnity();

            // Body: a capsule occupying the lower two thirds.
            FillCircle(texture, size / 2, 12, 7, Palette.WorkerOutline.ToUnity());
            FillCircle(texture, size / 2, 12, 6, body);
            FillRect(texture, size / 2 - 7, 8, 14, 6, Palette.WorkerOutline.ToUnity());
            FillRect(texture, size / 2 - 6, 9, 12, 5, body);

            // Head sits above and slightly smaller, giving a readable silhouette at 32px.
            FillCircle(texture, size / 2, 21, 6, Palette.WorkerOutline.ToUnity());
            FillCircle(texture, size / 2, 21, 5, Lighten(body, 0.12f));

            return Store(key, texture, PixelsPerUnit);
        }

        /// <summary>Small item chip drawn above a worker or inside a buffer readout.</summary>
        public static Sprite Item(ItemId item)
        {
            string key = "i_" + item;
            if (Cache.TryGetValue(key, out var cached)) return cached;

            const int size = 16;
            var texture = NewTexture(size, size);
            Fill(texture, Color.clear);

            var color = ColorConversion.ForItem(item);
            FillRect(texture, 2, 2, size - 4, size - 4, Palette.BuildingEdge.ToUnity());
            FillRect(texture, 3, 3, size - 6, size - 6, color);
            FillRect(texture, 3, size - 5, size - 6, 2, Lighten(color, 0.25f));

            return Store(key, texture, PixelsPerUnit);
        }

        /// <summary>
        /// One texture holding the whole factory floor. Cheaper than a tilemap for a map
        /// this size and it keeps the software-rendered self-test fast.
        /// </summary>
        public static Sprite Floor(TileMap map)
        {
            string key = "floor_" + map.Width + "x" + map.Height;
            if (Cache.TryGetValue(key, out var cached)) return cached;

            const int tilePx = 8;
            int w = map.Width * tilePx;
            int h = map.Height * tilePx;
            var texture = NewTexture(w, h);
            var pixels = new Color32[w * h];

            for (int ty = 0; ty < map.Height; ty++)
            {
                for (int tx = 0; tx < map.Width; tx++)
                {
                    var terrain = map.GetTerrain(new GridPos(tx, ty));
                    Color baseColor = terrain == TerrainKind.Floor
                        ? (((tx + ty) & 1) == 0 ? Palette.FloorA.ToUnity() : Palette.FloorB.ToUnity())
                        : Palette.Yard.ToUnity();

                    for (int py = 0; py < tilePx; py++)
                    {
                        for (int px = 0; px < tilePx; px++)
                        {
                            // A one pixel grid line on two edges gives scale without noise.
                            bool line = terrain == TerrainKind.Floor && (px == 0 || py == 0);
                            pixels[(ty * tilePx + py) * w + (tx * tilePx + px)] =
                                line ? Palette.FloorLine.ToUnity() : baseColor;
                        }
                    }
                }
            }

            texture.SetPixels32(pixels);

            // The floor sprite maps one tile to one world unit.
            return Store(key, texture, tilePx);
        }

        // ------------------------------------------------------------------ glyphs

        /// <summary>
        /// Draws a simple pictogram identifying the building's role. These are deliberately
        /// crude geometric marks: at 32 pixels per tile, silhouette beats detail.
        /// </summary>
        private static void DrawGlyph(Texture2D texture, BuildingKind kind, Color accent)
        {
            int w = texture.width;
            int h = texture.height;
            int cx = w / 2;
            int cy = h / 2;
            var ink = Darken(accent, 0.55f);

            switch (kind)
            {
                case BuildingKind.Intake:
                    // Downward arrow into a tray.
                    FillRect(texture, cx - 2, cy - 2, 4, 14, ink);
                    FillTriangleDown(texture, cx, cy - 10, 9, ink);
                    FillRect(texture, cx - 11, cy - 13, 22, 3, ink);
                    break;

                case BuildingKind.Shipping:
                    // Upward arrow out of a tray.
                    FillRect(texture, cx - 2, cy - 8, 4, 14, ink);
                    FillTriangleUp(texture, cx, cy + 10, 9, ink);
                    FillRect(texture, cx - 11, cy - 12, 22, 3, ink);
                    break;

                case BuildingKind.Sawbench:
                    // Circular blade with teeth.
                    FillCircle(texture, cx, cy, 10, ink);
                    FillCircle(texture, cx, cy, 7, accent);
                    FillCircle(texture, cx, cy, 2, ink);
                    for (int i = 0; i < 8; i++)
                    {
                        float angle = i * Mathf.PI / 4f;
                        int px = cx + Mathf.RoundToInt(Mathf.Cos(angle) * 12f);
                        int py = cy + Mathf.RoundToInt(Mathf.Sin(angle) * 12f);
                        FillRect(texture, px - 1, py - 1, 3, 3, ink);
                    }
                    break;

                case BuildingKind.Lathe:
                    // Spindle between two centres.
                    FillRect(texture, cx - 13, cy - 2, 26, 4, ink);
                    FillRect(texture, cx - 13, cy - 8, 3, 16, ink);
                    FillRect(texture, cx + 10, cy - 8, 3, 16, ink);
                    FillCircle(texture, cx, cy, 5, ink);
                    FillCircle(texture, cx, cy, 3, accent);
                    break;

                case BuildingKind.AssemblyBench:
                    // Four parts converging on one.
                    FillRect(texture, cx - 10, cy - 10, 7, 7, ink);
                    FillRect(texture, cx + 3, cy - 10, 7, 7, ink);
                    FillRect(texture, cx - 10, cy + 3, 7, 7, ink);
                    FillRect(texture, cx + 3, cy + 3, 7, 7, ink);
                    FillRect(texture, cx - 2, cy - 2, 4, 4, ink);
                    break;

                case BuildingKind.Storage:
                    // Shelf lines.
                    FillRect(texture, 7, h / 2 + 4, w - 14, 3, ink);
                    FillRect(texture, 7, h / 2 - 6, w - 14, 3, ink);
                    break;

                case BuildingKind.BreakRoom:
                    // Cup with steam.
                    FillRect(texture, cx - 8, cy - 8, 14, 11, ink);
                    FillRect(texture, cx - 6, cy - 6, 10, 7, accent);
                    FillRect(texture, cx + 6, cy - 5, 4, 5, ink);
                    FillRect(texture, cx - 4, cy + 6, 2, 6, ink);
                    FillRect(texture, cx + 1, cy + 6, 2, 6, ink);
                    break;
            }
        }

        // ------------------------------------------------------------- primitives

        private static Texture2D NewTexture(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            return texture;
        }

        private static Sprite Store(string key, Texture2D texture, float pixelsPerUnit)
        {
            texture.Apply(false, false);

            var sprite = Sprite.Create(
                texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit,
                0,
                SpriteMeshType.FullRect);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            Cache[key] = sprite;
            return sprite;
        }

        private static void Fill(Texture2D texture, Color color)
        {
            var pixels = new Color32[texture.width * texture.height];
            Color32 packed = color;
            for (int i = 0; i < pixels.Length; i++) pixels[i] = packed;
            texture.SetPixels32(pixels);
        }

        private static void FillRect(Texture2D texture, int x, int y, int width, int height, Color color)
        {
            int x0 = Mathf.Max(0, x);
            int y0 = Mathf.Max(0, y);
            int x1 = Mathf.Min(texture.width, x + width);
            int y1 = Mathf.Min(texture.height, y + height);
            if (x1 <= x0 || y1 <= y0) return;

            for (int py = y0; py < y1; py++)
            {
                for (int px = x0; px < x1; px++) texture.SetPixel(px, py, color);
            }
        }

        private static void FillCircle(Texture2D texture, int cx, int cy, int radius, Color color)
        {
            int r2 = radius * radius;
            for (int py = cy - radius; py <= cy + radius; py++)
            {
                if (py < 0 || py >= texture.height) continue;
                for (int px = cx - radius; px <= cx + radius; px++)
                {
                    if (px < 0 || px >= texture.width) continue;
                    int dx = px - cx;
                    int dy = py - cy;
                    if (dx * dx + dy * dy <= r2) texture.SetPixel(px, py, color);
                }
            }
        }

        private static void FillTriangleUp(Texture2D texture, int cx, int apexY, int halfWidth, Color color)
        {
            for (int i = 0; i <= halfWidth; i++)
            {
                int y = apexY - i;
                FillRect(texture, cx - i, y, i * 2 + 1, 1, color);
            }
        }

        private static void FillTriangleDown(Texture2D texture, int cx, int apexY, int halfWidth, Color color)
        {
            for (int i = 0; i <= halfWidth; i++)
            {
                int y = apexY + i;
                FillRect(texture, cx - i, y, i * 2 + 1, 1, color);
            }
        }

        private static Color Lighten(Color color, float amount)
            => Color.Lerp(color, Color.white, Mathf.Clamp01(amount));

        private static Color Darken(Color color, float amount)
            => Color.Lerp(color, Color.black, Mathf.Clamp01(amount));
    }
}
