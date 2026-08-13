using System.Collections.Generic;

namespace Worker.Core
{
    /// <summary>
    /// Turns the current world state into work orders and hands them to idle workers.
    ///
    /// Two properties matter more than cleverness here:
    /// 1. No deadlocks. Every task is re-validated on arrival, and in-flight hauls are
    ///    counted so two workers never chase the same crate or overfill the same buffer.
    /// 2. Determinism. Buildings and workers are always scanned in insertion order and
    ///    ties break on the lowest id, so a replay of the same seed assigns the same jobs.
    /// </summary>
    internal sealed class TaskScheduler
    {
        private readonly SimWorld _world;

        /// <summary>Items already on their way into a building: key is building+item.</summary>
        private readonly Dictionary<long, int> _incoming = new Dictionary<long, int>();

        /// <summary>Items already promised out of a building: key is building+item.</summary>
        private readonly Dictionary<long, int> _outgoing = new Dictionary<long, int>();

        private readonly HashSet<int> _claimedStations = new HashSet<int>();
        private readonly List<WorkerUnit> _idleWorkers = new List<WorkerUnit>();

        public TaskScheduler(SimWorld world)
        {
            _world = world;
        }

        private static long Key(int buildingId, ItemId item) => ((long)buildingId << 16) | (ushort)item;

        public void AssignTasks()
        {
            BuildReservations();

            _idleWorkers.Clear();
            var workers = _world.Workers;
            for (int i = 0; i < workers.Count; i++)
            {
                if (workers[i].IsIdle) _idleWorkers.Add(workers[i]);
            }
            if (_idleWorkers.Count == 0) return;

            for (int i = 0; i < _idleWorkers.Count; i++)
            {
                var worker = _idleWorkers[i];
                if (!worker.IsIdle) continue;

                if (TryAssignDropCarried(worker)) continue;
                if (TryAssignRest(worker)) continue;
                if (TryAssignShipFinishedGoods(worker)) continue;
                if (TryAssignFeedStation(worker)) continue;
                if (TryAssignOperateStation(worker)) continue;
                TryAssignClearOutput(worker);
            }
        }

        private void BuildReservations()
        {
            _incoming.Clear();
            _outgoing.Clear();
            _claimedStations.Clear();

            var workers = _world.Workers;
            for (int i = 0; i < workers.Count; i++)
            {
                var task = workers[i].Task;
                if (task == null) continue;

                if (task.Kind == TaskKind.Operate)
                {
                    _claimedStations.Add(task.TargetBuildingId);
                }
                else if (task.Kind == TaskKind.Haul)
                {
                    if (task.Phase == TaskPhase.MovingToSource || task.Phase == TaskPhase.PickingUp)
                    {
                        Add(_outgoing, Key(task.SourceBuildingId, task.Item), task.Count);
                    }
                    Add(_incoming, Key(task.TargetBuildingId, task.Item), task.Count);
                }
            }

            // A station being physically operated also counts as claimed, even if the
            // operator's task record was already cleared this tick.
            var buildings = _world.Buildings;
            for (int i = 0; i < buildings.Count; i++)
            {
                if (buildings[i].OperatorWorkerId != 0) _claimedStations.Add(buildings[i].Id);
            }
        }

        private static void Add(Dictionary<long, int> map, long key, int amount)
        {
            map.TryGetValue(key, out int current);
            map[key] = current + amount;
        }

        private static int Get(Dictionary<long, int> map, long key)
        {
            map.TryGetValue(key, out int value);
            return value;
        }

        /// <summary>Items in a building that are not already promised to another worker.</summary>
        private int FreeStock(BuildingInstance building, ItemId item)
        {
            var container = WorkerSystem.SourceContainer(building);
            int stock = container.CountOf(item);
            return stock - Get(_outgoing, Key(building.Id, item));
        }

        /// <summary>Space in a building that is not already spoken for by an in-flight haul.</summary>
        private int FreeSpace(BuildingInstance building, ItemId item)
        {
            var container = WorkerSystem.TargetContainer(building);
            int space = container.SpaceFor(item);
            return space - Get(_incoming, Key(building.Id, item));
        }

        // ------------------------------------------------------------ rule 1: hands full

