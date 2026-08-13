using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Undertown.Core.Economy;
using Undertown.Core.Inspection;

namespace Undertown.Tests
{
    /// <summary>
    /// The audit is the differentiating mechanic, so these tests assert the design intent
    /// and not merely the arithmetic. The intent is a pincer: every way of concealing
    /// diverted material breaks one of the three checks, and the only clean escape is to
    /// hide the diversion inside a large enough lawful operation.
    /// </summary>
    public class AuditResolverTests
    {
        // Two grain make one ale. This is the recipe the empire has on file.
        private static readonly RecipeExpectation[] Recipes =
        {
            new RecipeExpectation(MaterialId.Grain, MaterialId.Ale, 2000),
        };

        private static AuditReport Audit(LedgerBook books, Dictionary<MaterialId, int> stock, int level = 3)
        {
            return AuditResolver.Resolve(
                books,
                id => stock.TryGetValue(id, out var n) ? n : 0,
                Recipes,
                AuditSettings.ForLevel(level));
        }

        private static bool Has(AuditReport report, AuditIssueKind kind, MaterialId material) =>
            report.Issues.Any(i => i.Kind == kind && i.Material == material);

        [Test]
        public void HonestBooksPassCleanly()
        {
            var books = new LedgerBook();
            books.RecordPurchase(MaterialId.Grain, 200);
            books.RecordConsumption(MaterialId.Grain, 200);
            books.RecordProduction(MaterialId.Ale, 100);

            var stock = new Dictionary<MaterialId, int> { { MaterialId.Grain, 0 }, { MaterialId.Ale, 100 } };

            var report = Audit(books, stock);

            Assert.IsTrue(report.Clean, "an operation whose books describe what actually happened has nothing to find");
            Assert.AreEqual(0, report.TotalSuspicion);
        }

        [Test]
        public void StockDiscrepancyInsideToleranceIsWavedThrough()
        {
            // Level 3 tolerates 80 per mille of throughput. Throughput is 400, so a gap of 30 is 75 per mille.
            var books = new LedgerBook();
            books.RecordPurchase(MaterialId.Grain, 200);
            books.RecordConsumption(MaterialId.Grain, 200);
            books.RecordProduction(MaterialId.Ale, 100);

            var stock = new Dictionary<MaterialId, int> { { MaterialId.Grain, 30 }, { MaterialId.Ale, 100 } };

            var report = Audit(books, stock);

            Assert.IsFalse(Has(report, AuditIssueKind.StockDiscrepancy, MaterialId.Grain),
                "75 per mille sits under the level 3 tolerance of 80 and must not be flagged");
        }

        [Test]
        public void StockDiscrepancyBeyondToleranceIsFlagged()
        {
            var books = new LedgerBook();
            books.RecordPurchase(MaterialId.Grain, 200);
            books.RecordConsumption(MaterialId.Grain, 200);
            books.RecordProduction(MaterialId.Ale, 100);

            // 60 units unaccounted for on a throughput of 400 is 150 per mille, comfortably past 80.
            var stock = new Dictionary<MaterialId, int> { { MaterialId.Grain, 60 }, { MaterialId.Ale, 100 } };

            var report = Audit(books, stock);

            Assert.IsTrue(Has(report, AuditIssueKind.StockDiscrepancy, MaterialId.Grain));
            Assert.Greater(report.TotalSuspicion, 0);
        }

        [Test]
        public void GrainConsumedWithoutMatchingAleIsFlaggedAsYieldShortfall()
        {
            // The player brewed only 40 ale but burned through 200 grain; 60 units of grain
            // went somewhere the books do not explain.
            var books = new LedgerBook();
            books.RecordPurchase(MaterialId.Grain, 200);
            books.RecordConsumption(MaterialId.Grain, 200);
            books.RecordProduction(MaterialId.Ale, 40);

            var stock = new Dictionary<MaterialId, int> { { MaterialId.Grain, 0 }, { MaterialId.Ale, 40 } };

            var report = Audit(books, stock);

            Assert.IsTrue(Has(report, AuditIssueKind.YieldShortfall, MaterialId.Grain),
                "200 grain implies 100 ale; declaring 40 is a 600 per mille shortfall");
        }

