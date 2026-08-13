using NUnit.Framework;
using Undertown.Core.Economy;
using Undertown.Core.Sim;
using Undertown.Core.World;

namespace Undertown.Tests
{
    /// <summary>
    /// The running estimate of what an audit would cost is the player's main instrument.
    /// Its job is to make an abstract threat steerable, so it has to agree with what the
    /// inspector will actually do, and it has to notice the moment that changes.
    /// </summary>
    public class AuditPreviewTests
    {
        private static TownState NewTown()
        {
            var map = MapGenerator.Generate(MapSettings.Default, 20260813);
            var town = new TownState(map, 20260813);
            town.Stock.Add(MaterialId.Grain, 200);
            town.Books.BeginPeriod(town.Stock.Get);
            TownFounder.Found(town);
            return town;
        }

        [Test]
        public void HonestBooksPreviewAsNothingToFind()
        {
            var town = NewTown();
            Assert.AreEqual(0, town.DryRunAudit().TotalSuspicion);
            Assert.AreEqual(0, town.LedgerGap(MaterialId.Grain));
        }

        [Test]
        public void DivertingMaterialShowsUpImmediately()
        {
            var town = NewTown();
            town.Stock.TryTake(MaterialId.Grain, 120);

            Assert.AreEqual(120, town.LedgerGap(MaterialId.Grain),
                "the books still expect grain that is no longer on the shelf");
            Assert.Greater(town.DryRunAudit().TotalSuspicion, 0);
        }

        /// <summary>
        /// The preview and the audit are the same computation. If they ever drift apart the
        /// player is being told one thing and charged for another.
        /// </summary>
        [Test]
        public void ThePreviewIsTheSameVerdictTheInspectorWillReach()
        {
            var town = NewTown();
            town.Stock.TryTake(MaterialId.Grain, 90);

            int previewed = town.DryRunAudit().TotalSuspicion;

            int before = town.Suspicion;
            RunUntilAuditDone(town);

            Assert.AreEqual(previewed, town.Suspicion - before,
                "what the preview promised is what the audit charged");
        }

        [Test]
        public void TheEstimateTracksTheInspectorsRank()
        {
            var town = NewTown();
            town.Stock.TryTake(MaterialId.Grain, 60);

            town.InspectorLevel = 1;
            int lenient = town.DryRunAudit().TotalSuspicion;

            town.InspectorLevel = 5;
            int strict = town.DryRunAudit().TotalSuspicion;

            Assert.Greater(strict, lenient, "the same books read worse to a better inspector");
        }

        /// <summary>
        /// Revision counters are what let the interface recompute exactly when the answer
        /// changes. Recomputing on a timer instead allowed the estimate to disagree with the
        /// ledger rows printed directly above it.
        /// </summary>
        [Test]
        public void BooksAndStoresReportWhenTheyChange()
        {
            var town = NewTown();

            int books = town.Books.Revision;
            int stock = town.Stock.Revision;

            town.Books.RecordProduction(MaterialId.Ale, 5);
            Assert.Greater(town.Books.Revision, books);

            town.Stock.Add(MaterialId.Ale, 5);
            Assert.Greater(town.Stock.Revision, stock);

            int quiet = town.Stock.Revision;
            town.Stock.TryTake(MaterialId.Ale, 9999);
            Assert.AreEqual(quiet, town.Stock.Revision, "a refused withdrawal changes nothing");
        }

        [Test]
        public void CellaredContrabandIsNotCountedButIsStillHeld()
        {
            var town = NewTown();
            int capacity = town.HiddenStorageCapacity;
            town.Stock.Add(MaterialId.Moonshine, capacity);

            Assert.AreEqual(capacity, town.Stock.Get(MaterialId.Moonshine), "the town still has it");
            Assert.AreEqual(0, town.VisibleStock(MaterialId.Moonshine), "the inspector cannot count it");
            Assert.AreEqual(0, town.DryRunAudit().TotalSuspicion, "and so it costs nothing at audit");
        }

        private static void RunUntilAuditDone(TownState town)
        {
            const int chunk = 15;
            int minutes = SimClock.TicksPerDay * 11;
            for (int elapsed = 0; elapsed < minutes; elapsed += chunk)
            {
                int step = System.Math.Min(chunk, minutes - elapsed);
                town.Clock.Advance(step);
                InspectionSystem.Tick(town, step);
            }
        }
    }
}
