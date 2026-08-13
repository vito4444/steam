namespace Worker.Core
{
    /// <summary>
    /// Simulation tuning constants. Everything is integer and per-tick so the sim
    /// stays deterministic across machines and replays.
    /// </summary>
    public static class SimConfig
    {
        /// <summary>Simulation ticks per real second at 1x speed.</summary>
        public const int TicksPerSecond = 20;

        /// <summary>Ticks in one in-game working day.</summary>
        public const int TicksPerDay = TicksPerSecond * 60 * 4;

        /// <summary>Ticks between task-scheduling passes. Not every tick, to keep CPU cost flat.</summary>
        public const int SchedulerIntervalTicks = 5;

        /// <summary>Base ticks a worker needs to cross one tile at rate 100.</summary>
        public const int TicksPerTileAtBaseRate = 6;

        /// <summary>Handling time for picking up or dropping off a load.</summary>
        public const int HandlingTicks = 8;

        /// <summary>How many items a worker can carry in one trip.</summary>
        public const int CarryCapacity = 8;

        /// <summary>Stamina drained per tick of active labour.</summary>
        public const int StaminaDrainPerWorkTick = 3;

        /// <summary>Stamina drained per tick while walking.</summary>
        public const int StaminaDrainPerMoveTick = 1;

        /// <summary>Stamina recovered per tick in the break room.</summary>
        public const int StaminaRecoverPerRestTick = 18;

        /// <summary>Below this stamina a worker stops accepting production tasks and seeks rest.</summary>
        public const int StaminaSeekRestThreshold = 2000;

        /// <summary>A resting worker returns to duty once stamina reaches this.</summary>
        public const int StaminaResumeThreshold = 8500;

        /// <summary>Morale lost per tick when forced to work below the rest threshold.</summary>
        public const int MoraleLossPerExhaustedTick = 2;

        /// <summary>Morale gained per tick while resting.</summary>
        public const int MoraleGainPerRestTick = 3;

        /// <summary>
        /// Default factory dimensions. Deliberately small: the whole shop floor has to
        /// fit one screen at a tile size where a 2x2 bench still reads clearly, and a
        /// tight plot is what forces the layout decisions this genre is about.
        /// </summary>
        public const int DefaultMapWidth = 26;
        public const int DefaultMapHeight = 16;
    }
}
