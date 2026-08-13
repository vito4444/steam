using NUnit.Framework;
using Undertown.Core.Agents;
using Undertown.Core.Buildings;
using Undertown.Core.Economy;
using Undertown.Core.Sim;
using Undertown.Core.World;

namespace Undertown.Tests
{
    /// <summary>
    /// Needs are where the audit reaches the people. Grain is food and also what both the
    /// brewery and the still run on; wages come out of the same coin the tax is paid from.
    /// A town squeezing its books is a town tempted to squeeze its people, and its people
    /// are who the inspector asks.
    /// </summary>
    public class NeedsSystemTests
    {
        private static TownState NewTown(int grain = 1000, int coin = 5000)
        {
            var map = MapGenerator.Generate(MapSettings.Default, 20260813);
            var town = new TownState(map, 20260813);
            town.Stock.Add(MaterialId.Grain, grain);
            town.Coin = coin;
            town.Books.BeginPeriod(town.Stock.Get);
            TownFounder.Found(town);
            AgentSystem.Populate(town);
            return town;
        }

        private static void PassDays(TownState town, int days)
        {
            for (int i = 0; i < days; i++)
            {
                town.Clock.Advance(SimClock.TicksPerDay);
                NeedsSystem.Tick(town);
            }
        }

        /// <summary>
        /// The design document states this as an acceptance condition: three days without a
        /// ration must visibly cost loyalty. Hunger compounds rather than accruing flatly, so
        /// the third day hurts more than the first.
        /// </summary>
        [Test]
        public void ThreeDaysWithoutFoodVisiblyCostsLoyalty()
        {
            var town = NewTown(grain: 0);
            int before = town.Villagers[0].Loyalty;

            PassDays(town, 1);
            int afterOne = town.Villagers[0].Loyalty;

            PassDays(town, 2);
            int afterThree = town.Villagers[0].Loyalty;

            Assert.Less(afterOne, before, "one missed ration already shows");
            Assert.Less(afterThree, afterOne, "and three is worse than one");
            Assert.AreEqual(3, town.Villagers[0].DaysHungry);
            Assert.AreEqual("hungry 3 days", town.Villagers[0].Grievance,
                "the interface has something concrete to display");

            Assert.Greater(afterOne - afterThree, before - afterOne,
                "hunger compounds: the later days cost more than the first");
        }

        [Test]
        public void ThreeDaysOfHungerIsEnoughToMakeSomeoneTalk()
        {
            var town = NewTown(grain: 0);
            foreach (var villager in town.Villagers) Assert.IsFalse(villager.WillTalk);

            PassDays(town, 3);

            Assert.IsTrue(town.Villagers[0].WillTalk,
                "a worker starved for three days will answer an inspector's questions");
        }

        [Test]
        public void FedHousedAndPaidPeopleRecover()
        {
            var town = NewTown();
            town.Villagers[0].Loyalty = 20;

            PassDays(town, 10);

            Assert.Greater(town.Villagers[0].Loyalty, 20, "a decently run town wins its people back");
            Assert.AreEqual(0, town.Villagers[0].DaysHungry);
        }

        [Test]
        public void MeagreWagesBuyCoinAndCostSilence()
        {
            var generous = NewTown();
            generous.Wages = WageLevel.Generous;
            PassDays(generous, 8);

            var meagre = NewTown();
            meagre.Wages = WageLevel.Meagre;
            PassDays(meagre, 8);

            Assert.Greater(meagre.Coin, generous.Coin, "paying less leaves more in the treasury");
            Assert.Less(NeedsSystem.AverageLoyalty(meagre), NeedsSystem.AverageLoyalty(generous),
                "and buys correspondingly less goodwill");
        }

        [Test]
        public void AnEmptyTreasuryIsWorseThanALowWage()
        {
            var poor = NewTown(coin: 0);
            poor.Wages = WageLevel.Meagre;
            PassDays(poor, 1);

            Assert.IsFalse(poor.Villagers[0].Paid);
            Assert.AreEqual("unpaid", poor.Villagers[0].Grievance);

            var lowPaid = NewTown();
            lowPaid.Wages = WageLevel.Meagre;
            PassDays(lowPaid, 1);

            Assert.Less(poor.Villagers[0].Loyalty, lowPaid.Villagers[0].Loyalty,
                "no wage at all costs more than a poor one");
        }

        [Test]
        public void FoodComesOffTheSamePileTheStillDrawsFrom()
        {
            var town = NewTown(grain: 100);
            int before = town.Stock.Get(MaterialId.Grain);

            PassDays(town, 1);

            int eaten = before - town.Stock.Get(MaterialId.Grain);
            Assert.AreEqual(town.Villagers.Count * NeedsSystem.GrainPerPersonPerDay, eaten);
            Assert.AreEqual(eaten, town.Books.Flow(MaterialId.Grain).Consumed,
                "rations are a lawful use of grain and are declared as one");
        }

        [Test]
        public void HousingIsLimitedByTheNumberOfBeds()
        {
            var town = NewTown();

            int beds = 0;
            foreach (var building in town.Buildings)
                if (building.Kind == BuildingKind.House) beds += NeedsSystem.SleepersPerHouse;

            PassDays(town, 1);

            int housed = 0;
            foreach (var villager in town.Villagers)
                if (villager.Housed) housed++;

            Assert.AreEqual(System.Math.Min(beds, town.Villagers.Count), housed);
        }

        [Test]
        public void TheDailyReckoningNeverSkipsADayAfterAFastForward()
        {
            var town = NewTown(grain: 0);

            // Jump five days in one step, as the screenshot harness and a paused game both do.
            town.Clock.Advance(SimClock.TicksPerDay * 5);
            NeedsSystem.Tick(town);

            Assert.AreEqual(5, town.Villagers[0].DaysHungry,
                "five days passed, so five rations were missed");
        }

        /// <summary>
        /// A discontented worker who knows nothing cannot give up the chamber, but they can
        /// still tell the inspector this is a town worth returning to.
        /// </summary>
        [Test]
        public void DiscontentWithoutKnowledgeStillCostsSomething()
        {
            var town = NewTown(grain: 0);
            foreach (var villager in town.Villagers) villager.KnowsAboutTheWorks = false;

            PassDays(town, 4);
            int before = town.Suspicion;

            RunInspection(town);

            Assert.Greater(town.Suspicion, before, "complaints are not free");
            Assert.Less(town.Suspicion - before, 22, "but they are cheaper than being given the cellar");
        }

        private static void RunInspection(TownState town)
        {
            const int chunk = 15;
            while (town.Clock.DayOfSeason != 10) town.Clock.Advance(SimClock.TicksPerDay);
            for (int i = 0; i < 2000; i += chunk)
            {
                town.Clock.Advance(chunk);
                InspectionSystem.Tick(town, chunk);
            }
        }
    }
}
