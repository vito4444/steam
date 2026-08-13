using Undertown.Core.Buildings;
using Undertown.Core.Economy;

namespace Undertown.Core.Sim
{
    /// <summary>
    /// Advances every workshop by one tick and, critically, decides what gets written down.
    ///
    /// A lawful workshop records both sides of what it did: the grain it consumed and the
    /// ale it produced. An illicit one records neither - which is exactly why the grain it
    /// eats shows up later as a hole. The still does not hide anything by itself; it simply
    /// declines to write, and the auditor's arithmetic does the rest.
    /// </summary>
    public static class ProductionSystem
    {
        public static void Tick(TownState town, int ticks = 1)
        {
            var buildings = town.Buildings;
            for (int i = 0; i < buildings.Count; i++)
            {
                var building = buildings[i];
                var def = building.Def;
                if (def == null || !def.Produces) continue;

                int workers = building.AssignedWorkers;
                if (workers <= 0) { building.Starved = false; continue; }

                building.Progress += ticks * workers;
                while (building.Progress >= def.CycleTicks)
                {
                    if (!RunCycle(town, building, def))
                    {
                        // Hold the progress at the threshold so work resumes the instant the
                        // input arrives, rather than restarting the cycle from nothing.
                        building.Progress = def.CycleTicks;
                        building.Starved = true;
                        break;
                    }
                    building.Progress -= def.CycleTicks;
                    building.Starved = false;
                }
            }
        }

        private static bool RunCycle(TownState town, Building building, BuildingDef def)
        {
            if (def.Input != MaterialId.None)
            {
                if (!town.Stock.TryTake(def.Input, def.InputPerCycle)) return false;
                if (!def.Illicit) town.Books.RecordConsumption(def.Input, def.InputPerCycle);
            }

            town.Stock.Add(def.Output, def.OutputPerCycle);
            if (!def.Illicit) town.Books.RecordProduction(def.Output, def.OutputPerCycle);

            return true;
        }
    }
}
