using UnityEngine;

namespace Undertown.Game.Presentation
{
    /// <summary>
    /// The isometric projection everything on screen is drawn in: a 2:1 diamond grid, the
    /// standard for this style and what the concept art in docs/target is drawn to.
    ///
    /// The first implementation used a straight top-down square grid. It was easier to write
    /// and it did not look like the game it was supposed to be, which is the only test that
    /// mattered. The simulation never knew the difference - it deals in cell coordinates and
    /// has no opinion about how they are drawn - so the change is confined to this layer.
    /// </summary>
    public static class Iso
    {
        /// <summary>Width of one cell's diamond, in pixels.</summary>
        public const int TileWidth = 64;

        /// <summary>Height of one cell's diamond. Half the width gives the classic 2:1 projection.</summary>
        public const int TileHeight = 32;

        public const int HalfWidth = TileWidth / 2;
        public const int HalfHeight = TileHeight / 2;

        /// <summary>Pixels per world unit. One cell is one unit wide and half a unit tall.</summary>
        public const int PixelsPerUnit = TileWidth;

        public static readonly Vector3 CellSize = new Vector3(1f, 0.5f, 1f);

        /// <summary>
        /// Projects a point in cell space onto texture space, with the y axis running up as
        /// textures do. Fractional inputs are allowed so a shape can be built from the
        /// corners of a footprint rather than cell by cell.
        /// </summary>
        public static Vector2Int Project(float u, float v, int offsetX, int offsetY) =>
            new Vector2Int(
                Mathf.RoundToInt((u - v) * HalfWidth) + offsetX,
                Mathf.RoundToInt(-(u + v) * HalfHeight) + offsetY);

        /// <summary>Texture width needed to hold the diamond footprint of a w by h building.</summary>
        public static int FootprintWidth(int w, int h) => (w + h) * HalfWidth;

        /// <summary>Texture height of that footprint alone, before any vertical structure.</summary>
        public static int FootprintHeight(int w, int h) => (w + h) * HalfHeight;

        /// <summary>
        /// Where the origin cell's centre sits inside a sprite of this footprint, as a pivot
        /// in 0..1. Anchoring there lets a sprite be positioned by its origin cell no matter
        /// how large or tall it is.
        /// </summary>
        public static Vector2 OriginPivot(int w, int h, int totalHeightPx)
        {
            float x = h * HalfWidth + HalfWidth * 0f;
            float y = FootprintHeight(w, h) - HalfHeight;
            return new Vector2(x / FootprintWidth(w, h), y / totalHeightPx);
        }

        /// <summary>
        /// True when a point lies inside the diamond of a single cell whose bounding box is
        /// the given texture size. Used to mask ground tiles to their cell.
        /// </summary>
        public static bool InsideDiamond(int x, int y, int width, int height)
        {
            float dx = Mathf.Abs(x - (width - 1) / 2f) / (width / 2f);
            float dy = Mathf.Abs(y - (height - 1) / 2f) / (height / 2f);
            return dx + dy <= 1f;
        }
    }
}
