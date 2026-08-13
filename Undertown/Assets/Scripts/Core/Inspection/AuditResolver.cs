using System;
using System.Collections.Generic;
using Undertown.Core.Economy;

namespace Undertown.Core.Inspection
{
    /// <summary>
    /// How thoroughly a given inspector reads the books. All ratios are per mille (1000 = 100%)
    /// so the whole audit runs on integer arithmetic and stays bit-for-bit reproducible.
    /// </summary>
    [Serializable]
    public struct AuditSettings
    {
        /// <summary>Stock discrepancy, as a share of everything that entered the stores, that gets waved through.</summary>
        public int StockTolerancePerMille;

        /// <summary>Shortfall between recipe-implied output and declared output that gets waved through.</summary>
        public int YieldTolerancePerMille;

        /// <summary>The regional normal loss rate an honest operation would report.</summary>
        public int LossBaselinePerMille;

        /// <summary>How many times the baseline a declared loss rate may reach before it reads as a cover-up.</summary>
        public int LossMultiplierPerMille;

        /// <summary>Suspicion multiplier applied to contraband inputs relative to ordinary stock.</summary>
        public int ContrabandWeightPerMille;

        /// <summary>
        /// Inspectors get better at this job. Level 1 is a bored clerk; level 5 has seen
        /// every trick in this plan and brought a ledger of his own.
        /// </summary>
        public static AuditSettings ForLevel(int level)
        {
            int clamped = level < 1 ? 1 : (level > 5 ? 5 : level);
            switch (clamped)
            {
                case 1: return new AuditSettings { StockTolerancePerMille = 200, YieldTolerancePerMille = 250, LossBaselinePerMille = 50, LossMultiplierPerMille = 3000, ContrabandWeightPerMille = 3000 };
                case 2: return new AuditSettings { StockTolerancePerMille = 160, YieldTolerancePerMille = 200, LossBaselinePerMille = 50, LossMultiplierPerMille = 2500, ContrabandWeightPerMille = 3000 };
                case 3: return new AuditSettings { StockTolerancePerMille = 120, YieldTolerancePerMille = 160, LossBaselinePerMille = 45, LossMultiplierPerMille = 2200, ContrabandWeightPerMille = 3000 };
                case 4: return new AuditSettings { StockTolerancePerMille = 80, YieldTolerancePerMille = 120, LossBaselinePerMille = 40, LossMultiplierPerMille = 1800, ContrabandWeightPerMille = 3000 };
                default: return new AuditSettings { StockTolerancePerMille = 50, YieldTolerancePerMille = 80, LossBaselinePerMille = 35, LossMultiplierPerMille = 1500, ContrabandWeightPerMille = 3000 };
            }
        }
    }

    /// <summary>
    /// A declared production recipe the auditor knows about. If a workshop legally turns
    /// two grain into one ale, then a hundred grain consumed should have produced fifty ale.
    /// This is the check that makes hidden production expensive: concealing the output does
    /// not help, because the missing output is itself the evidence.
    /// </summary>
    [Serializable]
    public struct RecipeExpectation
    {
        public MaterialId Input;
        public MaterialId Output;

        /// <summary>Units of input consumed per unit of output, in per mille. 2000 means two-to-one.</summary>
        public int InputPerOutputPerMille;

        public RecipeExpectation(MaterialId input, MaterialId output, int inputPerOutputPerMille)
        {
            Input = input;
            Output = output;
            InputPerOutputPerMille = inputPerOutputPerMille;
        }
    }

    public enum AuditIssueKind
    {
        StockDiscrepancy,
        YieldShortfall,
        ExcessiveDeclaredLoss,
    }

    [Serializable]
    public struct AuditIssue
    {
        public AuditIssueKind Kind;
        public MaterialId Material;

        /// <summary>The raw gap in whole units, for showing the player the actual number.</summary>
        public int Magnitude;

        /// <summary>How far past the tolerance this went, in per mille.</summary>
        public int ExcessPerMille;

        /// <summary>Suspicion points this issue contributes.</summary>
        public int Suspicion;
    }

    [Serializable]
    public sealed class AuditReport
    {
        public readonly List<AuditIssue> Issues = new List<AuditIssue>();
        public int TotalSuspicion;
        public bool Clean => Issues.Count == 0;
    }

