using System.Collections.Generic;
using Maner.Cabin;
using Maner.Controls;
using NUnit.Framework;
using UnityEngine;

namespace Maner.Tests
{
    /// <summary>
    /// 程序化音效测试。这一层只能验证「合成出来的采样是否符合规格」，
    /// 听感好不好听没有任何自动化手段可以判断，必须由人工试玩确认。
    /// 因此这里守住的是可客观检验的部分：有声音、不爆音、不同控件可分辨。
    /// </summary>
    public class AudioTests
    {
        static float[] Samples(AudioClip clip)
        {
            var data = new float[clip.samples * clip.channels];
            clip.GetData(data, 0);
            return data;
        }

        static float Rms(float[] data)
        {
            double sum = 0.0;
            foreach (var v in data)
            {
                sum += v * v;
            }
            return Mathf.Sqrt((float)(sum / Mathf.Max(1, data.Length)));
        }

        static float Peak(float[] data)
        {
            float peak = 0f;
            foreach (var v in data)
            {
                peak = Mathf.Max(peak, Mathf.Abs(v));
            }
            return peak;
        }

        [SetUp]
        public void ClearCache() => ProceduralAudio.ClearCache();

        [Test]
        public void 每个可交互控件都能合成三段音效()
        {
            int verified = 0;
            foreach (var def in ConsoleLayout.All)
            {
                if (!def.IsInteractive)
                {
                    continue;
                }

                foreach (ProceduralAudio.Segment seg in System.Enum.GetValues(typeof(ProceduralAudio.Segment)))
                {
                    var clip = ProceduralAudio.Get(def, seg);
                    Assert.IsNotNull(clip, $"{def.Id} 的 {seg} 段没有生成");
                    Assert.Greater(clip.samples, 0, $"{def.Id} 的 {seg} 段长度为零");
                    Assert.AreEqual(ProceduralAudio.SampleRate, clip.frequency);
                }
                verified++;
            }

            Assert.GreaterOrEqual(verified, 24, "验收标准要求不少于 24 个可交互控件各有三段音效");
        }

        [Test]
        public void 合成的音效有实际能量且不爆音()
        {
            foreach (var def in ConsoleLayout.All)
            {
                if (!def.IsInteractive)
                {
                    continue;
                }

                foreach (ProceduralAudio.Segment seg in System.Enum.GetValues(typeof(ProceduralAudio.Segment)))
                {
                    var data = Samples(ProceduralAudio.Get(def, seg));
                    float rms = Rms(data);
                    float peak = Peak(data);

                    Assert.Greater(rms, 0.001f, $"{def.Id}/{seg} 几乎是静音，RMS={rms:0.#####}");
                    Assert.LessOrEqual(peak, 1.0f, $"{def.Id}/{seg} 峰值 {peak:0.###} 超出量程会爆音");
                }
            }
        }

        [Test]
        public void 不同控件的音效可以互相区分()
        {
            var fingerprints = new Dictionary<string, ControlId>();

            foreach (var def in ConsoleLayout.All)
            {
                if (!def.IsInteractive)
                {
                    continue;
                }

                var data = Samples(ProceduralAudio.Get(def, ProceduralAudio.Segment.Begin));
                // 用长度与前若干个采样的量化值做指纹，足以区分不同音色。
                var sb = new System.Text.StringBuilder();
                sb.Append(data.Length).Append(':');
                for (int i = 0; i < 24 && i < data.Length; i++)
                {
                    sb.Append(Mathf.RoundToInt(data[i] * 400f)).Append(',');
                }
                string key = sb.ToString();

                Assert.IsFalse(fingerprints.ContainsKey(key),
                    $"{def.Id} 与 {(fingerprints.TryGetValue(key, out var other) ? other.ToString() : "?")} 的音效完全相同，玩家无法靠声音区分");
                fingerprints[key] = def.Id;
            }
        }

        [Test]
        public void 同一控件重复取用返回同一份缓存()
        {
            var def = ConsoleLayout.Get(ControlId.FuelValve);
            var a = ProceduralAudio.Get(def, ProceduralAudio.Segment.Sustain);
            var b = ProceduralAudio.Get(def, ProceduralAudio.Segment.Sustain);
            Assert.AreSame(a, b, "音效应当被缓存，避免每次操作都重新合成");
        }

        [Test]
        public void 阀轮的持续段明显长于按钮的瞬态()
        {
            var valve = ProceduralAudio.Get(ConsoleLayout.Get(ControlId.FuelValve), ProceduralAudio.Segment.Sustain);
            var button = ProceduralAudio.Get(ConsoleLayout.Get(ControlId.SignalBell), ProceduralAudio.Segment.Begin);

            Assert.Greater(valve.length, button.length * 3f,
                "阀轮要转好几圈，它的持续段必须明显长于按钮的一下咔哒");
        }

        [Test]
        public void 环境声循环首尾平滑不会有爆点()
        {
            var clip = ProceduralAudio.CreateAmbientLoop(2f);
            var data = Samples(clip);

            Assert.Greater(data.Length, 0);
            Assert.LessOrEqual(Peak(data), 1.0f);
            Assert.Greater(Rms(data), 0.01f, "环境声不应是静音");

            // 循环点：末尾最后一个采样与开头第一个采样的落差不能太大，否则会听到咔哒。
            float seam = Mathf.Abs(data[data.Length - 1] - data[0]);
            Assert.Less(seam, 0.25f, $"循环接缝落差 {seam:0.###} 过大，会听到爆点");
        }

        [Test]
        public void 合成结果稳定可复现()
        {
            var def = ConsoleLayout.Get(ControlId.GeneratorMaster);
            var first = Samples(ProceduralAudio.Get(def, ProceduralAudio.Segment.End));

            ProceduralAudio.ClearCache();
            var second = Samples(ProceduralAudio.Get(def, ProceduralAudio.Segment.End));

            Assert.AreEqual(first.Length, second.Length);
            for (int i = 0; i < first.Length; i += 97)
            {
                Assert.AreEqual(first[i], second[i], 1e-6f, $"第 {i} 个采样不一致，合成不可复现");
            }
        }
    }
}
