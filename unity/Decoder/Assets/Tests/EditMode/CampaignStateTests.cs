using Decoder.Gameplay;
using NUnit.Framework;

namespace Decoder.Tests
{
    /// <summary>
    /// 战役进度与存档的测试。
    ///
    /// 存档坏掉的代价比大多数 bug 都高：玩家丢的不是一次运行，
    /// 是他之前几个小时做过的所有判断。所以这里对损坏输入的容错
    /// 测得比正常路径还密。
    /// </summary>
    public sealed class CampaignStateTests
    {
        private static ReportRecord Record(
            ReportOutcome outcome,
            ThreatLevel submitted = ThreatLevel.Routine,
            ThreatLevel correct = ThreatLevel.Routine)
        {
            return new ReportRecord
            {
                shiftId = "shift-01",
                callsign = "M08",
                outcome = outcome,
                submittedLevel = submitted,
                correctLevel = correct,
                accuracy = 0.9f,
            };
        }

        [Test]
        public void NewState_StartsAtFirstShiftWithNoHistory()
        {
            var state = new CampaignState();

            Assert.AreEqual(0, state.shiftIndex);
            Assert.AreEqual(0, state.history.Count);
            Assert.AreEqual(0f, state.Standing);
        }

        [Test]
        public void RecordAndAdvance_AppendsHistoryAndMovesToNextShift()
        {
            var state = new CampaignState();
            state.RecordAndAdvance(Record(ReportOutcome.Clean));

            Assert.AreEqual(1, state.shiftIndex);
            Assert.AreEqual(1, state.history.Count);
            Assert.AreEqual(1, state.CompletedShifts);
        }

        [Test]
        public void Standing_RewardsExactReports()
        {
            var state = new CampaignState();
            state.RecordAndAdvance(Record(ReportOutcome.Clean));
            state.RecordAndAdvance(Record(ReportOutcome.Clean));

            Assert.AreEqual(1f, state.Standing, 0.001f);
        }

        [Test]
        public void Standing_PunishesUnderreportingHarderThanOverreporting()
        {
            // 把紧急电文压成例行，后果是有人在边境上出事；
            // 把例行的报成紧急，后果只是浪费一次核查。
            // 这两件事的分量不该一样。
            var under = new CampaignState();
            under.RecordAndAdvance(Record(ReportOutcome.Underreported));

            var over = new CampaignState();
            over.RecordAndAdvance(Record(ReportOutcome.Overreacted));

            Assert.Less(under.Standing, over.Standing);
        }

        [Test]
        public void Standing_IsClampedToUnitRange()
        {
            var state = new CampaignState();
            for (var i = 0; i < 20; i++)
            {
                state.RecordAndAdvance(Record(ReportOutcome.Useless));
            }

            Assert.GreaterOrEqual(state.Standing, -1f);

            var good = new CampaignState();
            for (var i = 0; i < 20; i++)
            {
                good.RecordAndAdvance(Record(ReportOutcome.Clean));
            }

            Assert.LessOrEqual(good.Standing, 1f);
        }

        [Test]
        public void StandingLine_IsNeverEmptyAndCarriesNoNumbers()
        {
            // 这个岗位上没人会告诉你你的评分是多少。
            var outcomes = new[]
            {
                ReportOutcome.Clean, ReportOutcome.Garbled, ReportOutcome.Overreacted,
                ReportOutcome.Underreported, ReportOutcome.Useless,
            };

            foreach (var outcome in outcomes)
            {
                var state = new CampaignState();
                state.RecordAndAdvance(Record(outcome));

                var line = state.StandingLine();
                Assert.IsNotEmpty(line, $"{outcome} 没有对应的处境描述");
                foreach (var c in line)
                {
                    Assert.IsFalse(char.IsDigit(c), $"处境描述里不该出现数字：{line}");
                }
            }
        }

        [Test]
        public void StandingLine_DiffersBetweenGoodAndBadRecords()
        {
            var good = new CampaignState();
            var bad = new CampaignState();
            for (var i = 0; i < 3; i++)
            {
                good.RecordAndAdvance(Record(ReportOutcome.Clean));
                bad.RecordAndAdvance(Record(ReportOutcome.Useless));
            }

            Assert.AreNotEqual(good.StandingLine(), bad.StandingLine());
        }

