using NUnit.Framework;
using Undertown.Core.Buildings;
using Undertown.Core.Economy;
using Undertown.Core.Sim;
using Undertown.Core.World;

namespace Undertown.Tests
{
    /// <summary>
    /// Whole-season tests. These are the ones that catch design faults rather than coding
    /// faults: every problem fixed in this file's history was invisible to the unit tests
    /// and only appeared once a season was played end to end.
    /// </summary>
    public class InspectionSystemTests
    {
        private const uint Seed = 20260813;

        private static TownState NewTown()
        {
            var map = MapGenerator.Generate(MapSettings.Default, Seed);
            var town = new TownState(map, Seed);
            town.Stock.Add(MaterialId.Timber, 120);
            town.Stock.Add(MaterialId.Clay, 40);
            town.Stock.Add(MaterialId.Grain, 200);
            town.Books.BeginPeriod(town.Stock.Get);
            TownFounder.Found(town);
            AgentSystem.Populate(town);
            return town;
        }

        private static void Run(TownState town, int minutes)
        {
            const int chunk = 15;
            for (int elapsed = 0; elapsed < minutes; elapsed += chunk)
            {
                int step = System.Math.Min(chunk, minutes - elapsed);
                town.Clock.Advance(step);
                ProductionSystem.Tick(town, step);
                AgentSystem.Tick(town, step);
                InspectionSystem.Tick(town, step);
            }
        }

        [Test]
        public void TheInheritedTownStartsWithBothChainsRunning()
        {
            var town = NewTown();

            Assert.IsNotNull(BuildingPlacement.BuildingAt(town, FindKind(town, BuildingKind.Brewery)),
                "the lawful chain needs a brewery");
            Assert.IsNotNull(BuildingPlacement.BuildingAt(town, FindKind(town, BuildingKind.Still)),
                "the illicit chain needs a still");
            Assert.Greater(town.SurfaceSpoil, 0, "digging the inherited chamber must leave spoil to explain");
            Assert.AreEqual(6, town.Villagers.Count);
        }

        [Test]
        public void OnlyTheLawfulChainReachesTheBooks()
        {
            var town = NewTown();
            Run(town, 3000);

            Assert.Greater(town.Stock.Get(MaterialId.Ale), 0, "the brewery must have produced");
            Assert.Greater(town.Books.Flow(MaterialId.Ale).Produced, 0, "lawful output is declared");

            Assert.Greater(town.Stock.Get(MaterialId.Moonshine), 0, "the still must have produced");
            Assert.AreEqual(0, town.Books.Flow(MaterialId.Moonshine).Produced,
                "contraband output never reaches the books");
            Assert.AreEqual(0, town.Books.Flow(MaterialId.Moonshine).Consumed);
        }

        /// <summary>
        /// The still hides nothing by itself. What it does is decline to write down the grain
        /// it eats, and that missing grain is what the audit finds.
        /// </summary>
        [Test]
        public void GrainEatenByTheStillLeavesAHoleInTheBooks()
        {
            var town = NewTown();
            Run(town, 3000);

            var grain = town.Books.Flow(MaterialId.Grain);
            int expected = grain.ExpectedStock;
            int actual = town.Stock.Get(MaterialId.Grain);

            Assert.Greater(expected, actual,
                "the books should expect more grain on the shelf than the still has left there");
        }

        [Test]
        public void AnInspectorArrivesOnTheTenthDayAndLeavesAgain()
        {
            var town = NewTown();

            Run(town, SimClock.TicksPerDay * 9 + 9 * 60);
            Assert.IsNotNull(town.ActiveInspector, "an inspector is due on day 10");
            Assert.AreEqual(10, town.Clock.DayOfSeason);

            Run(town, 2000);
            Assert.IsNull(town.ActiveInspector, "the visit must finish rather than stall");
            Assert.Greater(town.LastFindings.Count, 0, "an undefended town gives him something to write down");
        }

        [Test]
        public void ASingleVisitToASingleCellarIsNotFatal()
        {
            var town = NewTown();
            Run(town, SimClock.TicksPerDay * 11);

            Assert.Less(town.Suspicion, TownState.AnnexationThreshold,
                "one visit to one cellar must not end the game outright");
            Assert.Greater(town.Suspicion, 0, "but it must not be free either");
        }

