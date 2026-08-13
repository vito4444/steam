using System;
using System.Collections.Generic;
using Undertown.Core.Buildings;
using Undertown.Core.Economy;
using Undertown.Core.World;

namespace Undertown.Core.Sim
{
    /// <summary>What the empire did about this town at the end of a season.</summary>
    [Serializable]
    public sealed class SettlementReport
    {
        public int Season;
        public SuspicionBand Band;
        public int TaxDue;
        public int TaxPaid;
        public int Fine;
        public int ChambersSeized;
        public int WorkersArrested;
        public int InspectorLevelAfter;
        public bool TownLost;
        public readonly List<string> Lines = new List<string>();

        /// <summary>
        /// The books as they stood when the season closed, taken before they were rolled
        /// forward. The player needs to be able to look back at the return they filed, and
        /// tests need something to assert against that survives the new period opening.
        /// </summary>
        public LedgerBook ClosingBooks;
    }

    /// <summary>
    /// Closes the season on the thirtieth day: the tax falls due, suspicion is answered for,
    /// and the books are rolled forward. This is where the whole loop pays off - without a
    /// reckoning, suspicion is just a number climbing in the corner of the screen.
    /// </summary>
    public static class SeasonSettlement
    {
        /// <summary>Base tax, plus a levy per building the empire can see.</summary>
        public const int BaseTax = 120;
        public const int TaxPerVisibleBuilding = 15;

        /// <summary>Black market price per unit of contraband when it goes out through the tunnels.</summary>
        public const int ContrabandPrice = 9;
        public const int AlePrice = 3;

        /// <summary>
        /// The last day of the season is itself an inspection day, and the reckoning has to
        /// come after that visit rather than at midnight before it. Settling early meant the
        /// auditor's findings landed on the following season's account, which is both wrong
        /// and unreadable: the player saw suspicion reset and then immediately climb again
        /// for reasons that belonged to a season already closed.
        /// </summary>
        public static bool IsDue(TownState town)
        {
            if (!SimClock.IsAuditDay(town.Clock.DayOfSeason)) return false;
            if (town.LastSettledSeason == town.Clock.Season) return false;
            if (town.LastInspectionDay != town.Clock.TotalDays) return false;
            return town.ActiveInspector == null;
        }

        public static SettlementReport Settle(TownState town)
        {
            town.LastSettledSeason = town.Clock.Season;

            var report = new SettlementReport
            {
                Season = town.Clock.Season + 1,
                Band = town.Band,
            };

            SellGoods(town, report);
            LevyTax(town, report);
            ApplyConsequences(town, report);
            RollForward(town, report);

            town.LastSettlement = report;
            foreach (var line in report.Lines) town.Record(line);
            return report;
        }

        /// <summary>
        /// Lawful goods are sold at the market and the sale is declared. Contraband leaves
        /// through the tunnels at three times the price and is never written down - which is
        /// exactly why its inputs have to be explained some other way.
        /// </summary>
        private static void SellGoods(TownState town, SettlementReport report)
        {
            int ale = town.Stock.TakeUpTo(MaterialId.Ale, town.Stock.Get(MaterialId.Ale));
            if (ale > 0)
            {
                town.Coin += ale * AlePrice;
                town.Books.RecordSale(MaterialId.Ale, ale);
                report.Lines.Add($"sold {ale} ale at market for {ale * AlePrice} coin");
            }

            int moonshine = town.Stock.TakeUpTo(MaterialId.Moonshine, town.Stock.Get(MaterialId.Moonshine));
            if (moonshine > 0)
            {
                town.BlackCoin += moonshine * ContrabandPrice;
                report.Lines.Add($"{moonshine} moonshine went out through the tunnels for {moonshine * ContrabandPrice} in black coin");
            }
        }

