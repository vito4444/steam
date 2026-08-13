using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Undertown.Core.World;

namespace Undertown.Game.Presentation
{
    /// <summary>
    /// Ground tiles as 2:1 diamonds. Everything is generated in code, but generated to the
    /// shape and shading the concept art uses rather than to whatever was easiest to draw.
    /// </summary>
    public static class IsoTileArt
    {
        public const int VariantCount = 4;

        private static readonly Dictionary<TileKind, Color32> Palette = new Dictionary<TileKind, Color32>
        {
            { TileKind.Grass,       new Color32(0x6E, 0x74, 0x3E, 0xFF) },
            { TileKind.Dirt,        new Color32(0x84, 0x6B, 0x46, 0xFF) },
            { TileKind.Road,        new Color32(0x99, 0x7E, 0x56, 0xFF) },
            { TileKind.Water,       new Color32(0x36, 0x63, 0x84, 0xFF) },
            // Close to grass on purpose: what marks woodland is the trees standing on it, not
            // a differently coloured floor. Tinting the ground too made every wooded cell read
            // as a dark tile pasted onto the map.
            { TileKind.Forest,      new Color32(0x67, 0x6E, 0x3B, 0xFF) },
            { TileKind.ClayDeposit, new Color32(0x9A, 0x5F, 0x44, 0xFF) },
            { TileKind.Rock,        new Color32(0x6E, 0x6E, 0x66, 0xFF) },
            { TileKind.DisusedMine, new Color32(0x54, 0x44, 0x30, 0xFF) },

            { TileKind.Earth,       new Color32(0x4C, 0x40, 0x32, 0xFF) },
            { TileKind.Cavity,      new Color32(0x2A, 0x23, 0x1B, 0xFF) },
            { TileKind.Bedrock,     new Color32(0x33, 0x36, 0x3B, 0xFF) },
            { TileKind.Aquifer,     new Color32(0x2F, 0x5A, 0x70, 0xFF) },
        };

        private static readonly Dictionary<int, Tile> Cache = new Dictionary<int, Tile>();
        private static Tile _hiddenHollowMarker;
        private static Tile _declaredHollowMarker;
        private static Tile _digOrderMarker;
        private static Sprite _selection;

        public static Color32 ColorOf(TileKind kind) =>
            Palette.TryGetValue(kind, out var c) ? c : new Color32(0xFF, 0x00, 0xFF, 0xFF);

        public static int VariantAt(int x, int y) => Hash(x, y, 7717) % VariantCount;

        public static Tile TileFor(TileKind kind, int variant = 0)
        {
            variant = ((variant % VariantCount) + VariantCount) % VariantCount;
            int key = (int)kind * 16 + variant;
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = BuildGround(kind, variant);
            tile.colliderType = Tile.ColliderType.None;
            Cache[key] = tile;
            return tile;
        }

        public static Tile HiddenHollowMarker =>
            _hiddenHollowMarker != null ? _hiddenHollowMarker
                : _hiddenHollowMarker = Marker("hollow_hidden", new Color32(0xC4, 0x4A, 0x3A, 0xFF), hatched: true);

        public static Tile DeclaredHollowMarker =>
            _declaredHollowMarker != null ? _declaredHollowMarker
                : _declaredHollowMarker = Marker("hollow_declared", new Color32(0x5E, 0x8C, 0x6A, 0xFF), hatched: false);

        public static Tile DigOrderMarker =>
            _digOrderMarker != null ? _digOrderMarker
                : _digOrderMarker = Marker("dig_order", new Color32(0xE0, 0xB5, 0x4A, 0xFF), hatched: true);

        public static Sprite SelectionSprite =>
            _selection != null ? _selection : _selection = MarkerSprite("selection", Color.white, hatched: false);

