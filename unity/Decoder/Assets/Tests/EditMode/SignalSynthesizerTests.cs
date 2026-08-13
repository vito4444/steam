using System;
using Decoder.Signal;
using NUnit.Framework;

namespace Decoder.Tests
{
    /// <summary>
    /// 信号合成的测试。这一层决定"调收音机"这件事手感对不对，
    /// 而手感问题在自动化环境里没法靠听，只能靠对采样数据下断言。
    /// </summary>
    public sealed class SignalSynthesizerTests
    {
        private const int SampleRate = 48000;

        private static SignalSynthesizer Silent()
        {
            return new SignalSynthesizer(SampleRate, 12345)
            {
                NoiseFloor = 0f,
                CracklePerSample = 0f,
                MasterGain = 1f,
            };
        }

        private static float Rms(float[] buffer, int from, int count)
        {
            var sum = 0.0;
            for (var i = from; i < from + count; i++)
            {
                sum += buffer[i] * (double)buffer[i];
            }

            return (float)Math.Sqrt(sum / count);
        }

        private static float Peak(float[] buffer)
        {
            var peak = 0f;
            foreach (var s in buffer)
            {
                peak = Math.Max(peak, Math.Abs(s));
            }

            return peak;
        }

        /// <summary>用过零率估计主频，避免为了测试引入 FFT 依赖。</summary>
        private static float EstimateFrequency(float[] buffer, int sampleRate)
        {
            var crossings = 0;
            for (var i = 1; i < buffer.Length; i++)
            {
                if ((buffer[i - 1] < 0f && buffer[i] >= 0f) ||
                    (buffer[i - 1] >= 0f && buffer[i] < 0f))
                {
                    crossings++;
                }
            }

            return crossings * sampleRate / (2f * buffer.Length);
        }

        // ---- 带通响应 ----

        [Test]
        public void BandpassResponse_PeaksAtZeroDetune()
        {
            Assert.AreEqual(1f, SignalSynthesizer.BandpassResponse(0f), 1e-5f);
        }

        [Test]
        public void BandpassResponse_FallsToZeroAtBandEdge()
        {
            Assert.AreEqual(0f, SignalSynthesizer.BandpassResponse(SignalSynthesizer.BandwidthKHz), 1e-5f);
            Assert.AreEqual(0f, SignalSynthesizer.BandpassResponse(SignalSynthesizer.BandwidthKHz + 1f));
        }

        [Test]
        public void BandpassResponse_IsSymmetric()
        {
            Assert.AreEqual(SignalSynthesizer.BandpassResponse(0.8f),
                SignalSynthesizer.BandpassResponse(-0.8f), 1e-6f);
        }

        [Test]
        public void BandpassResponse_DecreasesMonotonicallyWithDetune()
        {
            // 单调性是玩家能靠"越调越响"找到中心的前提。
            var previous = float.MaxValue;
            for (var d = 0f; d <= SignalSynthesizer.BandwidthKHz; d += 0.1f)
            {
                var response = SignalSynthesizer.BandpassResponse(d);
                Assert.LessOrEqual(response, previous + 1e-6f,
                    $"失配 {d} kHz 处响应不再单调下降");
                previous = response;
            }
        }

        // ---- 差频音调 ----

        [Test]
        public void ToneForDetune_IsNominalWhenCentered()
        {
            Assert.AreEqual(SignalSynthesizer.NominalToneHz,
                SignalSynthesizer.ToneForDetune(0f), 1e-4f);
        }

        [Test]
        public void ToneForDetune_RisesAboveCenterAndFallsBelow()
        {
            // 音调方向是玩家判断"该往哪边转"的唯一线索，方向反了整个手感就废了。
            Assert.Greater(SignalSynthesizer.ToneForDetune(0.5f), SignalSynthesizer.NominalToneHz);
            Assert.Less(SignalSynthesizer.ToneForDetune(-0.5f), SignalSynthesizer.NominalToneHz);
        }

        [Test]
        public void ToneForDetune_NeverGoesBelowAudibleFloor()
        {
            // 差频降到 0 附近会变成直流，听感上信号会凭空消失。
            Assert.GreaterOrEqual(SignalSynthesizer.ToneForDetune(-5f), 40f);
        }

        // ---- 渲染 ----