        private static void LevyTax(TownState town, SettlementReport report)
        {
            int visible = 0;
            for (int i = 0; i < town.Buildings.Count; i++)
            {
                var def = town.Buildings[i].Def;
                if (def != null && !def.Underground) visible++;
            }

            report.TaxDue = BaseTax + visible * TaxPerVisibleBuilding;

            int fromCoin = Math.Min(town.Coin, report.TaxDue);
            town.Coin -= fromCoin;
            report.TaxPaid = fromCoin;

            int shortfall = report.TaxDue - fromCoin;
            if (shortfall <= 0)
            {
                report.Lines.Add($"tax of {report.TaxDue} coin paid in full");
                return;
            }

            // Paying the tax out of black coin is possible and obvious, which is the point:
            // the empire notices when a struggling town settles its bill anyway.
            int fromBlack = Math.Min(town.BlackCoin, shortfall);
            town.BlackCoin -= fromBlack;
            report.TaxPaid += fromBlack;

            if (fromBlack > 0)
            {
                town.AddSuspicion(6, "the tax was settled with money the town should not have");
                report.Lines.Add($"{fromBlack} of the tax came out of black coin, and it was noticed");
            }

            if (report.TaxPaid < report.TaxDue)
            {
                town.AddSuspicion(10, "the tax went unpaid");
                report.Lines.Add($"tax short by {report.TaxDue - report.TaxPaid} coin");
            }
        }

        private static void ApplyConsequences(TownState town, SettlementReport report)
        {
            switch (report.Band)
            {
                case SuspicionBand.Unremarkable:
                    report.Lines.Add("the season closed without remark");
                    break;

                case SuspicionBand.Noted:
                    town.InspectorLevel = Math.Min(5, town.InspectorLevel + 1);
                    report.Lines.Add("the town is under notice; a harder inspector next season");
                    break;

                case SuspicionBand.Fined:
                    report.Fine = town.BlackCoin * 30 / 100;
                    town.BlackCoin -= report.Fine;
                    town.InspectorLevel = Math.Min(5, town.InspectorLevel + 1);
                    report.Lines.Add($"fined {report.Fine} black coin, and a harder inspector next season");
                    break;

                case SuspicionBand.Seized:
                    report.Fine = town.BlackCoin * 50 / 100;
                    town.BlackCoin -= report.Fine;
                    report.ChambersSeized = SeizeWorks(town, 2);
                    report.WorkersArrested = ArrestWorkers(town, 1);
                    town.InspectorLevel = Math.Min(5, town.InspectorLevel + 1);
                    report.Lines.Add($"seizure: {report.Fine} black coin taken, {report.ChambersSeized} chamber(s) broken open, " +
                                     $"{report.WorkersArrested} arrested");
                    break;

                case SuspicionBand.Annexed:
                    report.TownLost = true;
                    town.GameOver = true;
                    report.Lines.Add("the empire has taken the town");
                    break;
            }
        }

        /// <summary>
        /// Breaks open the works the empire already knows about. Only discovered chambers can
        /// be seized - a cellar nobody ever found is still a secret at the end of the season.
        /// </summary>
        private static int SeizeWorks(TownState town, int limit)
        {
            int seized = 0;
            for (int i = town.Buildings.Count - 1; i >= 0 && seized < limit; i--)
            {
                var building = town.Buildings[i];
                var def = building.Def;
                if (def == null || !def.Illicit) continue;
                if (!town.DiscoveredHollows.Contains(building.Origin)) continue;

                town.Buildings.RemoveAt(i);
                seized++;
            }
            return seized;
        }

        private static int ArrestWorkers(TownState town, int limit)
        {
            int arrested = 0;
            for (int i = town.Villagers.Count - 1; i >= 0 && arrested < limit; i--)
            {
                if (!town.Villagers[i].KnowsAboutTheWorks) continue;
                town.Villagers.RemoveAt(i);
                arrested++;
            }
            return arrested;
        }

        /// <summary>
        /// Opens a fresh set of books against a physical count, and clears the season's
        /// findings. Suspicion carries over at a third: a town that got away with something
        /// is not immediately trusted again, but it is not permanently condemned either.
        /// </summary>
        private static void RollForward(TownState town, SettlementReport report)
        {
            report.InspectorLevelAfter = town.InspectorLevel;
            report.ClosingBooks = town.Books.Clone();

            if (!report.TownLost)
            {
                town.EaseSuspicion(town.Suspicion - town.Suspicion / 3, "a new season, and a new set of books");
                town.Books.BeginPeriod(town.Stock.Get);
                town.SpoilNoticed = false;
                town.LastFindings.Clear();
            }
        }
    }
}
