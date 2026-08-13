using Monster.Rules;
using Monster.Shift;
using NUnit.Framework;

namespace Monster.Tests
{
    /// <summary>Tests for the clock.
    ///
    /// The clock exists for one reason: to make asking a question cost something. Without
    /// it the interrogation is four keys the player presses on every vehicle because there
    /// is no reason not to, and moving the pause and the layered voice off the monitors
    /// bought nothing.
    ///
    /// So what these tests actually check is the shape of the squeeze — that an early night
    /// has room to interrogate freely and a late one does not — because that is the design
    /// claim, and it is a claim about arithmetic that will quietly stop being true the first
    /// time anyone edits the quota curve.</summary>
    public sealed class ShiftClockTests
    {
        private const int CampaignSeed = 20260813;

        private static Verdict Correct(ShiftDirector d) =>
            RuleEvaluator.Evaluate(d.Current.Attributes, d.Manual).CorrectVerdict;

        [Test]
        public void ANightStartsAtTwentyTwoHundredWithTheWholeNightLeft()
        {
            var director = new ShiftDirector(CampaignSeed, 0);

            Assert.AreEqual("22:00", director.TimeOfDay);
            Assert.AreEqual(ShiftDirector.MinutesPerNight, director.MinutesRemaining);
            Assert.IsFalse(director.IsOutOfTime);
        }

        [Test]
        public void EachVehicleAdvancesTheClock()
        {
            var director = new ShiftDirector(CampaignSeed, 0);
            director.Decide(Correct(director));

            Assert.AreEqual(ShiftDirector.MinutesPerVehicle, director.MinutesSpent);
            Assert.AreEqual("22:18", director.TimeOfDay);
        }

        [Test]
        public void TheClockWrapsPastMidnight()
        {
            var director = new ShiftDirector(CampaignSeed, 0);
            Assert.IsTrue(director.Spend(150));

            Assert.AreEqual("00:30", director.TimeOfDay);
        }

        /// <summary>The whole queue has to fit on every night, or a player doing everything
        /// right and asking nothing would still be cut off, and the clock would be a
        /// punishment rather than a budget.</summary>
        [Test]
        public void EveryNightHasTimeForItsWholeQueueWithNoQuestions()
        {
            for (var night = 0; night < Campaign.TotalShifts; night++)
            {
                var director = new ShiftDirector(CampaignSeed, night);
                var queued = director.QueueLength;

                while (!director.IsFinished)
                {
                    director.Decide(Correct(director));
                }

                Assert.AreEqual(queued, director.Decisions.Count,
                    $"night {night + 1} ran out of clock after {director.Decisions.Count} of " +
                    $"{queued} vehicles");
            }
        }

        /// <summary>The design claim, stated as arithmetic so it cannot rot silently: the
        /// first night has room to interrogate almost freely and the last has room for
        /// barely a question.</summary>
        [Test]
        public void TheSqueezeTightensAcrossTheCampaign()
        {
            var first = QuestionsAffordableOn(0);
            var last = QuestionsAffordableOn(Campaign.TotalShifts - 1);

            Assert.GreaterOrEqual(first, 20,
                $"night one affords only {first} questions across its whole queue, which is not " +
                "enough room to learn what the keys are for");
            Assert.LessOrEqual(last, 3,
                $"the last night affords {last} questions, so the clock never actually bites");
            Assert.GreaterOrEqual(last, 1,
                "the last night affords no questions at all, which makes the intercom useless " +
                "rather than expensive");
        }

        private static int QuestionsAffordableOn(int night)
        {
            var director = new ShiftDirector(CampaignSeed, night);
            var slack = ShiftDirector.MinutesPerNight
                        - director.QueueLength * ShiftDirector.MinutesPerVehicle;

            return slack / ShiftDirector.MinutesPerQuestion;
        }

        /// <summary>A player who interrogates everybody on a late night should be cut off
        /// mid-queue. If they are not, the clock is decorative.</summary>
        [Test]
        public void InterrogatingEverybodyOnALateNightCostsYouVehicles()
        {
            var unhurried = new ShiftDirector(CampaignSeed, Campaign.TotalShifts - 1);
            while (!unhurried.IsFinished)
            {
                unhurried.Decide(Correct(unhurried));
            }

            var thorough = new ShiftDirector(CampaignSeed, Campaign.TotalShifts - 1);
            while (!thorough.IsFinished)
            {
                for (var q = 0; q < 4; q++)
                {
                    thorough.Spend(ShiftDirector.MinutesPerQuestion);
                }

                // Spending can itself run the night out, which is the point.
                if (thorough.IsFinished)
                {
                    break;
                }

                thorough.Decide(Correct(thorough));
            }

            Assert.Less(thorough.Decisions.Count, unhurried.Decisions.Count,
                "asking four questions of every bearer on the last night cost nothing");
        }

        [Test]
        public void TheClockRefusesToOverspend()
        {
            var director = new ShiftDirector(CampaignSeed, 0);

            Assert.IsTrue(director.Spend(ShiftDirector.MinutesPerNight - 1));
            Assert.IsFalse(director.Spend(ShiftDirector.MinutesPerQuestion),
                "a question was charged for with no time left to charge it against");
            Assert.IsTrue(director.IsOutOfTime);
            Assert.IsTrue(director.IsFinished);
        }

        [Test]
        public void RunningOutOfTimeEndsTheNightAndCostsTheQuota()
        {
            var campaign = new Campaign(CampaignSeed);
            var director = campaign.BeginShift();

            // Burn the night down to exactly one vehicle's worth of clock, then use it.
            Assert.IsTrue(director.Spend(ShiftDirector.MinutesPerNight - ShiftDirector.MinutesPerVehicle));
            Assert.IsFalse(director.IsFinished, "one vehicle's worth of clock should still be usable");

            campaign.Record(Verdict.Pass);
            director.Decide(Verdict.Pass);

            Assert.IsTrue(director.IsFinished);

            var statement = campaign.EndShift(director);

            Assert.AreEqual(1, statement.Processed);
            Assert.IsFalse(statement.QuotaMet);
            Assert.Greater(statement.Shortfall, 0, "missing the quota by eight vehicles cost nothing");
        }
    }
}
