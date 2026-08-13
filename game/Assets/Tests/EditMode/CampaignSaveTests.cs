using System;
using System.Linq;
using Monster.Rules;
using Monster.Shift;
using NUnit.Framework;

namespace Monster.Tests
{
    /// <summary>Tests for saving and resuming a campaign.
    ///
    /// A thirty-night campaign that cannot be resumed makes the length theoretical, so this
    /// is load-bearing rather than a convenience. The save holds only the seed and the
    /// verdicts and everything else is replayed, which means the thing worth testing is not
    /// whether the file round-trips but whether a resumed campaign is genuinely the same
    /// campaign — including the notices that were queued nights ago and have not arrived
    /// yet.</summary>
    public sealed class CampaignSaveTests
    {
        private const int CampaignSeed = 20260813;

        private static Verdict Correct(ShiftDirector d) =>
            RuleEvaluator.Evaluate(d.Current.Attributes, d.Manual).CorrectVerdict;

        private static Campaign PlayNights(int nights, Func<int, ShiftDirector, Verdict> policy)
        {
            var campaign = new Campaign(CampaignSeed);

            for (var night = 0; night < nights; night++)
            {
                var director = campaign.BeginShift();
                while (!director.IsFinished)
                {
                    var verdict = policy(night, director);
                    campaign.Record(verdict);
                    director.Decide(verdict);
                }

                campaign.EndShift(director);
            }

            return campaign;
        }

        [Test]
        public void ASaveRoundTripsThroughJson()
        {
            var save = PlayNights(3, (_, d) => Correct(d)).ToSave();
            var restored = CampaignSave.FromJson(save.ToJson());

            Assert.AreEqual(save.seed, restored.seed);
            Assert.AreEqual(save.nights.Count, restored.nights.Count);

            for (var i = 0; i < save.nights.Count; i++)
            {
                CollectionAssert.AreEqual(save.nights[i].verdicts, restored.nights[i].verdicts);
            }
        }

        [Test]
        public void AResumedCampaignHasTheSameMoneyAndTheSamePostBehindIt()
        {
            var original = PlayNights(7, (night, d) => night % 2 == 0 ? Correct(d) : Verdict.Pass);
            var resumed = Campaign.Restore(CampaignSave.FromJson(original.ToSave().ToJson()), out var open);

            Assert.IsNull(open, "a save taken on a night boundary should not resume mid-shift");
            Assert.AreEqual(original.Credits, resumed.Credits);
            Assert.AreEqual(original.ShiftIndex, resumed.ShiftIndex);
            CollectionAssert.AreEqual(
                original.Delivered.Select(n => n.Heading + string.Join("", n.Lines)),
                resumed.Delivered.Select(n => n.Heading + string.Join("", n.Lines)));
        }

        /// <summary>The one that matters. Consequences are queued by a night and delivered
        /// several nights later, so a save that only restored visible state would resume
        /// into a campaign with no future in it — the player would walk away from every
        /// mistake they had already made.</summary>
        [Test]
        public void ConsequencesQueuedBeforeTheSaveStillArriveAfterIt()
        {
            var original = PlayNights(2, (_, __) => Verdict.Pass);
            var resumed = Campaign.Restore(original.ToSave(), out _);

            var originalFuture = new int[6];
            var resumedFuture = new int[6];

            for (var i = 0; i < 6; i++)
            {
                originalFuture[i] = PlayOneMoreNight(original);
                resumedFuture[i] = PlayOneMoreNight(resumed);
            }

            CollectionAssert.AreEqual(originalFuture, resumedFuture);
            Assert.Greater(originalFuture.Sum(), 0,
                "two nights of waving everything through produced no later deductions at all, " +
                "so this test is not measuring anything");
        }

        private static int PlayOneMoreNight(Campaign campaign)
        {
            var director = campaign.BeginShift();
            while (!director.IsFinished)
            {
                campaign.Record(Verdict.Refer);
                director.Decide(Verdict.Refer);
            }

            return campaign.EndShift(director).Deductions;
        }

        [Test]
        public void QuittingMidShiftDoesNotThrowTheEveningAway()
        {
            var campaign = new Campaign(CampaignSeed);
            var director = campaign.BeginShift();

            for (var i = 0; i < 5; i++)
            {
                campaign.Record(Verdict.Pass);
                director.Decide(Verdict.Pass);
            }

            var resumed = Campaign.Restore(campaign.ToSave(), out var open);

            Assert.IsNotNull(open, "a save taken five vehicles into a shift resumed on a boundary");
            Assert.AreEqual(5, open.Position, "the resumed night did not pick up where it left off");
            Assert.AreEqual(0, resumed.ShiftIndex);
            Assert.AreEqual(0, resumed.Credits, "an unfinished night must not have been paid out");
        }

        [Test]
        public void AnUnfinishedNightIsNotPaidTwice()
        {
            var campaign = new Campaign(CampaignSeed);
            var director = campaign.BeginShift();

            for (var i = 0; i < 4; i++)
            {
                campaign.Record(Verdict.Pass);
                director.Decide(Verdict.Pass);
            }

            var resumed = Campaign.Restore(campaign.ToSave(), out var open);

            while (!open.IsFinished)
            {
                resumed.Record(Verdict.Pass);
                open.Decide(Verdict.Pass);
            }

            var statement = resumed.EndShift(open);

            Assert.AreEqual(1, resumed.Completed.Count);
            Assert.AreEqual(statement.Net, resumed.Credits);
        }

        // ------------------------------------------------------------- refusing rubbish --

        [Test]
        public void AnEmptyOrMalformedSaveIsRejected()
        {
            Assert.Throws<ArgumentException>(() => CampaignSave.FromJson(""));
            Assert.Throws<FormatException>(() => CampaignSave.FromJson("{\"version\":99}"));
        }

        /// <summary>A save written before a change to queue lengths would replay into a
        /// different game. Failing loudly beats silently resuming somebody else's
        /// campaign.</summary>
        [Test]
        public void ASaveThatNoLongerMatchesItsSeedIsRefused()
        {
            var save = PlayNights(2, (_, d) => Correct(d)).ToSave();
            save.nights[0].verdicts.Add((int)Verdict.Pass);

            Assert.IsFalse(save.IsConsistent(out var problem));
            StringAssert.Contains("night 1", problem);
            Assert.Throws<InvalidOperationException>(() => Campaign.Restore(save, out _));
        }

        [Test]
        public void AVerdictThisBuildDoesNotRecogniseIsRefused()
        {
            var save = PlayNights(1, (_, d) => Correct(d)).ToSave();
            save.nights[0].verdicts[0] = 99;

            Assert.IsFalse(save.IsConsistent(out var problem));
            StringAssert.Contains("does not recognise", problem);
        }

        [Test]
        public void ASaveIsSmall()
        {
            var save = PlayNights(Campaign.TotalShifts, (_, d) => Correct(d)).ToSave();
            var json = save.ToJson();

            Assert.Less(json.Length, 24000,
                $"a full campaign serialised to {json.Length} characters; the point of replaying " +
                "from verdicts is that a save stays a few hundred small integers");
            Assert.AreEqual(Campaign.TotalShifts, save.nights.Count);
        }
    }
}
