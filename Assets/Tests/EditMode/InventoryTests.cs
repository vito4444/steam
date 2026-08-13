using NUnit.Framework;

namespace Worker.Core.Tests
{
    [TestFixture]
    public class InventoryTests
    {
        [Test]
        public void NewInventoryIsEmpty()
        {
            var inv = new Inventory(4);
            Assert.IsTrue(inv.IsEmpty);
            Assert.IsFalse(inv.IsFull);
            Assert.AreEqual(0, inv.TotalCount());
        }

        [Test]
        public void AddThenCountRoundTrips()
        {
            var inv = new Inventory(4);
            int stored = inv.TryAdd(ItemId.Plank, 10);

            Assert.AreEqual(10, stored);
            Assert.AreEqual(10, inv.CountOf(ItemId.Plank));
            Assert.AreEqual(0, inv.CountOf(ItemId.Log));
        }

        [Test]
        public void AddRespectsSlotAndStackLimits()
        {
            // Logs stack to 20, so a 2-slot container holds at most 40.
            var inv = new Inventory(2);
            int stored = inv.TryAdd(ItemId.Log, 100);

            Assert.AreEqual(40, stored);
            Assert.AreEqual(40, inv.CountOf(ItemId.Log));
            Assert.IsTrue(inv.IsFull);
        }

        [Test]
        public void AddTopsUpPartialStacksBeforeOpeningNewOnes()
        {
            var inv = new Inventory(3);
            inv.TryAdd(ItemId.Log, 5);
            inv.TryAdd(ItemId.Log, 5);

            Assert.AreEqual(10, inv.CountOf(ItemId.Log));
            Assert.AreEqual(10, inv[0].Count, "second add should have merged into the first slot");
            Assert.IsTrue(inv[1].IsEmpty);
        }

        [Test]
        public void RemoveReturnsWhatWasActuallyTaken()
        {
            var inv = new Inventory(4);
            inv.TryAdd(ItemId.Plank, 7);

            Assert.AreEqual(7, inv.TryRemove(ItemId.Plank, 10));
            Assert.AreEqual(0, inv.CountOf(ItemId.Plank));
            Assert.IsTrue(inv.IsEmpty);
        }

        [Test]
        public void SpaceForAccountsForExistingContents()
        {
            var inv = new Inventory(2);
            Assert.AreEqual(40, inv.SpaceFor(ItemId.Log));

            inv.TryAdd(ItemId.Log, 15);
            Assert.AreEqual(25, inv.SpaceFor(ItemId.Log));

            // A slot occupied by another item is not available for logs.
            var mixed = new Inventory(2);
            mixed.TryAdd(ItemId.Plank, 1);
            Assert.AreEqual(20, mixed.SpaceFor(ItemId.Log));
        }

        [Test]
        public void ContainsAllChecksEveryRequirement()
        {
            var inv = new Inventory(4);
            inv.TryAdd(ItemId.ChairLeg, 4);
            inv.TryAdd(ItemId.Seat, 1);

            var recipe = GameData.Recipe(RecipeId.AssembleWoodChair);
            Assert.IsTrue(inv.ContainsAll(recipe.Inputs));

            inv.TryRemove(ItemId.Seat, 1);
            Assert.IsFalse(inv.ContainsAll(recipe.Inputs));
        }

        [Test]
        public void RemoveAllConsumesExactRecipeInputs()
        {
            var inv = new Inventory(4);
            inv.TryAdd(ItemId.ChairLeg, 6);
            inv.TryAdd(ItemId.Seat, 2);

            var recipe = GameData.Recipe(RecipeId.AssembleWoodChair);
            inv.RemoveAll(recipe.Inputs);

            Assert.AreEqual(2, inv.CountOf(ItemId.ChairLeg));
            Assert.AreEqual(1, inv.CountOf(ItemId.Seat));
        }

        [Test]
        public void RemoveAllThrowsRatherThanSilentlyUnderflowing()
        {
            var inv = new Inventory(4);
            inv.TryAdd(ItemId.ChairLeg, 1);

            var recipe = GameData.Recipe(RecipeId.AssembleWoodChair);
            Assert.Throws<System.InvalidOperationException>(() => inv.RemoveAll(recipe.Inputs));
        }

        [Test]
        public void EmptyStackNormalises()
        {
            var zero = new ItemStack(ItemId.Plank, 0);
            Assert.IsTrue(zero.IsEmpty);
            Assert.AreEqual(ItemId.None, zero.Item);
        }
    }
}
