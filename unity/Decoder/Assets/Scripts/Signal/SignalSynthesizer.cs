using System;
using System.Collections.Generic;

namespace Decoder.Signal
{
    /// <summary>
    /// 短波接收机的信号合成。这是玩法的心脏：玩家听到的每一声都由它生成。
    ///
    /// 物理模型是简化的，但保留了三个决定手感的真实特性：
    ///
    /// 1. 差频音调。CW 接收机靠本振与载波拍频出可听音，所以调偏了音调会变，
    ///    调准了才落在标称音高上。玩家因此可以"听音高找中心"，
    ///    这比看信号强度表更快，也是真实报务员的做法。
    /// 2. 带通响应。偏离中心越远幅度衰减越厉害，超出带宽就完全听不到。
    /// 3. 键控软化。真实发射机的升降沿是平滑的，硬开关会产生"咔哒"爆音。
    ///
    /// 类本身不依赖 UnityEngine，可以在无音频设备的环境里直接测采样值。
    /// </summary>
    public sealed class SignalSynthesizer
    {
        /// <summary>接收机的标称边带音高，调准时听到的音调。</summary>
        public const float NominalToneHz = 700f;

        /// <summary>半带宽。失配超过这个值就完全收不到。</summary>
        public const float BandwidthKHz = 2.4f;

        /// <summary>键控升降沿时长。低于 2ms 会有明显爆音，高于 12ms 会糊掉高速电码。</summary>
        public const float KeyRampSeconds = 0.005f;

        private readonly int _sampleRate;
        private readonly NoiseSource _noise;
        private readonly List<Station> _stations = new List<Station>();

        private double _carrierPhase;
        private float _keyEnvelope;
        private double _elapsedSeconds;

        public SignalSynthesizer(int sampleRate, int noiseSeed)
        {
            if (sampleRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            }

            _sampleRate = sampleRate;
            _noise = new NoiseSource(noiseSeed);
        }

        /// <summary>玩家当前调谐到的频率，单位千赫。</summary>
        public float TunedKHz { get; set; } = 7000f;

        /// <summary>底噪音量。短波底噪永远存在，静默时它就是玩家听到的全部。</summary>
        public float NoiseFloor { get; set; } = 0.16f;

        /// <summary>大气爆音的每采样触发概率。设为 0 关闭。</summary>
        public float CracklePerSample { get; set; } = 0.00004f;

        public float MasterGain { get; set; } = 0.8f;

        public IReadOnlyList<Station> Stations => _stations;

        public void AddStation(Station station)
        {
            if (station == null)
            {
                throw new ArgumentNullException(nameof(station));
            }

            _stations.Add(station);
        }

        public void ClearStations()
        {
            _stations.Clear();
        }

        public void ResetTime()
        {
            _elapsedSeconds = 0d;
            _carrierPhase = 0d;
            _keyEnvelope = 0f;
            foreach (var station in _stations)
            {
                station.Rewind();
            }
        }

        /// <summary>
        /// 失配到听感的映射。返回 0 表示完全收不到，1 表示正中。
        /// 用余弦窗而不是矩形窗，因为矩形窗会让信号在边界上突然出现，
        /// 玩家扫过频率时会觉得像开关而不像调收音机。
        /// </summary>
        public static float BandpassResponse(float detuneKHz)
        {
            var d = Math.Abs(detuneKHz);
            if (d >= BandwidthKHz)
            {
                return 0f;
            }

            var t = d / BandwidthKHz;
            return 0.5f * (1f + (float)Math.Cos(Math.PI * t));
        }

        /// <summary>
        /// 差频音调。调准时是标称音高，往上偏调高，往下偏调低。
        /// 1 kHz 失配对应 700 Hz 音调偏移，这个比例让整个带宽内的音调变化
        /// 覆盖大约两个八度，玩家能清楚分辨方向。
        /// </summary>
        public static float ToneForDetune(float detuneKHz)
        {
            var tone = NominalToneHz + detuneKHz * 700f;
            return tone < 40f ? 40f : tone;
        }

        /// <summary>
        /// 渲染一段单声道采样。返回值写进 buffer 的前 count 个位置。
        /// 这个方法要能在音频线程里跑，所以内部不做任何分配。
        /// </summary>
        public void Render(float[] buffer, int count)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (count > buffer.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(count),
                    "请求的采样数超过缓冲区长度");
            }

            var dt = 1.0 / _sampleRate;
            var rampStep = KeyRampSeconds > 0f ? 1f / (KeyRampSeconds * _sampleRate) : 1f;

