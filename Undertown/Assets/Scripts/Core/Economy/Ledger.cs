using System;

namespace Undertown.Core.Economy
{
    /// <summary>
    /// One material's declared movements over a single accounting period. Everything is
    /// whole units of stock; the simulation never uses floating point so that a replay of
    /// the same inputs always produces byte-identical books.
    /// </summary>
    [Serializable]
    public struct MaterialFlow
    {
        public int Opening;
        public int Purchased;
        public int Produced;
        public int Consumed;
        public int DeclaredLoss;
        public int Sold;

        /// <summary>What the books say should be sitting in the warehouse right now.</summary>
        public int ExpectedStock => Opening + Purchased + Produced - Consumed - DeclaredLoss - Sold;

        /// <summary>Total movement this period, used as the denominator for discrepancy ratios.</summary>
        public int Throughput => Purchased + Produced + Consumed + Sold;

        /// <summary>Everything that entered the warehouse, used as the denominator for loss rates.</summary>
        public int Inflow => Opening + Purchased + Produced;
    }

    /// <summary>
    /// The town's official books. The player writes to this every time goods move; the
    /// imperial auditor reads it and compares against a physical count of the warehouses.
    /// Nothing here knows about smuggling - concealment is expressed purely as the gap
    /// between what gets recorded and what actually happened.
    /// </summary>
    [Serializable]
    public sealed class LedgerBook
    {
        private MaterialFlow[] _flows = new MaterialFlow[Materials.Count];

        public MaterialFlow Flow(MaterialId id) => _flows[(int)id];

        public void RecordPurchase(MaterialId id, int qty) => _flows[(int)id].Purchased += Require(qty);
        public void RecordProduction(MaterialId id, int qty) => _flows[(int)id].Produced += Require(qty);
        public void RecordConsumption(MaterialId id, int qty) => _flows[(int)id].Consumed += Require(qty);
        public void RecordSale(MaterialId id, int qty) => _flows[(int)id].Sold += Require(qty);

        /// <summary>
        /// Loss is the one figure the player writes by hand rather than earning through
        /// play, which is exactly why the auditor cross-checks it against a regional baseline.
        /// </summary>
        public void DeclareLoss(MaterialId id, int qty) => _flows[(int)id].DeclaredLoss += Require(qty);

        public void SetDeclaredLoss(MaterialId id, int qty) => _flows[(int)id].DeclaredLoss = Require(qty);

        /// <summary>Rolls the books forward: this period's physical count becomes next period's opening line.</summary>
        public void BeginPeriod(Func<MaterialId, int> physicalCount)
        {
            for (int i = 0; i < _flows.Length; i++)
            {
                var id = (MaterialId)i;
                _flows[i] = new MaterialFlow { Opening = id == MaterialId.None ? 0 : physicalCount(id) };
            }
        }

        public LedgerBook Clone()
        {
            var copy = new LedgerBook();
            Array.Copy(_flows, copy._flows, _flows.Length);
            return copy;
        }

        private static int Require(int qty)
        {
            if (qty < 0) throw new ArgumentOutOfRangeException(nameof(qty), "ledger entries are always positive movements");
            return qty;
        }
    }
}