        [Test]
        public void Render_OutputStaysWithinUnitRange()
        {
            var synth = new SignalSynthesizer(SampleRate, 7)
            {
                TunedKHz = 7000f,
                NoiseFloor = 0.9f,
                CracklePerSample = 0.01f,
                MasterGain = 1f,
            };
            synth.AddStation(new SignalSynthesizer.Station("TEST", 7000f, "EEEEEEEE", 20f, 1f));

            var buffer = new float[SampleRate];
            synth.Render(buffer, buffer.Length);

            Assert.LessOrEqual(Peak(buffer), 1f, "输出超出 [-1, 1]，会在硬件上削波失真");
        }

        [Test]
        public void Render_WithNoStationsProducesOnlyNoise()
        {
            var synth = new SignalSynthesizer(SampleRate, 7) { NoiseFloor = 0.2f, CracklePerSample = 0f };
            var buffer = new float[4096];
            synth.Render(buffer, buffer.Length);

            var rms = Rms(buffer, 0, buffer.Length);
            Assert.Greater(rms, 0.001f, "底噪不应为绝对静音，短波频段永远有噪声");
            Assert.Less(rms, 0.35f, "底噪过大会盖住信号");
        }

        [Test]
        public void Render_CarrierIsAudibleWhenTunedOnStation()
        {
            var synth = Silent();
            synth.TunedKHz = 7000f;
            // 长划连发，保证整段缓冲区都在键控打开状态。
            synth.AddStation(new SignalSynthesizer.Station("TEST", 7000f, "TTTTTTTT", 8f, 1f));

            var buffer = new float[SampleRate / 2];
            synth.Render(buffer, buffer.Length);

            Assert.Greater(Rms(buffer, 0, buffer.Length), 0.1f);
        }

        [Test]
        public void Render_StationOutsideBandwidthIsInaudible()
        {
            var synth = Silent();
            synth.TunedKHz = 7000f;
            synth.AddStation(new SignalSynthesizer.Station("FAR",
                7000f + SignalSynthesizer.BandwidthKHz + 1f, "TTTTTTTT", 8f, 1f));

            var buffer = new float[SampleRate / 4];
            synth.Render(buffer, buffer.Length);

            Assert.Less(Peak(buffer), 1e-6f, "带外信号不应泄漏到输出");
        }

        [Test]
        public void Render_AmplitudeDropsAsTuningDriftsAway()
        {
            float MeasureAt(float tuned)
            {
                var synth = Silent();
                synth.TunedKHz = tuned;
                synth.AddStation(new SignalSynthesizer.Station("TEST", 7000f, "TTTTTTTT", 8f, 1f));
                var buffer = new float[SampleRate / 4];
                synth.Render(buffer, buffer.Length);
                return Rms(buffer, buffer.Length / 2, buffer.Length / 2);
            }

            var centered = MeasureAt(7000f);
            var slightlyOff = MeasureAt(7000.6f);
            var wayOff = MeasureAt(7001.8f);

            Assert.Greater(centered, slightlyOff);
            Assert.Greater(slightlyOff, wayOff);
        }

        [Test]
        public void Render_ToneMatchesNominalPitchWhenCentered()
        {
            var synth = Silent();
            synth.TunedKHz = 7000f;
            synth.AddStation(new SignalSynthesizer.Station("TEST", 7000f, "TTTTTTTT", 6f, 1f));

            var buffer = new float[SampleRate / 2];
            synth.Render(buffer, buffer.Length);

            // 过零法有量化误差，且缓冲区里含键控间隙，放宽到 12% 容差。
            var estimated = EstimateFrequency(buffer, SampleRate);
            Assert.AreEqual(SignalSynthesizer.NominalToneHz, estimated,
                SignalSynthesizer.NominalToneHz * 0.12f,
                $"调准时的音高应接近标称 {SignalSynthesizer.NominalToneHz} Hz，实测 {estimated} Hz");
        }

        [Test]
        public void Render_ToneRisesWhenTunedBelowStation()
        {
            float MeasureTone(float tuned)
            {
                var synth = Silent();
                synth.TunedKHz = tuned;
                synth.AddStation(new SignalSynthesizer.Station("TEST", 7000f, "TTTTTTTT", 6f, 1f));
                var buffer = new float[SampleRate / 2];
                synth.Render(buffer, buffer.Length);
                return EstimateFrequency(buffer, SampleRate);
            }

            // 调低于电台频率时，差频变大，音调应当升高。
            Assert.Greater(MeasureTone(6999.3f), MeasureTone(7000f));
        }

