using System;
using Hunter.Gameplay.Items;
using Hunter.Gameplay.Run;
using NUnit.Framework;

namespace Hunter.Tests
{
    public class RunDirectorTests
    {
        static ItemInstance Treasure(int value, float weight = 1f)
        {
            var def = new ItemDefinition("t" + value, "t", ItemCategory.Treasure, ItemRarity.Common, value, weight);
            return new ItemInstance(def);
        }

        static (RunDirector director, Inventory inventory) NewRun(RunDirector.Settings settings = null)
        {
            var inv = new Inventory(40f, 20);
            var dir = new RunDirector(inv, settings ?? new RunDirector.Settings());
            dir.Begin();
            return (dir, inv);
        }

        [Test]
        public void Begin_MovesFromInsertionToScavenging()
        {
            var (dir, _) = NewRun();
            Assert.AreEqual(RunPhase.Scavenging, dir.Phase);
        }

        [Test]
        public void RingBell_RejectedBeforeEarliestBellTime()
        {
            var (dir, _) = NewRun(new RunDirector.Settings { EarliestBellSeconds = 45f });
            dir.Tick(10f);

            Assert.AreEqual(BellResult.NotAllowedYet, dir.RingBell());
            Assert.AreEqual(RunPhase.Scavenging, dir.Phase);
        }

        [Test]
        public void RingBell_OpensPortalAndSpikesThreat()
        {
            var (dir, _) = NewRun();
            dir.Tick(60f);
            float before = dir.Threat;

            Assert.AreEqual(BellResult.Rung, dir.RingBell());
            Assert.AreEqual(RunPhase.ExtractionWindow, dir.Phase);
            Assert.Greater(dir.PortalSecondsRemaining, 0f);
            Assert.Greater(dir.Threat, before);
        }

        [Test]
        public void PortalClosesAndReturnsHunterToScavenging()
        {
            var (dir, _) = NewRun(new RunDirector.Settings { ExtractionWindowSeconds = 15f });
            dir.Tick(60f);
            dir.RingBell();

            dir.Tick(16f);

            Assert.AreEqual(RunPhase.Scavenging, dir.Phase);
            Assert.AreEqual(0f, dir.PortalSecondsRemaining, 1e-4f);
            Assert.IsFalse(dir.IsOver);
        }

        [Test]
        public void EnterPortal_BanksTheHaulAndEmptiesTheBag()
        {
            var (dir, inv) = NewRun();
            inv.TryAdd(Treasure(300));
            inv.TryAdd(Treasure(200));
            dir.Tick(60f);
            dir.RingBell();

            Assert.IsTrue(dir.EnterPortal());
            Assert.AreEqual(RunPhase.Extracted, dir.Phase);
            Assert.AreEqual(500, dir.BankedValue);
            Assert.AreEqual(2, dir.BankedItems.Count);
            Assert.AreEqual(0, inv.Count);
        }

        [Test]
        public void EnterPortal_FailsWhenNoWindowIsOpen()
        {
            var (dir, inv) = NewRun();
            inv.TryAdd(Treasure(300));
            dir.Tick(60f);

            Assert.IsFalse(dir.EnterPortal());
            Assert.AreEqual(RunPhase.Scavenging, dir.Phase);
            Assert.AreEqual(0, dir.BankedValue);
            Assert.AreEqual(1, inv.Count);
        }

        [Test]
        public void Kill_LosesEverythingCarried()
        {
            var (dir, inv) = NewRun();
            inv.TryAdd(Treasure(900));

            var droppedCount = 0;
            dir.LootDropped += items => droppedCount = items.Count;

            dir.Kill();

            Assert.AreEqual(RunPhase.Died, dir.Phase);
            Assert.AreEqual(0, dir.BankedValue);
            Assert.AreEqual(0, inv.Count);
            Assert.AreEqual(1, droppedCount);
        }

        [Test]
        public void Kill_AfterExtractionDoesNotRevokeBankedHaul()
        {
            var (dir, inv) = NewRun();
            inv.TryAdd(Treasure(400));
            dir.Tick(60f);
            dir.RingBell();
            dir.EnterPortal();

            dir.Kill();

            Assert.AreEqual(RunPhase.Extracted, dir.Phase);
            Assert.AreEqual(400, dir.BankedValue);
        }

