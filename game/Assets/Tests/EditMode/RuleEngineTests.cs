using System;
using System.Collections.Generic;
using System.Linq;
using Monster.Shift;
using Monster.Rules;
using NUnit.Framework;

namespace Monster.Tests
{
    /// <summary>Tests for the checkpoint rules engine.
    ///
    /// Each test below is written so that it can be stated which change to the production
    /// code turns it red. A test that cannot fail is not guarding anything, and this
    /// engine is the entire game — if it is wrong, the game is unfair rather than hard.
    /// </summary>
    public sealed class RuleEngineTests
    {
        private const int CampaignSeed = 20260813;

        private static CriteriaManual FullManual() => CheckpointManual.Build();

        // ------------------------------------------------------------ manual integrity --

        /// <summary>Turns red if a criterion's printed text stops naming the verdict it
        /// actually demands. That divergence is the single worst bug this game could ship:
        /// the player reads the page, follows it exactly, and is marked wrong.</summary>
        [Test]
        public void PrintedTextNamesTheVerdictItDemands()
        {
            foreach (var criterion in FullManual().AllPages)
            {
                var expectedWord = criterion.Demands switch
                {
                    Verdict.Pass => "PASS",
                    Verdict.Hold => "HOLD",
                    Verdict.Refer => "REFER",
                    Verdict.Alarm => "ALARM",
                    _ => throw new ArgumentOutOfRangeException(),
                };

                StringAssert.Contains(expectedWord, criterion.PrintedText.ToUpperInvariant(),
                    $"{criterion.Id} rev{criterion.Revision} demands {criterion.Demands} " +
                    $"but its printed text does not say so: \"{criterion.PrintedText}\"");
            }
        }

        /// <summary>Turns red if a criterion is added to the manual with no way for the
        /// generator to produce a subject that violates it, or the reverse. Either
        /// direction means content that can never appear in the game.</summary>
        [Test]
        public void EveryCriterionCanBeViolatedAndEveryViolationHasACriterion()
        {
            var manualIds = FullManual().AllPages.Select(p => p.Id).Distinct().OrderBy(x => x).ToList();
            var generatorIds = SubjectGenerator.KnownViolations.OrderBy(x => x).ToList();

            CollectionAssert.AreEqual(manualIds, generatorIds,
                "the manual and the subject generator disagree about which criteria exist");
        }

        /// <summary>Turns red if a criterion ever reads ground truth. The player cannot
        /// observe whether a subject is human, so a criterion that tests it would create
        /// an unsolvable case.</summary>
        [Test]
        public void NoCriterionReadsGroundTruth()
        {
            foreach (var criterion in FullManual().AllPages)
            {
                foreach (var field in criterion.Conditions.SelectMany(c => c.ReadFields()))
                {
                    Assert.AreNotEqual("IsHuman", field,
                        $"{criterion.Id} rev{criterion.Revision} tests ground truth");
                    Assert.AreNotEqual("unknown", field,
                        $"{criterion.Id} rev{criterion.Revision} has a condition of unknown kind");
                }
            }
        }

        [Test]
        public void IssuingTheSamePageTwiceIsRejected()
        {
            var manual = new CriteriaManual();
            manual.Issue(new Criterion("X-01", 1, "Test. HOLD.", Verdict.Hold,
                Condition.Flag(FlagField.SealPresent, false)));

            Assert.Throws<InvalidOperationException>(() =>
                manual.Issue(new Criterion("X-01", 1, "Test again. HOLD.", Verdict.Hold,
                    Condition.Flag(FlagField.SealPresent, false))));
        }

        // ---------------------------------------------------------------- supersession --

        /// <summary>Turns red if CriteriaManual.InForce stops preferring the highest
        /// revision, which would make every amendment in the game inert.</summary>
        [Test]
        public void AmendmentsSupersedeEarlierRevisionsButStayInTheBinder()
        {
            var manual = FullManual();

            var inForce = manual.InForce.Single(c => c.Id == "C-05");
            Assert.AreEqual(2, inForce.Revision, "the amendment should be the rule in force");

            var pages = manual.AllPages.Where(c => c.Id == "C-05").Select(c => c.Revision).OrderBy(r => r);
            CollectionAssert.AreEqual(new[] { 1, 2 }, pages,
                "the superseded page must remain in the binder; reading around it is the mechanic");

            Assert.IsTrue(manual.Superseded.Any(c => c.Id == "C-05" && c.Revision == 1));
        }

