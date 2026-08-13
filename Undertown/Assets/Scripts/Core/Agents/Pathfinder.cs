using System.Collections.Generic;
using Undertown.Core.World;

namespace Undertown.Core.Agents
{
    /// <summary>
    /// A* over one layer of the grid. Ties are broken by a fixed neighbour order rather than
    /// by whatever the priority queue happens to do, so the same request always returns the
    /// same path - agents that wander differently between two runs of the same seed would
    /// make the whole simulation untestable.
    /// </summary>
    public static class Pathfinder
    {
        private const int MaxExpansions = 4000;

        public static List<Coord> Find(GridMap map, Coord start, Coord goal, bool underground)
        {
            if (!map.InBounds(start) || !map.InBounds(goal)) return null;
            if (start.Depth != goal.Depth) return null;
            if (!Passable(map, goal, underground)) return null;
            if (start == goal) return new List<Coord> { start };

            var cameFrom = new Dictionary<Coord, Coord>();
            var costSoFar = new Dictionary<Coord, int> { [start] = 0 };
            var open = new List<Coord> { start };
            int expansions = 0;

            while (open.Count > 0 && expansions++ < MaxExpansions)
            {
                int bestIndex = 0;
                int bestScore = int.MaxValue;
                for (int i = 0; i < open.Count; i++)
                {
                    int score = costSoFar[open[i]] + open[i].ManhattanTo(goal);
                    if (score >= bestScore) continue;
                    bestScore = score;
                    bestIndex = i;
                }

                var current = open[bestIndex];
                open.RemoveAt(bestIndex);

                if (current == goal) return Reconstruct(cameFrom, start, goal);

                for (int i = 0; i < Coord.Neighbours4.Length; i++)
                {
                    var next = current.Offset(Coord.Neighbours4[i].X, Coord.Neighbours4[i].Y);
                    if (!map.InBounds(next) || !Passable(map, next, underground)) continue;

                    int newCost = costSoFar[current] + StepCost(map, next);
                    if (costSoFar.TryGetValue(next, out int existing) && newCost >= existing) continue;

                    costSoFar[next] = newCost;
                    cameFrom[next] = current;
                    if (!open.Contains(next)) open.Add(next);
                }
            }

            return null;
        }

        /// <summary>
        /// Routes between the surface and the tunnels. Workers cannot walk through rock, so a
        /// journey to another layer has to go via a shaft - which is the whole reason
        /// entrances are worth building and worth disguising. The returned path contains the
        /// vertical step as a single move between two cells sharing an X and Y.
        /// </summary>
        public static List<Coord> FindAcrossLayers(GridMap map, Coord start, Coord goal, IReadOnlyList<Coord> shafts)
        {
            if (start.Depth == goal.Depth) return Find(map, start, goal, !start.IsSurface);
            if (shafts == null || shafts.Count == 0) return null;

            List<Coord> best = null;
            for (int i = 0; i < shafts.Count; i++)
            {
                var shaft = shafts[i];
                var nearSide = shaft.AtDepth(start.Depth);
                var farSide = shaft.AtDepth(goal.Depth);

                var toShaft = Find(map, start, nearSide, !start.IsSurface);
                if (toShaft == null) continue;

                var fromShaft = Find(map, farSide, goal, !goal.IsSurface);
                if (fromShaft == null) continue;

                int length = toShaft.Count + fromShaft.Count;
                if (best != null && length >= best.Count) continue;

                var combined = new List<Coord>(length);
                combined.AddRange(toShaft);
                combined.AddRange(fromShaft);
                best = combined;
            }

            return best;
        }

        private static bool Passable(GridMap map, Coord cell, bool underground)
        {
            var kind = map.Get(cell);
            return underground ? Tiles.IsOpenUnderground(kind) : Tiles.IsWalkableSurface(kind);
        }

        /// <summary>Roads are quicker, which is why inspectors keep to them and why the player should not.</summary>
        private static int StepCost(GridMap map, Coord cell) =>
            map.Get(cell) == TileKind.Road ? 8 : 10;

        private static List<Coord> Reconstruct(Dictionary<Coord, Coord> cameFrom, Coord start, Coord goal)
        {
            var path = new List<Coord> { goal };
            var cursor = goal;
            while (cursor != start)
            {
                if (!cameFrom.TryGetValue(cursor, out cursor)) return null;
                path.Add(cursor);
            }
            path.Reverse();
            return path;
        }
    }
}
