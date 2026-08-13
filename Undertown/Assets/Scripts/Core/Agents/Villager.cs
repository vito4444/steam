using System;
using System.Collections.Generic;
using Undertown.Core.Buildings;
using Undertown.Core.World;

namespace Undertown.Core.Agents
{
    public enum VillagerRole : byte
    {
        Labourer = 0,
        Brewer = 1,
        Farmer = 2,
        Sawyer = 3,
        Digger = 4,
    }

    /// <summary>
    /// One of the townsfolk. Loyalty is the quiet resource: an inspector who questions a
    /// well-paid, well-housed worker learns nothing, and one who questions a resentful one
    /// learns where the chamber is.
    /// </summary>
    [Serializable]
    public sealed class Villager
    {
        public readonly string Name;
        public VillagerRole Role;
        public Coord Position;

        /// <summary>0 to 100. Below <see cref="TalksThreshold"/> they will answer questions honestly.</summary>
        public int Loyalty = 70;

        /// <summary>Whether this worker knows about the undeclared works. Only those who go down there do.</summary>
        public bool KnowsAboutTheWorks;

        /// <summary>Consecutive days without a ration. Hunger compounds rather than accruing flatly.</summary>
        public int DaysHungry;

        public bool Housed = true;
        public bool Paid = true;

        public const int TalksThreshold = 35;

        public List<Coord> Path;
        public int PathIndex;

        /// <summary>Ticks of walking accumulated toward the next step.</summary>
        public int MoveProgress;

        /// <summary>Ticks needed per tile. Slow enough that travel time is a real cost of digging far away.</summary>
        public const int TicksPerStep = 9;

        public Villager(string name, VillagerRole role, Coord position)
        {
            Name = name;
            Role = role;
            Position = position;
        }

        public bool WillTalk => Loyalty < TalksThreshold;

        /// <summary>The grievance an inspector would hear about first, or null if there is none.</summary>
        public string Grievance
        {
            get
            {
                if (DaysHungry > 0) return DaysHungry == 1 ? "hungry" : $"hungry {DaysHungry} days";
                if (!Paid) return "unpaid";
                if (!Housed) return "no bed";
                return null;
            }
        }

        public bool Underground => !Position.IsSurface;

        public void SetPath(List<Coord> path)
        {
            Path = path;
            PathIndex = 0;
            MoveProgress = 0;
        }

        public bool HasDestination => Path != null && PathIndex < Path.Count - 1;

        public Coord Destination => Path != null && Path.Count > 0 ? Path[Path.Count - 1] : Position;
    }
}
