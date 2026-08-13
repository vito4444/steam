using System.Collections.Generic;
using UnityEngine;
using Undertown.Core.Buildings;

namespace Undertown.Game.Presentation
{
    /// <summary>
    /// Buildings drawn as solids: two lit wall faces, a gable end, a pitched roof with eaves
    /// that overhang, and a shadow on the ground.
    ///
    /// The pitch is the part that has to be got right. A roof's ridge is drawn higher up the
    /// screen than its eaves by the height of the pitch, but the far eaves are already higher
    /// than the near ones by half the building's depth in cells. If the pitch is not taller
    /// than that, the ridge lands below the far eaves and both slopes read as one flat plane
    /// tilted the wrong way - which is what the first version did, and why every roof looked
    /// like a lean-to. The pitch therefore scales with the footprint rather than being a fixed
    /// number of pixels.
    /// </summary>
    public static class IsoBuildingArt
    {
        private static readonly Dictionary<int, Sprite> Cache = new Dictionary<int, Sprite>();

        /// <summary>How many distinct looks a repeated building has.</summary>
        public const int VariantCount = 4;

        /// <summary>How far the eaves stand out past the walls, in cells.</summary>
        private const float Overhang = 0.22f;

        private struct Scheme
        {
            public Color32 Wall;
            public Color32 Roof;
            public Color32 Timber;
            public Color32 Plinth;

            /// <summary>Wall height in pixels, eaves to plinth.</summary>
            public int WallHeight;

            /// <summary>Extra pixels the ridge rises above the far eaves, on top of the
            /// minimum the footprint already forces.</summary>
            public int Pitch;

            public bool Thatch;
            public bool HalfTimbered;
            public bool Chimney;
        }

        /// <summary>
        /// A building's sprite. The variant only has an effect on kinds a town has several of;
        /// eight identical cottages in a row is the single clearest tell that a settlement was
        /// generated rather than built, and it costs nothing to vary the render and the thatch.
        /// </summary>
        public static Sprite For(BuildingKind kind, int variant = 0)
        {
            variant = ((variant % VariantCount) + VariantCount) % VariantCount;
            int key = (int)kind * 16 + variant;
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
            return Cache[key] = Build(kind, variant);
        }

        private static Sprite Build(BuildingKind kind, int variant)
        {
            var def = BuildingCatalog.Get(kind);
            int cw = Mathf.Max(1, def?.Width ?? 1);
            int ch = Mathf.Max(1, def?.Height ?? 1);

            var scheme = SchemeFor(kind, variant);
            bool ridgeAlongX = cw >= ch;

            // Depth across the ridge, in cells: what the pitch has to beat.
            int across = ridgeAlongX ? ch : cw;
            int peak = across * Iso.HalfHeight / 2 + scheme.Pitch;

            int marginX = Mathf.CeilToInt(Overhang * Iso.TileWidth) + 4;
            int footW = Iso.FootprintWidth(cw, ch) + marginX * 2;
            int footH = Iso.FootprintHeight(cw, ch);
            int total = footH + scheme.WallHeight + peak + marginX + 12;

            var px = Blank(footW, total);
            int ox = ch * Iso.HalfWidth + marginX;
            int oy = footH;

            if (def != null && def.Underground)
                PaintUnderground(px, footW, total, cw, ch, ox, oy, kind);
            else if (IsOpenGround(kind))
                PaintYard(px, footW, total, cw, ch, ox, oy, kind);
            else
                PaintSolid(px, footW, total, cw, ch, ox, oy, scheme, kind, ridgeAlongX, peak);

            var pivot = new Vector2(
                (ch * Iso.HalfWidth + marginX) / (float)footW,
                (footH - Iso.HalfHeight) / (float)total);

            return ToSprite(px, footW, total, $"iso_{kind}_{variant}", pivot);
        }

        private static bool IsOpenGround(BuildingKind kind) =>
            kind == BuildingKind.Field || kind == BuildingKind.ClayPit ||
            kind == BuildingKind.Sawpit || kind == BuildingKind.Tunnel;

        private static void PaintSolid(Color32[] px, int w, int h, int cw, int ch, int ox, int oy,
            Scheme scheme, BuildingKind kind, bool ridgeAlongX, int peak)
        {
            Shadow(px, w, h, cw, ch, ox, oy);
            Walls(px, w, h, cw, ch, ox, oy, scheme, ridgeAlongX, peak);
            Openings(px, w, h, cw, ch, ox, oy, scheme, ridgeAlongX);
            Brackets(px, w, h, cw, ch, ox, oy, scheme);
            Roof(px, w, h, cw, ch, ox, oy, scheme, ridgeAlongX, peak);
            if (scheme.Chimney) Chimney(px, w, h, cw, ch, ox, oy, scheme, peak);

            // Landmarks last, over the roof they stand on.
            switch (kind)
            {
                case BuildingKind.TownHall: BellTower(px, w, h, cw, ch, ox, oy, scheme, peak); break;
                case BuildingKind.Warehouse: LoadingDoor(px, w, h, cw, ch, ox, oy, scheme, peak); break;
            }
        }

