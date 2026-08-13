using System.Collections.Generic;
using UnityEngine;

namespace Undertown.Game.Presentation
{
    /// <summary>
    /// Post-and-rail fencing drawn along the edges of a cell.
    ///
    /// This is the strongest single structural feature of the reference art and the one that
    /// was missing entirely. A settlement there is not buildings on open ground: it is a grid
    /// of lanes with every block between them fenced off, so the eye reads plots first and
    /// contents second. Without the fences the same buildings on the same ground look like
    /// objects that happen to be near each other.
    ///
    /// A cell's fencing is baked as one sprite keyed by which of its four edges are fenced,
    /// so a run of fence along a lane costs one sprite per cell and sixteen textures in total.
    /// </summary>
    public static class IsoFenceArt
    {
        /// <summary>Edge towards the cell at y-1, drawn up and to the right.</summary>
        public const int South = 1;

        /// <summary>Edge towards the cell at x+1, drawn down and to the right.</summary>
        public const int East = 2;

        /// <summary>Edge towards the cell at y+1, drawn down and to the left.</summary>
        public const int North = 4;

        /// <summary>Edge towards the cell at x-1, drawn up and to the left.</summary>
        public const int West = 8;

        private const int Width = Iso.TileWidth + 8;
        private const int Height = Iso.TileHeight + 20;

        /// <summary>Where the cell's centre sits inside the texture.</summary>
        private const int CentreX = Width / 2;
        private const int CentreY = 8 + Iso.HalfHeight;

        private static readonly Dictionary<int, Sprite> Cache = new Dictionary<int, Sprite>();

        public static Sprite For(int edgeMask)
        {
            edgeMask &= 0xF;
            if (edgeMask == 0) return null;
            if (Cache.TryGetValue(edgeMask, out var cached) && cached != null) return cached;

            var px = new Color32[Width * Height];
            var clear = new Color32(0, 0, 0, 0);
            for (int i = 0; i < px.Length; i++) px[i] = clear;

            // Far edges first, near edges over them, so a corner post reads as one join.
            if ((edgeMask & South) != 0) Run(px, 0f, 0f, 1f, 0f);
            if ((edgeMask & West) != 0) Run(px, 0f, 1f, 0f, 0f);
            if ((edgeMask & East) != 0) Run(px, 1f, 0f, 1f, 1f);
            if ((edgeMask & North) != 0) Run(px, 1f, 1f, 0f, 1f);

            return Cache[edgeMask] = ToSprite(px, $"iso_fence_{edgeMask:X}");
        }

        /// <summary>
        /// One straight run between two corners of the cell, given in cell-local coordinates
        /// where 0,0 is the corner drawn at the top of the diamond.
        /// </summary>
        private static void Run(Color32[] px, float u0, float v0, float u1, float v1)
        {
            var post = new Color32(0x4E, 0x38, 0x22, 0xFF);
            var postLit = new Color32(0x74, 0x58, 0x36, 0xFF);
            var rail = new Color32(0x63, 0x4A, 0x2C, 0xFF);
            var railLit = new Color32(0x86, 0x68, 0x42, 0xFF);
            var shadow = new Color32(0x18, 0x14, 0x0C, 0x40);

            // Low. A cell is thirty-two pixels tall on screen and a fence that reaches half of
            // that stands taller than it would in life, competing with the buildings and
            // burying the bottom of every wall behind it.
            const int steps = 96;
            const int postHeight = 11;
            const int railHeight = 8;

            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float u = Mathf.Lerp(u0, u1, t);
                float v = Mathf.Lerp(v0, v1, t);

                int x = CentreX + Mathf.RoundToInt(((u - 0.5f) - (v - 0.5f)) * Iso.HalfWidth);
                int y = CentreY - Mathf.RoundToInt(((u - 0.5f) + (v - 0.5f)) * Iso.HalfHeight);

                Blend(px, x + 2, y - 1, shadow);

                // Two rails, the upper one catching the light.
                Plot(px, x, y + railHeight, railLit);
                Plot(px, x, y + railHeight - 1, rail);
                Plot(px, x, y + railHeight - 4, rail);

                // A post at each end and a couple in between.
                if (i % (steps / 3) != 0 && i != steps) continue;
                for (int lift = 0; lift < postHeight; lift++)
                {
                    Plot(px, x, y + lift, postLit);
                    Plot(px, x + 1, y + lift, post);
                }
            }
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
