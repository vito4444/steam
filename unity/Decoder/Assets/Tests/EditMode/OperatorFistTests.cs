using System.Collections.Generic;
using Decoder.Signal;
using NUnit.Framework;

namespace Decoder.Tests
{
    /// <summary>
    /// 手键指纹的测试。
    ///
    /// 这一层要撑住的玩法是：玩家熟悉了几个常驻电台的手法之后，
    /// 在某一班遇到"呼号对得上但手法不对"的情况。
    /// 如果测量结果不稳定，玩家每次听同一个人都会得到不同的判断，
    /// 这层玩法就变成了掷骰子。
    /// </summary>
    public sealed class OperatorFistTests
    {
        private static OperatorFist Fist(float dah, float charGap, float wordGap, float jitter)
        {
            return new OperatorFist
            {
                dahRatio = dah,
                charGapRatio = charGap,
                wordGapRatio = wordGap,
                jitter = jitter,
            };
        }

        private static List<MorseCode.Element> Timeline(OperatorFist fist, int seed = 7)
        {
            // 够长的一段电文，保证点、划、字符间隔、词间隔的样本都取得到。
            return MorseCode.BuildTimeline(
                MorseCode.Encode("CQ CQ DE M08 K"), 12f, fist, seed);
        }

        [Test]
        public void Machine_MatchesTextbookRatios()
        {
            var machine = OperatorFist.Machine;

            Assert.AreEqual(3f, machine.dahRatio, 0.001f);
            Assert.AreEqual(3f, machine.charGapRatio, 0.001f);
            Assert.AreEqual(7f, machine.wordGapRatio, 0.001f);
            Assert.AreEqual(0f, machine.jitter, 0.001f);
            Assert.IsTrue(machine.IsValid);
        }

        [Test]
        public void DefaultStruct_IsNotValid()
        {
            // 零值结构体不该被当成真的手法用。
            Assert.IsFalse(default(OperatorFist).IsValid);
        }

        [Test]
        public void BuildTimeline_WithoutFist_MatchesMachineFist()
        {
            // 不指定手法时必须还是原来那条精确时序，
            // 否则所有既有的音频与解码行为都会跟着变。
            var plain = MorseCode.BuildTimeline(MorseCode.Encode("SOS"), 15f);
            var machine = MorseCode.BuildTimeline(MorseCode.Encode("SOS"), 15f, OperatorFist.Machine, 0);

            Assert.AreEqual(plain.Count, machine.Count);
            for (var i = 0; i < plain.Count; i++)
            {
                Assert.AreEqual(plain[i].KeyDown, machine[i].KeyDown);
                Assert.AreEqual(plain[i].Seconds, machine[i].Seconds, 1e-6f);
            }
        }