        /// <summary>
        /// The two faces the viewer can see, extruded from the footprint's near edges, plus the
        /// gable triangle on whichever of them is an end wall.
        /// </summary>
        private static void Walls(Color32[] px, int w, int h, int cw, int ch, int ox, int oy,
            Scheme scheme, bool ridgeAlongX, int peak)
        {
            var left = scheme.Wall;
            var right = Darken(scheme.Wall, 30);
            int wallTop = scheme.WallHeight;
            int steps = Mathf.Max(cw, ch) * 96;

            // South-facing wall, the edge from the west corner to the south corner.
            for (int i = 0; i <= steps; i++)
            {
                float u = i / (float)steps * cw;
                var p = Iso.Project(u, ch, ox, oy);
                int gable = ridgeAlongX ? 0 : GableAt(u / cw, peak);
                Column(px, w, h, p.x, p.y, wallTop + gable, left, scheme, u * Iso.TileWidth);
            }

            // East-facing wall, from the south corner to the east corner.
            for (int i = 0; i <= steps; i++)
            {
                float v = (1f - i / (float)steps) * ch;
                var p = Iso.Project(cw, v, ox, oy);
                int gable = ridgeAlongX ? GableAt(v / ch, peak) : 0;
                Column(px, w, h, p.x, p.y, wallTop + gable, right, scheme, v * Iso.TileWidth);
            }
        }

        /// <summary>Height of the gable triangle at a fraction across the end wall.</summary>
        private static int GableAt(float acrossFraction, int peak) =>
            Mathf.RoundToInt((1f - Mathf.Abs(acrossFraction - 0.5f) * 2f) * peak);

        /// <summary>
        /// One vertical strip of wall: plinth at the bottom, render above it, a timber post
        /// every so often, and a band of shade under the eaves.
        /// </summary>
        private static void Column(Color32[] px, int w, int h, int x, int y, int top,
            Color32 baseColor, Scheme scheme, float alongWallPx)
        {
            bool post = scheme.HalfTimbered && ((int)alongWallPx % 22) < 3;

            for (int lift = 0; lift < top; lift++)
            {
                Color32 tone;
                if (lift < 4) tone = scheme.Plinth;
                else if (post) tone = scheme.Timber;
                else tone = Shade(baseColor, lift, top);

                // Under the eaves the wall falls into shadow, which is what separates a roof
                // from the wall it sits on.
                if (lift > top - 4) tone = Darken(tone, 26);
                Plot(px, w, h, x, y + lift, tone);
            }
        }

        /// <summary>Two slopes meeting at a ridge, with eaves standing out past the walls.</summary>
        private static void Roof(Color32[] px, int w, int h, int cw, int ch, int ox, int oy,
            Scheme scheme, bool ridgeAlongX, int peak)
        {
            var lit = Lighten(scheme.Roof, 26);
            var dark = Darken(scheme.Roof, 34);
            var ridge = Lighten(scheme.Roof, 48);
            var edge = Darken(scheme.Roof, 52);

            float u0 = -Overhang, u1 = cw + Overhang;
            float v0 = -Overhang, v1 = ch + Overhang;
            int steps = Mathf.Max(cw, ch) * 150;

            for (int j = 0; j <= steps; j++)
            for (int i = 0; i <= steps; i++)
            {
                float u = Mathf.Lerp(u0, u1, i / (float)steps);
                float v = Mathf.Lerp(v0, v1, j / (float)steps);

                float across = ridgeAlongX
                    ? Mathf.InverseLerp(v0, v1, v)
                    : Mathf.InverseLerp(u0, u1, u);
                float fromRidge = Mathf.Abs(across - 0.5f) * 2f;

                int lift = scheme.WallHeight + Mathf.RoundToInt((1f - fromRidge) * peak);
                var p = Iso.Project(u, v, ox, oy);

                bool sunSide = across < 0.5f;
                var tone = sunSide ? lit : dark;

                if (scheme.Thatch)
                {
                    int clump = (Mathf.RoundToInt(u * 40f) * 7 + Mathf.RoundToInt(v * 40f) * 13) % 11;
                    if (clump < 3) tone = Darken(tone, 12);
                    else if (clump > 8) tone = Lighten(tone, 10);
                }
                else
                {
                    // Courses of tile ruled across the slope, parallel to the ridge.
                    int course = Mathf.RoundToInt(fromRidge * peak * 2f);
                    if (course % 5 == 0) tone = Darken(tone, 18);
                }

                if (fromRidge > 0.985f) tone = edge;
                if (fromRidge < 0.035f) tone = ridge;

                Plot(px, w, h, p.x, p.y + lift, tone);
            }
        }

