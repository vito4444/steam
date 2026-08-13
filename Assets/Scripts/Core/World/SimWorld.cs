using System;
using System.Collections.Generic;

namespace Worker.Core
{
    /// <summary>
    /// Root of the deterministic simulation. Everything that affects game state lives
    /// here or is reachable from here; the Unity layer only reads this and draws it.
    ///
    /// Contract: given the same seed and the same ordered sequence of commands,
    /// <see cref="Step"/> produces byte-identical state on every machine. That is what
    /// makes the automated self-test screenshots comparable across runs.
    /// </summary>
    public sealed class SimWorld
    {
        public readonly TileMap Map;
        public readonly Pathfinder Path;
        public readonly Ledger Ledger;
        public readonly DeterministicRandom Random;
        public readonly uint Seed;

        private readonly Dictionary<int, BuildingInstance> _buildingsById = new Dictionary<int, BuildingInstance>();
        private readonly List<BuildingInstance> _buildings = new List<BuildingInstance>();

        /// <summary>Belts, cached in insertion order so conveyor updates stay deterministic.</summary>
        private readonly List<BuildingInstance> _conveyors = new List<BuildingInstance>();

        /// <summary>Buildings that can emit onto a belt, cached for the same reason.</summary>
        private readonly List<BuildingInstance> _emitters = new List<BuildingInstance>();
        private readonly List<WorkerUnit> _workers = new List<WorkerUnit>();
        private readonly List<Order> _orders = new List<Order>();
        private readonly List<SupplyContract> _contracts = new List<SupplyContract>();
        private readonly TaskScheduler _scheduler;
        private readonly WorkerSystem _workerSystem;

        private int _nextBuildingId = 1;
        private int _nextWorkerId = 1;
        private int _nextOrderId = 1;
        private int _nextContractId = 1;

        public int Tick { get; private set; }

        /// <summary>Raised for anything the presentation or logging layer wants to react to.</summary>
        public event Action<SimEvent> EventRaised;

        public IReadOnlyList<BuildingInstance> Buildings => _buildings;
        public IReadOnlyList<WorkerUnit> Workers => _workers;
        public IReadOnlyList<Order> Orders => _orders;
        public IReadOnlyList<SupplyContract> Contracts => _contracts;

        /// <summary>Cumulative count of finished goods handed to <see cref="BuildingKind.Shipping"/>.</summary>
        public int TotalUnitsShipped { get; private set; }

        /// <summary>Cumulative count of recipe completions across all stations.</summary>
        public int TotalCraftsCompleted { get; private set; }

        public SimWorld(uint seed, int width = SimConfig.DefaultMapWidth, int height = SimConfig.DefaultMapHeight, int openingBalance = 500000)
        {
            Seed = seed;
            Random = new DeterministicRandom(seed);
            Map = new TileMap(width, height);
            Path = new Pathfinder(Map);
            Ledger = new Ledger(openingBalance);
            _scheduler = new TaskScheduler(this);
            _workerSystem = new WorkerSystem(this);
        }

        public void Raise(SimEvent evt) => EventRaised?.Invoke(evt);

        // ---------------------------------------------------------------- buildings

        public BuildingInstance GetBuilding(int id)
            => id != 0 && _buildingsById.TryGetValue(id, out var b) ? b : null;

        public BuildingInstance BuildingAt(GridPos pos) => GetBuilding(Map.GetBuildingId(pos));

        /// <summary>Places a building without charging for it. Used by scenario setup and save loading.</summary>
        public BuildingInstance PlaceBuildingFree(BuildingKind kind, GridPos origin, Direction facing = Direction.North)
        {
            if (!Map.CanPlace(kind, origin, facing))
            {
                throw new InvalidOperationException("Cannot place " + kind + " at " + origin + " facing " + facing);
            }

            var building = new BuildingInstance(_nextBuildingId++, kind, origin, facing);
            _buildingsById[building.Id] = building;
            _buildings.Add(building);
            IndexBuilding(building);
            Map.Occupy(building);

            // Stations default to their first available recipe so a freshly placed
            // bench is immediately useful; the player can change it afterwards.
            if (building.Def.IsStation)
            {
                var options = GameData.RecipesFor(kind);
                if (options.Count > 0) building.ActiveRecipe = options[0].Id;
            }

            Raise(SimEvent.BuildingPlaced(Tick, building.Id, kind, origin));
            return building;
        }

