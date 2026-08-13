using System;
using System.Collections.Generic;
using UnityEngine;

namespace Overclock
{
    /// <summary>
    /// 程序化音效。和贴图一样，不引入任何音频资产。
    ///
    /// 这个游戏的声音只需要几类：放置的咔哒、拆除的闷响、过热的滴滴、
    /// 烧毁的爆裂、通关的和弦，外加一层持续的电子嗡鸣。
    /// 它们都能用基础波形加包络合成出来，而且合成出来的电子音
    /// 比采样音更贴「你在一颗芯片内部」这个设定。
    /// </summary>
    public static class ProceduralAudio
    {
        public const int SampleRate = 44100;

        static readonly Dictionary<string, AudioClip> Cache = new Dictionary<string, AudioClip>();

        static AudioClip Cached(string key, Func<AudioClip> build)
        {
            if (Cache.TryGetValue(key, out var clip) && clip != null) return clip;
            clip = build();
            Cache[key] = clip;
            return clip;
        }

        public static void ClearCache()
        {
            foreach (var c in Cache.Values) if (c != null) UnityEngine.Object.DestroyImmediate(c);
            Cache.Clear();
        }

        static AudioClip FromSamples(string name, float[] samples)
        {
            var clip = AudioClip.Create(name, samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        /// <summary>指数衰减包络。绝大多数打击类音效都是这个形状。</summary>
        static float Decay(float t, float duration, float sharpness = 5f)
            => Mathf.Exp(-sharpness * t / duration);

        /// <summary>起音和收音都做几毫秒的斜坡，避免波形突变produce出爆音。</summary>
        static void ApplyEdgeFade(float[] samples, int fadeSamples = 128)
        {
            int n = samples.Length;
            fadeSamples = Mathf.Min(fadeSamples, n / 2);
            for (int i = 0; i < fadeSamples; i++)
            {
                float k = i / (float)fadeSamples;
                samples[i] *= k;
                samples[n - 1 - i] *= k;
            }
        }

        /// <summary>放置元件。短促的双频咔哒，音高随元件成本上升。</summary>
        public static AudioClip Place(int cost)
        {
            return Cached($"place:{cost}", () =>
            {
                const float duration = 0.075f;
                int n = (int)(SampleRate * duration);
                var s = new float[n];

                float baseFreq = 620f + cost * 55f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)SampleRate;
                    float env = Decay(t, duration, 22f);
                    s[i] = (Mathf.Sin(2f * Mathf.PI * baseFreq * t) * 0.55f +
                            Mathf.Sin(2f * Mathf.PI * baseFreq * 1.5f * t) * 0.25f) * env * 0.34f;
                }

                ApplyEdgeFade(s);
                return FromSamples($"Place{cost}", s);
            });
        }

