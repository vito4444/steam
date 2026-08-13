using Decoder.Signal;
using NUnit.Framework;
using UnityEngine;

namespace Decoder.Tests
{
    /// <summary>
    /// 慢扫描传真的调制格式与图像生成。
    ///
    /// 这条链路上发端和收端共用同一套时间换算，所以最要紧的检查是往返一致：
    /// 按格式发出去的频率序列，收端按同一套换算必须还原出原图。对不上的话，
    /// 玩家听到的和屏幕上画出来的就是两回事——而屏幕是听障玩家唯一的信息来源。
    /// </summary>
    public sealed class FacsimileTests
    {
        private const float Pixel = FacsimileSignal.DefaultPixelSeconds;

        [Test]
        public void Render_IsDeterministic()
        {
            var a = FacsimileImage.Render(FacsimileSubject.Coastline, 4242);
            var b = FacsimileImage.Render(FacsimileSubject.Coastline, 4242);
            for (var y = 0; y < a.Height; y++)
            {
                for (var x = 0; x < a.Width; x++)
                {
                    Assert.That(b.Sample(x, y), Is.EqualTo(a.Sample(x, y)).Within(1e-6f),
                        $"同一种子在 ({x},{y}) 生成了不同的像素，玩家重听同一次传输会看到两张图");
                }
            }
        }

        [Test]
        public void Render_DiffersBySeed()
        {
            var a = FacsimileImage.Render(FacsimileSubject.Coastline, 1);
            var b = FacsimileImage.Render(FacsimileSubject.Coastline, 2);
            var different = 0;
            for (var y = 0; y < a.Height; y++)
            {
                for (var x = 0; x < a.Width; x++)
                {
                    if (!Mathf.Approximately(a.Sample(x, y), b.Sample(x, y)))
                    {
                        different++;
                    }
                }
            }

            Assert.That(different, Is.GreaterThan(a.Width * a.Height / 20),
                "换了种子几乎生成同一张图，等于没有随机性");
        }

        [Test]
        public void Render_UsesTheWholeToneRange()
        {
            foreach (FacsimileSubject subject in System.Enum.GetValues(typeof(FacsimileSubject)))
            {
                var image = FacsimileImage.Render(subject, 77);
                var min = 1f;
                var max = 0f;
                for (var y = 0; y < image.Height; y++)
                {
                    for (var x = 0; x < image.Width; x++)
                    {
                        var v = image.Sample(x, y);
                        min = Mathf.Min(min, v);
                        max = Mathf.Max(max, v);
                    }
                }

                Assert.That(max - min, Is.GreaterThan(0.5f),
                    $"{subject} 的明暗跨度只有 {max - min:F2}，在示波管上会是一片糊");
            }
        }

        [Test]
        public void Frequency_StaysInsideTheVoiceBand()
        {
            // 整条链路要能挤进话音带宽，否则真实电台根本发不出去。
            var image = FacsimileImage.Render(FacsimileSubject.Calibration, 1);
            var total = FacsimileSignal.TotalSeconds(image, Pixel);
            for (var t = 0f; t < total; t += 0.0007f)
            {
                var hertz = FacsimileSignal.FrequencyAt(image, t, Pixel);
                Assert.That(hertz, Is.InRange(1100f, 2400f), $"t={t:F4} 处频率跑出话音带宽");
            }
        }

        [Test]
        public void EveryLineStartsWithASyncPulse()
        {
            var image = FacsimileImage.Render(FacsimileSubject.Facility, 9);
            var line = FacsimileSignal.LineSeconds(image.Width, Pixel);
            for (var row = 0; row < image.Height; row++)
            {
                var atSync = FacsimileSignal.LeaderSeconds + row * line + FacsimileSignal.SyncSeconds * 0.5f;
                Assert.That(FacsimileSignal.IsSync(image, atSync, Pixel), Is.True,
                    $"第 {row} 行没有行同步，接收端会从这里开始整幅图错位");
                Assert.That(FacsimileSignal.FrequencyAt(image, atSync, Pixel),
                    Is.EqualTo(FacsimileSignal.SyncHertz).Within(0.01f));
            }
        }

