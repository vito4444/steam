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

        private readonly System.Func<Color, Material> _makeMaterial;
        private Material[] _accents;

        public IsometricSceneDressing(Transform root, Material concrete, Material paint, Material hazard,
            Material timber, Material drum, Material grass, Material foliage, Material trunk,
            System.Func<Color, Material> makeMaterial)
        {
            _makeMaterial = makeMaterial;
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
            // Props were previously all timber, drum green and grey, which sat within a
            // few percent of the floor's own value. Measured edge density did not move at
            // all when their count nearly tripled, because an object only reads as an
            // object where it contrasts with what is behind it. These are deliberately
            // saturated industrial colours.
            // Second attempt at this palette. The first used six fully saturated hues
            // and the floor ended up looking like spilled sweets: edge density went up
            // but the machines, which are what the player actually reads, were lost in
            // the noise. A working yard has a narrow palette, mostly the same drab
            // colours with one or two safety accents.
            _accents = new[]
            {
                _makeMaterial(new Color(0.55f, 0.30f, 0.20f)),
                _makeMaterial(new Color(0.30f, 0.36f, 0.44f)),
                _makeMaterial(new Color(0.66f, 0.52f, 0.24f)),
                _makeMaterial(new Color(0.28f, 0.40f, 0.32f)),
                _makeMaterial(new Color(0.62f, 0.62f, 0.58f)),
                _makeMaterial(new Color(0.70f, 0.34f, 0.18f))
            };

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

            // Prefer tiles close to something. Material piles up where it is used, and
            // an even scatter across the whole floor reads as decoration rather than as
            // a working yard.
            free.Sort((a, b) => DistanceToNearestBuilding(world, a).CompareTo(DistanceToNearestBuilding(world, b)));

            int pool = Mathf.Min(free.Count, Mathf.Max(24, free.Count * 2 / 3));
            int wanted = Mathf.Min(34, pool);

            for (int i = 0; i < wanted && pool > 0; i++)
            {
                int index = random.NextInt(pool);
                var tile = free[index];
                free.RemoveAt(index);
                pool--;

                var centre = new Vector3(tile.X + 0.5f, 0.2f, tile.Y + 0.5f);
                switch (random.NextInt(7))
                {
                    case 0: Pallet(centre, random); break;
                    case 1: TimberStack(centre, random); break;
                    case 2: Drums(centre, random); break;
                    case 3: Toolbox(centre, random); break;
                    case 4: CableReel(centre); break;
                    case 5: Bin(centre); break;
                    default: FloorPipes(centre, random); break;
                }
            }
        }

        /// <summary>
        /// A tile is usable for dressing when it and its four orthogonal neighbours are
        /// clear. The original rule demanded a clear 3x3, which rejected most of the
        /// floor and left the frame measurably emptier than the reference games.
        /// Diagonal neighbours are allowed to be occupied: a crate tucked against the
        /// corner of a machine looks deliberate rather than misplaced.
        /// </summary>
        /// <summary>Manhattan distance to the closest occupied tile, capped for sorting.</summary>
        private static int DistanceToNearestBuilding(SimWorld world, GridPos tile)
        {
            for (int radius = 1; radius <= 6; radius++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (Mathf.Abs(dx) + Mathf.Abs(dy) != radius) continue;

                        var probe = new GridPos(tile.X + dx, tile.Y + dy);
                        if (!world.Map.InBounds(probe)) continue;
                        if (!world.Map.IsFree(probe)) return radius;
                    }
                }
            }
            return 99;
        }

        private static bool IsIsolated(SimWorld world, GridPos tile)
        {
            if (!world.Map.IsFree(tile)) return false;

            for (int d = 0; d < 4; d++)
            {
                var probe = tile.Step((Direction)d);
                if (!world.Map.InBounds(probe)) return false;
                if (!world.Map.IsFree(probe)) return false;
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
                Box("Crate", Accent(random), new Vector3(0.3f, 0.26f, 0.3f),
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
                Cylinder("Drum", Accent(random), new Vector3(0.28f, 0.24f, 0.28f),
                    centre + new Vector3(Mathf.Cos(angle) * 0.22f, 0.24f, Mathf.Sin(angle) * 0.22f));
            }
        }

        private void Toolbox(Vector3 centre, DeterministicRandom random)
        {
            Box("ToolboxBody", Accent(random), new Vector3(0.5f, 0.24f, 0.32f), centre + new Vector3(0f, 0.12f, 0f));
            Box("ToolboxLid", _paint, new Vector3(0.52f, 0.05f, 0.34f), centre + new Vector3(0f, 0.26f, 0f));
            Box("ToolboxHandle", _hazard, new Vector3(0.2f, 0.04f, 0.04f), centre + new Vector3(0f, 0.31f, 0f));

            if (random.NextInt(2) != 0) return;

            // Occasional second box stacked on top, so the props do not all read as one
            // repeated silhouette.
            Box("ToolboxSecond", Accent(random), new Vector3(0.34f, 0.2f, 0.26f), centre + new Vector3(0.05f, 0.4f, 0.02f));
        }

        private void CableReel(Vector3 centre)
        {
            var axle = Cylinder("ReelAxle", _timber, new Vector3(0.16f, 0.3f, 0.16f), centre + new Vector3(0f, 0.3f, 0f));
            axle.localRotation = Quaternion.Euler(90f, 0f, 0f);

            for (int side = -1; side <= 1; side += 2)
            {
                var disc = Cylinder("ReelSide", _timber, new Vector3(0.58f, 0.06f, 0.58f),
                    centre + new Vector3(0f, 0.3f, side * 0.15f));
                disc.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }

            var cable = Cylinder("Cable", _drum, new Vector3(0.44f, 0.24f, 0.44f), centre + new Vector3(0f, 0.3f, 0f));
            cable.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }

        private void Bin(Vector3 centre)
        {
            Cylinder("BinBody", Accent(new DeterministicRandom((uint)(centre.x * 31 + centre.z * 17))),
                new Vector3(0.42f, 0.5f, 0.42f), centre + new Vector3(0f, 0.25f, 0f));
            Cylinder("BinLid", _paint, new Vector3(0.46f, 0.06f, 0.46f), centre + new Vector3(0f, 0.53f, 0f));
        }

        private void FloorPipes(Vector3 centre, DeterministicRandom random)
        {
            bool alongX = random.NextInt(2) == 0;
            int count = random.NextInt(2, 4);
            var pipeMaterial = Accent(random);

            for (int i = 0; i < count; i++)
            {
                float offset = -0.18f + i * 0.18f;
                var pipe = Cylinder("Pipe", pipeMaterial,
                    new Vector3(0.13f, 0.5f, 0.13f),
                    centre + (alongX ? new Vector3(0f, 0.07f, offset) : new Vector3(offset, 0.07f, 0f)));

                pipe.localRotation = alongX
                    ? Quaternion.Euler(0f, 0f, 90f)
                    : Quaternion.Euler(90f, 0f, 0f);
            }

            // Bracket clamps, which are what make a pipe run read as installed.
            Box("PipeClamp", _paint, alongX ? new Vector3(0.08f, 0.16f, 0.6f) : new Vector3(0.6f, 0.16f, 0.08f),
                centre + new Vector3(0f, 0.08f, 0f));
        }

        private Material Accent(DeterministicRandom random)
            => _accents[random.NextInt(_accents.Length)];

        // ------------------------------------------------------------- primitives

        private Transform Box(string name, Material material, Vector3 scale, Vector3 position)
            => MeshObjects.Box(name, _root, material, scale, position);

        private Transform Cylinder(string name, Material material, Vector3 scale, Vector3 position)
            => MeshObjects.Cylinder(name, _root, material, scale, position);
    }
}