        [Test]
        public void ImplausibleDeclaredLossIsFlagged()
        {
            // Level 3 permits a loss rate of 45 per mille times 2.2, which is 99 per mille.
            var books = new LedgerBook();
            books.RecordPurchase(MaterialId.Grain, 200);
            books.RecordConsumption(MaterialId.Grain, 140);
            books.RecordProduction(MaterialId.Ale, 70);
            books.SetDeclaredLoss(MaterialId.Grain, 60); // 300 per mille of inflow

            var stock = new Dictionary<MaterialId, int> { { MaterialId.Grain, 0 }, { MaterialId.Ale, 70 } };

            var report = Audit(books, stock);

            Assert.IsTrue(Has(report, AuditIssueKind.ExcessiveDeclaredLoss, MaterialId.Grain),
                "writing off three tenths of the grain supply as spoilage is not a regional norm");
        }

        /// <summary>
        /// The central claim of the design: there is no free way to divert material. This
        /// test walks the same 60 diverted grain through all three cover stories and asserts
        /// each one trips a different check.
        /// </summary>
        [Test]
        public void EveryCoverStoryForDivertedGrainTripsSomething()
        {
            const int diverted = 60;

            // Story one: say nothing. The grain is gone from the shelf but the books still expect it.
            var silent = new LedgerBook();
            silent.RecordPurchase(MaterialId.Grain, 200);
            silent.RecordConsumption(MaterialId.Grain, 140);
            silent.RecordProduction(MaterialId.Ale, 70);
            var silentStock = new Dictionary<MaterialId, int> { { MaterialId.Grain, 0 }, { MaterialId.Ale, 70 } };
            var silentReport = Audit(silent, silentStock);
            Assert.IsTrue(Has(silentReport, AuditIssueKind.StockDiscrepancy, MaterialId.Grain),
                "hiding the diversion leaves the shelf short of what the books promise");

            // Story two: record the consumption honestly. Now the grain went in and no ale came out.
            var honest = new LedgerBook();
            honest.RecordPurchase(MaterialId.Grain, 200);
            honest.RecordConsumption(MaterialId.Grain, 200);
            honest.RecordProduction(MaterialId.Ale, 70);
            var honestStock = new Dictionary<MaterialId, int> { { MaterialId.Grain, 0 }, { MaterialId.Ale, 70 } };
            var honestReport = Audit(honest, honestStock);
            Assert.IsTrue(Has(honestReport, AuditIssueKind.YieldShortfall, MaterialId.Grain),
                "recording the diversion as consumption leaves the yield unexplained");

            // Story three: write it off as spoilage. The loss rate stops looking like a farm.
            var spoiled = new LedgerBook();
            spoiled.RecordPurchase(MaterialId.Grain, 200);
            spoiled.RecordConsumption(MaterialId.Grain, 140);
            spoiled.RecordProduction(MaterialId.Ale, 70);
            spoiled.SetDeclaredLoss(MaterialId.Grain, diverted);
            var spoiledStock = new Dictionary<MaterialId, int> { { MaterialId.Grain, 0 }, { MaterialId.Ale, 70 } };
            var spoiledReport = Audit(spoiled, spoiledStock);
            Assert.IsTrue(Has(spoiledReport, AuditIssueKind.ExcessiveDeclaredLoss, MaterialId.Grain),
                "writing the diversion off as spoilage produces an implausible loss rate");
        }

