namespace Worker.Core
{
    /// <summary>
    /// FNV-1a hash over every piece of simulation state that must be reproducible.
    /// Two runs from the same seed and the same commands must produce the same hash at
    /// the same tick; the self-test harness asserts exactly that before it trusts a
    /// screenshot comparison.
    /// </summary>
    public static class StateHash
    {
        private const ulong Offset = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        public static ulong Compute(SimWorld world)
        {
            ulong hash = Offset;

            Mix(ref hash, (ulong)world.Tick);
            Mix(ref hash, (ulong)(long)world.Ledger.Balance);
            Mix(ref hash, (ulong)world.TotalUnitsShipped);
            Mix(ref hash, (ulong)world.TotalCraftsCompleted);

            var buildings = world.Buildings;
            for (int i = 0; i < buildings.Count; i++)
            {
                var building = buildings[i];
                Mix(ref hash, (ulong)building.Id);
                Mix(ref hash, (ulong)building.Kind);
                Mix(ref hash, (ulong)(uint)building.Origin.X);
                Mix(ref hash, (ulong)(uint)building.Origin.Y);
                Mix(ref hash, (ulong)building.ActiveRecipe);
                Mix(ref hash, (ulong)(long)building.WorkProgress);
                Mix(ref hash, (ulong)building.OperatorWorkerId);
                MixInventory(ref hash, building.Input);
                MixInventory(ref hash, building.Output);

                if (building.Conveyor != null)
                {
                    for (int slot = 0; slot < ConveyorState.Capacity; slot++)
                    {
                        Mix(ref hash, (ulong)building.Conveyor.ItemAt(slot));
                        Mix(ref hash, (ulong)(long)building.Conveyor.ProgressAt(slot));
                    }
                }
            }

            var workers = world.Workers;
            for (int i = 0; i < workers.Count; i++)
            {
                var worker = workers[i];
                Mix(ref hash, (ulong)worker.Id);
                Mix(ref hash, (ulong)(uint)worker.Pos.X);
                Mix(ref hash, (ulong)(uint)worker.Pos.Y);
                Mix(ref hash, (ulong)(long)worker.Stamina);
                Mix(ref hash, (ulong)(long)worker.Morale);
                Mix(ref hash, (ulong)worker.Carried.Item);
                Mix(ref hash, (ulong)(long)worker.Carried.Count);
                Mix(ref hash, (ulong)(worker.Task?.Kind ?? TaskKind.None));
                Mix(ref hash, (ulong)(worker.Task?.Phase ?? TaskPhase.Done));
                Mix(ref hash, (ulong)(worker.Task?.SourceBuildingId ?? 0));
                Mix(ref hash, (ulong)(worker.Task?.TargetBuildingId ?? 0));
                Mix(ref hash, (ulong)(long)worker.PathCursor);
                Mix(ref hash, (ulong)(long)worker.MoveCooldown);
            }

            var orders = world.Orders;
            for (int i = 0; i < orders.Count; i++)
            {
                var order = orders[i];
                Mix(ref hash, (ulong)order.Id);
                Mix(ref hash, (ulong)order.State);
                Mix(ref hash, (ulong)(long)order.Delivered);
            }

            var contracts = world.Contracts;
            for (int i = 0; i < contracts.Count; i++)
            {
                var contract = contracts[i];
                Mix(ref hash, (ulong)contract.Id);
                Mix(ref hash, (ulong)(long)contract.NextDeliveryTick);
                Mix(ref hash, (ulong)(long)contract.MissedDeliveries);
            }

            return hash;
        }

        private static void MixInventory(ref ulong hash, Inventory inventory)
        {
            for (int i = 0; i < inventory.SlotCount; i++)
            {
                Mix(ref hash, (ulong)inventory[i].Item);
                Mix(ref hash, (ulong)(long)inventory[i].Count);
            }
        }

        private static void Mix(ref ulong hash, ulong value)
        {
            for (int i = 0; i < 8; i++)
            {
                hash ^= (value >> (i * 8)) & 0xFF;
                hash *= Prime;
            }
        }

        public static string ToHex(ulong hash) => hash.ToString("x16");
    }
}
