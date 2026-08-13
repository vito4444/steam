using NUnit.Framework;

namespace Worker.Core.Tests
{
    /// <summary>
    /// Guards the art direction rules that the rest of the game's readability depends on.
    /// These are cheap assertions, but they stop a future colour tweak from quietly
    /// flattening the image, which is exactly the kind of regression nobody notices until
    /// a screenshot looks wrong.
    /// </summary>
    [TestFixture]
    public class PaletteTests
    {
        /// <summary>
        /// The palette is organised in three value layers: floor darkest, structures in
        /// the middle, people brightest. If those bands ever overlap, a dense factory
        /// stops being readable at a glance.
        /// </summary>
        [Test]
        public void ValueLayersAreSeparated()
        {
            int floorTop = Max(
                Palette.Yard.Luminance,
                Palette.FloorA.Luminance,
                Palette.FloorB.Luminance,
                Palette.FloorLine.Luminance);

            int structureLow = Palette.BuildingBody.Luminance;
            int workerLow = Palette.WorkerBody.Luminance;

            Assert.Less(floorTop, structureLow,
                "brightest floor tone (" + floorTop + ") must stay below the building body (" + structureLow + ")");
            Assert.Less(structureLow, workerLow,
                "building body (" + structureLow + ") must stay below the worker body (" + workerLow + ")");

            // A separation this small would survive the assertions above but still look
            // muddy, so require a margin a viewer can actually perceive.
            Assert.GreaterOrEqual(structureLow - floorTop, 20, "floor and structures are too close in value");
            Assert.GreaterOrEqual(workerLow - structureLow, 60, "structures and workers are too close in value");
        }

        [Test]
        public void ConveyorsSitBetweenFloorAndStructures()
        {
            // Belts are infrastructure: the eye should be able to follow them without
            // them competing with the stations they connect.
            Assert.Greater(Palette.ConveyorBed.Luminance, Palette.FloorB.Luminance,
                "belt bed must be distinguishable from the floor it sits on");
            Assert.Greater(Palette.ConveyorRail.Luminance, Palette.ConveyorBed.Luminance,
                "belt rails must read against the belt bed");
            Assert.Less(Palette.ConveyorBed.Luminance, Palette.WorkerBody.Luminance,
                "belts must not compete with workers for attention");
        }

        [Test]
        public void EveryBuildingKindHasItsOwnAccent()
        {
            var kinds = new[]
            {
                BuildingKind.Intake, BuildingKind.Sawbench, BuildingKind.Lathe,
                BuildingKind.AssemblyBench, BuildingKind.Shipping, BuildingKind.Storage,
                BuildingKind.BreakRoom
            };

            for (int i = 0; i < kinds.Length; i++)
            {
                for (int j = i + 1; j < kinds.Length; j++)
                {
                    var a = Palette.ForBuilding(kinds[i]);
                    var b = Palette.ForBuilding(kinds[j]);
                    int distance = System.Math.Abs(a.R - b.R) + System.Math.Abs(a.G - b.G) + System.Math.Abs(a.B - b.B);

                    Assert.Greater(distance, 40,
                        kinds[i] + " and " + kinds[j] + " are too close to tell apart (" + a + " vs " + b + ")");
                }
            }
        }

        [Test]
        public void EveryProducedItemHasADistinctColour()
        {
            var items = new[]
            {
                ItemId.Log, ItemId.Plank, ItemId.ChairLeg, ItemId.Seat, ItemId.WoodChair
            };

            for (int i = 0; i < items.Length; i++)
            {
                for (int j = i + 1; j < items.Length; j++)
                {
                    var a = Palette.ForItem(items[i]);
                    var b = Palette.ForItem(items[j]);
                    int distance = System.Math.Abs(a.R - b.R) + System.Math.Abs(a.G - b.G) + System.Math.Abs(a.B - b.B);

                    Assert.Greater(distance, 20,
                        items[i] + " and " + items[j] + " are indistinguishable on a belt");
                }
            }
        }

        [Test]
        public void LightenAndDarkenMoveInTheRightDirection()
        {
            var mid = new RgbColor(120, 120, 120);

            Assert.Greater(mid.Lighten(30).Luminance, mid.Luminance);
            Assert.Less(mid.Darken(30).Luminance, mid.Luminance);
            Assert.AreEqual(255, mid.Lighten(100).R);
            Assert.AreEqual(0, mid.Darken(100).R);
        }

        [Test]
        public void LuminanceMatchesTheDocumentedBaselineValues()
        {
            // These exact numbers are quoted in Docs/art-target/README.md. Pinning them
            // here means the document cannot silently go stale.
            Assert.AreEqual(47, Palette.FloorA.Luminance);
            Assert.AreEqual(54, Palette.FloorB.Luminance);
            Assert.AreEqual(84, Palette.BuildingBody.Luminance);
            Assert.AreEqual(232, Palette.WorkerBody.Luminance);
        }

        private static int Max(params int[] values)
        {
            int best = int.MinValue;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] > best) best = values[i];
            }
            return best;
        }
    }
}
