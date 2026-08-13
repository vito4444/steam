using System.Linq;
using Decoder.Signal;
using NUnit.Framework;

namespace Decoder.Tests
{
    /// <summary>
    /// 摩尔斯编解码的测试。这一层是玩法内核：玩家听到的每一声都由它生成，
    /// 玩家抄下的每一个字母都由它判定，所以它必须逐位正确，不能靠"大概听着像"。
    /// </summary>
    public sealed class MorseCodeTests
    {
        [TestCase("SOS", "... --- ...")]
        [TestCase("E", ".")]
        [TestCase("T", "-")]
        [TestCase("PARIS", ".--. .- .-. .. ...")]
        [TestCase("0022", "----- ----- ..--- ..---")]
        [TestCase("HELLO WORLD", ".... . .-.. .-.. --- / .-- --- .-. .-.. -..")]
        public void Encode_MatchesItuStandardPatterns(string text, string expected)
        {
            Assert.AreEqual(expected, MorseCode.Encode(text));
        }

        [TestCase("... --- ...", "SOS")]
        [TestCase(".... . .-.. .-.. --- / .-- --- .-. .-.. -..", "HELLO WORLD")]
        [TestCase("----- ----- ..--- ..---", "0022")]
        public void Decode_MatchesItuStandardPatterns(string morse, string expected)
        {
            Assert.AreEqual(expected, MorseCode.Decode(morse));
        }

        [TestCase("THE QUICK BROWN FOX JUMPS OVER THE LAZY DOG")]
        [TestCase("0123456789")]
        [TestCase("CQ CQ DE R7X K")]
        [TestCase("0022 2429 7193 4316")]
        public void EncodeThenDecode_RoundTripsExactly(string text)
        {
            var roundTrip = MorseCode.Decode(MorseCode.Encode(text));
            Assert.AreEqual(text.ToUpperInvariant(), roundTrip);
        }

        [Test]
        public void Encode_SkipsUnsupportedCharacters()
        {
            // 电报发不出制表符和方括号，这些字符应当被丢弃而不是变成乱码。
            Assert.AreEqual(MorseCode.Encode("AB"), MorseCode.Encode("A\t[B]"));
        }

        [Test]
        public void Decode_UnknownPatternBecomesQuestionMark()
        {
            // 玩家听错一个符号时，应该得到一个明显的错字，而不是整句消失。
            Assert.AreEqual("S?S", MorseCode.Decode("... ........ ..."));
        }

        [Test]
        public void Decode_EmptyInputReturnsEmpty()
        {
            Assert.AreEqual(string.Empty, MorseCode.Decode(""));
            Assert.AreEqual(string.Empty, MorseCode.Decode("   "));
            Assert.AreEqual(string.Empty, MorseCode.Encode(""));
        }

        [TestCase(20f, 0.06f)]
        [TestCase(12f, 0.1f)]
        [TestCase(5f, 0.24f)]
        public void UnitSeconds_FollowsParisFormula(float wpm, float expected)
        {
            Assert.AreEqual(expected, MorseCode.UnitSeconds(wpm), 1e-5f);
        }

        [Test]
        public void BuildTimeline_SingleDitIsOneUnitOfCarrier()
        {
            var timeline = MorseCode.BuildTimeline(MorseCode.Encode("E"), 20f);

            Assert.AreEqual(1, timeline.Count);
            Assert.IsTrue(timeline[0].KeyDown);
            Assert.AreEqual(MorseCode.UnitSeconds(20f), timeline[0].Seconds, 1e-5f);
        }

        [Test]
        public void BuildTimeline_SingleDahIsThreeUnitsOfCarrier()
        {
            var timeline = MorseCode.BuildTimeline(MorseCode.Encode("T"), 20f);

            Assert.AreEqual(1, timeline.Count);
            Assert.IsTrue(timeline[0].KeyDown);
            Assert.AreEqual(MorseCode.UnitSeconds(20f) * 3f, timeline[0].Seconds, 1e-5f);
        }

