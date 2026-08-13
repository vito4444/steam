using System;
using System.Collections.Generic;
using Maner.Controls;
using UnityEngine;

namespace Maner.Cabin
{
    /// <summary>
    /// 控件音效的运行时合成。本项目不使用任何录制音频素材，
    /// 每一声咔哒、每一段阀轮吱呀都由代码算出采样点。
    ///
    /// 这样做的直接好处是音效可以跟着状态连续变化——卷扬机的低频轰鸣
    /// 会随转速改变基频，而不是播放一段固定的循环。对于一款卖点是
    /// 「机器的手感」的游戏，这一点比音质本身更要紧。
    ///
    /// 每个控件有三段音：起始、持续、结束。起始与结束是瞬态，
    /// 持续段是可循环的，只有阀轮与手柄这类需要过程的控件会用到。
    /// </summary>
    public static class ProceduralAudio
    {
        public const int SampleRate = 44100;

        public enum Segment
        {
            Begin,
            Sustain,
            End,
        }

        static readonly Dictionary<(ControlId, Segment), AudioClip> Cache =
            new Dictionary<(ControlId, Segment), AudioClip>();

        public static void ClearCache() => Cache.Clear();

        public static AudioClip Get(in ControlDefinition def, Segment segment)
        {
            var key = (def.Id, segment);
            if (Cache.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            var clip = Synthesize(def, segment);
            Cache[key] = clip;
            return clip;
        }

        static AudioClip Synthesize(in ControlDefinition def, Segment segment)
        {
            float duration = DurationFor(def.Kind, segment);
            int sampleCount = Mathf.Max(64, Mathf.RoundToInt(duration * SampleRate));
            var data = new float[sampleCount];

            // 每个控件用自己的基频做种子，保证同一个控件每次听起来一致，
            // 不同控件之间又有可分辨的音色差异。
            var rng = new System.Random(Mathf.RoundToInt(def.AudioBaseHz * 7.3f) + (int)segment * 101);

            switch (def.Kind)
            {
                case ControlKind.KnifeSwitch:
                    if (segment == Segment.End)
                    {
                        // 合闸到底：一记沉重的金属撞击加一小段电弧噼啪。
                        WriteImpact(data, def.AudioBaseHz * 0.7f, 0.055f, 0.85f, rng);
                        AddCrackle(data, 0.02f, 0.35f, rng, 0.28f);
                    }
                    else
                    {
                        WriteImpact(data, def.AudioBaseHz, 0.03f, 0.45f, rng);
                    }
                    break;

                case ControlKind.Breaker:
                    WriteImpact(data, def.AudioBaseHz * 1.4f, 0.022f, segment == Segment.End ? 0.7f : 0.4f, rng);
                    break;

                case ControlKind.ToggleLever:
                    WriteClick(data, def.AudioBaseHz * 2.2f, 0.012f, 0.5f, rng);
                    break;

                case ControlKind.PushButton:
                    WriteClick(data, def.AudioBaseHz, 0.008f, segment == Segment.Begin ? 0.6f : 0.35f, rng);
                    break;

                case ControlKind.RotaryKnob:
                    // 旋钮的段落感：一串等间距的细小咔哒。
                    WriteDetents(data, def.AudioBaseHz, 9, 0.006f, 0.3f, rng);
                    break;

                case ControlKind.ValveWheel:
                    if (segment == Segment.Sustain)
                    {
                        WriteMetalGroan(data, def.AudioBaseHz * 0.5f, 0.35f, rng);
                    }
                    else
                    {
                        WriteImpact(data, def.AudioBaseHz * 0.8f, 0.04f, 0.5f, rng);
                    }
                    break;

                case ControlKind.ThrottleHandle:
                    if (segment == Segment.Sustain)
                    {
                        WriteMetalGroan(data, def.AudioBaseHz * 0.65f, 0.25f, rng);
                    }
                    else
                    {
                        WriteDetents(data, def.AudioBaseHz * 1.5f, 3, 0.01f, 0.4f, rng);
                    }
                    break;

                default:
                    WriteClick(data, def.AudioBaseHz, 0.01f, 0.3f, rng);
                    break;
            }

            var clip = AudioClip.Create($"SFX_{def.Id}_{segment}", sampleCount, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float DurationFor(ControlKind kind, Segment segment) => (kind, segment) switch
        {
            (ControlKind.KnifeSwitch, Segment.End) => 0.42f,
            (ControlKind.KnifeSwitch, _) => 0.18f,
            (ControlKind.ValveWheel, Segment.Sustain) => 0.60f,
            (ControlKind.ThrottleHandle, Segment.Sustain) => 0.50f,
            (ControlKind.RotaryKnob, _) => 0.22f,
            (ControlKind.Breaker, _) => 0.20f,
            (ControlKind.PushButton, _) => 0.10f,
            _ => 0.16f,
        };

        /// <summary>金属撞击：几个非谐波分音加指数衰减，末尾补一点噪声尾巴。</summary>
        static void WriteImpact(float[] data, float baseHz, float decay, float gain, System.Random rng)
        {
            float[] ratios = { 1f, 2.41f, 3.83f, 5.19f };
            float[] weights = { 1f, 0.55f, 0.32f, 0.18f };
            float phaseJitter = (float)rng.NextDouble() * Mathf.PI;

            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)SampleRate;
                float env = Mathf.Exp(-t / decay);
                float v = 0f;
                for (int h = 0; h < ratios.Length; h++)
                {
                    v += weights[h] * Mathf.Sin(2f * Mathf.PI * baseHz * ratios[h] * t + phaseJitter * h);
                }
                v += ((float)rng.NextDouble() * 2f - 1f) * 0.25f * Mathf.Exp(-t / (decay * 0.35f));
                data[i] += v * env * gain * 0.32f;
            }
        }

        /// <summary>短促的塑料/胶木咔哒，比金属撞击干净得多。</summary>
        static void WriteClick(float[] data, float baseHz, float decay, float gain, System.Random rng)
        {
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)SampleRate;
                float env = Mathf.Exp(-t / decay);
                float noise = (float)rng.NextDouble() * 2f - 1f;
                float tone = Mathf.Sin(2f * Mathf.PI * baseHz * t);
                data[i] += (tone * 0.6f + noise * 0.4f) * env * gain * 0.5f;
            }
        }

