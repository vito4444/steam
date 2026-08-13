namespace Worker.Core
{
    /// <summary>
    /// The contents of a single conveyor tile.
    ///
    /// A tile is modelled as <see cref="Capacity"/> consecutive slots. An item advances
    /// <see cref="TicksPerSlot"/> ticks within a slot, then moves up one slot if it is
    /// free; from the last slot it tries to hand off to whatever the belt points at.
    /// This gives smooth, evenly spaced movement for rendering while keeping the whole
    /// thing integer-only and therefore replay-safe.
    ///
    /// Conveyors matter beyond logistics: every belt the player lays is a haul trip a
    /// worker no longer makes, which is the first step of the automation-versus-people
    /// trade-off the game is built on.
    /// </summary>
    public sealed class ConveyorState
    {
        public const int Capacity = 3;
        public const int TicksPerSlot = 5;

        private readonly ItemId[] _items = new ItemId[Capacity];
        private readonly int[] _progress = new int[Capacity];

        public ItemId ItemAt(int slot) => _items[slot];
        public int ProgressAt(int slot) => _progress[slot];

        public bool IsEmpty
        {
            get
            {
                for (int i = 0; i < Capacity; i++)
                {
                    if (_items[i] != ItemId.None) return false;
                }
                return true;
            }
        }

        public int Count
        {
            get
            {
                int total = 0;
                for (int i = 0; i < Capacity; i++)
                {
                    if (_items[i] != ItemId.None) total++;
                }
                return total;
            }
        }

        /// <summary>True when the entry slot is free, so an upstream tile can push into it.</summary>
        public bool CanAccept => _items[0] == ItemId.None;

        public bool TryAccept(ItemId item)
        {
            if (item == ItemId.None || !CanAccept) return false;
            _items[0] = item;
            _progress[0] = 0;
            return true;
        }

        /// <summary>Item ready to leave the belt, or <see cref="ItemId.None"/>.</summary>
        public ItemId PeekOutput()
            => _progress[Capacity - 1] >= TicksPerSlot ? _items[Capacity - 1] : ItemId.None;

        public ItemId TakeOutput()
        {
            var item = PeekOutput();
            if (item == ItemId.None) return ItemId.None;
            _items[Capacity - 1] = ItemId.None;
            _progress[Capacity - 1] = 0;
            return item;
        }

        /// <summary>
        /// Advances every item by one tick. The last slot is handled by the world, which
        /// knows what the belt points at, so this only moves items forward internally.
        /// Slots are walked from the front so a moving item never leapfrogs the one ahead.
        /// </summary>
        public void Advance()
        {
            for (int slot = Capacity - 1; slot >= 0; slot--)
            {
                if (_items[slot] == ItemId.None) continue;

                if (_progress[slot] < TicksPerSlot)
                {
                    _progress[slot]++;
                    continue;
                }

                if (slot == Capacity - 1) continue; // waiting on the world to take it

                if (_items[slot + 1] != ItemId.None) continue; // blocked by the item ahead

                _items[slot + 1] = _items[slot];
                _progress[slot + 1] = 0;
                _items[slot] = ItemId.None;
                _progress[slot] = 0;
            }
        }

        /// <summary>Normalised position of a slot's item along the tile, 0..1000. For rendering.</summary>
        public int PositionPermille(int slot)
        {
            int within = _progress[slot] > TicksPerSlot ? TicksPerSlot : _progress[slot];
            int numerator = slot * TicksPerSlot + within;
            int denominator = Capacity * TicksPerSlot;
            return numerator * 1000 / denominator;
        }

        internal void SetSlotRaw(int slot, ItemId item, int progress)
        {
            _items[slot] = item;
            _progress[slot] = progress;
        }
    }
}