        private static void Openings(Color32[] px, int w, int h, int cw, int ch, int ox, int oy,
            Scheme scheme, bool ridgeAlongX)
        {
            var frame = new Color32(0x2A, 0x1E, 0x14, 0xFF);
            var glow = new Color32(0xE8, 0xBA, 0x58, 0xFF);
            var glowDim = new Color32(0xB8, 0x8E, 0x3E, 0xFF);
            int wallTop = scheme.WallHeight;

            // A door on the south wall, arched, with a stone threshold.
            var door = Iso.Project(cw * 0.5f, ch, ox, oy);
            int doorTop = Mathf.Min(wallTop - 5, 20);
            for (int lift = 0; lift < doorTop; lift++)
            {
                int half = lift > doorTop - 4 ? 3 : 5;
                for (int dx = -half; dx <= half; dx++)
                    Plot(px, w, h, door.x + dx, door.y + lift, lift < 2 ? scheme.Plinth : frame);
            }

            // Windows along both visible walls, set below the eaves.
            for (int i = 0; i < cw; i++)
            {
                var p = Iso.Project(i + 0.5f, ch, ox, oy);
                if (Mathf.Abs(p.x - door.x) < 10) continue;
                Window(px, w, h, p.x, p.y, wallTop, frame, glow);
            }

            for (int i = 0; i < ch; i++)
            {
                var p = Iso.Project(cw, i + 0.5f, ox, oy);
                Window(px, w, h, p.x, p.y, wallTop, frame, glowDim);
            }
        }

        private static void Window(Color32[] px, int w, int h, int x, int y, int wallTop,
            Color32 frame, Color32 glow)
        {
            int top = wallTop - 6;
            int bottom = Mathf.Max(6, top - 9);

            for (int lift = bottom - 1; lift <= top + 1; lift++)
            for (int dx = -4; dx <= 4; dx++)
            {
                bool border = lift == bottom - 1 || lift == top + 1 || dx == -4 || dx == 4;
                Plot(px, w, h, x + dx, y + lift, border ? frame : glow);
            }

            // A mullion, which is what stops a window reading as a glowing sticker.
            for (int lift = bottom; lift <= top; lift++) Plot(px, w, h, x, y + lift, frame);
        }

        /// <summary>
        /// Timber brackets under the eaves, spaced along both visible walls. Small, but the
        /// eaves line is the longest single edge on a building and an unbroken one reads as a
        /// cut-out rather than as carpentry.
        /// </summary>
        private static void Brackets(Color32[] px, int w, int h, int cw, int ch, int ox, int oy,
            Scheme scheme)
        {
            var timber = scheme.Timber;
            var lit = Lighten(scheme.Timber, 22);
            int top = scheme.WallHeight;

            for (int i = 0; i < cw * 2; i++)
            {
                var p = Iso.Project((i + 0.5f) / 2f, ch, ox, oy);
                for (int d = 0; d < 5; d++)
                    for (int dx = 0; dx <= d / 2; dx++)
                        Plot(px, w, h, p.x + dx, p.y + top - 1 - d, dx == 0 ? lit : timber);
            }

            for (int i = 0; i < ch * 2; i++)
            {
                var p = Iso.Project(cw, (i + 0.5f) / 2f, ox, oy);
                for (int d = 0; d < 5; d++)
                    for (int dx = 0; dx <= d / 2; dx++)
                        Plot(px, w, h, p.x - dx, p.y + top - 1 - d, timber);
            }
        }

