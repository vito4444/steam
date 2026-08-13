using System.Collections.Generic;
using UnityEngine;
using Worker.Core;

namespace Worker.Game
{
    /// <summary>
    /// Everything in the scene that is not a machine: floor markings, pallets, stacked
    /// timber, drums, and the ground outside the building.
    ///
    /// This is the difference the reference screenshots make most obvious. Two Point
    /// Hospital and Timberborn fill their frames with objects that are not systems, and
    /// that incidental clutter is most of what makes a floor read as a place people work
    /// in rather than a diagram of a process. None of it participates in the simulation.
    ///
    /// Placement is driven by the world's seeded random and by the actual free tiles, so
    /// dressing never lands on a machine and is identical between runs of the same seed
    /// (which the screenshot regression depends on).
    /// </summary>
    public sealed class IsometricSceneDressing
    {
        private readonly Transform _root;
        private readonly Material _concrete;
        private readonly Material _paint;
        private readonly Material _hazard;
        private readonly Material _timber;
        private readonly Material _drum;
        private readonly Material _grass;
        private readonly Material _foliage;
        private readonly Material _trunk;

        public IsometricSceneDressing(Transform root, Material concrete, Material paint, Material hazard,
            Material timber, Material drum, Material grass, Material foliage, Material trunk)
        {
            _root = root;
            _concrete = concrete;
            _paint = paint;
            _hazard = hazard;
            _timber = timber;
            _drum = drum;
            _grass = grass;
            _foliage = foliage;
            _trunk = trunk;
        }

        public void Build(SimWorld world)
        {
            BuildSurroundings(world);
            BuildFloorMarkings(world);
            BuildClutter(world);
        }

        /// <summary>
        /// Ground and trees outside the walls. An isometric view with nothing beyond the
        /// building's edge reads as a model on a table; a bit of context sells it as a
        /// place with an outside.
        /// </summary>
        private void BuildSurroundings(SimWorld world)
        {
            var ground = Box("Ground", _grass,
                new Vector3(world.Map.Width + 26f, 0.3f, world.Map.Height + 22f),
                new Vector3(world.Map.Width * 0.5f, -0.32f, world.Map.Height * 0.5f));
            ground.name = "OutsideGround";

            // Apron of concrete around the shell so the walls do not sit straight on grass.
            Box("Apron", _concrete,
                new Vector3(world.Map.Width + 6f, 0.2f, world.Map.Height + 6f),
                new Vector3(world.Map.Width * 0.5f, -0.16f, world.Map.Height * 0.5f));

            var random = new DeterministicRandom(world.Seed ^ 0x5EEDu);

            for (int i = 0; i < 26; i++)
            {
                // Ring the site, keeping clear of the building footprint.
                bool alongX = random.NextInt(2) == 0;
                int distance = 4 + random.NextInt(7);
                bool positive = random.NextInt(2) == 0;

                float x, z;
                if (alongX)
                {
                    x = random.NextInt(-6, world.Map.Width + 6);
                    z = positive ? world.Map.Height + distance : -distance;
                }
                else
                {
                    x = positive ? world.Map.Width + distance : -distance;
                    z = random.NextInt(-6, world.Map.Height + 6);
                }

                Tree(new Vector3(x, 0f, z), random);
            }
        }

        private void Tree(Vector3 position, DeterministicRandom random)
        {
            float scale = 0.8f + random.NextInt(0, 60) / 100f;

            Cylinder("Trunk", _trunk, new Vector3(0.24f, 0.5f * scale, 0.24f),
                position + new Vector3(0f, 0.5f * scale, 0f));

            // Two stacked cones read as a conifer and cost two primitives.
            var lower = Cylinder("Canopy", _foliage, new Vector3(1.5f * scale, 0.5f * scale, 1.5f * scale),
                position + new Vector3(0f, 1.2f * scale, 0f));
            lower.localScale = new Vector3(1.4f * scale, 0.55f * scale, 1.4f * scale);

            Cylinder("CanopyTop", _foliage, new Vector3(0.95f * scale, 0.45f * scale, 0.95f * scale),
                position + new Vector3(0f, 1.95f * scale, 0f));
        }

        /// <summary>
        /// Painted markings: hazard edging at the doors, walkway borders, and bay
        /// numbers. Real factory floors are covered in paint, and it is the cheapest
        /// possible way to add detail that also happens to be informative.
        /// </summary>
        private void BuildFloorMarkings(SimWorld world)
        {
            int x0 = Scenarios.FloorOrigin.X;
            int y0 = Scenarios.FloorOrigin.Y;
            int w = Scenarios.FloorWidth;
            int h = Scenarios.FloorHeight;

            // Walkway down the centre of the shop.
            for (int i = 0; i < 2; i++)
            {
                Box("Walkway", _paint,
                    new Vector3(w - 4f, 0.012f, 0.09f),
                    new Vector3(x0 + w * 0.5f, 0.21f, y0 + 5.4f + i * 4.2f));
            }

            // Hazard stripes at both doorways.
            for (int i = 0; i < 8; i++)
            {
                Box("HazardIn", _hazard, new Vector3(0.22f, 0.012f, 0.9f),
                    new Vector3(x0 + 0.6f + i * 0.3f, 0.21f, y0 + 7f));
                Box("HazardOut", _hazard, new Vector3(0.22f, 0.012f, 0.9f),
                    new Vector3(x0 + w - 1.2f - i * 0.3f, 0.21f, y0 + 7f));
            }

            // Bay outlines: thin painted rectangles that make the floor look allocated.
            var bays = new[]
            {
                new Vector4(x0 + 3.5f, y0 + 11.5f, 5f, 3f),
                new Vector4(x0 + 3.5f, y0 + 2.5f, 5f, 3f),
                new Vector4(x0 + 17f, y0 + 4f, 4f, 3f)
            };

            foreach (var bay in bays)
            {
                Outline(new Vector3(bay.x, 0.208f, bay.y), bay.z, bay.w);
            }
        }

