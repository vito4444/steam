using System.Collections.Generic;

namespace Worker.Core
{
    /// <summary>
    /// Advances every worker by one tick: movement, pickup, delivery, labour and rest.
    /// Tasks are re-validated whenever the worker arrives somewhere, because the world
    /// can change while they walk.
    /// </summary>
    internal sealed class WorkerSystem
    {
        private readonly SimWorld _world;
        private readonly List<GridPos> _pathScratch = new List<GridPos>();

        public WorkerSystem(SimWorld world)
        {
            _world = world;
        }

        public void Update()
        {
            var workers = _world.Workers;
            for (int i = 0; i < workers.Count; i++)
            {
                UpdateWorker(workers[i]);
            }
        }

        private void UpdateWorker(WorkerUnit worker)
        {
            if (worker.Task == null)
            {
                // Idle workers recover a little; standing around is not as good as a break.
                if (worker.Stamina < WorkerUnit.MaxStamina) worker.AdjustStamina(1);
                return;
            }

            switch (worker.Task.Kind)
            {
                case TaskKind.Haul:
                    UpdateHaul(worker);
                    break;
                case TaskKind.Operate:
                    UpdateOperate(worker);
                    break;
                case TaskKind.Rest:
                    UpdateRest(worker);
                    break;
                default:
                    worker.AbandonTask();
                    break;
            }
        }

        // ---------------------------------------------------------------- haul

        private void UpdateHaul(WorkerUnit worker)
        {
            var task = worker.Task;

            switch (task.Phase)
            {
                case TaskPhase.MovingToSource:
                {
                    var source = _world.GetBuilding(task.SourceBuildingId);
                    if (source == null) { Abandon(worker); return; }

                    if (!MoveTowards(worker, source)) return;

                    task.Phase = TaskPhase.PickingUp;
                    task.PhaseTicks = 0;
                    break;
                }

                case TaskPhase.PickingUp:
                {
                    var source = _world.GetBuilding(task.SourceBuildingId);
                    if (source == null) { Abandon(worker); return; }

                    task.PhaseTicks++;
                    if (task.PhaseTicks < SimConfig.HandlingTicks) return;

                    var container = SourceContainer(source);
                    int available = container.CountOf(task.Item);
                    if (available <= 0)
                    {
                        // Someone beat us to it. Drop the task; the scheduler will find new work.
                        Abandon(worker);
                        return;
                    }

                    int take = available < task.Count ? available : task.Count;
                    container.TryRemove(task.Item, take);
                    worker.Carried = new ItemStack(task.Item, take);

                    task.Count = take;
                    task.Phase = TaskPhase.MovingToTarget;
                    task.PhaseTicks = 0;
                    worker.ClearPath();
                    break;
                }

                case TaskPhase.MovingToTarget:
                {
                    var target = _world.GetBuilding(task.TargetBuildingId);
                    if (target == null) { DropCarriedAndAbandon(worker); return; }

                    if (!MoveTowards(worker, target)) return;

                    task.Phase = TaskPhase.Delivering;
                    task.PhaseTicks = 0;
                    break;
                }

                case TaskPhase.Delivering:
                {
                    var target = _world.GetBuilding(task.TargetBuildingId);
                    if (target == null) { DropCarriedAndAbandon(worker); return; }

                    task.PhaseTicks++;
                    if (task.PhaseTicks < SimConfig.HandlingTicks) return;

                    var container = TargetContainer(target);
                    int stored = container.TryAdd(worker.Carried);
                    int leftover = worker.Carried.Count - stored;
                    worker.Carried = leftover > 0 ? new ItemStack(worker.Carried.Item, leftover) : ItemStack.Empty;

                    // Whatever could not fit stays in hand; the scheduler will route it to storage.
                    worker.Task = null;
                    worker.ClearPath();
                    break;
                }
            }
        }

        // ------------------------------------------------------------- operate

        private void UpdateOperate(WorkerUnit worker)
        {
            var task = worker.Task;
            var station = _world.GetBuilding(task.TargetBuildingId);
            if (station == null || !station.Def.IsStation) { Abandon(worker); return; }

            if (task.Phase == TaskPhase.MovingToTarget)
            {
                if (!MoveTowards(worker, station)) return;
                task.Phase = TaskPhase.Working;
                station.OperatorWorkerId = worker.Id;
            }

            if (task.Phase != TaskPhase.Working) return;

            // Another worker may have claimed this station while we walked over.
            if (station.OperatorWorkerId != 0 && station.OperatorWorkerId != worker.Id)
            {
                Abandon(worker);
                return;
            }
            station.OperatorWorkerId = worker.Id;

            var recipe = station.CurrentRecipe();
            if (recipe == null) { ReleaseStation(worker, station); return; }

            if (station.WorkProgress == 0)
            {
                if (!station.CanStartWork()) { ReleaseStation(worker, station); return; }
                station.Input.RemoveAll(recipe.Inputs);
            }

            int rate = worker.EffectiveRate();
            station.WorkProgress += rate;
            worker.LifetimeWorkTicks++;
            worker.AdjustStamina(-SimConfig.StaminaDrainPerWorkTick);

            if (worker.Stamina <= 0)
            {
                worker.AdjustMorale(-SimConfig.MoraleLossPerExhaustedTick);
            }

            int required = recipe.WorkTicks * 100;
            if (station.WorkProgress < required) return;

            station.WorkProgress = 0;
            station.Output.TryAdd(recipe.Output);
            _world.NotifyCraftCompleted();
            _world.Raise(SimEvent.CraftCompleted(_world.Tick, station.Id, recipe.Id));

            // Keep going only if the station can immediately start another unit.
            if (!station.CanStartWork() || worker.Stamina <= SimConfig.StaminaSeekRestThreshold)
            {
                ReleaseStation(worker, station);
            }
        }

