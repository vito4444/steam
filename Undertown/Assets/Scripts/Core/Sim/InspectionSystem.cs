using System.Collections.Generic;
using Undertown.Core.Agents;
using Undertown.Core.Buildings;
using Undertown.Core.Inspection;
using Undertown.Core.World;

namespace Undertown.Core.Sim
{
    /// <summary>
    /// Runs an inspection from the moment the inspector appears at the edge of the map to
    /// the moment he leaves. He works through four checks that come at the same secret from
    /// four directions - walking the roads, tapping the ground, reading the books, and
    /// asking the townsfolk - which is why no single countermeasure covers a town.
    /// </summary>
    public static class InspectionSystem
    {
        private const int PatrolStops = 6;
        private const int SoundingsPerVisit = 8;
        private const int AuditTicks = 90;
        private const int InterrogationTicks = 60;

        public static void Tick(TownState town, int ticks)
        {
            if (town.GameOver) return;

            MaybeBeginVisit(town);

            var inspector = town.ActiveInspector;
            if (inspector == null || inspector.Task == InspectorTask.Gone)
            {
                // The season closes only once the auditor has been and gone, so his findings
                // are part of the reckoning rather than arriving after it.
                if (SeasonSettlement.IsDue(town)) SeasonSettlement.Settle(town);
                return;
            }

            switch (inspector.Task)
            {
                case InspectorTask.Arriving: Arrive(town, inspector, ticks); break;
                case InspectorTask.Patrolling: Patrol(town, inspector, ticks); break;
                case InspectorTask.Sounding: Sound(town, inspector, ticks); break;
                case InspectorTask.Auditing: Audit(town, inspector, ticks); break;
                case InspectorTask.Interrogating: Interrogate(town, inspector, ticks); break;
                case InspectorTask.Departing: Depart(town, inspector, ticks); break;
            }
        }

        private static void MaybeBeginVisit(TownState town)
        {
            if (town.ActiveInspector != null) return;
            if (!town.Clock.IsInspectionToday) return;
            if (town.LastInspectionDay == town.Clock.TotalDays) return;
            if (town.Clock.Hour < 8) return;

            town.LastInspectionDay = town.Clock.TotalDays;

            var entry = new Coord(0, FindRoadRow(town.Map));
            town.ActiveInspector = new Inspector(town.InspectorLevel, entry);
            town.SoundingsRemaining = SoundingsPerVisit;
            town.Record(SimClock.IsAuditDay(town.Clock.DayOfSeason)
                ? "the season's auditor is on the road"
                : "an inspector is on the road");
        }

        private static void Arrive(TownState town, Inspector inspector, int ticks)
        {
            if (!inspector.HasDestination)
            {
                var hall = Find(town, BuildingKind.TownHall);
                var target = hall != null
                    ? new Coord(hall.Origin.X, hall.Origin.Y - 1)
                    : new Coord(town.Map.Width / 2, FindRoadRow(town.Map));

                var path = Pathfinder.Find(town.Map, inspector.Position, target, underground: false);
                if (path == null) { inspector.Task = InspectorTask.Auditing; return; }
                inspector.SetPath(path);
            }

            if (!Advance(town, inspector, ticks)) inspector.Task = InspectorTask.Patrolling;
        }

        private static void Patrol(TownState town, Inspector inspector, int ticks)
        {
            if (Advance(town, inspector, ticks)) return;

            if (town.PatrolStopsDone >= PatrolStops)
            {
                inspector.Task = InspectorTask.Auditing;
                inspector.TaskProgress = 0;
                return;
            }

            NoticeSurfaceEvidence(town, inspector);

            town.PatrolStopsDone++;
            var stop = PickPatrolStop(town, inspector);
            var path = Pathfinder.Find(town.Map, inspector.Position, stop, underground: false);
            if (path != null && path.Count >= 2)
            {
                inspector.SetPath(path);
                return;
            }

            // Nowhere to walk: start tapping where he already stands.
            inspector.Task = InspectorTask.Sounding;
            inspector.TaskProgress = 0;
        }

        /// <summary>
        /// The one exposure that needs no cleverness to find. A quarry town can explain a
        /// certain amount of loose earth; past that, a man walking down the road asks where
        /// it came from.
        /// </summary>
        private static void NoticeSurfaceEvidence(TownState town, Inspector inspector)
        {
            int exposure = town.SpoilExposure;
            if (exposure <= 0 || town.SpoilNoticed) return;

            town.SpoilNoticed = true;
            int suspicion = 2 + exposure / 10;
            inspector.Findings.Add($"unexplained spoil heaps ({town.SurfaceSpoil} loads)");
            town.AddSuspicion(suspicion, "spoil left in the open");
        }

