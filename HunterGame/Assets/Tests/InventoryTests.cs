using System.Collections.Generic;
using Hunter.Gameplay.Items;
using NUnit.Framework;

namespace Hunter.Tests
{
    public class InventoryTests
    {
        static ItemInstance Make(string id, int value, float weight, params ItemAffix[] affixes)
        {
            var def = new ItemDefinition(id, id, ItemCategory.Treasure, ItemRarity.Common, value, weight);
            return new ItemInstance(def, affixes);
        }

        [Test]
        public void TryAdd_RejectsItemHeavierThanRemainingCapacity()
        {
            var inv = new Inventory(weightLimit: 10f, slotLimit: 20);
            Assert.AreEqual(AddResult.Added, inv.TryAdd(Make("a", 10, 7f)));

            Assert.AreEqual(AddResult.RejectedOverweight, inv.TryAdd(Make("b", 10, 4f)));
            Assert.AreEqual(1, inv.Count);
            Assert.AreEqual(7f, inv.TotalWeight, 1e-4f);
        }

        [Test]
        public void TryAdd_RejectsWhenSlotsAreFullEvenIfLight()
        {
            var inv = new Inventory(weightLimit: 100f, slotLimit: 2);
            inv.TryAdd(Make("a", 1, 0.1f));
            inv.TryAdd(Make("b", 1, 0.1f));

            Assert.AreEqual(AddResult.RejectedFull, inv.TryAdd(Make("c", 1, 0.1f)));
            Assert.AreEqual(2, inv.Count);
        }

        [Test]
        public void SpeedMultiplier_IsUnpenalisedBelowSoftCap()
        {
            var inv = new Inventory(weightLimit: 20f, slotLimit: 20, softCapRatio: 0.6f);
            inv.TryAdd(Make("a", 10, 11f));   // 55% load

            Assert.AreEqual(1f, inv.SpeedMultiplier, 1e-4f);
        }

        [Test]
        public void SpeedMultiplier_FallsToFloorAtFullLoad()
        {
            var inv = new Inventory(weightLimit: 20f, slotLimit: 20, softCapRatio: 0.6f);
            inv.TryAdd(Make("a", 10, 20f));

            Assert.AreEqual(Inventory.MinSpeedMultiplier, inv.SpeedMultiplier, 1e-4f);
        }

        [Test]
        public void SpeedMultiplier_DecreasesMonotonicallyPastSoftCap()
        {
            var light = new Inventory(20f, 20, 0.6f);
            light.TryAdd(Make("a", 10, 14f));

            var heavy = new Inventory(20f, 20, 0.6f);
            heavy.TryAdd(Make("b", 10, 18f));

            Assert.Less(heavy.SpeedMultiplier, light.SpeedMultiplier);
            Assert.Less(light.SpeedMultiplier, 1f);
        }

        [Test]
        public void DropAll_EmptiesBagAndReturnsContents()
        {
            var inv = new Inventory(50f, 20);
            inv.TryAdd(Make("a", 10, 1f));
            inv.TryAdd(Make("b", 20, 2f));

            var dropped = inv.DropAll();

            Assert.AreEqual(2, dropped.Count);
            Assert.AreEqual(0, inv.Count);
            Assert.AreEqual(0f, inv.TotalWeight, 1e-4f);
        }

        [Test]
        public void WorstByDensity_PicksLowestValuePerKilo()
        {
            var inv = new Inventory(50f, 20);
            var dense = Make("dense", 300, 1f);     // 300/kg
            var bulky = Make("bulky", 300, 10f);    // 30/kg
            inv.TryAdd(dense);
            inv.TryAdd(bulky);

            Assert.AreSame(bulky, inv.WorstByDensity());
        }

        [Test]
        public void Value_CompoundsAffixMultipliers()
        {
            var plain = Make("plain", 100, 1f);
            var doubled = Make("fancy", 100, 1f, ItemCatalog.Keen, ItemCatalog.Gilded);

            Assert.AreEqual(100, plain.Value);
            // 100 * 1.35 * 1.6 = 216
            Assert.AreEqual(216, doubled.Value);
        }

        [Test]
        public void EffectiveRarity_RisesWithAffixCountButClampsAtEpic()
        {
            var def = new ItemDefinition("d", "d", ItemCategory.Weapon, ItemRarity.Rare, 10, 1f);

            Assert.AreEqual(ItemRarity.Rare, new ItemInstance(def).EffectiveRarity);
            Assert.AreEqual(ItemRarity.Epic, new ItemInstance(def, new[] { ItemCatalog.Keen }).EffectiveRarity);
            Assert.AreEqual(ItemRarity.Epic,
                new ItemInstance(def, new[] { ItemCatalog.Keen, ItemCatalog.Swift, ItemCatalog.Gilded })
                    .EffectiveRarity);
        }

        [Test]
        public void GetStat_SumsBaseAndAffixContributions()
        {
            var def = new ItemDefinition("w", "w", ItemCategory.Weapon, ItemRarity.Common, 10, 1f,
                new Dictionary<StatKind, float> { { StatKind.Damage, 20f } });
            var item = new ItemInstance(def, new[] { ItemCatalog.Keen });

            Assert.AreEqual(26f, item.GetStat(StatKind.Damage), 1e-4f);
            Assert.AreEqual(0f, item.GetStat(StatKind.Armour), 1e-4f);
        }
    }
}
