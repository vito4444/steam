using NUnit.Framework;
using Undertown.Core.World;

namespace Undertown.Tests
{
    public class GridMapTests
    {
        private static GridMap MakeMap(int w = 8, int h = 8, int depths = 6)
        {
            var map = new GridMap(w, h, depths);
            map.FillLayer(0, TileKind.Grass);
            for (int d = 1; d < depths - 1; d++) map.FillLayer(d, TileKind.Earth);
            map.FillLayer(depths - 1, TileKind.Bedrock);
            return map;
        }

        [Test]
        public void SurfaceAndUndergroundShareTheSameFootprint()
        {
            var map = MakeMap();
            var surface = new Coord(3, 4);
            var below = surface.AtDepth(2);

            Assert.AreEqual(surface.X, below.X);
            Assert.AreEqual(surface.Y, below.Y);
            Assert.AreEqual(2, below.Depth);
        }

        [Test]
        public void ExcavatingEarthYieldsExactlyOneSpoil()
        {
            var map = MakeMap();
            var cell = new Coord(2, 2, 1);

            Assert.AreEqual(1, map.Excavate(cell));
            Assert.AreEqual(TileKind.Cavity, map.Get(cell));
            Assert.AreEqual(0, map.Excavate(cell), "an already-open cavity produces no further spoil");
        }

        [Test]
        public void BedrockCannotBeDug()
        {
            var map = MakeMap();
            var cell = new Coord(2, 2, 5);

            Assert.AreEqual(0, map.Excavate(cell));
            Assert.AreEqual(TileKind.Bedrock, map.Get(cell));
        }

        [Test]
        public void SoundingFindsAShallowUndeclaredCavity()
        {
            var map = MakeMap();
            var surface = new Coord(4, 4);
            map.Excavate(surface.AtDepth(2));

            Assert.AreEqual(2, map.SoundColumn(surface, maxDepth: 3),
                "a chamber two metres down is within a level 3 inspector's reach");
        }

        [Test]
        public void SoundingCannotReachBelowTheInspectorsDepth()
        {
            var map = MakeMap();
            var surface = new Coord(4, 4);
            map.Excavate(surface.AtDepth(4));

            Assert.AreEqual(-1, map.SoundColumn(surface, maxDepth: 3),
                "digging deeper than the inspector can hear is the whole point of digging deep");
            Assert.AreEqual(4, map.SoundColumn(surface, maxDepth: 5),
                "a better inspector hears the same chamber");
        }

        [Test]
        public void DeclaredCellarsDoNotRegisterAsFindings()
        {
            var map = MakeMap();
            var surface = new Coord(4, 4);
            var cellar = surface.AtDepth(1);
            map.Excavate(cellar);
            map.SetDeclared(cellar, true);

            Assert.AreEqual(-1, map.SoundColumn(surface, maxDepth: 3),
                "a wine cellar on the tax roll is not contraband");
        }

        [Test]
        public void SoundingReportsTheShallowestHollowInTheColumn()
        {
            var map = MakeMap();
            var surface = new Coord(4, 4);
            map.Excavate(surface.AtDepth(1));
            map.Excavate(surface.AtDepth(3));

            Assert.AreEqual(1, map.SoundColumn(surface, maxDepth: 5));
        }

        [Test]
        public void SurfaceTilesCannotBePlacedUnderground()
        {
            var map = MakeMap();
            Assert.Throws<System.ArgumentException>(() => map.Set(new Coord(1, 1, 2), TileKind.Road));
            Assert.Throws<System.ArgumentException>(() => map.Set(new Coord(1, 1, 0), TileKind.Cavity));
        }

        [Test]
        public void OutOfBoundsReadsAsBedrockRatherThanThrowing()
        {
            var map = MakeMap();
            Assert.AreEqual(TileKind.Bedrock, map.Get(new Coord(-1, 0)));
            Assert.AreEqual(TileKind.Bedrock, map.Get(new Coord(99, 99, 9)));
        }

        [Test]
        public void FingerprintChangesWithEveryEdit()
        {
            var a = MakeMap();
            var b = MakeMap();
            Assert.AreEqual(a.Fingerprint(), b.Fingerprint(), "identical construction must fingerprint identically");

            a.Excavate(new Coord(3, 3, 1));
            Assert.AreNotEqual(a.Fingerprint(), b.Fingerprint());

            b.Excavate(new Coord(3, 3, 1));
            Assert.AreEqual(a.Fingerprint(), b.Fingerprint());

            a.SetDeclared(new Coord(3, 3, 1), true);
            Assert.AreNotEqual(a.Fingerprint(), b.Fingerprint(), "declaration status is part of world state");
        }
    }
}