        private static void Sound(TownState town, Inspector inspector, int ticks)
        {
            inspector.TaskProgress += ticks;
            if (inspector.TaskProgress < Inspector.SoundingTicks) return;
            inspector.TaskProgress = 0;

            TapHere(town, inspector);

            town.SoundingsRemaining--;
            inspector.Task = town.SoundingsRemaining > 0 ? InspectorTask.Patrolling : InspectorTask.Auditing;
        }

        /// <summary>
        /// Listens for a hollow under the cell the inspector is standing on. A false wall in
        /// the column can send him away satisfied, but only by a roll he gets better at with
        /// every rank.
        /// </summary>
        private static void TapHere(TownState town, Inspector inspector)
        {
            int depth = town.Map.SoundColumn(inspector.Position, inspector.SoundingDepth);
            if (depth < 0) return;

            var hollow = inspector.Position.AtDepth(depth);
            if (IsShieldedByFalseWall(town, hollow) && town.Rng.Chance(inspector.FalseWallSuccessPerMille))
            {
                town.Record($"he tapped above the works at {inspector.Position} and heard solid earth");
                return;
            }

            // A chamber is one discovery however many times it is tapped. Charging for every
            // tap meant a single visit to one six-by-three cellar could take a town from
            // nothing to annexation, which is not a game so much as a formality.
            if (town.DiscoveredHollows.Contains(hollow))
            {
                inspector.Findings.Add($"the chamber under {inspector.Position} is still open");
                town.AddSuspicion(5, "a chamber found last time has not been filled in");
                return;
            }

            MarkChamberDiscovered(town, hollow);
            inspector.Findings.Add($"hollow ground at {inspector.Position}, {depth}m down");
            town.AddSuspicion(18, $"undeclared chamber found under {inspector.Position}");
        }

        /// <summary>
        /// Flood-fills the connected cavity so that every cell of the same chamber counts as
        /// already found, not just the one that happened to be tapped.
        /// </summary>
        public static void MarkChamberDiscovered(TownState town, Coord seed)
        {
            var frontier = new Queue<Coord>();
            frontier.Enqueue(seed);
            town.DiscoveredHollows.Add(seed);

            while (frontier.Count > 0)
            {
                var cell = frontier.Dequeue();
                for (int i = 0; i < Coord.Neighbours4.Length; i++)
                {
                    var next = cell.Offset(Coord.Neighbours4[i].X, Coord.Neighbours4[i].Y);
                    if (town.DiscoveredHollows.Contains(next)) continue;
                    if (!Tiles.SoundsHollow(town.Map.Get(next))) continue;
                    town.DiscoveredHollows.Add(next);
                    frontier.Enqueue(next);
                }
            }
        }

        private static bool IsShieldedByFalseWall(TownState town, Coord hollow)
        {
            for (int i = 0; i < Coord.Neighbours4.Length; i++)
            {
                var neighbour = hollow.Offset(Coord.Neighbours4[i].X, Coord.Neighbours4[i].Y);
                var building = BuildingPlacement.BuildingAt(town, neighbour);
                if (building != null && building.Kind == BuildingKind.FalseWall) return true;
            }
            return BuildingPlacement.BuildingAt(town, hollow)?.Kind == BuildingKind.FalseWall;
        }

        private static void Audit(TownState town, Inspector inspector, int ticks)
        {
            if (Advance(town, inspector, ticks)) return;

            inspector.TaskProgress += ticks;
            if (inspector.TaskProgress < AuditTicks) return;

            var report = AuditResolver.Resolve(
                town.Books,
                town.VisibleStock,
                town.Recipes,
                town.CurrentAuditSettings);

            foreach (var issue in report.Issues)
                inspector.Findings.Add(Describe(issue));

            if (report.TotalSuspicion > 0)
                town.AddSuspicion(report.TotalSuspicion, "the books did not balance");
            else
                town.Record("the books balanced");

            town.LastAudit = report;
            inspector.Task = InspectorTask.Interrogating;
            inspector.TaskProgress = 0;
        }

        private static string Describe(AuditIssue issue)
        {
            string material = Economy.Materials.DisplayName(issue.Material);
            switch (issue.Kind)
            {
                case AuditIssueKind.StockDiscrepancy:
                    return $"{material}: {issue.Magnitude} units short of the books";
                case AuditIssueKind.YieldShortfall:
                    return $"{material}: {issue.Magnitude} units went in with nothing to show for it";
                default:
                    return $"{material}: {issue.Magnitude} units written off as spoilage";
            }
        }

