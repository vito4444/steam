using System.Collections.Generic;
using UnityEngine;

namespace Undertown.Game.Presentation
{
    /// <summary>
    /// The ragged margin where turf meets trodden earth.
    ///
    /// Two flat colours meeting along a cell boundary draw that boundary, and a map's worth of
    /// them draws the whole grid: the settlement came out looking tiled, with every yard a
    /// clean diamond stamped into the pasture. Ground does not end where a surveyor says it
    /// does. Grass creeps into the edge of a yard and gets worn back, and earth is carried out
    /// onto the verge on boots and wheels.
    ///
    /// Both sides of a boundary draw their own margin, so the two interleave and the join ends
    /// up several pixels wide and different everywhere along its length. Scatter only - the
    /// simulation's idea of what a cell is never changes.
    /// </summary>
    public static class IsoVergeArt
    {
        public const int Variants = 4;

        private const int Width = Iso.TileWidth + 8;
        private const int Height = Iso.TileHeight + 12;
        private const int CentreX = Width / 2;
        private const int CentreY = 6 + Iso.HalfHeight;

        private static readonly Dictionary<int, Sprite> Cache = new Dictionary<int, Sprite>();

        /// <summary>
        /// <paramref name="edges"/> uses the same bits as <see cref="IsoShoreArt"/>. Set
        /// <paramref name="onGrass"/> when the cell being drawn is turf, which is what decides
        /// whether earth is being carried in or grass is growing out.
        /// </summary>
        public static Sprite For(int edges, bool onGrass, int variant)
        {
            edges &= 0xF;
            if (edges == 0) return null;

            variant = ((variant % Variants) + Variants) % Variants;
            int key = (edges * Variants + variant) * 2 + (onGrass ? 1 : 0);
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var px = new Color32[Width * Height];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(0, 0, 0, 0);

            int seed = variant * 37 + (onGrass ? 11 : 0);

            if ((edges & IsoShoreArt.South) != 0) Edge(px, 0f, 0f, 1f, 0f, seed + 0, onGrass);
            if ((edges & IsoShoreArt.West) != 0) Edge(px, 0f, 1f, 0f, 0f, seed + 1, onGrass);
            if ((edges & IsoShoreArt.East) != 0) Edge(px, 1f, 0f, 1f, 1f, seed + 2, onGrass);
            if ((edges & IsoShoreArt.North) != 0) Edge(px, 1f, 1f, 0f, 1f, seed + 3, onGrass);

            return Cache[key] = ToSprite(px, $"iso_verge_{edges:X}_{(onGrass ? 'g' : 'e')}_{variant}");
        }

        /// <summary>
        /// The shadow an excavated cell takes from the rock standing along the given edges.
        ///
        /// Shading every cell darker at its own rim drew the grid across the workings, because
        /// a cell in the middle of a chamber has no wall to cast anything. Only the boundaries
        /// with rock behind them get a shadow, so a chamber comes out as one lit floor inside a
        /// dark rim and a tunnel as a corridor.
        /// </summary>
        public static Sprite ForCavity(int edges, int variant)
        {
            edges &= 0xF;
            if (edges == 0) return null;

            variant = ((variant % Variants) + Variants) % Variants;
            int key = 0x10000 + edges * Variants + variant;
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var px = new Color32[Width * Height];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(0, 0, 0, 0);

            int seed = variant * 53;
            if ((edges & IsoShoreArt.South) != 0) Wall(px, 0f, 0f, 1f, 0f, seed + 0);
            if ((edges & IsoShoreArt.West) != 0) Wall(px, 0f, 1f, 0f, 0f, seed + 1);
            if ((edges & IsoShoreArt.East) != 0) Wall(px, 1f, 0f, 1f, 1f, seed + 2);
            if ((edges & IsoShoreArt.North) != 0) Wall(px, 1f, 1f, 0f, 1f, seed + 3);

            return Cache[key] = ToSprite(px, $"iso_cavitywall_{edges:X}_{variant}");
        }

        private static void Wall(Color32[] px, float u0, float v0, float u1, float v1, int seed)
        {
            Project((u0 + u1) * 0.5f, (v0 + v1) * 0.5f, out int mx, out int my);
            float sx = CentreX - mx;
            float sy = CentreY - my;
            float mag = Mathf.Max(0.001f, Mathf.Sqrt(sx * sx + sy * sy));
            sx /= mag;
            sy /= mag;

            const int steps = 110;
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                Project(Mathf.Lerp(u0, u1, t), Mathf.Lerp(v0, v1, t), out int x, out int y);

                int n = Noise(i, seed);
                int reach = 5 + n % 3;
                for (int d = 0; d < reach; d++)
                {
                    // Densest against the rock and gone within a few pixels, and stippled so
                    // that the far edge of the shadow is not a second line beside the first.
                    int fade = 150 - d * 26;
                    if (d > 1 && Noise(i * 5 + d, seed + 7) % 6 < d) continue;
                    Plot(px, x + Mathf.RoundToInt(sx * d), y + Mathf.RoundToInt(sy * d),
                        new Color32(0x10, 0x0C, 0x08, (byte)Mathf.Clamp(fade, 0, 255)));
                }
            }
        }

        private static void Edge(Color32[] px, float u0, float v0, float u1, float v1, int seed,
            bool onGrass)
        {
            // Earth carried onto turf, or blades coming up through the edge of a yard.
            var near = onGrass
                ? new Color32(0x6C, 0x53, 0x30, 0xFF)
                : new Color32(0x4C, 0x52, 0x27, 0xFF);
            var far = onGrass
                ? new Color32(0x60, 0x4C, 0x2E, 0xFF)
                : new Color32(0x42, 0x48, 0x22, 0xFF);
            var bright = onGrass
                ? new Color32(0x7C, 0x61, 0x38, 0xFF)
                : new Color32(0x5C, 0x64, 0x2E, 0xFF);

            // Inwards from this edge, in pixels. Both edges of a diamond face a screen
            // diagonal, so this is a unit step along one.
            Project((u0 + u1) * 0.5f, (v0 + v1) * 0.5f, out int mx, out int my);
            float sx = CentreX - mx;
            float sy = CentreY - my;
            float mag = Mathf.Max(0.001f, Mathf.Sqrt(sx * sx + sy * sy));
            sx /= mag;
            sy /= mag;

            const int steps = 110;
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                Project(Mathf.Lerp(u0, u1, t), Mathf.Lerp(v0, v1, t), out int x, out int y);

                // Thinner on the worn side. Earth blown onto turf covers it; grass coming up
                // through a yard is trodden back to a few blades, and drawn as thick as the
                // other way round it reads as a green stripe painted round every plot.
                int n = Noise(i, seed);
                if (n % (onGrass ? 3 : 2) == 0) continue;

                // Deepest in patches rather than at an even width, so the margin has bays and
                // headlands in it instead of being a stripe of its own.
                int reach = (n % 11) < 6 ? 1 + n % 3 : 3 + n % 5;
                for (int d = 0; d < reach; d++)
                {
                    if (Noise(i * 7 + d, seed + 3) % 5 == 0) continue;

                    var tone = d == 0 ? near : d < reach - 1 ? far : bright;
                    Plot(px, x + Mathf.RoundToInt(sx * d), y + Mathf.RoundToInt(sy * d), tone);
                }
            }
        }

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