        [Test]
        public void BuildTimeline_IsDeterministicForSameSeed()
        {
            // 玩家反复听同一条电文来分辨手法时，听到的东西不能每次都变。
            var fist = Fist(3.4f, 2.6f, 7f, 0.08f);
            var a = Timeline(fist, 42);
            var b = Timeline(fist, 42);

            Assert.AreEqual(a.Count, b.Count);
            for (var i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].Seconds, b[i].Seconds, 1e-6f);
            }
        }

        [Test]
        public void BuildTimeline_DiffersBetweenSeeds()
        {
            var fist = Fist(3f, 3f, 7f, 0.1f);
            var a = Timeline(fist, 1);
            var b = Timeline(fist, 2);

            var identical = true;
            for (var i = 0; i < a.Count && identical; i++)
            {
                if (Mathf(a[i].Seconds - b[i].Seconds) > 1e-6f)
                {
                    identical = false;
                }
            }

            Assert.IsFalse(identical, "不同种子应当产生不同的抖动");
        }

        private static float Mathf(float v) => v < 0f ? -v : v;

        [Test]
        public void BuildTimeline_LongerDahRatioProducesLongerKeyDowns()
        {
            var normal = MorseCode.BuildTimeline(MorseCode.Encode("T"), 12f, Fist(3f, 3f, 7f, 0f), 0);
            var dragged = MorseCode.BuildTimeline(MorseCode.Encode("T"), 12f, Fist(3.8f, 3f, 7f, 0f), 0);

            // T 就是一个划。拖得长的人这一划应该明显更久。
            Assert.Greater(dragged[0].Seconds, normal[0].Seconds);
        }

        [Test]
        public void BuildTimeline_NoJitterKeepsEveryDitIdentical()
        {
            var timeline = MorseCode.BuildTimeline(MorseCode.Encode("HHH"), 12f, OperatorFist.Machine, 0);

            float? first = null;
            foreach (var element in timeline)
            {
                if (!element.KeyDown)
                {
                    continue;
                }

                first ??= element.Seconds;
                Assert.AreEqual(first.Value, element.Seconds, 1e-6f, "机器发报不该有任何偏差");
            }
        }

        // ---- 测量 ----

        [Test]
        public void Measure_RecoversMachineFistExactly()
        {
            var measured = FistAnalyzer.Measure(Timeline(OperatorFist.Machine));

            Assert.AreEqual(3f, measured.dahRatio, 0.05f);
            Assert.AreEqual(3f, measured.charGapRatio, 0.05f);
            Assert.AreEqual(0f, measured.jitter, 0.01f);
        }

        [TestCase(2.5f, 3f)]
        [TestCase(3f, 3f)]
        [TestCase(3.6f, 2.4f)]
        [TestCase(3.2f, 3.8f)]
        public void Measure_RecoversRatiosFromTimeline(float dah, float charGap)
        {
            // 这是整层玩法的地基：从玩家实际听到的东西里量出手法，
            // 而不是读发报方声称的参数。冒充者可以伪造呼号，伪造不了自己的手。
            var fist = Fist(dah, charGap, 7f, 0f);
            var measured = FistAnalyzer.Measure(Timeline(fist));

            Assert.AreEqual(dah, measured.dahRatio, 0.15f, "划长比没量准");
            Assert.AreEqual(charGap, measured.charGapRatio, 0.2f, "字符间隔没量准");
        }

        [Test]
        public void Measure_DetectsJitter()
        {
            var steady = FistAnalyzer.Measure(Timeline(Fist(3f, 3f, 7f, 0f)));
            var shaky = FistAnalyzer.Measure(Timeline(Fist(3f, 3f, 7f, 0.2f)));

            Assert.Less(steady.jitter, shaky.jitter, "手抖的人应该量出更大的抖动");
        }

        [Test]
        public void Measure_ReturnsInvalidOnTooFewElements()
        {
            // 样本太少量出来的东西没有意义，宁可说"看不出来"。
            var stub = new List<MorseCode.Element> { new MorseCode.Element(true, 0.1f) };

            Assert.IsFalse(FistAnalyzer.Measure(stub).IsValid);
            Assert.IsFalse(FistAnalyzer.Measure(null).IsValid);
        }

        [Test]
        public void Measure_IsIndependentOfSendingSpeed()
        {
            // 同一个人发快发慢，手法应该是同一份。
            // 量出来的东西如果跟着 WPM 变，玩家就没法靠它认人。
            var fist = Fist(3.4f, 2.6f, 7f, 0f);
            var slow = FistAnalyzer.Measure(
                MorseCode.BuildTimeline(MorseCode.Encode("CQ CQ DE M08 K"), 8f, fist, 3));
            var fast = FistAnalyzer.Measure(
                MorseCode.BuildTimeline(MorseCode.Encode("CQ CQ DE M08 K"), 20f, fist, 3));

            Assert.AreEqual(slow.dahRatio, fast.dahRatio, 0.1f);
            Assert.AreEqual(slow.charGapRatio, fast.charGapRatio, 0.15f);
        }

        // ---- 比对 ----

        [Test]
        public void DistanceTo_IsZeroForIdenticalFists()
        {
            var fist = Fist(3.2f, 2.8f, 7f, 0.05f);

            Assert.AreEqual(0f, fist.DistanceTo(fist), 0.001f);
        }

        [Test]
        public void DistanceTo_IsSymmetric()
        {
            var a = Fist(3.2f, 2.8f, 7f, 0.05f);
            var b = Fist(2.7f, 3.4f, 6.5f, 0.1f);

            Assert.AreEqual(a.DistanceTo(b), b.DistanceTo(a), 0.001f);
        }

        [Test]
        public void DistanceTo_GrowsWithDifference()
        {
            var baseline = Fist(3f, 3f, 7f, 0f);
            var near = Fist(3.1f, 3f, 7f, 0f);
            var far = Fist(3.9f, 2.2f, 7f, 0f);

            Assert.Less(baseline.DistanceTo(near), baseline.DistanceTo(far));
        }

        [Test]
        public void DistanceTo_SeparatesImpostorFromGenuineOperator()
        {
            // 这是玩法能不能成立的判据：同一个人两次发报之间的差距，
            // 必须明显小于冒充者与本人之间的差距。分不开就没得玩。
            var genuine = Fist(3.35f, 2.55f, 7f, 0.06f);
            var impostor = Fist(2.75f, 3.4f, 7f, 0.12f);

            var genuineFirst = FistAnalyzer.Measure(Timeline(genuine, 11));
            var genuineSecond = FistAnalyzer.Measure(Timeline(genuine, 29));
            var impostorHeard = FistAnalyzer.Measure(Timeline(impostor, 29));

            var selfDistance = genuineFirst.DistanceTo(genuineSecond);
            var impostorDistance = genuineFirst.DistanceTo(impostorHeard);

            Assert.Less(selfDistance, impostorDistance * 0.5f,
                $"本人前后差 {selfDistance:F3}，与冒充者差 {impostorDistance:F3}，分不开");
        }

        // ---- 描述 ----

        [Test]
        public void Describe_CallsOutMachineSending()
        {
            StringAssert.Contains("机器", OperatorFist.Machine.Describe());
        }

        [Test]
        public void Describe_IsNeverEmptyAndCarriesNoNumbers()
        {
            // 报务员看的是纸带和耳朵，不是仪表。玩家要靠"这次听起来划拖得比平时长"
            // 来起疑，而不是靠比对小数点后两位。
            var samples = new[]
            {
                Fist(3.6f, 2.3f, 7f, 0.2f),
                Fist(2.5f, 3.8f, 7f, 0.03f),
                Fist(3f, 3f, 7f, 0.08f),
                OperatorFist.Machine,
                default(OperatorFist),
            };

            foreach (var fist in samples)
            {
                var text = fist.Describe();
                Assert.IsNotEmpty(text);
                foreach (var c in text)
                {
                    Assert.IsFalse(char.IsDigit(c), $"描述里不该出现数字：{text}");
                }
            }
        }

        [Test]
        public void Describe_DistinguishesDraggedFromClippedDashes()
        {
            var dragged = Fist(3.6f, 3f, 7f, 0.08f).Describe();
            var clipped = Fist(2.5f, 3f, 7f, 0.08f).Describe();

            Assert.AreNotEqual(dragged, clipped);
        }
    }
}