        /// <summary>
        /// A bell turret on the hall's ridge. The town needs one thing taller than its roofs,
        /// or the eye has nowhere to land and the settlement reads as an even field of sheds.
        /// </summary>
        private static void BellTower(Color32[] px, int w, int h, int cw, int ch, int ox, int oy,
            Scheme scheme, int peak)
        {
            var post = C(0x5B, 0x40, 0x2A);
            var postLit = C(0x7E, 0x5E, 0x3E);
            var lead = C(0x54, 0x58, 0x5E);
            var leadLit = C(0x76, 0x7C, 0x84);
            var bell = C(0xB0, 0x8A, 0x3A);
            var flag = C(0x9C, 0x42, 0x30);

            var c = Iso.Project(cw * 0.5f, ch * 0.5f, ox, oy);
            int baseLift = scheme.WallHeight + peak - 2;

            for (int lift = 0; lift < 20; lift++)
            for (int dx = -9; dx <= 9; dx++)
            {
                bool upright = Mathf.Abs(dx) > 7;
                bool floor = lift < 2;
                if (!upright && !floor) continue;
                Plot(px, w, h, c.x + dx, c.y + baseLift + lift, dx < 0 ? postLit : post);
            }

            for (int lift = 6; lift < 15; lift++)
            {
                int half = lift < 12 ? 4 : 4 - (lift - 12);
                for (int dx = -half; dx <= half; dx++)
                    Plot(px, w, h, c.x + dx, c.y + baseLift + lift, dx < 0 ? Lighten(bell, 26) : bell);
            }

            // Lead spire.
            for (int lift = 0; lift < 18; lift++)
            {
                int half = Mathf.Max(0, 9 - lift * 9 / 17);
                for (int dx = -half; dx <= half; dx++)
                    Plot(px, w, h, c.x + dx, c.y + baseLift + 20 + lift, dx < 0 ? leadLit : lead);
            }

            for (int lift = 0; lift < 9; lift++) Plot(px, w, h, c.x, c.y + baseLift + 38 + lift, lead);
            for (int lift = 3; lift < 8; lift++)
            for (int dx = 1; dx <= 8 - lift; dx++)
                Plot(px, w, h, c.x + dx, c.y + baseLift + 38 + lift, flag);
        }

        /// <summary>A double cargo door with a hoist beam over it, on the warehouse gable.</summary>
        private static void LoadingDoor(Color32[] px, int w, int h, int cw, int ch, int ox, int oy,
            Scheme scheme, int peak)
        {
            var plank = C(0x4E, 0x3A, 0x26);
            var plankLit = C(0x6C, 0x52, 0x36);
            var iron = C(0x33, 0x31, 0x2E);

            var d = Iso.Project(cw, ch * 0.5f, ox, oy);

            for (int lift = 2; lift < scheme.WallHeight - 6; lift++)
            for (int dx = -12; dx <= 12; dx++)
            {
                bool seam = dx == 0 || Mathf.Abs(dx) == 12;
                bool strap = lift == 8 || lift == scheme.WallHeight - 12;
                Plot(px, w, h, d.x + dx, d.y + lift,
                    seam || strap ? iron : (dx < 0 ? plankLit : plank));
            }

            // Hoist beam out through the gable, with a block and tackle hanging off it.
            int beam = scheme.WallHeight + peak / 2;
            for (int dx = 0; dx <= 16; dx++)
            for (int dy = 0; dy < 3; dy++)
                Plot(px, w, h, d.x + dx, d.y + beam + dy, dy == 0 ? plank : plankLit);

            for (int lift = 0; lift < 9; lift++) Plot(px, w, h, d.x + 15, d.y + beam - lift, iron);
            for (int lift = 9; lift < 14; lift++)
            for (int dx = -2; dx <= 2; dx++)
                Plot(px, w, h, d.x + 15 + dx, d.y + beam - lift, plank);
        }

        private static void Chimney(Color32[] px, int w, int h, int cw, int ch, int ox, int oy,
            Scheme scheme, int peak)
        {
            var brick = new Color32(0x6E, 0x4E, 0x3C, 0xFF);
            var brickLit = new Color32(0x8E, 0x6A, 0x52, 0xFF);
            var cap = new Color32(0x24, 0x1C, 0x16, 0xFF);

            var stack = Iso.Project(cw * 0.72f, ch * 0.28f, ox, oy);
            int baseLift = scheme.WallHeight + peak / 2;
            int top = scheme.WallHeight + peak + 14;

            for (int lift = baseLift; lift <= top; lift++)
            for (int dx = -4; dx <= 4; dx++)
                Plot(px, w, h, stack.x + dx, stack.y + lift, dx < -1 ? brickLit : brick);

            for (int dx = -5; dx <= 5; dx++)
            {
                Plot(px, w, h, stack.x + dx, stack.y + top + 1, cap);
                Plot(px, w, h, stack.x + dx, stack.y + top + 2, cap);
            }
        }

        /// <summary>
        /// What is underground is plant, not architecture: a still, sacks, pit props, a ladder.
        ///
        /// Drawing cellars the same way as houses gave them roofs, and a roof underground is
        /// nonsense that also hides the thing the player came down to see. The excavated floor
        /// is already drawn by the tilemap and is deliberately the lightest surface down there,
        /// so anything placed on it has to sit low and leave most of it showing.
        /// </summary>
        private static void PaintUnderground(Color32[] px, int w, int h, int cw, int ch,
            int ox, int oy, BuildingKind kind)
        {
            switch (kind)
            {
                case BuildingKind.Still: Still(px, w, h, cw, ch, ox, oy); break;
                case BuildingKind.UnderStore: Sacks(px, w, h, cw, ch, ox, oy); break;
                case BuildingKind.Tunnel: PitProps(px, w, h, cw, ch, ox, oy); break;
                case BuildingKind.HiddenEntrance: Ladder(px, w, h, cw, ch, ox, oy); break;
                case BuildingKind.FalseWall: BlindWall(px, w, h, cw, ch, ox, oy); break;
                default: PitProps(px, w, h, cw, ch, ox, oy); break;
            }
        }

