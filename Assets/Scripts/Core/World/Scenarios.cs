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
        public static readonly GridPos FloorOrigin = new GridPos(1, 1);
        public const int FloorWidth = 24;
        public const int FloorHeight = 14;

        private static readonly string[] StarterNames =
        {
            "Mei", "Jonas", "Priya", "Otto", "Ana", "Kwame", "Iris", "Tomas"
        };

        /// <summary>
        /// The opening shop floor: intake on the left, two sawbenches and a lathe in the
        /// middle, assembly and shipping on the right, with a storage bank in the centre
        /// that every station has to reach through.
        ///
        /// The layout is intentionally cramped. Walking distance is the main cost in the
        /// early game, so the first thing a player feels is that their people are wasting
        /// half their day carrying things across the room.
        /// </summary>
        public static SimWorld StarterFactory(uint seed = 1, int workerCount = 3)
        {
            var world = BuildShell(seed);

            world.PlaceBuildingFree(BuildingKind.Intake, new GridPos(2, 7));

            var sawLogs = world.PlaceBuildingFree(BuildingKind.Sawbench, new GridPos(6, 7));
            sawLogs.ActiveRecipe = RecipeId.SawLogs;

            var sawSeats = world.PlaceBuildingFree(BuildingKind.Sawbench, new GridPos(6, 11));
            sawSeats.ActiveRecipe = RecipeId.CutSeat;

            var lathe = world.PlaceBuildingFree(BuildingKind.Lathe, new GridPos(6, 3));
            lathe.ActiveRecipe = RecipeId.TurnLegs;

            StorageBank(world, 11, 6, 2, 3);

            var assembly = world.PlaceBuildingFree(BuildingKind.AssemblyBench, new GridPos(15, 9));
            assembly.ActiveRecipe = RecipeId.AssembleWoodChair;

            var assemblyTwo = world.PlaceBuildingFree(BuildingKind.AssemblyBench, new GridPos(15, 4));
            assemblyTwo.ActiveRecipe = RecipeId.AssembleWoodChair;

            world.PlaceBuildingFree(BuildingKind.Shipping, new GridPos(20, 7));
            StorageBank(world, 21, 11, 2, 2);

            world.PlaceBuildingFree(BuildingKind.BreakRoom, new GridPos(2, 12));

            // One short belt from intake to the first bench, as a worked example of what
            // automation buys. Everything else is carried by hand until the player builds
            // more, which is the whole point of the opening hours.
            Belt(world, 4, 7, Direction.East);
            Belt(world, 5, 7, Direction.East);

            // Logs arrive every quarter day. Enough to keep one sawbench busy, not enough
            // to keep four workers busy: expanding supply is the player's first decision.
            world.AddSupplyContract(ItemId.Log, 12, SimConfig.TicksPerDay / 4, 100, 5);

            SpawnWorkers(world, workerCount, new GridPos(9, 9));
            return world;
        }

        /// <summary>
        /// The same factory after the player has built out its logistics: belts carry
        /// material between every stage and staff only handle the last leg. Used for
        /// long-run self-tests and for capture shots that show what the mid game looks
        /// like rather than the empty opening.
        /// </summary>
        public static SimWorld AutomatedFactory(uint seed = 1, int workerCount = 6)
        {
            var world = BuildShell(seed);

            world.PlaceBuildingFree(BuildingKind.Intake, new GridPos(2, 7));

            var sawLogs = world.PlaceBuildingFree(BuildingKind.Sawbench, new GridPos(6, 7));
            sawLogs.ActiveRecipe = RecipeId.SawLogs;

            var sawSeats = world.PlaceBuildingFree(BuildingKind.Sawbench, new GridPos(6, 11));
            sawSeats.ActiveRecipe = RecipeId.CutSeat;

            var lathe = world.PlaceBuildingFree(BuildingKind.Lathe, new GridPos(6, 3));
            lathe.ActiveRecipe = RecipeId.TurnLegs;

            StorageBank(world, 11, 6, 2, 3);

            var assembly = world.PlaceBuildingFree(BuildingKind.AssemblyBench, new GridPos(15, 9));
            assembly.ActiveRecipe = RecipeId.AssembleWoodChair;

            var assemblyTwo = world.PlaceBuildingFree(BuildingKind.AssemblyBench, new GridPos(15, 4));
            assemblyTwo.ActiveRecipe = RecipeId.AssembleWoodChair;

            world.PlaceBuildingFree(BuildingKind.Shipping, new GridPos(20, 7));
            StorageBank(world, 21, 11, 3, 2);
            StorageBank(world, 21, 3, 3, 2);
            StorageBank(world, 2, 5, 2, 2);

            world.PlaceBuildingFree(BuildingKind.BreakRoom, new GridPos(2, 12));

            // Intake feeds the log saw.
            Belt(world, 4, 7, Direction.East);
            Belt(world, 5, 7, Direction.East);

            // Log saw runs straight into the central storage bank.
            for (int x = 8; x <= 10; x++) Belt(world, x, 7, Direction.East);

            // Seat saw comes down the left and turns into storage.
            for (int x = 8; x <= 10; x++) Belt(world, x, 11, Direction.East);
            Belt(world, 11, 11, Direction.South);
            Belt(world, 11, 10, Direction.South);
            Belt(world, 11, 9, Direction.South);

            // Lathe comes up the left and turns into storage.
            for (int x = 8; x <= 10; x++) Belt(world, x, 3, Direction.East);
            Belt(world, 11, 3, Direction.North);
            Belt(world, 11, 4, Direction.North);
            Belt(world, 11, 5, Direction.North);

            // Upper assembly out to dispatch.
            Belt(world, 17, 9, Direction.East);
            Belt(world, 18, 9, Direction.East);
            Belt(world, 19, 9, Direction.South);
            Belt(world, 19, 8, Direction.East);

            // Lower assembly out to dispatch.
            Belt(world, 17, 4, Direction.East);
            Belt(world, 18, 4, Direction.East);
            Belt(world, 19, 4, Direction.North);
            Belt(world, 19, 5, Direction.North);
            Belt(world, 19, 6, Direction.North);
            Belt(world, 19, 7, Direction.East);

            PerimeterWalls(world);

            // A fully built line eats logs far faster than the opening contract supplies.
            world.AddSupplyContract(ItemId.Log, 24, SimConfig.TicksPerDay / 8, 100, 5);

            SpawnWorkers(world, workerCount, new GridPos(14, 12));
            return world;
        }

        private static SimWorld BuildShell(uint seed)
        {
            var world = new SimWorld(seed);
            world.Map.FillTerrain(FloorOrigin, FloorWidth, FloorHeight, TerrainKind.Floor);
            return world;
        }

        private static void StorageBank(SimWorld world, int x, int y, int width, int height)
        {
            for (int dy = 0; dy < height; dy++)
            {
                for (int dx = 0; dx < width; dx++)
                {
                    world.PlaceBuildingFree(BuildingKind.Storage, new GridPos(x + dx, y + dy));
                }
            }
        }

        private static void Belt(SimWorld world, int x, int y, Direction direction)
            => world.PlaceBuildingFree(BuildingKind.Conveyor, new GridPos(x, y), direction);

        /// <summary>
        /// Walls around the shop floor, with gaps left at the loading and dispatch ends.
        ///
        /// This is not decoration. An unbounded floor reads as a diagram on a desk; a
        /// walled one reads as a room, and it is the cheapest way to make an isometric
        /// view feel like a built place rather than objects floating on a plane.
        /// </summary>
        private static void PerimeterWalls(SimWorld world)
        {
            int x0 = FloorOrigin.X;
            int y0 = FloorOrigin.Y;
            int x1 = FloorOrigin.X + FloorWidth - 1;
            int y1 = FloorOrigin.Y + FloorHeight - 1;

            for (int x = x0; x <= x1; x++)
            {
                TryWall(world, x, y0);
                TryWall(world, x, y1);
            }

            for (int y = y0 + 1; y < y1; y++)
            {
                // Leave the loading bay and the dispatch door open.
                bool loadingDoor = y >= 6 && y <= 9;
                if (!loadingDoor) TryWall(world, x0, y);
                if (!loadingDoor) TryWall(world, x1, y);
            }
        }

        private static void TryWall(SimWorld world, int x, int y)
        {
            var pos = new GridPos(x, y);
            if (!world.Map.CanPlace(BuildingKind.Wall, pos, Direction.North)) return;
            world.PlaceBuildingFree(BuildingKind.Wall, pos);
        }

        private static void SpawnWorkers(SimWorld world, int count, GridPos spawn)
        {
            for (int i = 0; i < count; i++)
            {
                string name = StarterNames[i % StarterNames.Length];
                var pos = new GridPos(spawn.X + (i % 3), spawn.Y - (i / 3));
                world.HireWorker(name, pos, 100, 8000);
            }
        }

        /// <summary>The opening contract: 10 wooden chairs. Used as the vertical slice win condition.</summary>
        public static Order AddOpeningOrder(SimWorld world)
        {
            var order = world.AddOrder(ItemId.WoodChair, 10, 12000, 3000, SimConfig.TicksPerDay * 4);
            world.AcceptOrder(order.Id);
            return order;
        }

        /// <summary>
        /// A rolling series of chair contracts so a long self-test run keeps the factory
        /// under demand instead of idling once the opening order clears.
        /// </summary>
        public static void AddContractSeries(SimWorld world, int count)
        {
            for (int i = 0; i < count; i++)
            {
                int quantity = 8 + i * 4;
                var order = world.AddOrder(
                    ItemId.WoodChair,
                    quantity,
                    quantity * 1400,
                    quantity * 300,
                    SimConfig.TicksPerDay * (3 + i * 2));
                world.AcceptOrder(order.Id);
            }
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