        [Test]
        public void Render_KeyingHasSmoothEdgesWithoutClicks()
        {
            var synth = Silent();
            synth.TunedKHz = 7000f;
            synth.AddStation(new SignalSynthesizer.Station("TEST", 7000f, "E E E E", 20f, 1f));

            var buffer = new float[SampleRate];
            synth.Render(buffer, buffer.Length);

            // 硬开关会在键控边沿产生一个采样内的大跳变。软化后单采样最大跳变
            // 应当由载波本身的斜率主导，即 2*pi*f/fs 量级，约 0.092。
            var maxJump = 0f;
            for (var i = 1; i < buffer.Length; i++)
            {
                maxJump = Math.Max(maxJump, Math.Abs(buffer[i] - buffer[i - 1]));
            }

            Assert.Less(maxJump, 0.15f, $"存在键控爆音，单采样最大跳变 {maxJump}");
        }

        [Test]
        public void Render_IsDeterministicForSameSeed()
        {
            float[] Once()
            {
                var synth = new SignalSynthesizer(SampleRate, 4242)
                {
                    TunedKHz = 7000.4f,
                    NoiseFloor = 0.3f,
                    CracklePerSample = 0.001f,
                };
                synth.AddStation(new SignalSynthesizer.Station("A", 7000f, "CQ CQ", 18f, 0.9f));
                var buffer = new float[8192];
                synth.Render(buffer, buffer.Length);
                return buffer;
            }

            var first = Once();
            var second = Once();
            CollectionAssert.AreEqual(first, second,
                "同种子必须产生同波形，否则读档回到同一班次听到的东西会变");
        }

        [Test]
        public void Render_DifferentSeedsProduceDifferentNoise()
        {
            float[] WithSeed(int seed)
            {
                var synth = new SignalSynthesizer(SampleRate, seed)
                {
                    NoiseFloor = 0.3f,
                    CracklePerSample = 0f,
                };
                var buffer = new float[2048];
                synth.Render(buffer, buffer.Length);
                return buffer;
            }

            // 种子若被忽略，每个班次的底噪都会一模一样，
            // 玩家能靠背噪声的形状而不是靠听信号来通关。
            CollectionAssert.AreNotEqual(WithSeed(1), WithSeed(2));
        }

        [Test]
        public void Render_RejectsCountLargerThanBuffer()
        {
            var synth = Silent();
            var buffer = new float[16];
            Assert.Throws<ArgumentOutOfRangeException>(() => synth.Render(buffer, 32));
        }

        // ---- 电台时序 ----

        [Test]
        public void Station_KeyIsDownAtStartOfTransmission()
        {
            var station = new SignalSynthesizer.Station("T", 7000f, "T", 20f);
            Assert.IsTrue(station.IsKeyDown(0.001d));
        }

        [Test]
        public void Station_KeyIsUpBeforeStartOffset()
        {
            var station = new SignalSynthesizer.Station("T", 7000f, "T", 20f)
            {
                StartOffsetSeconds = 5d,
            };
            Assert.IsFalse(station.IsKeyDown(1d));
            Assert.IsTrue(station.IsKeyDown(5.001d));
        }

        [Test]
        public void Station_LoopsAfterGap()
        {
            // 一个 20 WPM 的划是 0.18 秒，加 2 秒间隔后应当重新开始。
            var station = new SignalSynthesizer.Station("T", 7000f, "T", 20f,
                loop: true, loopGapSeconds: 2f);

            Assert.IsTrue(station.IsKeyDown(0.05d));
            Assert.IsFalse(station.IsKeyDown(1.0d), "循环间隙内不应有载波");
            Assert.IsTrue(station.IsKeyDown(2.18d + 0.05d), "间隙结束后应当重新发报");
        }

        [Test]
        public void Station_NonLoopingStopsAfterMessage()
        {
            var station = new SignalSynthesizer.Station("T", 7000f, "T", 20f, loop: false);
            Assert.IsTrue(station.IsKeyDown(0.05d));
            Assert.IsFalse(station.IsKeyDown(10d));
        }

        [Test]
        public void Station_EmptyMessageNeverKeys()
        {
            var station = new SignalSynthesizer.Station("T", 7000f, "", 20f);
            Assert.IsFalse(station.IsKeyDown(0d));
            Assert.IsFalse(station.IsKeyDown(100d));
        }

        [Test]
        public void Station_TotalSecondsMatchesTimeline()
        {
            var station = new SignalSynthesizer.Station("T", 7000f, "PARIS", 20f);
            var expected = MorseCode.TotalSeconds(
                MorseCode.BuildTimeline(MorseCode.Encode("PARIS"), 20f));
            Assert.AreEqual(expected, station.TotalSeconds, 1e-4f);
        }
    }
}
