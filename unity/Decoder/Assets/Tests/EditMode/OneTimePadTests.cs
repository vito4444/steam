using System;
using Decoder.Signal;
using NUnit.Framework;

namespace Decoder.Tests
{
    /// <summary>
    /// 一次性密码本的测试。
    ///
    /// 这一层是方案中段的核心机制：玩家要从报头读页码、翻到对应页、逐位相减。
    /// 加解密如果不是严格互逆，玩家照着规则一步步算出来的结果会对不上，
    /// 而他没有任何办法判断是自己算错了还是游戏错了——这种挫败是不可接受的。
    /// </summary>
    public sealed class OneTimePadTests
    {
        [TestCase("0000", "0000", "0000")]
        [TestCase("1234", "0000", "1234")]
        [TestCase("1234", "1111", "2345")]
        [TestCase("9999", "1111", "0000")]
        [TestCase("5555", "5555", "0000")]
        [TestCase("0554", "7391", "7845")]
        public void Encrypt_IsDigitwiseModuloTenAddition(string plain, string key, string expected)
        {
            Assert.AreEqual(expected, OneTimePad.Encrypt(plain, key));
        }

        [TestCase("0000", "0000", "0000")]
        [TestCase("2345", "1111", "1234")]
        [TestCase("0000", "1111", "9999")]
        [TestCase("7845", "7391", "0554")]
        public void Decrypt_IsDigitwiseModuloTenSubtraction(string cipher, string key, string expected)
        {
            Assert.AreEqual(expected, OneTimePad.Decrypt(cipher, key));
        }

        [TestCase("0554007921483316")]
        [TestCase("0001")]
        [TestCase("999999999999")]
        public void EncryptThenDecrypt_RoundTripsExactly(string plain)
        {
            var key = OneTimePad.GeneratePage(20260813, 42);
            var cipher = OneTimePad.Encrypt(plain, key);

            Assert.AreNotEqual(plain, cipher, "密文不该等于明文");
            Assert.AreEqual(plain, OneTimePad.Decrypt(cipher, key));
        }

        [Test]
        public void Decrypt_WithWrongPageProducesGarbageNotOriginal()
        {
            // 用错页必须解出别的东西。如果错页也能解对，页码这层玩法就是摆设。
            const string plain = "05540079";
            var right = OneTimePad.GeneratePage(20260813, 42);
            var wrong = OneTimePad.GeneratePage(20260813, 43);

            var cipher = OneTimePad.Encrypt(plain, right);
            Assert.AreNotEqual(plain, OneTimePad.Decrypt(cipher, wrong));
        }

        [Test]
        public void Combine_PreservesSeparatorsWithoutConsumingKey()
        {
            // 玩家抄报时会自然分组。分组方式不该影响解密结果，
            // 否则同一份电文写成 "0554 0079" 和 "05540079" 会解出两个答案。
            var key = OneTimePad.GeneratePage(7, 1);
            var grouped = OneTimePad.Decrypt("1234 5678", key);
            var flat = OneTimePad.Decrypt("12345678", key);

            Assert.AreEqual(flat, grouped.Replace(" ", string.Empty));
            Assert.IsTrue(grouped.Contains(" "), "分隔符应当原样保留");
        }

        [Test]
        public void Encrypt_RejectsEmptyKey()
        {
            Assert.Throws<ArgumentException>(() => OneTimePad.Encrypt("1234", string.Empty));
        }

        [Test]
        public void Encrypt_RejectsNonNumericKey()
        {
            Assert.Throws<ArgumentException>(() => OneTimePad.Encrypt("1234", "12A4"));
        }

        [Test]
        public void Encrypt_EmptyInputReturnsEmpty()
        {
            Assert.AreEqual(string.Empty, OneTimePad.Encrypt(string.Empty, "1111"));
        }

        // ---- 密码本 ----

        [Test]
        public void GeneratePage_IsDeterministic()
        {
            // 读档回到同一个班次时，手上那本密码本必须还是原来那本，
            // 否则之前抄下的半页笔记全废了。
            Assert.AreEqual(OneTimePad.GeneratePage(123, 7), OneTimePad.GeneratePage(123, 7));
        }

        [Test]
        public void GeneratePage_DiffersBetweenPages()
        {
            Assert.AreNotEqual(OneTimePad.GeneratePage(123, 7), OneTimePad.GeneratePage(123, 8));
        }

