using Decoder.Signal;
using NUnit.Framework;

namespace Decoder.Tests
{
    /// <summary>
    /// 接收解码器的测试。
    ///
    /// 这一层对听障玩家是唯一的读码途径，所以标准不是"大致能认出来"，
    /// 而是把发报端的时序原样喂进去必须逐字还原。下面的用例都用
    /// MorseCode.BuildTimeline 生成时序，也就是说发报端和接收端
    /// 走的是同一份真值，任何一侧改动导致两边对不上都会被抓到。
    /// </summary>
    public sealed class MorseReceiverTests
    {
        /// <summary>把一段电文按真实时序喂给解码器，模拟玩家坐在那里听完整段。</summary>
        private static MorseReceiver PlayThrough(string text, float wpm, float stepSeconds = 0.004f)
        {
            var receiver = new MorseReceiver(wpm);
            var timeline = MorseCode.BuildTimeline(MorseCode.Encode(text), wpm);
            var total = MorseCode.TotalSeconds(timeline);

            // 末尾多喂一个词间隔的静默，让最后一个字符收尾。
            // 真实场景里这段静默一定存在，电台发完总要停。
            var tail = MorseCode.UnitSeconds(wpm) * 8f;

            var time = 0d;
            var index = 0;
            var segmentEnd = timeline.Count > 0 ? timeline[0].Seconds : 0d;

            while (time <= total + tail)
            {
                while (index < timeline.Count && time >= segmentEnd)
                {
                    index++;
                    if (index < timeline.Count)
                    {
                        segmentEnd += timeline[index].Seconds;
                    }
                }

                var keyDown = index < timeline.Count && timeline[index].KeyDown;
                receiver.Advance(time, keyDown);
                time += stepSeconds;
            }

            return receiver;
        }

        [TestCase("SOS", 12f)]
        [TestCase("CQ", 12f)]
        [TestCase("PARIS", 15f)]
        [TestCase("R7X", 14f)]
        [TestCase("0554", 9f)]
        [TestCase("05540079", 9f)]
        [TestCase("THE QUICK BROWN FOX", 18f)]
        public void Advance_RecoversTransmittedText(string text, float wpm)
        {
            Assert.AreEqual(text, PlayThrough(text, wpm).Transcript);
        }

        [Test]
        public void Advance_RecoversTextAtVeryLowSpeed()
        {
            // 第一班用 9 WPM。慢速下单位时长长，浮点误差占比更小，
            // 但帧步长相对单位时长的比例也变了，阈值判定必须仍然稳。
            Assert.AreEqual("0554", PlayThrough("0554", 6f).Transcript);
        }

        [Test]
        public void Advance_RecoversTextAtHighSpeed()
        {
            // 25 WPM 时一个点只有 48 毫秒，按 4 毫秒步长采样只有 12 个采样点。
            Assert.AreEqual("CQ DE R7X", PlayThrough("CQ DE R7X", 25f).Transcript);
        }

        [Test]
        public void Advance_IsRobustToCoarseSampling()
        {
            // 低帧率下每帧可能跨过好几个单位。判定依据是电平变化之间的时长而不是
            // 采样次数，所以粗采样下仍应正确——但采样太粗会错过短脉冲，
            // 这里用 15 毫秒，相当于 66 帧每秒，覆盖实际游戏的帧率下限。
            Assert.AreEqual("SOS", PlayThrough("SOS", 12f, 0.015f).Transcript);
        }

        [Test]
        public void Advance_SeparatesWordsWithSingleSpace()
        {
            var transcript = PlayThrough("DE R7X", 14f).Transcript;
            Assert.AreEqual("DE R7X", transcript);
            Assert.IsFalse(transcript.Contains("  "), "词间隔不应产生连续空格");
        }

        [Test]
        public void Advance_DoesNotEmitTrailingSpace()
        {
            // 电文末尾的长静默不该在转写里留下尾随空格，
            // 否则玩家复制出来的内容会和标准答案差一个字符。
            var transcript = PlayThrough("SOS", 12f).Transcript;
            Assert.AreEqual(transcript.TrimEnd(), transcript);
        }

