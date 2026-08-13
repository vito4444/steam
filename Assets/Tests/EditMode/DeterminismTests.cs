using NUnit.Framework;

namespace Worker.Core.Tests
{
    [TestFixture]
    public class DeterministicRandomTests
    {
        [Test]
        public void SameSeedProducesSameSequence()
        {
            var a = new DeterministicRandom(12345);
            var b = new DeterministicRandom(12345);

            for (int i = 0; i < 1000; i++)
            {
                Assert.AreEqual(a.NextUInt(), b.NextUInt(), "diverged at draw " + i);
            }
        }

        [Test]
        public void DifferentSeedsDiverge()
        {
            var a = new DeterministicRandom(1);
            var b = new DeterministicRandom(2);

            bool differs = false;
            for (int i = 0; i < 32 && !differs; i++)
            {
                if (a.NextUInt() != b.NextUInt()) differs = true;
            }

            Assert.IsTrue(differs, "adjacent seeds produced identical output for 32 draws");
        }

        [Test]
        public void NextIntStaysInRange()
        {
            var rng = new DeterministicRandom(99);
            for (int i = 0; i < 5000; i++)
            {
                int value = rng.NextInt(7);
                Assert.GreaterOrEqual(value, 0);
                Assert.Less(value, 7);
            }
        }

        [Test]
        public void NextIntCoversWholeRange()
        {
            var rng = new DeterministicRandom(4242);
            var seen = new bool[6];
            for (int i = 0; i < 5000; i++) seen[rng.NextInt(6)] = true;

            for (int i = 0; i < seen.Length; i++)
            {
                Assert.IsTrue(seen[i], "value " + i + " never drawn in 5000 attempts");
            }
        }

        [Test]
        public void StateRoundTripResumesSequence()
        {
            var rng = new DeterministicRandom(777);
            for (int i = 0; i < 50; i++) rng.NextUInt();

            var state = rng.SaveState();
            uint expected = rng.NextUInt();

            var restored = new DeterministicRandom(0);
            restored.LoadState(state);

            Assert.AreEqual(expected, restored.NextUInt());
        }
    }

    [TestFixture]
    public class GridPosTests
    {
        [Test]
        public void ManhattanDistanceIsSymmetricAndPositive()
        {
            var a = new GridPos(3, 7);
            var b = new GridPos(-2, 1);

            Assert.AreEqual(11, a.ManhattanTo(b));
            Assert.AreEqual(11, b.ManhattanTo(a));
            Assert.AreEqual(0, a.ManhattanTo(a));
        }

        [Test]
        public void StepMovesOneTileInEachDirection()
        {
            var origin = new GridPos(5, 5);

            Assert.AreEqual(new GridPos(5, 6), origin.Step(Direction.North));
            Assert.AreEqual(new GridPos(6, 5), origin.Step(Direction.East));
            Assert.AreEqual(new GridPos(5, 4), origin.Step(Direction.South));
            Assert.AreEqual(new GridPos(4, 5), origin.Step(Direction.West));
        }

        [Test]
        public void OppositeDirectionsCancelOut()
        {
            var origin = new GridPos(2, 2);
            foreach (var dir in DirectionExtensions.All)
            {
                Assert.AreEqual(origin, origin.Step(dir).Step(dir.Opposite()));
            }
        }

        [Test]
        public void RotatingFourTimesReturnsToStart()
        {
            foreach (var dir in DirectionExtensions.All)
            {
                Assert.AreEqual(dir, dir.RotateCW().RotateCW().RotateCW().RotateCW());
                Assert.AreEqual(dir, dir.RotateCW().RotateCCW());
            }
        }
    }
}