        [Test]
        public void SyncPulseSitsBelowBlackLevel()
        {
            // 同步必须落在黑电平以下，否则接收端分不出"换行"和"一行全黑"。
            Assert.That(FacsimileSignal.SyncHertz, Is.LessThan(FacsimileSignal.BlackHertz));
        }

        [Test]
        public void TransmittedFrequenciesDecodeBackToTheOriginalImage()
        {
            var image = FacsimileImage.Render(FacsimileSubject.Facility, 31);
            var received = new FacsimileImage(image.Width, image.Height);
            var painted = 0;

            // 按像素中点采样，这也是接收端实际的做法。
            var total = FacsimileSignal.TotalSeconds(image, Pixel);
            for (var t = 0f; t < total; t += Pixel * 0.25f)
            {
                if (!FacsimileSignal.PixelAt(image, t, Pixel, out var x, out var y))
                {
                    continue;
                }

                var hertz = FacsimileSignal.FrequencyAt(image, t, Pixel);
                received.Set(x, y, FacsimileSignal.HertzToLuminance(hertz));
                painted++;
            }

            Assert.That(painted, Is.GreaterThan(image.Width * image.Height),
                "采样点还没覆盖全图，这个往返测试说明不了问题");

            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < image.Width; x++)
                {
                    Assert.That(received.Sample(x, y), Is.EqualTo(image.Sample(x, y)).Within(0.01f),
                        $"({x},{y}) 收发不一致");
                }
            }
        }

        [Test]
        public void ScanAdvancesLeftToRightThenTopToBottom()
        {
            var image = FacsimileImage.Render(FacsimileSubject.Calibration, 5);
            var line = FacsimileSignal.LineSeconds(image.Width, Pixel);
            var start = FacsimileSignal.LeaderSeconds
                        + FacsimileSignal.SyncSeconds + FacsimileSignal.PorchSeconds;

            Assert.That(FacsimileSignal.PixelAt(image, start + Pixel * 0.5f, Pixel, out var x0, out var y0), Is.True);
            Assert.That((x0, y0), Is.EqualTo((0, 0)), "第一个像素不是左上角");

            Assert.That(FacsimileSignal.PixelAt(image, start + Pixel * 3.5f, Pixel, out var x1, out var y1), Is.True);
            Assert.That((x1, y1), Is.EqualTo((3, 0)), "同一行内没有向右推进");

            Assert.That(FacsimileSignal.PixelAt(image, start + line + Pixel * 0.5f, Pixel, out var x2, out var y2),
                Is.True);
            Assert.That((x2, y2), Is.EqualTo((0, 1)), "换行之后没有回到行首下一行");
        }

        [Test]
        public void NothingIsPaintedDuringLeaderOrSyncOrAfterTheEnd()
        {
            var image = FacsimileImage.Render(FacsimileSubject.Coastline, 12);
            Assert.That(FacsimileSignal.PixelAt(image, FacsimileSignal.LeaderSeconds * 0.5f, Pixel, out _, out _),
                Is.False, "引导音期间不该写像素");

            var atSync = FacsimileSignal.LeaderSeconds + FacsimileSignal.SyncSeconds * 0.5f;
            Assert.That(FacsimileSignal.PixelAt(image, atSync, Pixel, out _, out _),
                Is.False, "同步脉冲期间不该写像素");

            var total = FacsimileSignal.TotalSeconds(image, Pixel);
            Assert.That(FacsimileSignal.PixelAt(image, total + 1f, Pixel, out _, out _),
                Is.False, "发完之后还在写像素，屏幕会被最后一行刷成一片");
        }

        [Test]
        public void LuminanceRoundTripsThroughFrequency()
        {
            for (var i = 0; i <= 20; i++)
            {
                var luminance = i / 20f;
                var hertz = FacsimileSignal.LuminanceToHertz(luminance);
                Assert.That(FacsimileSignal.HertzToLuminance(hertz), Is.EqualTo(luminance).Within(1e-4f));
            }
        }

        [Test]
        public void BrighterPixelsSendHigherFrequencies()
        {
            // 极性反了的话整幅图会是负片，而玩家没有任何办法察觉。
            Assert.That(FacsimileSignal.LuminanceToHertz(1f),
                Is.GreaterThan(FacsimileSignal.LuminanceToHertz(0f)));
        }

        [Test]
        public void FacsimileStationKeepsTheCarrierOnForTheWholeImage()
        {
            // 传真是连续载波，不是键控。发图期间一刻不停，
            // 中间掉一段的话屏幕上就会缺一条。
            var image = FacsimileImage.Render(FacsimileSubject.Calibration, 8);
            var station = new SignalSynthesizer.Station(
                "F31", 7000f, string.Empty, 12f, facsimile: image);

            var total = FacsimileSignal.TotalSeconds(image, Pixel);
            for (var t = 0.01f; t < total; t += 0.05f)
            {
                Assert.That(station.IsKeyDown(t), Is.True, $"t={t:F2} 处载波断了");
            }

            Assert.That(station.IsKeyDown(total + 0.5f), Is.False, "发完之后载波还开着");
        }

        [Test]
        public void FacsimileToneTracksTheImageContent()
        {
            // 音频频率必须跟着图像走。跟不上的话，听到的和看到的就是两回事。
            var image = new FacsimileImage(8, 4);
            for (var y = 0; y < 4; y++)
            {
                image.Set(0, y, 0f);
                image.Set(7, y, 1f);
            }

            var station = new SignalSynthesizer.Station(
                "F31", 7000f, string.Empty, 12f, facsimile: image);

            var start = FacsimileSignal.LeaderSeconds
                        + FacsimileSignal.SyncSeconds + FacsimileSignal.PorchSeconds;
            var atBlack = SignalSynthesizer.FacsimileToneFor(station, start + Pixel * 0.5f, 0f);
            var atWhite = SignalSynthesizer.FacsimileToneFor(station, start + Pixel * 7.5f, 0f);

            Assert.That(atBlack, Is.EqualTo(FacsimileSignal.BlackHertz).Within(1f));
            Assert.That(atWhite, Is.EqualTo(FacsimileSignal.WhiteHertz).Within(1f));
        }

        [Test]
        public void DetuneShiftsTheWholeFacsimileToneTogether()
        {
            // 失配搬移整条音频，所以解调出的亮度会整体偏——图整个发白或发黑，
            // 而不是某些像素错。玩家据此可以只看屏幕把频率调准。
            var image = FacsimileImage.Render(FacsimileSubject.Calibration, 2);
            var station = new SignalSynthesizer.Station(
                "F31", 7000f, string.Empty, 12f, facsimile: image);

            var start = FacsimileSignal.LeaderSeconds
                        + FacsimileSignal.SyncSeconds + FacsimileSignal.PorchSeconds;
            for (var i = 0; i < 6; i++)
            {
                var t = start + Pixel * (i * 4 + 0.5f);
                var centred = SignalSynthesizer.FacsimileToneFor(station, t, 0f);
                var offset = SignalSynthesizer.FacsimileToneFor(station, t, 0.3f);
                Assert.That(offset - centred, Is.EqualTo(0.3f * 700f).Within(1f),
                    "失配没有把整条音频一起搬移");
            }
        }

        [Test]
        public void FacsimileEntryBuildsAStationThatCarriesTheImage()
        {
            var entry = new Gameplay.TransmissionEntry
            {
                callsign = "F31",
                frequencyKHz = 6968f,
                kind = Gameplay.SignalKind.Facsimile,
                facsimileSubject = FacsimileSubject.Facility,
                facsimileSeed = 51119,
            };

            var station = entry.BuildStation(ChineseTelegraphCode.Shared);
            Assert.That(station.Facsimile, Is.Not.Null, "传真信号建出来的电台没有图");
            Assert.That(station.Facsimile.Width, Is.EqualTo(FacsimileImage.DefaultWidth));
            Assert.That(station.TotalSeconds, Is.GreaterThan(5f),
                "整幅图的时长没有按传真算，还是按电码算的");
        }

        [Test]
        public void NonFacsimileEntriesCarryNoImage()
        {
            var entry = new Gameplay.TransmissionEntry
            {
                callsign = "M08",
                kind = Gameplay.SignalKind.PlainMorse,
                plainText = "CQ",
            };

            Assert.That(entry.BuildStation(ChineseTelegraphCode.Shared).Facsimile, Is.Null);
        }

        [Test]
        public void FacsimileReportsAreJudgedOnTheThreatCallNotOnCopiedText()
        {
            var entry = new Gameplay.TransmissionEntry
            {
                callsign = "F31",
                frequencyKHz = 6968f,
                kind = Gameplay.SignalKind.Facsimile,
                facsimileSubject = FacsimileSubject.Facility,
                correctLevel = Gameplay.ThreatLevel.Flash,
            };

            // 抄收纸留空是正常的，传真没有可抄的字符。
            var blank = Gameplay.ReportGrader.Grade(entry, new Gameplay.ReportSubmission
            {
                Callsign = "F31",
                FrequencyKHz = 6968f,
                CopiedText = string.Empty,
                Level = Gameplay.ThreatLevel.Flash,
            }, ChineseTelegraphCode.Shared);
            Assert.That(blank.Outcome, Is.EqualTo(Gameplay.ReportOutcome.Clean),
                "传真报告因为抄收纸是空的被判失真");

            // 就算玩家在抄收纸上写了点什么，也不该因此判失真。
            var scribbled = Gameplay.ReportGrader.Grade(entry, new Gameplay.ReportSubmission
            {
                Callsign = "F31",
                FrequencyKHz = 6968f,
                CopiedText = "3325 1234",
                Level = Gameplay.ThreatLevel.Flash,
            }, ChineseTelegraphCode.Shared);
            Assert.That(scribbled.Outcome, Is.EqualTo(Gameplay.ReportOutcome.Clean));

            // 但等级报错照样要担后果——这是唯一体现"看没看图"的地方。
            var missed = Gameplay.ReportGrader.Grade(entry, new Gameplay.ReportSubmission
            {
                Callsign = "F31",
                FrequencyKHz = 6968f,
                CopiedText = string.Empty,
                Level = Gameplay.ThreatLevel.Routine,
            }, ChineseTelegraphCode.Shared);
            Assert.That(missed.Outcome, Is.EqualTo(Gameplay.ReportOutcome.Underreported),
                "没看图就按例行报，却没有被判漏报");
        }

        [Test]
        public void NonFacsimileReportsStillNeedTheCopiedText()
        {
            // 上面那条豁免只能作用在传真上。漏到电码信号上的话，
            // 整个抄报玩法就没有了判定。
            var entry = new Gameplay.TransmissionEntry
            {
                callsign = "M08",
                frequencyKHz = 7000f,
                kind = Gameplay.SignalKind.PlainMorse,
                plainText = "ATTENTION ALL STATIONS",
                correctLevel = Gameplay.ThreatLevel.Routine,
            };

            var grade = Gameplay.ReportGrader.Grade(entry, new Gameplay.ReportSubmission
            {
                Callsign = "M08",
                FrequencyKHz = 7000f,
                CopiedText = string.Empty,
                Level = Gameplay.ThreatLevel.Routine,
            }, ChineseTelegraphCode.Shared);

            Assert.That(grade.Outcome, Is.EqualTo(Gameplay.ReportOutcome.Useless));
        }

        [Test]
        public void WholeImageFitsInAReasonableWatch()
        {
            // 一幅图要能在一个班次里发完，又得长到让玩家没法边扫频边接收。
            var image = FacsimileImage.Render(FacsimileSubject.Facility, 3);
            var total = FacsimileSignal.TotalSeconds(image, Pixel);
            Assert.That(total, Is.InRange(5f, 20f), $"一幅图要发 {total:F1} 秒，节奏不对");
        }
    }
}
