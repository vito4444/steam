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
        private double _lastSeenByMainThread;

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

        /// <summary>
        /// 当前带内最强信号的接收强度，0 表示什么都没收到。供信号强度表和调谐指示器读取。
        ///
        /// 这个值由 EvaluateReception 更新，而不是渲染的副产品。两者分开是必须的：
        /// 玩家可能把音量关到零，音频设备也可能初始化失败，
        /// 但信号强度表和调谐方向指示必须照常工作——它们是听觉线索的视觉等价物，
        /// 听障玩家只靠它们就要能玩下去。
        /// </summary>
        public float CurrentSignalLevel { get; private set; }

        /// <summary>当前最强信号的失配量，正表示电台在调谐点上方。</summary>
        public float CurrentDetuneKHz { get; private set; }

        /// <summary>当前收到的电台，没有则为 null。</summary>
        public Station CurrentStation { get; private set; }

        /// <summary>发报时钟。电台的键控时序都以它为准。</summary>
        public double ElapsedSeconds => _elapsedSeconds;

        /// <summary>
        /// 在音频线程停摆时由主线程接管推进发报时钟。
        ///
        /// 时钟正常情况下由音频渲染推进，一个采样一个采样地走，这样声音和时序严格同步。
        /// 但音频设备可能起不来，玩家也可能把音量关到零，此时渲染不再发生，
        /// 时钟就会冻住——电台不再发报，示波器画出一条直线，游戏看上去像卡死了。
        ///
        /// 每帧调用一次。只有确认音频线程这一帧没有推进过时钟，主线程才接手，
        /// 两者不会重复累加。
        /// </summary>
        public void AdvanceIfAudioStalled(double deltaSeconds)
        {
            if (_elapsedSeconds > _lastSeenByMainThread + 1e-9)
            {
                _lastSeenByMainThread = _elapsedSeconds;
                return;
            }

            if (deltaSeconds > 0d)
            {
                _elapsedSeconds += deltaSeconds;
            }

            _lastSeenByMainThread = _elapsedSeconds;
        }

        /// <summary>
        /// 当前调谐点上、指定时刻的解调包络，0 到 1。
        ///
        /// 这是示波器画的东西，也是听障玩家读电码的唯一途径：
        /// 包络的宽窄就是点和划的区别，所以它必须和耳朵听到的严格一致，
        /// 不能是一个"看起来差不多"的装饰动画。
        /// </summary>
        public float EnvelopeAt(double timeSeconds)
        {
            var station = CurrentStation;
            if (station == null || !station.IsKeyDown(timeSeconds))
            {
                return 0f;
            }

            return CurrentSignalLevel;
        }

        /// <summary>
        /// 计算当前调谐点上收到了什么。纯计算，不产生音频，可以在主线程按帧调用。
        /// 返回带内最强的电台，同时更新对外暴露的接收状态。
        /// </summary>
        public Station EvaluateReception()
        {
            Station best = null;
            var bestLevel = 0f;

            for (var s = 0; s < _stations.Count; s++)
            {
                var station = _stations[s];
                // 按实际到达功率比较，而不是只看带通响应。
                // 只看响应的话，一个几乎调准的弱台会压过一个稍微偏一点的强台，
                // 而真实接收机里听到的永远是功率最大的那一路。
                var level = BandpassResponse(station.FrequencyKHz - TunedKHz) * station.Strength;
                if (level > bestLevel)
                {
                    bestLevel = level;
                    best = station;
                }
            }

            CurrentStation = best;
            CurrentSignalLevel = bestLevel;
            CurrentDetuneKHz = best == null ? 0f : best.FrequencyKHz - TunedKHz;
            return best;
        }

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
            _lastSeenByMainThread = 0d;
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
                var best = EvaluateReception();
                var bestResponse = best == null
                    ? 0f
                    : BandpassResponse(best.FrequencyKHz - TunedKHz);

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
