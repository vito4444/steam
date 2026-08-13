using System;
using System.Collections.Generic;
using Decoder.Gameplay;
using Decoder.Signal;
using NUnit.Framework;

namespace Decoder.Tests
{
    /// <summary>
    /// 班次内容的测试。这一层守的不是代码逻辑而是内容本身：
    /// 一条玩家永远解不开的电文、两个撞在一起的频率、一个超出频段的电台，
    /// 都不会让代码报错，但会让这一班没法玩。
    /// </summary>
    public sealed class ShiftContentTests
    {
        private static IEnumerable<ShiftDefinition> AllShifts()
        {
            return ShiftLibrary.All();
        }

        [Test]
        public void EveryChineseCharacterIsInTheCodeTable()
        {
            var table = ChineseTelegraphCode.Shared;

            foreach (var shift in AllShifts())
            {
                foreach (var entry in shift.transmissions)
                {
                    // 明码摩尔斯之外的两种体制，明文都是中文，都要能查到电码。
                    // 加密电文尤其如此：它是先转电码再加密的，查不到码就根本发不出去。
                    if (entry.kind == SignalKind.PlainMorse)
                    {
                        continue;
                    }

                    foreach (var c in entry.plainText)
                    {
                        Assert.IsTrue(table.TryGetCode(c, out _),
                            $"{shift.shiftId} 的 {entry.callsign} 里的「{c}」不在电码表中，" +
                            "玩家查不到它，这条电文永远解不开");
                    }
                }
            }
        }

        [Test]
        public void EveryPlainMorseCharacterIsTransmittable()
        {
            foreach (var shift in AllShifts())
            {
                foreach (var entry in shift.transmissions)
                {
                    if (entry.kind != SignalKind.PlainMorse)
                    {
                        continue;
                    }

                    foreach (var c in entry.plainText)
                    {
                        if (c == ' ')
                        {
                            continue;
                        }

                        Assert.IsTrue(MorseCode.IsSupported(c),
                            $"{shift.shiftId} 的 {entry.callsign} 里的 '{c}' 发不出摩尔斯码");
                    }
                }
            }
        }

        [Test]
        public void EveryStationSitsInsideTheTunableBand()
        {
            foreach (var shift in AllShifts())
            {
                foreach (var entry in shift.transmissions)
                {
                    Assert.GreaterOrEqual(entry.frequencyKHz, shift.bandLowKHz,
                        $"{entry.callsign} 在频段下限之外，玩家转不到那里");
                    Assert.LessOrEqual(entry.frequencyKHz, shift.bandHighKHz,
                        $"{entry.callsign} 在频段上限之外，玩家转不到那里");
                }
            }
        }

        [Test]
        public void StationsAreFarEnoughApartToBeSeparable()
        {
            // 两个台靠得比一个带宽还近，玩家就没法把它们分开听。
            foreach (var shift in AllShifts())
            {
                for (var i = 0; i < shift.transmissions.Count; i++)
                {
                    for (var j = i + 1; j < shift.transmissions.Count; j++)
                    {
                        var gap = Math.Abs(shift.transmissions[i].frequencyKHz
                                           - shift.transmissions[j].frequencyKHz);
                        Assert.Greater(gap, SignalSynthesizer.BandwidthKHz,
                            $"{shift.transmissions[i].callsign} 与 " +
                            $"{shift.transmissions[j].callsign} 相距 {gap} kHz，" +
                            "小于接收带宽，两台会混在一起");
                    }
                }
            }
        }

        [Test]
        public void EveryShiftHasExactlyOnePrimaryTransmission()
        {
            foreach (var shift in AllShifts())
            {
                var count = 0;
                foreach (var entry in shift.transmissions)
                {
                    if (entry.isPrimary)
                    {
                        count++;
                    }
                }

                Assert.AreEqual(1, count,
                    $"{shift.shiftId} 有 {count} 条主线信号，应当恰好一条");
            }
        }