        /// <summary>
        /// The escape hatch, and the reason legal production is worth building: the same
        /// absolute diversion disappears into the variance of a large enough lawful operation.
        /// This is what caps contraband output at a fraction of legitimate capacity.
        /// </summary>
        [Test]
        public void LargeLawfulOperationHidesTheSameDiversion()
        {
            const int diverted = 60;

            var small = new LedgerBook();
            small.RecordPurchase(MaterialId.Grain, 200);
            small.RecordConsumption(MaterialId.Grain, 200);
            small.RecordProduction(MaterialId.Ale, (200 - diverted) / 2);
            var smallStock = new Dictionary<MaterialId, int> { { MaterialId.Grain, 0 }, { MaterialId.Ale, 70 } };
            Assert.IsTrue(Has(Audit(small, smallStock), AuditIssueKind.YieldShortfall, MaterialId.Grain),
                "a small brewery cannot absorb 60 grain of shrinkage");

            var large = new LedgerBook();
            large.RecordPurchase(MaterialId.Grain, 1600);
            large.RecordConsumption(MaterialId.Grain, 1600);
            large.RecordProduction(MaterialId.Ale, (1600 - diverted) / 2);
            var largeStock = new Dictionary<MaterialId, int> { { MaterialId.Grain, 0 }, { MaterialId.Ale, 770 } };
            Assert.IsFalse(Has(Audit(large, largeStock), AuditIssueKind.YieldShortfall, MaterialId.Grain),
                "the same 60 grain is ordinary variance for a brewery eight times the size");
        }

        [Test]
        public void HarsherInspectorsCatchWhatLenientOnesMiss()
        {
            var books = new LedgerBook();
            books.RecordPurchase(MaterialId.Grain, 200);
            books.RecordConsumption(MaterialId.Grain, 200);
            books.RecordProduction(MaterialId.Ale, 88); // 120 per mille shortfall
            var stock = new Dictionary<MaterialId, int> { { MaterialId.Grain, 0 }, { MaterialId.Ale, 88 } };

            Assert.IsFalse(Has(Audit(books, stock, level: 1), AuditIssueKind.YieldShortfall, MaterialId.Grain),
                "a level 1 clerk tolerates 250 per mille");
            Assert.IsTrue(Has(Audit(books, stock, level: 5), AuditIssueKind.YieldShortfall, MaterialId.Grain),
                "a level 5 inspector tolerates only 80 per mille");
        }

        [Test]
        public void ContrabandDiscrepanciesWeighTripleOrdinaryStock()
        {
            var ordinary = new LedgerBook();
            ordinary.RecordPurchase(MaterialId.Timber, 200);
            ordinary.RecordConsumption(MaterialId.Timber, 100);
            var ordinaryStock = new Dictionary<MaterialId, int> { { MaterialId.Timber, 40 } };

            var contraband = new LedgerBook();
            contraband.RecordPurchase(MaterialId.Moonshine, 200);
            contraband.RecordConsumption(MaterialId.Moonshine, 100);
            var contrabandStock = new Dictionary<MaterialId, int> { { MaterialId.Moonshine, 40 } };

            int ordinarySuspicion = Audit(ordinary, ordinaryStock).TotalSuspicion;
            int contrabandSuspicion = Audit(contraband, contrabandStock).TotalSuspicion;

            Assert.Greater(ordinarySuspicion, 0, "the fixture must actually produce a discrepancy");
            Assert.AreEqual(ordinarySuspicion * 3, contrabandSuspicion,
                "contraband carries a threefold weight");
        }

        [Test]
        public void SpoilIsNeverAudited()
        {
            // Spoil is dirt from digging. It appears on no tax return, so the auditor has
            // nothing to balance it against - the exposure it creates is visual, not clerical.
            var books = new LedgerBook();
            books.RecordProduction(MaterialId.Spoil, 500);
            var stock = new Dictionary<MaterialId, int> { { MaterialId.Spoil, 0 } };

            var report = Audit(books, stock);

            Assert.IsFalse(report.Issues.Any(i => i.Material == MaterialId.Spoil));
        }
    }
}
