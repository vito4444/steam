using System;
using System.Collections.Generic;
using System.Linq;
using Hunter.Gameplay.Items;

namespace Hunter.Gameplay.Camp
{
    public enum FacilityKind
    {
        /// Raises how much can be carried out. The most directly felt upgrade.
        Vault,
        /// Better starting weapon.
        Forge,
        /// Starting consumables.
        Alchemy,
        /// More known extraction points and richer pre-raid intel.
        Cartographer,
        /// Raises vitality.
        Shrine,
    }

    /// One upgradeable camp building.
    public sealed class Facility
    {
        public FacilityKind Kind { get; }
        public int Level { get; private set; }
        public int MaxLevel { get; }

        readonly int _baseCost;
        readonly float _costGrowth;

        public Facility(FacilityKind kind, int baseCost, int maxLevel = 5, float costGrowth = 1.85f)
        {
            Kind = kind;
            _baseCost = baseCost;
            _costGrowth = costGrowth;
            MaxLevel = maxLevel;
            Level = 0;
        }

        public bool IsMaxed => Level >= MaxLevel;

        /// Cost of the next level. Growth is steep enough that a single good raid buys one
        /// early upgrade but never a late one, which is what keeps the loop running.
        public int NextCost => IsMaxed
            ? int.MaxValue
            : (int)Math.Round(_baseCost * Math.Pow(_costGrowth, Level));

        public void Upgrade()
        {
            if (IsMaxed) throw new InvalidOperationException($"{Kind} is already at max level");
            Level++;
        }

        public void SetLevel(int level) => Level = Math.Clamp(level, 0, MaxLevel);
    }

    /// What the camp grants a hunter on insertion.
    public readonly struct LoadoutModifiers
    {
        public float CarryWeightBonus { get; }
        public int CarrySlotBonus { get; }
        public float VitalityBonus { get; }
        public int KnownExtractionPoints { get; }
        public int StartingConsumables { get; }
        public ItemDefinition StartingWeapon { get; }

        public LoadoutModifiers(float carryWeightBonus, int carrySlotBonus, float vitalityBonus,
            int knownExtractionPoints, int startingConsumables, ItemDefinition startingWeapon)
        {
            CarryWeightBonus = carryWeightBonus;
            CarrySlotBonus = carrySlotBonus;
            VitalityBonus = vitalityBonus;
            KnownExtractionPoints = knownExtractionPoints;
            StartingConsumables = startingConsumables;
            StartingWeapon = startingWeapon;
        }
    }

    public sealed class RunRecord
    {
        public bool Survived;
        public int Value;
        public int Items;
    }

    /// Persistent progress between raids.
    ///
    /// This is what makes a lost run sting and a good one matter. Without it the extraction
    /// loop has no stakes beyond the single raid: banking 1500 aurum has to buy something,
    /// and dying has to cost the chance to buy it.
    public sealed class CampState
    {
        readonly Dictionary<FacilityKind, Facility> _facilities;
        readonly List<RunRecord> _history = new();

        public CampState()
        {
            _facilities = new Dictionary<FacilityKind, Facility>
            {
                { FacilityKind.Vault, new Facility(FacilityKind.Vault, 420) },
                { FacilityKind.Forge, new Facility(FacilityKind.Forge, 560) },
                { FacilityKind.Alchemy, new Facility(FacilityKind.Alchemy, 300, maxLevel: 4) },
                { FacilityKind.Cartographer, new Facility(FacilityKind.Cartographer, 680, maxLevel: 3) },
                { FacilityKind.Shrine, new Facility(FacilityKind.Shrine, 500) },
            };
        }

        public int Aurum { get; private set; }
        public int RunsSurvived { get; private set; }
        public int RunsLost { get; private set; }
        public IReadOnlyList<RunRecord> History => _history;

        public IEnumerable<Facility> Facilities => _facilities.Values;
        public Facility GetFacility(FacilityKind kind) => _facilities[kind];
        public int LevelOf(FacilityKind kind) => _facilities[kind].Level;

        public event Action<FacilityKind, int> FacilityUpgraded;
        public event Action<int> AurumChanged;

        /// Called on a successful extraction. Only what was carried through the portal
        /// counts; a failed raid calls RecordLoss instead and banks nothing.
        public int BankHaul(IReadOnlyList<ItemInstance> haul)
        {
            int value = haul?.Sum(i => i.Value) ?? 0;
            Aurum += value;
            _history.Add(new RunRecord { Survived = true, Value = value, Items = haul?.Count ?? 0 });
            RunsSurvived++;
            AurumChanged?.Invoke(Aurum);
            return value;
        }

