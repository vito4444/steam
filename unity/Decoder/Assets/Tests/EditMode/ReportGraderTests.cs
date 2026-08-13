using Decoder.Gameplay;
using Decoder.Signal;
using NUnit.Framework;

namespace Decoder.Tests
{
    /// <summary>
    /// 上报判定的测试。这是玩法的裁判：它决定玩家一个班次的努力
    /// 换来什么后果，判错了整个反馈循环就是坏的。
    /// </summary>
    public sealed class ReportGraderTests
    {
        private const string SampleTable =
            "0001一\n0022中\n0079京\n0554北\n2429文\n4316码\n7193电\n";

        private static ChineseTelegraphCode Table()
        {
            return ChineseTelegraphCode.Parse(SampleTable);
        }

        private static TransmissionEntry PlainEntry()
        {
            return new TransmissionEntry
            {
                callsign = "R7X",
                frequencyKHz = 7042f,
                kind = SignalKind.PlainMorse,
                plainText = "CONVOY MOVING EAST",
                correctLevel = ThreatLevel.Urgent,
            };
        }

        private static TransmissionEntry TelegraphEntry()
        {
            return new TransmissionEntry
            {
                callsign = "M08",
                frequencyKHz = 6955f,
                kind = SignalKind.ChineseTelegraph,
                plainText = "北京",
                correctLevel = ThreatLevel.Attention,
            };
        }

        // ---- 编辑距离与相似度 ----

        [TestCase("", "", 0)]
        [TestCase("ABC", "ABC", 0)]
        [TestCase("ABC", "ABD", 1)]
        [TestCase("ABC", "AC", 1)]
        [TestCase("ABC", "AXBC", 1)]
        [TestCase("KITTEN", "SITTING", 3)]
        public void LevenshteinDistance_MatchesKnownValues(string a, string b, int expected)
        {
            Assert.AreEqual(expected, ReportGrader.LevenshteinDistance(a, b));
        }

        [Test]
        public void LevenshteinDistance_IsSymmetric()
        {
            Assert.AreEqual(ReportGrader.LevenshteinDistance("CONVOY", "CONVY"),
                ReportGrader.LevenshteinDistance("CONVY", "CONVOY"));
        }

        [Test]
        public void SimilarityRatio_IsOneForIdenticalStrings()
        {
            Assert.AreEqual(1f, ReportGrader.SimilarityRatio("0022", "0022"), 1e-6f);
        }

        [Test]
        public void SimilarityRatio_MissingOneDigitScoresBetterThanAllWrong()
        {
            // 这条断言守的是一个具体的设计意图：漏抄一位应当明显好过整段乱抄。
            // 如果换成逐位比对，两者会得到相同的分数，玩家就学不到抄报最重要的直觉。
            var missedOne = ReportGrader.SimilarityRatio("002242971934316", "0022242971934316");
            var allWrong = ReportGrader.SimilarityRatio("1111111111111111", "0022242971934316");

            Assert.Greater(missedOne, 0.9f);
            Assert.Less(allWrong, 0.3f);
            Assert.Greater(missedOne, allWrong);
        }

        [Test]
        public void SimilarityRatio_EmptyAgainstNonEmptyIsZero()
        {
            Assert.AreEqual(0f, ReportGrader.SimilarityRatio("", "0022"));
            Assert.AreEqual(1f, ReportGrader.SimilarityRatio("", ""));
        }

        // ---- 归一化 ----

        [Test]
        public void Normalize_IgnoresGroupingAndCase()
        {
            // 玩家抄报时会自然分组，分组方式不同不该判错。
            Assert.AreEqual("00222429", ReportGrader.Normalize("0022 2429"));
            Assert.AreEqual("00222429", ReportGrader.Normalize("0022-2429"));
            Assert.AreEqual("CONVOY", ReportGrader.Normalize("con voy"));
        }

        // ---- 明码信号判定 ----

