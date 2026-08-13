using System;
using System.Linq;
using Monster.Rules;
using NUnit.Framework;

namespace Monster.Tests
{
    /// <summary>Tests for putting a question through the intercom.
    ///
    /// The interrogation exists to stop two of the manual's criteria being free. Before it,
    /// the pause before an answer and a second voice under the first were printed on a
    /// monitor as finished readings; now they cost a question, and a question costs time
    /// the player does not have much of. These tests guard that the information is
    /// genuinely there to be found and that the answers stay consistent with the
    /// paperwork.</summary>
    public sealed class InterrogationTests
    {
        private const int CampaignSeed = 20260813;

        [Test]
        public void EveryQuestionHasAPromptAndAnAnswer()
        {
            var subject = new SubjectGenerator().Generate(CampaignSeed, 9, 2).Attributes;

            foreach (Question question in Enum.GetValues(typeof(Question)))
            {
                var reply = Interrogation.Ask(subject, question);

                Assert.IsNotEmpty(reply.Prompt, $"{question} has no prompt");
                Assert.IsNotEmpty(reply.Answer, $"{question} produced an empty answer");
                StringAssert.DoesNotContain("…", reply.Answer, $"{question} fell through to a placeholder");
            }
        }

        [Test]
        public void AskingIsDeterministic()
        {
            var subject = new SubjectGenerator().Generate(CampaignSeed, 3, 8).Attributes;

            foreach (Question question in Enum.GetValues(typeof(Question)))
            {
                Assert.AreEqual(Interrogation.Ask(subject, question).Answer,
                    Interrogation.Ask(subject, question).Answer);
            }
        }

        /// <summary>The reply carries the pause and the layering, because those are what
        /// the manual tests and there is nowhere else to read them any more.</summary>
        [Test]
        public void TheReplyCarriesThePauseAndTheLayering()
        {
            var subject = new SubjectGenerator().Generate(CampaignSeed, 3, 8).Attributes;
            subject.ResponseDelaySeconds = 4.3f;
            subject.SecondVoiceUnderTheFirst = true;

            var reply = Interrogation.Ask(subject, Question.Purpose);

            Assert.AreEqual(4.3f, reply.DelaySeconds, 0.001f);
            Assert.IsTrue(reply.Layered);
            Assert.AreEqual("4.3S", Interrogation.FormatDelay(reply.DelaySeconds));
        }

        /// <summary>The whole point of C-17. Turns red if the district answer stops
        /// tracking the attribute, which would make the criterion undiscoverable.</summary>
        [Test]
        public void TheDistrictAnswerAgreesWithThePermitUnlessItIsMeantNotTo()
        {
            var generator = new SubjectGenerator();
            var disagreements = 0;

            for (var i = 0; i < 4000; i++)
            {
                var subject = generator.Generate(CampaignSeed, i % 30, i).Attributes;
                var spoken = Interrogation.Ask(subject, Question.District).Answer;
                var printed = subject.OriginDistrict.ToUpperInvariant();

                if (subject.SpokenDistrictMatchesPermit)
                {
                    Assert.AreEqual(printed, spoken,
                        "a bearer with nothing to hide named a district other than their own");
                }
                else
                {
                    disagreements++;
                    Assert.AreNotEqual(printed, spoken,
                        "a bearer meant to contradict their permit named the district on it");
                }
            }

            Assert.Greater(disagreements, 30, "the sample should contain plenty of contradictions");
        }

        [Test]
        public void TheOfficeAnswerAlwaysMatchesThePermit()
        {
            var generator = new SubjectGenerator();

            for (var i = 0; i < 500; i++)
            {
                var subject = generator.Generate(CampaignSeed, i % 30, i).Attributes;
                Assert.AreEqual(subject.IssuingOffice.ToUpperInvariant(),
                    Interrogation.Ask(subject, Question.IssuingOffice).Answer);
            }
        }

        // ------------------------------------------------------------------ voice trace --

