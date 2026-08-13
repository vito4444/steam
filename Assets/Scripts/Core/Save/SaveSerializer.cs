using System;
using System.IO;
using System.Text;

namespace Worker.Core
{
    /// <summary>
    /// Binary save format for <see cref="SimWorld"/>.
    ///
    /// The correctness bar is exact: a world written and read back must produce the same
    /// <see cref="StateHash"/>, and must continue to simulate identically afterwards.
    /// Anything less means a loaded game quietly diverges from the one that was saved,
    /// which is the kind of bug players report as "my factory broke overnight".
    ///
    /// The format is deliberately dumb and explicit. Reflection or a general-purpose
    /// serializer would make it easy to add a field and forget it is now part of the
    /// save contract; writing every field by hand makes that omission visible in review.
    /// </summary>
    public static class SaveSerializer
    {
        private const uint Magic = 0x574F524Bu; // "WORK"
        private const int Version = 1;

        public static void Save(SimWorld world, Stream stream)
        {
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

            writer.Write(Magic);
            writer.Write(Version);

            writer.Write(world.Seed);
            writer.Write(world.Tick);
            writer.Write(world.TotalUnitsShipped);
            writer.Write(world.TotalCraftsCompleted);

            var randomState = world.Random.SaveState();
            writer.Write(randomState.Length);
            for (int i = 0; i < randomState.Length; i++) writer.Write(randomState[i]);

            writer.Write(world.PeekNextBuildingId());
            writer.Write(world.PeekNextWorkerId());
            writer.Write(world.PeekNextOrderId());
            writer.Write(world.PeekNextContractId());

            WriteMap(writer, world.Map);
            WriteLedger(writer, world.Ledger);

            writer.Write(world.Buildings.Count);
            for (int i = 0; i < world.Buildings.Count; i++) WriteBuilding(writer, world.Buildings[i]);

            writer.Write(world.Workers.Count);
            for (int i = 0; i < world.Workers.Count; i++) WriteWorker(writer, world.Workers[i]);

            writer.Write(world.Orders.Count);
            for (int i = 0; i < world.Orders.Count; i++) WriteOrder(writer, world.Orders[i]);

            writer.Write(world.Contracts.Count);
            for (int i = 0; i < world.Contracts.Count; i++) WriteContract(writer, world.Contracts[i]);
        }

        public static SimWorld Load(Stream stream)
        {
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

            uint magic = reader.ReadUInt32();
            if (magic != Magic) throw new InvalidDataException("Not a worker save file");

            int version = reader.ReadInt32();
            if (version != Version)
            {
                throw new InvalidDataException("Save version " + version + " is not supported (expected " + Version + ")");
            }

            uint seed = reader.ReadUInt32();
            int tick = reader.ReadInt32();
            int shipped = reader.ReadInt32();
            int crafts = reader.ReadInt32();

            int randomStateLength = reader.ReadInt32();
            var randomState = new uint[randomStateLength];
            for (int i = 0; i < randomStateLength; i++) randomState[i] = reader.ReadUInt32();

            int nextBuildingId = reader.ReadInt32();
            int nextWorkerId = reader.ReadInt32();
            int nextOrderId = reader.ReadInt32();
            int nextContractId = reader.ReadInt32();

            int width = reader.ReadInt32();
            int height = reader.ReadInt32();

            var world = new SimWorld(seed, width, height, openingBalance: 0);
            world.Random.LoadState(randomState);
            world.RestoreTick(tick);
            world.RestoreTotals(shipped, crafts);

            ReadMapTerrain(reader, world.Map, width, height);
            ReadLedger(reader, world.Ledger);

            int buildingCount = reader.ReadInt32();
            for (int i = 0; i < buildingCount; i++) world.RegisterLoadedBuilding(ReadBuilding(reader));

            int workerCount = reader.ReadInt32();
            for (int i = 0; i < workerCount; i++) world.RegisterLoadedWorker(ReadWorker(reader));

            int orderCount = reader.ReadInt32();
            for (int i = 0; i < orderCount; i++) world.RegisterLoadedOrder(ReadOrder(reader));

            int contractCount = reader.ReadInt32();
            for (int i = 0; i < contractCount; i++) world.RegisterLoadedContract(ReadContract(reader));

            // Counters are restored last so nothing registered above can bump them.
            world.RestoreCounters(nextBuildingId, nextWorkerId, nextOrderId, nextContractId);

            return world;
        }

        public static byte[] SaveToBytes(SimWorld world)
        {
            using var memory = new MemoryStream();
            Save(world, memory);
            return memory.ToArray();
        }

        public static SimWorld LoadFromBytes(byte[] bytes)
        {
            using var memory = new MemoryStream(bytes);
            return Load(memory);
        }