        private void Outline(Vector3 centre, float width, float depth)
        {
            Box("BayEdge", _paint, new Vector3(width, 0.01f, 0.07f), centre + new Vector3(0f, 0f, depth * 0.5f));
            Box("BayEdge", _paint, new Vector3(width, 0.01f, 0.07f), centre - new Vector3(0f, 0f, depth * 0.5f));
            Box("BayEdge", _paint, new Vector3(0.07f, 0.01f, depth), centre + new Vector3(width * 0.5f, 0f, 0f));
            Box("BayEdge", _paint, new Vector3(0.07f, 0.01f, depth), centre - new Vector3(width * 0.5f, 0f, 0f));
        }

        /// <summary>
        /// Pallets, timber stacks and drums on tiles the simulation is not using.
        /// </summary>
        private void BuildClutter(SimWorld world)
        {
            var random = new DeterministicRandom(world.Seed ^ 0xC1A77E5u);
            var free = new List<GridPos>();

            for (int y = Scenarios.FloorOrigin.Y + 1; y < Scenarios.FloorOrigin.Y + Scenarios.FloorHeight - 1; y++)
            {
                for (int x = Scenarios.FloorOrigin.X + 1; x < Scenarios.FloorOrigin.X + Scenarios.FloorWidth - 1; x++)
                {
                    var tile = new GridPos(x, y);
                    if (!world.Map.IsFree(tile)) continue;

                    // Keep a clear ring around occupied tiles so clutter never blocks the
                    // read of a machine or looks like it is inside a belt.
                    if (!IsIsolated(world, tile)) continue;
                    free.Add(tile);
                }
            }

            int wanted = Mathf.Min(18, free.Count);
            for (int i = 0; i < wanted && free.Count > 0; i++)
            {
                int index = random.NextInt(free.Count);
                var tile = free[index];
                free.RemoveAt(index);

                var centre = new Vector3(tile.X + 0.5f, 0.2f, tile.Y + 0.5f);
                switch (random.NextInt(3))
                {
                    case 0: Pallet(centre, random); break;
                    case 1: TimberStack(centre, random); break;
                    default: Drums(centre, random); break;
                }
            }
        }

        private static bool IsIsolated(SimWorld world, GridPos tile)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    var probe = new GridPos(tile.X + dx, tile.Y + dy);
                    if (!world.Map.InBounds(probe)) return false;
                    if (!world.Map.IsFree(probe)) return false;
                }
            }
            return true;
        }

        private void Pallet(Vector3 centre, DeterministicRandom random)
        {
            for (int i = 0; i < 3; i++)
            {
                Box("PalletSlat", _timber, new Vector3(0.72f, 0.05f, 0.14f),
                    centre + new Vector3(0f, 0.03f, -0.24f + i * 0.24f));
            }

            int crates = random.NextInt(1, 4);
            for (int i = 0; i < crates; i++)
            {
                Box("Crate", _timber, new Vector3(0.3f, 0.26f, 0.3f),
                    centre + new Vector3(-0.16f + (i % 2) * 0.32f, 0.19f + (i / 2) * 0.26f, -0.1f + (i % 3) * 0.12f));
            }
        }

        private void TimberStack(Vector3 centre, DeterministicRandom random)
        {
            int rows = random.NextInt(2, 5);
            for (int row = 0; row < rows; row++)
            {
                for (int i = 0; i < 3; i++)
                {
                    var log = Cylinder("Timber", _timber, new Vector3(0.17f, 0.42f, 0.17f),
                        centre + new Vector3(0f, 0.09f + row * 0.17f, -0.2f + i * 0.2f));
                    log.localRotation = Quaternion.Euler(0f, 0f, 90f);
                }
            }
        }

        private void Drums(Vector3 centre, DeterministicRandom random)
        {
            int count = random.NextInt(2, 5);
            for (int i = 0; i < count; i++)
            {
                float angle = i * Mathf.PI * 2f / count;
                Cylinder("Drum", _drum, new Vector3(0.28f, 0.24f, 0.28f),
                    centre + new Vector3(Mathf.Cos(angle) * 0.22f, 0.24f, Mathf.Sin(angle) * 0.22f));
            }
        }

        // ------------------------------------------------------------- primitives

        private Transform Box(string name, Material material, Vector3 scale, Vector3 position)
            => MeshObjects.Box(name, _root, material, scale, position);

        private Transform Cylinder(string name, Material material, Vector3 scale, Vector3 position)
            => MeshObjects.Cylinder(name, _root, material, scale, position);
    }
}
