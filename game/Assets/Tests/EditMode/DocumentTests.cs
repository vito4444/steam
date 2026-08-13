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

        /// <summary>Pulls a real bearer out of the generator rather than hand-building one,
        /// so these tests break if the generator stops producing the case.</summary>
        private static SubjectAttributes FindSubject(bool photoMatches)
        {
            var generator = new SubjectGenerator();

            for (var i = 0; i < 4000; i++)
            {
                var subject = generator.Generate(CampaignSeed, i % 30, i).Attributes;
                if (subject.PhotoMatchesFace == photoMatches)
                {
                    return subject;
                }
            }

            throw new AssertionException(
                $"the generator never produced a bearer with PhotoMatchesFace = {photoMatches}");
        }

        /// <summary>The comparison the whole tell depends on. Both faces have to be on the
        /// one screen, and they have to differ exactly when the photograph does not match --
        /// a screen that shows two identical grids for a mismatched bearer is worse than no
        /// screen, because the player will trust it.</summary>
        [Test]
        public void TheCabinFeedShowsBothFacesAtOnce()
        {
            var matching = FindSubject(photoMatches: true);
            var mismatched = FindSubject(photoMatches: false);

            foreach (var subject in new[] { matching, mismatched })
            {
                var feed = DocumentBuilder.CabinFeed(subject);
                Assert.IsTrue(feed.Portrait.HasValue, "no file photograph on the cabin feed");
                Assert.IsTrue(feed.Comparison.HasValue, "no observed face on the cabin feed");
            }

            Assert.AreEqual(DocumentBuilder.CabinFeed(matching).Portrait,
                DocumentBuilder.CabinFeed(matching).Comparison,
                "a bearer whose photograph matches is shown two different faces");

            Assert.AreNotEqual(DocumentBuilder.CabinFeed(mismatched).Portrait,
                DocumentBuilder.CabinFeed(mismatched).Comparison,
                "a bearer whose photograph does not match is shown two identical faces");
        }

        /// <summary>The permit keeps its photograph. It is a document; a transit permit
        /// without a picture on it is not one, and the screen is a convenience rather than
        /// the authority.</summary>
        [Test]
        public void ThePermitStillCarriesTheFilePhotograph()
        {
            var subject = FindSubject(photoMatches: false);

            Assert.AreEqual(DocumentBuilder.TransitPermit(subject).Portrait,
                DocumentBuilder.CabinFeed(subject).Portrait,
                "the permit and the screen disagree about what the district has on file");
        }

        [Test]
        public void BothFacesArePrintedOnTheSameLines()
        {
            var subject = FindSubject(photoMatches: false);
            var block = PortraitCode.SideBySide(
                DocumentBuilder.CabinFeed(subject).Portrait.Value,
                DocumentBuilder.CabinFeed(subject).Comparison.Value);

            var lines = block.Split('\n');

            Assert.AreEqual(PortraitCode.Size + 1, lines.Length, "the pair is not one caption and four rows");
            StringAssert.Contains("ON FILE", lines[0]);
            StringAssert.Contains("OBSERVED", lines[0]);

            for (var y = 1; y < lines.Length; y++)
            {
                Assert.AreEqual(lines[1].Length, lines[y].Length,
                    "the two grids do not line up, so cells cannot be compared by eye");
            }
        }


        /// <summary>MASS was NOMINAL exactly when CARGO was MATCHED -- the same boolean twice
        /// under two names. A screen that says one thing twice teaches the player to skim it,
        /// and it cost a line the paired portraits needed.</summary>
        [Test]
        public void NoScreenPrintsTheSameFactTwice()
        {
            var subject = FindSubject(photoMatches: true);

            foreach (var content in new[]
                     {
                         DocumentBuilder.CabinFeed(subject),
                         DocumentBuilder.BiometricReadout(subject),
                     })
            {
                var values = content.Fields.Select(f => f.Value).ToList();
                var derived = values.Where(v => v is "MATCHED" or "NOMINAL" or "DIVERGENT" or "OVER").ToList();

                Assert.LessOrEqual(derived.Count, 1,
                    $"{content.Title} prints the cargo scan result {derived.Count} times");
            }
        }

    }
}
