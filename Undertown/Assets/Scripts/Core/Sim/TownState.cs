using System;
using System.Collections.Generic;
using Undertown.Core.Buildings;
using Undertown.Core.Determinism;
using Undertown.Core.Economy;
using Undertown.Core.Inspection;
using Undertown.Core.World;

namespace Undertown.Core.Sim
{
    /// <summary>
    /// Where suspicion currently stands with the empire, and what happens at the end of the
    /// season if it stays there.
    /// </summary>
    public enum SuspicionBand
    {
        Unremarkable,
        Noted,
        Fined,
        Seized,
        Annexed,
    }

    /// <summary>
    /// The complete state of one playthrough. Everything the game is lives here; the Unity
    /// layer only reads it and draws it. Advancing this by a fixed tick from a fixed seed
    /// always lands in the same place, which is what makes the whole thing testable.
    /// </summary>
    [Serializable]
    public sealed class TownState
    {
        public const int AnnexationThreshold = 90;

        public readonly GridMap Map;
        public readonly SimClock Clock = new SimClock();
        public readonly LedgerBook Books = new LedgerBook();
        public readonly Stockpile Stock = new Stockpile();
        public readonly List<Building> Buildings = new List<Building>();

        public int Suspicion { get; private set; }
        public int Coin;
        public int BlackCoin;
        public int InspectorLevel = 1;

        /// <summary>Spoil heaped in the open. Every pile past what a quarry would explain is evidence.</summary>
        public int SurfaceSpoil;
        public const int SpoilTolerated = 40;

        public DeterministicRandom Rng;

        private readonly List<string> _log = new List<string>();
        public IReadOnlyList<string> Log => _log;

        public TownState(GridMap map, uint seed)
        {
            Map = map ?? throw new ArgumentNullException(nameof(map));
            Rng = new DeterministicRandom(seed);
        }

        public SuspicionBand Band => BandFor(Suspicion);

        public static SuspicionBand BandFor(int suspicion)
        {
            if (suspicion >= AnnexationThreshold) return SuspicionBand.Annexed;
            if (suspicion >= 70) return SuspicionBand.Seized;
            if (suspicion >= 50) return SuspicionBand.Fined;
            if (suspicion >= 30) return SuspicionBand.Noted;
            return SuspicionBand.Unremarkable;
        }

        public static string BandLabel(SuspicionBand band)
        {
            switch (band)
            {
                case SuspicionBand.Noted: return "Under Notice";
                case SuspicionBand.Fined: return "Fines Pending";
                case SuspicionBand.Seized: return "Seizure Order";
                case SuspicionBand.Annexed: return "Annexed";
                default: return "Unremarkable";
            }
        }

        public void AddSuspicion(int amount, string reason)
        {
            if (amount <= 0) return;
            Suspicion = Math.Min(100, Suspicion + amount);
            Record($"suspicion +{amount}: {reason}");
        }

        public void EaseSuspicion(int amount, string reason)
        {
            if (amount <= 0) return;
            Suspicion = Math.Max(0, Suspicion - amount);
            Record($"suspicion -{amount}: {reason}");
        }

        /// <summary>
        /// Spoil left in the open is the one exposure that needs no auditor to notice it.
        /// A quarry town can explain a certain amount of loose earth; past that, someone asks.
        /// </summary>
        public int SpoilExposure => Math.Max(0, SurfaceSpoil - SpoilTolerated);

        public AuditSettings CurrentAuditSettings => AuditSettings.ForLevel(InspectorLevel);

        public void Record(string entry)
        {
            _log.Add($"[d{Clock.DayOfSeason:D2} {Clock.TimeOfDayLabel}] {entry}");
            if (_log.Count > 200) _log.RemoveAt(0);
        }
    }
}
