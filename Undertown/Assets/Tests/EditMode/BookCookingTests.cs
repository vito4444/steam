using NUnit.Framework;
using Undertown.Core.Economy;
using Undertown.Core.Inspection;
using Undertown.Core.Sim;
using Undertown.Core.World;

namespace Undertown.Tests
{
    /// <summary>
    /// The deliberate falsifications. Each one has to close a hole by opening a different
    /// one, otherwise it is not a decision - it is just a button that makes the problem go
    /// away, and the whole tension of the game with it.
    /// </summary>
    public class BookCookingTests
    {
        private static TownState NewTown(int coin = 2000)
        {
            var map = MapGenerator.Generate(MapSettings.Default, 20260813);
            var town = new TownState(map, 20260813);
            town.Stock.Add(MaterialId.Grain, 400);
            town.Coin = coin;
            town.Books.BeginPeriod(town.Stock.Get);
            TownFounder.Found(town);
            return town;
        }

        [Test]
        public void WritingOffSpoilageClosesTheGapItIsWrittenAgainst()
        {
            var town = NewTown();
            town.Stock.TryTake(MaterialId.Grain, 40);
            Assert.AreEqual(40, town.LedgerGap(MaterialId.Grain));

            int written = BookCooking.DeclareSpoilage(town, MaterialId.Grain, 40);

            Assert.AreEqual(40, written);
            Assert.AreEqual(0, town.LedgerGap(MaterialId.Grain), "the books and the shelves agree again");
        }

        [Test]
        public void SpoilageCannotBeWrittenAgainstAGapThatDoesNotExist()
        {
            var town = NewTown();
            Assert.AreEqual(0, BookCooking.DeclareSpoilage(town, MaterialId.Grain, 50),
                "there is nothing to explain");
        }

        [Test]
        public void WritingOffCostsCoinAndIsLimitedByIt()
        {
            var town = NewTown(coin: 12);
            town.Stock.TryTake(MaterialId.Grain, 80);

            int written = BookCooking.DeclareSpoilage(town, MaterialId.Grain, 80);

            Assert.AreEqual(12, written, "a clerk writes only as many lines as he is paid for");
            Assert.AreEqual(0, town.Coin);
        }

        /// <summary>
        /// The catch. A write-off closes the stock gap, but a loss rate has to look like a
        /// normal year for the region, and one that does not is a finding in its own right.
        /// </summary>
        [Test]
        public void WritingOffTooMuchTradesOneFindingForAnother()
        {
            var town = NewTown();
            town.Stock.TryTake(MaterialId.Grain, 300);

            var before = town.DryRunAudit();
            Assert.IsTrue(HasKind(before, AuditIssueKind.StockDiscrepancy), "the shelf is short");

            BookCooking.DeclareSpoilage(town, MaterialId.Grain, 300);

            var after = town.DryRunAudit();
            Assert.IsFalse(HasKind(after, AuditIssueKind.StockDiscrepancy), "the shelf now balances");
            Assert.IsTrue(HasKind(after, AuditIssueKind.ExcessiveDeclaredLoss),
                "but three quarters of the grain supply spoiling is not a normal year");
        }

        /// <summary>
        /// And the reason the interface offers the plausible figure rather than the whole
        /// gap: written off at that size, the same manoeuvre is genuinely worth making.
        /// </summary>
        [Test]
        public void APlausibleWriteOffIsWorthMaking()
        {
            var town = NewTown();
            town.Stock.TryTake(MaterialId.Grain, 300);

            int before = town.DryRunAudit().TotalSuspicion;
            int plausible = BookCooking.PlausibleSpoilage(town, MaterialId.Grain);
            Assert.Greater(plausible, 0);

            BookCooking.DeclareSpoilage(town, MaterialId.Grain, plausible);

            var after = town.DryRunAudit();
            Assert.Less(after.TotalSuspicion, before, "the exposure went down");
            Assert.IsFalse(HasKind(after, AuditIssueKind.ExcessiveDeclaredLoss),
                "and stayed inside what the region's losses can explain");
        }

        [Test]
        public void ThePlausibleCeilingTightensAsInspectorsImprove()
        {
            var town = NewTown();
            town.Stock.TryTake(MaterialId.Grain, 300);

            town.InspectorLevel = 1;
            int lenient = BookCooking.PlausibleSpoilage(town, MaterialId.Grain);

            town.InspectorLevel = 5;
            int strict = BookCooking.PlausibleSpoilage(town, MaterialId.Grain);

            Assert.Greater(lenient, strict, "a better inspector believes less spoilage");
        }

        [Test]
        public void ABribeEasesTheNextAuditByOneRank()
        {
            var town = NewTown();
            town.InspectorLevel = 4;
            town.Stock.TryTake(MaterialId.Grain, 90);

            int unbribed = town.DryRunAudit().TotalSuspicion;

            Assert.IsTrue(BookCooking.BribeTheClerk(town));
            int bribed = town.DryRunAudit().TotalSuspicion;

            Assert.Less(bribed, unbribed, "the clerk reads the same books more kindly");
        }

        [Test]
        public void ABribeIsSpentOnTheVisitItBuys()
        {
            var town = NewTown();
            town.InspectorLevel = 3;
            BookCooking.BribeTheClerk(town);
            Assert.IsTrue(town.BriberyActive);

            RunOneInspection(town);

            Assert.IsFalse(town.BriberyActive, "he will not be careless twice");
        }

        /// <summary>
        /// Paying with money the books cannot explain is itself evidence, so the cheap route
        /// to a bribe carries its own cost.
        /// </summary>
        [Test]
        public void PayingABribeInBlackCoinIsNoticed()
        {
            var lawful = NewTown(coin: BookCooking.BribeCost);
            lawful.InspectorLevel = 3;
            BookCooking.BribeTheClerk(lawful);
            Assert.AreEqual(0, lawful.Suspicion);

            var illicit = NewTown(coin: 0);
            illicit.InspectorLevel = 3;
            illicit.BlackCoin = BookCooking.BribeCost;
            BookCooking.BribeTheClerk(illicit);

            Assert.Greater(illicit.Suspicion, 0, "the clerk knows what he was handed");
        }

        [Test]
        public void ABribeCannotBeStacked()
        {
            var town = NewTown(coin: BookCooking.BribeCost * 4);
            town.InspectorLevel = 4;

            Assert.IsTrue(BookCooking.BribeTheClerk(town));
            Assert.IsFalse(BookCooking.BribeTheClerk(town), "one careless reading, not a standing arrangement");
        }

        private static bool HasKind(AuditReport report, AuditIssueKind kind)
        {
            foreach (var issue in report.Issues)
                if (issue.Kind == kind) return true;
            return false;
        }

        private static void RunOneInspection(TownState town)
        {
            const int chunk = 15;
            while (town.Clock.DayOfSeason != 10) town.Clock.Advance(SimClock.TicksPerDay);
            for (int i = 0; i < 2500; i += chunk)
            {
                town.Clock.Advance(chunk);
                InspectionSystem.Tick(town, chunk);
            }
        }
    }
}
