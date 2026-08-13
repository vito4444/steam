using System;
using System.Collections.Generic;
using System.Text;

namespace Decoder.Gameplay
{
    /// <summary>
    /// 一次上报留下的记录。
    ///
    /// 存的是玩家做过什么，不是分数。后面的班次要根据这些记录改变语气——
    /// 连着三班判得准，上级的回电会开始信任你；错过一次紧急电文，
    /// 之后每一班的开场都会带着那件事的阴影。
    /// </summary>
    [Serializable]
    public struct ReportRecord
    {
        public string shiftId;
        public string callsign;
        public ReportOutcome outcome;
        public ThreatLevel submittedLevel;
        public ThreatLevel correctLevel;
        public float accuracy;

        /// <summary>这次上报是否把等级判对了。</summary>
        public bool LevelCorrect => submittedLevel == correctLevel;

        /// <summary>是否把该报的事压下去了。这类错误的代价最重。</summary>
        public bool Underreported => (int)submittedLevel < (int)correctLevel;
    }

    /// <summary>
    /// 战役进度。
    ///
    /// 刻意做成纯数据加纯函数，不碰 Unity 的任何 API：
    /// 这样存档逻辑能在编辑器测试里完整跑一遍，
    /// 不用起播放模式，也不用真的读写磁盘。
    /// </summary>
    [Serializable]
    public sealed class CampaignState
    {
        /// <summary>存档格式版本。读到更高的版本号就不要硬解，宁可当新档。</summary>
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;

        /// <summary>玩家当前在第几班，从 0 开始。等于已完成的班数。</summary>
        public int shiftIndex;

        public List<ReportRecord> history = new List<ReportRecord>();

        /// <summary>已完成的班次数。</summary>
        public int CompletedShifts => shiftIndex;

        /// <summary>记下一次上报并推进到下一班。</summary>
        public void RecordAndAdvance(ReportRecord record)
        {
            history.Add(record);
            shiftIndex++;
        }

        /// <summary>
        /// 上级对玩家的信任程度，取值 -1 到 1。
        ///
        /// 漏报的扣分比误报重：把紧急电文当例行压下去，
        /// 后果是有人在边境上出事；把例行的报成紧急，
        /// 后果只是浪费一次核查。这个不对称是有意的。
        /// </summary>
        public float Standing
        {
            get
            {
                if (history.Count == 0)
                {
                    return 0f;
                }

                var score = 0f;
                foreach (var record in history)
                {
                    switch (record.outcome)
                    {
                        case ReportOutcome.Clean:
                            score += 1f;
                            break;
                        case ReportOutcome.Garbled:
                            score += 0.1f;
                            break;
                        case ReportOutcome.Overreacted:
                            score -= 0.4f;
                            break;
                        case ReportOutcome.Underreported:
                            score -= 1f;
                            break;
                        case ReportOutcome.Useless:
                            score -= 1.2f;
                            break;
                    }
                }

                return Math.Max(-1f, Math.Min(1f, score / history.Count));
            }
        }

        /// <summary>
        /// 一句概括玩家当前处境的话，用在班次开场。
        /// 不给数字——这个岗位上没人会告诉你你的评分是多少。
        /// </summary>
        public string StandingLine()
        {
            if (history.Count == 0)
            {
                return "你是新来的。没人认识你，也没人指望你。";
            }

            var standing = Standing;
            if (standing >= 0.7f)
            {
                return "科长开始把加急的件直接送到你桌上。";
            }

            if (standing >= 0.25f)
            {
                return "你的名字在值班表上排得越来越靠前。";
            }

            if (standing >= -0.25f)
            {
                return "没人夸你，也没人找你麻烦。";
            }

            if (standing >= -0.7f)
            {
                return "上个月的复核报告里，你的名字被圈了一次。";
            }

            return "换班的时候，接你岗的人不再跟你说话。";
        }

        /// <summary>玩家漏报过几次。剧情里有几处分支只看这个数。</summary>
        public int UnderreportCount
        {
            get
            {
                var count = 0;
                foreach (var record in history)
                {
                    if (record.Underreported)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>
        /// 序列化成一行行的文本。
        ///
        /// 用自己的格式而不是 JsonUtility：存档要能被人打开看懂，
        /// 出问题时玩家可以把它贴进反馈里，我也能一眼看出哪里坏了。
        /// </summary>
        public string Serialize()
        {
            var builder = new StringBuilder();
            builder.Append("decoder-save\n");
            builder.Append("version\t").Append(version).Append('\n');
            builder.Append("shift\t").Append(shiftIndex).Append('\n');

            foreach (var record in history)
            {
                builder.Append("report\t")
                    .Append(record.shiftId).Append('\t')
                    .Append(record.callsign).Append('\t')
                    .Append(record.outcome).Append('\t')
                    .Append(record.submittedLevel).Append('\t')
                    .Append(record.correctLevel).Append('\t')
                    .Append(record.accuracy.ToString("F4"))
                    .Append('\n');
            }

            return builder.ToString();
        }

        /// <summary>
        /// 从文本还原。
        ///
        /// 存档损坏时返回 null 而不是抛异常：玩家的存档坏了已经够糟了，
        /// 再让游戏崩一次没有任何意义。调用方拿到 null 就当新档开始。
        /// </summary>
        public static CampaignState Deserialize(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            var lines = text.Split('\n');
            if (lines.Length == 0 || lines[0].Trim() != "decoder-save")
            {
                return null;
            }

            var state = new CampaignState { history = new List<ReportRecord>() };

            for (var i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                var parts = line.Split('\t');
                switch (parts[0])
                {
                    case "version":
                        if (parts.Length < 2 || !int.TryParse(parts[1], out state.version))
                        {
                            return null;
                        }

                        if (state.version > CurrentVersion)
                        {
                            // 比本体还新的存档不要硬解，字段含义可能已经变了。
                            return null;
                        }

                        break;

                    case "shift":
                        if (parts.Length < 2 || !int.TryParse(parts[1], out state.shiftIndex))
                        {
                            return null;
                        }

                        break;

                    case "report":
                        if (parts.Length < 7)
                        {
                            return null;
                        }

                        if (!Enum.TryParse<ReportOutcome>(parts[3], out var outcome) ||
                            !Enum.TryParse<ThreatLevel>(parts[4], out var submitted) ||
                            !Enum.TryParse<ThreatLevel>(parts[5], out var correct) ||
                            !float.TryParse(parts[6], out var accuracy))
                        {
                            return null;
                        }

                        state.history.Add(new ReportRecord
                        {
                            shiftId = parts[1],
                            callsign = parts[2],
                            outcome = outcome,
                            submittedLevel = submitted,
                            correctLevel = correct,
                            accuracy = accuracy,
                        });
                        break;
                }
            }

            return state;
        }
    }
}
