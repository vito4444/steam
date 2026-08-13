using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Undertown.Core.Economy;
using Undertown.Core.Inspection;
using Undertown.Core.Sim;

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
            // Discrepancies are measured against everything that entered the stores. Grain
            // inflow here is 200, and level 3 tolerates 120 per mille, so a gap of 20 is 100
            // per mille and passes.
            var books = new LedgerBook();
            books.RecordPurchase(MaterialId.Grain, 200);
            books.RecordConsumption(MaterialId.Grain, 200);
            books.RecordProduction(MaterialId.Ale, 100);

            var stock = new Dictionary<MaterialId, int> { { MaterialId.Grain, 20 }, { MaterialId.Ale, 100 } };

            var report = Audit(books, stock);

            Assert.IsFalse(Has(report, AuditIssueKind.StockDiscrepancy, MaterialId.Grain),
                "100 per mille sits under the level 3 tolerance of 120 and must not be flagged");
        }

        [Test]
        public void StockDiscrepancyBeyondToleranceIsFlagged()
        {
            var books = new LedgerBook();
            books.RecordPurchase(MaterialId.Grain, 200);
            books.RecordConsumption(MaterialId.Grain, 200);
            books.RecordProduction(MaterialId.Ale, 100);

            // 60 units unaccounted for on an inflow of 200 is 300 per mille, well past 120.
            var stock = new Dictionary<MaterialId, int> { { MaterialId.Grain, 60 }, { MaterialId.Ale, 100 } };

            var report = Audit(books, stock);

            Assert.IsTrue(Has(report, AuditIssueKind.StockDiscrepancy, MaterialId.Grain));
            Assert.Greater(report.TotalSuspicion, 0);
        }

        /// <summary>
        /// The measure has to be inflow rather than total ledger activity, otherwise a town
        /// with busy books could hide a larger absolute shortfall than a quiet one - which is
        /// the opposite of what an auditor would conclude from the same two numbers.
        /// </summary>
        [Test]
        public void BusyBooksDoNotDiluteAShortfall()
        {
            const int shortfall = 60;

            var quiet = new LedgerBook();
            quiet.RecordPurchase(MaterialId.Grain, 200);
            var quietStock = new Dictionary<MaterialId, int> { { MaterialId.Grain, 200 - shortfall } };

            var busy = new LedgerBook();
            busy.RecordPurchase(MaterialId.Grain, 200);
            busy.RecordConsumption(MaterialId.Grain, 180);
            busy.RecordProduction(MaterialId.Ale, 90);
            busy.RecordSale(MaterialId.Ale, 90);
            // The busy books expect 20 grain left (200 in, 180 consumed); an extra 60 on the
            // shelf is the same absolute discrepancy as the quiet town's missing 60.
            var busyStock = new Dictionary<MaterialId, int> { { MaterialId.Grain, 20 + shortfall } };

            int quietSuspicion = Audit(quiet, quietStock).TotalSuspicion;
            int busySuspicion = Audit(busy, busyStock).TotalSuspicion;

            Assert.Greater(quietSuspicion, 0, "the fixture must produce a discrepancy to compare");
            Assert.AreEqual(quietSuspicion, busySuspicion,
                "the same absolute shortfall on the same inflow must weigh the same either way");
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
            // Sized so the tripled figure still lands under the per-issue ceiling, otherwise
            // the ratio being tested is hidden by the clamp.
            var ordinary = new LedgerBook();
            ordinary.RecordPurchase(MaterialId.Timber, 200);
            ordinary.RecordConsumption(MaterialId.Timber, 100);
            var ordinaryStock = new Dictionary<MaterialId, int> { { MaterialId.Timber, 60 } };

            var contraband = new LedgerBook();
            contraband.RecordPurchase(MaterialId.Moonshine, 200);
            contraband.RecordConsumption(MaterialId.Moonshine, 100);
            var contrabandStock = new Dictionary<MaterialId, int> { { MaterialId.Moonshine, 60 } };

            int ordinarySuspicion = Audit(ordinary, ordinaryStock).TotalSuspicion;
            int contrabandSuspicion = Audit(contraband, contrabandStock).TotalSuspicion;

            Assert.Greater(ordinarySuspicion, 0, "the fixture must actually produce a discrepancy");
            Assert.LessOrEqual(ordinarySuspicion * 3, AuditResolver.SuspicionSoftCap,
                "the fixture must stay below the soft cap for the ratio to be observable");
            Assert.AreEqual(ordinarySuspicion * 3, contrabandSuspicion,
                "contraband carries a threefold weight");
        }

        /// <summary>
        /// A material with no ledger activity divides by a denominator of one, which without
        /// a ceiling turns one finding into an instant loss. The ceiling is what keeps a
        /// severe finding severe rather than terminal.
        /// </summary>
        [Test]
        public void NoSingleFindingCanAnnexTheTownOutright()
        {
            var books = new LedgerBook();
            var stock = new Dictionary<MaterialId, int> { { MaterialId.Moonshine, 5000 } };

            var report = Audit(books, stock, level: 5);

            Assert.AreEqual(1, report.Issues.Count);
            Assert.AreEqual(AuditResolver.MaxSuspicionPerIssue, report.TotalSuspicion);
            Assert.Less(report.TotalSuspicion, TownState.AnnexationThreshold,
                "the worst possible single line still leaves the town standing");
        }

        /// <summary>
        /// Severity has to stay monotonic past the soft cap. A hard clamp made a severe
        /// finding indistinguishable from a catastrophic one, which silently broke the
        /// player's countermeasures: closing part of a large shortfall changed nothing on
        /// screen, so a manoeuvre that genuinely helped looked useless.
        /// </summary>
        [Test]
        public void ReducingALargeShortfallAlwaysReducesTheFinding()
        {
            int previous = int.MaxValue;

            for (int missing = 380; missing >= 60; missing -= 40)
            {
                var books = new LedgerBook();
                books.RecordPurchase(MaterialId.Grain, 400);
                var stock = new Dictionary<MaterialId, int> { { MaterialId.Grain, 400 - missing } };

                int suspicion = Audit(books, stock, level: 3).TotalSuspicion;
                Assert.Less(suspicion, previous,
                    $"putting {missing} back on the shelf has to show up in the verdict");
                previous = suspicion;
            }
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
