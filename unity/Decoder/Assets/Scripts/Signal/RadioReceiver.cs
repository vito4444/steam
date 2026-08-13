using System;
using System.Collections.Generic;
using UnityEngine;

namespace Decoder.Signal
{
    /// <summary>
    /// 把 SignalSynthesizer 接到 Unity 音频系统。合成在音频线程完成，
    /// 所以这里不能调用任何 Unity API，也不能分配内存。
    ///
    /// 频率状态由主线程写、音频线程读。float 的读写在 .NET 上是原子的，
    /// 不会读到撕裂值，因此不加锁——加锁反而会让音频线程阻塞在主线程上，
    /// 那才是真正会爆音的做法。
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class RadioReceiver : MonoBehaviour
    {
        [Header("调谐")]
        [Tooltip("频段下限，单位千赫")]
        public float bandLowKHz = 6800f;

        [Tooltip("频段上限，单位千赫")]
        public float bandHighKHz = 7200f;

        [Tooltip("当前调谐频率，单位千赫")]
        public float tunedKHz = 7000f;

        [Header("接收机")]
        [Range(0f, 1f)] public float noiseFloor = 0.16f;
        [Range(0f, 1f)] public float masterGain = 0.7f;
        [Tooltip("大气爆音密度。0 关闭")]
        [Range(0f, 0.001f)] public float cracklePerSample = 0.00004f;
        [Tooltip("电源是否打开。关闭时完全静音")]
        public bool powered = true;

        [Header("确定性")]
        [Tooltip("噪声种子。同一班次固定种子，读档后听到的底噪保持一致")]
        public int noiseSeed = 20260813;

        private SignalSynthesizer _synth;
        private float[] _mono = Array.Empty<float>();

        /// <summary>
        /// 合成器。用懒初始化而不是在 Awake 里创建，因为 Awake 的执行顺序不确定：
        /// 界面的 Awake 可能先跑，先调 LoadStations 把电台装进来，
        /// 接着接收机的 Awake 再新建一个合成器，把刚装好的电台全部覆盖掉。
        /// 这个竞态不会报错，只会表现为"调到正确频率却收不到信号"。
        /// </summary>
        public SignalSynthesizer Synthesizer
        {
            get
            {
                if (_synth == null)
                {
                    _synth = new SignalSynthesizer(AudioSettings.outputSampleRate, noiseSeed);
                }

                return _synth;
            }
        }

        /// <summary>当前收到的信号强度，0 到 1。主线程可安全读取。</summary>
        public float SignalLevel => _synth?.CurrentSignalLevel ?? 0f;

        /// <summary>当前信号的失配量，正表示电台在调谐点上方。</summary>
        public float DetuneKHz => _synth?.CurrentDetuneKHz ?? 0f;

        public SignalSynthesizer.Station CurrentStation => _synth?.CurrentStation;

        private void Awake()
        {
            ApplySettings();

            var source = GetComponent<AudioSource>();
            source.playOnAwake = true;
            source.loop = true;
            source.spatialBlend = 0f;
            // OnAudioFilterRead 需要 AudioSource 处于播放状态才会被调用。
            // 给它一个静音的常量片段作为载体。
            if (source.clip == null)
            {
                source.clip = AudioClip.Create("RadioCarrier", AudioSettings.outputSampleRate,
                    1, AudioSettings.outputSampleRate, false);
            }

            source.Play();
        }

        private void Update()
        {
            ApplySettings();
        }

        private void ApplySettings()
        {
            var synth = Synthesizer;
            tunedKHz = Mathf.Clamp(tunedKHz, bandLowKHz, bandHighKHz);
            synth.TunedKHz = tunedKHz;
            synth.NoiseFloor = noiseFloor;
            synth.CracklePerSample = cracklePerSample;
            synth.MasterGain = powered ? masterGain : 0f;

            // 每帧在主线程重算一次接收状态。不能等音频线程去更新它：
            // 玩家可能静音，音频设备也可能起不来，而仪表必须照常动。
            synth.EvaluateReception();
            synth.AdvanceIfAudioStalled(Time.unscaledDeltaTime);
        }

        /// <summary>把频率移动指定的千赫数，并夹在频段范围内。</summary>
        public void Tune(float deltaKHz)
        {
            tunedKHz = Mathf.Clamp(tunedKHz + deltaKHz, bandLowKHz, bandHighKHz);
            Synthesizer.TunedKHz = tunedKHz;
        }

        /// <summary>频段内的归一化位置，0 到 1，用于驱动刻度盘游标。</summary>
        public float NormalizedDialPosition
        {
            get
            {
                var span = bandHighKHz - bandLowKHz;
                return span <= 0f ? 0f : Mathf.Clamp01((tunedKHz - bandLowKHz) / span);
            }
        }

        public void LoadStations(IEnumerable<SignalSynthesizer.Station> stations)
        {
            var synth = Synthesizer;
            synth.ClearStations();
            if (stations == null)
            {
                return;
            }

            foreach (var station in stations)
            {
                synth.AddStation(station);
            }

            synth.ResetTime();
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (_synth == null || channels <= 0)
            {
                return;
            }

            var frames = data.Length / channels;
            if (_mono.Length < frames)
            {
                // 只在缓冲区大小变化时分配一次。稳态下音频线程不做任何分配。
                _mono = new float[frames];
            }

            _synth.Render(_mono, frames);

            for (var f = 0; f < frames; f++)
            {
                var value = _mono[f];
                var baseIndex = f * channels;
                for (var c = 0; c < channels; c++)
                {
                    data[baseIndex + c] = value;
                }
            }
        }
    }
}
