namespace Worker.Core
{
    /// <summary>
    /// The game's entire colour vocabulary.
    ///
    /// The palette is organised by value, not by hue: the floor sits dark, structures
    /// sit mid, workers and carried items sit bright. That ordering is what keeps a
    /// dense factory legible at a glance, and it is asserted by
    /// <c>PaletteTests.ValueLayersAreSeparated</c> so a future colour tweak cannot
    /// quietly flatten the image.
    /// </summary>
    public static class Palette
    {
        // Layer 1: ground, darkest.
        public static readonly RgbColor Yard = new RgbColor(28, 31, 38);
        public static readonly RgbColor FloorA = new RgbColor(44, 48, 57);
        public static readonly RgbColor FloorB = new RgbColor(50, 55, 65);
        // Kept deliberately dim: grid lines are the brightest thing in the floor layer,
        // and at the previous value they sat only 16 luminance below the building body,
        // which made the floor compete with the structures standing on it.
        public static readonly RgbColor FloorLine = new RgbColor(56, 61, 72);

        // Layer 2: structures, mid.
        public static readonly RgbColor BuildingBody = new RgbColor(78, 85, 99);
        public static readonly RgbColor BuildingEdge = new RgbColor(24, 27, 33);

        // Per-role accents so a production line can be read without any text.
        public static readonly RgbColor AccentIntake = new RgbColor(96, 132, 168);
        public static readonly RgbColor AccentSaw = new RgbColor(214, 148, 74);
        public static readonly RgbColor AccentLathe = new RgbColor(196, 106, 92);
        public static readonly RgbColor AccentAssembly = new RgbColor(126, 168, 108);
        public static readonly RgbColor AccentShipping = new RgbColor(198, 176, 88);
        public static readonly RgbColor AccentStorage = new RgbColor(112, 120, 138);
        public static readonly RgbColor AccentBreakRoom = new RgbColor(138, 116, 172);

        // Belts sit between floor and structures in value: they are infrastructure the
        // eye should follow, but they must never out-shout the stations they connect.
        // Warm grey rather than blue-grey. Under the dusk key the original blue read as
        // a foreign colour running through an otherwise warm room, and belts cover more
        // floor area than any other single object.
        public static readonly RgbColor ConveyorBed = new RgbColor(78, 76, 74);
        public static readonly RgbColor ConveyorRail = new RgbColor(126, 122, 116);
        public static readonly RgbColor ConveyorArrow = new RgbColor(158, 152, 142);

        // Layer 3: workers, brightest.
        public static readonly RgbColor WorkerBody = new RgbColor(238, 232, 220);
        public static readonly RgbColor WorkerTired = new RgbColor(206, 122, 108);
        public static readonly RgbColor WorkerOutline = new RgbColor(20, 22, 28);

        // Feedback.
        public static readonly RgbColor ProgressFill = new RgbColor(126, 200, 138);
        public static readonly RgbColor ProgressTrack = new RgbColor(32, 36, 44);
        public static readonly RgbColor Warning = new RgbColor(220, 138, 84);
        public static readonly RgbColor PlacementValid = new RgbColor(126, 200, 138);
        public static readonly RgbColor PlacementInvalid = new RgbColor(206, 90, 84);

        // UI chrome.
        public static readonly RgbColor PanelBackground = new RgbColor(22, 25, 31);
        public static readonly RgbColor PanelBorder = new RgbColor(58, 64, 76);
        public static readonly RgbColor TextPrimary = new RgbColor(232, 234, 238);
        public static readonly RgbColor TextSecondary = new RgbColor(148, 156, 170);

        public static RgbColor ForBuilding(BuildingKind kind)
        {
            switch (kind)
            {
                case BuildingKind.Intake: return AccentIntake;
                case BuildingKind.Sawbench: return AccentSaw;
                case BuildingKind.Lathe: return AccentLathe;
                case BuildingKind.AssemblyBench: return AccentAssembly;
                case BuildingKind.Shipping: return AccentShipping;
                case BuildingKind.Storage: return AccentStorage;
                case BuildingKind.BreakRoom: return AccentBreakRoom;
                default: return BuildingBody;
            }
        }

        public static RgbColor ForItem(ItemId item)
        {
            switch (item)
            {
                case ItemId.Log: return new RgbColor(146, 104, 66);
                case ItemId.Plank: return new RgbColor(206, 166, 112);
                case ItemId.ChairLeg: return new RgbColor(182, 148, 104);
                case ItemId.Seat: return new RgbColor(166, 132, 90);
                case ItemId.WoodChair: return new RgbColor(226, 190, 128);
                case ItemId.Fabric: return new RgbColor(158, 122, 168);
                case ItemId.MetalRod: return new RgbColor(148, 158, 174);
                default: return new RgbColor(200, 60, 180);
            }
        }
    }
}