        /// <summary>
        /// One cell of ground: a diamond top with a short earth wall under its lower edges,
        /// so the terrain reads as a slab with thickness rather than as a flat lozenge.
        /// </summary>
        /// <summary>
        /// One cell of ground: a bare diamond, exactly the size of its cell.
        ///
        /// An earlier version hung a band of earth below each diamond to suggest thickness.
        /// It cannot work on flat ground. Every cell's band lands on top of the cell in front
        /// of it, and since neighbours are drawn in grid order the map ends up ruled with dark
        /// stripes - which is what a slab's shadow would look like if the terrain had steps in
        /// it, and this terrain does not. Thickness belongs to things that stand on the ground,
        /// not to the ground.
        /// </summary>
        private static Sprite BuildGround(TileKind kind, int variant)
        {
            int w = Iso.TileWidth;
            int h = Iso.TileHeight;

            var pixels = Blank(w, h);
            var top = ColorOf(kind);

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!Iso.InsideDiamond(x, y, w, h)) continue;
                int jitter = (Hash(x, y, (int)kind * 31 + variant * 977) % 15) - 7;
                pixels[y * w + x] = Shift(top, jitter);
            }

            Motif(kind, pixels, w, h, 0, variant);
            return ToSprite(pixels, w, h, $"iso_{kind}_{variant}", new Vector2(0.5f, 0.5f));
        }

        /// <summary>The detail that tells one kind of ground from another at a glance.</summary>
        private static void Motif(TileKind kind, Color32[] px, int w, int h, int skirt, int variant)
        {
            int cx = w / 2;
            int cy = skirt + Iso.HalfHeight;

            switch (kind)
            {
                case TileKind.Grass:
                    // Sparse and low-contrast. Dense tufts turned the whole map into visual
                    // noise: with every cell carrying nine bright marks the ground competed
                    // with the buildings instead of sitting behind them.
                    for (int i = 0; i < 3; i++)
                    {
                        int gx = Hash(i, variant, 311) % w;
                        int gy = skirt + Hash(i, variant, 977) % Iso.TileHeight;
                        if (!Iso.InsideDiamond(gx, gy - skirt, w, Iso.TileHeight)) continue;
                        Tuft(px, w, h, gx, gy, (i & 1) == 0
                            ? new Color32(0x64, 0x6A, 0x3A, 0xFF)
                            : new Color32(0x7A, 0x7E, 0x48, 0xFF));
                    }
                    break;

                case TileKind.Forest:
                    // The trees themselves are props, not part of the tile - see IsoPropArt.
                    // Anything drawn inside a ground tile is clipped by the cells in front of
                    // it, which decapitates anything that stands up. What belongs here is only
                    // what lies flat: leaf litter and a rougher floor.
                    for (int i = 0; i < 5; i++)
                    {
                        int gx = Hash(i, variant, 233) % w;
                        int gy = skirt + Hash(i, variant, 811) % Iso.TileHeight;
                        if (!Iso.InsideDiamond(gx, gy - skirt, w, Iso.TileHeight)) continue;
                        Plot(px, w, h, gx, gy, new Color32(0x53, 0x50, 0x2E, 0xFF));
                        Plot(px, w, h, gx + 1, gy, new Color32(0x53, 0x50, 0x2E, 0xFF));
                    }
                    break;

                case TileKind.Road:
                    // Wheel ruts running along the diamond's long axis.
                    for (int i = -12; i <= 12; i++)
                    {
                        Plot(px, w, h, cx + i * 2, cy + i - 4, new Color32(0x86, 0x6C, 0x48, 0xFF));
                        Plot(px, w, h, cx + i * 2, cy + i + 3, new Color32(0x86, 0x6C, 0x48, 0xFF));
                    }
                    break;

                case TileKind.Water:
                    for (int i = -8; i <= 8; i += 4)
                    {
                        Plot(px, w, h, cx + i * 2, cy + i, new Color32(0x5A, 0x8C, 0xAC, 0xFF));
                        Plot(px, w, h, cx + i * 2 + 1, cy + i, new Color32(0x5A, 0x8C, 0xAC, 0xFF));
                        Plot(px, w, h, cx + i * 2 + 2, cy + i, new Color32(0x5A, 0x8C, 0xAC, 0xFF));
                    }
                    break;

                case TileKind.Rock:
                case TileKind.ClayDeposit:
                case TileKind.DisusedMine:
                    // Also props. The tile only carries the scuffed ground around them.
                    for (int i = 0; i < 6; i++)
                    {
                        int gx = Hash(i, variant, 557) % w;
                        int gy = skirt + Hash(i, variant, 293) % Iso.TileHeight;
                        if (!Iso.InsideDiamond(gx, gy - skirt, w, Iso.TileHeight)) continue;
                        Plot(px, w, h, gx, gy, Darken(ColorOf(kind), 20));
                    }
                    break;

                case TileKind.Earth:
                    for (int i = 0; i < 5; i++)
                    {
                        int gx = Hash(i, variant, 641) % w;
                        int gy = skirt + Hash(i, variant, 419) % Iso.TileHeight;
                        if (!Iso.InsideDiamond(gx, gy - skirt, w, Iso.TileHeight)) continue;
                        Plot(px, w, h, gx, gy, new Color32(0x5E, 0x50, 0x3E, 0xFF));
                    }
                    break;

                case TileKind.Cavity:
                    // A swept floor with rubble against the walls.
                    for (int i = 0; i < 4; i++)
                    {
                        int gx = Hash(i, variant, 733) % w;
                        int gy = skirt + Hash(i, variant, 191) % Iso.TileHeight;
                        if (!Iso.InsideDiamond(gx, gy - skirt, w, Iso.TileHeight)) continue;
                        Plot(px, w, h, gx, gy, new Color32(0x3E, 0x34, 0x28, 0xFF));
                    }
                    break;

                case TileKind.Aquifer:
                    for (int i = -6; i <= 6; i += 3)
                        Plot(px, w, h, cx + i * 2, cy + i, new Color32(0x46, 0x7C, 0x94, 0xFF));
                    break;
            }
        }

        private static Tile Marker(string name, Color32 color, bool hatched)
        {
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = MarkerSprite(name, color, hatched);
            tile.colliderType = Tile.ColliderType.None;
            return tile;
        }

        /// <summary>An outlined diamond, optionally hatched, drawn over the ground it marks.</summary>
        private static Sprite MarkerSprite(string name, Color32 color, bool hatched)
        {
            int w = Iso.TileWidth;
            int h = Iso.TileHeight;
            var pixels = Blank(w, h);

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!Iso.InsideDiamond(x, y, w, h)) continue;

                bool border = !Iso.InsideDiamond(x, y, w - 5, h - 3)
                              || !Iso.InsideDiamond(x, y, w - 3, h - 2);
                bool hatch = hatched && (x + y * 2) % 14 < 2;
                if (border || hatch) pixels[y * w + x] = color;
            }

            return ToSprite(pixels, w, h, name, new Vector2(0.5f, 0.5f));
        }

        private static void Tuft(Color32[] px, int w, int h, int x, int y, Color32 color)
        {
            Plot(px, w, h, x, y, color);
            Plot(px, w, h, x - 1, y - 1, color);
            Plot(px, w, h, x + 1, y - 1, color);
        }



        private static Color32[] Blank(int w, int h)
        {
            var pixels = new Color32[w * h];
            var clear = new Color32(0, 0, 0, 0);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;
            return pixels;
        }

        /// <summary>Ground detail, clipped to the cell's own diamond so terrain never bleeds.</summary>
        private static void Plot(Color32[] px, int w, int h, int x, int y, Color32 color)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            if (px[y * w + x].a == 0) return;
            px[y * w + x] = color;
        }

        /// <summary>
        /// Anything that stands up off the cell, which has to be allowed out of the diamond or
        /// it comes out squashed. Draw order handles the overlap: the tilemap sorts from the
        /// top right, so a tree is covered by the cells in front of it.
        /// </summary>

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

        private static Color32 Shift(Color32 c, int delta) => new Color32(
            (byte)Mathf.Clamp(c.r + delta, 0, 255),
            (byte)Mathf.Clamp(c.g + delta, 0, 255),
            (byte)Mathf.Clamp(c.b + delta, 0, 255),
            c.a);

        private static Color32 Darken(Color32 c, int amount) => new Color32(
            (byte)Mathf.Max(0, c.r - amount), (byte)Mathf.Max(0, c.g - amount),
            (byte)Mathf.Max(0, c.b - amount), c.a);

        private static int Hash(int x, int y, int salt)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + salt * 2147483647;
                h = (h ^ (h >> 13)) * 1274126177;
                return (h ^ (h >> 16)) & 0x7FFFFFFF;
            }
        }
    }
}
