using System.Linq;
using Monster.Rules;
using NUnit.Framework;

namespace Monster.Tests
{
    /// <summary>Tests for the paperwork and monitor readouts.
    ///
    /// The property these exist to protect is fairness: a criterion may only test
    /// something the player can actually read somewhere on the desk. A rule that turns on
    /// a hidden value does not make the game hard, it makes it a coin flip.</summary>
    public sealed class DocumentTests
    {
        private const int CampaignSeed = 20260813;

        /// <summary>The important one. Turns red the moment a criterion is added that
        /// tests a field no document or monitor prints.</summary>
        [Test]
        public void EveryFieldACriterionTestsIsPrintedSomewhere()
        {
            var subject = new SubjectGenerator().Generate(CampaignSeed, 20, 1).Attributes;
            var covered = DocumentBuilder.ObservableFieldCoverage(subject);

            foreach (var criterion in CheckpointManual.Build().AllPages)
            {
                foreach (var field in criterion.Conditions.SelectMany(c => c.ReadFields()))
                {
                    Assert.IsTrue(covered.Contains(field),
                        $"{criterion.Id} rev{criterion.Revision} tests '{field}', " +
                        "which nothing on the desk shows the player");
                }
            }
        }

        [Test]
        public void ThePermitPrintsEveryPaperworkField()
        {
            var subject = new SubjectGenerator().Generate(CampaignSeed, 4, 2).Attributes;
            var page = DocumentBuilder.TransitPermit(subject).ToPrintedPage();

            StringAssert.Contains(subject.Name.ToUpperInvariant(), page);
            StringAssert.Contains(subject.BirthYear.ToString(), page);
            StringAssert.Contains(subject.OriginDistrict.ToUpperInvariant(), page);
            StringAssert.Contains(subject.PermitSerial, page);
            StringAssert.Contains(subject.IssuingOffice.ToUpperInvariant(), page);
        }

        /// <summary>The permit prints how old it is rather than a date, because the
        /// criterion is expressed in days and making the player do calendar arithmetic
        /// would add tedium rather than difficulty.</summary>
        [Test]
        public void ThePermitPrintsItsAgeInTheSameUnitTheManualUses()
        {
            var subject = new SubjectGenerator().Generate(CampaignSeed, 4, 2).Attributes;
            subject.PermitAgeDays = 22;
            StringAssert.Contains("22 DAYS AGO", DocumentBuilder.TransitPermit(subject).ToPrintedPage());

            subject.PermitAgeDays = 0;
            StringAssert.Contains("TODAY", DocumentBuilder.TransitPermit(subject).ToPrintedPage());
        }

        [Test]
        public void TheBiometricReadoutUsesTheSameUnitsAsTheManual()
        {
            var subject = new SubjectGenerator().Generate(CampaignSeed, 4, 5).Attributes;
            subject.BlinkRatePerMinute = 2;
            subject.BreathsPerMinute = 31;
            subject.SkinTemperatureC = 33.4f;
            subject.PupilsReactToLight = false;
            subject.VisibleLimbCount = 5;

            var page = DocumentBuilder.BiometricReadout(subject).ToPrintedPage();

            StringAssert.Contains("2/MIN", page);
            StringAssert.Contains("31/MIN", page);
            StringAssert.Contains("33.4C", page);
            StringAssert.Contains("FIXED", page);
            StringAssert.Contains("5", page);
        }

        // --------------------------------------------------------------- portrait codes --

        [Test]
        public void PortraitCodesAreDeterministic()
        {
            var subject = new SubjectGenerator().Generate(CampaignSeed, 11, 6).Attributes;
            Assert.AreEqual(PortraitCode.FromSubject(subject), PortraitCode.FromSubject(subject));
            Assert.AreEqual(PortraitCode.AsObserved(subject), PortraitCode.AsObserved(subject));
        }

        /// <summary>Turns red if a photograph mismatch becomes invisible or becomes so
        /// obvious that no comparison is needed. Both failures remove the mechanic.</summary>
        [Test]
        public void APhotographMismatchIsVisibleButNotObvious()
        {
            var generator = new SubjectGenerator();
            var comparedMismatches = 0;

            for (var i = 0; i < 4000; i++)
            {
                var subject = generator.Generate(CampaignSeed, i % 30, i).Attributes;
                var onPermit = PortraitCode.FromSubject(subject);
                var onMonitor = PortraitCode.AsObserved(subject);
                var distance = onPermit.DistanceTo(onMonitor);

                if (subject.PhotoMatchesFace)
                {
                    Assert.AreEqual(0, distance,
                        "a matching photograph must be pixel-identical or the player cannot trust it");
                    continue;
                }

                comparedMismatches++;
                Assert.GreaterOrEqual(distance, 1, "a mismatched photograph was identical to the permit");
                Assert.LessOrEqual(distance, 4,
                    "a mismatch differing in more than four of sixteen cells needs no comparison");
            }

            Assert.Greater(comparedMismatches, 40, "the sample should contain plenty of mismatches");
        }

        /// <summary>A consistent reflection is the mirror of what is on the glass. Turns
        /// red if the mirroring is dropped, which would make every subject look wrong.</summary>
        [Test]
        public void AConsistentReflectionIsTheMirrorOfTheSubject()
        {
            var generator = new SubjectGenerator();
            var inconsistent = 0;

            for (var i = 0; i < 3000; i++)
            {
                var subject = generator.Generate(CampaignSeed, i % 30, i).Attributes;
                var observed = PortraitCode.AsObserved(subject);
                var reflected = PortraitCode.AsReflected(subject);

                if (subject.ReflectionConsistent)
                {
                    Assert.AreEqual(observed.MirroredHorizontally(), reflected);
                }
                else
                {
                    inconsistent++;
                    Assert.AreNotEqual(observed.MirroredHorizontally(), reflected);
                }
            }

            Assert.Greater(inconsistent, 30);
        }

        /// <summary>A grid that is all lit or all dark carries nothing to compare.</summary>
        [Test]
        public void PortraitGridsAreNeverBlankOrSolid()
        {
            var generator = new SubjectGenerator();

            for (var i = 0; i < 2000; i++)
            {
                var subject = generator.Generate(CampaignSeed, i % 30, i).Attributes;

                foreach (var code in new[]
                         {
                             PortraitCode.FromSubject(subject),
                             PortraitCode.AsObserved(subject),
                             PortraitCode.AsReflected(subject),
                         })
                {
                    var lit = 0;
                    for (var y = 0; y < PortraitCode.Size; y++)
                    {
                        for (var x = 0; x < PortraitCode.Size; x++)
                        {
                            if (code.Get(x, y))
                            {
                                lit++;
                            }
                        }
                    }

                    Assert.GreaterOrEqual(lit, 4, $"portrait {code} is nearly blank");
                    Assert.LessOrEqual(lit, 12, $"portrait {code} is nearly solid");
                }
            }
        }

        [Test]
        public void PortraitRendersAsFourRowsOfBlocks()
        {
            var subject = new SubjectGenerator().Generate(CampaignSeed, 2, 2).Attributes;
            var rows = PortraitCode.FromSubject(subject).ToBlockRows().Split('\n');

            Assert.AreEqual(4, rows.Length);
            foreach (var row in rows)
            {
                Assert.AreEqual(8, row.Length, "each cell is drawn as two characters so the grid reads square");
            }
        }
    }
}
