namespace Undertown.Core.Economy
{
    /// <summary>
    /// Every tradeable or accountable substance in the town. Values are persisted in
    /// saves, so existing entries must keep their numbers.
    /// </summary>
    public enum MaterialId : byte
    {
        None = 0,
        Timber = 1,
        Clay = 2,
        Grain = 3,
        Spoil = 4,
        Brick = 5,
        Ale = 6,
        Moonshine = 7,
    }

    public static class Materials
    {
        public static readonly MaterialId[] All =
        {
            MaterialId.Timber,
            MaterialId.Clay,
            MaterialId.Grain,
            MaterialId.Spoil,
            MaterialId.Brick,
            MaterialId.Ale,
            MaterialId.Moonshine,
        };

        public const int Count = 8;

        /// <summary>Contraband draws far harsher scrutiny than ordinary stock.</summary>
        public static bool IsContraband(MaterialId id) => id == MaterialId.Moonshine;

        /// <summary>
        /// Spoil is dirt from digging. It never appears on a tax return because nobody
        /// buys or sells it, so the auditor does not balance its books - but a visible
        /// pile of it on the surface is its own kind of evidence.
        /// </summary>
        public static bool IsAudited(MaterialId id) => id != MaterialId.None && id != MaterialId.Spoil;

        public static string DisplayName(MaterialId id)
        {
            switch (id)
            {
                case MaterialId.Timber: return "Timber";
                case MaterialId.Clay: return "Clay";
                case MaterialId.Grain: return "Grain";
                case MaterialId.Spoil: return "Spoil";
                case MaterialId.Brick: return "Brick";
                case MaterialId.Ale: return "Ale";
                case MaterialId.Moonshine: return "Moonshine";
                default: return "None";
            }
        }
    }
}