        /// <summary>A worker holding leftovers must put them down before doing anything else.</summary>
        private bool TryAssignDropCarried(WorkerUnit worker)
        {
            if (worker.Carried.IsEmpty) return false;

            var item = worker.Carried.Item;
            var destination = FindBestDropOff(worker, item, worker.Carried.Count);
            if (destination == null) return false;

            var task = WorkerTask.Haul(0, destination.Id, item, worker.Carried.Count);
            task.Phase = TaskPhase.MovingToTarget;
            worker.Task = task;
            Add(_incoming, Key(destination.Id, item), worker.Carried.Count);
            return true;
        }

        private BuildingInstance FindBestDropOff(WorkerUnit worker, ItemId item, int count)
        {
            // Prefer a shipping bay when the item is actually wanted by an accepted order.
            if (_world.OutstandingDemand(item) > 0)
            {
                var bay = FindNearest(worker, BuildingKind.Shipping, b => FreeSpace(b, item) >= count);
                if (bay != null) return bay;
            }

            var storage = FindNearest(worker, BuildingKind.Storage, b => FreeSpace(b, item) >= count);
            if (storage != null) return storage;

            // Last resort: any station that consumes this item.
            return FindNearest(worker, BuildingKind.None,
                b => b.Def.IsStation && ConsumesItem(b, item) && FreeSpace(b, item) >= count);
        }

        // ---------------------------------------------------------------- rule 2: rest

        private bool TryAssignRest(WorkerUnit worker)
        {
            if (worker.Stamina > SimConfig.StaminaSeekRestThreshold) return false;

            var room = FindNearest(worker, BuildingKind.BreakRoom, null);
            if (room == null)
            {
                // Nowhere to rest. The worker keeps going and morale suffers in WorkerSystem.
                _world.Raise(SimEvent.WorkerExhausted(_world.Tick, worker.Id));
                return false;
            }

            worker.Task = WorkerTask.Rest(room.Id);
            return true;
        }

        // ------------------------------------------------------- rule 3: ship finished goods

        private bool TryAssignShipFinishedGoods(WorkerUnit worker)
        {
            var buildings = _world.Buildings;

            for (int i = 0; i < buildings.Count; i++)
            {
                var source = buildings[i];
                if (source.Kind == BuildingKind.Shipping) continue;

                var container = WorkerSystem.SourceContainer(source);
                if (container.IsEmpty) continue;

                for (int slot = 0; slot < container.SlotCount; slot++)
                {
                    var stack = container[slot];
                    if (stack.IsEmpty) continue;

                    int demand = _world.OutstandingDemand(stack.Item);
                    if (demand <= 0) continue;

                    int free = FreeStock(source, stack.Item);
                    if (free <= 0) continue;

                    var bay = FindNearest(worker, BuildingKind.Shipping, b => FreeSpace(b, stack.Item) > 0);
                    if (bay == null) continue;

                    int amount = Clamp(free, demand, FreeSpace(bay, stack.Item), SimConfig.CarryCapacity);
                    if (amount <= 0) continue;

                    Assign(worker, source, bay, stack.Item, amount);
                    return true;
                }
            }

            return false;
        }

        // --------------------------------------------------------- rule 4: feed stations

