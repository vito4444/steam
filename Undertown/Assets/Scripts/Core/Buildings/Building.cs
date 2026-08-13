using System;
using Undertown.Core.World;

namespace Undertown.Core.Buildings
{
    /// <summary>One placed structure and how far through its current production cycle it is.</summary>
    [Serializable]
    public sealed class Building
    {
        public readonly BuildingKind Kind;
        public readonly Coord Origin;

        /// <summary>Work accumulated towards the next output, in ticks.</summary>
        public int Progress;

        public int AssignedWorkers;

        /// <summary>
        /// Stalled because the input material ran out. Surfaced to the player, since a
        /// silently idle still is indistinguishable from a working one at a glance.
        /// </summary>
        public bool Starved;

        public Building(BuildingKind kind, Coord origin)
        {
            Kind = kind;
            Origin = origin;
        }

        public BuildingDef Def => BuildingCatalog.Get(Kind);

        public bool Working => AssignedWorkers > 0;

        /// <summary>Starts or stops the workshop. Returns the new state.</summary>
        public bool ToggleWork()
        {
            var def = Def;
            if (def == null || def.WorkerSlots <= 0) return false;

            AssignedWorkers = Working ? 0 : def.WorkerSlots;
            if (!Working) Starved = false;
            return Working;
        }

        public bool Covers(Coord cell)
        {
            var def = Def;
            if (def == null || cell.Depth != Origin.Depth) return false;
            return cell.X >= Origin.X && cell.X < Origin.X + def.Width &&
                   cell.Y >= Origin.Y && cell.Y < Origin.Y + def.Height;
        }
    }
}