        private void ReleaseStation(WorkerUnit worker, BuildingInstance station)
        {
            if (station.OperatorWorkerId == worker.Id) station.OperatorWorkerId = 0;
            worker.Task = null;
            worker.ClearPath();
        }

        // ---------------------------------------------------------------- rest

        private void UpdateRest(WorkerUnit worker)
        {
            var task = worker.Task;
            var room = _world.GetBuilding(task.TargetBuildingId);
            if (room == null) { Abandon(worker); return; }

            if (task.Phase == TaskPhase.MovingToTarget)
            {
                if (!MoveTowards(worker, room)) return;
                task.Phase = TaskPhase.Resting;
            }

            worker.AdjustStamina(SimConfig.StaminaRecoverPerRestTick);
            worker.AdjustMorale(SimConfig.MoraleGainPerRestTick);

            if (worker.Stamina >= SimConfig.StaminaResumeThreshold)
            {
                worker.Task = null;
                worker.ClearPath();
            }
        }

        // ------------------------------------------------------------ movement

        /// <summary>
        /// Walks the worker towards any tile adjacent to <paramref name="building"/>.
        /// Returns true once the worker is standing next to it. Abandons the task and
        /// returns false when no route exists.
        /// </summary>
        private bool MoveTowards(WorkerUnit worker, BuildingInstance building)
        {
            if (IsAdjacentTo(worker.Pos, building))
            {
                worker.ClearPath();
                return true;
            }

            if (!worker.HasPath)
            {
                var goals = building.AdjacentTiles();
                if (!_world.Path.TryFindPathToAny(worker.Pos, goals, _pathScratch))
                {
                    Abandon(worker);
                    return false;
                }

                worker.Path.Clear();
                worker.Path.AddRange(_pathScratch);
                worker.PathCursor = 0;
                worker.MoveDuration = TicksPerTile(worker);
                worker.MoveCooldown = worker.MoveDuration;

                if (worker.Path.Count == 0)
                {
                    return true;
                }
            }

            worker.MoveCooldown--;
            if (worker.MoveCooldown > 0) return false;

            worker.Pos = worker.Path[worker.PathCursor];
            worker.PathCursor++;
            worker.MoveDuration = TicksPerTile(worker);
            worker.MoveCooldown = worker.MoveDuration;
            worker.AdjustStamina(-SimConfig.StaminaDrainPerMoveTick);

            if (worker.PathCursor >= worker.Path.Count)
            {
                worker.ClearPath();
                return IsAdjacentTo(worker.Pos, building);
            }

            return false;
        }

        private static bool IsAdjacentTo(GridPos pos, BuildingInstance building)
        {
            var tiles = building.AdjacentTiles();
            for (int i = 0; i < tiles.Count; i++)
            {
                if (tiles[i] == pos) return true;
            }
            return false;
        }

        private static int TicksPerTile(WorkerUnit worker)
        {
            int ticks = SimConfig.TicksPerTileAtBaseRate * 100 / worker.EffectiveRate();
            return ticks < 1 ? 1 : ticks;
        }

        // ----------------------------------------------------------- containers

        /// <summary>Where a hauler takes items from: shipping bays have no output, everything else uses Output.</summary>
        internal static Inventory SourceContainer(BuildingInstance building)
            => building.Def.OutputSlots > 0 ? building.Output : building.Input;

        /// <summary>Where a hauler puts items: stations and shipping take Input, storage takes Output.</summary>
        internal static Inventory TargetContainer(BuildingInstance building)
            => building.Def.InputSlots > 0 ? building.Input : building.Output;

        private void Abandon(WorkerUnit worker)
        {
            var kind = worker.Task?.Kind ?? TaskKind.None;
            var stationId = worker.Task?.TargetBuildingId ?? 0;

            if (kind == TaskKind.Operate)
            {
                var station = _world.GetBuilding(stationId);
                if (station != null && station.OperatorWorkerId == worker.Id) station.OperatorWorkerId = 0;
            }

            worker.AbandonTask();
            _world.Raise(SimEvent.TaskAbandoned(_world.Tick, worker.Id, kind));
        }

        private void DropCarriedAndAbandon(WorkerUnit worker)
        {
            // The load stays in hand rather than vanishing; the scheduler routes it to storage.
            Abandon(worker);
        }
    }
}
