using System.Collections.Generic;
using NUnit.Framework;

namespace Worker.Core.Tests
{
    [TestFixture]
    public class PathfinderTests
    {
        private TileMap _map;
        private Pathfinder _pathfinder;
        private List<GridPos> _result;

        [SetUp]
        public void SetUp()
        {
            _map = new TileMap(16, 16);
            _map.FillTerrain(GridPos.Zero, 16, 16, TerrainKind.Floor);
            _pathfinder = new Pathfinder(_map);
            _result = new List<GridPos>();
        }

        [Test]
        public void StraightLinePathHasManhattanLength()
        {
            bool found = _pathfinder.TryFindPath(new GridPos(2, 2), new GridPos(8, 2), _result);

            Assert.IsTrue(found);
            Assert.AreEqual(6, _result.Count, "path should be exactly the Manhattan distance on open ground");
            Assert.AreEqual(new GridPos(8, 2), _result[_result.Count - 1]);
        }

        [Test]
        public void PathExcludesStartAndIncludesGoal()
        {
            _pathfinder.TryFindPath(new GridPos(0, 0), new GridPos(0, 3), _result);

            CollectionAssert.DoesNotContain(_result, new GridPos(0, 0));
            Assert.AreEqual(new GridPos(0, 3), _result[_result.Count - 1]);
        }

        [Test]
        public void SameTileNeedsNoSteps()
        {
            bool found = _pathfinder.TryFindPath(new GridPos(4, 4), new GridPos(4, 4), _result);

            Assert.IsTrue(found);
            Assert.AreEqual(0, _result.Count);
        }

        [Test]
        public void EveryStepIsOrthogonalAndWalkable()
        {
            BuildWall(x: 8, fromY: 0, toY: 12);
            bool found = _pathfinder.TryFindPath(new GridPos(2, 2), new GridPos(14, 2), _result);

            Assert.IsTrue(found, "a gap exists above the wall, so a route must be found");

            var cursor = new GridPos(2, 2);
            for (int i = 0; i < _result.Count; i++)
            {
                Assert.AreEqual(1, cursor.ManhattanTo(_result[i]), "step " + i + " was not a single orthogonal move");
                Assert.IsTrue(_map.IsWalkable(_result[i]), "step " + i + " walked through an obstacle");
                cursor = _result[i];
            }
        }

        [Test]
        public void RoutesAroundAWall()
        {
            BuildWall(x: 8, fromY: 0, toY: 12);
            _pathfinder.TryFindPath(new GridPos(2, 2), new GridPos(14, 2), _result);

            // Straight line would be 12; detouring over a wall that spans y=0..12 costs more.
            Assert.Greater(_result.Count, 12);
        }

        [Test]
        public void ReturnsFalseWhenFullyWalledOff()
        {
            BuildWall(x: 8, fromY: 0, toY: 15);
            bool found = _pathfinder.TryFindPath(new GridPos(2, 2), new GridPos(14, 2), _result);

            Assert.IsFalse(found);
            Assert.AreEqual(0, _result.Count);
        }

        [Test]
        public void ReturnsFalseWhenGoalIsBlocked()
        {
            var wall = new BuildingInstance(99, BuildingKind.Wall, new GridPos(5, 5), Direction.North);
            _map.Occupy(wall);

            Assert.IsFalse(_pathfinder.TryFindPath(new GridPos(1, 1), new GridPos(5, 5), _result));
        }

        [Test]
        public void PathToAnyPicksTheCheapestGoal()
        {
            var goals = new List<GridPos>
            {
                new GridPos(12, 12),
                new GridPos(3, 2),
                new GridPos(9, 9)
            };

            bool found = _pathfinder.TryFindPathToAny(new GridPos(2, 2), goals, _result);

            Assert.IsTrue(found);
            Assert.AreEqual(new GridPos(3, 2), _result[_result.Count - 1], "should have chosen the nearest goal");
            Assert.AreEqual(1, _result.Count);
        }

        [Test]
        public void PathToAnyReturnsEmptyWhenAlreadyStandingOnAGoal()
        {
            var goals = new List<GridPos> { new GridPos(5, 5), new GridPos(1, 1) };

            bool found = _pathfinder.TryFindPathToAny(new GridPos(5, 5), goals, _result);

            Assert.IsTrue(found);
            Assert.AreEqual(0, _result.Count);
        }

        [Test]
        public void SearchIsRepeatable()
        {
            BuildWall(x: 6, fromY: 3, toY: 11);

            var first = new List<GridPos>();
            var second = new List<GridPos>();
            _pathfinder.TryFindPath(new GridPos(1, 1), new GridPos(13, 13), first);
            _pathfinder.TryFindPath(new GridPos(1, 1), new GridPos(13, 13), second);

            CollectionAssert.AreEqual(first, second);
        }

        private void BuildWall(int x, int fromY, int toY)
        {
            int id = 1000;
            for (int y = fromY; y <= toY; y++)
            {
                _map.Occupy(new BuildingInstance(id++, BuildingKind.Wall, new GridPos(x, y), Direction.North));
            }
        }
    }

    [TestFixture]
    public class BuildingInstanceTests
    {
        [Test]
        public void FootprintCoversDeclaredSize()
        {
            var bench = new BuildingInstance(1, BuildingKind.Sawbench, new GridPos(5, 5), Direction.North);

            var tiles = new List<GridPos>(bench.Footprint());
            Assert.AreEqual(4, tiles.Count);
            CollectionAssert.Contains(tiles, new GridPos(5, 5));
            CollectionAssert.Contains(tiles, new GridPos(6, 6));
            Assert.IsTrue(bench.Covers(new GridPos(6, 5)));
            Assert.IsFalse(bench.Covers(new GridPos(7, 5)));
        }

        [Test]
        public void AdjacentTilesRingTheFootprintWithoutOverlappingIt()
        {
            var bench = new BuildingInstance(1, BuildingKind.Sawbench, new GridPos(5, 5), Direction.North);
            var adjacent = bench.AdjacentTiles();

            // A 2x2 building has 8 orthogonally adjacent tiles.
            Assert.AreEqual(8, adjacent.Count);
            foreach (var tile in adjacent)
            {
                Assert.IsFalse(bench.Covers(tile), tile + " should be outside the footprint");
            }
            CollectionAssert.AllItemsAreUnique(adjacent);
        }

        [Test]
        public void RotationSwapsExtentsForNonSquareFootprints()
        {
            // All current buildings are square, so rotation must be a no-op for them.
            var bench = new BuildingInstance(1, BuildingKind.Sawbench, new GridPos(0, 0), Direction.East);
            Assert.AreEqual(2, bench.Width);
            Assert.AreEqual(2, bench.Height);
        }

        [Test]
        public void CanStartWorkRequiresInputsAndOutputRoom()
        {
            var bench = new BuildingInstance(1, BuildingKind.Sawbench, new GridPos(0, 0), Direction.North)
            {
                ActiveRecipe = RecipeId.SawLogs
            };

            Assert.IsFalse(bench.CanStartWork(), "no logs yet");

            bench.Input.TryAdd(ItemId.Log, 1);
            Assert.IsTrue(bench.CanStartWork());

            // Fill the output so the resulting planks would have nowhere to go.
            bench.Output.TryAdd(ItemId.Plank, bench.Output.SpaceFor(ItemId.Plank));
            Assert.IsFalse(bench.CanStartWork(), "output buffer is full");
        }
    }
}
