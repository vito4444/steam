using System.Collections.Generic;
using System.IO;
using Hunter.Gameplay.Camp;
using Hunter.Gameplay.Items;
using NUnit.Framework;

namespace Hunter.Tests
{
    public class CampStateTests
    {
        static ItemInstance Treasure(int value, float weight = 1f)
        {
            var def = new ItemDefinition("t" + value, "t", ItemCategory.Treasure, ItemRarity.Common, value, weight);
            return new ItemInstance(def);
        }

        [Test]
        public void BankHaulAddsTheValueAndCountsASurvival()
        {
            var camp = new CampState();
            int banked = camp.BankHaul(new List<ItemInstance> { Treasure(300), Treasure(250) });

            Assert.AreEqual(550, banked);
            Assert.AreEqual(550, camp.Aurum);
            Assert.AreEqual(1, camp.RunsSurvived);
            Assert.AreEqual(0, camp.RunsLost);
        }

        [Test]
        public void RecordLossBanksNothing()
        {
            var camp = new CampState();
            camp.RecordLoss(valueLost: 900, itemsLost: 4);

            Assert.AreEqual(0, camp.Aurum, "dying must never pay out");
            Assert.AreEqual(1, camp.RunsLost);
            Assert.AreEqual(900, camp.LifetimeLosses);
        }

        [Test]
        public void UpgradeSpendsAurumAndRaisesLevel()
        {
            var camp = new CampState();
            camp.BankHaul(new List<ItemInstance> { Treasure(1000) });

            int cost = camp.GetFacility(FacilityKind.Vault).NextCost;
            Assert.IsTrue(camp.TryUpgrade(FacilityKind.Vault));

            Assert.AreEqual(1, camp.LevelOf(FacilityKind.Vault));
            Assert.AreEqual(1000 - cost, camp.Aurum);
        }

        [Test]
        public void UpgradeRefusedWithoutEnoughAurum()
        {
            var camp = new CampState();
            camp.BankHaul(new List<ItemInstance> { Treasure(10) });

            Assert.IsFalse(camp.TryUpgrade(FacilityKind.Vault));
            Assert.AreEqual(0, camp.LevelOf(FacilityKind.Vault));
            Assert.AreEqual(10, camp.Aurum, "a refused upgrade must not charge");
        }

        [Test]
        public void UpgradeCostsRiseSteeplyEnoughToKeepTheLoopRunning()
        {
            var vault = new Facility(FacilityKind.Vault, 420);
            int first = vault.NextCost;
            vault.Upgrade();
            int second = vault.NextCost;
            vault.Upgrade();
            int third = vault.NextCost;

            Assert.Greater(second, first * 1.5f);
            Assert.Greater(third, second * 1.5f);
        }

        [Test]
        public void FacilityCannotExceedMaxLevel()
        {
            var camp = new CampState();
            var shrine = camp.GetFacility(FacilityKind.Shrine);

            camp.BankHaul(new List<ItemInstance> { Treasure(10_000_000) });
            for (int i = 0; i < shrine.MaxLevel + 3; i++) camp.TryUpgrade(FacilityKind.Shrine);

            Assert.AreEqual(shrine.MaxLevel, shrine.Level);
            Assert.IsTrue(shrine.IsMaxed);
            Assert.IsFalse(camp.TryUpgrade(FacilityKind.Shrine));
        }

        [Test]
        public void VaultUpgradesRaiseCarryCapacity()
        {
            var camp = new CampState();
            var before = camp.BuildModifiers();

            camp.BankHaul(new List<ItemInstance> { Treasure(5000) });
            camp.TryUpgrade(FacilityKind.Vault);
            var after = camp.BuildModifiers();

            Assert.Greater(after.CarryWeightBonus, before.CarryWeightBonus);
            Assert.Greater(after.CarrySlotBonus, before.CarrySlotBonus);
        }