        /// <summary>Turns red if amendments stop being gated by arrival shift, which would
        /// hand the player the endgame manual on night one.</summary>
        [Test]
        public void AmendmentsAreNotInTheBinderBeforeTheyArrive()
        {
            var early = CheckpointManual.AsOfShift(3);
            var late = CheckpointManual.AsOfShift(20);

            Assert.AreEqual(1, early.InForce.Single(c => c.Id == "C-05").Revision,
                "C-05 rev2 arrives on shift 6 and must not be in force on shift 3");
            Assert.AreEqual(2, late.InForce.Single(c => c.Id == "C-05").Revision);

            Assert.IsFalse(early.AllPages.Any(c => c.Id == "C-16"),
                "C-16 arrives on shift 15 and must not exist at all on shift 3");
            Assert.IsTrue(late.AllPages.Any(c => c.Id == "C-16"));
        }

        /// <summary>The amendment to C-06 raises its severity from Refer to Alarm. Turns
        /// red if severity stops travelling with the revision.</summary>
        [Test]
        public void AnAmendmentCanChangeSeverityNotJustThresholds()
        {
            var subject = CleanSubject();
            subject.BlinkRatePerMinute = 2;

            Assert.AreEqual(Verdict.Refer,
                RuleEvaluator.Evaluate(subject, CheckpointManual.AsOfShift(10)).CorrectVerdict);
            Assert.AreEqual(Verdict.Alarm,
                RuleEvaluator.Evaluate(subject, CheckpointManual.AsOfShift(20)).CorrectVerdict);
        }

        // ------------------------------------------------------------------ evaluation --

        /// <summary>The binder prints the night each page arrived, and that is the only
        /// thing a player has to work out which of two revisions the office is grading
        /// against. If an amendment ever arrived on or before the page it replaces, the
        /// printed nights would contradict the rule that later revisions win and the
        /// player would be misled by the one piece of evidence they have.</summary>
        [Test]
        public void AnAmendmentAlwaysArrivesAfterThePageItReplaces()
        {
            var manual = CheckpointManual.AsOfShift(Campaign.TotalShifts - 1);

            foreach (var group in manual.AllPages.GroupBy(p => p.Id))
            {
                var revisions = group.OrderBy(p => p.Revision).ToList();

                for (var i = 1; i < revisions.Count; i++)
                {
                    var earlier = manual.ArrivalOf(revisions[i - 1]);
                    var later = manual.ArrivalOf(revisions[i]);

                    Assert.Greater(later, earlier,
                        $"{revisions[i].Id} revision {revisions[i].Revision} is printed as arriving " +
                        $"on night {later + 1}, the same night or earlier than revision " +
                        $"{revisions[i - 1].Revision} it supersedes");
                }
            }
        }

        /// <summary>Every page carries an arrival night, including the ones that were in
        /// the binder on the first shift. A page with nothing printed on it is a page the
        /// player cannot place.</summary>
        [Test]
        public void EveryPageInTheBinderKnowsWhenItArrived()
        {
            for (var night = 0; night < Campaign.TotalShifts; night++)
            {
                var manual = CheckpointManual.AsOfShift(night);

                foreach (var page in manual.AllPages)
                {
                    Assert.LessOrEqual(manual.ArrivalOf(page), night,
                        $"{page.Id}/{page.Revision} is in the night {night + 1} binder but is " +
                        $"printed as arriving on night {manual.ArrivalOf(page) + 1}");
                }
            }
        }

        [Test]
        public void ASubjectViolatingNothingPasses()
        {
            var evaluation = RuleEvaluator.Evaluate(CleanSubject(), FullManual());

            Assert.IsTrue(evaluation.IsClean, "a clean subject fired: " +
                                              string.Join("; ", RuleEvaluator.Explain(evaluation)));
            Assert.AreEqual(Verdict.Pass, evaluation.CorrectVerdict);
        }

        /// <summary>Turns red if the resolution rule stops being "most severe wins" — for
        /// example if it took the first match or the last.</summary>
        [Test]
        public void TheMostSevereFiredCriterionDecidesTheVerdict()
        {
            var subject = CleanSubject();
            subject.PermitAgeDays = 40;          // C-03, Hold
            subject.SealPresent = false;         // C-01, Refer
            subject.PupilsReactToLight = false;  // C-07, Alarm

            var evaluation = RuleEvaluator.Evaluate(subject, FullManual());

            Assert.AreEqual(Verdict.Alarm, evaluation.CorrectVerdict);
            CollectionAssert.AreEquivalent(new[] { "C-01", "C-03", "C-07" },
                evaluation.FiredCriteria.Select(c => c.Id));
        }

        /// <summary>Turns red if letting a dangerous subject through stops being weighted
        /// more heavily than being over-cautious. That asymmetry is a design decision, not
        /// an implementation detail.</summary>
        [Test]
        public void PassingSomethingDangerousIsWorseThanBeingOverCautious()
        {
            var tooLenient = RuleEvaluator.ErrorSeverity(Verdict.Pass, Verdict.Alarm);
            var tooStrict = RuleEvaluator.ErrorSeverity(Verdict.Alarm, Verdict.Pass);

            Assert.AreEqual(0, RuleEvaluator.ErrorSeverity(Verdict.Refer, Verdict.Refer));
            Assert.Greater(tooLenient, tooStrict,
                $"passing an alarm case scored {tooLenient}, alarming a clean one scored {tooStrict}");
        }

