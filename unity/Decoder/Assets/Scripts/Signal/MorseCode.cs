using System;
using System.Collections.Generic;
using System.Text;

namespace Decoder.Signal
{
    /// <summary>
    /// 摩尔斯电码编解码，以及供音频合成使用的键控时序生成。
    ///
    /// 时长遵循 ITU-R M.1677 的标准比例：点 1 单位，划 3 单位，
    /// 符号内间隔 1 单位，字符间隔 3 单位，词间隔 7 单位。
    /// 单位时长按 PARIS 法从每分钟字数换算：一个 PARIS 恰好 50 单位，
    /// 所以 unit = 60 / (50 * wpm) = 1.2 / wpm 秒。
    /// </summary>
    public static class MorseCode
    {
        public const char Dit = '.';
        public const char Dah = '-';

        /// <summary>词间隔在文本表示里用斜杠标记，与国际惯例一致。</summary>
        public const string WordSeparator = "/";

        private static readonly Dictionary<char, string> Forward = new Dictionary<char, string>
        {
            ['A'] = ".-",    ['B'] = "-...",  ['C'] = "-.-.",  ['D'] = "-..",
            ['E'] = ".",     ['F'] = "..-.",  ['G'] = "--.",   ['H'] = "....",
            ['I'] = "..",    ['J'] = ".---",  ['K'] = "-.-",   ['L'] = ".-..",
            ['M'] = "--",    ['N'] = "-.",    ['O'] = "---",   ['P'] = ".--.",
            ['Q'] = "--.-",  ['R'] = ".-.",   ['S'] = "...",   ['T'] = "-",
            ['U'] = "..-",   ['V'] = "...-",  ['W'] = ".--",   ['X'] = "-..-",
            ['Y'] = "-.--",  ['Z'] = "--..",
            ['0'] = "-----", ['1'] = ".----", ['2'] = "..---", ['3'] = "...--",
            ['4'] = "....-", ['5'] = ".....", ['6'] = "-....", ['7'] = "--...",
            ['8'] = "---..", ['9'] = "----.",
            ['.'] = ".-.-.-", [','] = "--..--", ['?'] = "..--..", ['\''] = ".----.",
            ['!'] = "-.-.--", ['/'] = "-..-.",  ['('] = "-.--.",  [')'] = "-.--.-",
            ['&'] = ".-...",  [':'] = "---...", [';'] = "-.-.-.", ['='] = "-...-",
            ['+'] = ".-.-.",  ['-'] = "-....-", ['_'] = "..--.-", ['"'] = ".-..-.",
            ['@'] = ".--.-.",
        };

        private static readonly Dictionary<string, char> Reverse = BuildReverse();

        private static Dictionary<string, char> BuildReverse()
        {
            var map = new Dictionary<string, char>(Forward.Count);
            foreach (var pair in Forward)
            {
                // 码表若出现重复码型会让解码产生歧义，这里直接暴露而不是静默覆盖。
                if (map.ContainsKey(pair.Value))
                {
                    throw new InvalidOperationException(
                        $"摩尔斯码表存在重复码型 \"{pair.Value}\"：" +
                        $"'{map[pair.Value]}' 与 '{pair.Key}' 冲突");
                }

                map[pair.Value] = pair.Key;
            }

            return map;
        }

        public static bool IsSupported(char c)
        {
            return Forward.ContainsKey(char.ToUpperInvariant(c));
        }

        /// <summary>
        /// 文本转摩尔斯。字符之间用空格分隔，词之间用 " / " 分隔。
        /// 不支持的字符会被跳过，因为电报本身就发不出这些字符。
        /// </summary>
        public static string Encode(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            var pendingSeparator = false;

            foreach (var raw in text)
            {
                if (raw == ' ')
                {
                    if (builder.Length > 0)
                    {
                        builder.Append(' ').Append(WordSeparator);
                        pendingSeparator = true;
                    }

                    continue;
                }

                if (!Forward.TryGetValue(char.ToUpperInvariant(raw), out var pattern))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(pattern);
                pendingSeparator = false;
            }

            // 文本以空格结尾时会留下一个孤立的词分隔符，去掉它。
            if (pendingSeparator)
            {
                builder.Length -= WordSeparator.Length + 1;
            }

            return builder.ToString();
        }

