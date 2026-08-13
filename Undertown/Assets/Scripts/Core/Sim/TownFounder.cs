using Undertown.Core.Buildings;
using Undertown.Core.Economy;
using Undertown.Core.World;

namespace Undertown.Core.Sim
{
    /// <summary>
    /// Lays out the town the player inherits on the first morning: a lawful settlement
    /// strung along the imperial road, plus one small chamber under the brewery that the
    /// previous mayor never declared. Starting with the illicit side already open, rather
    /// than making the player dig their first tunnel blind, puts the central tension on
    /// screen from the first minute.
    /// </summary>
    public static class TownFounder
    {
        public static void Found(TownState town)
        {
            var map = town.Map;
            int roadY = FindRoadRow(map);
            int cx = map.Width / 2;

            // Laid out over a roughly square block of cells rather than strung along the road.
            //
            // This is a constraint the projection imposes on the layout, not a matter of
            // taste. A row of buildings running east-west becomes a diagonal band on screen,
            // because +x goes down-right and +y goes up-right; a settlement built along one
            // street ends up as a thin line across the corner of the display with empty
            // pasture on either side of it. Spreading the same buildings over equal spans of
            // x and y makes a compact diamond, which is what a town is supposed to look like
            // from here.
            Pave(map, cx - 8, roadY - 1, cx + 11, roadY, TileKind.Road);
            Pave(map, cx - 1, roadY - 5, cx, roadY + 11, TileKind.Road);
            Pave(map, cx + 5, roadY, cx + 6, roadY + 11, TileKind.Dirt);
            Pave(map, cx - 6, roadY + 5, cx + 9, roadY + 6, TileKind.Dirt);

            // The civic block, north of the street.
            PlaceNear(town, BuildingKind.TownHall, cx - 6, roadY + 1);
            PlaceNear(town, BuildingKind.Warehouse, cx + 1, roadY + 1);
            PlaceNear(town, BuildingKind.Brewery, cx + 7, roadY + 1);

            // Dwellings behind it, off the lane.
            PlaceNear(town, BuildingKind.House, cx - 6, roadY + 7);
            PlaceNear(town, BuildingKind.House, cx - 3, roadY + 7);
            PlaceNear(town, BuildingKind.House, cx + 1, roadY + 7);
            PlaceNear(town, BuildingKind.House, cx + 7, roadY + 7);
            PlaceNear(town, BuildingKind.Sawpit, cx + 7, roadY + 10);

            // Working ground south of the street, where the fields have room.
            PlaceNear(town, BuildingKind.House, cx - 6, roadY - 4);
            PlaceNear(town, BuildingKind.ClayPit, cx - 3, roadY - 4);
            PlaceNear(town, BuildingKind.Field, cx + 1, roadY - 4);
            PlaceNear(town, BuildingKind.Field, cx + 6, roadY - 4);

            // Yards: trodden earth around everything that was built, which is what stops the
            // buildings looking as though they were dropped onto untouched pasture.
            TreadYards(town);

            FoundHiddenWorks(town, cx + 7, roadY + 3);

            town.Record("the town is yours");
        }

        /// <summary>
        /// Lays a surface over a rectangle, leaving water and anything already harder than
        /// grass alone: a road does not run through the river, and paving over the clay you
        /// were going to dig would be self-defeating.
        /// </summary>
        private static void Pave(GridMap map, int x0, int y0, int x1, int y1, TileKind surface)
        {
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                var cell = new Coord(x, y);
                if (!map.InBounds(cell)) continue;

                var existing = map.Get(cell);
                if (existing != TileKind.Grass && existing != TileKind.Forest) continue;
                map.Set(cell, surface);
            }
        }

        /// <summary>Beats a ring of bare earth around every building above ground.</summary>
        private static void TreadYards(TownState town)
        {
            var map = town.Map;
            foreach (var building in town.Buildings)
            {
                var def = building.Def;
                if (def == null || def.Underground) continue;

                Pave(map,
                    building.Origin.X - 1, building.Origin.Y - 1,
                    building.Origin.X + def.Width, building.Origin.Y + def.Height,
                    TileKind.Dirt);
            }
        }

        private static int FindRoadRow(GridMap map)
        {
            for (int y = 0; y < map.Height; y++)
                if (map.Get(new Coord(map.Width / 2, y)) == TileKind.Road) return y;
            return map.Height / 2;
        }

        /// <summary>
        /// Tries the requested spot, then spirals outwards. Terrain is generated, so a
        /// hand-picked coordinate can land in the river; the layout has to bend around it.
        /// </summary>
        private static Building PlaceNear(TownState town, BuildingKind kind, int x, int y, int searchRadius = 6)
        {
            for (int radius = 0; radius <= searchRadius; radius++)
            for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (radius > 0 && Abs(dx) != radius && Abs(dy) != radius) continue;
                var building = BuildingPlacement.Place(town, kind, new Coord(x + dx, y + dy), payCost: false);
                if (building != null) return building;
            }
            return null;
        }

        /// <summary>
        /// One shaft down from beside the brewery, a short run of tunnel, and a still at the
        /// end of it. Deliberately shallow: a level 1 inspector can already hear it, so the
        /// player's first real decision is whether to dig deeper or wall it off.
        /// </summary>
        private static void FoundHiddenWorks(TownState town, int x, int y)
        {
            var map = town.Map;

            var shaft = new Coord(x, y, 1);
            if (!map.InBounds(shaft)) return;

            int spoil = 0;
            for (int dx = 0; dx < 6; dx++)
            for (int dy = 0; dy < 3; dy++)
                spoil += map.Excavate(new Coord(x + dx, y + dy, 1));

            town.SurfaceSpoil += spoil;

            // The still and the cellar store are both two by two, so they are placed on
            // separate rows of the excavation. Overlapping them made the store fail silently
            // and quietly removed the town's only hidden storage.
            Require(town, BuildingKind.HiddenEntrance, shaft);
            Require(town, BuildingKind.Tunnel, new Coord(x + 1, y, 1));
            Require(town, BuildingKind.Tunnel, new Coord(x + 2, y, 1));
            Require(town, BuildingKind.Still, new Coord(x + 3, y, 1));
            Require(town, BuildingKind.UnderStore, new Coord(x, y + 1, 1));

            town.Record($"an undeclared chamber under the brewery, {spoil} loads of spoil to explain");
        }

        /// <summary>
        /// Places a building that the starting layout depends on, and writes it into the log
        /// if it could not go down. A silent failure here removes a whole mechanic from the
        /// game without anything appearing to be wrong.
        /// </summary>
        private static Building Require(TownState town, BuildingKind kind, Coord origin)
        {
            var building = BuildingPlacement.Place(town, kind, origin, payCost: false);
            if (building == null)
                town.Record($"could not found {kind} at {origin}: {BuildingPlacement.Check(town, kind, origin, false)}");
            return building;
        }

        private static int Abs(int v) => v < 0 ? -v : v;
    }
}