        [Test]
        public void BuildTimeline_IntraCharacterGapIsOneUnit()
        {
            // "I" 是两个点，中间应当恰好隔 1 单位。
            var unit = MorseCode.UnitSeconds(20f);
            var timeline = MorseCode.BuildTimeline(MorseCode.Encode("I"), 20f);

            Assert.AreEqual(3, timeline.Count);
            Assert.IsTrue(timeline[0].KeyDown);
            Assert.IsFalse(timeline[1].KeyDown);
            Assert.IsTrue(timeline[2].KeyDown);
            Assert.AreEqual(unit, timeline[1].Seconds, 1e-5f);
        }

        [Test]
        public void BuildTimeline_InterCharacterGapIsThreeUnits()
        {
            // "EE" 是两个独立字符，中间应当隔 3 单位而不是 1 单位。
            var unit = MorseCode.UnitSeconds(20f);
            var timeline = MorseCode.BuildTimeline(MorseCode.Encode("EE"), 20f);

            Assert.AreEqual(3, timeline.Count);
            Assert.IsFalse(timeline[1].KeyDown);
            Assert.AreEqual(unit * 3f, timeline[1].Seconds, 1e-5f);
        }

        [Test]
        public void BuildTimeline_WordGapIsSevenUnits()
        {
            // "E E" 跨词，间隔应当是 7 单位，而不是字符间隔的 3 单位加词分隔符另算。
            var unit = MorseCode.UnitSeconds(20f);
            var timeline = MorseCode.BuildTimeline(MorseCode.Encode("E E"), 20f);

            Assert.AreEqual(3, timeline.Count);
            Assert.IsFalse(timeline[1].KeyDown);
            Assert.AreEqual(unit * 7f, timeline[1].Seconds, 1e-5f);
        }

        [Test]
        public void BuildTimeline_NeverEndsWithSilence()
        {
            // 尾部多出来的静音会让发报机在收尾时空转，也会让循环播放出现不规则的停顿。
            var timeline = MorseCode.BuildTimeline(MorseCode.Encode("CQ DE R7X"), 18f);

            Assert.Greater(timeline.Count, 0);
            Assert.IsTrue(timeline[timeline.Count - 1].KeyDown);
        }

        [Test]
        public void BuildTimeline_ParisAtTwentyWpmTakesThreeSeconds()
        {
            // PARIS 法的定义：一个 "PARIS " 恰好 50 单位。20 WPM 下每分钟 20 个 PARIS，
            // 所以单个 PARIS 含词尾间隔应为 3 秒。这里的时序不含尾部词间隔（7 单位），
            // 因此期望值是 50 - 7 = 43 单位。
            var unit = MorseCode.UnitSeconds(20f);
            var timeline = MorseCode.BuildTimeline(MorseCode.Encode("PARIS"), 20f);

            Assert.AreEqual(unit * 43f, MorseCode.TotalSeconds(timeline), 1e-4f);
        }

        [Test]
        public void BuildTimeline_TotalTimeScalesInverselyWithSpeed()
        {
            var slow = MorseCode.TotalSeconds(MorseCode.BuildTimeline(MorseCode.Encode("PARIS"), 10f));
            var fast = MorseCode.TotalSeconds(MorseCode.BuildTimeline(MorseCode.Encode("PARIS"), 20f));

            Assert.AreEqual(2f, slow / fast, 1e-4f);
        }

        [Test]
        public void BuildTimeline_AllSegmentsHavePositiveDuration()
        {
            // 零长片段会在音频合成时产生咔哒声。
            var timeline = MorseCode.BuildTimeline(
                MorseCode.Encode("THE QUICK BROWN FOX 0123 / TEST"), 15f);

            Assert.IsTrue(timeline.All(e => e.Seconds > 0f),
                "时序中存在零长或负长片段");
        }
    }
}
