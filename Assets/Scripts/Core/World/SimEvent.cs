namespace Worker.Core
{
    public enum SimEventKind : byte
    {
        BuildingPlaced = 0,
        BuildingRemoved = 1,
        WorkerHired = 2,
        WorkerLaidOff = 3,
        CraftCompleted = 4,
        OrderAccepted = 5,
        OrderFulfilled = 6,
        OrderFailed = 7,
        WagesPaid = 8,
        WorkerExhausted = 9,
        TaskAbandoned = 10
    }

    /// <summary>
    /// Flat event record. Deliberately allocation-light and free of object references so
    /// the self-test can dump an entire run to a log file and diff two runs line by line.
    /// </summary>
    public readonly struct SimEvent
    {
        public readonly int Tick;
        public readonly SimEventKind Kind;
        public readonly int A;
        public readonly int B;
        public readonly string Text;

        private SimEvent(int tick, SimEventKind kind, int a, int b, string text)
        {
            Tick = tick;
            Kind = kind;
            A = a;
            B = b;
            Text = text;
        }

        public static SimEvent BuildingPlaced(int tick, int buildingId, BuildingKind kind, GridPos pos)
            => new SimEvent(tick, SimEventKind.BuildingPlaced, buildingId, (int)kind, pos.ToString());

        public static SimEvent BuildingRemoved(int tick, int buildingId)
            => new SimEvent(tick, SimEventKind.BuildingRemoved, buildingId, 0, null);

        public static SimEvent WorkerHired(int tick, int workerId, string name)
            => new SimEvent(tick, SimEventKind.WorkerHired, workerId, 0, name);

        public static SimEvent WorkerLaidOff(int tick, int workerId, string name)
            => new SimEvent(tick, SimEventKind.WorkerLaidOff, workerId, 0, name);

        public static SimEvent CraftCompleted(int tick, int buildingId, RecipeId recipe)
            => new SimEvent(tick, SimEventKind.CraftCompleted, buildingId, (int)recipe, null);

        public static SimEvent OrderAccepted(int tick, int orderId)
            => new SimEvent(tick, SimEventKind.OrderAccepted, orderId, 0, null);

        public static SimEvent OrderFulfilled(int tick, int orderId, int payout)
            => new SimEvent(tick, SimEventKind.OrderFulfilled, orderId, payout, null);

        public static SimEvent OrderFailed(int tick, int orderId, int penalty)
            => new SimEvent(tick, SimEventKind.OrderFailed, orderId, penalty, null);

        public static SimEvent WagesPaid(int tick, int total)
            => new SimEvent(tick, SimEventKind.WagesPaid, total, 0, null);

        public static SimEvent WorkerExhausted(int tick, int workerId)
            => new SimEvent(tick, SimEventKind.WorkerExhausted, workerId, 0, null);

        public static SimEvent TaskAbandoned(int tick, int workerId, TaskKind kind)
            => new SimEvent(tick, SimEventKind.TaskAbandoned, workerId, (int)kind, null);

        /// <summary>Stable single-line form used by the self-test event log.</summary>
        public override string ToString()
            => Tick + "\t" + Kind + "\t" + A + "\t" + B + (Text == null ? "" : "\t" + Text);
    }
}
