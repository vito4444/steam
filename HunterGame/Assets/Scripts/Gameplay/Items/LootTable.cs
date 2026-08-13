using System;
using System.Collections.Generic;
using System.Linq;

namespace Hunter.Gameplay.Items
{
    /// Seeded loot generation. Determinism matters twice over: runs are reproducible for
    /// debugging, and tests can assert on distributions rather than on single rolls.
    public sealed class LootTable
    {
        public sealed class Entry
        {
            public ItemDefinition Definition { get; }
            public float Weight { get; }

            public Entry(ItemDefinition definition, float weight)
            {
                Definition = definition ?? throw new ArgumentNullException(nameof(definition));
                if (weight <= 0f) throw new ArgumentOutOfRangeException(nameof(weight));
                Weight = weight;
            }
        }

        readonly List<Entry> _entries;
        readonly List<ItemAffix> _affixPool;
        readonly float _totalWeight;

        public LootTable(IEnumerable<Entry> entries, IEnumerable<ItemAffix> affixPool = null)
        {
            _entries = entries?.ToList() ?? throw new ArgumentNullException(nameof(entries));
            if (_entries.Count == 0) throw new ArgumentException("loot table needs entries", nameof(entries));
            _affixPool = affixPool?.ToList() ?? new List<ItemAffix>();
            _totalWeight = _entries.Sum(e => e.Weight);
        }

        /// Chance of rolling an additional affix, applied repeatedly. Elite enemies pass a
        /// higher luck value, which is how "kill the elite for gold gear" reads in play.
        public ItemInstance Roll(Random rng, float luck = 0f)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            var definition = PickDefinition(rng);
            var affixes = new List<ItemAffix>();

            if (_affixPool.Count > 0)
            {
                float chance = Math.Clamp(0.18f + luck, 0f, 0.9f);
                int maxAffixes = Math.Min(3, _affixPool.Count);

                for (int i = 0; i < maxAffixes; i++)
                {
                    if (rng.NextDouble() >= chance) break;

                    var affix = _affixPool[rng.Next(_affixPool.Count)];
                    if (affixes.Any(a => a.Id == affix.Id)) continue;
                    affixes.Add(affix);
                    chance *= 0.45f;
                }
            }

            return new ItemInstance(definition, affixes);
        }

        ItemDefinition PickDefinition(Random rng)
        {
            double roll = rng.NextDouble() * _totalWeight;
            foreach (var entry in _entries)
            {
                roll -= entry.Weight;
                if (roll <= 0d) return entry.Definition;
            }
            return _entries[^1].Definition;
        }