        public static void SaveToFile(SimWorld world, string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            // Write to a temporary file and move it into place, so a crash mid-save
            // cannot leave the player with a truncated file where their game used to be.
            string temporary = path + ".tmp";
            using (var file = File.Create(temporary)) Save(world, file);

            if (File.Exists(path)) File.Delete(path);
            File.Move(temporary, path);
        }

        public static SimWorld LoadFromFile(string path)
        {
            using var file = File.OpenRead(path);
            return Load(file);
        }

        // ------------------------------------------------------------------- map

        private static void WriteMap(BinaryWriter writer, TileMap map)
        {
            writer.Write(map.Width);
            writer.Write(map.Height);

            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    writer.Write((byte)map.GetTerrain(new GridPos(x, y)));
                }
            }
        }

        private static void ReadMapTerrain(BinaryReader reader, TileMap map, int width, int height)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    map.SetTerrain(new GridPos(x, y), (TerrainKind)reader.ReadByte());
                }
            }
        }

        // ---------------------------------------------------------------- ledger

        private static void WriteLedger(BinaryWriter writer, Ledger ledger)
        {
            writer.Write(ledger.Balance);
            writer.Write(ledger.History.Count);
            for (int i = 0; i < ledger.History.Count; i++)
            {
                var entry = ledger.History[i];
                writer.Write(entry.Tick);
                writer.Write((byte)entry.Kind);
                writer.Write(entry.Amount);
            }
        }

        private static void ReadLedger(BinaryReader reader, Ledger ledger)
        {
            int balance = reader.ReadInt32();
            int count = reader.ReadInt32();

            for (int i = 0; i < count; i++)
            {
                int tick = reader.ReadInt32();
                var kind = (LedgerEntryKind)reader.ReadByte();
                int amount = reader.ReadInt32();
                ledger.AppendHistoryRaw(tick, kind, amount);
            }

            // Set the balance after replaying history so the entries do not move it.
            ledger.SetBalanceRaw(balance);
        }

        // -------------------------------------------------------------- buildings

        private static void WriteBuilding(BinaryWriter writer, BuildingInstance building)
        {
            writer.Write(building.Id);
            writer.Write((byte)building.Kind);
            writer.Write(building.Origin.X);
            writer.Write(building.Origin.Y);
            writer.Write((byte)building.Facing);
            writer.Write((ushort)building.ActiveRecipe);
            writer.Write(building.WorkProgress);
            writer.Write(building.OperatorWorkerId);

            WriteInventory(writer, building.Input);
            WriteInventory(writer, building.Output);

            writer.Write(building.Conveyor != null);
            if (building.Conveyor == null) return;

            for (int slot = 0; slot < ConveyorState.Capacity; slot++)
            {
                writer.Write((ushort)building.Conveyor.ItemAt(slot));
                writer.Write(building.Conveyor.ProgressAt(slot));
            }
        }

        private static BuildingInstance ReadBuilding(BinaryReader reader)
        {
            int id = reader.ReadInt32();
            var kind = (BuildingKind)reader.ReadByte();
            int x = reader.ReadInt32();
            int y = reader.ReadInt32();
            var facing = (Direction)reader.ReadByte();

            var building = new BuildingInstance(id, kind, new GridPos(x, y), facing)
            {
                ActiveRecipe = (RecipeId)reader.ReadUInt16(),
                WorkProgress = reader.ReadInt32(),
                OperatorWorkerId = reader.ReadInt32()
            };

            ReadInventory(reader, building.Input);
            ReadInventory(reader, building.Output);

            bool hasConveyor = reader.ReadBoolean();
            if (!hasConveyor) return building;

            for (int slot = 0; slot < ConveyorState.Capacity; slot++)
            {
                var item = (ItemId)reader.ReadUInt16();
                int progress = reader.ReadInt32();
                building.Conveyor?.SetSlotRaw(slot, item, progress);
            }

            return building;
        }

        private static void WriteInventory(BinaryWriter writer, Inventory inventory)
        {
            writer.Write(inventory.SlotCount);
            for (int i = 0; i < inventory.SlotCount; i++)
            {
                writer.Write((ushort)inventory[i].Item);
                writer.Write(inventory[i].Count);
            }
        }

        private static void ReadInventory(BinaryReader reader, Inventory inventory)
        {
            int slots = reader.ReadInt32();
            for (int i = 0; i < slots; i++)
            {
                var item = (ItemId)reader.ReadUInt16();
                int count = reader.ReadInt32();
                if (i < inventory.SlotCount) inventory.SetSlotRaw(i, new ItemStack(item, count));
            }
        }

        // ---------------------------------------------------------------- workers

        private static void WriteWorker(BinaryWriter writer, WorkerUnit worker)
        {
            writer.Write(worker.Id);
            writer.Write(worker.Name ?? string.Empty);
            writer.Write(worker.Pos.X);
            writer.Write(worker.Pos.Y);
            writer.Write(worker.Stamina);
            writer.Write(worker.Morale);
            writer.Write(worker.SpeedRating);
            writer.Write(worker.DailyWage);
            writer.Write(worker.LifetimeWorkTicks);
            writer.Write((ushort)worker.Carried.Item);
            writer.Write(worker.Carried.Count);
            writer.Write(worker.MoveCooldown);
            writer.Write(worker.MoveDuration);
            writer.Write(worker.PathCursor);

            writer.Write(worker.Path.Count);
            for (int i = 0; i < worker.Path.Count; i++)
            {
                writer.Write(worker.Path[i].X);
                writer.Write(worker.Path[i].Y);
            }

            writer.Write(worker.Task != null);
            if (worker.Task == null) return;

            writer.Write((byte)worker.Task.Kind);
            writer.Write((byte)worker.Task.Phase);
            writer.Write(worker.Task.SourceBuildingId);
            writer.Write(worker.Task.TargetBuildingId);
            writer.Write((ushort)worker.Task.Item);
            writer.Write(worker.Task.Count);
            writer.Write(worker.Task.PhaseTicks);
        }

        private static WorkerUnit ReadWorker(BinaryReader reader)
        {
            int id = reader.ReadInt32();
            string name = reader.ReadString();
            int x = reader.ReadInt32();
            int y = reader.ReadInt32();

            var worker = new WorkerUnit(id, name, new GridPos(x, y), 100, 0)
            {
                Stamina = reader.ReadInt32(),
                Morale = reader.ReadInt32(),
                SpeedRating = reader.ReadInt32(),
                DailyWage = reader.ReadInt32(),
                LifetimeWorkTicks = reader.ReadInt32()
            };

            var carriedItem = (ItemId)reader.ReadUInt16();
            int carriedCount = reader.ReadInt32();
            worker.Carried = new ItemStack(carriedItem, carriedCount);

            worker.MoveCooldown = reader.ReadInt32();
            worker.MoveDuration = reader.ReadInt32();
            worker.PathCursor = reader.ReadInt32();

            int pathLength = reader.ReadInt32();
            for (int i = 0; i < pathLength; i++)
            {
                int px = reader.ReadInt32();
                int py = reader.ReadInt32();
                worker.Path.Add(new GridPos(px, py));
            }

            bool hasTask = reader.ReadBoolean();
            if (!hasTask) return worker;

            worker.Task = new WorkerTask
            {
                Kind = (TaskKind)reader.ReadByte(),
                Phase = (TaskPhase)reader.ReadByte(),
                SourceBuildingId = reader.ReadInt32(),
                TargetBuildingId = reader.ReadInt32(),
                Item = (ItemId)reader.ReadUInt16(),
                Count = reader.ReadInt32(),
                PhaseTicks = reader.ReadInt32()
            };

            return worker;
        }

        // ----------------------------------------------------------------- orders

        private static void WriteOrder(BinaryWriter writer, Order order)
        {
            writer.Write(order.Id);
            writer.Write((ushort)order.Item);
            writer.Write(order.Quantity);
            writer.Write(order.Payout);
            writer.Write(order.Penalty);
            writer.Write(order.DeadlineTick);
            writer.Write((byte)order.State);
            writer.Write(order.Delivered);
        }

        private static Order ReadOrder(BinaryReader reader)
        {
            int id = reader.ReadInt32();
            var item = (ItemId)reader.ReadUInt16();
            int quantity = reader.ReadInt32();
            int payout = reader.ReadInt32();
            int penalty = reader.ReadInt32();

            return new Order(id, item, quantity, payout, penalty)
            {
                DeadlineTick = reader.ReadInt32(),
                State = (OrderState)reader.ReadByte(),
                Delivered = reader.ReadInt32()
            };
        }

        // -------------------------------------------------------------- contracts

        private static void WriteContract(BinaryWriter writer, SupplyContract contract)
        {
            writer.Write(contract.Id);
            writer.Write((ushort)contract.Item);
            writer.Write(contract.QuantityPerDelivery);
            writer.Write(contract.IntervalTicks);
            writer.Write(contract.UnitCostCents);
            writer.Write(contract.NextDeliveryTick);
            writer.Write(contract.Active);
            writer.Write(contract.MissedDeliveries);
        }

        private static SupplyContract ReadContract(BinaryReader reader)
        {
            int id = reader.ReadInt32();
            var item = (ItemId)reader.ReadUInt16();
            int quantity = reader.ReadInt32();
            int interval = reader.ReadInt32();
            int unitCost = reader.ReadInt32();
            int nextDelivery = reader.ReadInt32();

            var contract = new SupplyContract(id, item, quantity, interval, unitCost, nextDelivery)
            {
                Active = reader.ReadBoolean(),
                MissedDeliveries = reader.ReadInt32()
            };

            return contract;
        }
    }
}
