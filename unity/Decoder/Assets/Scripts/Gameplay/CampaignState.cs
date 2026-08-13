using System;
using System.Collections.Generic;
using System.Text;
using Decoder.Signal;

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
    /// 班次进行到一半时的现场。
    ///
    /// 玩家可能抄了十分钟的电码才不得不下线。把这些丢掉的代价不小，
    /// 所以连密码本翻到第几页、上报单填了一半的呼号都一并存下来，
    /// 让他回来时看到的还是离开时那张桌子。
    /// </summary>
    [Serializable]
    public struct ShiftProgress
    {
        public bool active;
        public string shiftId;
        public string copied;
        public string solved;
        public string lookup;
        public string callsign;
        public string frequency;
        public int padPage;
        public ThreatLevel level;

        /// <summary>这份现场是不是属于指定班次的。班次对不上就不该恢复。</summary>
        public bool BelongsTo(string id)
        {
            return active && !string.IsNullOrEmpty(shiftId) && shiftId == id;
        }
    }

    /// <summary>
    /// 档案里记着的一个电台。
    ///
    /// 玩家隔了几天回来接着玩，记不住 M08 上次听起来什么样。
    /// 这层玩法唯一的线索不能只存在他脑子里。
    /// </summary>
    [Serializable]
    public struct FistRecord
    {
        public string callsign;
        public string firstHeardShift;
        public int timesHeard;
        public float dahRatio;
        public float charGapRatio;
        public float wordGapRatio;
        public float jitter;

        public OperatorFist ToFist()
        {
            return new OperatorFist
            {
                dahRatio = dahRatio,
                charGapRatio = charGapRatio,
                wordGapRatio = wordGapRatio,
                jitter = jitter,
            };
        }

        public static FistRecord From(string callsign, string shiftId, OperatorFist fist)
        {
            return new FistRecord
            {
                callsign = callsign,
                firstHeardShift = shiftId,
                timesHeard = 1,
                dahRatio = fist.dahRatio,
                charGapRatio = fist.charGapRatio,
                wordGapRatio = fist.wordGapRatio,
                jitter = fist.jitter,
            };
        }
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

        /// <summary>当前这一班做到哪了。上报之后会被清空。</summary>
        public ShiftProgress progress;

        /// <summary>听过的电台档案。玩家翻它来对照手法。</summary>
        public List<FistRecord> fistArchive = new List<FistRecord>();

        /// <summary>已完成的班次数。</summary>
        public int CompletedShifts => shiftIndex;

        /// <summary>记下一次上报并推进到下一班。</summary>
        public void RecordAndAdvance(ReportRecord record)
        {
            history.Add(record);
            shiftIndex++;

            // 这一班交出去了，现场就不该再留着——否则下一班开局
            // 会看到上一班的抄收内容。
            progress = default;
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
        /// 记下听到的手法。
        ///
        /// 只记第一次听到的那一份，之后累加次数而不覆盖。
        /// 覆盖会毁掉这层玩法：冒充者的手法一旦盖掉本人的，
        /// 档案就成了帮凶，玩家再也对照不出来。
        /// </summary>
        public void NoteFist(string callsign, string shiftId, OperatorFist fist)
        {
            if (string.IsNullOrEmpty(callsign) || !fist.IsValid)
            {
                return;
            }

            for (var i = 0; i < fistArchive.Count; i++)
            {
                if (fistArchive[i].callsign != callsign)
                {
                    continue;
                }

                var existing = fistArchive[i];
                existing.timesHeard++;
                fistArchive[i] = existing;
                return;
            }

            fistArchive.Add(FistRecord.From(callsign, shiftId, fist));
        }

        /// <summary>查档案里这个呼号的手法。没听过就返回无效值。</summary>
        public OperatorFist ArchivedFist(string callsign)
        {
            foreach (var record in fistArchive)
            {
                if (record.callsign == callsign)
                {
                    return record.ToFist();
                }
            }

            return default;
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

            foreach (var record in fistArchive)
            {
                builder.Append("fist\t")
                    .Append(record.callsign).Append('\t')
                    .Append(record.firstHeardShift).Append('\t')
                    .Append(record.timesHeard).Append('\t')
                    .Append(record.dahRatio.ToString("F4")).Append('\t')
                    .Append(record.charGapRatio.ToString("F4")).Append('\t')
                    .Append(record.wordGapRatio.ToString("F4")).Append('\t')
                    .Append(record.jitter.ToString("F4"))
                    .Append('\n');
            }

            if (progress.active)
            {
                builder.Append("progress\t")
                    .Append(progress.shiftId).Append('\t')
                    .Append(Escape(progress.copied)).Append('\t')
                    .Append(Escape(progress.solved)).Append('\t')
                    .Append(Escape(progress.lookup)).Append('\t')
                    .Append(Escape(progress.callsign)).Append('\t')
                    .Append(Escape(progress.frequency)).Append('\t')
                    .Append(progress.padPage).Append('\t')
                    .Append(progress.level)
                    .Append('\n');
            }

            return builder.ToString();
        }

        /// <summary>
        /// 字段用制表符分隔，所以内容里的制表符和换行必须先躲开，
        /// 否则玩家抄收纸上一个意外的空白字符就能把整份存档解歪。
        /// 空串单独用一个记号，免得和"字段缺失"混在一起。
        /// </summary>
        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "~";
            }

            return value.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n");
        }

        private static string Unescape(string value)
        {
            if (string.IsNullOrEmpty(value) || value == "~")
            {
                return string.Empty;
            }

            return value.Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\\\", "\\");
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

            var state = new CampaignState
            {
                history = new List<ReportRecord>(),
                fistArchive = new List<FistRecord>(),
            };

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

                    case "fist":
                        if (parts.Length < 8)
                        {
                            return null;
                        }

                        if (!int.TryParse(parts[3], out var timesHeard) ||
                            !float.TryParse(parts[4], out var dah) ||
                            !float.TryParse(parts[5], out var charGap) ||
                            !float.TryParse(parts[6], out var wordGap) ||
                            !float.TryParse(parts[7], out var jitter))
                        {
                            return null;
                        }

                        state.fistArchive.Add(new FistRecord
                        {
                            callsign = parts[1],
                            firstHeardShift = parts[2],
                            timesHeard = timesHeard,
                            dahRatio = dah,
                            charGapRatio = charGap,
                            wordGapRatio = wordGap,
                            jitter = jitter,
                        });
                        break;

                    case "progress":
                        if (parts.Length < 9)
                        {
                            return null;
                        }

                        if (!int.TryParse(parts[7], out var padPage) ||
                            !Enum.TryParse<ThreatLevel>(parts[8], out var level))
                        {
                            return null;
                        }

                        state.progress = new ShiftProgress
                        {
                            active = true,
                            shiftId = parts[1],
                            copied = Unescape(parts[2]),
                            solved = Unescape(parts[3]),
                            lookup = Unescape(parts[4]),
                            callsign = Unescape(parts[5]),
                            frequency = Unescape(parts[6]),
                            padPage = padPage,
                            level = level,
                        };
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
