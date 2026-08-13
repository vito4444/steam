using Hunter.Gameplay.Camp;
using Hunter.Gameplay.Combat;
using NUnit.Framework;

namespace Hunter.Tests
{
    /// The camp screen quotes an exact number before the player spends aurum on it. These
    /// tests exist to catch the case where the shop and the raid drift apart, which is the
    /// failure a player would read as the upgrade doing nothing.
    public class FacilityInfoTests
    {
        [Test]
        public void VaultCopyMatchesTheWeightTheRaidGrants()
        {
            var camp = new CampState();
            camp.LoadFrom(new CampState.SaveData
            {
                facilityKinds = new[] { (int)FacilityKind.Vault },
                facilityLevels = new[] { 3 },
            });

            var modifiers = camp.BuildModifiers();
            float advertised = FacilityInfo.Weight(3);
            float granted = FacilityInfo.BaseCarryWeight + modifiers.CarryWeightBonus;

            Assert.AreEqual(advertised, granted, 0.001f,
                "vault row promises a carry weight the run does not apply");
        }

        [Test]
        public void VaultCopyMatchesTheSlotsTheRaidGrants()
        {
            var camp = new CampState();
            camp.LoadFrom(new CampState.SaveData
            {
                facilityKinds = new[] { (int)FacilityKind.Vault },
                facilityLevels = new[] { 4 },
            });

            Assert.AreEqual(FacilityInfo.Slots(4),
                FacilityInfo.BaseCarrySlots + camp.BuildModifiers().CarrySlotBonus);
        }

        [Test]
        public void ShrineCopyMatchesTheVitalityTheRaidGrants()
        {
            var camp = new CampState();
            camp.LoadFrom(new CampState.SaveData
            {
                facilityKinds = new[] { (int)FacilityKind.Shrine },
                facilityLevels = new[] { 2 },
            });

            Assert.AreEqual(FacilityInfo.Vitality(2),
                FacilityInfo.BaseVitality + camp.BuildModifiers().VitalityBonus, 0.001f);
        }

        [Test]
        public void ForgeCopyMatchesTheWeaponTheRaidGrants()
        {
            for (int level = 0; level <= 5; level++)
            {
                var camp = new CampState();
                camp.LoadFrom(new CampState.SaveData
                {
                    facilityKinds = new[] { (int)FacilityKind.Forge },
                    facilityLevels = new[] { level },
                });

                Assert.AreSame(FacilityInfo.WeaponAt(level), camp.BuildModifiers().StartingWeapon,
                    $"forge level {level} hands out a different weapon than the shop listed");
            }
        }

        [Test]
        public void ForgeDamageMatchesTheProfileTheRaidEquips()
        {
            for (int level = 0; level <= 5; level++)
            {
                var profile = MeleeProfile.ForForgeLevel(level);
                profile.BaseDamage *= FacilityInfo.ForgeDamageMultiplier(level);

                Assert.AreEqual(FacilityInfo.ForgeDamage(level), profile.BaseDamage, 0.001f,
                    $"forge level {level} swings for a different number than the shop quoted");
            }
        }

        [Test]
        public void EveryForgeLevelBuysAStrongerSwing()
        {
            // A purchase that changes nothing is the bug this catches: tiers only move at
            // three of the levels, so the honing multiplier has to cover the rest.
            for (int level = 0; level < 5; level++)
            {
                Assert.Greater(FacilityInfo.ForgeDamage(level + 1), FacilityInfo.ForgeDamage(level),
                    $"forge level {level} to {level + 1} costs aurum but changes no damage");
            }
        }

        [Test]
        public void EveryFacilityDescribesItsNextLevel()
        {
            foreach (FacilityKind kind in System.Enum.GetValues(typeof(FacilityKind)))
            {
                Assert.IsNotEmpty(FacilityInfo.DisplayName(kind), $"{kind} has no name");
                Assert.IsNotEmpty(FacilityInfo.Tagline(kind), $"{kind} has no tagline");
                Assert.IsNotEmpty(FacilityInfo.NextEffect(kind, 0), $"{kind} does not say what level 1 buys");
                Assert.IsNotEmpty(FacilityInfo.CurrentEffect(kind, 0), $"{kind} does not say what it grants now");
            }
        }

        [Test]
        public void NextEffectStatesBothSidesOfTheChange()
        {
            // "Better blade" is what this guards against; the row has to carry the numbers.
            foreach (FacilityKind kind in System.Enum.GetValues(typeof(FacilityKind)))
            {
                StringAssert.Contains("\u2192", FacilityInfo.NextEffect(kind, 0),
                    $"{kind} does not show a before and after value");
            }
        }

        [Test]
        public void CampScreenCopyAvoidsGlyphsTheBuiltInFontCannotDraw()
        {
            // The item catalog is in Chinese and the built-in UI font has no glyphs for it,
            // so anything the camp screen prints has to stay inside its own English names.
            foreach (FacilityKind kind in System.Enum.GetValues(typeof(FacilityKind)))
            {
                foreach (var copy in new[]
                         {
                             FacilityInfo.DisplayName(kind), FacilityInfo.Tagline(kind),
                             FacilityInfo.NextEffect(kind, 1), FacilityInfo.CurrentEffect(kind, 1),
                         })
                {
                    foreach (char c in copy)
                    {
                        Assert.Less(c, 0x2E80, $"{kind} copy contains CJK character '{c}' the font cannot render");
                    }
                }
            }
        }

        [Test]
        public void MaxedFacilityIsNotAskedToDescribeAnotherLevel()
        {
            var camp = new CampState();
            var vault = camp.GetFacility(FacilityKind.Vault);
            for (int i = 0; i < vault.MaxLevel; i++) vault.SetLevel(i + 1);

            Assert.IsTrue(vault.IsMaxed);
            Assert.AreEqual(int.MaxValue, vault.NextCost,
                "a maxed facility must not quote a purchasable price");
        }

        [Test]
        public void RustedBladeIsWeakerThanTheFalchionItReplaces()
        {
            var rusted = MeleeProfile.RustedBlade();
            var falchion = MeleeProfile.Falchion();

            Assert.Less(rusted.BaseDamage, falchion.BaseDamage);
            Assert.Less(rusted.Range, falchion.Range);
            Assert.Greater(rusted.StartupTime, falchion.StartupTime,
                "the starting blade should feel slower, not only weaker");
        }

        [Test]
        public void UpgradingSpendsExactlyTheQuotedPrice()
        {
            var camp = new CampState();
            int quoted = camp.GetFacility(FacilityKind.Vault).NextCost;

            camp.LoadFrom(new CampState.SaveData { aurum = quoted + 50 });
            int before = camp.Aurum;

            Assert.IsTrue(camp.TryUpgrade(FacilityKind.Vault));
            Assert.AreEqual(before - quoted, camp.Aurum, "charged a different price than the row showed");
        }

        [Test]
        public void CannotUpgradeWithoutTheAurum()
        {
            var camp = new CampState();
            Assert.IsFalse(camp.CanAfford(FacilityKind.Vault));
            Assert.IsFalse(camp.TryUpgrade(FacilityKind.Vault));
            Assert.AreEqual(0, camp.LevelOf(FacilityKind.Vault));
        }
    }
}
