using System;

namespace Decoder.Signal
{
    /// <summary>
    /// 确定性噪声源。同一个种子必然产生同一段波形，这一点很重要：
    /// 玩家读档回到同一个班次时听到的噪声应当一致，
    /// 音频相关的测试也才有可能断言具体数值。
    ///
    /// 用 xorshift 而不是 System.Random，因为它无需分配、状态只有 32 位，
    /// 可以在音频线程里安全使用。
    /// </summary>
    public sealed class NoiseSource
    {
        private uint _state;
        private float _pink0, _pink1, _pink2;

        public NoiseSource(int seed)
        {
            // 状态为 0 时 xorshift 会永远输出 0，必须避开。
            _state = seed == 0 ? 0x9E3779B9u : unchecked((uint)seed);
        }

        private uint NextBits()
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return _state;
        }

        /// <summary>均匀分布的白噪声，范围 [-1, 1)。</summary>
        public float NextWhite()
        {
            return NextBits() / 2147483648f - 1f;
        }

        /// <summary>
        /// 粉噪声（1/f）。真实的大气噪声低频能量更强，
        /// 纯白噪声听上去像电视雪花，粉噪声才像短波底噪。
        /// 这里用 Voss-McCartney 的三阶简化：三个不同时间常数的一阶低通叠加。
        /// </summary>
        public float NextPink()
        {
            var white = NextWhite();
            _pink0 = 0.99765f * _pink0 + white * 0.0990460f;
            _pink1 = 0.96300f * _pink1 + white * 0.2965164f;
            _pink2 = 0.57000f * _pink2 + white * 1.0526913f;
            var pink = _pink0 + _pink1 + _pink2 + white * 0.1848f;
            // 三阶叠加后的峰值约在 ±3.5，归一化到与白噪声相当的量级。
            return pink * 0.2f;
        }

        /// <summary>
        /// 大气爆音（static crash）。短波频段常见的雷电干扰，
        /// 表现为随机出现的尖锐脉冲。返回值是这一采样点上的冲击幅度，
        /// 大多数时候是 0。
        /// </summary>
        public float NextCrackle(float probabilityPerSample, float amplitude)
        {
            if (probabilityPerSample <= 0f)
            {
                return 0f;
            }

            var roll = NextBits() / 4294967296f;
            if (roll >= probabilityPerSample)
            {
                return 0f;
            }

            return NextWhite() * amplitude;
        }

        public void Reset(int seed)
        {
            _state = seed == 0 ? 0x9E3779B9u : unchecked((uint)seed);
            _pink0 = _pink1 = _pink2 = 0f;
        }
    }
}
