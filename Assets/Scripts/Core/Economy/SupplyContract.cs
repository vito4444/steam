namespace Worker.Core
{
    /// <summary>
    /// A standing purchase order that periodically drops raw material into an intake bay.
    /// Deliveries are skipped (not queued) when the intake is full or the balance is short,
    /// which is the pressure that makes storage layout and cash flow matter.
    /// </summary>
    public sealed class SupplyContract
    {
        public readonly int Id;
        public readonly ItemId Item;
        public readonly int QuantityPerDelivery;
        public readonly int IntervalTicks;
        public readonly int UnitCostCents;

        public int NextDeliveryTick;
        public bool Active;

        /// <summary>Deliveries that could not be received. Surfaced in the UI as a warning.</summary>
        public int MissedDeliveries;

        public SupplyContract(int id, ItemId item, int quantityPerDelivery, int intervalTicks, int unitCostCents, int firstDeliveryTick)
        {
            Id = id;
            Item = item;
            QuantityPerDelivery = quantityPerDelivery;
            IntervalTicks = intervalTicks;
            UnitCostCents = unitCostCents;
            NextDeliveryTick = firstDeliveryTick;
            Active = true;
        }

        public int DeliveryCost => QuantityPerDelivery * UnitCostCents;
    }
}
