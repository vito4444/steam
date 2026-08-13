using System;

namespace Undertown.Core.Economy
{
    /// <summary>
    /// What is physically on the shelves, as opposed to what the books claim is on the
    /// shelves. The gap between this and <see cref="LedgerBook"/> is the entire game.
    /// </summary>
    [Serializable]
    public sealed class Stockpile
    {
        private readonly int[] _counts = new int[Materials.Count];

        public int Get(MaterialId id) => _counts[(int)id];

        public void Add(MaterialId id, int qty)
        {
            if (qty < 0) throw new ArgumentOutOfRangeException(nameof(qty));
            _counts[(int)id] += qty;
        }

        /// <summary>Removes what it can and reports how much was actually taken.</summary>
        public int TakeUpTo(MaterialId id, int qty)
        {
            if (qty < 0) throw new ArgumentOutOfRangeException(nameof(qty));
            int taken = Math.Min(qty, _counts[(int)id]);
            _counts[(int)id] -= taken;
            return taken;
        }

        public bool TryTake(MaterialId id, int qty)
        {
            if (qty < 0) throw new ArgumentOutOfRangeException(nameof(qty));
            if (_counts[(int)id] < qty) return false;
            _counts[(int)id] -= qty;
            return true;
        }

        public bool Has(MaterialId id, int qty) => _counts[(int)id] >= qty;
    }
}