        [Test]
        public void PrimaryTransmissionIsNotRoutine()
        {
            // 主线如果也是例行，玩家就没有理由去分辨它和干扰信号的区别。
            foreach (var shift in AllShifts())
            {
                Assert.AreNotEqual(ThreatLevel.Routine, shift.Primary.correctLevel,
                    $"{shift.shiftId} 的主线信号等级是例行，这一班就没有判断可做了");
            }
        }

        [Test]
        public void EveryTransmissionHasAudibleStrength()
        {
            foreach (var shift in AllShifts())
            {
                foreach (var entry in shift.transmissions)
                {
                    Assert.Greater(entry.strength, 0.25f,
                        $"{entry.callsign} 的强度低于底噪，玩家听不见");
                    Assert.LessOrEqual(entry.strength, 1f,
                        $"{entry.callsign} 的强度超过 1，会导致削波");
                }
            }
        }

        [Test]
        public void SpeedIsWithinCopyableRange()
        {
            foreach (var shift in AllShifts())
            {
                foreach (var entry in shift.transmissions)
                {
                    Assert.GreaterOrEqual(entry.wordsPerMinute, 5f,
                        $"{entry.callsign} 太慢，一遍要发很久");
                    Assert.LessOrEqual(entry.wordsPerMinute, 25f,
                        $"{entry.callsign} 超过 25 WPM，新手抄不下来");
                }
            }
        }

        [Test]
        public void EveryEncryptedTransmissionSolvesBackToItsPlainText()
        {
            // 加密电文必须能用它自己声明的页码解回原文。解不回来的话，
            // 玩家照着规则一步步算出来的结果会对不上，而他没法判断
            // 是自己算错了还是游戏错了——这种挫败不可接受。
            var table = ChineseTelegraphCode.Shared;

            foreach (var shift in AllShifts())
            {
                foreach (var entry in shift.transmissions)
                {
                    if (entry.kind != SignalKind.OneTimePad)
                    {
                        continue;
                    }

                    var wire = entry.ResolveAirText(table);
                    var page = OneTimePad.ReadPageNumber(wire);

                    Assert.AreEqual(entry.padPage, page,
                        $"{entry.callsign} 的报头页码与声明的不一致");

                    var solved = OneTimePad.Solve(wire, entry.padBookSeed, page);
                    Assert.AreEqual(entry.plainText, table.DecodeDigits(solved),
                        $"{entry.callsign} 用自己的页码解不回原文");
                }
            }
        }

        [Test]
        public void EncryptedTransmissionsInTheSameShiftUseDifferentPages()
        {
            // 同一班里两条加密电文如果用同一页，玩家拿主线的页码去解干扰信号
            // 也能解通，"页码是关键"这个教学点就传达不到了。
            foreach (var shift in AllShifts())
            {
                var seen = new HashSet<int>();
                foreach (var entry in shift.transmissions)
                {
                    if (entry.kind != SignalKind.OneTimePad)
                    {
                        continue;
                    }

                    Assert.IsTrue(seen.Add(entry.padPage),
                        $"{shift.shiftId} 里有两条电文都用第 {entry.padPage} 页");
                }
            }
        }

        [Test]
        public void FirstShiftPrimaryDecodesToItsPlainText()
        {
            // 端到端验证：明文转数字流、数字流发报、再从数字流还原，必须回到原文。
            var table = ChineseTelegraphCode.Shared;
            var primary = ShiftLibrary.FirstShift().Primary;

            var air = primary.ResolveAirText(table);
            Assert.AreEqual(primary.plainText.Length * ChineseTelegraphCode.CodeLength, air.Length);
            Assert.AreEqual(primary.plainText, table.DecodeDigits(air));
        }

        [Test]
        public void FirstShiftPrimaryFitsInOneMinute()
        {
            // 一条主线电文循环一遍不该超过一分钟，否则玩家等一遍重播太久，
            // 抄漏一个字的代价会变得不合理。
            var station = ShiftLibrary.FirstShift().Primary
                .BuildStation(ChineseTelegraphCode.Shared);

            Assert.Less(station.TotalSeconds, 60f,
                $"主线电文一遍要 {station.TotalSeconds:F1} 秒，太长了");
        }
    }
}