        public void RecordLoss(int valueLost, int itemsLost)
        {
            _history.Add(new RunRecord { Survived = false, Value = valueLost, Items = itemsLost });
            RunsLost++;
        }

        public bool CanAfford(FacilityKind kind) => Aurum >= _facilities[kind].NextCost;

        public bool TryUpgrade(FacilityKind kind)
        {
            var facility = _facilities[kind];
            if (facility.IsMaxed) return false;

            int cost = facility.NextCost;
            if (Aurum < cost) return false;

            Aurum -= cost;
            facility.Upgrade();
            AurumChanged?.Invoke(Aurum);
            FacilityUpgraded?.Invoke(kind, facility.Level);
            return true;
        }

        /// The camp's effect on the next raid. Every value here is read by RunController at
        /// insertion, so an upgrade is felt on the very next run.
        public LoadoutModifiers BuildModifiers()
        {
            int vault = LevelOf(FacilityKind.Vault);
            int forge = LevelOf(FacilityKind.Forge);
            int alchemy = LevelOf(FacilityKind.Alchemy);
            int cartographer = LevelOf(FacilityKind.Cartographer);
            int shrine = LevelOf(FacilityKind.Shrine);

            var weapon = forge switch
            {
                0 => ItemCatalog.RustedBlade,
                1 or 2 => ItemCatalog.HuntersFalchion,
                _ => ItemCatalog.WardenHalberd,
            };

            return new LoadoutModifiers(
                carryWeightBonus: vault * 4.5f,
                carrySlotBonus: vault * 2,
                vitalityBonus: shrine * 14f,
                // One route home to start with; the cartographer sells the rest.
                knownExtractionPoints: 1 + cartographer,
                startingConsumables: alchemy,
                startingWeapon: weapon);
        }

        /// Total spent plus banked. Used by the camp screen to show lifetime progress.
        public int LifetimeEarnings => _history.Where(r => r.Survived).Sum(r => r.Value);
        public int LifetimeLosses => _history.Where(r => !r.Survived).Sum(r => r.Value);

        public float SurvivalRate
        {
            get
            {
                int total = RunsSurvived + RunsLost;
                return total == 0 ? 0f : (float)RunsSurvived / total;
            }
        }

        // ----- serialisation -----

        [Serializable]
        public sealed class SaveData
        {
            public int aurum;
            public int runsSurvived;
            public int runsLost;
            public int[] facilityKinds = Array.Empty<int>();
            public int[] facilityLevels = Array.Empty<int>();
            public int lifetimeEarnings;
            public int lifetimeLosses;
        }

        public SaveData ToSaveData()
        {
            var kinds = _facilities.Keys.ToArray();
            return new SaveData
            {
                aurum = Aurum,
                runsSurvived = RunsSurvived,
                runsLost = RunsLost,
                facilityKinds = kinds.Select(k => (int)k).ToArray(),
                facilityLevels = kinds.Select(k => _facilities[k].Level).ToArray(),
                lifetimeEarnings = LifetimeEarnings,
                lifetimeLosses = LifetimeLosses,
            };
        }

        public void LoadFrom(SaveData data)
        {
            if (data == null) return;

            Aurum = Math.Max(0, data.aurum);
            RunsSurvived = Math.Max(0, data.runsSurvived);
            RunsLost = Math.Max(0, data.runsLost);

            _history.Clear();
            // History detail is not persisted; the aggregate is enough to restore the
            // camp screen, and keeping it small keeps saves cheap.
            if (data.lifetimeEarnings > 0)
                _history.Add(new RunRecord { Survived = true, Value = data.lifetimeEarnings, Items = 0 });
            if (data.lifetimeLosses > 0)
                _history.Add(new RunRecord { Survived = false, Value = data.lifetimeLosses, Items = 0 });

            if (data.facilityKinds == null || data.facilityLevels == null) return;

            int count = Math.Min(data.facilityKinds.Length, data.facilityLevels.Length);
            for (int i = 0; i < count; i++)
            {
                var kind = (FacilityKind)data.facilityKinds[i];
                if (_facilities.TryGetValue(kind, out var facility)) facility.SetLevel(data.facilityLevels[i]);
            }
        }
    }
}