        private bool TryAssignFeedStation(WorkerUnit worker)
        {
            var buildings = _world.Buildings;

            for (int i = 0; i < buildings.Count; i++)
            {
                var station = buildings[i];
                if (!station.Def.IsStation) continue;

                var recipe = station.CurrentRecipe();
                if (recipe == null) continue;

                for (int r = 0; r < recipe.Inputs.Length; r++)
                {
                    var need = recipe.Inputs[r];

                    // Keep roughly two batches of headroom so the bench does not stall
                    // the moment a worker walks away.
                    int target = need.Count * 2;
                    int held = station.Input.CountOf(need.Item);
                    int enRoute = Get(_incoming, Key(station.Id, need.Item));
                    int shortfall = target - held - enRoute;
                    if (shortfall <= 0) continue;

                    int space = FreeSpace(station, need.Item);
                    if (space <= 0) continue;

                    var source = FindBestSource(worker, need.Item, station.Id);
                    if (source == null) continue;

                    int amount = Clamp(FreeStock(source, need.Item), shortfall, space, SimConfig.CarryCapacity);
                    if (amount <= 0) continue;

                    Assign(worker, source, station, need.Item, amount);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Picks where to fetch an input from. Intakes and storage come first; pulling
        /// straight from an upstream bench's output is allowed and keeps chains flowing.
        /// </summary>
        private BuildingInstance FindBestSource(WorkerUnit worker, ItemId item, int excludeBuildingId)
        {
            var best = FindNearest(worker, BuildingKind.Intake, b => FreeStock(b, item) > 0);
            if (best != null) return best;

            best = FindNearest(worker, BuildingKind.Storage, b => FreeStock(b, item) > 0);
            if (best != null) return best;

            return FindNearest(worker, BuildingKind.None,
                b => b.Id != excludeBuildingId
                     && b.Kind != BuildingKind.Shipping
                     && FreeStock(b, item) > 0);
        }

        // ------------------------------------------------------- rule 5: operate stations

        private bool TryAssignOperateStation(WorkerUnit worker)
        {
            var buildings = _world.Buildings;
            BuildingInstance best = null;
            int bestDistance = int.MaxValue;

            for (int i = 0; i < buildings.Count; i++)
            {
                var station = buildings[i];
                if (!station.Def.IsStation) continue;
                if (_claimedStations.Contains(station.Id)) continue;
                if (station.WorkProgress == 0 && !station.CanStartWork()) continue;

                int distance = worker.Pos.ManhattanTo(station.CenterTile());
                if (distance >= bestDistance) continue;

                best = station;
                bestDistance = distance;
            }

            if (best == null) return false;

            worker.Task = WorkerTask.Operate(best.Id);
            _claimedStations.Add(best.Id);
            return true;
        }

        // --------------------------------------------------- rule 6: unclog full outputs

        /// <summary>Moves finished sub-assemblies out of a bench's output into storage so it can keep running.</summary>
        private bool TryAssignClearOutput(WorkerUnit worker)
        {
            var buildings = _world.Buildings;

            for (int i = 0; i < buildings.Count; i++)
            {
                var station = buildings[i];
                if (!station.Def.IsStation) continue;
                if (station.Output.IsEmpty) continue;

                for (int slot = 0; slot < station.Output.SlotCount; slot++)
                {
                    var stack = station.Output[slot];
                    if (stack.IsEmpty) continue;

                    int free = FreeStock(station, stack.Item);
                    if (free <= 0) continue;

                    var storage = FindNearest(worker, BuildingKind.Storage, b => FreeSpace(b, stack.Item) > 0);
                    if (storage == null) continue;

                    int amount = Clamp(free, FreeSpace(storage, stack.Item), SimConfig.CarryCapacity, int.MaxValue);
                    if (amount <= 0) continue;

                    Assign(worker, station, storage, stack.Item, amount);
                    return true;
                }
            }

            return false;
        }

        // -------------------------------------------------------------------- helpers

        private void Assign(WorkerUnit worker, BuildingInstance source, BuildingInstance target, ItemId item, int amount)
        {
            worker.Task = WorkerTask.Haul(source.Id, target.Id, item, amount);
            Add(_outgoing, Key(source.Id, item), amount);
            Add(_incoming, Key(target.Id, item), amount);
        }

        /// <summary>
        /// Nearest building by Manhattan distance. Pass <see cref="BuildingKind.None"/> to
        /// search all kinds. Ties break on the lower building id to stay deterministic.
        /// </summary>
        private BuildingInstance FindNearest(WorkerUnit worker, BuildingKind kind, System.Func<BuildingInstance, bool> filter)
        {
            var buildings = _world.Buildings;
            BuildingInstance best = null;
            int bestDistance = int.MaxValue;

            for (int i = 0; i < buildings.Count; i++)
            {
                var building = buildings[i];
                if (kind != BuildingKind.None && building.Kind != kind) continue;
                if (filter != null && !filter(building)) continue;

                int distance = worker.Pos.ManhattanTo(building.CenterTile());
                if (distance > bestDistance) continue;
                if (distance == bestDistance && best != null && building.Id >= best.Id) continue;

                best = building;
                bestDistance = distance;
            }

            return best;
        }

        private static bool ConsumesItem(BuildingInstance station, ItemId item)
        {
            var recipe = station.CurrentRecipe();
            if (recipe == null) return false;
            for (int i = 0; i < recipe.Inputs.Length; i++)
            {
                if (recipe.Inputs[i].Item == item) return true;
            }
            return false;
        }

        private static int Clamp(int a, int b, int c, int d)
        {
            int min = a;
            if (b < min) min = b;
            if (c < min) min = c;
            if (d < min) min = d;
            return min < 0 ? 0 : min;
        }
    }
}
