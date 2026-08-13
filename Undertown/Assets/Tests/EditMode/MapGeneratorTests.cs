using NUnit.Framework;
using Undertown.Core.World;

namespace Undertown.Tests
{
    public class MapGeneratorTests
    {
        /// <summary>
        /// The design document states this as an acceptance condition: two generations from
        /// one seed must be identical cell for cell. Everything else that claims to be
        /// deterministic rests on it, including save replay and every whole-season test.
        /// </summary>
        [Test]
        public void OneSeedAlwaysProducesTheSameMap()
        {
            var first = MapGenerator.Generate(MapSettings.Default, 20260813);
            var second = MapGenerator.Generate(MapSettings.Default, 20260813);

            Assert.AreEqual(first.Fingerprint(), second.Fingerprint());

            for (int depth = 0; depth < first.DepthCount; depth++)
            for (int y = 0; y < first.Height; y++)
            for (int x = 0; x < first.Width; x++)
            {
                var cell = new Coord(x, y, depth);
                Assert.AreEqual(first.Get(cell), second.Get(cell), $"terrain differs at {cell}");
                Assert.AreEqual(first.IsDeclared(cell), second.IsDeclared(cell), $"declaration differs at {cell}");
            }
        }

        [Test]
        public void DifferentSeedsProduceDifferentMaps()
        {
            var a = MapGenerator.Generate(MapSettings.Default, 1);
            var b = MapGenerator.Generate(MapSettings.Default, 2);
            Assert.AreNotEqual(a.Fingerprint(), b.Fingerprint());
        }

        /// <summary>The features the design document requires the starting map to contain.</summary>
        [Test]
        public void TheMapContainsEveryFeatureTheDesignRequires()
        {
            var map = MapGenerator.Generate(MapSettings.Default, 20260813);

            int water = 0, forest = 0, clay = 0, road = 0, mines = 0, aquifer = 0;
            for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                switch (map.Get(new Coord(x, y)))
                {
                    case TileKind.Water: water++; break;
                    case TileKind.Forest: forest++; break;
                    case TileKind.ClayDeposit: clay++; break;
                    case TileKind.Road: road++; break;
                    case TileKind.DisusedMine: mines++; break;
                }

                if (map.Get(new Coord(x, y, 1)) == TileKind.Aquifer) aquifer++;
            }

            Assert.Greater(water, 0, "a river");
            Assert.Greater(forest, 0, "woodland to cut");
            Assert.Greater(clay, 0, "clay to dig");
            Assert.Greater(road, 0, "the imperial road the inspector walks in on");
            Assert.AreEqual(MapSettings.Default.DisusedMineCount, mines,
                "disused shafts are the only lawful place to dump spoil, so the count matters");
            Assert.Greater(aquifer, 0, "groundwater under the riverbed");
        }

        [Test]
        public void DisusedShaftsAreAlreadyOnTheTaxRoll()
        {
            var map = MapGenerator.Generate(MapSettings.Default, 20260813);

            for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                if (map.Get(new Coord(x, y)) != TileKind.DisusedMine) continue;

                var surface = new Coord(x, y);
                Assert.AreEqual(-1, map.SoundColumn(surface, maxDepth: 5),
                    "an old shaft the empire knows about must not read as a finding");
            }
        }

        [Test]
        public void TheBottomLayerCannotBeDugThrough()
        {
            var map = MapGenerator.Generate(MapSettings.Default, 20260813);
            int bottom = MapSettings.Default.DepthCount - 1;

            for (int y = 0; y < map.Height; y += 7)
            for (int x = 0; x < map.Width; x += 7)
            {
                var cell = new Coord(x, y, bottom);
                Assert.AreEqual(TileKind.Bedrock, map.Get(cell));
                Assert.AreEqual(0, map.Excavate(cell));
            }
        }
    }
}