        /// <summary>一串等间距的段落咔哒，用于旋钮与手柄的档位感。</summary>
        static void WriteDetents(float[] data, float baseHz, int count, float decay, float gain, System.Random rng)
        {
            int stride = Mathf.Max(1, data.Length / Mathf.Max(1, count));
            for (int d = 0; d < count; d++)
            {
                int start = d * stride;
                float pitch = baseHz * (0.94f + (float)rng.NextDouble() * 0.12f);
                for (int i = start; i < Mathf.Min(data.Length, start + stride); i++)
                {
                    float t = (i - start) / (float)SampleRate;
                    float env = Mathf.Exp(-t / decay);
                    float noise = (float)rng.NextDouble() * 2f - 1f;
                    data[i] += (Mathf.Sin(2f * Mathf.PI * pitch * t) * 0.5f + noise * 0.5f) * env * gain * 0.4f;
                }
            }
        }

        /// <summary>金属摩擦的持续吱呀声：窄带噪声加缓慢的音高漂移。</summary>
        static void WriteMetalGroan(float[] data, float baseHz, float gain, System.Random rng)
        {
            float phase = 0f;
            float lowpassState = 0f;

            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)SampleRate;
                // 音高缓慢起伏，模拟手转动阀轮时的不匀速。
                float drift = 1f + 0.14f * Mathf.Sin(2f * Mathf.PI * 2.3f * t) + 0.06f * Mathf.Sin(2f * Mathf.PI * 7.1f * t);
                phase += 2f * Mathf.PI * baseHz * drift / SampleRate;

                float tone = Mathf.Sin(phase) * 0.5f + Mathf.Sin(phase * 2.02f) * 0.28f + Mathf.Sin(phase * 3.05f) * 0.14f;
                float noise = (float)rng.NextDouble() * 2f - 1f;
                lowpassState += (noise - lowpassState) * 0.16f;

                float attack = Mathf.Clamp01(t / 0.03f);
                float release = Mathf.Clamp01((data.Length / (float)SampleRate - t) / 0.05f);
                data[i] += (tone * 0.7f + lowpassState * 0.6f) * gain * attack * release * 0.5f;
            }
        }

        static void AddCrackle(float[] data, float startSeconds, float duration, System.Random rng, float gain)
        {
            int start = Mathf.RoundToInt(startSeconds * SampleRate);
            int end = Mathf.Min(data.Length, start + Mathf.RoundToInt(duration * SampleRate));
            for (int i = start; i < end; i++)
            {
                if (rng.NextDouble() > 0.06)
                {
                    continue;
                }
                int burst = Mathf.Min(end - i, rng.Next(12, 60));
                float amp = (float)rng.NextDouble() * gain;
                for (int j = 0; j < burst; j++)
                {
                    float env = 1f - j / (float)burst;
                    data[i + j] += ((float)rng.NextDouble() * 2f - 1f) * amp * env;
                }
                i += burst;
            }
        }

        /// <summary>
        /// 机械环境声。频率随卷扬机速度与风机转速连续变化，
        /// 这是程序化合成相对录制素材的核心优势：声音永远与机器状态同步。
        /// </summary>
        public static AudioClip CreateAmbientLoop(float seconds = 4f)
        {
            int sampleCount = Mathf.RoundToInt(seconds * SampleRate);
            var data = new float[sampleCount];
            var rng = new System.Random(20260813);

            float rumblePhase = 0f;
            float airState = 0f;

            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)SampleRate;
                // 低频轰鸣：卷扬机房传过来的结构声。
                rumblePhase += 2f * Mathf.PI * 41f / SampleRate;
                float rumble = Mathf.Sin(rumblePhase) * 0.5f + Mathf.Sin(rumblePhase * 1.51f) * 0.22f;

                // 通风白噪，做一次单极点低通，去掉刺耳的高频。
                float noise = (float)rng.NextDouble() * 2f - 1f;
                airState += (noise - airState) * 0.05f;

                // 水泵的周期性搏动。
                float pump = Mathf.Max(0f, Mathf.Sin(2f * Mathf.PI * 1.35f * t)) * 0.18f;

                data[i] = rumble * 0.16f + airState * 0.5f + pump * airState;
            }

            // 首尾交叉淡化，保证循环无缝。
            int fade = Mathf.Min(2048, sampleCount / 8);
            for (int i = 0; i < fade; i++)
            {
                float k = i / (float)fade;
                data[i] = Mathf.Lerp(data[sampleCount - fade + i], data[i], k);
            }

            var clip = AudioClip.Create("SFX_CabinAmbient", sampleCount, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
