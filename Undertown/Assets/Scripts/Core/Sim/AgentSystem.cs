using System.Collections.Generic;
using Undertown.Core.Agents;
using Undertown.Core.Buildings;
using Undertown.Core.World;

namespace Undertown.Core.Sim
{
    /// <summary>
    /// Walks the townsfolk to their work and keeps them there. Movement is deliberately slow
    /// relative to the working day, because travel time is what makes digging a chamber far
    /// from the village a genuine trade-off rather than a free way to stay out of earshot.
    /// </summary>
    public static class AgentSystem
    {
        public static void Tick(TownState town, int ticks)
        {
            for (int i = 0; i < town.Villagers.Count; i++)
            {
                var villager = town.Villagers[i];

                if (villager.HasDestination)
                {
                    Step(town, villager, ticks);
                    continue;
                }

                if (TryDig(town, villager, ticks)) continue;
                AssignWork(town, villager);
            }
        }

        /// <summary>
        /// Diggers work the excavation queue before anything else. A cell finished here
        /// produces a load of spoil, and spoil left in the open is evidence that needs no
        /// auditor to find - so ordering a large dig is also ordering a disposal problem.
        /// </summary>
        private static bool TryDig(TownState town, Villager villager, int ticks)
        {
            if (villager.Role != VillagerRole.Digger) return false;
            if (town.Digs.Count == 0) return false;

            if (!town.Digs.TryFindNearest(villager.Position, out var target))
            {
                RouteToDigFace(town, villager);
                return villager.HasDestination;
            }

            // Has to be standing next to the face to swing at it.
            if (target.ManhattanTo(villager.Position) > 1)
            {
                RouteTo(town, villager, target);
                return villager.HasDestination;
            }

            if (!town.Digs.AddWork(town.Map, target, ticks)) return true;

            int spoil = town.Map.Excavate(target);
            town.SurfaceSpoil += spoil;
            town.Record($"{villager.Name} broke through at {target}");
            return true;
        }

        /// <summary>Sends a digger to the layer the outstanding orders are on.</summary>
        private static void RouteToDigFace(TownState town, Villager villager)
        {
            var pending = town.Digs.Pending;
            if (pending.Count == 0) return;
            RouteTo(town, villager, pending[0]);
        }

        /// <summary>
        /// Paths to a cell adjacent to the target, crossing layers by way of a shaft when the
        /// target is not on the layer the worker is standing on.
        /// </summary>
        private static void RouteTo(TownState town, Villager villager, Coord target)
        {
            for (int i = 0; i < Coord.Neighbours4.Length; i++)
            {
                var stand = target.Offset(Coord.Neighbours4[i].X, Coord.Neighbours4[i].Y);
                if (!town.Map.InBounds(stand)) continue;
                if (!Tiles.IsOpenUnderground(town.Map.Get(stand)) && !Tiles.IsWalkableSurface(town.Map.Get(stand))) continue;

                var path = villager.Position.Depth == stand.Depth
                    ? Pathfinder.Find(town.Map, villager.Position, stand, underground: !stand.IsSurface)
                    : Pathfinder.FindAcrossLayers(town.Map, villager.Position, stand, town.ShaftCells);

                if (path == null || path.Count < 2) continue;
                villager.SetPath(path);
                return;
            }
        }

        private static void Step(TownState town, Villager villager, int ticks)
        {
            villager.MoveProgress += ticks;
            while (villager.MoveProgress >= Villager.TicksPerStep && villager.HasDestination)
            {
                villager.MoveProgress -= Villager.TicksPerStep;
                villager.PathIndex++;
                villager.Position = villager.Path[villager.PathIndex];
            }
        }

        /// <summary>
        /// Sends a worker to a building that matches their trade, or to the town hall if
        /// there is nothing for them to do.
        /// </summary>
        private static void AssignWork(TownState town, Villager villager)
        {
            var target = FindWorkplace(town, villager.Role);
            if (target == null) return;

            var def = target.Def;
            var door = new Coord(target.Origin.X + def.Width / 2, target.Origin.Y, target.Origin.Depth);
            if (door == villager.Position) return;

            // A worker on the surface cannot walk into a chamber; they need a shaft, and the
            // simulation models that by only pathing within one layer at a time.
            if (door.Depth != villager.Position.Depth) return;

            var path = Pathfinder.Find(town.Map, villager.Position, door, underground: !villager.Position.IsSurface);
            if (path == null || path.Count < 2)
            {
                // Nothing reachable this tick; try a neighbouring tile so workers do not
                // freeze permanently when their doorway is blocked by another building.
                for (int i = 0; i < Coord.Neighbours4.Length; i++)
                {
                    var alternative = door.Offset(Coord.Neighbours4[i].X, Coord.Neighbours4[i].Y);
                    path = Pathfinder.Find(town.Map, villager.Position, alternative, underground: !villager.Position.IsSurface);
                    if (path != null && path.Count >= 2) break;
                }
            }

            if (path != null && path.Count >= 2) villager.SetPath(path);
        }

        private static Building FindWorkplace(TownState town, VillagerRole role)
        {
            BuildingKind wanted;
            switch (role)
            {
                case VillagerRole.Brewer: wanted = BuildingKind.Brewery; break;
                case VillagerRole.Farmer: wanted = BuildingKind.Field; break;
                case VillagerRole.Sawyer: wanted = BuildingKind.Sawpit; break;
                case VillagerRole.Digger: wanted = BuildingKind.Still; break;
                default: wanted = BuildingKind.TownHall; break;
            }

            Building fallback = null;
            for (int i = 0; i < town.Buildings.Count; i++)
            {
                var building = town.Buildings[i];
                if (building.Kind == wanted) return building;
                if (building.Kind == BuildingKind.TownHall) fallback = building;
            }
            return fallback;
        }

        /// <summary>Populates the starting workforce and puts each of them somewhere sensible.</summary>
        public static void Populate(TownState town)
        {
            var names = new[] { "Marta", "Ovid", "Hessel", "Brun", "Aleida", "Corin" };
            var roles = new[]
            {
                VillagerRole.Brewer, VillagerRole.Farmer, VillagerRole.Sawyer,
                VillagerRole.Labourer, VillagerRole.Farmer, VillagerRole.Digger,
            };

            var hall = FindWorkplace(town, VillagerRole.Labourer);
            var spawn = hall != null
                ? new Coord(hall.Origin.X, hall.Origin.Y - 1)
                : new Coord(town.Map.Width / 2, town.Map.Height / 2);

            for (int i = 0; i < names.Length; i++)
            {
                var position = FindOpenSurfaceNear(town.Map, spawn.Offset(i, 0));
                var villager = new Villager(names[i], roles[i], position)
                {
                    // Only the digger has been down the shaft, so only the digger has
                    // anything to give away under questioning.
                    KnowsAboutTheWorks = roles[i] == VillagerRole.Digger,
                    Loyalty = roles[i] == VillagerRole.Digger ? 62 : 70,
                };
                town.Villagers.Add(villager);
            }
        }

        private static Coord FindOpenSurfaceNear(GridMap map, Coord wanted)
        {
            for (int radius = 0; radius < 12; radius++)
            for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                var candidate = wanted.Offset(dx, dy);
                if (map.InBounds(candidate) && Tiles.IsWalkableSurface(map.Get(candidate))) return candidate;
            }
            return wanted;
        }
    }
}
