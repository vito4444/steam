namespace Undertown.Core.World
{
    /// <summary>
    /// Terrain classes. Surface kinds occupy depth 0; subsurface kinds occupy depth 1 and below.
    /// Persisted in saves, so existing values must keep their numbers.
    /// </summary>
    public enum TileKind : byte
    {
        Grass = 0,
        Dirt = 1,
        Road = 2,
        Water = 3,
        Forest = 4,
        ClayDeposit = 5,
        Rock = 6,
        DisusedMine = 7,

        /// <summary>Undug earth. Diggable, and the default state of everything underground.</summary>
        Earth = 20,

        /// <summary>Dug-out space. Chambers and tunnels are built inside these.</summary>
        Cavity = 21,

        /// <summary>Undiggable. Bounds the underground map and forms natural obstacles.</summary>
        Bedrock = 22,

        /// <summary>Groundwater. Digging into it floods the adjacent cavity.</summary>
        Aquifer = 23,
    }

    public static class Tiles
    {
        public static bool IsSurfaceKind(TileKind kind) => (byte)kind < 20;

        public static bool IsWalkableSurface(TileKind kind) =>
            kind == TileKind.Grass || kind == TileKind.Dirt || kind == TileKind.Road ||
            kind == TileKind.ClayDeposit || kind == TileKind.DisusedMine;

        public static bool IsDiggable(TileKind kind) => kind == TileKind.Earth;

        public static bool IsOpenUnderground(TileKind kind) => kind == TileKind.Cavity;

        /// <summary>
        /// Sounding works by listening for hollowness, so only genuinely empty rock
        /// registers. Everything solid - including a false wall - reads as ordinary earth.
        /// </summary>
        public static bool SoundsHollow(TileKind kind) => kind == TileKind.Cavity;

        /// <summary>Digging cost in worker-ticks per cell.</summary>
        public static int DigCost(TileKind kind)
        {
            switch (kind)
            {
                case TileKind.Earth: return 120;
                case TileKind.Aquifer: return 400;
                default: return int.MaxValue;
            }
        }
    }
}
