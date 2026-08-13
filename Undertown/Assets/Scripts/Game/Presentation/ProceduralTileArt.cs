using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Undertown.Core.World;

namespace Undertown.Game.Presentation
{
    /// <summary>
    /// Generates the tile sprites in code. Hand-drawn pixel art will replace this, but
    /// having the palette and the tile grammar defined in one place first means the layout,
    /// readability and colour contrast can be evaluated before any art exists - and it keeps
    /// the project buildable on a machine with no artist attached to it.
    /// </summary>
    public static class ProceduralTileArt
    {
        public const int PixelsPerTile = 32;

        // Surface reads warm, underground reads cold. The temperature shift between the two
        // layers is itself information: the player should know which view they are in at a glance.
        private static readonly Dictionary<TileKind, Color32> Palette = new Dictionary<TileKind, Color32>
        {
            { TileKind.Grass,       new Color32(0x6B, 0x7A, 0x3A, 0xFF) },
            { TileKind.Dirt,        new Color32(0x8B, 0x6F, 0x47, 0xFF) },
            { TileKind.Road,        new Color32(0x9C, 0x81, 0x58, 0xFF) },
            { TileKind.Water,       new Color32(0x3D, 0x6B, 0x8A, 0xFF) },
            { TileKind.Forest,      new Color32(0x3D, 0x52, 0x29, 0xFF) },
            { TileKind.ClayDeposit, new Color32(0xA5, 0x67, 0x4A, 0xFF) },
            { TileKind.Rock,        new Color32(0x6E, 0x6E, 0x66, 0xFF) },
            { TileKind.DisusedMine, new Color32(0x4A, 0x3B, 0x2A, 0xFF) },

            { TileKind.Earth,       new Color32(0x4A, 0x3E, 0x30, 0xFF) },
            { TileKind.Cavity,      new Color32(0x1C, 0x18, 0x14, 0xFF) },
            { TileKind.Bedrock,     new Color32(0x2E, 0x31, 0x36, 0xFF) },
            { TileKind.Aquifer,     new Color32(0x2F, 0x5A, 0x70, 0xFF) },
        };

        private static readonly Dictionary<TileKind, Tile> Cache = new Dictionary<TileKind, Tile>();

        private static Tile _hiddenHollowMarker;
        private static Tile _declaredHollowMarker;

        /// <summary>Seen from the surface: ground that is hollow underneath and not on the tax roll.</summary>
        public static Tile HiddenHollowMarker =>
            _hiddenHollowMarker != null
                ? _hiddenHollowMarker
                : _hiddenHollowMarker = MarkerTile("hollow_hidden", new Color32(0xC4, 0x4A, 0x3A, 0xFF), hatched: true);

        /// <summary>Seen from the surface: a cellar or shaft the empire already knows about.</summary>
        public static Tile DeclaredHollowMarker =>
            _declaredHollowMarker != null
                ? _declaredHollowMarker
                : _declaredHollowMarker = MarkerTile("hollow_declared", new Color32(0x5E, 0x8C, 0x6A, 0xFF), hatched: false);

        public static Color32 ColorOf(TileKind kind) =>
            Palette.TryGetValue(kind, out var c) ? c : new Color32(0xFF, 0x00, 0xFF, 0xFF);

        /// <summary>
        /// A translucent hatch drawn over the surface. Hatching rather than a solid fill so
        /// the terrain underneath the mark stays readable.
        /// </summary>
        private static Tile MarkerTile(string name, Color32 color, bool hatched)
        {
            var texture = new Texture2D(PixelsPerTile, PixelsPerTile, TextureFormat.RGBA32, mipChain: false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = name,
            };

            var clear = new Color32(0, 0, 0, 0);
            var pixels = new Color32[PixelsPerTile * PixelsPerTile];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;

            // Border only, plus sparse diagonals. A denser mark would read as terrain and
            // bury the buildings underneath it; this has to say "hollow below" while leaving
            // the ground itself legible.
            for (int y = 0; y < PixelsPerTile; y++)
            for (int x = 0; x < PixelsPerTile; x++)
            {
                bool onEdge = x < 2 || y < 2 || x >= PixelsPerTile - 2 || y >= PixelsPerTile - 2;
                bool onHatch = hatched && (x + y) % 11 == 0;
                if (onEdge || onHatch) pixels[y * PixelsPerTile + x] = color;
            }

            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);

            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = Sprite.Create(
                texture,
                new Rect(0, 0, PixelsPerTile, PixelsPerTile),
                new Vector2(0.5f, 0.5f),
                PixelsPerTile, 0, SpriteMeshType.FullRect);
            tile.colliderType = Tile.ColliderType.None;
            return tile;
        }

        public static Tile TileFor(TileKind kind)
        {
            if (Cache.TryGetValue(kind, out var cached) && cached != null) return cached;

            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = SpriteFor(kind);
            tile.colliderType = Tile.ColliderType.None;
            Cache[kind] = tile;
            return tile;
        }

        public static Sprite SpriteFor(TileKind kind)
        {
            var texture = new Texture2D(PixelsPerTile, PixelsPerTile, TextureFormat.RGBA32, mipChain: false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = $"tile_{kind}",
            };

            var basis = ColorOf(kind);
            var pixels = new Color32[PixelsPerTile * PixelsPerTile];

            for (int y = 0; y < PixelsPerTile; y++)
            for (int x = 0; x < PixelsPerTile; x++)
            {
                // A cheap positional hash gives each pixel a stable speckle, which reads as
                // texture rather than as a flat swatch, without needing a noise asset.
                int h = Hash(x, y, (int)kind);
                int jitter = (h % 17) - 8;

                // Darken one pixel at the tile border so the grid stays legible when zoomed out.
                bool edge = x == 0 || y == 0;
                int shade = edge ? -22 : 0;

                pixels[y * PixelsPerTile + x] = Shift(basis, jitter + shade);
            }

            ApplyMotif(kind, pixels);

            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);

