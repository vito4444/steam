using System;
using System.Text;

namespace Monster.Rules
{
    /// <summary>A 4x4 monochrome grid that stands in for a face.
    ///
    /// The player's job includes comparing the photograph on the permit with the person on
    /// the cabin monitor. Doing that with actual portrait art would need dozens of
    /// authored faces plus subtly altered variants of each, which is exactly the kind of
    /// content cost that sinks a small project. A deterministic grid gives the same
    /// perceptual task -- look at two small patterns and decide whether they are the same
    /// -- for no art at all, and it reads as a degraded photobooth print under a CRT.
    ///
    /// The mismatch is deliberately near, not obvious: a wrong code differs from the right
    /// one in two or three cells out of sixteen, so it has to be actually looked at.</summary>
    public readonly struct PortraitCode : IEquatable<PortraitCode>
    {
        public const int Size = 4;

        private readonly ushort _bits;

        private PortraitCode(ushort bits) => _bits = bits;

        public static PortraitCode FromSubject(in SubjectAttributes subject)
        {
            unchecked
            {
                var hash = 2166136261u;
                foreach (var c in subject.Name ?? string.Empty)
                {
                    hash = (hash ^ c) * 16777619u;
                }

                hash = (hash ^ (uint)subject.BirthYear) * 16777619u;
                foreach (var c in subject.PermitSerial ?? string.Empty)
                {
                    hash = (hash ^ c) * 16777619u;
                }

                // Fold to 16 bits and force a middling density; an all-dark or all-light
                // grid carries no information to compare.
                var bits = (ushort)((hash ^ (hash >> 16)) & 0xFFFF);
                return new PortraitCode(Balance(bits));
            }
        }

        /// <summary>The pattern actually seen on the monitor, which differs from the permit
        /// when the photograph does not match the bearer.</summary>
        public static PortraitCode AsObserved(in SubjectAttributes subject)
        {
            var truth = FromSubject(subject);
            if (subject.PhotoMatchesFace)
            {
                return truth;
            }

            // Flip three cells chosen deterministically from the same subject, so the
            // difference is stable across runs but not in a fixed position.
            unchecked
            {
                var hash = 2166136261u;
                foreach (var c in (subject.Name ?? string.Empty) + "|observed")
                {
                    hash = (hash ^ c) * 16777619u;
                }

                var bits = truth._bits;
                for (var i = 0; i < 3; i++)
                {
                    var index = (int)(hash % 16);
                    bits ^= (ushort)(1 << index);
                    hash = hash * 16777619u + 7u;
                }

                return new PortraitCode(bits);
            }
        }

        /// <summary>The reflection on the rear cabin feed. A subject whose reflection is
        /// inconsistent shows a pattern that is not the mirror of the one on the glass.</summary>
        public static PortraitCode AsReflected(in SubjectAttributes subject)
        {
            var observed = AsObserved(subject);
            var mirrored = observed.MirroredHorizontally();
            if (subject.ReflectionConsistent)
            {
                return mirrored;
            }

            unchecked
            {
                var bits = (ushort)(mirrored._bits ^ 0b0000_0110_0110_0000);
                return new PortraitCode(Balance(bits));
            }
        }

        public bool Get(int x, int y) => (_bits & (1 << (y * Size + x))) != 0;

        public PortraitCode MirroredHorizontally()
        {
            ushort result = 0;
            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    if (Get(x, y))
                    {
                        result |= (ushort)(1 << (y * Size + (Size - 1 - x)));
                    }
                }
            }

            return new PortraitCode(result);
        }

        /// <summary>How many of the sixteen cells differ. Used by tests to assert that a
        /// mismatch is neither invisible nor unmissable.</summary>
        public int DistanceTo(PortraitCode other)
        {
            var diff = (ushort)(_bits ^ other._bits);
            var count = 0;
            while (diff != 0)
            {
                count += diff & 1;
                diff >>= 1;
            }

            return count;
        }

        /// <summary>Nudges a pattern towards six to ten lit cells. Anything outside that
        /// range is a grey square or a black square and cannot be compared by eye.</summary>
        private static ushort Balance(ushort bits)
        {
            int Population(ushort v)
            {
                var n = 0;
                while (v != 0)
                {
                    n += v & 1;
                    v >>= 1;
                }

                return n;
            }

            var index = 0;
            while (Population(bits) < 6 && index < 16)
            {
                bits |= (ushort)(1 << index);
                index += 3;
            }

            index = 0;
            while (Population(bits) > 10 && index < 16)
            {
                bits &= (ushort)~(1 << index);
                index += 3;
            }

            return bits;
        }

        public bool Equals(PortraitCode other) => _bits == other._bits;

        public override bool Equals(object obj) => obj is PortraitCode other && Equals(other);

        public override int GetHashCode() => _bits;

        /// <summary>Renders the grid with block characters so it can be printed straight
        /// into a TextMeshPro field on the permit.</summary>
        public string ToBlockRows(char lit = '\u2588', char dark = '\u2591')
        {
            var builder = new StringBuilder(Size * (Size * 2 + 1));
            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    var c = Get(x, y) ? lit : dark;
                    builder.Append(c).Append(c);
                }

                if (y < Size - 1)
                {
                    builder.Append('\n');
                }
            }

            return builder.ToString();
        }

        public override string ToString() => Convert.ToString(_bits, 2).PadLeft(16, '0');
    }
}
