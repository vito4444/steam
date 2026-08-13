using System;
using System.Text;

namespace Decoder.Signal
{
    /// <summary>
    /// 一次性密码本。
    ///
    /// 真实的数字电台加密方式：发方把明文数字与密钥数字逐位做模 10 加法，
    /// 收方用同一页密钥做模 10 减法还原。密钥只用一次，用完即毁——
    /// 这是密码学上唯一被证明无法破译的体制，前提是密钥真随机、只用一次、
    /// 且只有收发双方持有。
    ///
    /// 之所以用模 10 而不是模 26 或异或：电报线路上传的是十进制数字，
    /// 中文电码本身就是四位十进制，两者天然对齐。
    ///
    /// 游戏里玩家要做的事和真实报务员一样：从电文头部读出页码指示，
    /// 翻到密码本对应那一页，逐位相减。用错页会得到一串通顺不了的数字，
    /// 而游戏不会提示——玩家得自己发现译文不通，回头查页码。
    /// </summary>
    public static class OneTimePad
    {
        /// <summary>每页密钥的长度。够发一条二十个汉字的电文。</summary>
        public const int PageLength = 80;

        /// <summary>电文头部用来指示页码的数字位数。</summary>
        public const int PageIndicatorDigits = 3;

        /// <summary>
        /// 逐位模 10 加。密钥比明文短时循环使用——
        /// 真实的一次性密码本绝不允许这样做，但游戏里的电文长度可控，
        /// 页长足够时不会触发循环，这里只是兜底而不是设计。
        /// </summary>
        public static string Encrypt(string plainDigits, string keyDigits)
        {
            return Combine(plainDigits, keyDigits, add: true);
        }

        /// <summary>逐位模 10 减，Encrypt 的逆运算。</summary>
        public static string Decrypt(string cipherDigits, string keyDigits)
        {
            return Combine(cipherDigits, keyDigits, add: false);
        }

        private static string Combine(string digits, string keyDigits, bool add)
        {
            if (string.IsNullOrEmpty(digits))
            {
                return string.Empty;
            }

            if (string.IsNullOrEmpty(keyDigits))
            {
                throw new ArgumentException("密钥不能为空", nameof(keyDigits));
            }

            var builder = new StringBuilder(digits.Length);
            var keyIndex = 0;

            foreach (var c in digits)
            {
                if (c < '0' || c > '9')
                {
                    // 分组空格之类的分隔符原样保留，且不消耗密钥位。
                    // 玩家抄报时的分组方式不该影响解密结果。
                    builder.Append(c);
                    continue;
                }

                var k = keyDigits[keyIndex % keyDigits.Length];
                if (k < '0' || k > '9')
                {
                    throw new ArgumentException($"密钥含非数字字符 '{k}'", nameof(keyDigits));
                }

                var value = c - '0';
                var key = k - '0';
                var result = add ? (value + key) % 10 : ((value - key) % 10 + 10) % 10;
                builder.Append((char)('0' + result));
                keyIndex++;
            }

            return builder.ToString();
        }

        /// <summary>
        /// 生成一页密钥。
        ///
        /// 用确定性伪随机而不是真随机：玩家读档回到同一个班次时，
        /// 手上那本密码本必须还是原来那本，否则之前抄下的半页笔记全废了。
        /// </summary>
        public static string GeneratePage(int bookSeed, int pageNumber, int length = PageLength)
        {
            if (length <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            // 页号参与混合，保证同一本册子里每页都不同。
            var state = unchecked((uint)(bookSeed * 2654435761L + pageNumber * 40503L));
            if (state == 0u)
            {
                state = 0x9E3779B9u;
            }

            var builder = new StringBuilder(length);
            for (var i = 0; i < length; i++)
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                builder.Append((char)('0' + (int)(state % 10u)));
            }

            return builder.ToString();
        }

        /// <summary>
        /// 从电文头部读出页码。
        ///
        /// 真实数字电台的报头会先播一组指示数字，收方据此翻页。
        /// 返回 -1 表示报头不完整——玩家漏抄了开头，这时候他手上的电文
        /// 根本无从下手，而这本身就是一种值得让玩家体验到的失败。
        /// </summary>
        public static int ReadPageNumber(string cipherWithHeader)
        {
            var digits = ChineseTelegraphCode.ToDigitStream(cipherWithHeader);
            if (digits.Length < PageIndicatorDigits)
            {
                return -1;
            }

            return int.Parse(digits.Substring(0, PageIndicatorDigits));
        }

        /// <summary>去掉报头，留下真正的密文部分。</summary>
        public static string StripHeader(string cipherWithHeader)
        {
            var digits = ChineseTelegraphCode.ToDigitStream(cipherWithHeader);
            return digits.Length <= PageIndicatorDigits
                ? string.Empty
                : digits.Substring(PageIndicatorDigits);
        }

        /// <summary>
        /// 组装一条完整的加密电文：三位页码指示加上密文。
        /// 这是发报端用的，玩家侧只会看到结果。
        /// </summary>
        public static string BuildTransmission(string plainDigits, int bookSeed, int pageNumber)
        {
            if (pageNumber < 0 || pageNumber >= 1000)
            {
                throw new ArgumentOutOfRangeException(nameof(pageNumber),
                    $"页码必须能用 {PageIndicatorDigits} 位数字表示");
            }

            var key = GeneratePage(bookSeed, pageNumber);
            var cipher = Encrypt(ChineseTelegraphCode.ToDigitStream(plainDigits), key);
            return pageNumber.ToString("D" + PageIndicatorDigits) + cipher;
        }

        /// <summary>
        /// 用指定页解一条完整电文。页码给错就会得到一串解不通的数字，
        /// 这正是设计意图——游戏不会告诉玩家页码错了。
        /// </summary>
        public static string Solve(string cipherWithHeader, int bookSeed, int pageNumber)
        {
            var body = StripHeader(cipherWithHeader);
            return body.Length == 0
                ? string.Empty
                : Decrypt(body, GeneratePage(bookSeed, pageNumber));
        }
    }
}
