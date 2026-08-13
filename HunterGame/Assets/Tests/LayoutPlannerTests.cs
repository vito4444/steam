using System.Linq;
using Hunter.Worldgen;
using NUnit.Framework;

namespace Hunter.Tests
{
    /// The layout rules are what stop a procedural ruin from being a corridor of copies or
    /// a map with no way home. They are asserted here rather than checked by looking at it.
    public class LayoutPlannerTests
    {
        [Test]
        public void EveryLayoutStartsAtTheEntrance()
        {
            for (int seed = 0; seed < 60; seed++)
            {
                var plan = LayoutPlanner.Plan(seed);
                Assert.AreEqual(SegmentKind.Entrance, plan.Segments[0].Kind, $"seed {seed}");
            }
        }

        [Test]
        public void EveryLayoutHasExactlyOneBellAndOneVault()
        {
            for (int seed = 0; seed < 120; seed++)
            {
                var plan = LayoutPlanner.Plan(seed, 5 + seed % 8);

                Assert.AreEqual(1, plan.Segments.Count(s => s.Kind == SegmentKind.BellChamber),
                    $"seed {seed} must have exactly one way home");
                Assert.AreEqual(1, plan.Segments.Count(s => s.Kind == SegmentKind.Vault),
                    $"seed {seed} must have exactly one vault");
            }
        }

        [Test]
        public void TheVaultIsAlwaysDeeperThanTheBell()
        {
            // Walking past your own exit to reach the best loot is the core tension of the
            // layout; if the vault were nearer than the bell there would be no decision.
            for (int seed = 0; seed < 120; seed++)
            {
                var plan = LayoutPlanner.Plan(seed, 5 + seed % 8);
                Assert.Greater(plan.Vault.StartZ, plan.Bell.StartZ, $"seed {seed}");
            }
        }

        [Test]
        public void SegmentsTileWithoutGapsOrOverlaps()
        {
            for (int seed = 0; seed < 40; seed++)
            {
                var plan = LayoutPlanner.Plan(seed);
                for (int i = 1; i < plan.Segments.Count; i++)
                {
                    Assert.AreEqual(plan.Segments[i - 1].EndZ, plan.Segments[i].StartZ, 1e-3f,
                        $"seed {seed} segment {i} does not meet its predecessor");
                }
            }
        }

        [Test]
        public void NonColonnadeSegmentsNeverRepeatBackToBack()
        {
            for (int seed = 0; seed < 120; seed++)
            {
                var plan = LayoutPlanner.Plan(seed, 12);
                for (int i = 1; i < plan.Segments.Count; i++)
                {
                    var previous = plan.Segments[i - 1].Kind;
                    var current = plan.Segments[i].Kind;
                    if (current == SegmentKind.Colonnade) continue;

                    Assert.AreNotEqual(previous, current,
                        $"seed {seed} repeats {current} back to back at index {i}");
                }
            }
        }

        [Test]
        public void SameSeedProducesTheSameRuin()
        {
            var a = LayoutPlanner.Plan(9182, 9);
            var b = LayoutPlanner.Plan(9182, 9);

            Assert.AreEqual(a.Segments.Count, b.Segments.Count);
            for (int i = 0; i < a.Segments.Count; i++)
            {
                Assert.AreEqual(a.Segments[i].Kind, b.Segments[i].Kind);
                Assert.AreEqual(a.Segments[i].StartZ, b.Segments[i].StartZ, 1e-4f);
                Assert.AreEqual(a.Segments[i].Branches.Count, b.Segments[i].Branches.Count);
            }
        }

        [Test]
        public void DifferentSeedsProduceDifferentRuins()
        {
            var a = string.Join(",", LayoutPlanner.Plan(1, 10).Segments.Select(s => s.Kind));
            var b = string.Join(",", LayoutPlanner.Plan(2, 10).Segments.Select(s => s.Kind));

            Assert.AreNotEqual(a, b);
        }

        [Test]
        public void SegmentCountIsClampedToTheSupportedRange()
        {
            Assert.AreEqual(LayoutPlanner.MinSegments, LayoutPlanner.Plan(7, 1).Segments.Count);
            Assert.AreEqual(LayoutPlanner.MaxSegments, LayoutPlanner.Plan(7, 999).Segments.Count);
        }

        [Test]
        public void EveryLayoutHasEnoughLootToBeWorthEntering()
        {
            for (int seed = 0; seed < 60; seed++)
            {
                var plan = LayoutPlanner.Plan(seed, 8);
                Assert.GreaterOrEqual(plan.TotalCaches, 5,
                    $"seed {seed} does not hold enough loot to justify the risk");
            }
        }

        [Test]
        public void RuinsAreLongEnoughToFillARaid()
        {
            for (int seed = 0; seed < 40; seed++)
            {
                var plan = LayoutPlanner.Plan(seed, 8);
                Assert.Greater(plan.TotalLength, 80f, $"seed {seed} is too short for an 18 minute raid");
            }
        }

        [Test]
        public void NarrowGalleriesAreActuallyNarrowerThanCourtyards()
        {
            // The layout only creates tension if its segment types feel different; this
            // guards the numbers that make a gallery an ambush spot and a yard a relief.
            var galleries = new System.Collections.Generic.List<float>();
            var courtyards = new System.Collections.Generic.List<float>();

            for (int seed = 0; seed < 200; seed++)
            {
                foreach (var segment in LayoutPlanner.Plan(seed, 12).Segments)
                {
                    if (segment.Kind == SegmentKind.NarrowGallery) galleries.Add(segment.HalfWidth);
                    if (segment.Kind == SegmentKind.Courtyard) courtyards.Add(segment.HalfWidth);
                }
            }

            Assert.IsNotEmpty(galleries);
            Assert.IsNotEmpty(courtyards);
            Assert.Less(galleries.Max(), courtyards.Min());
        }
    }
}