        /// <summary>The layered voice is an audio tell, and audio cannot be verified on a
        /// machine with no sound card — nor by a player with the volume down. The trace has
        /// to carry it visually.</summary>
        [Test]
        public void TheTraceShowsASecondRowOnlyWhenThereIsASecondVoice()
        {
            var subject = new SubjectGenerator().Generate(CampaignSeed, 5, 5).Attributes;

            subject.SecondVoiceUnderTheFirst = false;
            var single = Interrogation.VoiceTrace(Interrogation.Ask(subject, Question.Purpose)).Split('\n');
            Assert.AreEqual(2, single.Length);
            Assert.IsTrue(single[1].All(char.IsWhiteSpace), "a single voice drew a second trace");

            subject.SecondVoiceUnderTheFirst = true;
            var layered = Interrogation.VoiceTrace(Interrogation.Ask(subject, Question.Purpose)).Split('\n');
            Assert.IsFalse(layered[1].All(char.IsWhiteSpace), "a layered voice drew no second trace");
        }

        [Test]
        public void TheTraceIsTheRequestedWidthAndDrawnFromTheBlockRamp()
        {
            const string ramp = "\u2581\u2582\u2583\u2584\u2585\u2586\u2587\u2588";
            var subject = new SubjectGenerator().Generate(CampaignSeed, 5, 5).Attributes;
            subject.SecondVoiceUnderTheFirst = true;

            var rows = Interrogation.VoiceTrace(Interrogation.Ask(subject, Question.District), 18).Split('\n');

            foreach (var row in rows)
            {
                Assert.AreEqual(18, row.Length);
                foreach (var c in row)
                {
                    Assert.IsTrue(c == ' ' || ramp.Contains(c),
                        $"trace contains '{c}', which the booth typeface may not carry");
                }
            }
        }

        [Test]
        public void TheTraceIsNotFlat()
        {
            var subject = new SubjectGenerator().Generate(CampaignSeed, 5, 5).Attributes;
            var row = Interrogation.VoiceTrace(Interrogation.Ask(subject, Question.Purpose)).Split('\n')[0];

            Assert.Greater(row.Distinct().Count(), 3, "the primary trace is nearly a flat line");
        }

        /// <summary>Questions cost nine minutes each and their answers used to be the least
        /// durable thing on the desk: one reply replaced the last, so a player who spent
        /// three of them could read one answer and had to hold the other two in their head.
        /// </summary>
        [Test]
        public void TheIntercomKeepsEverythingThisBearerHasBeenAsked()
        {
            var subject = new SubjectGenerator().Generate(CampaignSeed, 3, 0).Attributes;

            var replies = new[]
            {
                Interrogation.Ask(subject, Question.District),
                Interrogation.Ask(subject, Question.Purpose),
                Interrogation.Ask(subject, Question.Destination),
            };

            var page = DocumentBuilder.IntercomReply(replies).ToPrintedPage();

            foreach (var reply in replies)
            {
                StringAssert.Contains(Interrogation.TagFor(reply.Question), page,
                    $"the transcript dropped the {reply.Question} question");
                StringAssert.Contains(reply.Answer, page,
                    $"the transcript dropped what the bearer said about {reply.Question}");
            }
        }

        [Test]
        public void EveryQuestionHasAKeyTagAndTheyAreAllDifferent()
        {
            var tags = System.Enum.GetValues(typeof(Question))
                .Cast<Question>()
                .Select(Interrogation.TagFor)
                .ToList();

            CollectionAssert.AllItemsAreUnique(tags);
            Assert.IsTrue(tags.All(t => t.Length == 4), "a tag does not fit on a key face");
        }

        [Test]
        public void AnEmptyTranscriptIsTheIdleChannel()
        {
            Assert.AreEqual(
                DocumentBuilder.IntercomIdle().ToPrintedPage(),
                DocumentBuilder.IntercomReply(System.Array.Empty<Reply>()).ToPrintedPage());
        }

        /// <summary>The trace belongs to the newest reply. It is a thing you watch rather
        /// than read, and a stack of four of them would be noise.</summary>
        [Test]
        public void TheVoiceTraceFollowsTheMostRecentReply()
        {
            var subject = new SubjectGenerator().Generate(CampaignSeed, 3, 1).Attributes;

            var first = Interrogation.Ask(subject, Question.District);
            var second = Interrogation.Ask(subject, Question.Destination);

            Assert.AreEqual(Interrogation.VoiceTrace(second, 20),
                DocumentBuilder.IntercomReply(new[] { first, second }).Footer);
        }

    }
}
