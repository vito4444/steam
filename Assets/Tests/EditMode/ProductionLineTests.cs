using System.Collections.Generic;
using NUnit.Framework;

namespace Worker.Core.Tests
{
    /// <summary>
    /// End-to-end tests. These are the ones that actually guard the game: they run the
    /// real scheduler, the real worker state machine and the real economy for thousands
    /// of ticks and assert that chairs come out the far end.
    /// </summary>
    [TestFixture]
    public class ProductionLineTests
    {
        [Test]
        public void SingleWorkerHaulsLogsAndSawsPlanks()
        {
            var world = Scenarios.MinimalHaulTest();
            var intake = world.BuildingsOfKind(BuildingKind.Intake)[0];
            var bench = world.BuildingsOfKind(BuildingKind.Sawbench)[0];
            intake.Output.TryAdd(ItemId.Log, 10);

            world.StepMany(2000);

            Assert.Greater(world.TotalCraftsCompleted, 0, "the worker never completed a single saw cycle");
            Assert.Greater(bench.Output.CountOf(ItemId.Plank) + StorageTotal(world, ItemId.Plank), 0,
                "planks were produced but ended up nowhere");
        }

        [Test]
        public void StarterFactoryShipsChairsAndFulfilsTheOpeningOrder()
        {
            var world = Scenarios.StarterFactory(seed: 7, workerCount: 4);
            var order = Scenarios.AddOpeningOrder(world);

            world.StepMany(SimConfig.TicksPerDay * 3);

            Assert.Greater(world.TotalUnitsShipped, 0,
                "no finished chair ever reached the shipping bay in three in-game days");
            Assert.AreEqual(OrderState.Fulfilled, order.State,
                "opening order stalled at " + order.Delivered + "/" + order.Quantity);
        }

        [Test]
        public void ProductionChainProducesEveryIntermediate()
        {
            var world = Scenarios.StarterFactory(seed: 3, workerCount: 4);
            Scenarios.AddOpeningOrder(world);

            var seen = new HashSet<RecipeId>();
            world.EventRaised += evt =>
            {
                if (evt.Kind == SimEventKind.CraftCompleted) seen.Add((RecipeId)evt.B);
            };

            world.StepMany(SimConfig.TicksPerDay * 3);

            Assert.Contains(RecipeId.SawLogs, new List<RecipeId>(seen), "logs were never sawn");
            Assert.Contains(RecipeId.CutSeat, new List<RecipeId>(seen), "seats were never cut");
            Assert.Contains(RecipeId.TurnLegs, new List<RecipeId>(seen), "legs were never turned");
            Assert.Contains(RecipeId.AssembleWoodChair, new List<RecipeId>(seen), "chairs were never assembled");
        }

        [Test]
        public void WorkersNeverStayIdleWhileThereIsWorkToDo()
        {
            var world = Scenarios.StarterFactory(seed: 11, workerCount: 3);
            Scenarios.AddOpeningOrder(world);

            // Warm up so the supply contract has delivered and tasks exist.
            world.StepMany(SimConfig.TicksPerDay / 2);

            int idleSamples = 0;
            int totalSamples = 0;
            for (int i = 0; i < 600; i++)
            {
                world.Step();
                for (int w = 0; w < world.Workers.Count; w++)
                {
                    totalSamples++;
                    if (world.Workers[w].IsIdle) idleSamples++;
                }
            }

            // Some idling is legitimate (waiting on the next log delivery), but a
            // scheduler that has stopped handing out work shows up as near-total idleness.
            Assert.Less(idleSamples, totalSamples * 9 / 10,
                "workers were idle " + idleSamples + "/" + totalSamples + " samples, scheduler is probably deadlocked");
        }

        [Test]
        public void HaulingMovesItemsWithoutChangingTheTotal()
        {
            var world = Scenarios.MinimalHaulTest(seed: 5);
            var intake = world.BuildingsOfKind(BuildingKind.Intake)[0];
            var storage = world.BuildingsOfKind(BuildingKind.Storage)[0];
            intake.Output.TryAdd(ItemId.Log, 12);

            // Idling the bench means the scheduler has nothing to offer, so the only
            // hauls that happen are the ones this test issues by hand.
            world.BuildingsOfKind(BuildingKind.Sawbench)[0].ActiveRecipe = RecipeId.None;

            var worker = world.Workers[0];
            int before = CountEverywhere(world, ItemId.Log);
            const int wantedHauls = 5;
            int issuedHauls = 0;

            for (int i = 0; i < 6000 && issuedHauls < wantedHauls; i++)
            {
                if (worker.IsIdle && worker.Carried.IsEmpty)
                {
                    worker.Task = WorkerTask.Haul(intake.Id, storage.Id, ItemId.Log, 2);
                    issuedHauls++;
                }
                world.Step();
            }

            // Drain any haul still in flight.
            world.StepMany(600);

            Assert.AreEqual(wantedHauls, issuedHauls, "the test never got to issue its hauls");
            Assert.Greater(storage.Output.CountOf(ItemId.Log), 0,
                "nothing was actually delivered, so this test would pass even if hauling were broken");
            Assert.AreEqual(before, CountEverywhere(world, ItemId.Log), "hauling leaked or duplicated logs");
        }

