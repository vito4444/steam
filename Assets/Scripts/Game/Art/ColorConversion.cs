using UnityEngine;
using Worker.Core;

namespace Worker.Game
{
    /// <summary>
    /// Bridges the engine-free <see cref="RgbColor"/> used by <see cref="Palette"/> into
    /// Unity colours. The palette itself deliberately lives in Worker.Core so the headless
    /// preview renderer and the Unity renderer cannot drift apart.
    /// </summary>
    public static class ColorConversion
    {
        public static Color ToUnity(this RgbColor color) => new Color32(color.R, color.G, color.B, color.A);

        public static Color ForBuilding(BuildingKind kind) => Palette.ForBuilding(kind).ToUnity();

        public static Color ForItem(ItemId item) => Palette.ForItem(item).ToUnity();
    }
}
