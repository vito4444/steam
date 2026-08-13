using System;

namespace Undertown.Core.Determinism
{
    /// <summary>
    /// xorshift128 with fully serialisable state. The simulation never touches
    /// UnityEngine.Random, because a save that cannot be replayed identically cannot be
    /// unit tested and cannot be debugged from a bug report.
    /// </summary>
    [Serializable]
    public struct DeterministicRandom
    {
        private uint _x, _y, _z, _w;

        public DeterministicRandom(uint seed)
        {
            // Splitmix-style scrambling so that adjacent seeds do not produce correlated streams.
            _x = Scramble(seed == 0 ? 0x9E3779B9u : seed);
            _y = Scramble(_x);
            _z = Scramble(_y);
            _w = Scramble(_z);
        }

        private static uint Scramble(uint v)
        {
            v ^= v << 13;
            v ^= v >> 17;
            v ^= v << 5;
            return v == 0 ? 0x6C078965u : v;
        }

        public uint NextUInt()
        {
            uint t = _x ^ (_x << 11);
            _x = _y; _y = _z; _z = _w;
            _w = _w ^ (_w >> 19) ^ t ^ (t >> 8);
            return _w;
        }

        /// <summary>Uniform in [0, exclusiveMax). Rejection sampled so the distribution has no modulo bias.</summary>
        public int NextInt(int exclusiveMax)
        {
            if (exclusiveMax <= 0) throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
            uint bound = (uint)exclusiveMax;
            uint limit = uint.MaxValue - (uint.MaxValue % bound) - 1;
            uint value;
            do { value = NextUInt(); } while (value > limit);
            return (int)(value % bound);
        }

        /// <summary>Uniform in [inclusiveMin, exclusiveMax).</summary>
        public int NextInt(int inclusiveMin, int exclusiveMax)
        {
            if (exclusiveMax <= inclusiveMin) throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
            return inclusiveMin + NextInt(exclusiveMax - inclusiveMin);
        }

        /// <summary>Returns true with the given per mille probability. 1000 always, 0 never.</summary>
        public bool Chance(int perMille) => perMille > 0 && NextInt(1000) < perMille;

        public DeterministicRandom Fork(uint salt)
        {
            var forked = new DeterministicRandom(_w ^ Scramble(salt));
            NextUInt();
            return forked;
        }
    }
}