        /// <summary>拆除。比放置低沉，带一点噪声，读起来像「拔掉」而不是「装上」。</summary>
        public static AudioClip Remove()
        {
            return Cached("remove", () =>
            {
                const float duration = 0.10f;
                int n = (int)(SampleRate * duration);
                var s = new float[n];
                var rng = new System.Random(4242);

                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)SampleRate;
                    float env = Decay(t, duration, 16f);
                    float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                    s[i] = (Mathf.Sin(2f * Mathf.PI * 210f * t) * 0.6f + noise * 0.25f) * env * 0.30f;
                }

                ApplyEdgeFade(s);
                return FromSamples("Remove", s);
            });
        }

        /// <summary>放不下时的拒绝音。短、闷、下行，一听就知道操作没生效。</summary>
        public static AudioClip Denied()
        {
            return Cached("denied", () =>
            {
                const float duration = 0.13f;
                int n = (int)(SampleRate * duration);
                var s = new float[n];

                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)SampleRate;
                    float freq = Mathf.Lerp(300f, 170f, t / duration);
                    float env = Decay(t, duration, 9f);
                    s[i] = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * freq * t)) * env * 0.16f;
                }

                ApplyEdgeFade(s);
                return FromSamples("Denied", s);
            });
        }

        /// <summary>过热警告。两声短促的高频滴滴，在嗡鸣底噪之上必须能穿透出来。</summary>
        public static AudioClip ThermalAlarm()
        {
            return Cached("alarm", () =>
            {
                const float duration = 0.34f;
                int n = (int)(SampleRate * duration);
                var s = new float[n];

                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)SampleRate;
                    // 0.00–0.10 秒和 0.16–0.26 秒各响一声。
                    bool onFirst = t < 0.10f;
                    bool onSecond = t >= 0.16f && t < 0.26f;
                    if (!onFirst && !onSecond) continue;

                    float local = onFirst ? t : t - 0.16f;
                    float env = Mathf.Sin(Mathf.PI * local / 0.10f);
                    s[i] = Mathf.Sin(2f * Mathf.PI * 1480f * t) * env * 0.22f;
                }

                ApplyEdgeFade(s);
                return FromSamples("ThermalAlarm", s);
            });
        }

        /// <summary>元件烧毁。噪声爆裂加一声下坠，这是玩家最不想听到的声音。</summary>
        public static AudioClip Burnout()
        {
            return Cached("burnout", () =>
            {
                const float duration = 0.55f;
                int n = (int)(SampleRate * duration);
                var s = new float[n];
                var rng = new System.Random(9001);

                float lowpass = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)SampleRate;
                    float env = Decay(t, duration, 6f);

                    // 白噪过一个单极点低通，得到接近「噼啪」的质感。
                    float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                    lowpass += (noise - lowpass) * 0.35f;

                    float sweep = Mathf.Lerp(420f, 90f, Mathf.Clamp01(t / duration));
                    s[i] = (lowpass * 0.65f + Mathf.Sin(2f * Mathf.PI * sweep * t) * 0.45f) * env * 0.40f;
                }

                ApplyEdgeFade(s);
                return FromSamples("Burnout", s);
            });
        }

        /// <summary>通关。一个上行大三和弦琶音，是整局里唯一明确的正反馈。</summary>
        public static AudioClip LayerClear()
        {
            return Cached("clear", () =>
            {
                const float duration = 0.95f;
                int n = (int)(SampleRate * duration);
                var s = new float[n];

                float[] notes = { 523.25f, 659.25f, 783.99f, 1046.50f };
                for (int k = 0; k < notes.Length; k++)
                {
                    float start = k * 0.11f;
                    for (int i = 0; i < n; i++)
                    {
                        float t = i / (float)SampleRate;
                        if (t < start) continue;

                        float local = t - start;
                        float env = Decay(local, duration - start, 4.2f);
                        s[i] += Mathf.Sin(2f * Mathf.PI * notes[k] * local) * env * 0.13f;
                    }
                }

                ApplyEdgeFade(s);
                return FromSamples("LayerClear", s);
            });
        }

        /// <summary>失败。下行小三和弦，和通关音刻意做成镜像。</summary>
        public static AudioClip RunFailed()
        {
            return Cached("failed", () =>
            {
                const float duration = 1.30f;
                int n = (int)(SampleRate * duration);
                var s = new float[n];

                float[] notes = { 466.16f, 349.23f, 277.18f };
                for (int k = 0; k < notes.Length; k++)
                {
                    float start = k * 0.20f;
                    for (int i = 0; i < n; i++)
                    {
                        float t = i / (float)SampleRate;
                        if (t < start) continue;

                        float local = t - start;
                        float env = Decay(local, duration - start, 2.6f);
                        s[i] += Mathf.Sin(2f * Mathf.PI * notes[k] * local) * env * 0.15f;
                    }
                }

                ApplyEdgeFade(s);
                return FromSamples("RunFailed", s);
            });
        }

        /// <summary>
        /// 可循环的电子底噪。两个略微失谐的正弦加一层极慢的调制，
        /// 制造出「有东西在运转」的持续感。
        /// 循环点必须严格对齐整数个周期，否则每次循环都会听到一下爆音。
        /// </summary>
        public static AudioClip AmbientHum()
        {
            return Cached("hum", () =>
            {
                const float duration = 4.0f;
                int n = (int)(SampleRate * duration);
                var s = new float[n];

                // 取能被时长整除的频率，保证首尾无缝。
                float f1 = Mathf.Round(58f * duration) / duration;
                float f2 = Mathf.Round(87f * duration) / duration;
                float fm = Mathf.Round(0.25f * duration) / duration;

                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)SampleRate;
                    float modulation = 0.82f + 0.18f * Mathf.Sin(2f * Mathf.PI * fm * t);
                    s[i] = (Mathf.Sin(2f * Mathf.PI * f1 * t) * 0.55f +
                            Mathf.Sin(2f * Mathf.PI * f2 * t) * 0.30f) * modulation * 0.055f;
                }

                return FromSamples("AmbientHum", s);
            });
        }
    }
}
