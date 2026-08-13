using System;
using System.Collections.Generic;
using Undertown.Core.World;

namespace Undertown.Core.Agents
{
    public enum InspectorTask : byte
    {
        Arriving,
        Patrolling,
        Sounding,
        Auditing,
        Interrogating,
        Departing,
        Gone,
    }

    /// <summary>
    /// An imperial inspector working through his checklist. He walks the roads, taps the
    /// ground, reads the books and asks the townsfolk questions - four separate ways of
    /// finding the same thing, which is why no single countermeasure is sufficient.
    /// </summary>
    [Serializable]
    public sealed class Inspector
    {
        public readonly int Level;
        public Coord Position;
        public InspectorTask Task = InspectorTask.Arriving;

        public List<Coord> Path;
        public int PathIndex;
        public int MoveProgress;

        /// <summary>Ticks spent standing still on the current tap, audit or interview.</summary>
        public int TaskProgress;

        public readonly List<string> Findings = new List<string>();

        public const int TicksPerStep = 7;
        public const int SoundingTicks = 30;

        public Inspector(int level, Coord entry)
        {
            Level = Math.Max(1, level);
            Position = entry;
        }

        /// <summary>How deep this inspector can hear a hollow. A level 5 man reaches five metres.</summary>
        public int SoundingDepth => Math.Min(5, 1 + Level);

        /// <summary>
        /// Chance in per mille that a false wall fools him. A bored clerk is easy; a senior
        /// inspector has knocked on a lot of walls.
        /// </summary>
        public int FalseWallSuccessPerMille
        {
            get
            {
                switch (Level)
                {
                    case 1: return 950;
                    case 2: return 850;
                    case 3: return 700;
                    case 4: return 550;
                    default: return 400;
                }
            }
        }

        public void SetPath(List<Coord> path)
        {
            Path = path;
            PathIndex = 0;
            MoveProgress = 0;
        }

        public bool HasDestination => Path != null && PathIndex < Path.Count - 1;
    }
}
