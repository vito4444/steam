using System;
using Undertown.Core.Agents;
using Undertown.Core.Buildings;
using Undertown.Core.Economy;

namespace Undertown.Core.Sim
{
    /// <summary>How much the town pays its people, and what that buys in silence.</summary>
    public enum WageLevel : byte
    {
        Meagre = 0,
        Standard = 1,
        Generous = 2,
    }

    /// <summary>
    /// Feeds, houses and pays the townsfolk once a day, and moves loyalty accordingly.
    ///
    /// This is where the audit reaches the people. Grain is food, and grain is also what the
    /// brewery and the still both run on, so every sack diverted to contraband is a sack not
    /// eaten. Wages come out of lawful coin, so a town squeezing its books is a town tempted
    /// to squeeze its wages - and an underfed, underpaid worker is exactly who the inspector
    /// asks his questions of.
    /// </summary>
    public static class NeedsSystem
    {
        public const int GrainPerPersonPerDay = 2;
        public const int SleepersPerHouse = 2;

        public static int WageCost(WageLevel level)
        {
            switch (level)
            {
                case WageLevel.Meagre: return 1;
                case WageLevel.Generous: return 6;
                default: return 3;
            }
        }

        private static int WageLoyalty(WageLevel level)
        {
            switch (level)
            {
                case WageLevel.Meagre: return -4;
                case WageLevel.Generous: return 2;
                default: return 0;
            }
        }

        public static string WageLabel(WageLevel level)
        {
            switch (level)
            {
                case WageLevel.Meagre: return "Meagre";
                case WageLevel.Generous: return "Generous";
                default: return "Standard";
            }
        }

        /// <summary>Runs the daily reckoning whenever the calendar has rolled over.</summary>
        public static void Tick(TownState town)
        {
            if (town.GameOver) return;

            int today = town.Clock.TotalDays;
            while (town.LastNeedsDay < today)
            {
                town.LastNeedsDay++;
                RunDay(town);
            }
        }

        private static void RunDay(TownState town)
        {
            int beds = CountBeds(town);
            int wage = WageCost(town.Wages);

            for (int i = 0; i < town.Villagers.Count; i++)
            {
                var villager = town.Villagers[i];

                int food = Feed(town, villager);
                int shelter = House(villager, i < beds);
                int pay = Pay(town, villager, wage);

                // A bed and a fair wage count for nothing on an empty stomach. Their penalties
                // still apply - being hungry and unpaid is worse than being merely hungry -
                // but nothing comforts someone who has not eaten.
                if (villager.DaysHungry > 0)
                {
                    shelter = Math.Min(0, shelter);
                    pay = Math.Min(0, pay);
                }

                villager.Loyalty = Clamp(villager.Loyalty + food + shelter + pay);
            }
        }

        private static int Feed(TownState town, Villager villager)
        {
            // Food comes off the same pile the brewery and the still draw from. Nothing
            // reserves it, so a player who runs both flat out is choosing to starve the town.
            if (town.Stock.TryTake(MaterialId.Grain, GrainPerPersonPerDay))
            {
                town.Books.RecordConsumption(MaterialId.Grain, GrainPerPersonPerDay);
                villager.DaysHungry = 0;
                return 1;
            }

            villager.DaysHungry++;
            return -6 * Math.Min(villager.DaysHungry, 3);
        }

        private static int House(Villager villager, bool hasBed)
        {
            villager.Housed = hasBed;
            return hasBed ? 1 : -3;
        }

        private static int Pay(TownState town, Villager villager, int wage)
        {
            if (town.Coin >= wage)
            {
                town.Coin -= wage;
                villager.Paid = true;
                return WageLoyalty(town.Wages);
            }

            // Nothing at all is worse than little: a town that cannot make payroll is a town
            // whose people have no reason left to keep its secrets.
            villager.Paid = false;
            return -8;
        }

        private static int CountBeds(TownState town)
        {
            int beds = 0;
            for (int i = 0; i < town.Buildings.Count; i++)
                if (town.Buildings[i].Kind == BuildingKind.House) beds += SleepersPerHouse;
            return beds;
        }

        private static int Clamp(int loyalty) => loyalty < 0 ? 0 : (loyalty > 100 ? 100 : loyalty);

        /// <summary>Average loyalty across the workforce, for the summary line in the interface.</summary>
        public static int AverageLoyalty(TownState town)
        {
            if (town.Villagers.Count == 0) return 0;
            int total = 0;
            for (int i = 0; i < town.Villagers.Count; i++) total += town.Villagers[i].Loyalty;
            return total / town.Villagers.Count;
        }

        public static int CountHungry(TownState town)
        {
            int hungry = 0;
            for (int i = 0; i < town.Villagers.Count; i++)
                if (town.Villagers[i].DaysHungry > 0) hungry++;
            return hungry;
        }

        public static int CountWillTalk(TownState town)
        {
            int talkers = 0;
            for (int i = 0; i < town.Villagers.Count; i++)
                if (town.Villagers[i].WillTalk) talkers++;
            return talkers;
        }
    }
}
