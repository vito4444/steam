using NUnit.Framework;
using Undertown.Core.Determinism;

namespace Undertown.Tests
{
    public class DeterminismTests
    {
        [Test]
        public void SameSeedProducesTheSameStream()
        {
            var a = new DeterministicRandom(12345);
            var b = new DeterministicRandom(12345);

            for (int i = 0; i < 512; i++)
                Assert.AreEqual(a.NextUInt(), b.NextUInt(), $"streams diverged at draw {i}");
        }

        [Test]
        public void DifferentSeedsDiverge()
        {
            var a = new DeterministicRandom(1);
            var b = new DeterministicRandom(2);

            bool anyDifference = false;
            for (int i = 0; i < 32; i++)
                if (a.NextUInt() != b.NextUInt()) { anyDifference = true; break; }

            Assert.IsTrue(anyDifference, "adjacent seeds must not produce correlated streams");
        }

        [Test]
        public void RangedDrawsStayInsideTheirBounds()
        {
            var rng = new DeterministicRandom(99);
            for (int i = 0; i < 10000; i++)
            {
                int v = rng.NextInt(7);
                Assert.GreaterOrEqual(v, 0);
                Assert.Less(v, 7);

                int r = rng.NextInt(-5, 5);
                Assert.GreaterOrEqual(r, -5);
                Assert.Less(r, 5);
            }
        }

        [Test]
        public void ChanceHonoursItsExtremes()
        {
            var rng = new DeterministicRandom(7);
            for (int i = 0; i < 200; i++)
            {
                Assert.IsFalse(rng.Chance(0));
                Assert.IsTrue(rng.Chance(1000));
            }
        }

        [Test]
        public void ChanceApproximatesItsStatedRate()
        {
            var rng = new DeterministicRandom(2024);
            int hits = 0;
            const int trials = 100000;
            for (int i = 0; i < trials; i++)
                if (rng.Chance(250)) hits++;

            // A fair 25% over 100k trials lands well inside two percentage points.
            Assert.That(hits, Is.InRange(trials * 23 / 100, trials * 27 / 100),
                $"expected roughly a quarter of {trials} draws, saw {hits}");
        }

        [Test]
        public void ForkedStreamsAreIndependentButReproducible()
        {
            var parentA = new DeterministicRandom(555);
            var parentB = new DeterministicRandom(555);

            var childA = parentA.Fork(9);
            var childB = parentB.Fork(9);

            for (int i = 0; i < 64; i++)
                Assert.AreEqual(childA.NextUInt(), childB.NextUInt());

            // Forking advanced both parents identically, so they too stay in lockstep.
            Assert.AreEqual(parentA.NextUInt(), parentB.NextUInt());
        }
    }
}
