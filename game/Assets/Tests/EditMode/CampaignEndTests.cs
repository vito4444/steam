using System.Linq;
using Monster.Rules;
using Monster.Shift;
using NUnit.Framework;

namespace Monster.Tests
{
    /// <summary>Tests for how a run finishes.
    ///
    /// Until now night thirty ended and the thirty-first simply did not start: the booth
    /// sat there with a stale morning report on the desk and no way forward. A campaign
    /// with no ending makes its own length meaningless in the same way one that cannot be
    /// resumed does.
    ///
    /// The ending has to close the run without breaking the rule the whole game is built
    /// on, which is that the player is not told how they did.</summary>
    public sealed class CampaignEndTests
    {
        private const int CampaignSeed = 20260813;

        private static Campaign PlayWholeCampaign(System.Func<ShiftDirector, Verdict> policy)
        {
            var campaign = new Campaign(CampaignSeed);

            while (!campaign.IsOver)
            {
                var director = campaign.BeginShift();
                while (!director.IsFinished)
                {
                    var verdict = policy(director);
                    campaign.Record(verdict);
                    director.Decide(verdict);
                }

                campaign.EndShift(director);
            }

            return campaign;
        }

        private static Verdict Correct(ShiftDirector d) =>
            RuleEvaluator.Evaluate(d.Current.Attributes, d.Manual).CorrectVerdict;

        [Test]
        public void ARunThatEndsProducesALetter()
        {
            var notice = PlayWholeCampaign(Correct).FinalNotice();

            Assert.IsNotEmpty(notice.Heading);
            Assert.Greater(notice.Lines.Count, 1);
        }

        /// <summary>Careful and careless runs must end differently, or the thirty nights
        /// did not matter.</summary>
        [Test]
        public void HowTheRunWentChangesHowItEnds()
        {
            var careful = PlayWholeCampaign(Correct).FinalNotice();
            var careless = PlayWholeCampaign(_ => Verdict.Pass).FinalNotice();

            Assert.AreNotEqual(careful.Heading, careless.Heading,
                $"both a flawless run and one spent waving everything through ended with " +
                $"\"{careful.Heading}\"");
        }

        /// <summary>The pillar, at the last possible moment. An ending that grades the
        /// player undoes thirty nights of not telling them.</summary>
        [Test]
        public void TheEndingStillDoesNotSayWhetherYouWereRight()
        {
            var endings = new[]
            {
                PlayWholeCampaign(Correct).FinalNotice(),
                PlayWholeCampaign(_ => Verdict.Pass).FinalNotice(),
                PlayWholeCampaign(_ => Verdict.Alarm).FinalNotice(),
                PlayWholeCampaign(d => d.Position % 2 == 0 ? Verdict.Hold : Verdict.Refer).FinalNotice(),
            };

            var banned = new[]
            {
                "CORRECT", "INCORRECT", "WRONG", "RIGHT", "MISTAKE", "ERROR",
                "WELL DONE", "FAILED", "SCORE", "ACCURACY",
            };

            foreach (var ending in endings)
            {
                var text = (ending.Heading + " " + string.Join(" ", ending.Lines)).ToUpperInvariant();

                foreach (var word in banned)
                {
                    StringAssert.DoesNotContain(word, text, $"the ending grades the player: \"{text}\"");
                }
            }
        }

        [Test]
        public void TheEndingIsDeterministic()
        {
            Assert.AreEqual(
                string.Join("|", PlayWholeCampaign(Correct).FinalNotice().Lines),
                string.Join("|", PlayWholeCampaign(Correct).FinalNotice().Lines));
        }

        /// <summary>A run that never finished has no ending to give, and asking for one
        /// early must not invent a verdict on nights that have not happened.</summary>
        [Test]
        public void AnUnfinishedRunIsJudgedOnlyOnTheNightsItPlayed()
        {
            var campaign = new Campaign(CampaignSeed);

            for (var night = 0; night < 3; night++)
            {
                var director = campaign.BeginShift();
                while (!director.IsFinished)
                {
                    campaign.Record(Verdict.Pass);
                    director.Decide(Verdict.Pass);
                }

                campaign.EndShift(director);
            }

            Assert.IsFalse(campaign.IsOver);
            Assert.IsNotEmpty(campaign.FinalNotice().Heading);
        }

        /// <summary>Three nights of waving everything through is not yet enough deduction
        /// to look bad, because the office is four nights slow. The ending must be read
        /// from what actually arrived rather than from what is owed.</summary>
        [Test]
        public void TheEndingReadsTheDeductionsThatArrivedNotTheOnesStillInThePost()
        {
            var campaign = new Campaign(CampaignSeed);
            var delivered = 0;

            while (!campaign.IsOver)
            {
                var director = campaign.BeginShift();
                while (!director.IsFinished)
                {
                    campaign.Record(Verdict.Pass);
                    director.Decide(Verdict.Pass);
                }

                delivered += campaign.EndShift(director).Deductions;
            }

            Assert.AreEqual(delivered, campaign.Delivered.Sum(n => n.Deduction),
                "the deductions the ending reads are not the ones the player was actually shown");
            Assert.Greater(delivered, 0);
        }
    }
}
