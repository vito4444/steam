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
        public const int VariantCount = 8;

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

            /// <summary>A mill tower and sails standing off one gable.</summary>
            public bool Mill;

            /// <summary>A gable turned through ninety degrees, breaking the front roof plane.</summary>
            public bool CrossGable;
            public bool HalfTimbered;
            public bool Chimney;

            /// <summary>A single-pitch outshot along the east wall, or none.</summary>
            public bool LeanTo;

            /// <summary>A covered porch on posts over the south doorway, or none.</summary>
            public bool Porch;
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

            // Enough room for the eaves, and for an outshot standing clear of the east wall.
            int marginX = Mathf.CeilToInt(Overhang * Iso.TileWidth) + (scheme.LeanTo || scheme.Porch ? 30 : 4);
            if (scheme.Mill) marginX = Mathf.Max(marginX, 44);
            int footW = Iso.FootprintWidth(cw, ch) + marginX * 2;
            int footH = Iso.FootprintHeight(cw, ch);
            int total = footH + scheme.WallHeight + peak + marginX + 12 + (scheme.Mill ? 96 : 0);

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
            if (scheme.CrossGable) CrossGable(px, w, h, cw, ch, ox, oy, scheme, ridgeAlongX, peak);
            if (scheme.LeanTo) LeanTo(px, w, h, cw, ch, ox, oy, scheme);
            if (scheme.Porch) Porch(px, w, h, cw, ch, ox, oy, scheme);
            if (scheme.Chimney) Chimney(px, w, h, cw, ch, ox, oy, scheme, peak);

            // Landmarks last, over the roof they stand on.
            switch (kind)
            {
                case BuildingKind.TownHall: BellTower(px, w, h, cw, ch, ox, oy, scheme, peak); break;
                case BuildingKind.Warehouse: LoadingDoor(px, w, h, cw, ch, ox, oy, scheme, peak); break;
            }

            if (scheme.Mill) MillTower(px, w, h, cw, ch, ox, oy, scheme);
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
                    // Thatch is laid in courses from the eaves up, each overlapping the one
                    // below, so a roof carries bands parallel to the ridge with a line of
                    // shadow where each course ends. It was a field of random light and dark
                    // pixels before, which at this size reads as moss growing on the roof
                    // rather than as a roof.
                    const float courses = 5f;
                    float within = fromRidge * courses;
                    within -= Mathf.Floor(within);

                    if (within > 0.84f) tone = Darken(tone, 20);
                    else if (within < 0.14f) tone = Lighten(tone, 12);

                    // Bundles laid up the slope, crossing the courses. Courses alone gave a
                    // roof banded like a deckchair; what a thatched roof actually shows at
                    // this distance is the grain of the straw running eaves to ridge, with
                    // the courses stepping across it.
                    float along = ridgeAlongX ? u : v;
                    int bundle = Mathf.Abs(Mathf.RoundToInt(along * 16f)) % 3;
                    if (bundle == 0) tone = Darken(tone, 9);
                    else if (bundle == 2) tone = Lighten(tone, 6);

                    // Enough roughness that the courses are not ruled lines. Straw is combed,
                    // not machined.
                    int fray = (Mathf.RoundToInt(u * 40f) * 7 + Mathf.RoundToInt(v * 40f) * 13) % 13;
                    if (fray < 2) tone = Darken(tone, 9);
                    else if (fray > 10) tone = Lighten(tone, 7);

                    // The eaves course stands proud of the wall and is cut square.
                    if (fromRidge > 0.93f) tone = Darken(tone, 14);
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
        /// A single-pitch outshot leaning against the east wall: a lower roof sloping away
        /// from the main one, on posts, open at the front.
        ///
        /// Every building here is otherwise a box, and a row of boxes is the remaining reason
        /// the settlement still looks assembled from parts rather than built. An outshot costs
        /// one extra shape and breaks the silhouette of whatever it is attached to, which is
        /// what the reference gets from having no two buildings the same plan.
        /// </summary>
        private static void LeanTo(Color32[] px, int w, int h, int cw, int ch, int ox, int oy,
            Scheme scheme)
        {
            var roof = Darken(scheme.Roof, 16);
            var roofLit = Lighten(scheme.Roof, 8);
            var post = scheme.Timber;
            var postLit = Lighten(scheme.Timber, 26);
            var floor = Darken(scheme.Plinth, 12);

            const float depth = 0.62f;
            int highSide = scheme.WallHeight - 6;
            int lowSide = highSide - 13;
            int steps = Mathf.Max(2, ch) * 130;

            // Floor of the outshot, on the ground outside the east wall.
            for (int j = 0; j <= steps; j++)
            for (int i = 0; i <= steps; i++)
            {
                float u = cw + i / (float)steps * depth;
                float v = 0.15f + j / (float)steps * (ch - 0.3f);
                var p = Iso.Project(u, v, ox, oy);
                Plot(px, w, h, p.x, p.y + 1, floor);
            }

            // Two posts carrying the outer edge.
            for (int end = 0; end < 2; end++)
            {
                var p = Iso.Project(cw + depth, end == 0 ? 0.15f : ch - 0.15f, ox, oy);
                for (int lift = 0; lift < lowSide; lift++)
                {
                    Plot(px, w, h, p.x, p.y + lift, postLit);
                    Plot(px, w, h, p.x + 1, p.y + lift, post);
                }
            }

            // The sloping roof, from the main wall down to the posts.
            for (int j = 0; j <= steps; j++)
            for (int i = 0; i <= steps; i++)
            {
                float across = i / (float)steps;
                float u = cw - 0.08f + across * (depth + 0.16f);
                float v = -0.08f + j / (float)steps * (ch + 0.16f);
                int lift = Mathf.RoundToInt(Mathf.Lerp(highSide, lowSide, across));

                var p = Iso.Project(u, v, ox, oy);
                var tone = across < 0.12f ? Darken(roof, 20) : across > 0.94f ? Darken(roof, 30) : roof;
                if (scheme.Thatch)
                {
                    float band = across * 3f;
                    if (band - Mathf.Floor(band) > 0.82f) tone = Darken(tone, 16);
                    else if ((i + j) % 11 == 0) tone = roofLit;
                }
                if (!scheme.Thatch && Mathf.RoundToInt(across * 40f) % 5 == 0) tone = Darken(tone, 14);
                Plot(px, w, h, p.x, p.y + lift, tone);
            }
        }

        /// <summary>
        /// A porch over the south door: a small pitched hood on two posts, standing out from
        /// the wall the door is in.
        ///
        /// Where the outshot changes a building's plan, this changes its face. Both matter
        /// because the reference has no two buildings alike, and a row of identical frontages
        /// is as much of a tell as a row of identical roofs.
        /// </summary>
        private static void Porch(Color32[] px, int w, int h, int cw, int ch, int ox, int oy,
            Scheme scheme)
        {
            var roof = Darken(scheme.Roof, 12);
            var roofLit = Lighten(scheme.Roof, 10);
            var post = scheme.Timber;
            var postLit = Lighten(scheme.Timber, 28);
            var floor = Darken(scheme.Plinth, 8);

            // Narrow and high enough to stand over the door rather than across the frontage:
            // a hood the width of the wall hides the door it is supposed to shelter, along
            // with the windows either side of it.
            const float depth = 0.42f;
            float mid = cw / 2f;
            float from = Mathf.Max(0.1f, mid - 0.42f);
            float to = Mathf.Min(cw - 0.1f, mid + 0.42f);

            int head = scheme.WallHeight - 2;
            int eave = head - 6;
            int steps = 150;

            // The south wall is the one the door is in, and south is v at its largest: the
            // sprite's v axis runs opposite the world's y, because the projection negates the
            // sum of the two. Built at small v the porch comes out on the far side of the
            // house, hanging off the back wall.
            for (int j = 0; j <= steps; j++)
            for (int i = 0; i <= steps; i++)
            {
                float u = Mathf.Lerp(from, to, i / (float)steps);
                float v = ch + (j / (float)steps) * depth;
                var p = Iso.Project(u, v, ox, oy);
                Plot(px, w, h, p.x, p.y + 1, floor);
            }

            for (int end = 0; end < 2; end++)
            {
                var p = Iso.Project(end == 0 ? from : to, ch + depth, ox, oy);
                for (int lift = 0; lift < eave; lift++)
                {
                    Plot(px, w, h, p.x, p.y + lift, postLit);
                    Plot(px, w, h, p.x + 1, p.y + lift, post);
                }
            }

            for (int j = 0; j <= steps; j++)
            for (int i = 0; i <= steps; i++)
            {
                float out01 = j / (float)steps;
                float u = Mathf.Lerp(from - 0.12f, to + 0.12f, i / (float)steps);
                float v = ch - 0.06f + out01 * (depth + 0.18f);
                int lift = Mathf.RoundToInt(Mathf.Lerp(head, eave, out01));

                var p = Iso.Project(u, v, ox, oy);
                var tone = out01 > 0.93f ? Darken(roof, 26) : out01 < 0.1f ? Darken(roof, 12) : roof;
                if (scheme.Thatch)
                {
                    float band = out01 * 2f;
                    if (band - Mathf.Floor(band) > 0.8f) tone = Darken(tone, 14);
                    else if ((i + j) % 13 == 0) tone = roofLit;
                }
                Plot(px, w, h, p.x, p.y + lift, tone);
            }
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
        /// <summary>
        /// A cross gable over the front: a second ridge at right angles to the main one,
        /// breaking the south roof plane with a gable end of its own.
        ///
        /// The buildings here are boxes with two roof slopes each, and the reference has almost
        /// no two on the same plan. A cross gable is the cheapest way out of that which is
        /// still honest carpentry: it changes the outline against the sky and puts a second
        /// gable end on the face the viewer sees, and it costs nothing on the ground - the
        /// footprint, and therefore everything the simulation knows about the building, is
        /// unchanged.
        ///
        /// Its ridge is level, as a cross gable's is, and its height is set just below the main
        /// roof's so the two planes meet where they should instead of one cutting through the
        /// other.
        /// </summary>
        private static void CrossGable(Color32[] px, int w, int h, int cw, int ch, int ox, int oy,
            Scheme scheme, bool ridgeAlongX, int peak)
        {
            var wall = scheme.Wall;
            var roof = scheme.Roof;
            var roofLit = Lighten(scheme.Roof, 22);
            var roofDark = Darken(scheme.Roof, 26);
            var edge = Darken(scheme.Roof, 48);
            var timber = scheme.Timber;

            // Off centre, not over the door. A bay in the middle of the frontage buries the
            // doorway it is standing in front of, and an asymmetric front is closer to the
            // reference anyway - nothing there is centred on anything.
            float mid = cw * 0.32f;
            float half = Mathf.Min(0.55f, cw * 0.22f);
            float depth = Mathf.Min(1.1f, ch * 0.6f);
            int ridgeLift = scheme.WallHeight + Mathf.RoundToInt(peak * 0.78f);

            int steps = 260;

            // The gable end itself. It stands a little south of the main wall, out past the
            // eaves rather than flush with them: set back, the main roof's overhang cuts across
            // its head and the whole thing reads as a hole in the slope instead of a bay
            // standing out of it.
            const float jut = 0.26f;

            for (int i = 0; i <= steps; i++)
            {
                float u = Mathf.Lerp(mid - half, mid + half, i / (float)steps);
                float t = Mathf.Abs(u - mid) / half;
                int top = scheme.WallHeight + Mathf.RoundToInt((1f - t) * (ridgeLift - scheme.WallHeight));

                var p = Iso.Project(u, ch + jut, ox, oy);

                // Carried on the wall below, so the bay does not float. Studding on the same
                // spacing as the rest of the building: a blank panel this size on a frontage
                // that is half-timbered everywhere else looks like a rendering fault.
                bool stud = scheme.HalfTimbered && ((i * 7) / steps) % 2 == 0 && t > 0.12f;

                for (int lift = 0; lift < top; lift++)
                {
                    var tone = lift < scheme.WallHeight
                        ? Darken(wall, 16 + (int)(t * 10f))
                        : Darken(wall, (int)(t * 12f));
                    if (stud && lift < scheme.WallHeight - 2) tone = timber;
                    Plot(px, w, h, p.x, p.y + lift, tone);
                }

                // A sill beam where the gable sits on the wall head.
                if (top > scheme.WallHeight)
                    Plot(px, w, h, p.x, p.y + scheme.WallHeight - 1, timber);

                // Barge boards down both slopes of the gable end.
                Plot(px, w, h, p.x, p.y + top, timber);
                Plot(px, w, h, p.x, p.y + top + 1, timber);
            }

            // The two roof planes running back from it.
            for (int j = 0; j <= steps; j++)
            for (int i = 0; i <= steps; i++)
            {
                float u = Mathf.Lerp(mid - half - 0.09f, mid + half + 0.09f, i / (float)steps);
                float v = ch + jut + 0.09f - (j / (float)steps) * (depth + jut + 0.09f);

                float t = Mathf.Clamp01(Mathf.Abs(u - mid) / (half + 0.09f));
                int lift = scheme.WallHeight
                    + Mathf.RoundToInt((1f - t) * (ridgeLift - scheme.WallHeight));

                // Only the part that stands above the main roof. Drawn without this check the
                // cross gable paints over the slope it is supposed to be let into, and the
                // whole thing reads as a patch stuck onto the front rather than a second ridge
                // meeting the first.
                if (lift <= MainRoofLift(cw, ch, u, v, scheme, ridgeAlongX, peak)) continue;

                var tone = u < mid ? roofLit : roofDark;
                if (t > 0.94f) tone = edge;
                else if (t < 0.06f) tone = Lighten(roof, 34);
                else if (scheme.Thatch)
                {
                    float band = t * 3f;
                    if (band - Mathf.Floor(band) > 0.8f) tone = Darken(tone, 16);
                }

                var p = Iso.Project(u, v, ox, oy);
                Plot(px, w, h, p.x, p.y + lift, tone);
            }
        }

        /// <summary>How high the main roof stands at a point, in the same units as everything
        /// drawn on top of it.</summary>
        private static int MainRoofLift(int cw, int ch, float u, float v, Scheme scheme,
            bool ridgeAlongX, int peak)
        {
            float u0 = -Overhang, u1 = cw + Overhang;
            float v0 = -Overhang, v1 = ch + Overhang;

            float across = ridgeAlongX
                ? Mathf.InverseLerp(v0, v1, v)
                : Mathf.InverseLerp(u0, u1, u);
            float fromRidge = Mathf.Abs(across - 0.5f) * 2f;
            return scheme.WallHeight + Mathf.RoundToInt((1f - fromRidge) * peak);
        }

        /// <summary>
        /// A mill tower with sails, standing off the brewery's west gable.
        ///
        /// The reference has a windmill and a watermill, both taller than anything around them
        /// and neither shaped like a house; the tallest thing here was the town hall's bell
        /// turret, and a settlement whose skyline is all roof ridges reads as one building type
        /// repeated. A mill is also the one landmark this town can have without inventing a
        /// trade for it - the brewery already needs its malt ground.
        ///
        /// Nothing about it is simulated. The brewery's recipe, cost and output are untouched;
        /// this is the same building wearing what it does on the outside.
        /// </summary>
        private static void MillTower(Color32[] px, int w, int h, int cw, int ch, int ox, int oy,
            Scheme scheme)
        {
            var stone = C(0x6C, 0x63, 0x54);
            var stoneLit = C(0x82, 0x79, 0x68);
            var stoneDark = C(0x4A, 0x43, 0x38);
            var cap = C(0x4A, 0x3A, 0x26);
            var capLit = C(0x64, 0x50, 0x34);
            var sail = C(0x5E, 0x46, 0x2A);
            var sailLit = C(0x86, 0x68, 0x40);
            var cloth = C(0x8A, 0x80, 0x66);

            // Standing clear of the west gable rather than on the roof: a tower that starts
            // at ridge height is a turret, and the point of a mill is that it is taller than
            // the buildings around it.
            var c = Iso.Project(-0.24f, ch * 0.80f, ox, oy);
            const int height = 78;

            // The tower batters inwards as it rises, which is what tells it from a chimney.
            for (int lift = 0; lift < height; lift++)
            {
                float t = lift / (float)height;
                int half = Mathf.RoundToInt(Mathf.Lerp(17f, 10f, t));

                for (int dx = -half; dx <= half; dx++)
                {
                    var tone = dx < -half / 2 ? stoneLit : dx > half / 2 ? stoneDark : stone;

                    // Courses, broken so they do not rule straight across the curve.
                    if ((lift + (dx < 0 ? 0 : 1)) % 4 == 0) tone = Darken(tone, 12);
                    Plot(px, w, h, c.x + dx, c.y + 2 + lift, tone);
                }
            }

            for (int lift = 0; lift < 12; lift++)
            {
                int half = Mathf.RoundToInt(Mathf.Lerp(10f, 1f, lift / 11f));
                for (int dx = -half; dx <= half; dx++)
                    Plot(px, w, h, c.x + dx, c.y + 2 + height + lift, dx < 0 ? capLit : cap);
            }

            // Four sails on a shaft standing out from the cap, set as a saltire: upright and
            // level arms make a cross that reads as scaffolding, and a mill at rest is nearly
            // always left with its sails on the diagonal anyway.
            int hubX = c.x + 6;
            int hubY = c.y + height - 2;
            const int arm = 31;

            for (int blade = 0; blade < 4; blade++)
            {
                float angle = (45f + blade * 90f) * Mathf.Deg2Rad;
                float dirX = Mathf.Cos(angle);
                float dirY = Mathf.Sin(angle) * 0.86f;

                for (int t = 3; t <= arm; t++)
                {
                    int bx = hubX + Mathf.RoundToInt(dirX * t);
                    int by = hubY + Mathf.RoundToInt(dirY * t);

                    Plot(px, w, h, bx, by, sailLit);

                    // Whip and lattice: bars across the frame every third step, plus the cloth
                    // laid over the trailing half of each sail.
                    int span = t < arm - 3 ? 3 : 1;
                    for (int k = 1; k <= span; k++)
                    {
                        int lx = bx + Mathf.RoundToInt(-dirY * k * 1.6f);
                        int ly = by + Mathf.RoundToInt(dirX * k * 0.9f);
                        bool bar = t % 3 == 0;
                        Plot(px, w, h, lx, ly, bar ? sail : k == span ? sail : cloth);
                    }
                }
            }

            for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
                if (dx * dx + dy * dy <= 5) Plot(px, w, h, hubX + dx, hubY + dy, C(0x38, 0x30, 0x24));
        }

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
            var brick = new Color32(0x5C, 0x42, 0x33, 0xFF);
            var brickLit = new Color32(0x72, 0x54, 0x40, 0xFF);
            var mortar = new Color32(0x46, 0x33, 0x28, 0xFF);
            var cap = new Color32(0x24, 0x1C, 0x16, 0xFF);

            var stack = Iso.Project(cw * 0.72f, ch * 0.28f, ox, oy);
            int baseLift = scheme.WallHeight + peak / 2;
            int top = scheme.WallHeight + peak + 14;

            // Seven pixels across rather than nine, coursed, and split into a lit and a shaded
            // face down the corner. A plain slab of one colour at this width was the heaviest
            // thing on any cottage and the eye went straight to it.
            for (int lift = baseLift; lift <= top; lift++)
            for (int dx = -3; dx <= 3; dx++)
            {
                var tone = dx <= -1 ? brickLit : brick;
                int course = lift - baseLift;
                if (course % 3 == 0) tone = mortar;
                else if ((dx + (course / 3) * 2 + 6) % 4 == 0) tone = Darken(tone, 10);
                Plot(px, w, h, stack.x + dx, stack.y + lift, tone);
            }

            for (int dx = -4; dx <= 4; dx++)
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
                case BuildingKind.Field: ground = new Color32(0x86, 0x6E, 0x32, 0xFF); break;
                case BuildingKind.ClayPit: ground = new Color32(0x7C, 0x49, 0x32, 0xFF); break;
                case BuildingKind.Tunnel: ground = new Color32(0x35, 0x2C, 0x22, 0xFF); break;
                default: ground = new Color32(0x69, 0x56, 0x39, 0xFF); break;
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
                    // Standing corn in drilled rows, not flat stripes. A field painted as two
                    // alternating bands of colour reads as a rug; what says "crop" is the bare
                    // earth showing in the gap between rows and the broken texture of the ears.
                    float row = v * 5f;
                    float frac = row - Mathf.Floor(row);
                    int n = Hash(p.x, p.y * 3 + Mathf.FloorToInt(row));

                    if (frac < 0.24f) tone = Darken(ground, 30);
                    else
                    {
                        tone = n % 4 == 0 ? Lighten(ground, 28)
                            : n % 3 == 0 ? Lighten(ground, 10)
                            : ground;
                        if (n % 13 == 0) Plot(px, w, h, p.x, p.y + 6, Lighten(ground, 44));
                    }
                }

                Plot(px, w, h, p.x, p.y + 4, tone);
            }

            if (kind == BuildingKind.ClayPit) Diggings(px, w, h, cw, ch, ox, oy, ground);

            Palings(px, w, h, cw, ch, ox, oy);
            if (kind == BuildingKind.Sawpit) Sawdust(px, w, h, cw, ch, ox, oy);
            if (kind == BuildingKind.Sawpit) PlankStack(px, w, h, cw, ch, ox, oy);
            if (kind == BuildingKind.Sawpit) LogPile(px, w, h, cw, ch, ox, oy);
            if (kind == BuildingKind.Sawpit) SawFrame(px, w, h, cw, ch, ox, oy);
            if (kind == BuildingKind.Field) Sheaves(px, w, h, cw, ch, ox, oy);
        }

        /// <summary>
        /// Cuts the clay pit down into the ground as stepped benches rather than shading its
        /// edges darker.
        ///
        /// The reference's pit is one of the few places in the scene with real depth, and it is
        /// the shape that makes it read: concentric benches dropping to a floor. Darkening a
        /// flat square towards the middle produces a stain, not a hole - there is no step for
        /// the light to catch and nothing casts.
        ///
        /// Only the north and west faces of each bench are drawn. Seen from above at this
        /// angle, the near walls of a pit face away and are hidden by the ground in front of
        /// them; drawing all four is what makes a hole turn inside out and read as a mound.
        /// </summary>
        private static void Diggings(Color32[] px, int w, int h, int cw, int ch, int ox, int oy,
            Color32 ground)
        {
            const int benches = 3;
            const int riser = 5;

            float stepU = cw / (float)(benches * 2 + 2);
            float stepV = ch / (float)(benches * 2 + 2);

            for (int level = 1; level <= benches; level++)
            {
                float insetU = stepU * level;
                float insetV = stepV * level;
                int drop = riser * level;

                var floor = Darken(ground, 8 + level * 11);
                var wall = Darken(ground, 20 + level * 13);
                var lip = Lighten(Darken(ground, level * 9), 12);

                int steps = Mathf.Max(cw, ch) * 150;
                for (int j = 0; j <= steps; j++)
                for (int i = 0; i <= steps; i++)
                {
                    float u = Mathf.Lerp(insetU, cw - insetU, i / (float)steps);
                    float v = Mathf.Lerp(insetV, ch - insetV, j / (float)steps);
                    var p = Iso.Project(u, v, ox, oy);

                    bool atNorth = v - insetV < 0.06f;
                    bool atWest = u - insetU < 0.06f;
                    if (atNorth || atWest)
                    {
                        for (int lift = 1; lift <= riser; lift++)
                            Plot(px, w, h, p.x, p.y + 4 - drop + lift, wall);
                        Plot(px, w, h, p.x, p.y + 5 - drop + riser, lip);
                    }

                    Plot(px, w, h, p.x, p.y + 4 - drop, floor);
                }
            }
        }

        /// <summary>
        /// The saw frame over the pit: two uprights, a head beam and braces, with the log being
        /// worked lying across it.
        ///
        /// A sawpit was a patch of scuffed earth with some timber lying on it, which is the one
        /// thing it cannot be - the reference's mill is read from across the map by the frame
        /// standing over it, and nothing else in this town has that outline.
        /// </summary>
        private static void SawFrame(Color32[] px, int w, int h, int cw, int ch, int ox, int oy)
        {
            var post = new Color32(0x54, 0x3C, 0x24, 0xFF);
            var postLit = new Color32(0x7A, 0x5C, 0x38, 0xFF);
            var beam = new Color32(0x66, 0x4A, 0x2C, 0xFF);
            var beamLit = new Color32(0x8C, 0x6C, 0x42, 0xFF);
            var blade = new Color32(0x9A, 0x9C, 0x98, 0xFF);

            float midV = ch * 0.30f;
            float fromU = cw * 0.20f;
            float toU = cw * 0.80f;
            const int height = 26;

            for (int end = 0; end < 2; end++)
            {
                float u = end == 0 ? fromU : toU;

                // Each upright is a pair of legs straddling the pit. Three pixels across, not
                // one: a single-pixel post at this scale reads as a scratch, and the frame
                // ended up looking like railings with a beam floating over them.
                for (int leg = 0; leg < 2; leg++)
                {
                    var p = Iso.Project(u, midV + (leg == 0 ? -0.30f : 0.30f), ox, oy);
                    for (int lift = 0; lift < height; lift++)
                    {
                        Plot(px, w, h, p.x - 1, p.y + 4 + lift, postLit);
                        Plot(px, w, h, p.x, p.y + 4 + lift, post);
                        Plot(px, w, h, p.x + 1, p.y + 4 + lift, Darken(post, 14));
                    }

                    for (int lift = 0; lift < 3; lift++)
                    for (int foot = -3; foot <= 3; foot++)
                        Plot(px, w, h, p.x + foot, p.y + 4 + lift, Darken(post, 18));

                    for (int brace = 0; brace < 8; brace++)
                    {
                        int bx = p.x + (leg == 0 ? brace : -brace);
                        Plot(px, w, h, bx, p.y + 4 + height - 9 - brace, post);
                        Plot(px, w, h, bx, p.y + 5 + height - 9 - brace, postLit);
                    }
                }
            }

            int steps = Mathf.Max(2, cw) * 140;
            for (int i = 0; i <= steps; i++)
            {
                float u = Mathf.Lerp(fromU - 0.12f, toU + 0.12f, i / (float)steps);

                for (int leg = 0; leg < 2; leg++)
                {
                    var p = Iso.Project(u, midV + (leg == 0 ? -0.32f : 0.32f), ox, oy);
                    Plot(px, w, h, p.x, p.y + 4 + height, beamLit);
                    Plot(px, w, h, p.x, p.y + 3 + height, beam);
                }

                // The log across the frame, and the blade sunk into it.
                var q = Iso.Project(u, midV, ox, oy);
                for (int t = 0; t < 5; t++)
                    Plot(px, w, h, q.x, q.y + 8 + t, t > 2 ? beamLit : beam);
                if (i % 3 == 0) Plot(px, w, h, q.x, q.y + 14, blade);
            }
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

        /// <summary>
        /// Timber stacked as it would be at a mill: courses of logs one on top of another,
        /// ends squared off towards the viewer so the cut faces show.
        ///
        /// Three logs lying flat on the dirt did not read as a stock of timber, and the cut
        /// end is the whole tell - it is the one part of a log that says it has been felled and
        /// crosscut rather than grown where it lies.
        /// </summary>
        /// <summary>
        /// Cut boards stacked to season, on the near side of the pit.
        ///
        /// A sawmill whose yard held one small stack of rounds and nothing else looked closed.
        /// What a working one is full of is the output: squared timber, stacked where it can
        /// be got at, taking up more room than the logs it came from.
        /// </summary>
        private static void PlankStack(Color32[] px, int w, int h, int cw, int ch, int ox, int oy)
        {
            // Freshly sawn, so much paler than the bark and the fencing around it. Timber
            // cut to the same brown as everything else in the yard disappeared into it.
            var top = new Color32(0xC2, 0x9C, 0x60, 0xFF);
            var topSeam = new Color32(0x94, 0x74, 0x44, 0xFF);
            var south = new Color32(0x74, 0x56, 0x34, 0xFF);
            var east = new Color32(0x96, 0x74, 0x46, 0xFF);

            const int lift = 13;
            float u0 = cw * 0.40f;
            float u1 = cw * 0.88f;
            float v0 = ch * 0.05f;
            float v1 = ch * 0.36f;

            const int fill = 150;
            for (int i = 0; i <= fill; i++)
            for (int j = 0; j <= fill; j++)
            {
                float u = Mathf.Lerp(u0, u1, i / (float)fill);
                float v = Mathf.Lerp(v0, v1, j / (float)fill);
                var p = Iso.Project(u, v, ox, oy);

                // Seams between boards run the length of the stack.
                bool seam = Mathf.RoundToInt(v * 11f) % 3 == 0;
                Plot(px, w, h, p.x, p.y + 4 + lift, seam ? topSeam : top);
            }

            // Near faces last, so the ends of the boards read in front of the top.
            for (int i = 0; i <= fill; i++)
            {
                float t = i / (float)fill;
                var ps = Iso.Project(Mathf.Lerp(u0, u1, t), v0, ox, oy);
                var pe = Iso.Project(u1, Mathf.Lerp(v0, v1, t), ox, oy);

                for (int d = 0; d < lift; d++)
                {
                    bool course = d % 3 == 0;
                    Plot(px, w, h, ps.x, ps.y + 4 + d, course ? Darken(south, 16) : south);
                    Plot(px, w, h, pe.x, pe.y + 4 + d, course ? Darken(east, 16) : east);
                }
            }
        }

        /// <summary>Heaps of sawdust and offcuts, swept clear of where the work happens.</summary>
        private static void Sawdust(Color32[] px, int w, int h, int cw, int ch, int ox, int oy)
        {
            var dust = new Color32(0x8E, 0x74, 0x44, 0xFF);
            var dustLit = new Color32(0xA6, 0x8A, 0x54, 0xFF);

            for (int heap = 0; heap < 2; heap++)
            {
                float u = heap == 0 ? cw * 0.86f : cw * 0.12f;
                float v = heap == 0 ? ch * 0.20f : ch * 0.86f;
                var p = Iso.Project(u, v, ox, oy);

                for (int dy = -4; dy <= 4; dy++)
                for (int dx = -9; dx <= 9; dx++)
                {
                    if (dx * dx + dy * dy * 5 > 81) continue;
                    if (Hash(dx + heap * 31, dy) % 7 == 0) continue;
                    Plot(px, w, h, p.x + dx, p.y + 5 + dy, dy < 0 ? dustLit : dust);
                }
            }
        }

        private static void LogPile(Color32[] px, int w, int h, int cw, int ch, int ox, int oy)
        {
            var bark = new Color32(0x54, 0x3C, 0x24, 0xFF);
            var barkLit = new Color32(0x74, 0x56, 0x34, 0xFF);
            var cut = new Color32(0xB4, 0x90, 0x5C, 0xFF);
            var cutRing = new Color32(0x92, 0x72, 0x46, 0xFF);

            const int diameter = 7;
            float startV = ch * 0.66f;

            // Courses narrow as they go up, so the stack has a shoulder rather than a wall.
            int[] perCourse = { 3, 2, 1 };
            for (int course = 0; course < perCourse.Length; course++)
            for (int n = 0; n < perCourse[course]; n++)
            {
                float v = startV + n * 0.30f + course * 0.15f;
                int lift = 5 + course * (diameter - 1);
                if (v > ch - 0.15f) continue;

                int steps = Mathf.Max(2, cw) * 130;
                for (int i = 0; i <= steps; i++)
                {
                    float t = i / (float)steps;
                    float u = Mathf.Lerp(0.22f, cw - 0.22f, t);
                    var p = Iso.Project(u, v, ox, oy);

                    for (int d = 0; d < diameter; d++)
                    {
                        // Lit along the top of the round, dark underneath.
                        var tone = d >= diameter - 2 ? barkLit : d <= 1 ? Darken(bark, 12) : bark;
                        Plot(px, w, h, p.x, p.y + lift + d, tone);
                    }
                }

                // The sawn end facing the viewer: an ellipse, not a bar. A log points along the
                // east axis, so its end presents as a circle squashed by the projection, and
                // squaring it off is what made the stack read as planks on edge.
                var end = Iso.Project(cw - 0.22f, v, ox, oy);
                float radius = diameter / 2f;
                for (int d = -1; d <= diameter; d++)
                for (int across = -1; across <= 5; across++)
                {
                    float fy = (d - radius + 0.5f) / (radius + 0.5f);
                    float fx = (across - 2f) / 3.2f;
                    float r2 = fx * fx + fy * fy;
                    if (r2 > 1f) continue;

                    var tone = r2 > 0.66f ? Darken(cut, 26) : r2 > 0.3f ? cutRing : cut;
                    Plot(px, w, h, end.x + across, end.y + lift + d, tone);
                }
            }
        }

        private static void Sheaves(Color32[] px, int w, int h, int cw, int ch, int ox, int oy)
        {
            var straw = new Color32(0xAE, 0x94, 0x46, 0xFF);
            var strawDark = new Color32(0x84, 0x6E, 0x30, 0xFF);
            var band = new Color32(0x66, 0x52, 0x24, 0xFF);

            for (int i = 0; i < 6; i++)
            {
                int n = Hash(i * 17 + cw, ch * 5 + i);

                // Off the grid they were set out on, and no two the same height. Six identical
                // cones at even spacing looked like a row of tents.
                float u = 0.5f + (i % 3) * (cw - 1f) / 2f + ((n % 7) - 3) * 0.09f;
                float v = 0.5f + (i / 3) * (ch - 1f) + (((n / 7) % 7) - 3) * 0.09f;
                var p = Iso.Project(u, v, ox, oy);

                int top = 13 + (n / 49) % 4;
                for (int lift = 3; lift < top; lift++)
                {
                    float t = (lift - 3) / (float)(top - 3);
                    int spread = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(4f, 0.6f, t * t)));
                    for (int dx = -spread; dx <= spread; dx++)
                    {
                        var tone = dx < 0 ? straw : strawDark;
                        // Twine round the waist of the sheaf, and a ragged butt at the bottom.
                        if (lift == 3 + (top - 3) / 3) tone = band;
                        else if (Hash(p.x + dx, p.y + lift) % 6 == 0) tone = Darken(tone, 18);
                        Plot(px, w, h, p.x + dx, p.y + lift, tone);
                    }
                }
            }
        }

        private static int Hash(int a, int b)
        {
            unchecked
            {
                int n = (a * 73856093) ^ (b * 19349663);
                n = (n ^ (n >> 13)) * 1274126177;
                return (n ^ (n >> 16)) & 0x7FFFFFFF;
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
                    //
                    // Darker than they were. Sampled thatch in the reference sits around
                    // #534b15 and its daub around #6b5f45; ours was near-white plaster under
                    // straw the colour of fresh butter, and with ten of them on screen that
                    // alone was holding the whole frame far brighter than the target.
                    //
                    // Two of the six are roofed in something other than straw. In the reference
                    // no two roofs on screen are the same colour - slate, tile, board and thatch
                    // all appear within a few plots of each other - and a street of identical
                    // roofs is what the eye reads as one building repeated, however much the
                    // walls beneath them differ. Roofs are the largest flat areas in the frame
                    // and the ones seen from directly above, so they carry more of the town's
                    // colour than anything except the ground.
                    // Six of the eight are thatch. Slate and fired tile cost money a cottager
                    // does not have, and giving them an even share turned the street the other
                    // way: hardly a straw roof left, and a row of blue and red that read as a
                    // town from somewhere else entirely.
                    var walls = new[]
                    {
                        C(0x9E, 0x8C, 0x6C), C(0x8C, 0x78, 0x58), C(0xA6, 0x96, 0x78),
                        C(0x84, 0x6E, 0x50), C(0x96, 0x84, 0x62), C(0x90, 0x80, 0x60),
                        C(0x8E, 0x88, 0x7A), C(0x9A, 0x88, 0x66),
                    };
                    var roofs = new[]
                    {
                        C(0x7E, 0x64, 0x32), C(0x70, 0x59, 0x2B), C(0x88, 0x70, 0x3A),
                        C(0x67, 0x51, 0x27), C(0x76, 0x5C, 0x2E), C(0x82, 0x6A, 0x36),
                        C(0x46, 0x4E, 0x56), // slate
                        C(0x7C, 0x40, 0x2C), // fired tile
                    };
                    bool straw = variant < 6;
                    return new Scheme
                    {
                        Wall = walls[variant], Roof = roofs[variant],
                        Timber = C(0x59, 0x3E, 0x28), Plinth = C(0x64, 0x5E, 0x54),
                        WallHeight = 24 + (variant % 4) * 2, Pitch = 13 + (variant & 1) * 3,
                        Thatch = straw, HalfTimbered = true, Chimney = true,
                        LeanTo = variant == 1 || variant == 2 || variant == 5 || variant == 7,
                        Porch = variant == 0 || variant == 3 || variant == 4 || variant == 6,
                        CrossGable = variant == 2 || variant == 5 || variant == 7,
                    };
                }
                case BuildingKind.TownHall:
                    return new Scheme
                    {
                        Porch = true,
                        Wall = C(0xA4, 0x98, 0x80), Roof = C(0x82, 0x38, 0x28),
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
                        Wall = C(0x80, 0x60, 0x3A), Roof = C(0x3E, 0x52, 0x3C),
                        Timber = C(0x4E, 0x36, 0x22), Plinth = C(0x60, 0x5A, 0x50),
                        WallHeight = 32, Pitch = 14, HalfTimbered = true, Chimney = true,
                        LeanTo = true, Mill = true,
                    };
                case BuildingKind.Still:
                    return new Scheme
                    {
                        Wall = C(0x56, 0x44, 0x30), Roof = C(0x8A, 0x5A, 0x26),
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
                        Wall = C(0x54, 0x42, 0x2C), Roof = C(0x72, 0x58, 0x32),
                        Timber = C(0x3A, 0x2A, 0x18), Plinth = C(0x44, 0x3C, 0x30),
                        WallHeight = 16, Pitch = 6, Thatch = true,
                    };
                default:
                    return new Scheme
                    {
                        Wall = C(0x7C, 0x64, 0x44), Roof = C(0x5E, 0x46, 0x2E),
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