        [Test]
        public void GeneratePage_DiffersBetweenBooks()
        {
            Assert.AreNotEqual(OneTimePad.GeneratePage(123, 7), OneTimePad.GeneratePage(124, 7));
        }

        [Test]
        public void GeneratePage_ContainsOnlyDigitsAtRequestedLength()
        {
            var page = OneTimePad.GeneratePage(555, 12);

            Assert.AreEqual(OneTimePad.PageLength, page.Length);
            foreach (var c in page)
            {
                Assert.IsTrue(c >= '0' && c <= '9', $"密码本出现非数字字符 '{c}'");
            }
        }

        [Test]
        public void GeneratePage_IsReasonablyUniform()
        {
            // 密钥若明显偏向某几个数字，密文就会泄露明文的统计特征。
            // 这里不要求严格均匀，只要求没有哪个数字完全缺席或者占掉三成。
            var page = OneTimePad.GeneratePage(999, 3, 2000);
            var counts = new int[10];
            foreach (var c in page)
            {
                counts[c - '0']++;
            }

            foreach (var count in counts)
            {
                Assert.Greater(count, 2000 * 0.05, "某个数字出现得太少");
                Assert.Less(count, 2000 * 0.20, "某个数字出现得太多");
            }
        }

        // ---- 报头与整条电文 ----

        [Test]
        public void ReadPageNumber_ExtractsLeadingDigits()
        {
            Assert.AreEqual(42, OneTimePad.ReadPageNumber("042123456"));
            Assert.AreEqual(7, OneTimePad.ReadPageNumber("007 1234"));
        }

        [Test]
        public void ReadPageNumber_ReturnsMinusOneWhenHeaderIncomplete()
        {
            // 玩家漏抄了开头，手上的电文无从下手。这是一种应当让他体验到的失败，
            // 但不能表现成崩溃或者一个看起来合法的错误页码。
            Assert.AreEqual(-1, OneTimePad.ReadPageNumber("04"));
            Assert.AreEqual(-1, OneTimePad.ReadPageNumber(string.Empty));
        }

        [Test]
        public void StripHeader_RemovesExactlyThePageIndicator()
        {
            Assert.AreEqual("123456", OneTimePad.StripHeader("042123456"));
            Assert.AreEqual(string.Empty, OneTimePad.StripHeader("042"));
        }

        [Test]
        public void BuildTransmission_ThenSolve_RecoversPlainText()
        {
            const string plain = "05540079";
            const int book = 19851104;
            const int page = 17;

            var wire = OneTimePad.BuildTransmission(plain, book, page);

            Assert.AreEqual(page, OneTimePad.ReadPageNumber(wire));
            Assert.AreEqual(plain, OneTimePad.Solve(wire, book, page));
        }

        [Test]
        public void BuildTransmission_HeaderIsPaddedToFixedWidth()
        {
            // 页码指示必须定长，否则收方不知道从第几位开始才是密文。
            var wire = OneTimePad.BuildTransmission("1234", 1, 7);
            Assert.IsTrue(wire.StartsWith("007", StringComparison.Ordinal), $"报头不对: {wire}");
        }

        [Test]
        public void BuildTransmission_RejectsPageBeyondIndicatorRange()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => OneTimePad.BuildTransmission("1234", 1, 1000));
        }

        [Test]
        public void Solve_WithWrongPageDoesNotRecoverPlainText()
        {
            const string plain = "05540079";
            var wire = OneTimePad.BuildTransmission(plain, 19851104, 17);

            Assert.AreNotEqual(plain, OneTimePad.Solve(wire, 19851104, 18));
        }

        [Test]
        public void FullChain_CipherDecodesToChineseThroughTelegraphTable()
        {
            // 端到端：汉字转电码、电码加密、发出去、解密、再查表还原成汉字。
            // 这是玩家在第二班之后每一条主线电文要走的完整链路。
            var table = ChineseTelegraphCode.Shared;
            const string text = "北风已起";
            const int book = 19851104;
            const int page = 23;

            var plainDigits = ChineseTelegraphCode.ToDigitStream(table.EncodeText(text));
            var wire = OneTimePad.BuildTransmission(plainDigits, book, page);
            var solved = OneTimePad.Solve(wire, book, OneTimePad.ReadPageNumber(wire));

            Assert.AreEqual(plainDigits, solved);
            Assert.AreEqual(text, table.DecodeDigits(solved));
        }
    }
}
