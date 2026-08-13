namespace Worker.Core
{
    /// <summary>
    /// Persisted in save files; never renumber existing values.
    /// </summary>
    public enum BuildingKind : byte
    {
        None = 0,

        /// <summary>Raw material arrives here from outside the factory.</summary>
        Intake = 1,

        /// <summary>General purpose shelving. Buffers anything.</summary>
        Storage = 2,

        Sawbench = 3,
        Lathe = 4,
        AssemblyBench = 5,

        /// <summary>Finished goods leave here to fulfil orders.</summary>
        Shipping = 6,

        Conveyor = 7,

        /// <summary>Workers recover stamina and morale here.</summary>
        BreakRoom = 8,

        Wall = 9
    }
}