        private static void Still(Color32[] px, int w, int h, int cw, int ch, int ox, int oy)
        {
            var copper = new Color32(0xA8, 0x6A, 0x32, 0xFF);
            var copperLit = new Color32(0xD2, 0x96, 0x4E, 0xFF);
            var copperDark = new Color32(0x70, 0x44, 0x1E, 0xFF);
            var brick = new Color32(0x5A, 0x40, 0x34, 0xFF);
            var fire = new Color32(0xF0, 0x9A, 0x38, 0xFF);
            var pipe = new Color32(0x8E, 0x5A, 0x2C, 0xFF);

            var c = Iso.Project(cw * 0.5f, ch * 0.55f, ox, oy);

            // Brick firebox with the fire showing through its mouth.
            for (int lift = 0; lift < 9; lift++)
            for (int dx = -13; dx <= 13; dx++)
                Plot(px, w, h, c.x + dx, c.y + lift, dx < -4 ? Lighten(brick, 16) : brick);
            for (int lift = 2; lift < 7; lift++)
            for (int dx = -4; dx <= 4; dx++)
                Plot(px, w, h, c.x + dx, c.y + lift, lift > 4 ? fire : Lighten(fire, 30));

            // The pot itself, a squat copper drum with a domed head.
            for (int lift = 9; lift < 30; lift++)
            {
                int half = lift < 26 ? 12 : 12 - (lift - 26) * 3;
                for (int dx = -half; dx <= half; dx++)
                {
                    var tone = dx < -half / 2 ? copperLit : dx > half / 2 ? copperDark : copper;
                    if (lift == 14 || lift == 22) tone = Darken(tone, 34);
                    Plot(px, w, h, c.x + dx, c.y + lift, tone);
                }
            }

            // Swan neck running off to the condenser.
            for (int t = 0; t < 18; t++)
            {
                int x = c.x + 2 + t;
                int y = c.y + 30 - t * t / 14;
                Plot(px, w, h, x, y, pipe);
                Plot(px, w, h, x, y + 1, copperLit);
                Plot(px, w, h, x, y + 2, pipe);
            }

            for (int lift = 0; lift < 12; lift++)
            for (int dx = -3; dx <= 3; dx++)
                Plot(px, w, h, c.x + 19 + dx, c.y + lift, dx < 0 ? Lighten(copper, 14) : copperDark);
        }

        private static void Sacks(Color32[] px, int w, int h, int cw, int ch, int ox, int oy)
        {
            var hessian = new Color32(0x9E, 0x8A, 0x5E, 0xFF);
            var hessianLit = new Color32(0xC0, 0xAA, 0x76, 0xFF);
            var hessianDark = new Color32(0x6E, 0x5E, 0x3E, 0xFF);
            var tie = new Color32(0x50, 0x42, 0x2A, 0xFF);

            for (int i = 0; i < 5; i++)
            {
                float u = 0.35f + (i % 3) * (cw - 0.7f) / 2f;
                float v = 0.35f + (i / 3) * (ch - 0.7f);
                var p = Iso.Project(u, v, ox, oy);
                int lean = (i % 2 == 0) ? 1 : -1;

                for (int lift = 0; lift < 15; lift++)
                {
                    int half = lift < 3 ? 6 : lift < 11 ? 7 : 7 - (lift - 11) * 2;
                    for (int dx = -half; dx <= half; dx++)
                    {
                        var tone = dx < -half / 2 ? hessianLit : dx > half / 2 ? hessianDark : hessian;
                        Plot(px, w, h, p.x + dx + lift * lean / 8, p.y + lift, tone);
                    }
                }
                for (int dx = -2; dx <= 2; dx++)
                    Plot(px, w, h, p.x + dx + lean, p.y + 14, tie);
            }
        }

        private static void PitProps(Color32[] px, int w, int h, int cw, int ch, int ox, int oy)
        {
            var timber = new Color32(0x63, 0x4A, 0x2C, 0xFF);
            var timberLit = new Color32(0x86, 0x66, 0x3E, 0xFF);

            for (int side = 0; side < 2; side++)
            {
                var p = Iso.Project(cw * 0.5f, side == 0 ? 0.12f : ch - 0.12f, ox, oy);
                for (int lift = 0; lift < 20; lift++)
                {
                    Plot(px, w, h, p.x - 11, p.y + lift, timberLit);
                    Plot(px, w, h, p.x - 10, p.y + lift, timber);
                    Plot(px, w, h, p.x + 10, p.y + lift, timber);
                    Plot(px, w, h, p.x + 11, p.y + lift, Darken(timber, 18));
                }
                for (int dx = -12; dx <= 12; dx++)
                {
                    Plot(px, w, h, p.x + dx, p.y + 20, timberLit);
                    Plot(px, w, h, p.x + dx, p.y + 21, timber);
                    Plot(px, w, h, p.x + dx, p.y + 22, Darken(timber, 22));
                }
            }
        }