        /// <summary>
        /// Regression: eight taps on one chamber used to be eight discoveries, because
        /// discovery was recorded per cell rather than per chamber. Finding any part of a
        /// chamber has to mark the whole connected cavity.
        /// </summary>
        [Test]
        public void FindingOneCellOfAChamberFindsAllOfIt()
        {
            var town = NewTown();

            var origin = new Coord(4, 4, 1);
            for (int dy = 0; dy < 3; dy++)
            for (int dx = 0; dx < 3; dx++)
                town.Map.Excavate(origin.Offset(dx, dy));

            // A second chamber, deliberately not connected to the first.
            var separate = new Coord(20, 20, 1);
            town.Map.Excavate(separate);

            InspectionSystem.MarkChamberDiscovered(town, origin.Offset(1, 1));

            for (int dy = 0; dy < 3; dy++)
            for (int dx = 0; dx < 3; dx++)
                Assert.IsTrue(town.DiscoveredHollows.Contains(origin.Offset(dx, dy)),
                    $"cell {origin.Offset(dx, dy)} belongs to the chamber that was found");

            Assert.IsFalse(town.DiscoveredHollows.Contains(separate),
                "an unconnected chamber elsewhere on the map is still a secret");
        }

        /// <summary>
        /// The consequence of the above, stated the way the player experiences it: one
        /// inspection cannot report the same cellar over and over.
        /// </summary>
        [Test]
        public void OneInspectionReportsAChamberAtMostOnce()
        {
            var town = NewTown();
            Run(town, SimClock.TicksPerDay * 11);

            int discoveries = 0;
            foreach (var finding in town.LastFindings)
                if (finding.StartsWith("hollow ground at")) discoveries++;

            Assert.LessOrEqual(discoveries, 1,
                "the town has exactly one hidden chamber, so a visit can discover at most one");
        }

        /// <summary>
        /// Regression: the audit used to count contraband stored in cellars the inspector
        /// cannot enter, which made hidden storage pointless and the arithmetic explosive.
        /// </summary>
        [Test]
        public void CellarsHideContrabandUpToTheirCapacity()
        {
            var town = NewTown();
            int capacity = town.HiddenStorageCapacity;
            Assert.Greater(capacity, 0, "the inherited works include a cellar store");

            town.Stock.Add(MaterialId.Moonshine, capacity);
            Assert.AreEqual(0, town.VisibleStock(MaterialId.Moonshine),
                "everything inside the cellars is out of the inspector's reach");

            town.Stock.Add(MaterialId.Moonshine, 40);
            Assert.AreEqual(40, town.VisibleStock(MaterialId.Moonshine),
                "what will not fit has to sit where he walks");
        }

        [Test]
        public void LawfulMaterialsAreAlwaysCountable()
        {
            var town = NewTown();
            Assert.AreEqual(town.Stock.Get(MaterialId.Timber), town.VisibleStock(MaterialId.Timber),
                "there is nothing to hide about timber, and no cellar would help if there were");
        }

        [Test]
        public void SuspicionBandsMapToTheirConsequences()
        {
            Assert.AreEqual(SuspicionBand.Unremarkable, TownState.BandFor(0));
            Assert.AreEqual(SuspicionBand.Unremarkable, TownState.BandFor(29));
            Assert.AreEqual(SuspicionBand.Noted, TownState.BandFor(30));
            Assert.AreEqual(SuspicionBand.Fined, TownState.BandFor(50));
            Assert.AreEqual(SuspicionBand.Seized, TownState.BandFor(70));
            Assert.AreEqual(SuspicionBand.Annexed, TownState.BandFor(90));
        }

        [Test]
        public void TheSeasonClockCountsDownToTheRightVisit()
        {
            var clock = new SimClock();
            Assert.AreEqual(10, clock.NextInspectionDay);
            Assert.AreEqual(9, clock.DaysUntilInspection);

            clock.Advance(SimClock.TicksPerDay * 9);
            Assert.AreEqual(10, clock.DayOfSeason);
            Assert.AreEqual(0, clock.DaysUntilInspection);
            Assert.IsTrue(clock.IsInspectionToday);

            clock.Advance(SimClock.TicksPerDay);
            Assert.AreEqual(20, clock.NextInspectionDay);
            Assert.IsFalse(clock.IsInspectionToday);

            clock.Advance(SimClock.TicksPerDay * 19);
            Assert.AreEqual(30, clock.DayOfSeason);
            Assert.IsTrue(SimClock.IsAuditDay(clock.DayOfSeason), "the last day of the season is the full audit");
        }

        private static Coord FindKind(TownState town, BuildingKind kind)
        {
            foreach (var building in town.Buildings)
                if (building.Kind == kind) return building.Origin;
            return new Coord(-1, -1);
        }
    }
}
