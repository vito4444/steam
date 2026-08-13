using Decoder.Signal;
using NUnit.Framework;

namespace Decoder.Tests
{
    /// <summary>
    /// 发报时钟与解调包络的测试。
    ///
    /// 这一层守的是一条无障碍承诺：示波器上的波形必须和耳朵听到的严格同源。
    /// 听障玩家完全靠看包络的宽窄来分辨点和划，如果波形是另算的一套，
    /// 或者在静音时干脆停住，这条路就断了。
    /// </summary>
    public sealed class SignalClockTests
    {
        private static SignalSynthesizer Silent()
        {
            return new SignalSynthesizer(48000, 99)
            {
                NoiseFloor = 0f,
                CracklePerSample = 0f,
            };
        }

        [Test]
        public void Clock_StartsAtZero()
        {
            Assert.AreEqual(0d, Silent().ElapsedSeconds, 1e-9);
        }

        [Test]
        public void Clock_AdvancesOnMainThreadWhenAudioNeverRenders()
        {
            // 云端没有声卡，玩家也可能把音量关到零。
            // 这两种情况下音频线程都不会推进时钟，主线程必须接手。
            var synth = Silent();

            synth.AdvanceIfAudioStalled(0.5d);
            synth.AdvanceIfAudioStalled(0.5d);

            Assert.AreEqual(1.0d, synth.ElapsedSeconds, 1e-6);
        }

        [Test]
        public void Clock_DoesNotDoubleCountWhenAudioIsRunning()
        {
            // 音频线程推进过之后，主线程这一帧不该再加一次，
            // 否则时钟会跑得比声音快，波形和声音就对不上了。
            var synth = Silent();
            synth.AddStation(new SignalSynthesizer.Station("T", 7000f, "TTTT", 10f));
            synth.TunedKHz = 7000f;

            var buffer = new float[24000];
            synth.Render(buffer, buffer.Length);
            var afterRender = synth.ElapsedSeconds;

            Assert.AreEqual(0.5d, afterRender, 1e-3, "渲染半秒采样应当推进半秒时钟");

            synth.AdvanceIfAudioStalled(10d);
            Assert.AreEqual(afterRender, synth.ElapsedSeconds, 1e-9,
                "音频线程已经推进过，主线程不应重复累加");
        }

        [Test]
        public void Clock_ResumesMainThreadTakeoverAfterAudioStops()
        {
            var synth = Silent();
            var buffer = new float[4800];
            synth.Render(buffer, buffer.Length);

            synth.AdvanceIfAudioStalled(1d);
            var afterFirstFrame = synth.ElapsedSeconds;

            synth.AdvanceIfAudioStalled(1d);
            Assert.AreEqual(afterFirstFrame + 1d, synth.ElapsedSeconds, 1e-6,
                "音频停摆后的第二帧起，主线程应当持续接管");
        }

        [Test]
        public void Clock_IgnoresNegativeDelta()
        {
            var synth = Silent();
            synth.AdvanceIfAudioStalled(1d);
            synth.AdvanceIfAudioStalled(-5d);

            Assert.AreEqual(1d, synth.ElapsedSeconds, 1e-6, "时钟不能倒流");
        }

        [Test]
        public void Clock_ResetsWithTime()
        {
            var synth = Silent();
            synth.AdvanceIfAudioStalled(3d);
            synth.ResetTime();

            Assert.AreEqual(0d, synth.ElapsedSeconds, 1e-9);

            // 复位之后主线程必须还能继续推进，不能因为内部的观察值没清而卡死。
            synth.AdvanceIfAudioStalled(0.25d);
            Assert.AreEqual(0.25d, synth.ElapsedSeconds, 1e-6);
        }

        // ---- 包络与键控同源 ----

        [Test]
        public void Envelope_IsZeroWhenNothingIsTuned()
        {
            var synth = Silent();
            synth.AddStation(new SignalSynthesizer.Station("T", 7000f, "TTTT", 10f));
            synth.TunedKHz = 7100f;
            synth.EvaluateReception();

            Assert.AreEqual(0f, synth.EnvelopeAt(0.05d));
        }

        [Test]
        public void Envelope_FollowsKeyingExactly()
        {
            // 逐个时间点比对包络与键控状态。这是"看到的等于听到的"的字面验证。
            var synth = Silent();
            var station = new SignalSynthesizer.Station("T", 7000f, "PARIS", 12f);
            synth.AddStation(station);
            synth.TunedKHz = 7000f;
            synth.EvaluateReception();

            for (var t = 0d; t < station.TotalSeconds; t += 0.005d)
            {
                var keyDown = station.IsKeyDown(t);
                var envelope = synth.EnvelopeAt(t);
                Assert.AreEqual(keyDown, envelope > 0f,
                    $"t={t:F3}s 处包络与键控不一致：键控 {keyDown}，包络 {envelope}");
            }
        }

        [Test]
        public void Envelope_ScalesWithReceivedSignalLevel()
        {
            // 波形高度要反映信号强弱，这样玩家看一眼就知道调准没有。
            var synth = Silent();
            synth.AddStation(new SignalSynthesizer.Station("T", 7000f, "TTTT", 8f, 0.8f));

            synth.TunedKHz = 7000f;
            synth.EvaluateReception();
            var centered = synth.EnvelopeAt(0.02d);

            synth.TunedKHz = 7001.4f;
            synth.EvaluateReception();
            var offTune = synth.EnvelopeAt(0.02d);

            Assert.Greater(centered, offTune);
            Assert.AreEqual(0.8f, centered, 1e-4f);
        }

        [Test]
        public void Envelope_DistinguishesDitFromDah()
        {
            // 点和划的区别就是包络持续的长短。这是听障玩家读电码的全部依据，
            // 如果两者在包络上没有可测的差异，这条无障碍路径就是空的。
            var synth = Silent();
            var dit = new SignalSynthesizer.Station("E", 7000f, "E", 12f);
            var dah = new SignalSynthesizer.Station("T", 7000f, "T", 12f);

            var unit = MorseCode.UnitSeconds(12f);

            synth.ClearStations();
            synth.AddStation(dit);
            synth.TunedKHz = 7000f;
            synth.EvaluateReception();
            Assert.Greater(synth.EnvelopeAt(unit * 0.5d), 0f, "点的中点应当有包络");
            Assert.AreEqual(0f, synth.EnvelopeAt(unit * 2.0d), "点在 1 单位后应当结束");

            synth.ClearStations();
            synth.AddStation(dah);
            synth.EvaluateReception();
            Assert.Greater(synth.EnvelopeAt(unit * 2.0d), 0f, "划在 2 单位处仍应有包络");
            Assert.AreEqual(0f, synth.EnvelopeAt(unit * 4.0d), "划在 3 单位后应当结束");
        }
    }
}
