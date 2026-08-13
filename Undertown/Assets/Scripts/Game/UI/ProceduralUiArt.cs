using UnityEngine;

namespace Undertown.Game.UI
{
    /// <summary>
    /// Generates the HUD's sprites in code, for the same reason the tiles are generated:
    /// the layout and the readability of the interface have to be evaluatable before any
    /// artist has touched the project.
    /// </summary>
    public static class ProceduralUiArt
    {
        public static readonly Color32 PanelFill = new Color32(0x22, 0x1C, 0x16, 0xF2);
        public static readonly Color32 PanelEdge = new Color32(0x6B, 0x54, 0x35, 0xFF);
        public static readonly Color32 InsetFill = new Color32(0x15, 0x11, 0x0D, 0xFF);
        public static readonly Color32 Ink = new Color32(0xE6, 0xD6, 0xB8, 0xFF);
        public static readonly Color32 InkDim = new Color32(0x9A, 0x8A, 0x72, 0xFF);
        public static readonly Color32 Danger = new Color32(0xC4, 0x3A, 0x33, 0xFF);
        public static readonly Color32 Contraband = new Color32(0xC9, 0x8B, 0x3A, 0xFF);

        private static Sprite _panel;
        private static Sprite _inset;
        private static Sprite _solid;
        private static Sprite _seasonDial;
        private static Sprite _dialHand;
        private static Font _font;

        /// <summary>A bordered plate, sliced so it stretches to any panel size without distorting the edge.</summary>
        public static Sprite Panel => _panel != null ? _panel : _panel = BorderedSprite("ui_panel", PanelFill, PanelEdge, 2);

        /// <summary>A recessed well for things sitting inside a panel, like the ledger table.</summary>
        public static Sprite Inset => _inset != null ? _inset : _inset = BorderedSprite("ui_inset", InsetFill, PanelEdge, 1);

        public static Sprite Solid => _solid != null ? _solid : _solid = SolidSprite("ui_solid");

        public static Font Font =>
            _font != null ? _font : _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        /// <summary>
        /// The season dial: four quadrants for the seasons with the three inspection days
        /// marked on the rim. The player reads "how long until someone comes to count" off
        /// this more often than off any number on screen.
        /// </summary>
        public static Sprite SeasonDial => _seasonDial != null ? _seasonDial : _seasonDial = BuildSeasonDial();

        public static Sprite DialHand => _dialHand != null ? _dialHand : _dialHand = BuildDialHand();

        private static Sprite SolidSprite(string name)
        {
            var tex = NewTexture(name, 4, 4);
            var pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
            tex.SetPixels32(pixels);
            tex.Apply(false);
            return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4, 0, SpriteMeshType.FullRect);
        }

        private static Sprite BorderedSprite(string name, Color32 fill, Color32 edge, int border)
        {
            const int size = 16;
            var tex = NewTexture(name, size, size);
            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool onEdge = x < border || y < border || x >= size - border || y >= size - border;
                pixels[y * size + x] = onEdge ? edge : fill;
            }

            tex.SetPixels32(pixels);
            tex.Apply(false);
            return Sprite.Create(
                tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 16,
                0, SpriteMeshType.FullRect, new Vector4(border + 1, border + 1, border + 1, border + 1));
        }

        private static Sprite BuildSeasonDial()
        {
            const int size = 128;
            const int radius = 60;
            var tex = NewTexture("ui_dial", size, size);
            var pixels = new Color32[size * size];

            var quadrants = new[]
            {
                new Color32(0x4E, 0x6B, 0x3A, 0xFF), // spring
                new Color32(0x8A, 0x7A, 0x33, 0xFF), // summer
                new Color32(0x8A, 0x5A, 0x2E, 0xFF), // autumn
                new Color32(0x46, 0x58, 0x6B, 0xFF), // winter
            };

            int centre = size / 2;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int dx = x - centre, dy = y - centre;
                int distanceSquared = dx * dx + dy * dy;

                if (distanceSquared > radius * radius) { pixels[y * size + x] = new Color32(0, 0, 0, 0); continue; }

                if (distanceSquared > (radius - 4) * (radius - 4)) { pixels[y * size + x] = PanelEdge; continue; }

                // Clockwise from the top, so the hand sweeps the way a clock does.
                float angle = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg;
                if (angle < 0f) angle += 360f;
                int quadrant = Mathf.Clamp((int)(angle / 90f), 0, 3);
                pixels[y * size + x] = quadrants[quadrant];
            }

            // Notch the rim on each inspection day so the hand's approach to one is visible.
            // The notches are drawn thick: at HUD scale the dial is only ~148 px across, and
            // a single-pixel tick disappears entirely.
            foreach (int day in Undertown.Core.Sim.SimClock.InspectionDays)
            {
                float t = (day - 1) / (float)Undertown.Core.Sim.SimClock.DaysPerSeason;
                float radians = t * Mathf.PI * 2f;
                for (int r = radius - 20; r < radius - 3; r++)
                for (int spread = -2; spread <= 2; spread++)
                {
                    int x = centre + Mathf.RoundToInt(Mathf.Sin(radians) * r) + spread;
                    int y = centre + Mathf.RoundToInt(Mathf.Cos(radians) * r);
                    if (x < 0 || y < 0 || x >= size || y >= size) continue;
                    if (pixels[y * size + x].a == 0) continue;
                    pixels[y * size + x] = Danger;
                }
            }

            // A hub so the hand has something to pivot out of.
            for (int y = centre - 6; y <= centre + 6; y++)
            for (int x = centre - 6; x <= centre + 6; x++)
            {
                int dx = x - centre, dy = y - centre;
                if (dx * dx + dy * dy <= 36) pixels[y * size + x] = PanelEdge;
            }

            tex.SetPixels32(pixels);
            tex.Apply(false);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size, 0, SpriteMeshType.FullRect);
        }

        private static Sprite BuildDialHand()
        {
            const int w = 9;
            const int h = 52;
            var tex = NewTexture("ui_hand", w, h);
            var pixels = new Color32[w * h];
            var outline = new Color32(0x1A, 0x14, 0x0E, 0xFF);

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                // Taper towards the tip so the pointing end is unambiguous, and outline the
                // shaft so it stays legible over both the pale and the dark dial quadrants.
                int halfWidth = Mathf.Max(0, 3 - y * 3 / h);
                int distance = Mathf.Abs(x - w / 2);
                pixels[y * w + x] =
                    distance <= halfWidth ? Ink :
                    distance <= halfWidth + 1 ? outline :
                    new Color32(0, 0, 0, 0);
            }

            tex.SetPixels32(pixels);
            tex.Apply(false);

            // Pivot at the base so rotation happens around the dial centre.
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0f), h, 0, SpriteMeshType.FullRect);
        }

        private static Texture2D NewTexture(string name, int w, int h) =>
            new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = name,
            };
    }
}
