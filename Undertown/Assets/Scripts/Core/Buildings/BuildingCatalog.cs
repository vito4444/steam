using System.Collections.Generic;
using Undertown.Core.Economy;

namespace Undertown.Core.Buildings
{
    /// <summary>Persisted in saves, so existing values must keep their numbers.</summary>
    public enum BuildingKind : byte
    {
        None = 0,

        TownHall = 1,
        Sawpit = 2,
        ClayPit = 3,
        Field = 4,
        Brewery = 5,
        Warehouse = 6,
        House = 7,

        Tunnel = 20,
        Still = 21,
        UnderStore = 22,
        FalseWall = 23,
        HiddenEntrance = 24,
    }

    public struct MaterialCost
    {
        public MaterialId Material;
        public int Amount;

        public MaterialCost(MaterialId material, int amount)
        {
            Material = material;
            Amount = amount;
        }
    }

    public sealed class BuildingDef
    {
        public BuildingKind Kind;
        public string Name;
        public string Blurb;
        public int Width = 1;
        public int Height = 1;
        public bool Underground;
        public MaterialCost[] Cost = new MaterialCost[0];

        public MaterialId Input = MaterialId.None;
        public int InputPerCycle;
        public MaterialId Output = MaterialId.None;
        public int OutputPerCycle;

        /// <summary>Ticks of work for one production cycle. Zero means the building produces nothing.</summary>
        public int CycleTicks;

        public int WorkerSlots;

        /// <summary>
        /// Whether the empire is told this building exists. Undeclared surface buildings are
        /// absurd - an inspector walking past would see them - so only underground works can
        /// be hidden, and their output never reaches the books.
        /// </summary>
        public bool Illicit;

        public bool Produces => CycleTicks > 0 && Output != MaterialId.None;
    }

    /// <summary>
    /// Every building in the vertical slice. The economy is deliberately narrow: one legal
    /// chain (grain into ale) and one illicit chain (grain into moonshine) sharing an input,
    /// because that shared input is what makes the audit bite.
    /// </summary>
    public static class BuildingCatalog
    {
        private static readonly Dictionary<BuildingKind, BuildingDef> Defs = Build();

        public static BuildingDef Get(BuildingKind kind) => Defs.TryGetValue(kind, out var def) ? def : null;

        public static IEnumerable<BuildingDef> All => Defs.Values;

        private static Dictionary<BuildingKind, BuildingDef> Build()
        {
            var all = new[]
            {
                new BuildingDef
                {
                    Kind = BuildingKind.TownHall, Name = "Town Hall", Width = 3, Height = 3,
                    Blurb = "Where the tax return is written.",
                    Cost = new[] { new MaterialCost(MaterialId.Timber, 40), new MaterialCost(MaterialId.Clay, 20) },
                    WorkerSlots = 1,
                },
                new BuildingDef
                {
                    Kind = BuildingKind.Sawpit, Name = "Sawpit", Width = 2, Height = 2,
                    Blurb = "Turns standing woodland into timber.",
                    Cost = new[] { new MaterialCost(MaterialId.Timber, 15) },
                    Output = MaterialId.Timber, OutputPerCycle = 4, CycleTicks = 180, WorkerSlots = 2,
                },
                new BuildingDef
                {
                    Kind = BuildingKind.ClayPit, Name = "Clay Pit", Width = 2, Height = 2,
                    Blurb = "Digs clay from the riverbank.",
                    Cost = new[] { new MaterialCost(MaterialId.Timber, 12) },
                    Output = MaterialId.Clay, OutputPerCycle = 3, CycleTicks = 200, WorkerSlots = 2,
                },
                new BuildingDef
                {
                    Kind = BuildingKind.Field, Name = "Field", Width = 3, Height = 2,
                    Blurb = "Grain. Every chain in this town starts here.",
                    Cost = new[] { new MaterialCost(MaterialId.Timber, 8) },
                    Output = MaterialId.Grain, OutputPerCycle = 6, CycleTicks = 240, WorkerSlots = 2,
                },
                new BuildingDef
                {
                    Kind = BuildingKind.Brewery, Name = "Brewery", Width = 3, Height = 2,
                    Blurb = "Two grain to one ale, and the cover for everything below.",
                    Cost = new[] { new MaterialCost(MaterialId.Timber, 30), new MaterialCost(MaterialId.Clay, 15) },
                    Input = MaterialId.Grain, InputPerCycle = 2,
                    Output = MaterialId.Ale, OutputPerCycle = 1, CycleTicks = 90, WorkerSlots = 3,
                },
                new BuildingDef
                {
                    Kind = BuildingKind.Warehouse, Name = "Warehouse", Width = 2, Height = 3,
                    Blurb = "Where the inspector counts.",
                    Cost = new[] { new MaterialCost(MaterialId.Timber, 25) },
                    WorkerSlots = 1,
                },
                new BuildingDef
                {
                    Kind = BuildingKind.House, Name = "House", Width = 2, Height = 2,
                    Blurb = "Houses two. Unhoused townsfolk talk.",
                    Cost = new[] { new MaterialCost(MaterialId.Timber, 18), new MaterialCost(MaterialId.Clay, 6) },
                },

                new BuildingDef
                {
                    Kind = BuildingKind.Tunnel, Name = "Tunnel", Underground = true,
                    Blurb = "Connects chambers. Sounds hollow if it is shallow.",
                    Cost = new[] { new MaterialCost(MaterialId.Timber, 2) },
                    Illicit = true,
                },
                new BuildingDef
                {
                    Kind = BuildingKind.Still, Name = "Still", Width = 2, Height = 2, Underground = true,
                    Blurb = "Three grain to one moonshine. Nothing about it reaches the books.",
                    Cost = new[] { new MaterialCost(MaterialId.Timber, 20), new MaterialCost(MaterialId.Clay, 10) },
                    Input = MaterialId.Grain, InputPerCycle = 3,
                    Output = MaterialId.Moonshine, OutputPerCycle = 1, CycleTicks = 140, WorkerSlots = 2,
                    Illicit = true,
                },
                new BuildingDef
                {
                    Kind = BuildingKind.UnderStore, Name = "Cellar Store", Width = 2, Height = 2, Underground = true,
                    Blurb = "Keeps contraband out of the warehouse count.",
                    Cost = new[] { new MaterialCost(MaterialId.Timber, 14) },
                    Illicit = true,
                },
                new BuildingDef
                {
                    Kind = BuildingKind.FalseWall, Name = "False Wall", Underground = true,
                    Blurb = "Reads as solid earth when tapped. Usually.",
                    Cost = new[] { new MaterialCost(MaterialId.Timber, 6), new MaterialCost(MaterialId.Clay, 4) },
                    Illicit = true,
                },
                new BuildingDef
                {
                    Kind = BuildingKind.HiddenEntrance, Name = "Hidden Entrance", Underground = true,
                    Blurb = "A shaft that comes up inside a cellar rather than in the open.",
                    Cost = new[] { new MaterialCost(MaterialId.Timber, 10), new MaterialCost(MaterialId.Clay, 8) },
                    Illicit = true,
                },
            };

            var map = new Dictionary<BuildingKind, BuildingDef>(all.Length);
            foreach (var def in all) map[def.Kind] = def;
            return map;
        }
    }
}
