using System.IO;
using NUnit.Framework;

namespace Worker.Core.Tests
{
    /// <summary>
    /// The bar for save/load is exact: a restored world must hash identically to the one
    /// that was saved, and must keep simulating identically afterwards. A save that is
    /// merely "close enough" produces a factory that quietly diverges from the one the
    /// player left, which is the worst kind of bug to diagnose after the fact.
    /// </summary>
    [TestFixture]
    public class SaveLoadTests
    {
        [Test]
        public void RoundTripPreservesStateHash()
        {
            var original = Scenarios.AutomatedFactory(seed: 5, workerCount: 6);
            Scenarios.AddOpeningOrder(original);
            original.StepMany(3000);

            var bytes = SaveSerializer.SaveToBytes(original);
            var restored = SaveSerializer.LoadFromBytes(bytes);

            Assert.AreEqual(
                StateHash.ToHex(StateHash.Compute(original)),
                StateHash.ToHex(StateHash.Compute(restored)),
                "restored world does not match the saved one");
        }

        [Test]
        public void RestoredWorldContinuesIdentically()
        {
            var original = Scenarios.AutomatedFactory(seed: 9, workerCount: 5);
            Scenarios.AddOpeningOrder(original);
            Scenarios.AddContractSeries(original, 3);
            original.StepMany(2000);

            var restored = SaveSerializer.LoadFromBytes(SaveSerializer.SaveToBytes(original));

            // Diverging later is the failure mode a hash-at-save-time check would miss:
            // an unsaved field only matters once the simulation touches it again.
            original.StepMany(4000);
            restored.StepMany(4000);

            Assert.AreEqual(
                StateHash.ToHex(StateHash.Compute(original)),
                StateHash.ToHex(StateHash.Compute(restored)),
                "worlds diverged after 4000 further ticks");
            Assert.AreEqual(original.TotalUnitsShipped, restored.TotalUnitsShipped);
            Assert.AreEqual(original.Ledger.Balance, restored.Ledger.Balance);
        }

        [Test]
        public void RoundTripIsStable()
        {
            var world = Scenarios.StarterFactory(seed: 11, workerCount: 4);
            Scenarios.AddOpeningOrder(world);
            world.StepMany(1500);

            var first = SaveSerializer.SaveToBytes(world);
            var second = SaveSerializer.SaveToBytes(SaveSerializer.LoadFromBytes(first));

            CollectionAssert.AreEqual(first, second, "saving a loaded world produced different bytes");
        }

        [Test]
        public void BeltContentsSurviveTheRoundTrip()
        {
            var world = Scenarios.AutomatedFactory(seed: 13, workerCount: 6);
            world.StepMany(2500);

            int onBelts = 0;
            for (int i = 0; i < world.Buildings.Count; i++)
            {
                var conveyor = world.Buildings[i].Conveyor;
                if (conveyor != null) onBelts += conveyor.Count;
            }

            Assert.Greater(onBelts, 0, "no items were on belts, so this test would prove nothing");

            var restored = SaveSerializer.LoadFromBytes(SaveSerializer.SaveToBytes(world));

            int restoredOnBelts = 0;
            for (int i = 0; i < restored.Buildings.Count; i++)
            {
                var conveyor = restored.Buildings[i].Conveyor;
                if (conveyor != null) restoredOnBelts += conveyor.Count;
            }

            Assert.AreEqual(onBelts, restoredOnBelts);
        }

        [Test]
        public void InFlightWorkerTasksSurviveTheRoundTrip()
        {
            var world = Scenarios.StarterFactory(seed: 17, workerCount: 4);
            Scenarios.AddOpeningOrder(world);
            world.StepMany(1200);

            int busy = 0;
            for (int i = 0; i < world.Workers.Count; i++)
            {
                if (world.Workers[i].Task != null) busy++;
            }

            Assert.Greater(busy, 0, "nobody was mid-task, so this test would prove nothing");

            var restored = SaveSerializer.LoadFromBytes(SaveSerializer.SaveToBytes(world));

            for (int i = 0; i < world.Workers.Count; i++)
            {
                var before = world.Workers[i];
                var after = restored.Workers[i];

                Assert.AreEqual(before.Name, after.Name);
                Assert.AreEqual(before.Pos, after.Pos);
                Assert.AreEqual(before.Carried, after.Carried);
                Assert.AreEqual(before.Task?.Kind, after.Task?.Kind);
                Assert.AreEqual(before.Task?.Phase, after.Task?.Phase);
                Assert.AreEqual(before.Path.Count, after.Path.Count);
            }
        }

        [Test]
        public void FileRoundTripWorks()
        {
            var world = Scenarios.StarterFactory(seed: 21, workerCount: 3);
            world.StepMany(600);

            string path = Path.Combine(Path.GetTempPath(), "worker-save-test", "slot0.wsave");
            try
            {
                SaveSerializer.SaveToFile(world, path);
                Assert.IsTrue(File.Exists(path));

                var restored = SaveSerializer.LoadFromFile(path);
                Assert.AreEqual(StateHash.Compute(world), StateHash.Compute(restored));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void RejectsFilesThatAreNotSaves()
        {
            var bytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            Assert.Throws<InvalidDataException>(() => SaveSerializer.LoadFromBytes(bytes));
        }
    }
}
