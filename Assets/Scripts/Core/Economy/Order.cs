using System;

namespace Worker.Core
{
    public enum OrderState : byte
    {
        Available = 0,
        Accepted = 1,
        Fulfilled = 2,
        Failed = 3
    }

    /// <summary>
    /// A customer contract. Orders are the pacing device: they set what the factory
    /// must produce, by when, and what the payroll is funded by.
    /// </summary>
    public sealed class Order
    {
        public readonly int Id;
        public readonly ItemId Item;
        public readonly int Quantity;

        /// <summary>Payout in cents on full delivery.</summary>
        public readonly int Payout;

        /// <summary>Tick by which the order must be complete once accepted.</summary>
        public int DeadlineTick;

        /// <summary>Penalty in cents if the deadline passes unfulfilled.</summary>
        public readonly int Penalty;

        public OrderState State;
        public int Delivered;

        public Order(int id, ItemId item, int quantity, int payout, int penalty)
        {
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            Id = id;
            Item = item;
            Quantity = quantity;
            Payout = payout;
            Penalty = penalty;
            State = OrderState.Available;
        }

        public int Remaining => Quantity - Delivered;
        public bool IsComplete => Delivered >= Quantity;

        public override string ToString()
            => "Order#" + Id + " " + Delivered + "/" + Quantity + "x" + Item + " (" + State + ")";
    }
}