        [Test]
        public void Grade_PerfectCopyWithRightLevelIsClean()
        {
            var grade = ReportGrader.Grade(PlainEntry(), new ReportSubmission
            {
                Callsign = "R7X",
                FrequencyKHz = 7042f,
                CopiedText = "CONVOY MOVING EAST",
                Level = ThreatLevel.Urgent,
            }, Table());

            Assert.AreEqual(ReportOutcome.Clean, grade.Outcome);
            Assert.AreEqual(1f, grade.Accuracy, 1e-6f);
            Assert.IsTrue(grade.CallsignCorrect);
            Assert.IsTrue(grade.FrequencyCorrect);
            Assert.AreEqual(0, grade.LevelDelta);
        }

        [Test]
        public void Grade_CorrectCopyButLevelTooHighIsOverreacted()
        {
            var grade = ReportGrader.Grade(PlainEntry(), new ReportSubmission
            {
                Callsign = "R7X",
                FrequencyKHz = 7042f,
                CopiedText = "CONVOY MOVING EAST",
                Level = ThreatLevel.Flash,
            }, Table());

            Assert.AreEqual(ReportOutcome.Overreacted, grade.Outcome);
            Assert.AreEqual(1, grade.LevelDelta);
        }

        [Test]
        public void Grade_CorrectCopyButLevelTooLowIsUnderreported()
        {
            var grade = ReportGrader.Grade(PlainEntry(), new ReportSubmission
            {
                Callsign = "R7X",
                FrequencyKHz = 7042f,
                CopiedText = "CONVOY MOVING EAST",
                Level = ThreatLevel.Routine,
            }, Table());

            Assert.AreEqual(ReportOutcome.Underreported, grade.Outcome);
            Assert.AreEqual(-2, grade.LevelDelta);
        }

        [Test]
        public void Grade_SmallCopyErrorIsGarbledNotClean()
        {
            // 抄错一个字母就不该算干净，但也不该算完全没用。
            var grade = ReportGrader.Grade(PlainEntry(), new ReportSubmission
            {
                Callsign = "R7X",
                FrequencyKHz = 7042f,
                CopiedText = "CONVOY MOVING WEST",
                Level = ThreatLevel.Urgent,
            }, Table());

            Assert.AreEqual(ReportOutcome.Garbled, grade.Outcome);
            Assert.Greater(grade.Accuracy, ReportGrader.UselessThreshold);
            Assert.Less(grade.Accuracy, ReportGrader.CleanThreshold);
        }

        [Test]
        public void Grade_MostlyWrongCopyIsUseless()
        {
            var grade = ReportGrader.Grade(PlainEntry(), new ReportSubmission
            {
                Callsign = "R7X",
                FrequencyKHz = 7042f,
                CopiedText = "XXXXX",
                Level = ThreatLevel.Urgent,
            }, Table());

            Assert.AreEqual(ReportOutcome.Useless, grade.Outcome);
        }

        [Test]
        public void Grade_LevelIsIgnoredWhenCopyIsUseless()
        {
            // 内容都没抄对，等级判对了也没有意义，不该因为等级正确就上调评价。
            var grade = ReportGrader.Grade(PlainEntry(), new ReportSubmission
            {
                CopiedText = "ZZZZZ",
                Level = ThreatLevel.Urgent,
            }, Table());

            Assert.AreEqual(ReportOutcome.Useless, grade.Outcome);
        }

        // ---- 频率与呼号 ----

        [TestCase(7042f, true)]
        [TestCase(7042.4f, true)]
        [TestCase(7041.6f, true)]
        [TestCase(7043f, false)]
        [TestCase(7000f, false)]
        public void Grade_FrequencyToleranceIsHalfKilohertz(float reported, bool expected)
        {
            var grade = ReportGrader.Grade(PlainEntry(), new ReportSubmission
            {
                Callsign = "R7X",
                FrequencyKHz = reported,
                CopiedText = "CONVOY MOVING EAST",
                Level = ThreatLevel.Urgent,
            }, Table());

            Assert.AreEqual(expected, grade.FrequencyCorrect);
        }