        [Test]
        public void PendingSymbols_ShowsPartialCharacterWhileReceiving()
        {
            // 字符收到一半时，玩家应当能看到已经落地的那几个点划。
            // 这是"跟着听"的核心体验，等整个字符结束才显示就晚了。
            var wpm = 10f;
            var unit = MorseCode.UnitSeconds(wpm);
            var receiver = new MorseReceiver(wpm);

            // 手工喂一个划加一个间隔：这是字母 N 的前半段
            receiver.Advance(0d, false);
            receiver.Advance(0.001d, true);
            receiver.Advance(unit * 3d, true);
            receiver.Advance(unit * 3d + 0.001d, false);

            Assert.AreEqual("-", receiver.PendingSymbols);
            Assert.AreEqual(string.Empty, receiver.Transcript);
        }

        [Test]
        public void PendingSymbols_ClearsOnceCharacterCompletes()
        {
            var receiver = PlayThrough("E", 12f);
            Assert.AreEqual(string.Empty, receiver.PendingSymbols);
            Assert.AreEqual("E", receiver.Transcript);
        }

        [Test]
        public void Advance_DistinguishesDitFromDah()
        {
            // 这是整个解码器的分界线。混淆点和划会让每个字符都错。
            Assert.AreEqual("E", PlayThrough("E", 12f).Transcript);
            Assert.AreEqual("T", PlayThrough("T", 12f).Transcript);
        }

        [Test]
        public void Reset_ClearsEverything()
        {
            var receiver = PlayThrough("SOS", 12f);
            Assert.IsNotEmpty(receiver.Transcript);

            receiver.Reset();
            Assert.AreEqual(string.Empty, receiver.Transcript);
            Assert.AreEqual(string.Empty, receiver.PendingSymbols);
        }

        [Test]
        public void Advance_RestartsWhenTimeGoesBackwards()
        {
            // 换班或读档会把时钟拨回去。沿用旧状态会算出一个超长的键控段，
            // 转写里凭空多出一个字符。
            var receiver = PlayThrough("SOS", 12f);
            receiver.Advance(0d, false);

            Assert.AreEqual(string.Empty, receiver.Transcript);
        }

        [Test]
        public void SetSpeed_ChangesUnitDuration()
        {
            var receiver = new MorseReceiver(20f);
            Assert.AreEqual(0.06f, receiver.UnitSeconds, 1e-5f);

            receiver.SetSpeed(10f);
            Assert.AreEqual(0.12f, receiver.UnitSeconds, 1e-5f);
        }

        [Test]
        public void Format_RespectsAssistLevel()
        {
            var receiver = PlayThrough("SOS", 12f);

            Assert.AreEqual(string.Empty, receiver.Format(CopyAssist.None),
                "关闭辅助时不该泄露任何内容");
            Assert.AreEqual("SOS", receiver.Format(CopyAssist.Characters));
            Assert.AreEqual("... --- ...", receiver.Format(CopyAssist.Symbols));
        }

        [Test]
        public void Format_SymbolsShowWordSeparator()
        {
            var receiver = PlayThrough("E T", 12f);
            Assert.AreEqual(". / -", receiver.Format(CopyAssist.Symbols));
        }

        [Test]
        public void Advance_MatchesWhatTheSynthesizerActuallyKeys()
        {
            // 端到端：直接从合成器的电台对象取键控状态，
            // 确认接收端与发声端读的是同一份真值。
            const float wpm = 11f;
            var station = new SignalSynthesizer.Station("M08", 6955f, "05540079", wpm);
            var receiver = new MorseReceiver(wpm);

            var tail = MorseCode.UnitSeconds(wpm) * 8f;
            for (var t = 0d; t <= station.TotalSeconds + tail; t += 0.004d)
            {
                receiver.Advance(t, station.IsKeyDown(t));
            }

            Assert.AreEqual("05540079", receiver.Transcript);
        }
    }
}
