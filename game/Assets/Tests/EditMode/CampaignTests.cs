using System;
using System.Linq;
using Monster.Rules;
using Monster.Shift;
using NUnit.Framework;

namespace Monster.Tests
{
    /// <summary>Tests for a whole run of nights, and for the delayed consequences that make
    /// the game withhold whether you were right.
    ///
    /// This is the concept's central design pillar and it is easy to break by accident: any
    /// number printed on the morning report that correlates with accuracy hands the answer
    /// straight back. These tests exist to catch that.</summary>
    public sealed class CampaignTests
    {
        private const int CampaignSeed = 20260813;

        private static Verdict Correct(ShiftDirector d) =>
            RuleEvaluator.Evaluate(d.Current.Attributes, d.Manual).CorrectVerdict;

        // ------------------------------------------------------- withholding the answer --

        /// <summary>The most important test here. Turns red if the morning report ever
        /// starts reporting accuracy, directly or through a number that tracks it.</summary>
        [Test]
        public void TheMorningReportNeverRevealsHowManyWereCorrect()
        {
            var careful = new Campaign(CampaignSeed);
            var careless = new Campaign(CampaignSeed);

            var carefulDirector = careful.BeginShift();
            while (!carefulDirector.IsFinished)
            {
                carefulDirector.Decide(Correct(carefulDirector));
            }

            var carelessDirector = careless.BeginShift();
            while (!carelessDirector.IsFinished)
            {
                carelessDirector.Decide(Verdict.Pass);
            }

            var good = careful.EndShift(carefulDirector);
            var bad = careless.EndShift(carelessDirector);

            Assert.AreEqual(good.Processed, bad.Processed);
            Assert.AreEqual(good.Wage, bad.Wage,
                "the wage differed on the first night, so it can be read as a score");
            Assert.AreEqual(good.Net, bad.Net,
                "the take-home pay differed on the first night, which tells the player " +
                "immediately how they did");
        }

        /// <summary>The office is slow, not blind. The difference has to show up
        /// eventually, or none of the player's care matters.</summary>
        [Test]
        public void ErrorsCatchUpWithYouLater()
        {
            var campaign = new Campaign(CampaignSeed);
            var deductionsByNight = new int[8];

            for (var night = 0; night < 8; night++)
            {
                var director = campaign.BeginShift();
                while (!director.IsFinished)
                {
                    director.Decide(Verdict.Pass);
                }

                deductionsByNight[night] = campaign.EndShift(director).Deductions;
            }

            for (var night = 0; night < ConsequenceWriter.DeductionDelay; night++)
            {
                Assert.AreEqual(0, deductionsByNight[night],
                    $"a deduction arrived on night {night + 1}, sooner than the office is meant to move");
            }

            Assert.Greater(deductionsByNight.Skip(ConsequenceWriter.DeductionDelay).Sum(), 0,
                "waving everything through for eight nights was never noticed");
        }

        /// <summary>Over a full campaign, reading the manual has to pay better than not.
        /// Per-night this is deliberately invisible; across thirty nights it must not be.</summary>
        [Test]
        public void CareIsPaidBetterThanCarelessnessAcrossACampaign()
        {
            var careful = Campaign.PlayOut(CampaignSeed, Correct);
            var waveThrough = Campaign.PlayOut(CampaignSeed, _ => Verdict.Pass);
            var alarmEverything = Campaign.PlayOut(CampaignSeed, _ => Verdict.Alarm);

            Assert.Greater(careful, waveThrough,
                $"perfect play earned {careful}, waving everything through earned {waveThrough}");
            Assert.Greater(careful, alarmEverything,
                $"perfect play earned {careful}, alarming everything earned {alarmEverything}");
        }

        [Test]
        public void APerfectCampaignIsNeverDocked()
        {
            var campaign = new Campaign(CampaignSeed);

            while (!campaign.IsOver)
            {
                var director = campaign.BeginShift();
                while (!director.IsFinished)
                {
                    director.Decide(Correct(director));
                }

                var statement = campaign.EndShift(director);
                Assert.AreEqual(0, statement.Deductions,
                    $"night {statement.ShiftIndex + 1} docked a player who got everything right");
            }

            Assert.Greater(campaign.Credits, 0);
        }

        [Test]
        public void PayNeverGoesNegative()
        {
            var campaign = new Campaign(CampaignSeed);
            var flip = false;

            while (!campaign.IsOver)
            {
                var director = campaign.BeginShift();
                while (!director.IsFinished)
                {
                    flip = !flip;
                    director.Decide(flip ? Verdict.Alarm : Verdict.Pass);
                }

                Assert.GreaterOrEqual(campaign.EndShift(director).Net, 0);
            }
        }

        // -------------------------------------------------------------------- the mail --

        /// <summary>Turns red if every envelope starts meaning something. A player who can
        /// treat mail as a scoreboard is being told the answer with extra steps.</summary>
        [Test]
        public void SomeMailRefersToNothingAtAll()
        {
            var campaign = new Campaign(CampaignSeed);

            while (!campaign.IsOver)
            {
                var director = campaign.BeginShift();
                while (!director.IsFinished)
                {
                    director.Decide(Correct(director));
                }

                campaign.EndShift(director);
            }

            var routine = campaign.Delivered.Count(n => n.Kind == NoticeKind.Routine);
            Assert.Greater(routine, 3,
                "a flawless campaign received almost no post, so any envelope at all is a verdict");
        }

