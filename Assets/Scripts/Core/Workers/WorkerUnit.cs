using System.Collections.Generic;

namespace Worker.Core
{
    /// <summary>
    /// One employee. Workers are the scarce resource of the whole game: they have
    /// names, they get tired, and replacing them with machines is the central
    /// tension the design is built around.
    /// </summary>
    public sealed class WorkerUnit
    {
        public const int MaxStamina = 10000;
        public const int MaxMorale = 10000;

        public readonly int Id;
        public string Name;

        public GridPos Pos;

        /// <summary>0..<see cref="MaxStamina"/>. Drains while working, refills while resting.</summary>
        public int Stamina;

        /// <summary>0..<see cref="MaxMorale"/>. Drives work speed and quit risk.</summary>
        public int Morale;

        /// <summary>Movement and labour rate, 100 = baseline. Grows slowly with experience.</summary>
        public int SpeedRating;

        /// <summary>Wage per in-game day, in cents.</summary>
        public int DailyWage;

        /// <summary>Total ticks this worker has spent producing, used for experience and stats.</summary>
        public int LifetimeWorkTicks;

        public ItemStack Carried;

        public WorkerTask Task;

        public readonly List<GridPos> Path = new List<GridPos>();
        public int PathCursor;

        /// <summary>Counts down between tile steps; a step happens when it reaches zero.</summary>
        public int MoveCooldown;

        /// <summary>
        /// Ticks the current tile step started with. The simulation itself is discrete,
        /// so the presentation layer divides this into <see cref="MoveCooldown"/> to
        /// interpolate the figure smoothly between tiles.
        /// </summary>
        public int MoveDuration;

        /// <summary>Where the worker is walking to right now, or their current tile when standing still.</summary>
        public GridPos NextStep => PathCursor < Path.Count ? Path[PathCursor] : Pos;

        /// <summary>Progress towards <see cref="NextStep"/> in permille, for interpolation.</summary>
        public int StepProgressPermille
        {
            get
            {
                if (MoveDuration <= 0 || PathCursor >= Path.Count) return 0;
                int elapsed = MoveDuration - MoveCooldown;
                if (elapsed < 0) elapsed = 0;
                if (elapsed > MoveDuration) elapsed = MoveDuration;
                return elapsed * 1000 / MoveDuration;
            }
        }

        public WorkerUnit(int id, string name, GridPos pos, int speedRating, int dailyWage)
        {
            Id = id;
            Name = name;
            Pos = pos;
            SpeedRating = speedRating;
            DailyWage = dailyWage;
            Stamina = MaxStamina;
            Morale = MaxMorale * 3 / 4;
        }

        public bool IsIdle => Task == null;
        public bool HasPath => PathCursor < Path.Count;

        /// <summary>
        /// Effective work rate in percent. Tired or demoralised workers are slower;
        /// this is the number the automation-versus-people trade-off is balanced on.
        /// </summary>
        public int EffectiveRate()
        {
            int staminaFactor = 50 + (Stamina * 50) / MaxStamina;   // 50..100
            int moraleFactor = 60 + (Morale * 40) / MaxMorale;      // 60..100
            int rate = SpeedRating * staminaFactor / 100 * moraleFactor / 100;
            return rate < 10 ? 10 : rate;
        }

        public void ClearPath()
        {
            Path.Clear();
            PathCursor = 0;
            MoveCooldown = 0;
            MoveDuration = 0;
        }

        public void AbandonTask()
        {
            Task = null;
            ClearPath();
        }

        public void AdjustStamina(int delta)
        {
            Stamina += delta;
            if (Stamina < 0) Stamina = 0;
            else if (Stamina > MaxStamina) Stamina = MaxStamina;
        }

        public void AdjustMorale(int delta)
        {
            Morale += delta;
            if (Morale < 0) Morale = 0;
            else if (Morale > MaxMorale) Morale = MaxMorale;
        }

        public override string ToString() => Name + "#" + Id + "@" + Pos;
    }
}
