using UnityEngine;

namespace Undertown.Game.Presentation
{
    /// <summary>
    /// A cell of kitchen garden: dug beds with a crop growing in them, laid flat over the
    /// ground tile.
    ///
    /// The plots in the reference are not lawns. Every enclosed square within sight of a
    /// house has something growing in it in rows, and those rows are most of what stops the
    /// settled part of the map from reading as buildings dropped onto a field. Grass with a
    /// few tufts on it reads as ground nobody uses, which is the opposite of what a fenced
    /// plot behind a cottage means.
    ///
    /// Drawn as an overlay rather than as a tile kind, because these beds have no meaning in
    /// the simulation - the ground under them stays walkable grass, and pathing, building and
    /// the audit all continue to see exactly what they saw before.
    /// </summary>
    public static class IsoGardenArt
    {
        public const int Variants = 4;

        private static readonly Sprite[] Cache = new Sprite[Variants];

        public static Sprite For(int variant)
        {
            variant = ((variant % Variants) + Variants) % Variants;
            if (Cache[variant] != null) return Cache[variant];
            return Cache[variant] = Build(variant);
        }

        private static Sprite Build(int variant)
        {
            int w = Iso.TileWidth;
            int h = Iso.TileHeight;
            var px = new Color32[w * h];

            var soil = new Color32(0x4E, 0x3C, 0x22, 0xFF);
            var soilLit = new Color32(0x5E, 0x4A, 0x2C, 0xFF);
            var crop = variant == 3
                ? new Color32(0x6A, 0x6E, 0x2E, 0xFF)   // pale, near-ripe
                : new Color32(0x46, 0x5C, 0x28, 0xFF);
            var cropLit = variant == 3
                ? new Color32(0x86, 0x88, 0x3C, 0xFF)
                : new Color32(0x5C, 0x76, 0x34, 0xFF);

            // Ridges run along one of the two grid axes, so the beds sit square to the plot
            // rather than cutting across it at an angle. On screen that axis steps two across
            // for one up, which is why the row index is x/2 + y and not x + y.
            bool alongU = (variant & 1) == 0;
            int spacing = variant == 2 ? 3 : 4;

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!Iso.InsideDiamond(x, y, w, h)) continue;

                int row = alongU ? (x / 2 + y) : (x / 2 - y + h);
                int phase = ((row % spacing) + spacing) % spacing;

                Color32 tone;
                if (phase == 0) tone = soil;
                else if (phase == 1) tone = soilLit;
                else tone = ((x + y * 3) % 5 == 0) ? cropLit : crop;

                px[y * w + x] = tone;
            }

            var sprite = Sprite.Create(MakeTexture(px, w, h), new Rect(0, 0, w, h),
                new Vector2(0.5f, 0.5f), Iso.PixelsPerUnit, 0, SpriteMeshType.FullRect);
            sprite.name = $"iso_garden_{variant}";
            return sprite;
        }

        private static Texture2D MakeTexture(Color32[] px, int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
