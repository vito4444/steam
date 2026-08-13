using Decoder.Signal;
using NUnit.Framework;

namespace Decoder.Tests
{
    /// <summary>
    /// 中文电码的测试。分两部分：用注入的小码表测解析与查表逻辑，
    /// 用真实码表测数据本身没有在生成或导入环节被破坏。
    /// </summary>
    public sealed class ChineseTelegraphCodeTests
    {
        private const string SampleTable =
            "0001一\n" +
            "0022中\n" +
            "0079京\n" +
            "0554北\n" +
            "2429文\n" +
            "4316码\n" +
            "7193电\n";

        private static ChineseTelegraphCode Sample()
        {
            return ChineseTelegraphCode.Parse(SampleTable);
        }

        [Test]
        public void Parse_ReadsEveryRecord()
        {
            Assert.AreEqual(7, Sample().Count);
        }

        [TestCase("0001", '一')]
        [TestCase("0022", '中')]
        [TestCase("2429", '文')]
        [TestCase("7193", '电')]
        [TestCase("4316", '码')]
        public void TryGetCharacter_ReturnsMappedCharacter(string code, char expected)
        {
            Assert.IsTrue(Sample().TryGetCharacter(code, out var actual));
            Assert.AreEqual(expected, actual);
        }

        [Test]
        public void TryGetCharacter_UnknownCodeFails()
        {
            Assert.IsFalse(Sample().TryGetCharacter("9999", out _));
            Assert.IsFalse(Sample().TryGetCharacter(null, out _));
        }

        [Test]
        public void TryGetCode_IsInverseOfTryGetCharacter()
        {
            var table = Sample();
            Assert.IsTrue(table.TryGetCode('中', out var code));
            Assert.AreEqual("0022", code);
            Assert.IsTrue(table.TryGetCharacter(code, out var character));
            Assert.AreEqual('中', character);
        }

        [Test]
        public void DecodeDigits_SplitsStreamIntoFourDigitGroups()
        {
            Assert.AreEqual("中文电码", Sample().DecodeDigits("0022242971934316"));
        }

        [Test]
        public void DecodeDigits_UnknownGroupBecomesBox()
        {
            // 玩家抄错一组数字时，应该看到具体是哪一格错了。
            Assert.AreEqual("中□文", Sample().DecodeDigits("002299992429"));
        }

        [Test]
        public void DecodeDigits_KeepsTrailingPartialGroup()
        {
            // 漏抄一位是常见错误，残缺部分要原样显示出来而不是悄悄丢掉。
            Assert.AreEqual("中242", Sample().DecodeDigits("0022242"));
        }

        [Test]
        public void EncodeText_ProducesSpaceSeparatedGroups()
        {
            Assert.AreEqual("0022 2429", Sample().EncodeText("中文"));
        }

        [Test]
        public void EncodeText_SkipsCharactersOutsideTable()
        {
            Assert.AreEqual("0022 2429", Sample().EncodeText("中X文Y"));
        }

        [Test]
        public void EncodeThenDecode_RoundTripsExactly()
        {
            var table = Sample();
            const string text = "北京电码";
            var digits = ChineseTelegraphCode.ToDigitStream(table.EncodeText(text));

            Assert.AreEqual(text, table.DecodeDigits(digits));
        }

        [Test]
        public void ToDigitStream_StripsSeparators()
        {
            Assert.AreEqual("00222429", ChineseTelegraphCode.ToDigitStream("0022 2429"));
            Assert.AreEqual("00222429", ChineseTelegraphCode.ToDigitStream("0022-2429"));
        }

        [Test]
        public void Parse_IgnoresMalformedLines()
        {
            var table = ChineseTelegraphCode.Parse(
                "0001一\n" +
                "\n" +
                "abc\n" +
                "12\n" +
                "12X4中\n" +
                "0022中\n");

            Assert.AreEqual(2, table.Count);
        }

        [Test]
        public void Parse_HandlesCarriageReturns()
        {
            var table = ChineseTelegraphCode.Parse("0001一\r\n0022中\r\n");
            Assert.AreEqual(2, table.Count);
            Assert.IsTrue(table.TryGetCharacter("0022", out var c));
            Assert.AreEqual('中', c);
        }

        // ---- 真实码表：确认生成与导入环节没有破坏数据 ----

        [Test]
        public void SharedTable_LoadsFullDataset()
        {
            // 生成时实测 7078 条。数量掉下来意味着生成脚本或资源导入出了问题。
            Assert.AreEqual(7078, ChineseTelegraphCode.Shared.Count);
        }

        [TestCase("0001", '一')]
        [TestCase("0022", '中')]
        [TestCase("0079", '京')]
        [TestCase("0554", '北')]
        [TestCase("2429", '文')]
        [TestCase("4316", '码')]
        [TestCase("7193", '电')]
        public void SharedTable_MatchesKnownHistoricalCodes(string code, char expected)
        {
            // 这些码值可对照 Unicode Unihan 的 kMainlandTelegraph 字段核实。
            Assert.IsTrue(ChineseTelegraphCode.Shared.TryGetCharacter(code, out var actual));
            Assert.AreEqual(expected, actual);
        }

        [Test]
        public void SharedTable_RoundTripsRealSentence()
        {
            var table = ChineseTelegraphCode.Shared;
            const string text = "北京电码文一中";
            var digits = ChineseTelegraphCode.ToDigitStream(table.EncodeText(text));

            Assert.AreEqual(text.Length * ChineseTelegraphCode.CodeLength, digits.Length);
            Assert.AreEqual(text, table.DecodeDigits(digits));
        }
    }
}
