using System;
using System.Collections.Generic;
using System.Linq;

namespace Hunter.Gameplay.Items
{
    public enum AddResult
    {
        Added,
        RejectedOverweight,
        RejectedFull,
    }

    /// Weight-limited satchel shared by the player and by AI gold-hunters. Both sides
    /// running the same container is what keeps rival hunters honest: an AI cannot hoard
    /// more than a player could carry out of the same fight.
    public sealed class Inventory
    {
        readonly List<ItemInstance> _items = new();

        /// Hard cap. Going over is not allowed at all; the soft penalty band below
        /// SoftCapRatio is where the interesting decisions live.
        public float WeightLimit { get; private set; }

        public int SlotLimit { get; private set; }

        /// Fraction of WeightLimit above which movement starts to suffer.
        public float SoftCapRatio { get; }

        public Inventory(float weightLimit, int slotLimit, float softCapRatio = 0.6f)
        {
            if (weightLimit <= 0f) throw new ArgumentOutOfRangeException(nameof(weightLimit));
            if (slotLimit <= 0) throw new ArgumentOutOfRangeException(nameof(slotLimit));
            WeightLimit = weightLimit;
            SlotLimit = slotLimit;
            SoftCapRatio = Math.Clamp(softCapRatio, 0.05f, 1f);
        }

        public IReadOnlyList<ItemInstance> Items => _items;
        public int Count => _items.Count;
        public float TotalWeight => _items.Sum(i => i.Weight);
        public int TotalValue => _items.Sum(i => i.Value);
        public float RemainingWeight => Math.Max(0f, WeightLimit - TotalWeight);
        public float LoadRatio => WeightLimit <= 0f ? 1f : TotalWeight / WeightLimit;

        public bool CanFit(ItemInstance item)
        {
            if (item == null) return false;
            if (_items.Count >= SlotLimit) return false;
            return TotalWeight + item.Weight <= WeightLimit + 1e-4f;
        }

        public AddResult TryAdd(ItemInstance item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (_items.Count >= SlotLimit) return AddResult.RejectedFull;
            if (TotalWeight + item.Weight > WeightLimit + 1e-4f) return AddResult.RejectedOverweight;

            _items.Add(item);
            return AddResult.Added;
        }

        public bool Remove(ItemInstance item) => _items.Remove(item);

        /// Everything carried is lost on death and left on the corpse. This method returns
        /// the dropped contents so the caller can spawn them in the world.
        public List<ItemInstance> DropAll()
        {
            var dropped = new List<ItemInstance>(_items);
            _items.Clear();
            return dropped;
        }

        /// Movement multiplier. Unencumbered up to the soft cap, then falls off linearly to
        /// MinSpeedMultiplier at the hard limit, so a full bag is a real commitment rather
        /// than a number in a menu.
        public const float MinSpeedMultiplier = 0.55f;

        public float SpeedMultiplier
        {
            get
            {
                float ratio = LoadRatio;
                if (ratio <= SoftCapRatio) return 1f;

                float t = (ratio - SoftCapRatio) / Math.Max(1e-4f, 1f - SoftCapRatio);
                t = Math.Clamp(t, 0f, 1f);
                return 1f - t * (1f - MinSpeedMultiplier);
            }
        }

        /// Applied when the run ends in extraction. Equipment bonuses can raise the limit
        /// between runs via camp upgrades.
        public void SetLimits(float weightLimit, int slotLimit)
        {
            if (weightLimit <= 0f) throw new ArgumentOutOfRangeException(nameof(weightLimit));
            if (slotLimit <= 0) throw new ArgumentOutOfRangeException(nameof(slotLimit));
            WeightLimit = weightLimit;
            SlotLimit = slotLimit;
        }

        /// Lowest value-per-kilo item, i.e. the first thing a hunter should ditch when a
        /// better find will not fit.
        public ItemInstance WorstByDensity()
        {
            ItemInstance worst = null;
            float worstDensity = float.MaxValue;

            foreach (var item in _items)
            {
                float density = LootValuation.ValueDensity(item);
                if (density < worstDensity)
                {
                    worstDensity = density;
                    worst = item;
                }
            }
            return worst;
        }
    }
}
