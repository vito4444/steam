namespace Decoder.Gameplay
{
    /// <summary>
    /// 上报之后发生的事。
    ///
    /// 这套设计里没有分数。玩家干得好不好，只通过三样东西告诉他：
    /// 上级的回电、值班室黑板上同事留的字条、次日的报纸。
    /// 这三样都不直接评价玩家，它们只是陈述后来发生了什么。
    ///
    /// 文本按"后果类别 × 威胁等级"组织，而不是按准确度打分档，
    /// 因为玩家真正会追问的是"我报错等级会怎样"，不是"我抄对了百分之多少"。
    /// </summary>
    public static class ReportAftermath
    {
        /// <summary>上级的回电。语气克制，从不直接夸奖或责备。</summary>
        public static string Reply(TransmissionEntry entry, ReportGrade grade)
        {
            switch (grade.Outcome)
            {
                case ReportOutcome.Clean:
                    return entry.correctLevel >= ThreatLevel.Urgent
                        ? "收到。值班军官已离开办公室。"
                        : "收到。归档。";

                case ReportOutcome.Overreacted:
                    return grade.LevelDelta >= 2
                        ? "收到。下次判断等级前，先看看窗外有没有真的在打仗。"
                        : "收到。等级偏高。归档。";

                case ReportOutcome.Underreported:
                    return grade.LevelDelta <= -2
                        ? "收到。这条本该在四十分钟前送到指挥部。"
                        : "收到。等级偏低，已代为上调。";

                case ReportOutcome.Garbled:
                    return "收到。电文有缺漏，已要求邻站补抄。";

                default:
                    return "收到。内容无法识别，本条作废。";
            }
        }

        /// <summary>值班室黑板上的字条。这里是唯一有人味的地方。</summary>
        public static string DeskNote(TransmissionEntry entry, ReportGrade grade)
        {
            switch (grade.Outcome)
            {
                case ReportOutcome.Clean:
                    return string.IsNullOrEmpty(entry.debriefNote)
                        ? "壶里还有热水。"
                        : entry.debriefNote;

                case ReportOutcome.Overreacted:
                    return "老韩说他年轻时也这样，报什么都往大了报。";

                case ReportOutcome.Underreported:
                    return "早班的人问了一句，昨晚是不是漏了什么。";

                case ReportOutcome.Garbled:
                    return "抄不全就写抄不全，别猜。猜错了没人替你担。";

                default:
                    return "有人把你的上报单从架子上拿走了，没说去哪。";
            }
        }

        /// <summary>次日报纸的一行标题。它从不提监听站，只描述外面的世界。</summary>
        public static string Headline(TransmissionEntry entry, ReportGrade grade)
        {
            if (grade.Outcome == ReportOutcome.Useless || grade.Outcome == ReportOutcome.Garbled)
            {
                return "边境地区通讯中断，官方称系天气所致";
            }

            switch (entry.correctLevel)
            {
                case ThreatLevel.Flash:
                    return grade.LevelDelta < 0
                        ? "北部两处哨所昨夜失联，原因不明"
                        : "边防部队完成例行调动，未提及具体位置";

                case ThreatLevel.Urgent:
                    return grade.LevelDelta < 0
                        ? "运输队在山口滞留一夜，无人员伤亡报告"
                        : "气象部门发布强风预警，铁路运输部分调整";

                case ThreatLevel.Attention:
                    return "本报讯：入冬以来第一场雪覆盖北部林区";

                default:
                    return "农业合作社完成秋季收储任务";
            }
        }
    }
}