        [Test]
        public void NoNoticeEverStatesWhetherADecisionWasRight()
        {
            var campaign = new Campaign(CampaignSeed);
            var flip = 0;

            while (!campaign.IsOver)
            {
                var director = campaign.BeginShift();
                while (!director.IsFinished)
                {
                    director.Decide((Verdict)(flip++ % 4));
                }

                campaign.EndShift(director);
            }

            var banned = new[] { "CORRECT", "INCORRECT", "WRONG", "RIGHT", "MISTAKE", "ERROR" };

            foreach (var notice in campaign.Delivered)
            {
                var text = (notice.Heading + " " + string.Join(" ", notice.Lines)).ToUpperInvariant();
                foreach (var word in banned)
                {
                    StringAssert.DoesNotContain(word, text,
                        $"a {notice.Kind} notice grades the player: \"{text}\"");
                }
            }
        }

        /// <summary>Every notice that is about a night names that night, so the player has
        /// something to reason from even though nothing is spelled out.</summary>
        [Test]
        public void NoticesAboutANightSayWhichNight()
        {
            var campaign = new Campaign(CampaignSeed);
            var flip = 0;

            while (!campaign.IsOver)
            {
                var director = campaign.BeginShift();
                while (!director.IsFinished)
                {
                    director.Decide((Verdict)(flip++ % 4));
                }

                campaign.EndShift(director);
            }

            var referring = campaign.Delivered.Where(n => n.RefersToShift >= 0).ToList();
            Assert.Greater(referring.Count, 10, "the campaign produced almost no consequences");

            foreach (var notice in referring)
            {
                var night = (notice.RefersToShift + 1).ToString();
                var text = string.Join(" ", notice.Lines);
                StringAssert.Contains(night, text,
                    $"a {notice.Kind} notice about night {night} does not say so: \"{text}\"");
            }
        }

        /// <summary>Admitting something that was not a person and refusing someone who was
        /// produce different post, because they are different mistakes.</summary>
        [Test]
        public void TheTwoKindsOfMistakeProduceDifferentPost()
        {
            var campaign = new Campaign(CampaignSeed);
            var flip = 0;

            while (!campaign.IsOver)
            {
                var director = campaign.BeginShift();
                while (!director.IsFinished)
                {
                    director.Decide(flip++ % 3 == 0 ? Verdict.Pass : Verdict.Alarm);
                }

                campaign.EndShift(director);
            }

            Assert.Greater(campaign.Delivered.Count(n => n.Kind == NoticeKind.Incident), 0,
                "nothing that got through was ever reported");
            Assert.Greater(campaign.Delivered.Count(n => n.Kind == NoticeKind.MissingPerson), 0,
                "nobody turned away was ever missed");
        }

        [Test]
        public void ACampaignReplaysIdenticallyFromItsSeed()
        {
            Assert.AreEqual(
                Campaign.PlayOut(CampaignSeed, Correct),
                Campaign.PlayOut(CampaignSeed, Correct));

            // Deliberately not compared across seeds. A flawless campaign earns the same
            // amount whatever the seed, because the wage is flat per vehicle and the queue
            // lengths are fixed — which is the point of the flat wage, not a bug. What
            // differs between seeds is who turns up and therefore what the post says.
            Assert.AreNotEqual(NoticeFingerprint(CampaignSeed), NoticeFingerprint(CampaignSeed + 1));
            Assert.AreEqual(NoticeFingerprint(CampaignSeed), NoticeFingerprint(CampaignSeed));
        }

        private static string NoticeFingerprint(int seed)
        {
            var campaign = new Campaign(seed);
            var flip = 0;

            while (!campaign.IsOver)
            {
                var director = campaign.BeginShift();
                while (!director.IsFinished)
                {
                    director.Decide((Verdict)(flip++ % 4));
                }

                campaign.EndShift(director);
            }

            return string.Join("|", campaign.Delivered.Select(n =>
                $"{n.Kind}:{n.ArrivesOnShift}:{string.Join(" ", n.Lines)}"));
        }

        /// <summary>BeginShift on the presenter takes a night and used to ignore it,
        /// because Campaign.BeginShift is sequential and a fresh campaign always started at
        /// night one. Nothing caught it until a screenshot of night twenty-two came back
        /// captioned NIGHT 1.</summary>
        [Test]
        public void SkippingForwardOpensTheNightItWasAskedFor()
        {
            var campaign = new Campaign(CampaignSeed);
            campaign.SkipToShift(21);

            var director = campaign.BeginShift();

            Assert.AreEqual(21, director.ShiftIndex);
            Assert.AreEqual(ShiftDirector.QuotaFor(21), director.Quota);
            Assert.Greater(director.Manual.Superseded.Count, 0,
                "night twenty-two's binder holds no amendments, so it is not a late binder");
        }

        [Test]
        public void SkippingOutsideTheCampaignIsRefused()
        {
            var campaign = new Campaign(CampaignSeed);

            Assert.Throws<ArgumentOutOfRangeException>(() => campaign.SkipToShift(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => campaign.SkipToShift(Campaign.TotalShifts));
        }

        [Test]
        public void TheCampaignRefusesToRunPastItsLastNight()
        {
            var campaign = new Campaign(CampaignSeed);

            while (!campaign.IsOver)
            {
                var director = campaign.BeginShift();
                while (!director.IsFinished)
                {
                    director.Decide(Verdict.Pass);
                }

                campaign.EndShift(director);
            }

            Assert.AreEqual(Campaign.TotalShifts, campaign.Completed.Count);
            Assert.Throws<InvalidOperationException>(() => campaign.BeginShift());
        }
    }
}
