using System;
using System.Collections.Generic;
using System.Linq;

namespace Hunter.Worldgen
{
    public enum SegmentKind
    {
        /// Where the hunter lands. Always first, always open enough to orient in.
        Entrance,
        /// Wide, columned, the default. Long sightlines and cover.
        Colonnade,
        /// Tight and roofed. Sightlines collapse; melee ambush territory.
        NarrowGallery,
        /// Open to the sky. The key light reaches the floor here, so it reads as relief.
        Courtyard,
        /// Roof and floor have come down. Rubble cover, broken movement.
        CollapsedSpan,
        /// Holds the bell. Exactly one per layout.
        BellChamber,
        /// The best loot in the ruin, placed deep. Exactly one per layout.
        Vault,
    }

    public sealed class Branch
    {
        public float AlongZ;
        public int Side;          // -1 left, +1 right
        public float Depth;
        public bool HasCache;
    }

    public sealed class Segment
    {
        public SegmentKind Kind;
        public float StartZ;
        public float Length;
        public float HalfWidth;
        public bool RoofIntact;
        public int LootCaches;
        public readonly List<Branch> Branches = new();

        public float EndZ => StartZ + Length;
        public float CentreZ => StartZ + Length * 0.5f;
    }

    /// A planned ruin. Geometry generation reads this; it does not decide anything itself.
    public sealed class LayoutPlan
    {
        public readonly List<Segment> Segments = new();
        public int Seed;

        public float TotalLength => Segments.Count == 0 ? 0f : Segments[^1].EndZ;
        public Segment Bell => Segments.FirstOrDefault(s => s.Kind == SegmentKind.BellChamber);
        public Segment Vault => Segments.FirstOrDefault(s => s.Kind == SegmentKind.Vault);
        public int TotalCaches => Segments.Sum(s => s.LootCaches + s.Branches.Count(b => b.HasCache));
    }

    /// Plans a ruin as a sequence of segments along one axis, with side branches.
    ///
    /// Concept doc A.9 sets the constraint this follows: purely procedural layouts read as
    /// repetitive, so each map keeps two or three hand-authored landmarks — here the bell
    /// chamber and the vault — and the generator only decides what connects them. That is
    /// the same split Hades and Deep Rock Galactic use.
    ///
    /// Pure logic with no Unity dependency, so the rules that matter (there is always
    /// exactly one bell, the vault is always deep, the entrance is always first) are
    /// assertable in tests rather than eyeballed.
    public static class LayoutPlanner
    {
        public const int MinSegments = 5;
        public const int MaxSegments = 12;

        public static LayoutPlan Plan(int seed, int segmentCount = 8)
        {
            segmentCount = Math.Clamp(segmentCount, MinSegments, MaxSegments);
            var rng = new Random(seed);
            var plan = new LayoutPlan { Seed = seed };

            // The bell sits in the back half so reaching it is a commitment, but never
            // dead last, because the vault has to be deeper than the way home.
            int bellIndex = segmentCount / 2 + rng.Next(0, Math.Max(1, segmentCount / 4));
            int vaultIndex = segmentCount - 1;
            bellIndex = Math.Min(bellIndex, vaultIndex - 1);

            float z = 0f;
            SegmentKind previous = SegmentKind.Entrance;

            for (int i = 0; i < segmentCount; i++)
            {
                SegmentKind kind;
                if (i == 0) kind = SegmentKind.Entrance;
                else if (i == bellIndex) kind = SegmentKind.BellChamber;
                else if (i == vaultIndex) kind = SegmentKind.Vault;
                else kind = PickBody(rng, previous);

                var segment = BuildSegment(kind, z, rng);
                AddBranches(segment, rng, kind);
                plan.Segments.Add(segment);

                z = segment.EndZ;
                previous = kind;
            }

            return plan;
        }

        /// Colonnade is the connective tissue and may repeat; everything else must not
        /// follow itself, or the ruin reads as a corridor of copies.
        static SegmentKind PickBody(Random rng, SegmentKind previous)
        {
            Span<SegmentKind> pool = stackalloc SegmentKind[]
            {
                SegmentKind.Colonnade, SegmentKind.Colonnade,
                SegmentKind.NarrowGallery, SegmentKind.Courtyard, SegmentKind.CollapsedSpan,
            };

            for (int attempt = 0; attempt < 8; attempt++)
            {
                var candidate = pool[rng.Next(pool.Length)];
                if (candidate != previous || candidate == SegmentKind.Colonnade) return candidate;
            }
            return SegmentKind.Colonnade;
        }

        static Segment BuildSegment(SegmentKind kind, float startZ, Random rng)
        {
            float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            return kind switch
            {
                SegmentKind.Entrance => new Segment
                {
                    Kind = kind, StartZ = startZ, Length = Range(9f, 12f), HalfWidth = Range(5.5f, 6.5f),
                    RoofIntact = false, LootCaches = 0,
                },
                SegmentKind.Colonnade => new Segment
                {
                    Kind = kind, StartZ = startZ, Length = Range(14f, 20f), HalfWidth = Range(5f, 6.4f),
                    RoofIntact = rng.NextDouble() < 0.65, LootCaches = rng.Next(1, 3),
                },
                SegmentKind.NarrowGallery => new Segment
                {
                    Kind = kind, StartZ = startZ, Length = Range(10f, 15f), HalfWidth = Range(2.6f, 3.6f),
                    RoofIntact = true, LootCaches = rng.Next(1, 3),
                },
                SegmentKind.Courtyard => new Segment
                {
                    Kind = kind, StartZ = startZ, Length = Range(13f, 18f), HalfWidth = Range(7f, 9f),
                    RoofIntact = false, LootCaches = rng.Next(0, 2),
                },
                SegmentKind.CollapsedSpan => new Segment
                {
                    Kind = kind, StartZ = startZ, Length = Range(8f, 13f), HalfWidth = Range(4f, 5.5f),
                    RoofIntact = false, LootCaches = rng.Next(1, 3),
                },
                SegmentKind.BellChamber => new Segment
                {
                    Kind = kind, StartZ = startZ, Length = Range(12f, 16f), HalfWidth = Range(6f, 7.5f),
                    RoofIntact = true, LootCaches = 1,
                },
                SegmentKind.Vault => new Segment
                {
                    Kind = kind, StartZ = startZ, Length = Range(11f, 15f), HalfWidth = Range(5f, 6.5f),
                    RoofIntact = true, LootCaches = 2,
                },
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
        }

        /// Side rooms. They exist to reward players who leave the main axis, so most of
        /// them hold a cache; a branch with nothing in it is just a dead end.
        static void AddBranches(Segment segment, Random rng, SegmentKind kind)
        {
            int count = kind switch
            {
                SegmentKind.Entrance => 0,
                SegmentKind.NarrowGallery => rng.Next(0, 2),
                SegmentKind.Courtyard => rng.Next(1, 3),
                SegmentKind.Vault => 0,
                _ => rng.Next(0, 3),
            };

            for (int i = 0; i < count; i++)
            {
                segment.Branches.Add(new Branch
                {
                    AlongZ = segment.StartZ + (float)rng.NextDouble() * segment.Length,
                    Side = rng.NextDouble() < 0.5 ? -1 : 1,
                    Depth = 4f + (float)rng.NextDouble() * 4.5f,
                    HasCache = rng.NextDouble() < 0.7,
                });
            }
        }
    }
}
