namespace Maner.Sim
{
    /// <summary>
    /// 仿真专用伪随机数发生器。刻意不使用 System.Random 与 UnityEngine.Random，
    /// 因为二者的内部实现都不保证跨版本、跨平台一致，会破坏「相同输入产生相同输出」这条硬要求。
    /// 采用 xorshift128+，状态可完整序列化。
    /// </summary>
    public sealed class DeterministicRandom
    {
        ulong s0;
        ulong s1;

        public DeterministicRandom(ulong seed)
        {
            // SplitMix64 做种子扩散，避免低熵种子导致前几个输出相关。
            s0 = SplitMix(ref seed);
            s1 = SplitMix(ref seed);
            if (s0 == 0 && s1 == 0)
            {
                s0 = 0x9E3779B97F4A7C15UL;
            }
        }

        static ulong SplitMix(ref ulong x)
        {
            x += 0x9E3779B97F4A7C15UL;
            ulong z = x;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public ulong NextULong()
        {
            ulong x = s0;
            ulong y = s1;
            s0 = y;
            x ^= x << 23;
            s1 = x ^ y ^ (x >> 17) ^ (y >> 26);
            return s1 + y;
        }

        /// <summary>返回 [0,1) 区间的双精度值。</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / 9007199254740992.0);

        /// <summary>返回 [min,max) 区间的双精度值。</summary>
        public double Range(double min, double max) => min + NextDouble() * (max - min);

        /// <summary>返回 [0,exclusiveMax) 区间的整数。</summary>
        public int Next(int exclusiveMax) => exclusiveMax <= 0 ? 0 : (int)(NextULong() % (ulong)exclusiveMax);

        public (ulong, ulong) SaveState() => (s0, s1);

        public void LoadState(ulong a, ulong b)
        {
            s0 = a;
            s1 = b;
        }
    }
}
