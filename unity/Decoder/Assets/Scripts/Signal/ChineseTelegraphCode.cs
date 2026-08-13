using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Decoder.Signal
{
    /// <summary>
    /// 中文电码：四位十进制数字对应一个汉字，1871 年启用的真实历史系统，
    /// 用于中文在电报线路上的传输。这是方案 A 中文版的核心解码机制。
    ///
    /// 码表由 tools/build_telegraph_table.py 从 Unicode Unihan 数据库的
    /// kMainlandTelegraph 字段生成，存为定长记录（4 位数字 + 1 汉字）。
    /// </summary>
    public sealed class ChineseTelegraphCode
    {
        public const int CodeLength = 4;
        private const string DefaultResourcePath = "telegraph-cn";

        private readonly Dictionary<string, char> _codeToChar;
        private readonly Dictionary<char, string> _charToCode;

        private static ChineseTelegraphCode _shared;

        private ChineseTelegraphCode(Dictionary<string, char> codeToChar,
            Dictionary<char, string> charToCode)
        {
            _codeToChar = codeToChar;
            _charToCode = charToCode;
        }

        public int Count => _codeToChar.Count;

        /// <summary>运行时共享实例，从 Resources 加载。加载失败会抛异常而不是静默退化成空表。</summary>
        public static ChineseTelegraphCode Shared
        {
            get
            {
                if (_shared == null)
                {
                    var asset = Resources.Load<TextAsset>(DefaultResourcePath);
                    if (asset == null)
                    {
                        throw new InvalidOperationException(
                            $"未找到中文电码表 Resources/{DefaultResourcePath}.txt。" +
                            "请运行 tools/build_telegraph_table.py 生成。");
                    }

                    _shared = Parse(asset.text);
                }

                return _shared;
            }
        }

        /// <summary>
        /// 解析定长码表。每行前 4 个字符是数字码，第 5 个字符是汉字。
        /// 单元测试通过这个入口注入自己的小码表，不依赖 Resources。
        /// </summary>
        public static ChineseTelegraphCode Parse(string content)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            var codeToChar = new Dictionary<string, char>();
            var charToCode = new Dictionary<char, string>();

            foreach (var rawLine in content.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r', ' ', '\t');
                if (line.Length < CodeLength + 1)
                {
                    continue;
                }

                var code = line.Substring(0, CodeLength);
                if (!IsFourDigits(code))
                {
                    continue;
                }

                var character = line[CodeLength];
                codeToChar[code] = character;
                // 同一个汉字可能出现在多个码位上，保留第一个，让编码结果稳定可复现。
                if (!charToCode.ContainsKey(character))
                {
                    charToCode[character] = code;
                }
            }

            return new ChineseTelegraphCode(codeToChar, charToCode);
        }

        private static bool IsFourDigits(string s)
        {
            if (s.Length != CodeLength)
            {
                return false;
            }

            for (var i = 0; i < CodeLength; i++)
            {
                if (s[i] < '0' || s[i] > '9')
                {
                    return false;
                }
            }

            return true;
        }

        public bool TryGetCharacter(string code, out char character)
        {
            character = default;
            return code != null && _codeToChar.TryGetValue(code, out character);
        }

        public bool TryGetCode(char character, out string code)
        {
            return _charToCode.TryGetValue(character, out code);
        }

        /// <summary>
        /// 把连续数字流解成汉字。长度不是 4 的倍数时，尾部残缺的部分原样保留，
        /// 因为玩家漏抄一位数字是常见错误，把残缺显示出来比悄悄丢掉更有用。
        /// 查不到的码位输出 '□'，玩家一眼就能看出是哪一格出了问题。
        /// </summary>
        public string DecodeDigits(string digits)
        {
            if (string.IsNullOrEmpty(digits))
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            var i = 0;
            while (i + CodeLength <= digits.Length)
            {
                var code = digits.Substring(i, CodeLength);
                builder.Append(TryGetCharacter(code, out var c) ? c : '□');
                i += CodeLength;
            }

            if (i < digits.Length)
            {
                builder.Append(digits.Substring(i));
            }

            return builder.ToString();
        }

        /// <summary>
        /// 汉字转电码序列，组间用空格分隔。查不到的字跳过，
        /// 因为电报本身就发不出码表以外的字。
        /// </summary>
        public string EncodeText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            foreach (var c in text)
            {
                if (!TryGetCode(c, out var code))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(code);
            }

            return builder.ToString();
        }

        /// <summary>把电码序列转成不带分隔的连续数字流，用于驱动摩尔斯发报。</summary>
        public static string ToDigitStream(string codeGroups)
        {
            if (string.IsNullOrEmpty(codeGroups))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(codeGroups.Length);
            foreach (var c in codeGroups)
            {
                if (c >= '0' && c <= '9')
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }
    }
}
