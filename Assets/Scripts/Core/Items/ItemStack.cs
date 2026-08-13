using System;

namespace Worker.Core
{
    /// <summary>An item id paired with a count. A count of zero always normalises to <see cref="Empty"/>.</summary>
    [Serializable]
    public readonly struct ItemStack : IEquatable<ItemStack>
    {
        public readonly ItemId Item;
        public readonly int Count;

        public ItemStack(ItemId item, int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "Stack count cannot be negative");
            if (item == ItemId.None || count == 0)
            {
                Item = ItemId.None;
                Count = 0;
            }
            else
            {
                Item = item;
                Count = count;
            }
        }

        public static readonly ItemStack Empty = default;

        public bool IsEmpty => Item == ItemId.None || Count == 0;

        public ItemStack WithCount(int count) => new ItemStack(Item, count);

        public ItemStack Add(int amount) => new ItemStack(Item, Count + amount);

        public bool Equals(ItemStack other) => Item == other.Item && Count == other.Count;
        public override bool Equals(object obj) => obj is ItemStack other && Equals(other);
        public override int GetHashCode() => ((int)Item << 16) ^ Count;
        public override string ToString() => IsEmpty ? "empty" : Count + "x" + Item;
    }
}
