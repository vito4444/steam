using System;
using Decoder.Signal;

namespace Decoder.Gameplay
{
    /// <summary>玩家填写并送出的一份电报上报单。</summary>
    public sealed class ReportSubmission
    {
        /// <summary>玩家抄下的呼号。</summary>
        public string Callsign = string.Empty;

        /// <summary>玩家记录的频率，单位千赫。</summary>
        public float FrequencyKHz;

        /// <summary>玩家听写下来的原始字符流。中文电码信号这里是数字串。</summary>
        public string CopiedText = string.Empty;

        /// <summary>玩家判断的威胁等级。</summary>
        public ThreatLevel Level = ThreatLevel.Routine;
    }

    /// <summary>上报造成的后果类别。这套设计里没有分数，只有后果。</summary>
    public enum ReportOutcome
    {
        /// <summary>抄收准确且等级判断正确。</summary>
        Clean,

        /// <summary>内容抄对了，但等级报高了。上级会觉得你大惊小怪。</summary>
        Overreacted,

        /// <summary>内容抄对了，但等级报低了。该被知道的人没被通知。</summary>
        Underreported,

        /// <summary>抄收有错漏，情报本身失真。</summary>
        Garbled,

        /// <summary>抄错太多，这份上报没有任何价值。</summary>
        Useless,
    }

    public sealed class ReportGrade
    {
        /// <summary>抄收准确度，0 到 1。</summary>
        public float Accuracy;

        /// <summary>频率记录是否落在容差内。</summary>
        public bool FrequencyCorrect;

        /// <summary>呼号是否抄对。</summary>
        public bool CallsignCorrect;

        /// <summary>等级判断相对正确值的偏差。正数表示报高了。</summary>
        public int LevelDelta;

        public ReportOutcome Outcome;

        /// <summary>玩家抄收结果经码表还原后的可读文本，用于复盘时对照。</summary>
        public string DecodedText = string.Empty;

        /// <summary>正确答案的可读文本。</summary>
        public string ExpectedText = string.Empty;
    }

    /// <summary>
    /// 上报判定。这是玩法的裁判，决定玩家这一班干得怎么样。
    ///
    /// 判定原则是宽进严出：抄收允许有错，因为真实报务员也会漏；
    /// 但等级判断不允许含糊，因为那是这份工作真正的责任所在。
    /// </summary>
    public static class ReportGrader
    {
        /// <summary>频率记录的容差。写到 0.5 kHz 以内就算记对了。</summary>
        public const float FrequencyToleranceKHz = 0.5f;

        /// <summary>低于这个准确度，上报被视为完全没有价值。</summary>
        public const float UselessThreshold = 0.5f;

        /// <summary>达到这个准确度才算抄清楚了。</summary>
        public const float CleanThreshold = 0.95f;

        public static ReportGrade Grade(TransmissionEntry expected, ReportSubmission submission,
            ChineseTelegraphCode telegraph)
        {
            if (expected == null)
            {
                throw new ArgumentNullException(nameof(expected));
            }

            if (submission == null)
            {
                throw new ArgumentNullException(nameof(submission));
            }

            var expectedAir = expected.ResolveAirText(telegraph);
            var copied = Normalize(submission.CopiedText);
            var target = Normalize(expectedAir);

            // 传真没有可抄的字符，抄收纸不参与判定。
            //
            // 这不等于"看不看图都一样"：图上有什么决定了这条信号该报什么等级，
            // 没看图的人不会知道那张平面图上有人圈了一处，只会当例行流量报上去。
            // 判读的责任被压到等级这一项上，而等级从来都是这份工作真正的考核项。
            var isFacsimile = expected.kind == SignalKind.Facsimile;

            var grade = new ReportGrade
            {
                Accuracy = isFacsimile ? 1f : SimilarityRatio(copied, target),
                FrequencyCorrect = Math.Abs(submission.FrequencyKHz - expected.frequencyKHz)
                                   <= FrequencyToleranceKHz,
                CallsignCorrect = string.Equals(Normalize(submission.Callsign),
                    Normalize(expected.callsign), StringComparison.Ordinal),
                LevelDelta = (int)submission.Level - (int)expected.correctLevel,
                ExpectedText = expected.plainText,
            };

            if (isFacsimile)
            {
                grade.DecodedText = "（图像）";
            }
            else
            {
                grade.DecodedText = expected.kind == SignalKind.ChineseTelegraph && telegraph != null
                    ? telegraph.DecodeDigits(copied)
                    : copied;
            }

            grade.Outcome = ResolveOutcome(grade);
            return grade;
        }

        private static ReportOutcome ResolveOutcome(ReportGrade grade)
        {
            if (grade.Accuracy < UselessThreshold)
            {
                return ReportOutcome.Useless;
            }

            if (grade.Accuracy < CleanThreshold)
            {
                return ReportOutcome.Garbled;
            }

            if (grade.LevelDelta > 0)
            {
                return ReportOutcome.Overreacted;
            }

            if (grade.LevelDelta < 0)
            {
                return ReportOutcome.Underreported;
            }

            return ReportOutcome.Clean;
        }

        /// <summary>
        /// 去掉空格与大小写差异。玩家抄报时会自然地分组，
        /// 因为分组方式不同就判错太苛刻了。
        /// </summary>
        public static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var buffer = new char[text.Length];
            var length = 0;
            foreach (var c in text)
            {
                if (char.IsWhiteSpace(c) || c == '-' || c == '/')
                {
                    continue;
                }

                buffer[length++] = char.ToUpperInvariant(c);
            }

            return new string(buffer, 0, length);
        }

        /// <summary>
        /// 相似度：1 减去归一化编辑距离。
        ///
        /// 用编辑距离而不是逐位比对，是因为漏抄一个字符会让后面全部错位。
        /// 逐位比对下"漏一位"和"全抄错"得分一样，那样玩家就学不到
        /// "漏一位比乱抄好得多"这件事，而这恰恰是抄报最重要的直觉。
        /// </summary>
        public static float SimilarityRatio(string a, string b)
        {
            if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b))
            {
                return 1f;
            }

            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            {
                return 0f;
            }

            var distance = LevenshteinDistance(a, b);
            var longest = Math.Max(a.Length, b.Length);
            return 1f - distance / (float)longest;
        }

        public static int LevenshteinDistance(string a, string b)
        {
            if (string.IsNullOrEmpty(a))
            {
                return b?.Length ?? 0;
            }

            if (string.IsNullOrEmpty(b))
            {
                return a.Length;
            }

            // 只保留两行，长电文下比完整矩阵省一个数量级的内存。
            var previous = new int[b.Length + 1];
            var current = new int[b.Length + 1];

            for (var j = 0; j <= b.Length; j++)
            {
                previous[j] = j;
            }

            for (var i = 1; i <= a.Length; i++)
            {
                current[0] = i;
                for (var j = 1; j <= b.Length; j++)
                {
                    var substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                    var deletion = previous[j] + 1;
                    var insertion = current[j - 1] + 1;
                    current[j] = Math.Min(substitution, Math.Min(deletion, insertion));
                }

                var swap = previous;
                previous = current;
                current = swap;
            }

            return previous[b.Length];
        }
    }
}
