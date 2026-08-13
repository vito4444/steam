using Undertown.Core.Economy;
using Undertown.Core.Sim;
using Undertown.Core.World;

namespace Undertown.Core.Buildings
{
    public enum PlacementResult
    {
        Ok,
        UnknownBuilding,
        WrongLayer,
        OutOfBounds,
        BlockedTerrain,
        Overlapping,
        Unaffordable,
    }

    public static class BuildingPlacement
    {
        public static PlacementResult Check(TownState town, BuildingKind kind, Coord origin, bool checkCost = true)
        {
            var def = BuildingCatalog.Get(kind);
            if (def == null) return PlacementResult.UnknownBuilding;

            bool wantsUnderground = def.Underground;
            if (wantsUnderground == origin.IsSurface) return PlacementResult.WrongLayer;

            for (int dy = 0; dy < def.Height; dy++)
            for (int dx = 0; dx < def.Width; dx++)
            {
                var cell = origin.Offset(dx, dy);
                if (!town.Map.InBounds(cell)) return PlacementResult.OutOfBounds;
                if (!IsBuildable(town.Map.Get(cell), wantsUnderground)) return PlacementResult.BlockedTerrain;
                if (BuildingAt(town, cell) != null) return PlacementResult.Overlapping;
            }

            if (checkCost && !CanAfford(town, def)) return PlacementResult.Unaffordable;
            return PlacementResult.Ok;
        }

        /// <summary>
        /// Underground structures go in cavities the player has already paid to dig, which is
        /// what makes excavation the real cost of the illicit side rather than the buildings.
        /// </summary>
        private static bool IsBuildable(TileKind kind, bool underground) =>
            underground ? Tiles.IsOpenUnderground(kind) : Tiles.IsWalkableSurface(kind);

        public static bool CanAfford(TownState town, BuildingDef def)
        {
            for (int i = 0; i < def.Cost.Length; i++)
                if (!town.Stock.Has(def.Cost[i].Material, def.Cost[i].Amount)) return false;
            return true;
        }

        public static Building BuildingAt(TownState town, Coord cell)
        {
            var buildings = town.Buildings;
            for (int i = 0; i < buildings.Count; i++)
                if (buildings[i].Covers(cell)) return buildings[i];
            return null;
        }

        /// <summary>
        /// Places the building and pays for it. Materials spent on lawful construction are
        /// recorded; materials spent below ground are not, which is one more quiet hole in
        /// the timber account for the player to explain later.
        /// </summary>
        public static Building Place(TownState town, BuildingKind kind, Coord origin, bool payCost = true)
        {
            var result = Check(town, kind, origin, payCost);
            if (result != PlacementResult.Ok) return null;

            var def = BuildingCatalog.Get(kind);
            if (payCost)
            {
                for (int i = 0; i < def.Cost.Length; i++)
                {
                    var cost = def.Cost[i];
                    town.Stock.TryTake(cost.Material, cost.Amount);
                    if (!def.Illicit) town.Books.RecordConsumption(cost.Material, cost.Amount);
                }
            }

            if (!def.Underground)
            {
                // Surface construction lays a footing, which also stops the tile being
                // mistaken for open ground by anything that walks over it.
                for (int dy = 0; dy < def.Height; dy++)
                for (int dx = 0; dx < def.Width; dx++)
                    town.Map.Set(origin.Offset(dx, dy), TileKind.Dirt);
            }

            var building = new Building(kind, origin) { AssignedWorkers = def.WorkerSlots };
            town.Buildings.Add(building);
            return building;
        }
    }
}
