using System;
using System.Collections.Generic;
using Undertown.Core.Agents;
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
        public readonly List<Villager> Villagers = new List<Villager>();

        /// <summary>The recipes the empire has on file, and therefore the ones it checks yields against.</summary>
        public readonly RecipeExpectation[] Recipes =
        {
            new RecipeExpectation(MaterialId.Grain, MaterialId.Ale, 2000),
            new RecipeExpectation(MaterialId.Clay, MaterialId.Brick, 1000),
        };

        public Inspector ActiveInspector;

        /// <summary>Chambers the empire already knows about, so a second tap on one is not a second discovery.</summary>
        public readonly HashSet<Coord> DiscoveredHollows = new HashSet<Coord>();

        public int SoundingsRemaining;
        public int PatrolStopsDone;
        public int LastInspectionDay = -1;
        public bool SpoilNoticed;
        public AuditReport LastAudit;
        public readonly List<string> LastFindings = new List<string>();

        public int LastSettledSeason = -1;
        public SettlementReport LastSettlement;
        public bool GameOver;

        public int Suspicion { get; private set; }
        public int Coin;
        public int BlackCoin;
        public int InspectorLevel = 1;

        /// <summary>Spoil heaped in the open. Every pile past what a quarry would explain is evidence.</summary>
        public int SurfaceSpoil;
        public const int SpoilTolerated = 40;

        public readonly DigOrders Digs = new DigOrders();

        /// <summary>
        /// Cells where a worker can change layer. Only entrances provide one, which is why
        /// they are worth building and worth disguising.
        /// </summary>
        public IReadOnlyList<Coord> ShaftCells
        {
            get
            {
                _shaftCache.Clear();
                for (int i = 0; i < Buildings.Count; i++)
                {
                    var building = Buildings[i];
                    if (building.Kind != BuildingKind.HiddenEntrance) continue;
                    _shaftCache.Add(building.Origin);
                }
                return _shaftCache;
            }
        }

        private readonly List<Coord> _shaftCache = new List<Coord>();

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

        /// <summary>
        /// What an audit would find if one happened right now. This is the single most
        /// important number in the interface: an audit is an abstract thing to be threatened
        /// by, and without a running total the player cannot tell a safe arrangement from a
        /// fatal one until an inspector tells them, by which point it is too late to change.
        /// </summary>
        public AuditReport DryRunAudit() =>
            AuditResolver.Resolve(Books, VisibleStock, Recipes, CurrentAuditSettings);

        /// <summary>
        /// The gap between what the books promise is on the shelf and what an inspector would
        /// actually count. Positive means material has gone somewhere unexplained.
        /// </summary>
        public int LedgerGap(MaterialId id) => Books.Flow(id).ExpectedStock - VisibleStock(id);

        /// <summary>How much contraband the cellars can keep out of sight. Each cellar store holds this much.</summary>
        public const int HiddenStoragePerCellar = 120;

        public int HiddenStorageCapacity
        {
            get
            {
                int capacity = 0;
                for (int i = 0; i < Buildings.Count; i++)
                    if (Buildings[i].Kind == BuildingKind.UnderStore) capacity += HiddenStoragePerCellar;
                return capacity;
            }
        }

        /// <summary>
        /// What an inspector can actually count. He walks the warehouse, not the cellars, so
        /// contraband inside the hidden stores is simply not there as far as the audit is
        /// concerned. Anything beyond that capacity has to sit somewhere he does walk - which
        /// makes cellar space a hard ceiling on production rather than a convenience.
        /// </summary>
        public int VisibleStock(MaterialId id)
        {
            int total = Stock.Get(id);
            if (!Materials.IsContraband(id)) return total;
            return Math.Max(0, total - HiddenStorageCapacity);
        }

        public void Record(string entry)
        {
            _log.Add($"[d{Clock.DayOfSeason:D2} {Clock.TimeOfDayLabel}] {entry}");
            if (_log.Count > 200) _log.RemoveAt(0);
        }
    }
}
