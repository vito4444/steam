using System.Collections.Generic;

namespace Worker.Core
{
    /// <summary>
    /// Deterministic world setups. The starter factory is shared by the game's
    /// new-game flow, the automated self-test and the unit tests, so all three
    /// exercise exactly the same layout.
    /// </summary>
    public static class Scenarios
    {
        public static readonly GridPos FloorOrigin = new GridPos(4, 3);
        public const int FloorWidth = 40;
        public const int FloorHeight = 26;

        private static readonly string[] StarterNames =
        {
            "Mei", "Jonas", "Priya", "Otto", "Ana", "Kwame", "Iris", "Tomas"
        };

        /// <summary>
        /// A hand-authored chair factory: intake, two sawbenches, a lathe, an assembly
        /// bench, storage, shipping and a break room, wired left to right.
        /// </summary>
        public static SimWorld StarterFactory(uint seed = 1, int workerCount = 3)
        {
            var world = new SimWorld(seed);
            world.Map.FillTerrain(FloorOrigin, FloorWidth, FloorHeight, TerrainKind.Floor);

            var intake = world.PlaceBuildingFree(BuildingKind.Intake, new GridPos(6, 12));

            var sawLogs = world.PlaceBuildingFree(BuildingKind.Sawbench, new GridPos(12, 16));
            sawLogs.ActiveRecipe = RecipeId.SawLogs;

            var sawSeats = world.PlaceBuildingFree(BuildingKind.Sawbench, new GridPos(12, 8));
            sawSeats.ActiveRecipe = RecipeId.CutSeat;

            var lathe = world.PlaceBuildingFree(BuildingKind.Lathe, new GridPos(19, 16));
            lathe.ActiveRecipe = RecipeId.TurnLegs;

            var assembly = world.PlaceBuildingFree(BuildingKind.AssemblyBench, new GridPos(26, 12));
            assembly.ActiveRecipe = RecipeId.AssembleWoodChair;

            world.PlaceBuildingFree(BuildingKind.Storage, new GridPos(19, 12));
            world.PlaceBuildingFree(BuildingKind.Storage, new GridPos(20, 12));
            world.PlaceBuildingFree(BuildingKind.Storage, new GridPos(19, 11));
            world.PlaceBuildingFree(BuildingKind.Storage, new GridPos(20, 11));

            world.PlaceBuildingFree(BuildingKind.Shipping, new GridPos(34, 12));
            world.PlaceBuildingFree(BuildingKind.BreakRoom, new GridPos(8, 22));

            // Logs arrive every quarter day. Enough to keep one sawbench busy, not enough
            // to keep four workers busy: expanding supply is the player's first decision.
            world.AddSupplyContract(ItemId.Log, 12, SimConfig.TicksPerDay / 4, 100, 5);

            var spawn = new GridPos(10, 12);
            for (int i = 0; i < workerCount; i++)
            {
                string name = StarterNames[i % StarterNames.Length];
                world.HireWorker(name, new GridPos(spawn.X + (i % 3), spawn.Y - (i / 3)), 100, 8000);
            }

            return world;
        }

        /// <summary>The opening contract: 10 wooden chairs. Used as the vertical slice win condition.</summary>
        public static Order AddOpeningOrder(SimWorld world)
        {
            var order = world.AddOrder(ItemId.WoodChair, 10, 12000, 3000, SimConfig.TicksPerDay * 4);
            world.AcceptOrder(order.Id);
            return order;
        }

        /// <summary>Minimal two-building setup used by focused unit tests.</summary>
        public static SimWorld MinimalHaulTest(uint seed = 1)
        {
            var world = new SimWorld(seed, 24, 16);
            world.Map.FillTerrain(new GridPos(1, 1), 22, 14, TerrainKind.Floor);

            world.PlaceBuildingFree(BuildingKind.Intake, new GridPos(2, 6));
            var bench = world.PlaceBuildingFree(BuildingKind.Sawbench, new GridPos(12, 6));
            bench.ActiveRecipe = RecipeId.SawLogs;
            world.PlaceBuildingFree(BuildingKind.Storage, new GridPos(18, 6));

            world.HireWorker("Test", new GridPos(6, 6));
            return world;
        }

        public static IReadOnlyList<string> WorkerNamePool => StarterNames;
    }
}