        /// <summary>Places a building and charges its cost. Returns null when unaffordable or blocked.</summary>
        public BuildingInstance TryBuild(BuildingKind kind, GridPos origin, Direction facing = Direction.North)
        {
            var def = BuildingData.Get(kind);
            if (!Ledger.CanAfford(def.Cost)) return null;
            if (!Map.CanPlace(kind, origin, facing)) return null;

            var building = PlaceBuildingFree(kind, origin, facing);
            if (def.Cost > 0) Ledger.Record(Tick, LedgerEntryKind.Construction, -def.Cost);
            return building;
        }

        public bool RemoveBuilding(int id)
        {
            var building = GetBuilding(id);
            if (building == null) return false;

            // Any worker committed to this building must let go before it disappears.
            for (int i = 0; i < _workers.Count; i++)
            {
                var worker = _workers[i];
                if (worker.Task == null) continue;
                if (worker.Task.SourceBuildingId == id || worker.Task.TargetBuildingId == id)
                {
                    worker.AbandonTask();
                }
            }

            Map.Vacate(building);
            _buildings.Remove(building);
            _conveyors.Remove(building);
            _emitters.Remove(building);
            _buildingsById.Remove(id);
            Raise(SimEvent.BuildingRemoved(Tick, id));
            return true;
        }

        private void IndexBuilding(BuildingInstance building)
        {
            if (building.IsConveyor) _conveyors.Add(building);

            // Intakes and stations push their output onto a belt automatically. Storage
            // deliberately does not: shelves are a buffer the player controls, and a
            // self-emptying shelf would make layout planning impossible.
            if (building.Kind == BuildingKind.Intake || building.Def.IsStation) _emitters.Add(building);
        }

        public List<BuildingInstance> BuildingsOfKind(BuildingKind kind)
        {
            var result = new List<BuildingInstance>();
            for (int i = 0; i < _buildings.Count; i++)
            {
                if (_buildings[i].Kind == kind) result.Add(_buildings[i]);
            }
            return result;
        }

        // ------------------------------------------------------------------ workers

        public WorkerUnit HireWorker(string name, GridPos pos, int speedRating = 100, int dailyWage = 8000)
        {
            var worker = new WorkerUnit(_nextWorkerId++, name, pos, speedRating, dailyWage);
            _workers.Add(worker);
            Raise(SimEvent.WorkerHired(Tick, worker.Id, name));
            return worker;
        }

        public WorkerUnit GetWorker(int id)
        {
            for (int i = 0; i < _workers.Count; i++)
            {
                if (_workers[i].Id == id) return _workers[i];
            }
            return null;
        }

        /// <summary>
        /// Removes a worker and pays severance. Every remaining worker loses morale:
        /// this is the cost side of automating a job away.
        /// </summary>
        public bool LayOffWorker(int id, int severanceCents, int moraleHitPerWorker)
        {
            var worker = GetWorker(id);
            if (worker == null) return false;

            worker.AbandonTask();
            _workers.Remove(worker);

            if (severanceCents > 0) Ledger.Record(Tick, LedgerEntryKind.Severance, -severanceCents);

            for (int i = 0; i < _workers.Count; i++)
            {
                _workers[i].AdjustMorale(-moraleHitPerWorker);
            }

            Raise(SimEvent.WorkerLaidOff(Tick, id, worker.Name));
            return true;
        }

        // ------------------------------------------------------------------- orders

        public Order AddOrder(ItemId item, int quantity, int payout, int penalty, int deadlineTicksFromNow)
        {
            var order = new Order(_nextOrderId++, item, quantity, payout, penalty)
            {
                DeadlineTick = Tick + deadlineTicksFromNow
            };
            _orders.Add(order);
            return order;
        }

        public bool AcceptOrder(int orderId)
        {
            for (int i = 0; i < _orders.Count; i++)
            {
                if (_orders[i].Id != orderId) continue;
                if (_orders[i].State != OrderState.Available) return false;
                _orders[i].State = OrderState.Accepted;
                Raise(SimEvent.OrderAccepted(Tick, orderId));
                return true;
            }
            return false;
        }

        /// <summary>Total outstanding demand for an item across accepted orders.</summary>
        public int OutstandingDemand(ItemId item)
        {
            int total = 0;
            for (int i = 0; i < _orders.Count; i++)
            {
                var order = _orders[i];
                if (order.State == OrderState.Accepted && order.Item == item) total += order.Remaining;
            }
            return total;
        }

