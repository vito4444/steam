using System;
using Undertown.Core.Sim;

namespace Undertown.Core.Economy
{
    /// <summary>
    /// The deliberate falsifications a player can make to their own books. Each one closes
    /// a hole by opening a different one, which is the point: there is no entry that makes
    /// a diversion simply go away.
    /// </summary>
    public static class BookCooking
    {
        /// <summary>
        /// Cost in coin to have a clerk write off a unit of stock as spoiled. Cheap enough to
        /// be tempting, dear enough that covering a large diversion is a real expense.
        /// </summary>
        public const int LossDeclarationCostPerUnit = 1;

        /// <summary>
        /// Writes off stock as spoilage. This closes the hole between the books and the
        /// shelves - but a loss rate has to be plausible for the region, and an implausible
        /// one is its own finding. Returns how many units were actually written off.
        /// </summary>
        public static int DeclareSpoilage(TownState town, MaterialId material, int units)
        {
            if (units <= 0 || town.GameOver) return 0;

            int gap = town.LedgerGap(material);
            if (gap <= 0) return 0;

            int affordable = LossDeclarationCostPerUnit > 0
                ? town.Coin / LossDeclarationCostPerUnit
                : units;

            int written = Math.Min(units, Math.Min(gap, affordable));
            if (written <= 0) return 0;

            town.Coin -= written * LossDeclarationCostPerUnit;
            town.Books.DeclareLoss(material, written);
            town.Record($"{written} {Materials.DisplayName(material)} written off as spoiled");
            return written;
        }

        /// <summary>
        /// The largest write-off that still reads as a normal year's losses to the current
        /// inspector. Shown to the player so the decision is "how much can I explain" rather
        /// than "how much dare I guess".
        /// </summary>
        public static int PlausibleSpoilage(TownState town, MaterialId material)
        {
            var flow = town.Books.Flow(material);
            var settings = town.CurrentAuditSettings;

            int permittedRate = settings.LossBaselinePerMille * settings.LossMultiplierPerMille / 1000;
            int ceiling = flow.Inflow * permittedRate / 1000;
            return Math.Max(0, ceiling - flow.DeclaredLoss);
        }

        /// <summary>
        /// Pays a clerk to look at the books less carefully. The bribe eases one rank off the
        /// inspector for the next visit only, and the money has to come from somewhere the
        /// books can explain - so paying it out of black coin is itself a risk.
        /// </summary>
        public const int BribeCost = 220;

        public static bool BribeTheClerk(TownState town)
        {
            if (town.GameOver || town.BriberyActive) return false;

            if (town.Coin >= BribeCost)
            {
                town.Coin -= BribeCost;
                town.BriberyActive = true;
                town.Record("a clerk has been paid to read the next return quickly");
                return true;
            }

            if (town.BlackCoin >= BribeCost)
            {
                town.BlackCoin -= BribeCost;
                town.BriberyActive = true;

                // Lawful coin leaves a trail the books can explain. Black coin does not, and
                // a clerk who takes it knows exactly what he has been paid with.
                town.AddSuspicion(5, "the clerk was paid with money the town cannot account for");
                town.Record("a clerk has been paid, in coin the town should not have");
                return true;
            }

            return false;
        }
    }
}