        [Test]
        public void Grade_CallsignComparisonIgnoresCase()
        {
            var grade = ReportGrader.Grade(PlainEntry(), new ReportSubmission
            {
                Callsign = "r7x",
                FrequencyKHz = 7042f,
                CopiedText = "CONVOY MOVING EAST",
                Level = ThreatLevel.Urgent,
            }, Table());

            Assert.IsTrue(grade.CallsignCorrect);
        }

        // ---- 中文电码信号判定 ----

        [Test]
        public void Grade_ChineseTelegraphComparesDigitStream()
        {
            var table = Table();
            // 北=0554，京=0079。
            var grade = ReportGrader.Grade(TelegraphEntry(), new ReportSubmission
            {
                Callsign = "M08",
                FrequencyKHz = 6955f,
                CopiedText = "0554 0079",
                Level = ThreatLevel.Attention,
            }, table);

            Assert.AreEqual(ReportOutcome.Clean, grade.Outcome);
            Assert.AreEqual("北京", grade.DecodedText);
            Assert.AreEqual("北京", grade.ExpectedText);
        }

        [Test]
        public void Grade_ChineseTelegraphWrongDigitShowsWhichGroupFailed()
        {
            var grade = ReportGrader.Grade(TelegraphEntry(), new ReportSubmission
            {
                Callsign = "M08",
                FrequencyKHz = 6955f,
                CopiedText = "0554 9999",
                Level = ThreatLevel.Attention,
            }, Table());

            // 复盘时玩家要能一眼看出是第二组抄错了。
            Assert.AreEqual("北□", grade.DecodedText);
            Assert.AreNotEqual(ReportOutcome.Clean, grade.Outcome);
        }

        [Test]
        public void Grade_ChineseTelegraphAcceptsUngroupedDigits()
        {
            var grade = ReportGrader.Grade(TelegraphEntry(), new ReportSubmission
            {
                Callsign = "M08",
                FrequencyKHz = 6955f,
                CopiedText = "05540079",
                Level = ThreatLevel.Attention,
            }, Table());

            Assert.AreEqual(ReportOutcome.Clean, grade.Outcome);
        }

        // ---- 信号构建 ----

        [Test]
        public void TransmissionEntry_ChineseTelegraphSendsDigitsNotCharacters()
        {
            // 电报线路上传的是数字，不是汉字。这条断言守的是整个中文电码设定的前提。
            var air = TelegraphEntry().ResolveAirText(Table());
            Assert.AreEqual("05540079", air);
        }

        [Test]
        public void TransmissionEntry_PlainMorseIsUppercased()
        {
            var entry = new TransmissionEntry { plainText = "cq de r7x" };
            Assert.AreEqual("CQ DE R7X", entry.ResolveAirText(null));
        }

        [Test]
        public void TransmissionEntry_BuildsStationWithMatchingFrequency()
        {
            var station = TelegraphEntry().BuildStation(Table());
            Assert.AreEqual(6955f, station.FrequencyKHz);
            Assert.AreEqual("M08", station.Callsign);
            Assert.Greater(station.TotalSeconds, 0f);
        }

        [Test]
        public void ShiftDefinition_FindsPrimaryTransmission()
        {
            var shift = new ShiftDefinition();
            shift.transmissions.Add(new TransmissionEntry { callsign = "A" });
            shift.transmissions.Add(new TransmissionEntry { callsign = "B", isPrimary = true });

            Assert.IsNotNull(shift.Primary);
            Assert.AreEqual("B", shift.Primary.callsign);
        }

        [Test]
        public void ShiftDefinition_BuildsOneStationPerTransmission()
        {
            var shift = new ShiftDefinition();
            shift.transmissions.Add(new TransmissionEntry { callsign = "A", frequencyKHz = 7000f, plainText = "E" });
            shift.transmissions.Add(new TransmissionEntry { callsign = "B", frequencyKHz = 7100f, plainText = "T" });

            var stations = shift.BuildStations(Table());
            Assert.AreEqual(2, stations.Count);
            Assert.AreEqual(7000f, stations[0].FrequencyKHz);
            Assert.AreEqual(7100f, stations[1].FrequencyKHz);
        }
    }
}