        [TestCase("C-01", Verdict.Refer)]
        [TestCase("C-03", Verdict.Hold)]
        [TestCase("C-07", Verdict.Alarm)]
        [TestCase("C-14", Verdict.Refer)]
        public void EachCriterionProducesItsOwnVerdictInIsolation(string criterionId, Verdict expected)
        {
            var manual = new CriteriaManual();
            var page = FullManual().InForce.Single(c => c.Id == criterionId);
            manual.Issue(page);

            var subject = CleanSubject();
            ApplyViolation(criterionId, ref subject);

            var evaluation = RuleEvaluator.Evaluate(subject, manual);
            Assert.IsFalse(evaluation.IsClean, $"{criterionId} did not fire on a subject built to violate it");
            Assert.AreEqual(expected, evaluation.CorrectVerdict);
        }

        // ------------------------------------------------------------------- generator --

        [Test]
        public void GenerationIsDeterministic()
        {
            var a = new SubjectGenerator().Generate(CampaignSeed, 7, 3);
            var b = new SubjectGenerator().Generate(CampaignSeed, 7, 3);
            var different = new SubjectGenerator().Generate(CampaignSeed, 7, 4);

            Assert.AreEqual(a.Attributes, b.Attributes, "the same seed produced two different subjects");
            CollectionAssert.AreEqual(a.IntendedViolations, b.IntendedViolations);
            Assert.AreNotEqual(a.Attributes, different.Attributes,
                "consecutive subjects in a queue must not be identical");
        }

        /// <summary>The property that matters most: over a whole campaign's worth of
        /// generated cases, the generator and the evaluator never disagree. Turns red if
        /// a violation stops actually violating its criterion — for instance if a
        /// threshold moves in the manual but not in the generator.</summary>
        [Test]
        public void EveryIntendedViolationActuallyFiresItsCriterion()
        {
            var generator = new SubjectGenerator();
            var manual = FullManual();
            var checkedCases = 0;

            for (var shift = 0; shift < 30; shift++)
            {
                for (var index = 0; index < 24; index++)
                {
                    var generated = generator.Generate(CampaignSeed, shift, index);
                    var evaluation = RuleEvaluator.Evaluate(generated.Attributes, manual);
                    var firedIds = evaluation.FiredCriteria.Select(c => c.Id).ToHashSet();

                    foreach (var intended in generated.IntendedViolations)
                    {
                        Assert.IsTrue(firedIds.Contains(intended),
                            $"shift {shift} subject {index} (seed {generated.Seed}) was built to violate " +
                            $"{intended} but the evaluator did not fire it. Subject: {generated.Attributes}");
                    }

                    checkedCases++;
                }
            }

            Assert.AreEqual(720, checkedCases);
        }

        /// <summary>Turns red if a subject the generator intended to be clean is judged
        /// guilty, which would make the game unwinnable in a way no player could diagnose.</summary>
        [Test]
        public void SubjectsIntendedToBeCleanAlwaysPass()
        {
            var generator = new SubjectGenerator();
            var manual = FullManual();
            var cleanCases = 0;

            for (var shift = 0; shift < 30; shift++)
            {
                for (var index = 0; index < 24; index++)
                {
                    var generated = generator.Generate(CampaignSeed, shift, index);
                    if (generated.IntendedViolations.Count != 0)
                    {
                        continue;
                    }

                    cleanCases++;
                    var evaluation = RuleEvaluator.Evaluate(generated.Attributes, manual);
                    Assert.AreEqual(Verdict.Pass, evaluation.CorrectVerdict,
                        $"seed {generated.Seed} was meant to be clean but fired " +
                        string.Join("; ", RuleEvaluator.Explain(evaluation)));
                }
            }

            Assert.Greater(cleanCases, 200, "the sample should contain plenty of clean subjects");
        }

        /// <summary>Turns red if the difficulty curve flattens or inverts.</summary>
        [Test]
        public void MoreSubjectsAreWrongLateInTheCampaignThanEarly()
        {
            var generator = new SubjectGenerator();

            double MeanViolations(int shift) =>
                Enumerable.Range(0, 400)
                    .Select(i => generator.Generate(CampaignSeed, shift, i).IntendedViolations.Count)
                    .Average();

            var early = MeanViolations(1);
            var late = MeanViolations(28);

            Assert.Less(early, late,
                $"shift 1 averaged {early:F2} violations, shift 28 averaged {late:F2}");
            Assert.Less(early, 0.75, "night one should be mostly clean paperwork");
            Assert.Greater(late, 1.0, "the last nights should be mostly problems");
        }

