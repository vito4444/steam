using System;
using System.Linq;
using Monster.Rules;
using Monster.Shift;
using NUnit.Framework;

namespace Monster.Tests
{
    /// <summary>Tests for one night at the checkpoint and for a whole campaign.
    ///
    /// ShiftDirector has no Unity types in it on purpose, so a thirty-night campaign runs
    /// here in milliseconds. That makes it possible to assert things about the economy and
    /// the difficulty curve that nobody could ever verify by playing.</summary>
    public sealed class ShiftTests
    {
        private const int CampaignSeed = 20260813;

        [Test]
        public void TheQueueIsAlwaysLongerThanTheQuota()
        {
            for (var shift = 0; shift < 30; shift++)
            {
                var director = new ShiftDirector(CampaignSeed, shift);
                for (var i = 0; i < director.Quota; i++)
                {
                    Assert.IsFalse(director.IsFinished,
                        $"shift {shift} ran out of vehicles at {i} of a {director.Quota} quota");
                    director.Decide(Verdict.Pass);
                }
            }
        }

        [Test]
        public void TheQuotaRisesAcrossTheCampaign()
        {
            Assert.AreEqual(9, ShiftDirector.QuotaFor(0));
            Assert.AreEqual(20, ShiftDirector.QuotaFor(29));
            Assert.Less(ShiftDirector.QuotaFor(5), ShiftDirector.QuotaFor(25));
        }

        [Test]
        public void DecidingPastTheEndOfTheQueueIsRefused()
        {
            var director = new ShiftDirector(CampaignSeed, 0);
            while (!director.IsFinished)
            {
                director.Decide(Verdict.Pass);
            }

            Assert.Throws<InvalidOperationException>(() => director.Decide(Verdict.Pass));
        }

        /// <summary>A player who reads the manual and gets everything right must clear the
        /// quota with a positive wage on every single night. Turns red if the economy is
        /// ever tuned into a state where perfect play still loses.</summary>
        [Test]
        public void PerfectPlayIsPaidOnEveryNight()
        {
            for (var shift = 0; shift < 30; shift++)
            {
                var director = new ShiftDirector(CampaignSeed, shift);

                while (!director.IsFinished)
                {
                    var correct = RuleEvaluator.Evaluate(director.Current.Attributes, director.Manual);
                    director.Decide(correct.CorrectVerdict);
                }

                var report = director.BuildReport();

                Assert.AreEqual(report.Processed, report.Correct, $"shift {shift}: perfect play scored wrong");
                Assert.AreEqual(0, report.TotalSeverity, $"shift {shift}: perfect play took a penalty");
                Assert.IsTrue(report.QuotaMet, $"shift {shift}: perfect play missed the quota");
                Assert.Greater(report.NetPay, 0, $"shift {shift}: perfect play earned nothing");
            }
        }

        /// <summary>Waving everything through must be worse than reading the manual, on
        /// every night. Turns red if the difficulty curve ever makes reflexive PASS a
        /// viable strategy, which would make the whole game pointless.</summary>
        [Test]
        public void PassingEverythingIsAlwaysWorseThanPlayingProperly()
        {
            for (var shift = 0; shift < 30; shift++)
            {
                var lazy = new ShiftDirector(CampaignSeed, shift);
                while (!lazy.IsFinished)
                {
                    lazy.Decide(Verdict.Pass);
                }

                var careful = new ShiftDirector(CampaignSeed, shift);
                while (!careful.IsFinished)
                {
                    careful.Decide(RuleEvaluator.Evaluate(careful.Current.Attributes, careful.Manual).CorrectVerdict);
                }

                var lazyReport = lazy.BuildReport();
                var carefulReport = careful.BuildReport();

                Assert.Less(lazyReport.NetPay, carefulReport.NetPay,
                    $"shift {shift}: passing everything paid {lazyReport.NetPay}, " +
                    $"playing properly paid {carefulReport.NetPay}");
            }
        }

