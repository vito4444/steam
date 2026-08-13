using Hunter.Gameplay.Items;
using NUnit.Framework;

namespace Hunter.Tests
{
    public class LootValuationTests
    {
        static ItemInstance Make(string id, int value, float weight)
        {
            var def = new ItemDefinition(id, id, ItemCategory.Treasure, ItemRarity.Common, value, weight);
            return new ItemInstance(def);
        }

        [Test]
        public void ValueDensity_IsValuePerKilogram()
        {
            Assert.AreEqual(50f, LootValuation.ValueDensity(Make("a", 100, 2f)), 1e-3f);
        }

        [Test]
        public void ValueDensity_TreatsWeightlessItemsAsAlwaysWorthTaking()
        {
            Assert.Greater(LootValuation.ValueDensity(Make("token", 1, 0f)), 1e6f);
        }

        [Test]
        public void RequiredDensity_RisesLateInTheRun()
        {
            float early = LootValuation.RequiredDensity(new LootValuation.Context(0.05f, 40f, 0f));
            float late = LootValuation.RequiredDensity(new LootValuation.Context(0.95f, 40f, 0f));

            Assert.Greater(late, early * 2f);
        }

        [Test]
        public void RequiredDensity_RisesUnderThreat()
        {
            float calm = LootValuation.RequiredDensity(new LootValuation.Context(0.3f, 40f, 0f));
            float hunted = LootValuation.RequiredDensity(new LootValuation.Context(0.3f, 40f, 1f));

            Assert.Greater(hunted, calm * 2f);
        }

        [Test]
        public void Appraise_TakesHighDensityLootWhenBagHasRoom()
        {
            var inv = new Inventory(30f, 10);
            var result = LootValuation.Appraise(Make("gem", 500, 1f), inv, LootValuation.Context.FreshRun);

            Assert.AreEqual(LootValuation.Decision.Take, result.Decision);
        }

        [Test]
        public void Appraise_IgnoresLowDensityLoot()
        {
            var inv = new Inventory(30f, 10);
            // 12 per kilo, below the fresh-run threshold of 4 * 1 * 1.4 * 1 = 5.6? No:
            // this must be genuinely poor, so use 1 per kilo.
            var result = LootValuation.Appraise(Make("rock", 10, 10f), inv, LootValuation.Context.FreshRun);

            Assert.AreEqual(LootValuation.Decision.Ignore, result.Decision);
        }

        [Test]
        public void Appraise_SwapsOutClearlyWorseItemWhenBagIsFull()
        {
            var inv = new Inventory(weightLimit: 10f, slotLimit: 10);
            var junk = Make("junk", 60, 9f);         // ~6.7 per kilo
            inv.TryAdd(junk);

            var prize = Make("prize", 900, 2f);      // 450 per kilo
            var result = LootValuation.Appraise(prize, inv, LootValuation.Context.FreshRun);

            Assert.AreEqual(LootValuation.Decision.SwapForWorseItem, result.Decision);
            Assert.AreSame(junk, result.ItemToDrop);
        }

        [Test]
        public void Appraise_DoesNotSwapOnNearTies()
        {
            var inv = new Inventory(weightLimit: 10f, slotLimit: 10);
            var held = Make("held", 800, 8f);        // 100 per kilo
            inv.TryAdd(held);

            var similar = Make("similar", 840, 8f);  // 105 per kilo, only 5% better
            var result = LootValuation.Appraise(similar, inv, LootValuation.Context.FreshRun);

            Assert.AreEqual(LootValuation.Decision.Ignore, result.Decision);
        }

        [Test]
        public void ExtractionUrgency_RisesWithHaulValue()
        {
            var poor = new Inventory(60f, 20);
            poor.TryAdd(Make("a", 50, 1f));

            var rich = new Inventory(60f, 20);
            rich.TryAdd(Make("b", 1200, 1f));

            var ctx = new LootValuation.Context(0.3f, 50f, 0.1f);
            Assert.Greater(LootValuation.ExtractionUrgency(rich, ctx),
                           LootValuation.ExtractionUrgency(poor, ctx));
        }

        [Test]
        public void ShouldHeadForExtraction_TrueWhenLateAndLoadedAndHunted()
        {
            var inv = new Inventory(30f, 20);
            inv.TryAdd(Make("haul", 1300, 24f));

            var ctx = new LootValuation.Context(runProgress: 0.8f, distanceToExtraction: 40f, threat: 0.8f);
            Assert.IsTrue(LootValuation.ShouldHeadForExtraction(inv, ctx));
        }

        [Test]
        public void ShouldHeadForExtraction_FalseOnAnEmptyEarlySafeRun()
        {
            var inv = new Inventory(30f, 20);
            Assert.IsFalse(LootValuation.ShouldHeadForExtraction(inv, LootValuation.Context.FreshRun));
        }
    }
}
