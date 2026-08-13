using System;
using System.Collections.Generic;
using System.Linq;

namespace Hunter.Gameplay.Items
{
    public enum ItemRarity
    {
        Common = 0,
        Uncommon = 1,
        Rare = 2,
        Epic = 3,
    }

    public enum ItemCategory
    {
        Weapon,
        Armour,
        Trinket,
        Treasure,
        Consumable,
    }

    public enum StatKind
    {
        Damage,
        AttackSpeed,
        Armour,
        CarryCapacity,
        MoveSpeed,
        LanternRange,
    }

    /// Immutable catalogue entry. Instances roll their own affixes on top of this.
    public sealed class ItemDefinition
    {
        public string Id { get; }
        public string DisplayName { get; }
        public ItemCategory Category { get; }
        public ItemRarity BaseRarity { get; }

        /// Value in流金 (the run currency). Treasure exists purely to be carried out.
        public int BaseValue { get; }

        /// Kilograms. Weight is the whole point of the extraction loop: it is what turns
        /// "grab everything" into a decision.
        public float Weight { get; }

        public IReadOnlyDictionary<StatKind, float> BaseStats { get; }

        public ItemDefinition(string id, string displayName, ItemCategory category, ItemRarity baseRarity,
            int baseValue, float weight, IReadOnlyDictionary<StatKind, float> baseStats = null)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("id required", nameof(id));
            if (weight < 0f) throw new ArgumentOutOfRangeException(nameof(weight));
            if (baseValue < 0) throw new ArgumentOutOfRangeException(nameof(baseValue));

            Id = id;
            DisplayName = displayName;
            Category = category;
            BaseRarity = baseRarity;
            BaseValue = baseValue;
            Weight = weight;
            BaseStats = baseStats ?? new Dictionary<StatKind, float>();
        }
    }

    /// A rolled modifier. Affixes multiply value, which is what makes two copies of the
    /// same base item worth arguing over.
    public sealed class ItemAffix
    {
        public string Id { get; }
        public string DisplayName { get; }
        public StatKind Stat { get; }
        public float Amount { get; }
        public float ValueMultiplier { get; }

        public ItemAffix(string id, string displayName, StatKind stat, float amount, float valueMultiplier)
        {
            if (valueMultiplier <= 0f) throw new ArgumentOutOfRangeException(nameof(valueMultiplier));
            Id = id;
            DisplayName = displayName;
            Stat = stat;
            Amount = amount;
            ValueMultiplier = valueMultiplier;
        }
    }

    public sealed class ItemInstance
    {
        static int _nextInstanceId = 1;

        public int InstanceId { get; }
        public ItemDefinition Definition { get; }
        public IReadOnlyList<ItemAffix> Affixes { get; }

        public ItemInstance(ItemDefinition definition, IReadOnlyList<ItemAffix> affixes = null)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            Affixes = affixes ?? Array.Empty<ItemAffix>();
            InstanceId = _nextInstanceId++;
        }

        public float Weight => Definition.Weight;

        /// Affix multipliers compound, so a two-affix item is worth more than the sum of
        /// its parts. That is deliberate: it creates the "this one is special" moment.
        public int Value
        {
            get
            {
                float value = Definition.BaseValue;
                foreach (var affix in Affixes) value *= affix.ValueMultiplier;
                return (int)Math.Round(value);
            }
        }

        /// Displayed rarity is driven upward by affix count so the colour of an item in the
        /// world matches how good it actually is.
        public ItemRarity EffectiveRarity
        {
            get
            {
                int rank = (int)Definition.BaseRarity + Affixes.Count;
                return (ItemRarity)Math.Min(rank, (int)ItemRarity.Epic);
            }
        }

        public float GetStat(StatKind stat)
        {
            float total = Definition.BaseStats.TryGetValue(stat, out var b) ? b : 0f;
            foreach (var affix in Affixes)
            {
                if (affix.Stat == stat) total += affix.Amount;
            }
            return total;
        }

        public override string ToString()
        {
            var prefix = Affixes.Count > 0 ? string.Join(" ", Affixes.Select(a => a.DisplayName)) + " " : "";
            return $"{prefix}{Definition.DisplayName}";
        }
    }
}