        private static void Ladder(Color32[] px, int w, int h, int cw, int ch, int ox, int oy)
        {
            var timber = new Color32(0x74, 0x58, 0x34, 0xFF);
            var dark = new Color32(0x14, 0x0E, 0x0A, 0xFF);
            var sky = new Color32(0x8E, 0x8A, 0x6E, 0xFF);

            var c = Iso.Project(cw * 0.5f, ch * 0.5f, ox, oy);

            // A hole in the roof of the chamber, with daylight coming down it.
            for (int dy = -6; dy <= 6; dy++)
            for (int dx = -14; dx <= 14; dx++)
                if (dx * dx + dy * dy * 5 <= 196) Plot(px, w, h, c.x + dx, c.y + dy + 26, dy > 2 ? sky : dark);

            for (int lift = 0; lift < 30; lift++)
            {
                Plot(px, w, h, c.x - 5, c.y + lift, timber);
                Plot(px, w, h, c.x + 5, c.y + lift, Darken(timber, 20));
                if (lift % 5 == 0)
                    for (int dx = -5; dx <= 5; dx++) Plot(px, w, h, c.x + dx, c.y + lift, Lighten(timber, 18));
            }
        }

        private static void BlindWall(Color32[] px, int w, int h, int cw, int ch, int ox, int oy)
        {
            var stone = new Color32(0x5E, 0x54, 0x46, 0xFF);
            var stoneLit = new Color32(0x7C, 0x72, 0x60, 0xFF);
            var mortar = new Color32(0x3A, 0x33, 0x2A, 0xFF);

            int steps = Mathf.Max(cw, ch) * 96;
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                var p = Iso.Project(t * cw, ch * 0.5f, ox, oy);
                for (int lift = 0; lift < 26; lift++)
                {
                    bool course = lift % 6 == 0 || (i * 40 / steps + lift / 6) % 5 == 0;
                    Plot(px, w, h, p.x, p.y + lift, course ? mortar : lift > 18 ? stoneLit : stone);
                }
            }
        }

        private static void PaintYard(Color32[] px, int w, int h, int cw, int ch, int ox, int oy,
            BuildingKind kind)
        {
            Shadow(px, w, h, cw, ch, ox, oy);

            Color32 ground;
            switch (kind)
            {
                case BuildingKind.Field: ground = new Color32(0x9E, 0x86, 0x3E, 0xFF); break;
                case BuildingKind.ClayPit: ground = new Color32(0x8E, 0x55, 0x3C, 0xFF); break;
                case BuildingKind.Tunnel: ground = new Color32(0x35, 0x2C, 0x22, 0xFF); break;
                default: ground = new Color32(0x7B, 0x66, 0x46, 0xFF); break;
            }

            int steps = Mathf.Max(cw, ch) * 150;
            for (int j = 0; j <= steps; j++)
            for (int i = 0; i <= steps; i++)
            {
                float u = i / (float)steps * cw;
                float v = j / (float)steps * ch;
                var p = Iso.Project(u, v, ox, oy);

                var tone = ground;
                if (kind == BuildingKind.Field)
                {
                    // Ploughed furrows running the length of the field.
                    tone = (Mathf.RoundToInt(v * 6f) % 2 == 0) ? Lighten(ground, 16) : Darken(ground, 10);
                }
                else if (kind == BuildingKind.ClayPit)
                {
                    float toEdge = Mathf.Min(Mathf.Min(u, cw - u), Mathf.Min(v, ch - v));
                    tone = Darken(ground, Mathf.RoundToInt(Mathf.Clamp01(toEdge) * 34f));
                }

                Plot(px, w, h, p.x, p.y + 4, tone);
            }

            Palings(px, w, h, cw, ch, ox, oy);
            if (kind == BuildingKind.Sawpit) LogPile(px, w, h, cw, ch, ox, oy);
            if (kind == BuildingKind.Field) Sheaves(px, w, h, cw, ch, ox, oy);
        }

