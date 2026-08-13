using System.Collections.Generic;

namespace Worker.Core
{
    public enum LedgerEntryKind : byte
    {
        OrderPayout = 0,
        OrderPenalty = 1,
        Wages = 2,
        Construction = 3,
        MaterialPurchase = 4,
        Severance = 5
    }

    public readonly struct LedgerEntry
    {
        public readonly int Tick;
        public readonly LedgerEntryKind Kind;
        /// <summary>Positive for income, negative for expense. Cents.</summary>
        public readonly int Amount;

        public LedgerEntry(int tick, LedgerEntryKind kind, int amount)
        {
            Tick = tick;
            Kind = kind;
            Amount = amount;
        }
    }

    /// <summary>
    /// Cash balance plus a bounded transaction history. All amounts are integer cents;
    /// the sim never uses floating point for money.
    /// </summary>
    public sealed class Ledger
    {
        private const int MaxHistory = 512;

        private readonly List<LedgerEntry> _history = new List<LedgerEntry>();

        public int Balance { get; private set; }

        public IReadOnlyList<LedgerEntry> History => _history;

        public Ledger(int openingBalance)
        {
            Balance = openingBalance;
        }

        public void Record(int tick, LedgerEntryKind kind, int amount)
        {
            Balance += amount;
            _history.Add(new LedgerEntry(tick, kind, amount));
            if (_history.Count > MaxHistory) _history.RemoveAt(0);
        }

        public bool CanAfford(int cost) => Balance >= cost;

        /// <summary>Sum of a given entry kind over the whole retained history.</summary>
        public int TotalOf(LedgerEntryKind kind)
        {
            int sum = 0;
            for (int i = 0; i < _history.Count; i++)
            {
                if (_history[i].Kind == kind) sum += _history[i].Amount;
            }
            return sum;
        }

        internal void SetBalanceRaw(int balance) => Balance = balance;

        /// <summary>Restores a history entry during load without touching the balance.</summary>
        internal void AppendHistoryRaw(int tick, LedgerEntryKind kind, int amount)
            => _history.Add(new LedgerEntry(tick, kind, amount));
    }
}
