namespace Worker.Core
{
    /// <summary>
    /// xorshift128 PRNG. The simulation must never call UnityEngine.Random or
    /// System.Random: replay determinism depends on every random draw coming from
    /// a seeded state that is captured in the save file.
    /// </summary>
    public sealed class DeterministicRandom
    {
        private uint _x, _y, _z, _w;

        public DeterministicRandom(uint seed)
        {
            Reseed(seed);
        }

        public void Reseed(uint seed)
        {
            // splitmix-style expansion so that adjacent seeds diverge immediately.
            _x = seed == 0 ? 0x9E3779B9u : seed;
            _y = _x * 1812433253u + 1u;
            _z = _y * 1812433253u + 1u;
            _w = _z * 1812433253u + 1u;
            for (int i = 0; i < 8; i++) NextUInt();
        }

        public uint NextUInt()
        {
            uint t = _x ^ (_x << 11);
            _x = _y; _y = _z; _z = _w;
            _w = _w ^ (_w >> 19) ^ t ^ (t >> 8);
            return _w;
        }

        /// <summary>Uniform in [0, maxExclusive). Rejection sampling keeps the distribution exact.</summary>
        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 1) return 0;
            uint bound = (uint)maxExclusive;
            uint threshold = (uint)(-(int)bound) % bound;
            while (true)
            {
                uint r = NextUInt();
                if (r >= threshold) return (int)(r % bound);
            }
        }

        public int NextInt(int minInclusive, int maxExclusive)
            => minInclusive + NextInt(maxExclusive - minInclusive);

        /// <summary>Returns true with probability numerator/denominator.</summary>
        public bool Chance(int numerator, int denominator)
            => NextInt(denominator) < numerator;

        public uint[] SaveState() => new[] { _x, _y, _z, _w };

        public void LoadState(uint[] state)
        {
            _x = state[0]; _y = state[1]; _z = state[2]; _w = state[3];
        }
    }
}