        // ---------------------------------------------------------- supply contracts

        public SupplyContract AddSupplyContract(ItemId item, int quantityPerDelivery, int intervalTicks, int unitCostCents, int firstDeliveryTick = 0)
        {
            var contract = new SupplyContract(_nextContractId++, item, quantityPerDelivery, intervalTicks, unitCostCents,
                firstDeliveryTick > 0 ? firstDeliveryTick : Tick + 1);
            _contracts.Add(contract);
            return contract;
        }

        private void UpdateSupplyContracts()
        {
            for (int i = 0; i < _contracts.Count; i++)
            {
                var contract = _contracts[i];
                if (!contract.Active) continue;
                if (Tick < contract.NextDeliveryTick) continue;

                contract.NextDeliveryTick = Tick + contract.IntervalTicks;

                if (!Ledger.CanAfford(contract.DeliveryCost))
                {
                    contract.MissedDeliveries++;
                    continue;
                }

                var bay = FindIntakeWithRoom(contract.Item, contract.QuantityPerDelivery);
                if (bay == null)
                {
                    contract.MissedDeliveries++;
                    continue;
                }

                int stored = bay.Output.TryAdd(contract.Item, contract.QuantityPerDelivery);
                if (stored <= 0)
                {
                    contract.MissedDeliveries++;
                    continue;
                }

                Ledger.Record(Tick, LedgerEntryKind.MaterialPurchase, -(stored * contract.UnitCostCents));
            }
        }

        private BuildingInstance FindIntakeWithRoom(ItemId item, int quantity)
        {
            for (int i = 0; i < _buildings.Count; i++)
            {
                var building = _buildings[i];
                if (building.Kind != BuildingKind.Intake) continue;
                if (building.Output.SpaceFor(item) >= quantity) return building;
            }
            return null;
        }

        // ---------------------------------------------------------------- conveyors

        /// <summary>
        /// Moves belt contents one tick.
        ///
        /// Order matters: hand-offs are resolved before advancement so an item that
        /// leaves a belt frees its slot in the same tick, and belts are always walked in
        /// insertion order so the result is reproducible.
        /// </summary>
        private void UpdateConveyors()
        {
            for (int i = 0; i < _conveyors.Count; i++)
            {
                var belt = _conveyors[i];
                var item = belt.Conveyor.PeekOutput();
                if (item == ItemId.None) continue;

                var target = BuildingAt(belt.ConveyorTarget);
                if (target == null) continue;

                if (target.IsConveyor)
                {
                    // Never push back into the belt that feeds us; that would be a loop
                    // of two tiles handing the same item back and forth.
                    if (target.ConveyorTarget == belt.Origin) continue;
                    if (!target.Conveyor.CanAccept) continue;

                    target.Conveyor.TryAccept(item);
                    belt.Conveyor.TakeOutput();
                    continue;
                }

                var destination = target.Def.InputSlots > 0 ? target.Input : target.Output;
                if (destination.TryAdd(item, 1) == 1) belt.Conveyor.TakeOutput();
            }

            for (int i = 0; i < _conveyors.Count; i++)
            {
                _conveyors[i].Conveyor.Advance();
            }

            EmitOntoConveyors();
        }

        /// <summary>
        /// Lets stations and intakes drop their output onto an adjacent belt. This is the
        /// mechanism that turns a belt into saved labour: anything a belt carries is a
        /// trip no worker has to walk.
        /// </summary>
        private void EmitOntoConveyors()
        {
            for (int i = 0; i < _emitters.Count; i++)
            {
                var source = _emitters[i];
                if (source.IsConveyor) continue;
                if (source.Output.IsEmpty) continue;

                int slot = source.Output.FirstOccupiedSlot();
                if (slot < 0) continue;

                var stack = source.Output[slot];
                var belt = FindOutgoingBelt(source);
                if (belt == null) continue;

                if (!belt.Conveyor.TryAccept(stack.Item)) continue;
                source.Output.TryRemove(stack.Item, 1);
            }
        }