        [Test]
        public void FullChainConservesMaterialInPlankEquivalents()
        {
            // Every recipe is a pure transformation, so the factory's total material
            // measured in plank-equivalents may only grow when a log delivery arrives.
            var world = Scenarios.StarterFactory(seed: 29, workerCount: 4);
            Scenarios.AddOpeningOrder(world);

            int purchasedLogs = 0;
            world.EventRaised += evt => { };

            int previous = PlankEquivalents(world);
            for (int i = 0; i < SimConfig.TicksPerDay * 2; i++)
            {
                int beforeStep = PlankEquivalents(world);
                world.Step();
                int afterStep = PlankEquivalents(world);

                int delta = afterStep - beforeStep;
                if (delta > 0)
                {
                    // The only legitimate increase is a supply delivery of 12 logs.
                    Assert.AreEqual(12 * PlankValue(ItemId.Log), delta,
                        "material appeared out of nowhere at tick " + world.Tick);
                    purchasedLogs++;
                }
                else
                {
                    // Shipping removes finished goods; nothing else may destroy material.
                    Assert.GreaterOrEqual(delta, -PlankValue(ItemId.WoodChair) * 8,
                        "material vanished at tick " + world.Tick);
                }

                previous = afterStep;
            }

            Assert.Greater(purchasedLogs, 0, "no deliveries arrived, so conservation was never exercised");
            Assert.Greater(previous, 0);
        }

        /// <summary>Material content of an item expressed in planks, following the recipe tree.</summary>
        private static int PlankValue(ItemId item)
        {
            switch (item)
            {
                case ItemId.Log: return 6;        // 1 log -> 3 planks, scaled by 2
                case ItemId.Plank: return 2;
                case ItemId.ChairLeg: return 1;   // 1 plank -> 2 legs
                case ItemId.Seat: return 4;       // 2 planks
                case ItemId.WoodChair: return 8;  // 4 legs + 1 seat
                default: return 0;
            }
        }

        private static int PlankEquivalents(SimWorld world)
        {
            int total = 0;
            for (int i = 0; i < world.Buildings.Count; i++)
            {
                total += InventoryPlankValue(world.Buildings[i].Input);
                total += InventoryPlankValue(world.Buildings[i].Output);

                // Items riding a belt are still in the factory.
                var conveyor = world.Buildings[i].Conveyor;
                if (conveyor != null)
                {
                    for (int slot = 0; slot < ConveyorState.Capacity; slot++)
                    {
                        total += PlankValue(conveyor.ItemAt(slot));
                    }
                }

                // Work in progress: inputs are consumed the moment a craft starts.
                var building = world.Buildings[i];
                if (building.WorkProgress > 0)
                {
                    var recipe = building.CurrentRecipe();
                    if (recipe != null)
                    {
                        for (int r = 0; r < recipe.Inputs.Length; r++)
                        {
                            total += PlankValue(recipe.Inputs[r].Item) * recipe.Inputs[r].Count;
                        }
                    }
                }
            }

            for (int i = 0; i < world.Workers.Count; i++)
            {
                var carried = world.Workers[i].Carried;
                total += PlankValue(carried.Item) * carried.Count;
            }

            return total;
        }

        private static int InventoryPlankValue(Inventory inventory)
        {
            int total = 0;
            for (int i = 0; i < inventory.SlotCount; i++)
            {
                total += PlankValue(inventory[i].Item) * inventory[i].Count;
            }
            return total;
        }

        [Test]
        public void ExhaustedWorkersGoToTheBreakRoomAndRecover()
        {
            var world = Scenarios.StarterFactory(seed: 13, workerCount: 2);
            Scenarios.AddOpeningOrder(world);

            var worker = world.Workers[0];
            worker.AdjustStamina(-WorkerUnit.MaxStamina + 500);
            int lowest = worker.Stamina;

            world.StepMany(3000);

            Assert.Greater(worker.Stamina, lowest,
                "an exhausted worker never recovered despite a break room being available");
        }

        [Test]
        public void LayingOffAWorkerCostsMoraleForEveryoneElse()
        {
            var world = Scenarios.StarterFactory(seed: 17, workerCount: 3);
            var survivor = world.Workers[1];
            int moraleBefore = survivor.Morale;

            world.LayOffWorker(world.Workers[0].Id, severanceCents: 5000, moraleHitPerWorker: 1200);

            Assert.AreEqual(2, world.Workers.Count);
            Assert.AreEqual(moraleBefore - 1200, survivor.Morale,
                "remaining staff should take a morale hit when a colleague is let go");
        }