            return Sprite.Create(
                texture,
                new Rect(0, 0, PixelsPerTile, PixelsPerTile),
                new Vector2(0.5f, 0.5f),
                PixelsPerTile,
                0,
                SpriteMeshType.FullRect);
        }

        /// <summary>Adds the small silhouette that tells one tile kind from another at a glance.</summary>
        private static void ApplyMotif(TileKind kind, Color32[] pixels)
        {
            switch (kind)
            {
                case TileKind.Forest:
                    // A pair of conifer blobs.
                    Blob(pixels, 10, 18, 5, new Color32(0x2A, 0x3D, 0x1C, 0xFF));
                    Blob(pixels, 21, 12, 4, new Color32(0x2A, 0x3D, 0x1C, 0xFF));
                    break;
                case TileKind.Rock:
                    Blob(pixels, 16, 16, 7, new Color32(0x55, 0x55, 0x4E, 0xFF));
                    Blob(pixels, 11, 20, 3, new Color32(0x86, 0x86, 0x7C, 0xFF));
                    break;
                case TileKind.ClayDeposit:
                    Blob(pixels, 13, 14, 4, new Color32(0x8A, 0x4F, 0x38, 0xFF));
                    Blob(pixels, 22, 21, 3, new Color32(0x8A, 0x4F, 0x38, 0xFF));
                    break;
                case TileKind.DisusedMine:
                    // A dark mouth with a timber lintel.
                    Rect(pixels, 8, 6, 16, 14, new Color32(0x14, 0x10, 0x0C, 0xFF));
                    Rect(pixels, 6, 20, 20, 3, new Color32(0x6B, 0x53, 0x35, 0xFF));
                    break;
                case TileKind.Water:
                    Rect(pixels, 4, 10, 10, 2, new Color32(0x59, 0x8A, 0xA8, 0xFF));
                    Rect(pixels, 18, 20, 9, 2, new Color32(0x59, 0x8A, 0xA8, 0xFF));
                    break;
                case TileKind.Road:
                    // Wheel ruts running east to west.
                    Rect(pixels, 0, 10, PixelsPerTile, 2, new Color32(0x87, 0x6D, 0x49, 0xFF));
                    Rect(pixels, 0, 20, PixelsPerTile, 2, new Color32(0x87, 0x6D, 0x49, 0xFF));
                    break;
                case TileKind.Aquifer:
                    Rect(pixels, 6, 14, 20, 3, new Color32(0x46, 0x7C, 0x94, 0xFF));
                    break;
                case TileKind.Cavity:
                    // A dug-out chamber needs to read as floor rather than as a hole in the
                    // render, so it gets a swept floor and a lip of loose rubble at the wall.
                    Rect(pixels, 3, 3, PixelsPerTile - 6, PixelsPerTile - 6, new Color32(0x2C, 0x25, 0x1D, 0xFF));
                    Rect(pixels, 3, 3, PixelsPerTile - 6, 2, new Color32(0x3A, 0x31, 0x26, 0xFF));
                    Blob(pixels, 9, 22, 2, new Color32(0x3E, 0x35, 0x2A, 0xFF));
                    Blob(pixels, 23, 11, 2, new Color32(0x3E, 0x35, 0x2A, 0xFF));
                    break;

                case TileKind.Earth:
                    // Bedding planes and the odd pebble. Without them a whole screen of
                    // undug earth is one flat brown rectangle and the eye has nothing to hold.
                    Rect(pixels, 0, 7, PixelsPerTile, 1, new Color32(0x41, 0x36, 0x2A, 0xFF));
                    Rect(pixels, 0, 21, PixelsPerTile, 1, new Color32(0x41, 0x36, 0x2A, 0xFF));
                    Blob(pixels, 12, 15, 2, new Color32(0x57, 0x4A, 0x3A, 0xFF));
                    Blob(pixels, 26, 27, 1, new Color32(0x57, 0x4A, 0x3A, 0xFF));
                    break;

                case TileKind.Bedrock:
                    Blob(pixels, 10, 10, 6, new Color32(0x26, 0x29, 0x2E, 0xFF));
                    Blob(pixels, 24, 22, 5, new Color32(0x36, 0x39, 0x3F, 0xFF));
                    break;
            }
        }

        private static void Blob(Color32[] pixels, int cx, int cy, int radius, Color32 color)
        {
            for (int y = cy - radius; y <= cy + radius; y++)
            for (int x = cx - radius; x <= cx + radius; x++)
            {
                if (x < 0 || y < 0 || x >= PixelsPerTile || y >= PixelsPerTile) continue;
                int dx = x - cx, dy = y - cy;
                if (dx * dx + dy * dy > radius * radius) continue;
                pixels[y * PixelsPerTile + x] = Shift(color, (Hash(x, y, radius) % 11) - 5);
            }
        }

        private static void Rect(Color32[] pixels, int x0, int y0, int w, int h, Color32 color)
        {
            for (int y = y0; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++)
            {
                if (x < 0 || y < 0 || x >= PixelsPerTile || y >= PixelsPerTile) continue;
                pixels[y * PixelsPerTile + x] = Shift(color, (Hash(x, y, w) % 9) - 4);
            }
        }

        private static Color32 Shift(Color32 c, int delta) => new Color32(
            (byte)Mathf.Clamp(c.r + delta, 0, 255),
            (byte)Mathf.Clamp(c.g + delta, 0, 255),
            (byte)Mathf.Clamp(c.b + delta, 0, 255),
            c.a);

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
