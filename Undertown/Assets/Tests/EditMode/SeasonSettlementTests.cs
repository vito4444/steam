using NUnit.Framework;
using Undertown.Core.Buildings;
using Undertown.Core.Economy;
using Undertown.Core.Sim;
using Undertown.Core.World;

namespace Undertown.Tests
{
    /// <summary>
    /// The season's reckoning is where the loop pays off. Without it, suspicion is a number
    /// climbing in the corner of the screen with nothing at the end of it.
    /// </summary>
    public class SeasonSettlementTests
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

        private static void AdvanceToAuditDay(TownState town)
        {
            town.Clock.Advance(SimClock.TicksPerDay * (SimClock.DaysPerSeason - 1));

            // The reckoning waits for that day's auditor to have been and gone. Tests that
            // call Settle directly stand in for a visit that already finished.
            town.LastInspectionDay = town.Clock.TotalDays;
        }

        [Test]
        public void SettlementIsOnlyDueOnTheLastDayOfTheSeason()
        {
            var town = NewTown();
            Assert.IsFalse(SeasonSettlement.IsDue(town), "day one is not the reckoning");

            AdvanceToAuditDay(town);
            Assert.AreEqual(30, town.Clock.DayOfSeason);
            Assert.IsTrue(SeasonSettlement.IsDue(town));

            SeasonSettlement.Settle(town);
            Assert.IsFalse(SeasonSettlement.IsDue(town), "a season is settled once, not once per tick");
        }

        /// <summary>
        /// Regression: the reckoning used to fire at midnight on day thirty, before that
        /// day's auditor arrived, so his findings were charged to the season that followed.
        /// </summary>
        [Test]
        public void TheReckoningWaitsForTheSeasonsAuditor()
        {
            var town = NewTown();
            town.Clock.Advance(SimClock.TicksPerDay * (SimClock.DaysPerSeason - 1));

            Assert.AreEqual(30, town.Clock.DayOfSeason);
            Assert.IsFalse(SeasonSettlement.IsDue(town),
                "nobody has audited anything yet on the morning of day thirty");

            town.LastInspectionDay = town.Clock.TotalDays;
            town.ActiveInspector = new Undertown.Core.Agents.Inspector(1, new Coord(0, 0));
            Assert.IsFalse(SeasonSettlement.IsDue(town), "not while he is still walking the town");

            town.ActiveInspector = null;
            Assert.IsTrue(SeasonSettlement.IsDue(town), "once he has gone, the season can close");
        }

        /// <summary>
        /// The same regression stated end to end: whatever the day-thirty audit finds has to
        /// be answered for in the season it belongs to, not carried into the next one.
        /// </summary>
        [Test]
        public void FindingsFromTheFinalAuditAreAnsweredForInTheSameSeason()
        {
            var town = NewTown();
            PlaySeason(town);

            Assert.IsNotNull(town.LastSettlement, "a season must have closed by the start of the next one");
            Assert.AreEqual(1, town.LastSettlement.Season);
            Assert.AreEqual(0, town.LastSettledSeason);
            Assert.IsFalse(town.GameOver, "a season run lawfully must not end the game");

            int settledSuspicion = town.Suspicion;
            Assert.LessOrEqual(settledSuspicion, 34,
                "carrying over a third of a full meter is 33; anything above that is a later " +
                $"season being charged for the one that just closed. Actual {settledSuspicion}. " +
                $"Settlement lines: {string.Join(" | ", town.LastSettlement.Lines)}");
        }

        /// <summary>
        /// The still starts idle, so a player who touches nothing runs a lawful town and
        /// survives their first season. Learning what an audit is should not require having
        /// already lost to one.
        /// </summary>
        [Test]
        public void ATownLeftAloneSurvivesItsFirstSeason()
        {
            var town = NewTown();

            foreach (var building in town.Buildings)
                if (building.Def != null && building.Def.Illicit)
                    Assert.IsFalse(building.Working, $"{building.Kind} must start stood down");

            PlaySeason(town);

            Assert.IsFalse(town.GameOver);
            Assert.AreEqual(0, town.Stock.Get(MaterialId.Moonshine), "an idle still produces nothing");
        }

        /// <summary>
        /// And the other half of that bargain: start the still and the season's audit has
        /// something to find. Without this the idle default would just be a way to make the
        /// game safe rather than a decision with two real sides.
        /// </summary>
        [Test]
        public void StartingTheStillMakesTheSeasonDangerous()
        {
            var quiet = NewTown();
            PlaySeason(quiet);

            var running = NewTown();
            foreach (var building in running.Buildings)
                if (building.Def != null && building.Def.Illicit && building.Def.WorkerSlots > 0)
                    building.ToggleWork();
            PlaySeason(running);

            Assert.Greater(running.LastSettlement.Season, 0);
            Assert.Greater(running.BlackCoin, quiet.BlackCoin, "the risk has to pay something");

            bool worseOff = running.GameOver
                            || running.Suspicion > quiet.Suspicion
                            || running.InspectorLevel > quiet.InspectorLevel;
            Assert.IsTrue(worseOff, "and it has to cost something too");
        }

