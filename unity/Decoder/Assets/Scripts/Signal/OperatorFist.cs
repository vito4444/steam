using System;
using System.Collections.Generic;

namespace Decoder.Signal
{
    /// <summary>
    /// 报务员的手法。
    ///
    /// 手键发报的人做不到机器那样的精确。教科书说划是点的三倍长、
    /// 字符之间空三个单位、词之间空七个，但真人发出来总有系统性的偏差：
    /// 有人划拖得长，有人字间赶得紧，有人节奏稳得像机器，有人忽快忽慢。
    /// 这些偏差在同一个人身上是稳定的，换个人就不一样。
    ///
    /// 二战和冷战期间，监听方靠这个辨认发报人，即使对方换了呼号、
    /// 换了频率、换了密码本——手法换不掉。英国人管这套分析叫 TINA。
    ///
    /// 游戏里它是一层玩法：玩家会先熟悉几个常驻电台的手法，
    /// 然后在某一班遇到"呼号对得上但手法不对"的情况。
    /// 系统不会提示，提示了就没意思了。
    /// </summary>
    [Serializable]
    public struct OperatorFist
    {
        /// <summary>划相对点的时长倍数。教科书是 3，真人大多在 2.4 到 3.8 之间。</summary>
        public float dahRatio;

        /// <summary>字符间隔相对点的倍数。教科书是 3。赶时间的人会压到 2 出头。</summary>
        public float charGapRatio;

        /// <summary>词间隔相对点的倍数。教科书是 7。</summary>
        public float wordGapRatio;

        /// <summary>
        /// 抖动幅度，0 到 1。
        /// 0 是机器发报，完全没有随机偏差；手键的老手大约 0.03 到 0.08；
        /// 生手能到 0.2 以上，听起来就是一顿一顿的。
        /// </summary>
        public float jitter;

        /// <summary>教科书标准，机器发报用这个。</summary>
        public static OperatorFist Machine => new OperatorFist
        {
            dahRatio = 3f,
            charGapRatio = 3f,
            wordGapRatio = 7f,
            jitter = 0f,
        };

        /// <summary>这份手法是不是有效的。零值结构体不该被当成真的手法用。</summary>
        public bool IsValid => dahRatio > 0f && charGapRatio > 0f && wordGapRatio > 0f;

        /// <summary>
        /// 两份手法之间的距离，0 表示完全一致。
        ///
        /// 各项都按相对差归一：划长比的 0.3 偏差和词间隔的 0.3 偏差
        /// 在听感上完全不是一回事，前者在三倍长上是一成，后者在七倍长上不到半成。
        /// </summary>
        public float DistanceTo(OperatorFist other)
        {
            var dah = RelativeDifference(dahRatio, other.dahRatio);
            var charGap = RelativeDifference(charGapRatio, other.charGapRatio);
            var wordGap = RelativeDifference(wordGapRatio, other.wordGapRatio);

            // 划长比和字符间隔最容易被人耳察觉，权重给得高一些。
            return dah * 0.4f + charGap * 0.4f + wordGap * 0.2f;
        }

        private static float RelativeDifference(float a, float b)
        {
            var scale = Math.Max(Math.Abs(a), Math.Abs(b));
            return scale <= 0f ? 0f : Math.Abs(a - b) / scale;
        }

        /// <summary>
        /// 一句话描述这个手法的特点，用在游戏里的节奏分析面板上。
        ///
        /// 刻意不给数字：报务员看的是纸带和耳朵，不是仪表。
        /// 玩家要靠"这次听起来划拖得比平时长"来起疑，而不是靠比对小数点后两位。
        /// </summary>
        public string Describe()
        {
            if (!IsValid)
            {
                return "样本不足，看不出手法。";
            }

            if (jitter <= 0.005f)
            {
                return "节奏没有一丝偏差。这是机器发的。";
            }

            var parts = new List<string>();

            if (dahRatio >= 3.35f)
            {
                parts.Add("划拖得长");
            }
            else if (dahRatio <= 2.65f)
            {
                parts.Add("划收得短");
            }

            if (charGapRatio <= 2.5f)
            {
                parts.Add("字挨得紧");
            }
            else if (charGapRatio >= 3.6f)
            {
                parts.Add("字之间停得久");
            }

            if (jitter >= 0.14f)
            {
                parts.Add("手不稳");
            }
            else if (jitter <= 0.045f)
            {
                parts.Add("手很稳");
            }

            return parts.Count == 0
                ? "手法很规矩，说不出特别的地方。"
                : string.Join("，", parts) + "。";
        }
    }