        /// <summary>
        /// 摩尔斯转文本。无法识别的码型会变成 '?'，
        /// 这样玩家听错一个符号时得到的是一个明显的错字，而不是整句消失。
        /// </summary>
        public static string Decode(string morse)
        {
            if (string.IsNullOrWhiteSpace(morse))
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            var tokens = morse.Split(new[] { ' ', '\t', '\n', '\r' },
                StringSplitOptions.RemoveEmptyEntries);

            foreach (var token in tokens)
            {
                if (token == WordSeparator)
                {
                    builder.Append(' ');
                    continue;
                }

                builder.Append(Reverse.TryGetValue(token, out var c) ? c : '?');
            }

            return builder.ToString();
        }

        /// <summary>一个键控片段：载波是否打开，以及持续多少秒。</summary>
        public readonly struct Element
        {
            public Element(bool keyDown, float seconds)
            {
                KeyDown = keyDown;
                Seconds = seconds;
            }

            public bool KeyDown { get; }
            public float Seconds { get; }
        }

        public static float UnitSeconds(float wordsPerMinute)
        {
            if (wordsPerMinute <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(wordsPerMinute),
                    "每分钟字数必须为正");
            }

            return 1.2f / wordsPerMinute;
        }

        /// <summary>
        /// 把摩尔斯码串展开成键控时序，供音频合成逐段生成波形。
        /// 输入是 Encode 的输出格式（字符间空格、词间斜杠）。
        /// </summary>
        public static List<Element> BuildTimeline(string morse, float wordsPerMinute)
        {
            return BuildTimeline(morse, wordsPerMinute, OperatorFist.Machine, 0);
        }

        /// <summary>
        /// 按指定报务员的手法展开键控时序。
        ///
        /// 手键发报的人做不到机器那样的精确，划长和间隔都会有系统性偏差，
        /// 而这些偏差在同一个人身上是稳定的。抖动用种子驱动，
        /// 保证同一条电文每次听到的都是同一段节奏——玩家反复听来分辨手法时，
        /// 听到的东西不能每次都变。
        /// </summary>
        public static List<Element> BuildTimeline(
            string morse, float wordsPerMinute, OperatorFist fist, int seed)
        {
            if (!fist.IsValid)
            {
                fist = OperatorFist.Machine;
            }

            var unit = UnitSeconds(wordsPerMinute);
            var noise = new NoiseSource(seed);

            float Shape(float units)
            {
                var seconds = unit * units;
                if (fist.jitter <= 0f)
                {
                    return seconds;
                }

                // 抖动是乘性的：发得快的人绝对偏差自然更小。
                // 下限卡在两成，免得抽到极端值时片段短到听不见。
                var factor = 1f + noise.NextWhite() * fist.jitter;
                return seconds * Math.Max(0.2f, factor);
            }

            var timeline = new List<Element>();
            if (string.IsNullOrWhiteSpace(morse))
            {
                return timeline;
            }

            var tokens = morse.Split(new[] { ' ', '\t', '\n', '\r' },
                StringSplitOptions.RemoveEmptyEntries);

            for (var t = 0; t < tokens.Length; t++)
            {
                var token = tokens[t];

                if (token == WordSeparator)
                {
                    ReplaceOrAppendGap(timeline, Shape(fist.wordGapRatio), unit);
                    continue;
                }

                for (var i = 0; i < token.Length; i++)
                {
                    timeline.Add(new Element(true, Shape(token[i] == Dah ? fist.dahRatio : 1f)));

                    if (i < token.Length - 1)
                    {
                        timeline.Add(new Element(false, Shape(1f)));
                    }
                }

                if (t < tokens.Length - 1 && tokens[t + 1] != WordSeparator)
                {
                    timeline.Add(new Element(false, Shape(fist.charGapRatio)));
                }
            }

            return timeline;
        }

        private static void ReplaceOrAppendGap(List<Element> timeline, float totalGap, float unit)
        {
            if (timeline.Count > 0 && !timeline[timeline.Count - 1].KeyDown)
            {
                var existing = timeline[timeline.Count - 1].Seconds;
                timeline[timeline.Count - 1] = new Element(false, totalGap - existing > 0f
                    ? totalGap
                    : existing);
            }
            else
            {
                timeline.Add(new Element(false, totalGap));
            }

            // 词分隔符后面紧跟下一个字符，不需要再补字符间隔。
            _ = unit;
        }

        public static float TotalSeconds(IReadOnlyList<Element> timeline)
        {
            var total = 0f;
            for (var i = 0; i < timeline.Count; i++)
            {
                total += timeline[i].Seconds;
            }

            return total;
        }
    }
}