        public List<ItemInstance> RollMany(Random rng, int count, float luck = 0f)
        {
            var results = new List<ItemInstance>(count);
            for (int i = 0; i < count; i++) results.Add(Roll(rng, luck));
            return results;
        }
    }

    /// Starter catalogue for the vertical slice. Values and weights are tuned so that a
    /// full satchel of treasure is worth roughly one equipment upgrade, which is the
    /// exchange rate the camp progression assumes.
    public static class ItemCatalog
    {
        public static readonly ItemDefinition RustedBlade = new(
            "wpn_rusted_blade", "锈蚀短剑", ItemCategory.Weapon, ItemRarity.Common, 40, 3.2f,
            new Dictionary<StatKind, float> { { StatKind.Damage, 18f }, { StatKind.AttackSpeed, 1.0f } });

        public static readonly ItemDefinition HuntersFalchion = new(
            "wpn_hunters_falchion", "猎金弯刀", ItemCategory.Weapon, ItemRarity.Uncommon, 120, 4.1f,
            new Dictionary<StatKind, float> { { StatKind.Damage, 27f }, { StatKind.AttackSpeed, 0.92f } });

        public static readonly ItemDefinition WardenHalberd = new(
            "wpn_warden_halberd", "守望长戟", ItemCategory.Weapon, ItemRarity.Rare, 260, 7.4f,
            new Dictionary<StatKind, float> { { StatKind.Damage, 44f }, { StatKind.AttackSpeed, 0.68f } });

        public static readonly ItemDefinition PaddedJerkin = new(
            "arm_padded_jerkin", "衬垫皮衣", ItemCategory.Armour, ItemRarity.Common, 55, 5.5f,
            new Dictionary<StatKind, float> { { StatKind.Armour, 12f }, { StatKind.MoveSpeed, -0.02f } });

        public static readonly ItemDefinition GildedCuirass = new(
            "arm_gilded_cuirass", "鎏金胸甲", ItemCategory.Armour, ItemRarity.Rare, 310, 11.2f,
            new Dictionary<StatKind, float> { { StatKind.Armour, 34f }, { StatKind.MoveSpeed, -0.08f } });

        public static readonly ItemDefinition PorterCharm = new(
            "trk_porter_charm", "负重符", ItemCategory.Trinket, ItemRarity.Uncommon, 150, 0.4f,
            new Dictionary<StatKind, float> { { StatKind.CarryCapacity, 8f } });

        public static readonly ItemDefinition LanternOil = new(
            "cns_lantern_oil", "灯油", ItemCategory.Consumable, ItemRarity.Common, 20, 0.8f,
            new Dictionary<StatKind, float> { { StatKind.LanternRange, 6f } });

        // Treasure has no stats. It exists to be heavy, valuable and tempting.
        public static readonly ItemDefinition AurumShard = new(
            "tre_aurum_shard", "流金碎片", ItemCategory.Treasure, ItemRarity.Common, 75, 1.1f);

        public static readonly ItemDefinition ReliquaryPlate = new(
            "tre_reliquary_plate", "圣匣金板", ItemCategory.Treasure, ItemRarity.Rare, 420, 9.5f);

        public static readonly ItemDefinition GodsbloodPhial = new(
            "tre_godsblood_phial", "神血瓶", ItemCategory.Treasure, ItemRarity.Epic, 900, 2.2f);

        public static readonly ItemAffix Keen = new("afx_keen", "锋锐的", StatKind.Damage, 6f, 1.35f);
        public static readonly ItemAffix Swift = new("afx_swift", "迅捷的", StatKind.AttackSpeed, 0.12f, 1.3f);
        public static readonly ItemAffix Reinforced = new("afx_reinforced", "加固的", StatKind.Armour, 9f, 1.28f);
        public static readonly ItemAffix Featherlight = new("afx_feather", "轻羽的", StatKind.MoveSpeed, 0.05f, 1.22f);
        public static readonly ItemAffix Gilded = new("afx_gilded", "鎏金的", StatKind.CarryCapacity, 3f, 1.6f);

        public static IReadOnlyList<ItemAffix> AllAffixes { get; } = new[]
        {
            Keen, Swift, Reinforced, Featherlight, Gilded,
        };

        /// Ordinary containers: mostly consumables and small treasure.
        public static LootTable CommonCache() => new(new[]
        {
            new LootTable.Entry(LanternOil, 30f),
            new LootTable.Entry(AurumShard, 26f),
            new LootTable.Entry(RustedBlade, 14f),
            new LootTable.Entry(PaddedJerkin, 12f),
            new LootTable.Entry(HuntersFalchion, 7f),
            new LootTable.Entry(PorterCharm, 5f),
            new LootTable.Entry(ReliquaryPlate, 3f),
        }, AllAffixes);

        /// Elite drops. Documented in the concept as the reason to fight rather than sneak.
        public static LootTable EliteCache() => new(new[]
        {
            new LootTable.Entry(AurumShard, 18f),
            new LootTable.Entry(HuntersFalchion, 16f),
            new LootTable.Entry(WardenHalberd, 13f),
            new LootTable.Entry(GildedCuirass, 12f),
            new LootTable.Entry(PorterCharm, 10f),
            new LootTable.Entry(ReliquaryPlate, 9f),
            new LootTable.Entry(GodsbloodPhial, 4f),
        }, AllAffixes);
    }
}