    /// <summary>
    /// 从一段键控时序里量出手法。
    ///
    /// 输入是玩家实际听到的东西，不是发报方声称的参数——
    /// 这一点很重要：冒充者可以伪造呼号，但伪造不了自己的手。
    /// </summary>
    public static class FistAnalyzer
    {
        /// <summary>量出手法所需的最少键控段数。样本太少量出来的东西没有意义。</summary>
        public const int MinimumElements = 12;

        /// <summary>
        /// 从键控时序里反推手法。
        ///
        /// 做法是先把最短的键下时长当作一个"点"，再用它去衡量其他所有片段。
        /// 不依赖发报方声称的 WPM：那个数字在冒充者那里同样可以是假的。
        /// </summary>
        public static OperatorFist Measure(IReadOnlyList<MorseCode.Element> timeline)
        {
            if (timeline == null || timeline.Count < MinimumElements)
            {
                return default;
            }

            // 点长取键下片段里较短的那一批的中位数，而不是最小值：
            // 最小值容易被一次手抖或者一个截断的片段带偏。
            var downs = new List<float>();
            var ups = new List<float>();
            foreach (var element in timeline)
            {
                if (element.Seconds <= 0f)
                {
                    continue;
                }

                (element.KeyDown ? downs : ups).Add(element.Seconds);
            }

            if (downs.Count < 4)
            {
                return default;
            }

            downs.Sort();
            var ditSamples = new List<float>();
            var dahSamples = new List<float>();

            // 点和划的时长差三倍，中间那条线放在两倍处：
            // 比它短的算点，比它长的算划。
            var threshold = downs[0] * 2f;
            foreach (var seconds in downs)
            {
                (seconds < threshold ? ditSamples : dahSamples).Add(seconds);
            }

            if (ditSamples.Count == 0)
            {
                return default;
            }

            var dit = Median(ditSamples);
            if (dit <= 0f)
            {
                return default;
            }

            var fist = new OperatorFist
            {
                dahRatio = dahSamples.Count > 0 ? Median(dahSamples) / dit : 3f,
                jitter = Jitter(ditSamples, dit),
            };

            // 键上片段分三类：符号间隔一个单位、字符间隔三个、词间隔七个。
            // 分界同样取在相邻两档的中间。
            var charSamples = new List<float>();
            var wordSamples = new List<float>();
            foreach (var seconds in ups)
            {
                var units = seconds / dit;
                if (units >= 5f)
                {
                    wordSamples.Add(units);
                }
                else if (units >= 2f)
                {
                    charSamples.Add(units);
                }
            }

            fist.charGapRatio = charSamples.Count > 0 ? Median(charSamples) : 3f;
            fist.wordGapRatio = wordSamples.Count > 0 ? Median(wordSamples) : 7f;

            return fist;
        }

        private static float Median(List<float> values)
        {
            if (values.Count == 0)
            {
                return 0f;
            }

            var sorted = new List<float>(values);
            sorted.Sort();
            var mid = sorted.Count / 2;
            return sorted.Count % 2 == 1
                ? sorted[mid]
                : (sorted[mid - 1] + sorted[mid]) * 0.5f;
        }

        /// <summary>
        /// 抖动幅度：点长的平均相对偏差。
        /// 用平均绝对偏差而不是标准差，是因为一次大的失手不该
        /// 把整段的评价拉到"手很抖"去。
        /// </summary>
        private static float Jitter(List<float> ditSamples, float dit)
        {
            if (ditSamples.Count == 0 || dit <= 0f)
            {
                return 0f;
            }

            var sum = 0f;
            foreach (var sample in ditSamples)
            {
                sum += Math.Abs(sample - dit);
            }

            return sum / ditSamples.Count / dit;
        }
    }
}
