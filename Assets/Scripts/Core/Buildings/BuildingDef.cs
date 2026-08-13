using System;
using System.Collections.Generic;

namespace Worker.Core
{
    public sealed class BuildingDef
    {
        public readonly BuildingKind Kind;
        public readonly string DisplayKey;
        public readonly int Width;
        public readonly int Height;

        /// <summary>Whether workers can walk over the footprint. Conveyors are walkable, benches are not.</summary>
        public readonly bool Walkable;

        /// <summary>Stations need a worker standing on an adjacent tile to make progress.</summary>
        public readonly bool IsStation;

        public readonly int InputSlots;
        public readonly int OutputSlots;

        /// <summary>Build cost in cents.</summary>
        public readonly int Cost;

        public BuildingDef(BuildingKind kind, string displayKey, int width, int height,
            bool walkable, bool isStation, int inputSlots, int outputSlots, int cost)
        {
            Kind = kind;
            DisplayKey = displayKey;
            Width = width;
            Height = height;
            Walkable = walkable;
            IsStation = isStation;
            InputSlots = inputSlots;
            OutputSlots = outputSlots;
            Cost = cost;
        }

        public int FootprintArea => Width * Height;
    }

    public static class BuildingData
    {
        private static readonly BuildingDef[] Defs =
        {
            //                                              w  h  walk  station in out  cost
            new BuildingDef(BuildingKind.Intake,        "building.intake",         2, 2, false, false, 0, 4, 0),
            new BuildingDef(BuildingKind.Storage,       "building.storage",        1, 1, false, false, 0, 4, 4000),
            new BuildingDef(BuildingKind.Sawbench,      "building.sawbench",       2, 2, false, true,  3, 2, 25000),
            new BuildingDef(BuildingKind.Lathe,         "building.lathe",          2, 2, false, true,  3, 2, 32000),
            new BuildingDef(BuildingKind.AssemblyBench, "building.assembly_bench", 2, 2, false, true,  4, 2, 40000),
            new BuildingDef(BuildingKind.Shipping,      "building.shipping",       2, 2, false, false, 6, 0, 0),
            new BuildingDef(BuildingKind.Conveyor,      "building.conveyor",       1, 1, true,  false, 0, 0, 1200),
            new BuildingDef(BuildingKind.BreakRoom,     "building.break_room",     2, 2, false, false, 0, 0, 15000),
            new BuildingDef(BuildingKind.Wall,          "building.wall",           1, 1, false, false, 0, 0, 500)
        };

        private static readonly Dictionary<BuildingKind, BuildingDef> Lookup = Build();

        private static Dictionary<BuildingKind, BuildingDef> Build()
        {
            var map = new Dictionary<BuildingKind, BuildingDef>(Defs.Length);
            foreach (var def in Defs) map[def.Kind] = def;
            return map;
        }

        public static BuildingDef Get(BuildingKind kind)
        {
            if (Lookup.TryGetValue(kind, out var def)) return def;
            throw new ArgumentOutOfRangeException(nameof(kind), "No BuildingDef registered for " + kind);
        }

        public static IReadOnlyList<BuildingDef> All => Defs;
    }
}
