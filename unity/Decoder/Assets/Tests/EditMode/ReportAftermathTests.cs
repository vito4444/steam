using System;
using System.Collections.Generic;
using Decoder.Gameplay;
using NUnit.Framework;

namespace Decoder.Tests
{
    /// <summary>
    /// 后果文本的测试。这套设计里没有分数，玩家全靠回电、字条和报纸
    /// 判断自己干得怎么样。任何一个组合漏了文本，玩家在那种情况下就得不到反馈，
    /// 而这恰恰是最需要反馈的时候——通常是他做错了事的时候。
    /// </summary>
    public sealed class ReportAftermathTests
    {
        private static IEnumerable<ReportOutcome> AllOutcomes()
        {
            return (ReportOutcome[])Enum.GetValues(typeof(ReportOutcome));
        }

        private static IEnumerable<ThreatLevel> AllLevels()
        {
            return (ThreatLevel[])Enum.GetValues(typeof(ThreatLevel));
        }

        [Test]
        public void EveryOutcomeAndLevelCombinationHasText()
        {
            foreach (var outcome in AllOutcomes())
            {
                foreach (var level in AllLevels())
                {
                    foreach (var delta in new[] { -3, -1, 0, 1, 3 })
                    {
                        var entry = new TransmissionEntry { correctLevel = level };
                        var grade = new ReportGrade { Outcome = outcome, LevelDelta = delta };

                        Assert.IsNotEmpty(ReportAftermath.Reply(entry, grade),
                            $"{outcome}/{level}/delta={delta} 缺少回电");
                        Assert.IsNotEmpty(ReportAftermath.DeskNote(entry, grade),
                            $"{outcome}/{level}/delta={delta} 缺少黑板字条");
                        Assert.IsNotEmpty(ReportAftermath.Headline(entry, grade),
                            $"{outcome}/{level}/delta={delta} 缺少报纸标题");
                    }
                }
            }
        }

        [Test]
        public void ReplyDistinguishesSevereUnderreportingFromMild()
        {
            // 少报两级和少报一级的后果必须不同，否则玩家学不到等级判断的分量。
            var entry = new TransmissionEntry { correctLevel = ThreatLevel.Flash };
            var mild = new ReportGrade { Outcome = ReportOutcome.Underreported, LevelDelta = -1 };
            var severe = new ReportGrade { Outcome = ReportOutcome.Underreported, LevelDelta = -3 };

            Assert.AreNotEqual(ReportAftermath.Reply(entry, mild),
                ReportAftermath.Reply(entry, severe));
        }

        [Test]
        public void CleanReportOnUrgentSignalMovesSomeone()
        {
            // 高等级的准确上报应当有可见的后果，不能和例行归档说一样的话。
            var urgent = new TransmissionEntry { correctLevel = ThreatLevel.Urgent };
            var routine = new TransmissionEntry { correctLevel = ThreatLevel.Routine };
            var clean = new ReportGrade { Outcome = ReportOutcome.Clean };

            Assert.AreNotEqual(ReportAftermath.Reply(routine, clean),
                ReportAftermath.Reply(urgent, clean));
        }

        [Test]
        public void DeskNoteFallsBackWhenTransmissionHasNoDebrief()
        {
            // 内容没写 debriefNote 时不能露出空字符串，得有个兜底。
            var entry = new TransmissionEntry { debriefNote = string.Empty };
            var clean = new ReportGrade { Outcome = ReportOutcome.Clean };

            Assert.IsNotEmpty(ReportAftermath.DeskNote(entry, clean));
        }

        [Test]
        public void DeskNoteUsesAuthoredTextWhenAvailable()
        {
            var entry = new TransmissionEntry { debriefNote = "值班军官记下了这条。" };
            var clean = new ReportGrade { Outcome = ReportOutcome.Clean };

            Assert.AreEqual(entry.debriefNote, ReportAftermath.DeskNote(entry, clean));
        }

        [Test]
        public void HeadlineNeverMentionsTheListeningStation()
        {
            // 报纸是外部视角，提到监听站会破坏整个设定。
            foreach (var outcome in AllOutcomes())
            {
                foreach (var level in AllLevels())
                {
                    var entry = new TransmissionEntry { correctLevel = level };
                    var grade = new ReportGrade { Outcome = outcome, LevelDelta = 0 };
                    var headline = ReportAftermath.Headline(entry, grade);

                    Assert.IsFalse(headline.Contains("监听"), $"报纸标题泄露了设定: {headline}");
                    Assert.IsFalse(headline.Contains("电台"), $"报纸标题泄露了设定: {headline}");
                }
            }
        }
    }
}
