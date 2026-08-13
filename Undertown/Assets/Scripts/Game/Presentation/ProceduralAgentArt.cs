using System.Collections.Generic;
using UnityEngine;

namespace Undertown.Game.Presentation
{
    /// <summary>
    /// Small top-down figures generated at runtime. At 32 pixels per tile a person is a
    /// head, shoulders and a coat colour; that is enough to count how many are in a yard and
    /// to spot the one in red, which is all the current build asks of them.
    /// </summary>
    public static class ProceduralAgentArt
    {
        private const int Size = ProceduralTileArt.PixelsPerTile;

        private static readonly Dictionary<int, Sprite> Cache = new Dictionary<int, Sprite>();

        public static Sprite Person(Color32 coat, Color32 trim, bool tall = false)
        {
            int key = (coat.r << 24) ^ (coat.g << 16) ^ (coat.b << 8) ^ (trim.r << 4) ^ trim.g ^ (tall ? 1 : 0);
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var pixels = new Color32[Size * Size];
            var clear = new Color32(0, 0, 0, 0);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;

            var skin = new Color32(0xD9, 0xB0, 0x88, 0xFF);
            var shadow = new Color32(0x18, 0x14, 0x10, 0x77);
            var outline = new Color32(0x14, 0x10, 0x0C, 0xFF);

            int cx = Size / 2;
            int bodyBottom = tall ? 8 : 9;
            int bodyTop = tall ? 20 : 19;

            // Contact shadow, so the figure sits on the ground instead of floating over it.
            Ellipse(pixels, cx, bodyBottom - 1, 6, 3, shadow);

            // Coat.
            Rect(pixels, cx - 4, bodyBottom, 8, bodyTop - bodyBottom, coat);
            Rect(pixels, cx - 5, bodyBottom + 2, 1, bodyTop - bodyBottom - 4, coat);
            Rect(pixels, cx + 4, bodyBottom + 2, 1, bodyTop - bodyBottom - 4, coat);

            // Trim across the shoulders picks out a rank or a mood without another sprite.
            Rect(pixels, cx - 4, bodyTop - 3, 8, 2, trim);

            // Head.
            Ellipse(pixels, cx, bodyTop + 3, 4, 4, skin);

            Outline(pixels, outline);

            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, mipChain: false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "agent",
            };
            texture.SetPixels32(pixels);
            texture.Apply(false);

            var sprite = Sprite.Create(
                texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), Size, 0, SpriteMeshType.FullRect);
            Cache[key] = sprite;
            return sprite;
        }

        private static void Rect(Color32[] px, int x0, int y0, int w, int h, Color32 color)
        {
            for (int y = y0; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++)
            {
                if (x < 0 || y < 0 || x >= Size || y >= Size) continue;
                px[y * Size + x] = color;
            }
        }

        private static void Ellipse(Color32[] px, int cx, int cy, int rx, int ry, Color32 color)
        {
            for (int y = cy - ry; y <= cy + ry; y++)
            for (int x = cx - rx; x <= cx + rx; x++)
            {
                if (x < 0 || y < 0 || x >= Size || y >= Size) continue;
                int dx = x - cx, dy = y - cy;
                if (dx * dx * ry * ry + dy * dy * rx * rx > rx * rx * ry * ry) continue;
                px[y * Size + x] = color;
            }
        }

        /// <summary>Traces a dark edge around whatever has been drawn, so figures read against any tile.</summary>
        private static void Outline(Color32[] px, Color32 outline)
        {
            var original = (Color32[])px.Clone();
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                if (original[y * Size + x].a > 8) continue;

                bool touchesFigure = false;
                for (int dy = -1; dy <= 1 && !touchesFigure; dy++)
                for (int dx = -1; dx <= 1 && !touchesFigure; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= Size || ny >= Size) continue;
                    touchesFigure = original[ny * Size + nx].a > 200;
                }

                if (touchesFigure) px[y * Size + x] = outline;
            }
        }
    }
}