        [Test]
        public void ForgeUpgradesImproveTheStartingWeapon()
        {
            var camp = new CampState();
            Assert.AreEqual(ItemCatalog.RustedBlade, camp.BuildModifiers().StartingWeapon);

            camp.BankHaul(new List<ItemInstance> { Treasure(50000) });
            camp.TryUpgrade(FacilityKind.Forge);
            Assert.AreEqual(ItemCatalog.HuntersFalchion, camp.BuildModifiers().StartingWeapon);

            camp.TryUpgrade(FacilityKind.Forge);
            camp.TryUpgrade(FacilityKind.Forge);
            Assert.AreEqual(ItemCatalog.WardenHalberd, camp.BuildModifiers().StartingWeapon);
        }

        [Test]
        public void SurvivalRateReflectsHistory()
        {
            var camp = new CampState();
            Assert.AreEqual(0f, camp.SurvivalRate, 1e-4f);

            camp.BankHaul(new List<ItemInstance> { Treasure(100) });
            camp.RecordLoss(50, 1);
            camp.RecordLoss(70, 2);

            Assert.AreEqual(1f / 3f, camp.SurvivalRate, 1e-3f);
        }
    }

    public class CampSaveTests
    {
        string _path;

        [SetUp]
        public void SetUp() => _path = Path.Combine(Path.GetTempPath(), $"hunter_camp_{System.Guid.NewGuid():N}.json");

        [TearDown]
        public void TearDown() => CampSave.Delete(_path);

        [Test]
        public void SaveThenLoadRestoresAurumAndFacilities()
        {
            var camp = new CampState();
            camp.BankHaul(new List<ItemInstance>
            {
                new(new ItemDefinition("a", "a", ItemCategory.Treasure, ItemRarity.Common, 4000, 1f)),
            });
            camp.TryUpgrade(FacilityKind.Vault);
            camp.TryUpgrade(FacilityKind.Alchemy);

            CampSave.Save(camp, _path);

            var restored = new CampState();
            Assert.IsTrue(CampSave.TryLoad(restored, _path));

            Assert.AreEqual(camp.Aurum, restored.Aurum);
            Assert.AreEqual(camp.LevelOf(FacilityKind.Vault), restored.LevelOf(FacilityKind.Vault));
            Assert.AreEqual(camp.LevelOf(FacilityKind.Alchemy), restored.LevelOf(FacilityKind.Alchemy));
            Assert.AreEqual(camp.RunsSurvived, restored.RunsSurvived);
        }

        [Test]
        public void LoadFromMissingFileLeavesAFreshCampUntouched()
        {
            var camp = new CampState();
            Assert.IsFalse(CampSave.TryLoad(camp, _path));
            Assert.AreEqual(0, camp.Aurum);
        }

        [Test]
        public void CorruptSaveDoesNotThrow()
        {
            File.WriteAllText(_path, "{ this is not json ");
            var camp = new CampState();

            // Losing progress is bad; refusing to launch is worse.
            Assert.DoesNotThrow(() => CampSave.TryLoad(camp, _path));
            Assert.AreEqual(0, camp.Aurum);
        }

        [Test]
        public void SaveIsWrittenAtomicallyLeavingNoTemporaryBehind()
        {
            var camp = new CampState();
            CampSave.Save(camp, _path);

            Assert.IsTrue(File.Exists(_path));
            Assert.IsFalse(File.Exists(_path + ".tmp"), "the temporary file must be moved, not left");
        }

        [Test]
        public void RepeatedSavesOverwriteCleanly()
        {
            var camp = new CampState();
            CampSave.Save(camp, _path);

            camp.BankHaul(new List<ItemInstance>
            {
                new(new ItemDefinition("b", "b", ItemCategory.Treasure, ItemRarity.Common, 777, 1f)),
            });
            CampSave.Save(camp, _path);

            var restored = new CampState();
            CampSave.TryLoad(restored, _path);
            Assert.AreEqual(777, restored.Aurum);
        }
    }
}