            for (var i = 0; i < count; i++)
            {
                var sample = _noise.NextPink() * NoiseFloor;
                sample += _noise.NextCrackle(CracklePerSample, 0.7f);

                // 只混入带内最强的那一路。真实接收机在同一时刻也是被最强信号主导，
                // 而且这样能避免多台同时可闻时听感糊成一团。
                Station best = null;
                var bestResponse = 0f;
                for (var s = 0; s < _stations.Count; s++)
                {
                    var station = _stations[s];
                    var response = BandpassResponse(station.FrequencyKHz - TunedKHz);
                    if (response > bestResponse)
                    {
                        bestResponse = response;
                        best = station;
                    }
                }

                if (best != null)
                {
                    var keyDown = best.IsKeyDown(_elapsedSeconds);
                    var target = keyDown ? 1f : 0f;
                    if (_keyEnvelope < target)
                    {
                        _keyEnvelope = Math.Min(target, _keyEnvelope + rampStep);
                    }
                    else if (_keyEnvelope > target)
                    {
                        _keyEnvelope = Math.Max(target, _keyEnvelope - rampStep);
                    }

                    if (_keyEnvelope > 0f)
                    {
                        var tone = ToneForDetune(best.FrequencyKHz - TunedKHz);
                        _carrierPhase += 2.0 * Math.PI * tone * dt;
                        if (_carrierPhase > 2.0 * Math.PI)
                        {
                            _carrierPhase -= 2.0 * Math.PI;
                        }

                        sample += (float)Math.Sin(_carrierPhase)
                                  * _keyEnvelope * bestResponse * best.Strength;
                    }
                }
                else
                {
                    _keyEnvelope = Math.Max(0f, _keyEnvelope - rampStep);
                }

                _elapsedSeconds += dt;

                var output = sample * MasterGain;
                buffer[i] = output > 1f ? 1f : output < -1f ? -1f : output;
            }
        }

        /// <summary>
        /// 频段上的一个电台。时序在构造时一次性展开，
        /// 渲染时只做二分查找，不做分配。
        /// </summary>
        public sealed class Station
        {
            private readonly float[] _cumulativeSeconds;
            private readonly bool[] _keyStates;
            private readonly double _loopSeconds;

            public Station(string callsign, float frequencyKHz, string message,
                float wordsPerMinute, float strength = 1f, bool loop = true,
                float loopGapSeconds = 2f)
            {
                Callsign = callsign ?? string.Empty;
                FrequencyKHz = frequencyKHz;
                Message = message ?? string.Empty;
                WordsPerMinute = wordsPerMinute;
                Strength = strength;
                Loop = loop;

                var timeline = MorseCode.BuildTimeline(MorseCode.Encode(Message), wordsPerMinute);
                _cumulativeSeconds = new float[timeline.Count];
                _keyStates = new bool[timeline.Count];

                var accumulated = 0f;
                for (var i = 0; i < timeline.Count; i++)
                {
                    accumulated += timeline[i].Seconds;
                    _cumulativeSeconds[i] = accumulated;
                    _keyStates[i] = timeline[i].KeyDown;
                }

                // 循环播报之间留一段静默，否则玩家分不清一遍结束和下一遍开始。
                _loopSeconds = accumulated + Math.Max(0f, loopGapSeconds);
                TotalSeconds = accumulated;
            }

            public string Callsign { get; }
            public float FrequencyKHz { get; }
            public string Message { get; }
            public float WordsPerMinute { get; }
            public float Strength { get; }
            public bool Loop { get; }
            public float TotalSeconds { get; }

            /// <summary>发报起始时刻的偏移，用于让不同电台错开而不是同时开始。</summary>
            public double StartOffsetSeconds { get; set; }

            public void Rewind()
            {
            }

            public bool IsKeyDown(double elapsedSeconds)
            {
                if (_cumulativeSeconds.Length == 0)
                {
                    return false;
                }

                var t = elapsedSeconds - StartOffsetSeconds;
                if (t < 0d)
                {
                    return false;
                }

                if (Loop)
                {
                    t %= _loopSeconds;
                }
                else if (t >= TotalSeconds)
                {
                    return false;
                }

                if (t >= TotalSeconds)
                {
                    return false;
                }

                var lo = 0;
                var hi = _cumulativeSeconds.Length - 1;
                while (lo < hi)
                {
                    var mid = (lo + hi) / 2;
                    if (t < _cumulativeSeconds[mid])
                    {
                        hi = mid;
                    }
                    else
                    {
                        lo = mid + 1;
                    }
                }

                return _keyStates[lo];
            }
        }
    }
}