        private static void PlaySeason(TownState town)
        {
            const int chunk = 15;
            int minutes = SimClock.TicksPerDay * SimClock.DaysPerSeason;
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
        public void ContrabandSellsForFarMoreThanAleAndNeverReachesTheBooks()
        {
            var town = NewTown();
            town.Stock.Add(MaterialId.Ale, 100);
            town.Stock.Add(MaterialId.Moonshine, 100);
            AdvanceToAuditDay(town);

            var report = SeasonSettlement.Settle(town);

            Assert.AreEqual(0, town.Stock.Get(MaterialId.Moonshine), "the contraband went out through the tunnels");
            Assert.AreEqual(100 * SeasonSettlement.ContrabandPrice, town.BlackCoin);
            Assert.Greater(SeasonSettlement.ContrabandPrice, SeasonSettlement.AlePrice,
                "the risk has to be worth taking");

            // Checked against the closing books rather than the live ones, which have already
            // been rolled forward into the new season by the time Settle returns.
            Assert.AreEqual(100, report.ClosingBooks.Flow(MaterialId.Ale).Sold, "the lawful sale is declared");
            Assert.AreEqual(0, report.ClosingBooks.Flow(MaterialId.Moonshine).Sold, "the unlawful one is not");
        }

        [Test]
        public void PayingTheTaxWithBlackCoinIsNoticed()
        {
            var town = NewTown();
            town.Coin = 0;
            town.BlackCoin = 5000;
            AdvanceToAuditDay(town);

            int before = town.Suspicion;
            var report = SeasonSettlement.Settle(town);

            Assert.AreEqual(report.TaxDue, report.TaxPaid, "a town with black coin can always pay");
            Assert.Greater(town.Suspicion, before,
                "settling a poor town's bill in full is itself a thing worth noticing");
        }

        [Test]
        public void AnUnpaidTaxRaisesSuspicion()
        {
            var town = NewTown();
            town.Coin = 0;
            town.BlackCoin = 0;
            AdvanceToAuditDay(town);

            var report = SeasonSettlement.Settle(town);

            Assert.Less(report.TaxPaid, report.TaxDue);
            Assert.Greater(town.Suspicion, 0);
        }

        [Test]
        public void EachSuspicionBandHasADistinctConsequence()
        {
            Assert.AreEqual(SuspicionBand.Unremarkable, Settle(0).Band);
            Assert.AreEqual(1, InspectorLevelAfter(0), "a clean season does not sharpen the empire's attention");

            Assert.AreEqual(2, InspectorLevelAfter(35), "being under notice means a harder inspector");

            var fined = Settle(55);
            Assert.AreEqual(SuspicionBand.Fined, fined.Band);
            Assert.Greater(fined.Fine, 0);

            var seized = Settle(75);
            Assert.AreEqual(SuspicionBand.Seized, seized.Band);
            Assert.Greater(seized.Fine, 0);

            var annexed = Settle(95);
            Assert.IsTrue(annexed.TownLost);
        }

        [Test]
        public void OnlyDiscoveredChambersCanBeSeized()
        {
            var town = NewTown();
            town.AddSuspicion(75, "test fixture");
            town.BlackCoin = 1000;
            AdvanceToAuditDay(town);

            int illicitBefore = CountIllicit(town);
            var untouched = SeasonSettlement.Settle(town);

            Assert.AreEqual(0, untouched.ChambersSeized,
                "a cellar nobody ever found is still a secret at the end of the season");
            Assert.AreEqual(illicitBefore, CountIllicit(town));

            // Now let the empire find them, and settle another season.
            var second = NewTown();
            second.AddSuspicion(75, "test fixture");
            second.BlackCoin = 1000;
            foreach (var building in second.Buildings)
                if (building.Def != null && building.Def.Illicit)
                    second.DiscoveredHollows.Add(building.Origin);

            second.Clock.Advance(SimClock.TicksPerDay * (SimClock.DaysPerSeason - 1));
            var report = SeasonSettlement.Settle(second);

            Assert.Greater(report.ChambersSeized, 0, "what they found, they can take");
        }

        [Test]
        public void ASettledSeasonOpensFreshBooksAgainstAPhysicalCount()
        {
            var town = NewTown();
            town.Stock.Add(MaterialId.Timber, 50);
            AdvanceToAuditDay(town);

            SeasonSettlement.Settle(town);

            var timber = town.Books.Flow(MaterialId.Timber);
            Assert.AreEqual(town.Stock.Get(MaterialId.Timber), timber.Opening,
                "the new season opens with the books agreeing with the shelves");
            Assert.AreEqual(0, timber.Purchased);
            Assert.AreEqual(0, timber.Consumed);
        }

        /// <summary>
        /// Getting away with something should not clear the record. Carrying a third of the
        /// suspicion forward means a town that keeps pushing accumulates a reputation, while
        /// one that has a bad season can still recover from it.
        /// </summary>
        [Test]
        public void SuspicionCarriesOverAtAThird()
        {
            var town = NewTown();
            town.AddSuspicion(45, "test fixture");
            town.Coin = 10000;
            AdvanceToAuditDay(town);

            SeasonSettlement.Settle(town);

            Assert.AreEqual(15, town.Suspicion);
        }

        [Test]
        public void LosingTheTownStopsTheSimulation()
        {
            var town = NewTown();
            town.AddSuspicion(95, "test fixture");
            AdvanceToAuditDay(town);

            SeasonSettlement.Settle(town);
            Assert.IsTrue(town.GameOver);

            int suspicion = town.Suspicion;
            InspectionSystem.Tick(town, 1000);
            Assert.AreEqual(suspicion, town.Suspicion, "nothing continues after the town is taken");
        }

        private static SettlementReport Settle(int suspicion)
        {
            var town = NewTown();
            town.AddSuspicion(suspicion, "test fixture");
            town.Coin = 10000;
            town.BlackCoin = 1000;
            town.Clock.Advance(SimClock.TicksPerDay * (SimClock.DaysPerSeason - 1));
            return SeasonSettlement.Settle(town);
        }

        private static int InspectorLevelAfter(int suspicion) => Settle(suspicion).InspectorLevelAfter;

        private static int CountIllicit(TownState town)
        {
            int count = 0;
            foreach (var building in town.Buildings)
                if (building.Def != null && building.Def.Illicit) count++;
            return count;
        }
    }
}