        [Test]
        public void SovereignAwakensAtConfiguredTime()
        {
            var (dir, _) = NewRun(new RunDirector.Settings { SovereignSpawnSeconds = 100f });
            int awakenings = 0;
            dir.SovereignAwakened += () => awakenings++;

            dir.Tick(99f);
            Assert.IsFalse(dir.SovereignActive);

            dir.Tick(2f);
            Assert.IsTrue(dir.SovereignActive);

            dir.Tick(200f);
            Assert.AreEqual(1, awakenings, "sovereign must only awaken once");
        }

        [Test]
        public void RunningOutOfTimeLosesTheHaul()
        {
            var (dir, inv) = NewRun(new RunDirector.Settings { RunDurationSeconds = 100f });
            inv.TryAdd(Treasure(700));

            dir.Tick(101f);

            Assert.AreEqual(RunPhase.TimedOut, dir.Phase);
            Assert.AreEqual(0, dir.BankedValue);
            Assert.AreEqual(0, inv.Count);
        }

        [Test]
        public void ThreatRisesOverTheCourseOfARun()
        {
            var (dir, _) = NewRun(new RunDirector.Settings { RunDurationSeconds = 1000f, SovereignSpawnSeconds = 900f });
            dir.Tick(50f);
            float early = dir.Threat;

            dir.Tick(700f);
            Assert.Greater(dir.Threat, early);
        }

        [Test]
        public void ValuationContextReflectsLiveRunState()
        {
            var (dir, _) = NewRun(new RunDirector.Settings { RunDurationSeconds = 1000f });
            dir.Tick(500f);

            var ctx = dir.BuildValuationContext(distanceToExtraction: 33f);

            Assert.AreEqual(0.5f, ctx.RunProgress, 1e-3f);
            Assert.AreEqual(33f, ctx.DistanceToExtraction, 1e-3f);
            Assert.AreEqual(dir.Threat, ctx.Threat, 1e-3f);
        }

        [Test]
        public void TickRejectsNegativeDelta()
        {
            var (dir, _) = NewRun();
            Assert.Throws<ArgumentOutOfRangeException>(() => dir.Tick(-1f));
        }
    }

    public class LootTableTests
    {
        [Test]
        public void SameSeedProducesSameRoll()
        {
            var table = ItemCatalog.CommonCache();
            var a = table.Roll(new Random(1234));
            var b = table.Roll(new Random(1234));

            Assert.AreEqual(a.Definition.Id, b.Definition.Id);
            Assert.AreEqual(a.Affixes.Count, b.Affixes.Count);
        }

        [Test]
        public void HigherLuckProducesMoreAffixesOverManyRolls()
        {
            var table = ItemCatalog.EliteCache();

            int unlucky = CountAffixes(table, luck: 0f);
            int lucky = CountAffixes(table, luck: 0.5f);

            Assert.Greater(lucky, unlucky);
        }

        static int CountAffixes(LootTable table, float luck)
        {
            var rng = new Random(99);
            int total = 0;
            for (int i = 0; i < 400; i++) total += table.Roll(rng, luck).Affixes.Count;
            return total;
        }

        [Test]
        public void RollNeverProducesDuplicateAffixesOnOneItem()
        {
            var table = ItemCatalog.EliteCache();
            var rng = new Random(7);

            for (int i = 0; i < 500; i++)
            {
                var item = table.Roll(rng, luck: 0.85f);
                CollectionAssert.AllItemsAreUnique(System.Linq.Enumerable.ToList(
                    System.Linq.Enumerable.Select(item.Affixes, a => a.Id)));
            }
        }

        [Test]
        public void CommonCacheFavoursCheapItemsOverTreasure()
        {
            var table = ItemCatalog.CommonCache();
            var rng = new Random(2026);
            int epics = 0;

            for (int i = 0; i < 1000; i++)
            {
                if (table.Roll(rng).Definition.BaseRarity == ItemRarity.Epic) epics++;
            }

            Assert.AreEqual(0, epics, "common caches must never drop epic base items");
        }
    }
}