        [Test]
        public void UnderreportCount_CountsOnlyLevelsBelowCorrect()
        {
            var state = new CampaignState();
            state.RecordAndAdvance(Record(ReportOutcome.Underreported, ThreatLevel.Routine, ThreatLevel.Urgent));
            state.RecordAndAdvance(Record(ReportOutcome.Overreacted, ThreatLevel.Flash, ThreatLevel.Routine));
            state.RecordAndAdvance(Record(ReportOutcome.Clean, ThreatLevel.Urgent, ThreatLevel.Urgent));

            Assert.AreEqual(1, state.UnderreportCount);
        }

        [Test]
        public void LevelCorrect_ReflectsExactLevelMatch()
        {
            Assert.IsTrue(Record(ReportOutcome.Clean, ThreatLevel.Urgent, ThreatLevel.Urgent).LevelCorrect);
            Assert.IsFalse(Record(ReportOutcome.Garbled, ThreatLevel.Routine, ThreatLevel.Urgent).LevelCorrect);
        }

        // ---- 存档 ----

        [Test]
        public void SerializeThenDeserialize_PreservesEverything()
        {
            var state = new CampaignState();
            state.RecordAndAdvance(new ReportRecord
            {
                shiftId = "shift-01",
                callsign = "M08",
                outcome = ReportOutcome.Garbled,
                submittedLevel = ThreatLevel.Urgent,
                correctLevel = ThreatLevel.Flash,
                accuracy = 0.8125f,
            });
            state.RecordAndAdvance(Record(ReportOutcome.Clean));

            var restored = CampaignState.Deserialize(state.Serialize());

            Assert.IsNotNull(restored);
            Assert.AreEqual(state.shiftIndex, restored.shiftIndex);
            Assert.AreEqual(state.history.Count, restored.history.Count);
            Assert.AreEqual("M08", restored.history[0].callsign);
            Assert.AreEqual(ReportOutcome.Garbled, restored.history[0].outcome);
            Assert.AreEqual(ThreatLevel.Flash, restored.history[0].correctLevel);
            Assert.AreEqual(0.8125f, restored.history[0].accuracy, 0.0001f);
            Assert.AreEqual(state.Standing, restored.Standing, 0.0001f);
        }

        [Test]
        public void Serialize_EmptyStateRoundTrips()
        {
            var restored = CampaignState.Deserialize(new CampaignState().Serialize());

            Assert.IsNotNull(restored);
            Assert.AreEqual(0, restored.shiftIndex);
            Assert.AreEqual(0, restored.history.Count);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("这不是存档")]
        [TestCase("decoder-save\nversion\t不是数字\n")]
        [TestCase("decoder-save\nshift\tx\n")]
        [TestCase("decoder-save\nreport\t字段不够\n")]
        [TestCase("decoder-save\nreport\ts\tM08\t不存在的结果\tRoutine\tRoutine\t0.5\n")]
        public void Deserialize_ReturnsNullOnCorruptInputInsteadOfThrowing(string text)
        {
            // 玩家的存档坏了已经够糟了，再让游戏崩一次没有任何意义。
            Assert.DoesNotThrow(() => CampaignState.Deserialize(text));
            Assert.IsNull(CampaignState.Deserialize(text));
        }

        [Test]
        public void Deserialize_RejectsFutureVersion()
        {
            // 比本体还新的存档不要硬解，字段含义可能已经变了。
            var future = $"decoder-save\nversion\t{CampaignState.CurrentVersion + 1}\nshift\t3\n";

            Assert.IsNull(CampaignState.Deserialize(future));
        }

        [Test]
        public void Deserialize_AcceptsCurrentVersion()
        {
            var text = $"decoder-save\nversion\t{CampaignState.CurrentVersion}\nshift\t2\n";
            var state = CampaignState.Deserialize(text);

            Assert.IsNotNull(state);
            Assert.AreEqual(2, state.shiftIndex);
        }

        [Test]
        public void Serialize_IsHumanReadable()
        {
            // 存档要能被人打开看懂：出问题时玩家可以把它贴进反馈里。
            var state = new CampaignState();
            state.RecordAndAdvance(Record(ReportOutcome.Clean));

            var text = state.Serialize();

            StringAssert.StartsWith("decoder-save", text);
            StringAssert.Contains("M08", text);
            StringAssert.Contains("Clean", text);
        }
    }
}