        /// <summary>
        /// First belt adjacent to <paramref name="source"/> that leads away from it and
        /// has room. Adjacency is scanned in the building's stable tile order.
        /// </summary>
        private BuildingInstance FindOutgoingBelt(BuildingInstance source)
        {
            var tiles = source.AdjacentTiles();
            for (int i = 0; i < tiles.Count; i++)
            {
                var candidate = BuildingAt(tiles[i]);
                if (candidate == null || !candidate.IsConveyor) continue;
                if (!candidate.Conveyor.CanAccept) continue;

                // A belt pointing back into this building would just return the item.
                if (source.Covers(candidate.ConveyorTarget)) continue;

                return candidate;
            }
            return null;
        }

        // --------------------------------------------------------------------- tick

        public void Step()
        {
            Tick++;

            UpdateSupplyContracts();

            if (Tick % SimConfig.SchedulerIntervalTicks == 0)
            {
                _scheduler.AssignTasks();
            }

            _workerSystem.Update();
            UpdateConveyors();
            UpdateShipping();

            if (Tick % SimConfig.TicksPerDay == 0)
            {
                PayWages();
            }

            UpdateOrderDeadlines();
        }

        public void StepMany(int ticks)
        {
            for (int i = 0; i < ticks; i++) Step();
        }

        /// <summary>Consumes finished goods sitting in shipping bays to fill accepted orders.</summary>
        private void UpdateShipping()
        {
            for (int b = 0; b < _buildings.Count; b++)
            {
                var bay = _buildings[b];
                if (bay.Kind != BuildingKind.Shipping) continue;
                if (bay.Input.IsEmpty) continue;

                for (int o = 0; o < _orders.Count; o++)
                {
                    var order = _orders[o];
                    if (order.State != OrderState.Accepted) continue;

                    int available = bay.Input.CountOf(order.Item);
                    if (available <= 0) continue;

                    int moved = available < order.Remaining ? available : order.Remaining;
                    bay.Input.TryRemove(order.Item, moved);
                    order.Delivered += moved;
                    TotalUnitsShipped += moved;

                    if (order.IsComplete)
                    {
                        order.State = OrderState.Fulfilled;
                        Ledger.Record(Tick, LedgerEntryKind.OrderPayout, order.Payout);
                        Raise(SimEvent.OrderFulfilled(Tick, order.Id, order.Payout));
                    }
                }
            }
        }

        private void UpdateOrderDeadlines()
        {
            for (int i = 0; i < _orders.Count; i++)
            {
                var order = _orders[i];
                if (order.State != OrderState.Accepted) continue;
                if (Tick < order.DeadlineTick) continue;

                order.State = OrderState.Failed;
                if (order.Penalty > 0) Ledger.Record(Tick, LedgerEntryKind.OrderPenalty, -order.Penalty);
                Raise(SimEvent.OrderFailed(Tick, order.Id, order.Penalty));
            }
        }

        private void PayWages()
        {
            int total = 0;
            for (int i = 0; i < _workers.Count; i++) total += _workers[i].DailyWage;
            if (total <= 0) return;

            Ledger.Record(Tick, LedgerEntryKind.Wages, -total);
            Raise(SimEvent.WagesPaid(Tick, total));
        }

        internal void NotifyCraftCompleted() => TotalCraftsCompleted++;

        // ---------------------------------------------------------- save/load support

        internal int PeekNextBuildingId() => _nextBuildingId;
        internal int PeekNextWorkerId() => _nextWorkerId;
        internal int PeekNextOrderId() => _nextOrderId;
        internal int PeekNextContractId() => _nextContractId;

        internal void RestoreCounters(int nextBuilding, int nextWorker, int nextOrder, int nextContract)
        {
            _nextBuildingId = nextBuilding;
            _nextWorkerId = nextWorker;
            _nextOrderId = nextOrder;
            _nextContractId = nextContract;
        }

        internal void RestoreTick(int tick) => Tick = tick;

        internal void RestoreTotals(int shipped, int crafts)
        {
            TotalUnitsShipped = shipped;
            TotalCraftsCompleted = crafts;
        }

        internal void RegisterLoadedBuilding(BuildingInstance building)
        {
            _buildingsById[building.Id] = building;
            _buildings.Add(building);
            IndexBuilding(building);
            Map.Occupy(building);
        }

        internal void RegisterLoadedWorker(WorkerUnit worker) => _workers.Add(worker);

        internal void RegisterLoadedOrder(Order order) => _orders.Add(order);

        internal void RegisterLoadedContract(SupplyContract contract) => _contracts.Add(contract);
    }
}
