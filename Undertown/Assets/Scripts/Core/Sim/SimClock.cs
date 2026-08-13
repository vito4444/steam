using System;

namespace Undertown.Core.Sim
{
    /// <summary>
    /// The town's calendar. Every decision in the game is timed against it: how many days
    /// of digging remain before the next inspection, and whether it is late enough that a
    /// worker crossing the yard would be noticed.
    /// </summary>
    [Serializable]
    public sealed class SimClock
    {
        public const int MinutesPerHour = 60;
        public const int HoursPerDay = 24;
        public const int TicksPerDay = MinutesPerHour * HoursPerDay;
        public const int DaysPerSeason = 30;

        public const int CurfewStartHour = 22;
        public const int CurfewEndHour = 6;

        /// <summary>Days of the season on which an inspector arrives, one-based as the player sees them.</summary>
        public static readonly int[] InspectionDays = { 10, 20, 30 };

        public int Tick { get; private set; }

        public void Advance(int ticks = 1)
        {
            if (ticks < 0) throw new ArgumentOutOfRangeException(nameof(ticks));
            Tick += ticks;
        }

        public int TotalDays => Tick / TicksPerDay;
        public int Season => TotalDays / DaysPerSeason;

        /// <summary>One-based day within the season, so it reads 1 to 30 on the clock face.</summary>
        public int DayOfSeason => TotalDays % DaysPerSeason + 1;

        public int MinuteOfDay => Tick % TicksPerDay;
        public int Hour => MinuteOfDay / MinutesPerHour;
        public int Minute => MinuteOfDay % MinutesPerHour;

        public bool IsCurfew => Hour >= CurfewStartHour || Hour < CurfewEndHour;

        /// <summary>The last inspection day of a season is the full audit, not a routine visit.</summary>
        public static bool IsAuditDay(int dayOfSeason) => dayOfSeason == DaysPerSeason;

        public bool IsInspectionToday => Array.IndexOf(InspectionDays, DayOfSeason) >= 0;

        /// <summary>Zero on the day itself. Never negative: it rolls into the next season.</summary>
        public int DaysUntilInspection
        {
            get
            {
                int today = DayOfSeason;
                for (int i = 0; i < InspectionDays.Length; i++)
                    if (InspectionDays[i] >= today) return InspectionDays[i] - today;

                return DaysPerSeason - today + InspectionDays[0];
            }
        }

        public int NextInspectionDay
        {
            get
            {
                int today = DayOfSeason;
                for (int i = 0; i < InspectionDays.Length; i++)
                    if (InspectionDays[i] >= today) return InspectionDays[i];
                return InspectionDays[0];
            }
        }

        /// <summary>Watch names give the night a texture the raw hour does not.</summary>
        public string PhaseName
        {
            get
            {
                int h = Hour;
                if (h < 3) return "Dead of Night";
                if (h < 6) return "Third Watch";
                if (h < 9) return "Dawn";
                if (h < 12) return "Morning";
                if (h < 14) return "Midday";
                if (h < 18) return "Afternoon";
                if (h < 22) return "Evening";
                return "First Watch";
            }
        }

        public string TimeOfDayLabel => $"{Hour:D2}:{Minute:D2}";
    }
}