        private static void Palings(Color32[] px, int w, int h, int cw, int ch, int ox, int oy)
        {
            var rail = new Color32(0x74, 0x5A, 0x38, 0xFF);
            var post = new Color32(0x4C, 0x39, 0x22, 0xFF);
            int steps = Mathf.Max(cw, ch) * 120;

            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                var edges = new[]
                {
                    Iso.Project(t * cw, 0f, ox, oy),
                    Iso.Project(t * cw, ch, ox, oy),
                    Iso.Project(0f, t * ch, ox, oy),
                    Iso.Project(cw, t * ch, ox, oy),
                };

                bool isPost = i % (steps / (Mathf.Max(cw, ch) * 4)) == 0;
                foreach (var p in edges)
                {
                    for (int lift = 4; lift < (isPost ? 15 : 10); lift++)
                        Plot(px, w, h, p.x, p.y + lift, isPost ? post : rail);
                    if (!isPost) continue;
                    for (int lift = 8; lift < 10; lift++) Plot(px, w, h, p.x, p.y + lift, rail);
                }
            }
        }

        private static void LogPile(Color32[] px, int w, int h, int cw, int ch, int ox, int oy)
        {
            var bark = new Color32(0x5E, 0x44, 0x28, 0xFF);
            var barkLit = new Color32(0x7C, 0x5E, 0x3A, 0xFF);
            var cut = new Color32(0xC4, 0xA0, 0x68, 0xFF);

            for (int row = 0; row < 3; row++)
            for (int i = 0; i <= 200; i++)
            {
                float t = i / 200f;
                var p = Iso.Project(0.35f + t * (cw - 0.7f), 0.5f + row * 0.4f, ox, oy);
                for (int lift = 5; lift < 12; lift++)
                    Plot(px, w, h, p.x, p.y + lift, lift > 9 ? barkLit : bark);
                if (i > 195) for (int lift = 5; lift < 12; lift++) Plot(px, w, h, p.x, p.y + lift, cut);
            }
        }

        private static void Sheaves(Color32[] px, int w, int h, int cw, int ch, int ox, int oy)
        {
            var straw = new Color32(0xCE, 0xB0, 0x56, 0xFF);
            var strawDark = new Color32(0x9C, 0x82, 0x3A, 0xFF);

            for (int i = 0; i < 6; i++)
            {
                float u = 0.5f + (i % 3) * (cw - 1f) / 2f;
                float v = 0.5f + (i / 3) * (ch - 1f);
                var p = Iso.Project(u, v, ox, oy);

                for (int lift = 4; lift < 16; lift++)
                {
                    int spread = lift < 10 ? 3 : lift < 13 ? 2 : 1;
                    for (int dx = -spread; dx <= spread; dx++)
                        Plot(px, w, h, p.x + dx, p.y + lift, dx < 0 ? straw : strawDark);
                }
            }
        }

        private static void Shadow(Color32[] px, int w, int h, int cw, int ch, int ox, int oy)
        {
            var shadow = new Color32(0x16, 0x12, 0x0C, 0x4A);
            int steps = Mathf.Max(cw, ch) * 100;

            for (int j = 0; j <= steps; j++)
            for (int i = 0; i <= steps; i++)
            {
                float u = Mathf.Lerp(-Overhang, cw + Overhang, i / (float)steps);
                float v = Mathf.Lerp(-Overhang, ch + Overhang, j / (float)steps);
                var p = Iso.Project(u, v, ox, oy);
                Blend(px, w, h, p.x + 6, p.y - 3, shadow);
            }
        }

        private static Scheme SchemeFor(BuildingKind kind, int variant)
        {
            switch (kind)
            {
                case BuildingKind.House:
                {
                    // Four cottages: differing render, thatch age and eaves height. Village
                    // houses were built by different hands in different decades and the ones
                    // that were re-thatched last are visibly paler.
                    var walls = new[] { C(0xC6, 0xB2, 0x8E), C(0xB0, 0x9A, 0x76), C(0xCE, 0xBE, 0xA0), C(0xA8, 0x8E, 0x68) };
                    var thatch = new[] { C(0xC4, 0x9E, 0x52), C(0xA8, 0x86, 0x44), C(0xD2, 0xB0, 0x62), C(0x96, 0x78, 0x3E) };
                    return new Scheme
                    {
                        Wall = walls[variant], Roof = thatch[variant],
                        Timber = C(0x59, 0x3E, 0x28), Plinth = C(0x64, 0x5E, 0x54),
                        WallHeight = 24 + variant * 2, Pitch = 13 + (variant & 1) * 3,
                        Thatch = true, HalfTimbered = true, Chimney = true,
                    };
                }
                case BuildingKind.TownHall:
                    return new Scheme
                    {
                        Wall = C(0xCA, 0xBE, 0xA4), Roof = C(0x9C, 0x42, 0x30),
                        Timber = C(0x53, 0x3A, 0x26), Plinth = C(0x6E, 0x6A, 0x62),
                        WallHeight = 34, Pitch = 16, HalfTimbered = true, Chimney = true,
                    };
                case BuildingKind.Warehouse:
                    return new Scheme
                    {
                        Wall = C(0x7C, 0x5E, 0x3C), Roof = C(0x50, 0x44, 0x34),
                        Timber = C(0x4A, 0x36, 0x20), Plinth = C(0x54, 0x4E, 0x46),
                        WallHeight = 38, Pitch = 12, HalfTimbered = true,
                    };
                case BuildingKind.Brewery:
                    return new Scheme
                    {
                        Wall = C(0x9C, 0x76, 0x48), Roof = C(0x4C, 0x62, 0x48),
                        Timber = C(0x4E, 0x36, 0x22), Plinth = C(0x60, 0x5A, 0x50),
                        WallHeight = 32, Pitch = 14, HalfTimbered = true, Chimney = true,
                    };
                case BuildingKind.Still:
                    return new Scheme
                    {
                        Wall = C(0x64, 0x50, 0x38), Roof = C(0xA8, 0x6E, 0x2E),
                        Timber = C(0x3A, 0x2A, 0x1A), Plinth = C(0x40, 0x38, 0x30),
                        WallHeight = 20, Pitch = 8,
                    };
                case BuildingKind.UnderStore:
                    return new Scheme
                    {
                        Wall = C(0x5A, 0x4A, 0x36), Roof = C(0x42, 0x36, 0x28),
                        Timber = C(0x33, 0x26, 0x1A), Plinth = C(0x38, 0x30, 0x28),
                        WallHeight = 18, Pitch = 6,
                    };
                case BuildingKind.FalseWall:
                    return new Scheme
                    {
                        Wall = C(0x50, 0x44, 0x34), Roof = C(0x48, 0x3C, 0x2E),
                        Timber = C(0x3A, 0x30, 0x24), Plinth = C(0x34, 0x2C, 0x22),
                        WallHeight = 22, Pitch = 4,
                    };
                case BuildingKind.HiddenEntrance:
                    return new Scheme
                    {
                        Wall = C(0x62, 0x4E, 0x34), Roof = C(0x8A, 0x6C, 0x3E),
                        Timber = C(0x3A, 0x2A, 0x18), Plinth = C(0x44, 0x3C, 0x30),
                        WallHeight = 16, Pitch = 6, Thatch = true,
                    };
                default:
                    return new Scheme
                    {
                        Wall = C(0x94, 0x78, 0x52), Roof = C(0x72, 0x54, 0x38),
                        Timber = C(0x46, 0x32, 0x20), Plinth = C(0x56, 0x50, 0x46),
                        WallHeight = 26, Pitch = 12,
                    };
            }
        }

        private static Color32 C(byte r, byte g, byte b) => new Color32(r, g, b, 0xFF);

        /// <summary>Walls darken towards the ground, which is what gives a flat face relief.</summary>
        private static Color32 Shade(Color32 c, int lift, int top)
        {
            int delta = -18 + lift * 28 / Mathf.Max(1, top);
            return new Color32(
                (byte)Mathf.Clamp(c.r + delta, 0, 255),
                (byte)Mathf.Clamp(c.g + delta, 0, 255),
                (byte)Mathf.Clamp(c.b + delta, 0, 255), c.a);
        }

        private static Color32[] Blank(int w, int h)
        {
            var px = new Color32[w * h];
            var clear = new Color32(0, 0, 0, 0);
            for (int i = 0; i < px.Length; i++) px[i] = clear;
            return px;
        }

        private static void Plot(Color32[] px, int w, int h, int x, int y, Color32 color)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            px[y * w + x] = color;
        }

        private static void Blend(Color32[] px, int w, int h, int x, int y, Color32 color)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            var under = px[y * w + x];
            int a = color.a;
            px[y * w + x] = new Color32(
                (byte)((color.r * a + under.r * (255 - a)) / 255),
                (byte)((color.g * a + under.g * (255 - a)) / 255),
                (byte)((color.b * a + under.b * (255 - a)) / 255),
                (byte)Mathf.Max(under.a, a));
        }

        private static Color32 Lighten(Color32 c, int amount) => new Color32(
            (byte)Mathf.Min(255, c.r + amount), (byte)Mathf.Min(255, c.g + amount),
            (byte)Mathf.Min(255, c.b + amount), c.a);

        private static Color32 Darken(Color32 c, int amount) => new Color32(
            (byte)Mathf.Max(0, c.r - amount), (byte)Mathf.Max(0, c.g - amount),
            (byte)Mathf.Max(0, c.b - amount), c.a);

        private static Sprite ToSprite(Color32[] pixels, int w, int h, string name, Vector2 pivot)
        {
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = name,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false);
            return Sprite.Create(texture, new Rect(0, 0, w, h), pivot, Iso.PixelsPerUnit, 0, SpriteMeshType.FullRect);
        }
    }
}
