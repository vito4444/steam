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

            // North side of the road, west to east.
            PlaceNear(town, BuildingKind.TownHall, map.Width / 2 - 8, roadY + 2);
            PlaceNear(town, BuildingKind.Warehouse, map.Width / 2 - 3, roadY + 2);
            PlaceNear(town, BuildingKind.Brewery, map.Width / 2 + 1, roadY + 2);
            PlaceNear(town, BuildingKind.Sawpit, map.Width / 2 + 7, roadY + 2);

            // South side.
            PlaceNear(town, BuildingKind.House, map.Width / 2 - 8, roadY - 4);
            PlaceNear(town, BuildingKind.House, map.Width / 2 - 5, roadY - 4);
            PlaceNear(town, BuildingKind.House, map.Width / 2 - 2, roadY - 4);
            PlaceNear(town, BuildingKind.Field, map.Width / 2 + 2, roadY - 4);
            PlaceNear(town, BuildingKind.Field, map.Width / 2 + 6, roadY - 4);
            PlaceNear(town, BuildingKind.ClayPit, map.Width / 2 - 12, roadY - 3);

            FoundHiddenWorks(town, map.Width / 2 + 2, roadY + 3);

            town.Record("the town is yours");
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