        [Test]
        public void FailedDeadlineChargesThePenalty()
        {
            var world = Scenarios.StarterFactory(seed: 19, workerCount: 1);
            var order = world.AddOrder(ItemId.OfficeChair, 5, 20000, 4000, deadlineTicksFromNow: 100);
            world.AcceptOrder(order.Id);

            world.StepMany(150);

            Assert.AreEqual(OrderState.Failed, order.State);
            // Asserting on the penalty line rather than the balance, because the standing
            // log contract also moves money during these 150 ticks.
            Assert.AreEqual(-4000, world.Ledger.TotalOf(LedgerEntryKind.OrderPenalty));
        }

        [Test]
        public void SupplyContractDeliversAndCharges()
        {
            var world = Scenarios.StarterFactory(seed: 23, workerCount: 1);
            var intake = world.BuildingsOfKind(BuildingKind.Intake)[0];

            world.StepMany(10);

            Assert.Greater(intake.Output.CountOf(ItemId.Log) + CountEverywhere(world, ItemId.Log), 0,
                "the standing log contract never delivered");
            Assert.Less(world.Ledger.TotalOf(LedgerEntryKind.MaterialPurchase), 0,
                "a delivery arrived without being paid for");
        }

        private static int StorageTotal(SimWorld world, ItemId item)
        {
            int total = 0;
            var storages = world.BuildingsOfKind(BuildingKind.Storage);
            for (int i = 0; i < storages.Count; i++)
            {
                total += storages[i].Output.CountOf(item) + storages[i].Input.CountOf(item);
            }
            return total;
        }

        private static int CountEverywhere(SimWorld world, ItemId item)
        {
            int total = 0;
            for (int i = 0; i < world.Buildings.Count; i++)
            {
                total += world.Buildings[i].Input.CountOf(item);
                total += world.Buildings[i].Output.CountOf(item);

                var conveyor = world.Buildings[i].Conveyor;
                if (conveyor == null) continue;
                for (int slot = 0; slot < ConveyorState.Capacity; slot++)
                {
                    if (conveyor.ItemAt(slot) == item) total++;
                }
            }
            for (int i = 0; i < world.Workers.Count; i++)
            {
                if (world.Workers[i].Carried.Item == item) total += world.Workers[i].Carried.Count;
            }
            return total;
        }
    }

    [TestFixture]
    public class SimDeterminismTests
    {
        [Test]
        public void SameSeedProducesIdenticalStateHash()
        {
            ulong first = RunAndHash(seed: 42, ticks: 4000);
            ulong second = RunAndHash(seed: 42, ticks: 4000);

            Assert.AreEqual(first, second,
                "identical seeds diverged: " + StateHash.ToHex(first) + " vs " + StateHash.ToHex(second));
        }

        [Test]
        public void DifferentSeedsDoNotAccidentallyCollide()
        {
            // Layout is currently seed-independent, so this asserts the hash reacts to
            // real state rather than being a constant.
            ulong a = RunAndHash(seed: 1, ticks: 2000, workerCount: 3);
            ulong b = RunAndHash(seed: 1, ticks: 2000, workerCount: 5);

            Assert.AreNotEqual(a, b, "state hash ignored a change in worker count");
        }

        [Test]
        public void HashChangesAsTheWorldEvolves()
        {
            var world = Scenarios.StarterFactory(seed: 8, workerCount: 3);
            Scenarios.AddOpeningOrder(world);

            ulong atStart = StateHash.Compute(world);
            world.StepMany(1000);
            ulong later = StateHash.Compute(world);

            Assert.AreNotEqual(atStart, later, "1000 ticks of simulation produced no state change at all");
        }

        [Test]
        public void SteppingOneAtATimeMatchesSteppingInBulk()
        {
            var single = Scenarios.StarterFactory(seed: 31, workerCount: 3);
            Scenarios.AddOpeningOrder(single);
            for (int i = 0; i < 2500; i++) single.Step();

            var bulk = Scenarios.StarterFactory(seed: 31, workerCount: 3);
            Scenarios.AddOpeningOrder(bulk);
            bulk.StepMany(2500);

            Assert.AreEqual(StateHash.Compute(single), StateHash.Compute(bulk));
        }

        private static ulong RunAndHash(uint seed, int ticks, int workerCount = 4)
        {
            var world = Scenarios.StarterFactory(seed, workerCount);
            Scenarios.AddOpeningOrder(world);
            world.StepMany(ticks);
            return StateHash.Compute(world);
        }
    }
}