        /// <summary>Ten thousand cases through the evaluator, asserting only that it always
        /// produces a defined answer. A cheap property test that would catch an
        /// unhandled field, a null district, or a comparison that throws.</summary>
        [Test]
        public void TheEvaluatorNeverFailsOnAGeneratedSubject()
        {
            var generator = new SubjectGenerator();
            var manual = FullManual();
            var verdicts = new Dictionary<Verdict, int>();

            for (var i = 0; i < 10_000; i++)
            {
                var generated = generator.Generate(CampaignSeed, i % 30, i);
                var evaluation = RuleEvaluator.Evaluate(generated.Attributes, manual);

                Assert.IsTrue(Enum.IsDefined(typeof(Verdict), evaluation.CorrectVerdict));
                verdicts[evaluation.CorrectVerdict] = verdicts.GetValueOrDefault(evaluation.CorrectVerdict) + 1;
            }

            // Every verdict has to be reachable, or a switch on the classification panel is
            // decoration.
            foreach (Verdict verdict in Enum.GetValues(typeof(Verdict)))
            {
                Assert.Greater(verdicts.GetValueOrDefault(verdict), 0,
                    $"no generated subject in 10,000 ever required {verdict}");
            }
        }

        /// <summary>Ground truth has to correlate with the body-signal criteria, because
        /// the endings are scored on it. Turns red if the two drift apart.</summary>
        [Test]
        public void PaperworkProblemsDoNotMakeSomeoneInhuman()
        {
            var generator = new SubjectGenerator();
            // C-17 is deliberately not here: a bearer who names a district other than the
            // one on their permit is a body-signal problem, not a clerical one.
            var paperworkOnly = new[] { "C-01", "C-02", "C-03", "C-04", "C-05", "C-14", "C-15", "C-16" };
            var samples = 0;

            for (var i = 0; i < 3000; i++)
            {
                var generated = generator.Generate(CampaignSeed, i % 30, i);
                if (generated.IntendedViolations.Count == 0 ||
                    !generated.IntendedViolations.All(paperworkOnly.Contains))
                {
                    continue;
                }

                samples++;
                Assert.IsTrue(generated.Attributes.IsHuman,
                    $"seed {generated.Seed} has only paperwork problems " +
                    $"({string.Join(", ", generated.IntendedViolations)}) but was marked inhuman");
            }

            Assert.Greater(samples, 100, "the sample should contain plenty of paperwork-only cases");
        }

        // --------------------------------------------------------------------- helpers --

        private static SubjectAttributes CleanSubject() => new()
        {
            Name = "Test Subject",
            BirthYear = 1980,
            OriginDistrict = "Lowbank",
            PermitSerial = "AR-4412",
            PermitAgeDays = 3,
            IssuingOffice = "Central",
            PhotoMatchesFace = true,
            SealPresent = true,
            BlinkRatePerMinute = 14,
            ResponseDelaySeconds = 0.9f,
            PupilsReactToLight = true,
            BreathsPerMinute = 15,
            SkinTemperatureC = 36.6f,
            VisibleLimbCount = 4,
            ReflectionConsistent = true,
            SecondVoiceUnderTheFirst = false,
            CargoDeclarationMatchesScan = true,
            SpokenDistrictMatchesPermit = true,
            IsHuman = true,
        };

        private static void ApplyViolation(string criterionId, ref SubjectAttributes subject)
        {
            switch (criterionId)
            {
                case "C-01": subject.SealPresent = false; break;
                case "C-02": subject.PhotoMatchesFace = false; break;
                case "C-03": subject.PermitAgeDays = 40; break;
                case "C-04": subject.PermitSerial = "KX-1234"; break;
                case "C-05": subject.OriginDistrict = "Vessel"; break;
                case "C-06": subject.BlinkRatePerMinute = 2; break;
                case "C-07": subject.PupilsReactToLight = false; break;
                case "C-08": subject.BreathsPerMinute = 3; break;
                case "C-09": subject.BreathsPerMinute = 33; break;
                case "C-10": subject.SecondVoiceUnderTheFirst = true; break;
                case "C-11": subject.ReflectionConsistent = false; break;
                case "C-12": subject.SkinTemperatureC = 31f; break;
                case "C-13": subject.VisibleLimbCount = 6; break;
                case "C-14": subject.ResponseDelaySeconds = 4.2f; break;
                case "C-15": subject.IssuingOffice = "Northgate"; break;
                case "C-16": subject.CargoDeclarationMatchesScan = false; break;
                case "C-17": subject.SpokenDistrictMatchesPermit = false; break;
                default: throw new ArgumentOutOfRangeException(nameof(criterionId), criterionId, null);
            }
        }
    }
}