        /// <summary>Alarming everything must also lose. Together with the test above this
        /// closes both degenerate strategies: there is no single switch that works.</summary>
        [Test]
        public void AlarmingEverythingIsAlsoWorseThanPlayingProperly()
        {
            for (var shift = 0; shift < 30; shift += 3)
            {
                var paranoid = new ShiftDirector(CampaignSeed, shift);
                while (!paranoid.IsFinished)
                {
                    paranoid.Decide(Verdict.Alarm);
                }

                var careful = new ShiftDirector(CampaignSeed, shift);
                while (!careful.IsFinished)
                {
                    careful.Decide(RuleEvaluator.Evaluate(careful.Current.Attributes, careful.Manual).CorrectVerdict);
                }

                Assert.Less(paranoid.BuildReport().NetPay, careful.BuildReport().NetPay,
                    $"shift {shift}: alarming everything was not punished");
            }
        }

        /// <summary>Pay can reach zero but never goes negative, because a wage that eats
        /// into the player's savings would need a debt system the concept does not have.</summary>
        [Test]
        public void PayNeverGoesNegative()
        {
            for (var shift = 0; shift < 30; shift += 2)
            {
                var director = new ShiftDirector(CampaignSeed, shift);
                var flip = false;
                while (!director.IsFinished)
                {
                    flip = !flip;
                    director.Decide(flip ? Verdict.Alarm : Verdict.Pass);
                }

                Assert.GreaterOrEqual(director.BuildReport().NetPay, 0);
            }
        }

        /// <summary>The two kinds of mistake have to be distinguishable, because the
        /// endings are scored on which one the player makes more of.</summary>
        [Test]
        public void RefusingAHumanAndAdmittingANonHumanAreCountedSeparately()
        {
            var refusedSomeone = false;
            var admittedSomething = false;

            for (var shift = 0; shift < 30 && !(refusedSomeone && admittedSomething); shift++)
            {
                var director = new ShiftDirector(CampaignSeed, shift);
                while (!director.IsFinished)
                {
                    director.Decide(director.Position % 2 == 0 ? Verdict.Pass : Verdict.Alarm);
                }

                var report = director.BuildReport();
                refusedSomeone |= report.HumansRefused > 0;
                admittedSomething |= report.NonHumansAdmitted > 0;
            }

            Assert.IsTrue(refusedSomeone, "a careless campaign never refused a single person");
            Assert.IsTrue(admittedSomething, "a careless campaign never admitted a single non-human");
        }

        /// <summary>The self-check needs a specific subject on screen without deciding its
        /// way there, or every screenshot run would pollute the report.</summary>
        [Test]
        public void SkippingToASubjectDoesNotRecordDecisions()
        {
            var director = new ShiftDirector(CampaignSeed, 12);
            director.SkipTo(4);

            Assert.AreEqual(4, director.Position);
            Assert.IsEmpty(director.Decisions);
            Assert.AreEqual(new SubjectGenerator().Generate(CampaignSeed, 12, 4).Attributes,
                director.Current.Attributes);
        }

        [Test]
        public void TheSameSeedGivesTheSameNight()
        {
            var a = new ShiftDirector(CampaignSeed, 7);
            var b = new ShiftDirector(CampaignSeed, 7);

            while (!a.IsFinished)
            {
                Assert.AreEqual(a.Current.Attributes, b.Current.Attributes);
                a.Decide(Verdict.Hold);
                b.Decide(Verdict.Hold);
            }

            CollectionAssert.AreEqual(
                a.Decisions.Select(d => d.Correct).ToList(),
                b.Decisions.Select(d => d.Correct).ToList());
        }

        /// <summary>The paperwork on the desk always describes the vehicle at the window.
        /// Turns red if the presentation and the director ever get out of step, which would
        /// be invisible in a screenshot and fatal in play.</summary>
        [Test]
        public void ThePaperworkAlwaysDescribesTheCurrentSubject()
        {
            var director = new ShiftDirector(CampaignSeed, 16);

            while (!director.IsFinished)
            {
                var subject = director.Current.Attributes;
                StringAssert.Contains(subject.Name.ToUpperInvariant(), director.Permit.ToPrintedPage());
                StringAssert.Contains($"{subject.BlinkRatePerMinute}/MIN", director.Biometrics.ToPrintedPage());
                Assert.AreEqual(PortraitCode.FromSubject(subject), director.Cabin.Portrait,
                    "the cabin feed is showing a different bearer's file photograph");
                Assert.AreEqual(PortraitCode.AsObserved(subject), director.Cabin.Comparison,
                    "the cabin feed is showing a different bearer's face");
                director.Decide(Verdict.Pass);
            }
        }
    }
}