        private static void Interrogate(TownState town, Inspector inspector, int ticks)
        {
            inspector.TaskProgress += ticks;
            if (inspector.TaskProgress < InterrogationTicks) return;
            inspector.TaskProgress = 0;

            var witness = PickWitness(town);
            if (witness == null)
            {
                inspector.Task = InspectorTask.Departing;
                return;
            }

            string grievance = witness.Grievance;

            if (witness.WillTalk && witness.KnowsAboutTheWorks)
            {
                inspector.Findings.Add(grievance == null
                    ? $"{witness.Name} talked"
                    : $"{witness.Name} talked; {grievance}");
                town.AddSuspicion(22, grievance == null
                    ? $"{witness.Name} answered his questions honestly"
                    : $"{witness.Name} is {grievance}, and said so along with everything else");
            }
            else if (witness.WillTalk)
            {
                // Someone unhappy but out of the loop cannot give up the chamber. What they
                // can do is tell the inspector this is a town worth coming back to.
                inspector.Findings.Add($"{witness.Name} complained ({grievance ?? "discontent"})");
                town.AddSuspicion(4, $"{witness.Name} had complaints, though nothing he could use");
            }
            else
            {
                town.Record($"{witness.Name} said nothing useful");
            }

            inspector.Task = InspectorTask.Departing;
        }

        private static Villager PickWitness(TownState town)
        {
            if (town.Villagers.Count == 0) return null;

            // The inspector does not pick at random: he asks whoever looks least content.
            Villager weakest = town.Villagers[0];
            for (int i = 1; i < town.Villagers.Count; i++)
                if (town.Villagers[i].Loyalty < weakest.Loyalty) weakest = town.Villagers[i];
            return weakest;
        }

        private static void Depart(TownState town, Inspector inspector, int ticks)
        {
            if (!inspector.HasDestination)
            {
                var exit = new Coord(town.Map.Width - 1, FindRoadRow(town.Map));
                var path = Pathfinder.Find(town.Map, inspector.Position, exit, underground: false);
                if (path == null) { Finish(town, inspector); return; }
                inspector.SetPath(path);
            }

            if (!Advance(town, inspector, ticks)) Finish(town, inspector);
        }

        private static void Finish(TownState town, Inspector inspector)
        {
            inspector.Task = InspectorTask.Gone;
            town.Record(inspector.Findings.Count == 0
                ? "the inspector left with nothing"
                : $"the inspector left with {inspector.Findings.Count} finding(s)");
            town.LastFindings.Clear();
            town.LastFindings.AddRange(inspector.Findings);
            town.ActiveInspector = null;
            town.PatrolStopsDone = 0;
        }

        /// <summary>Moves along the current path. Returns true while still walking.</summary>
        private static bool Advance(TownState town, Inspector inspector, int ticks)
        {
            if (!inspector.HasDestination) return false;

            inspector.MoveProgress += ticks;
            while (inspector.MoveProgress >= Inspector.TicksPerStep && inspector.HasDestination)
            {
                inspector.MoveProgress -= Inspector.TicksPerStep;
                inspector.PathIndex++;
                inspector.Position = inspector.Path[inspector.PathIndex];
            }
            return inspector.HasDestination;
        }

        /// <summary>
        /// Chooses somewhere worth tapping. He favours ground beside the workshops, because
        /// that is where a chamber would be useful - which is exactly why the player should
        /// dig somewhere less convenient.
        /// </summary>
        private static Coord PickPatrolStop(TownState town, Inspector inspector)
        {
            var candidates = new List<Coord>();
            for (int i = 0; i < town.Buildings.Count; i++)
            {
                var building = town.Buildings[i];
                var def = building.Def;
                if (def == null || def.Underground) continue;

                var beside = new Coord(building.Origin.X - 1, building.Origin.Y - 1);
                if (town.Map.InBounds(beside) && Tiles.IsWalkableSurface(town.Map.Get(beside)))
                    candidates.Add(beside);
            }

            if (candidates.Count == 0) return inspector.Position;
            return candidates[town.Rng.NextInt(candidates.Count)];
        }

        private static Building Find(TownState town, BuildingKind kind)
        {
            for (int i = 0; i < town.Buildings.Count; i++)
                if (town.Buildings[i].Kind == kind) return town.Buildings[i];
            return null;
        }

        private static int FindRoadRow(GridMap map)
        {
            for (int y = 0; y < map.Height; y++)
                if (map.Get(new Coord(map.Width / 2, y)) == TileKind.Road) return y;
            return map.Height / 2;
        }
    }
}
