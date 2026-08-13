using System.Collections.Generic;
using UnityEngine;

namespace Undertown.Game.Presentation
{
    /// <summary>
    /// The edge of the water: foam against the bank, and the boulders the bank is made of.
    ///
    /// Without it a river is a blue shape butted against a green one, and the two look like
    /// two flat colours rather than one thing lying lower than the other. In the reference the
    /// bank is doing all of that work - broken stone standing proud of the water with white
    /// water pulling around it - and it is the only place in the scene with real relief.
    ///
    /// Keyed by which of a cell's four edges face dry land, exactly as the fencing is, so a
    /// whole coastline costs sixteen textures.
    /// </summary>
    public static class IsoShoreArt
    {
        public const int South = 1;
        public const int East = 2;
        public const int North = 4;
        public const int West = 8;

        private const int Width = Iso.TileWidth + 8;
        private const int Height = Iso.TileHeight + 22;
        private const int CentreX = Width / 2;
        private const int CentreY = 6 + Iso.HalfHeight;

        private static readonly Dictionary<int, Sprite> Cache = new Dictionary<int, Sprite>();

        /// <summary>
        /// Differently-arranged banks per edge combination. The last three carry no stone, so
        /// roughly a third of any coastline is bare turf running down to the water. A boulder
        /// on every single cell is as regular as no boulders at all, just noisier.
        /// </summary>
        public const int Variants = 8;

        private const int StonyVariants = 5;

        public static Sprite For(int landMask, int variant)
        {
            landMask &= 0xF;
            if (landMask == 0) return null;

            // One sprite per combination would put identical stones on every cell, and a
            // repeating boulder every sixty-four pixels along a river is more obviously
            // machine-made than no boulders at all.
            variant = ((variant % Variants) + Variants) % Variants;
            int key = landMask * Variants + variant;
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var px = new Color32[Width * Height];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(0, 0, 0, 0);

            int s = variant * 41;
            bool stony = variant < StonyVariants;

            // Far edges first so nearer stone overlaps it.
            if ((landMask & South) != 0) Edge(px, 0f, 0f, 1f, 0f, s + 0, stony);
            if ((landMask & West) != 0) Edge(px, 0f, 1f, 0f, 0f, s + 1, stony);
            if ((landMask & East) != 0) Edge(px, 1f, 0f, 1f, 1f, s + 2, stony);
            if ((landMask & North) != 0) Edge(px, 1f, 1f, 0f, 1f, s + 3, stony);

            return Cache[key] = ToSprite(px, $"iso_shore_{landMask:X}_{variant}");
        }

        private static void Edge(Color32[] px, float u0, float v0, float u1, float v1, int seed,
            bool stony)
        {
            var foam = new Color32(0xB6, 0xD2, 0xDE, 0xFF);
            var foamSoft = new Color32(0x84, 0xB0, 0xC6, 0xFF);

            const int steps = 64;
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float u = Mathf.Lerp(u0, u1, t);
                float v = Mathf.Lerp(v0, v1, t);
                Project(u, v, out int x, out int y);

                // Broken rather than continuous: a solid line of white would draw the cell
                // boundary, which is the one thing the water must not show.
                int n = Noise(i, seed);
                if (n % 5 == 0) continue;
                Plot(px, x, y, n % 3 == 0 ? foam : foamSoft);
                if (n % 7 == 0) Plot(px, x, y - 1, foamSoft);
            }

            // One or two boulders, unevenly spaced and unevenly sized. Three regular ones per
            // edge produced a kerb of matched cobbles running the length of the river, which
            // is a wall, not a bank.
            int count = stony ? 1 + Noise(seed * 31, 7) % 2 : 0;
            for (int b = 0; b < count; b++)
            {
                float t = 0.18f + (Noise(b * 13, seed) % 64) / 100f + b * 0.34f;
                if (t > 0.92f) continue;

                float u = Mathf.Lerp(u0, u1, t);
                float v = Mathf.Lerp(v0, v1, t);
                Project(u, v, out int x, out int y);
                Boulder(px, x, y, 3 + Noise(b, seed + 5) % 6, Noise(b, seed + 9));
            }
        }

        private static void Boulder(Color32[] px, int cx, int cy, int radius, int seed)
        {
            var lit = new Color32(0x7E, 0x80, 0x7A, 0xFF);
            var face = new Color32(0x5E, 0x60, 0x5C, 0xFF);
            var dark = new Color32(0x3A, 0x3C, 0x3C, 0xFF);
            var moss = new Color32(0x4E, 0x60, 0x3C, 0xFF);

            int height = radius + 3;
            for (int dy = -radius; dy <= height; dy++)
            for (int dx = -radius - 2; dx <= radius + 2; dx++)
            {
                // Squat, and knocked out of round: stone that fractures does not come in
                // ellipses, and a row of ellipses is what gives away that they were generated.
                float fx = dx / (float)(radius + 1);
                float fy = (dy - height * 0.25f) / (float)(radius + 2);
                float bite = 0.82f + (Noise(dx * 5 + dy, seed) % 36) / 100f;
                if (fx * fx + fy * fy > bite) continue;

                var tone = dx < -radius / 3 ? lit : dx > radius / 2 ? dark : face;
                if (Noise(dx + dy * 7, seed + 3) % 9 == 0) tone = Shade(tone, -14);
                if (dy > height - 3 && Noise(dx, seed) % 3 == 0) tone = moss;
                Plot(px, cx + dx, cy + dy, tone);
            }
        }

        private static Color32 Shade(Color32 c, int delta) => new Color32(
            (byte)Mathf.Clamp(c.r + delta, 0, 255),
            (byte)Mathf.Clamp(c.g + delta, 0, 255),
            (byte)Mathf.Clamp(c.b + delta, 0, 255), c.a);

        private static void Project(float u, float v, out int x, out int y)
        {
            x = CentreX + Mathf.RoundToInt(((u - 0.5f) - (v - 0.5f)) * Iso.HalfWidth);
            y = CentreY - Mathf.RoundToInt(((u - 0.5f) + (v - 0.5f)) * Iso.HalfHeight);
        }

        private static int Noise(int i, int seed)
        {
            int h = i * 73856093 ^ (seed + 1) * 19349663;
            h ^= h >> 13;
            return Mathf.Abs(h) % 1024;
        }

        private static void Plot(Color32[] px, int x, int y, Color32 color)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) return;
            px[y * Width + x] = color;
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

            return Sprite.Create(
                texture, new Rect(0, 0, Width, Height),
                new Vector2(CentreX / (float)Width, CentreY / (float)Height),
                Iso.PixelsPerUnit, 0, SpriteMeshType.FullRect);
        }
    }
}
