namespace Worker.Core
{
    public enum TaskKind : byte
    {
        None = 0,

        /// <summary>Fetch items from one building and deliver them to another.</summary>
        Haul = 1,

        /// <summary>Stand next to a station and run its active recipe.</summary>
        Operate = 2,

        /// <summary>Walk to the break room and recover.</summary>
        Rest = 3
    }

    public enum TaskPhase : byte
    {
        MovingToSource = 0,
        PickingUp = 1,
        MovingToTarget = 2,
        Delivering = 3,
        Working = 4,
        Resting = 5,
        Done = 6
    }

    /// <summary>
    /// A unit of assigned work. Tasks are re-validated on arrival, so a task that
    /// became impossible while the worker was walking is abandoned rather than
    /// corrupting inventories.
    /// </summary>
    public sealed class WorkerTask
    {
        public TaskKind Kind;
        public TaskPhase Phase;

        public int SourceBuildingId;
        public int TargetBuildingId;

        public ItemId Item;
        public int Count;

        /// <summary>Ticks spent on the current phase, used for pickup/delivery handling time.</summary>
        public int PhaseTicks;

        public static WorkerTask Haul(int sourceId, int targetId, ItemId item, int count)
            => new WorkerTask
            {
                Kind = TaskKind.Haul,
                Phase = TaskPhase.MovingToSource,
                SourceBuildingId = sourceId,
                TargetBuildingId = targetId,
                Item = item,
                Count = count
            };

        public static WorkerTask Operate(int stationId)
            => new WorkerTask
            {
                Kind = TaskKind.Operate,
                Phase = TaskPhase.MovingToTarget,
                TargetBuildingId = stationId
            };

        public static WorkerTask Rest(int breakRoomId)
            => new WorkerTask
            {
                Kind = TaskKind.Rest,
                Phase = TaskPhase.MovingToTarget,
                TargetBuildingId = breakRoomId
            };

        /// <summary>The building the worker is currently heading towards.</summary>
        public int DestinationBuildingId
            => Phase == TaskPhase.MovingToSource || Phase == TaskPhase.PickingUp
                ? SourceBuildingId
                : TargetBuildingId;

        public override string ToString()
            => Kind + "/" + Phase + " src=" + SourceBuildingId + " dst=" + TargetBuildingId
               + (Item == ItemId.None ? "" : " " + Count + "x" + Item);
    }
}