    /// <summary>
    /// The auditor. Reads the books, counts the warehouses, and looks for the three ways
    /// the numbers can fail to describe a lawful town:
    ///
    ///   1. The shelves do not hold what the books say they hold.
    ///   2. Raw material went in and nothing lawful came out.
    ///   3. The losses written off are implausible for this region.
    ///
    /// These three form a pincer. Failing to record a diverted sack of grain breaks (1);
    /// recording it honestly breaks (2); writing it off as spoilage breaks (3). The only
    /// clean way through is to run a legal operation big enough that the diversion hides
    /// inside its normal variance.
    /// </summary>
    public static class AuditResolver
    {
        public static AuditReport Resolve(
            LedgerBook books,
            Func<MaterialId, int> physicalCount,
            IReadOnlyList<RecipeExpectation> recipes,
            AuditSettings settings)
        {
            var report = new AuditReport();

            foreach (var id in Materials.All)
            {
                if (!Materials.IsAudited(id)) continue;
                CheckStockBalance(report, books, physicalCount, settings, id);
                CheckDeclaredLoss(report, books, settings, id);
            }

            for (int i = 0; i < recipes.Count; i++)
            {
                CheckRecipeYield(report, books, settings, recipes[i]);
            }

            for (int i = 0; i < report.Issues.Count; i++) report.TotalSuspicion += report.Issues[i].Suspicion;
            return report;
        }

        private static void CheckStockBalance(
            AuditReport report, LedgerBook books, Func<MaterialId, int> physicalCount,
            AuditSettings settings, MaterialId id)
        {
            var flow = books.Flow(id);
            int gap = Math.Abs(flow.ExpectedStock - physicalCount(id));
            if (gap == 0) return;

            // Measured against everything that entered the stores, not against total ledger
            // activity. Consumption and sales inflate throughput, which would let a town with
            // busy books hide a larger absolute shortfall than a quiet one - the opposite of
            // what an auditor would conclude.
            int denominator = Math.Max(1, flow.Inflow);
            int ratio = gap * 1000 / denominator;
            int excess = ratio - settings.StockTolerancePerMille;
            if (excess <= 0) return;

            report.Issues.Add(new AuditIssue
            {
                Kind = AuditIssueKind.StockDiscrepancy,
                Material = id,
                Magnitude = gap,
                ExcessPerMille = excess,
                Suspicion = Weigh(excess, id, settings),
            });
        }

        private static void CheckRecipeYield(
            AuditReport report, LedgerBook books, AuditSettings settings, RecipeExpectation recipe)
        {
            if (recipe.InputPerOutputPerMille <= 0) return;

            var inputFlow = books.Flow(recipe.Input);
            var outputFlow = books.Flow(recipe.Output);

            // Only material that was actually consumed should have yielded product; grain
            // written off as spoiled never reached the mash tun.
            int effectiveInput = inputFlow.Consumed;
            if (effectiveInput <= 0) return;

            int expectedOutput = effectiveInput * 1000 / recipe.InputPerOutputPerMille;
            int shortfall = expectedOutput - outputFlow.Produced;
            if (shortfall <= 0) return;

            int ratio = shortfall * 1000 / Math.Max(1, expectedOutput);
            int excess = ratio - settings.YieldTolerancePerMille;
            if (excess <= 0) return;

            report.Issues.Add(new AuditIssue
            {
                Kind = AuditIssueKind.YieldShortfall,
                Material = recipe.Input,
                Magnitude = shortfall,
                ExcessPerMille = excess,
                Suspicion = Weigh(excess, recipe.Input, settings),
            });
        }

        private static void CheckDeclaredLoss(
            AuditReport report, LedgerBook books, AuditSettings settings, MaterialId id)
        {
            var flow = books.Flow(id);
            if (flow.DeclaredLoss <= 0) return;

            int inflow = Math.Max(1, flow.Inflow);
            int lossRate = flow.DeclaredLoss * 1000 / inflow;
            int permitted = settings.LossBaselinePerMille * settings.LossMultiplierPerMille / 1000;
            int excess = lossRate - permitted;
            if (excess <= 0) return;

            report.Issues.Add(new AuditIssue
            {
                Kind = AuditIssueKind.ExcessiveDeclaredLoss,
                Material = id,
                Magnitude = flow.DeclaredLoss,
                ExcessPerMille = excess,
                Suspicion = Weigh(excess, id, settings),
            });
        }

        /// <summary>
        /// Ceiling on what any single line of the audit can contribute. Without it, a
        /// material with no ledger activity at all divides by a denominator of one and a
        /// single finding annexes the town outright - which reads as a bug to the player
        /// however defensible the arithmetic is.
        /// </summary>
        public const int MaxSuspicionPerIssue = 25;

        /// <summary>
        /// Converts a per-mille overshoot into suspicion points. Ten per mille of overshoot
        /// is one point, so a discrepancy that doubles the tolerance on an ordinary material
        /// is a nuisance while the same overshoot on contraband is a serious problem.
        /// </summary>
        private static int Weigh(int excessPerMille, MaterialId id, AuditSettings settings)
        {
            int weight = Materials.IsContraband(id) ? settings.ContrabandWeightPerMille : 1000;
            int points = excessPerMille * weight / 1000 / 10;
            if (points < 1) points = 1;
            return points > MaxSuspicionPerIssue ? MaxSuspicionPerIssue : points;
        }
    }
}
