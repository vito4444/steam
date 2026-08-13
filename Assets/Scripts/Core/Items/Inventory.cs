using System;
using System.Collections.Generic;

namespace Worker.Core
{
    /// <summary>
    /// Fixed-slot item container. Slot order is stable, which keeps iteration
    /// deterministic and makes save round-trips byte-identical.
    /// </summary>
    public sealed class Inventory
    {
        private readonly ItemStack[] _slots;

        public Inventory(int slotCount)
        {
            if (slotCount <= 0) throw new ArgumentOutOfRangeException(nameof(slotCount));
            _slots = new ItemStack[slotCount];
        }

        public int SlotCount => _slots.Length;

        public ItemStack this[int index] => _slots[index];

        public bool IsEmpty
        {
            get
            {
                for (int i = 0; i < _slots.Length; i++)
                {
                    if (!_slots[i].IsEmpty) return false;
                }
                return true;
            }
        }

        public bool IsFull
        {
            get
            {
                for (int i = 0; i < _slots.Length; i++)
                {
                    var slot = _slots[i];
                    if (slot.IsEmpty) return false;
                    if (slot.Count < GameData.Item(slot.Item).StackSize) return false;
                }
                return true;
            }
        }

        public int CountOf(ItemId item)
        {
            if (item == ItemId.None) return 0;
            int total = 0;
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i].Item == item) total += _slots[i].Count;
            }
            return total;
        }

        public int TotalCount()
        {
            int total = 0;
            for (int i = 0; i < _slots.Length; i++) total += _slots[i].Count;
            return total;
        }

        /// <summary>How many of <paramref name="item"/> would fit right now.</summary>
        public int SpaceFor(ItemId item)
        {
            if (item == ItemId.None) return 0;
            int stackSize = GameData.Item(item).StackSize;
            int space = 0;
            for (int i = 0; i < _slots.Length; i++)
            {
                var slot = _slots[i];
                if (slot.IsEmpty) space += stackSize;
                else if (slot.Item == item) space += stackSize - slot.Count;
            }
            return space;
        }

        /// <summary>Adds up to <paramref name="count"/> items; returns how many were actually stored.</summary>
        public int TryAdd(ItemId item, int count)
        {
            if (item == ItemId.None || count <= 0) return 0;
            int stackSize = GameData.Item(item).StackSize;
            int remaining = count;

            // Top up partial stacks first so the container stays compact.
            for (int i = 0; i < _slots.Length && remaining > 0; i++)
            {
                var slot = _slots[i];
                if (slot.Item != item) continue;
                int room = stackSize - slot.Count;
                if (room <= 0) continue;
                int moved = room < remaining ? room : remaining;
                _slots[i] = slot.Add(moved);
                remaining -= moved;
            }

            for (int i = 0; i < _slots.Length && remaining > 0; i++)
            {
                if (!_slots[i].IsEmpty) continue;
                int moved = stackSize < remaining ? stackSize : remaining;
                _slots[i] = new ItemStack(item, moved);
                remaining -= moved;
            }

            return count - remaining;
        }

        public int TryAdd(ItemStack stack) => TryAdd(stack.Item, stack.Count);

        /// <summary>Removes up to <paramref name="count"/> items; returns how many were actually removed.</summary>
        public int TryRemove(ItemId item, int count)
        {
            if (item == ItemId.None || count <= 0) return 0;
            int remaining = count;

            // Drain partial stacks first, mirroring TryAdd, so slots free up predictably.
            for (int i = _slots.Length - 1; i >= 0 && remaining > 0; i--)
            {
                var slot = _slots[i];
                if (slot.Item != item) continue;
                int moved = slot.Count < remaining ? slot.Count : remaining;
                int left = slot.Count - moved;
                _slots[i] = left > 0 ? slot.WithCount(left) : ItemStack.Empty;
                remaining -= moved;
            }

            return count - remaining;
        }

        public bool Contains(ItemId item, int count) => CountOf(item) >= count;

        public bool ContainsAll(IReadOnlyList<ItemStack> stacks)
        {
            for (int i = 0; i < stacks.Count; i++)
            {
                if (CountOf(stacks[i].Item) < stacks[i].Count) return false;
            }
            return true;
        }

        /// <summary>Removes every stack listed; caller must have checked <see cref="ContainsAll"/> first.</summary>
        public void RemoveAll(IReadOnlyList<ItemStack> stacks)
        {
            for (int i = 0; i < stacks.Count; i++)
            {
                int removed = TryRemove(stacks[i].Item, stacks[i].Count);
                if (removed != stacks[i].Count)
                {
                    throw new InvalidOperationException(
                        "Inventory underflow removing " + stacks[i] + ", only removed " + removed);
                }
            }
        }

        /// <summary>First non-empty slot index, or -1. Used by haul tasks to pick something to move.</summary>
        public int FirstOccupiedSlot()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                if (!_slots[i].IsEmpty) return i;
            }
            return -1;
        }

        public void Clear()
        {
            for (int i = 0; i < _slots.Length; i++) _slots[i] = ItemStack.Empty;
        }

        internal void SetSlotRaw(int index, ItemStack stack) => _slots[index] = stack;
    }
}
