using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Monster.Rules
{
    public enum Question
    {
        District,
        Purpose,
        IssuingOffice,
        Destination,
    }

    /// <summary>What came back over the intercom.</summary>
    public readonly struct Reply
    {
        public Reply(Question question, string prompt, string answer, float delaySeconds, bool layered)
        {
            Question = question;
            Prompt = prompt;
            Answer = answer;
            DelaySeconds = delaySeconds;
            Layered = layered;
        }

        public Question Question { get; }
        public string Prompt { get; }
        public string Answer { get; }

        /// <summary>How long the subject took to begin answering. Printed on the strip as
        /// well as waited out in real time, so nobody has to count in their head.</summary>
        public float DelaySeconds { get; }

        /// <summary>Whether a second voice is audible underneath the first.</summary>
        public bool Layered { get; }
    }

    /// <summary>The questions the operator can put through the glass, and what comes back.
    ///
    /// This exists because two of the manual's criteria — the pause before an answer and a
    /// second voice under the first — were being printed on a monitor as finished readings.
    /// That made them free. Asking now costs time the player does not have much of, which
    /// turns "check everything" into a decision rather than a routine, and it is the only
    /// place in the game where the subject is a person rather than a form.</summary>
    public static class Interrogation
    {
        private static readonly string[] Purposes =
        {
            "DELIVERY OF GOODS", "FAMILY VISIT", "MEDICAL APPOINTMENT", "RETURN TO RESIDENCE",
            "CONTRACT LABOUR", "FUNERAL", "COLLECTION OF EFFECTS",
        };

        private static readonly string[] Destinations =
        {
            "CENTRAL DEPOT", "THE COAST ROAD", "MILE 40", "THE OLD QUARTER",
            "STENNMARK YARDS", "NO FIXED POINT",
        };

        public static string PromptFor(Question question) => question switch
        {
            Question.District => "STATE YOUR DISTRICT",
            Question.Purpose => "PURPOSE OF TRANSIT",
            Question.IssuingOffice => "WHO ISSUED THIS PERMIT",
            Question.Destination => "WHERE ARE YOU GOING",
            _ => throw new ArgumentOutOfRangeException(nameof(question), question, null),
        };

        /// <summary>The four letters engraved on the key that asks it. Used where a whole
        /// prompt will not fit, which is the transcript of everything asked so far.</summary>
        public static string TagFor(Question question) => question switch
        {
            Question.District => "DIST",
            Question.Purpose => "PURP",
            Question.IssuingOffice => "OFFC",
            Question.Destination => "DEST",
            _ => throw new ArgumentOutOfRangeException(nameof(question), question, null),
        };

        public static Reply Ask(in SubjectAttributes subject, Question question)
        {
            var answer = question switch
            {
                // The one answer that can contradict the paperwork. A bearer who names a
                // district other than the one printed on their permit is criterion C-17,
                // and this is the only place that discrepancy can be found.
                Question.District => (subject.SpokenDistrictMatchesPermit
                    ? subject.OriginDistrict
                    : DisagreeingDistrict(subject)).ToUpperInvariant(),
                Question.Purpose => Pick(Purposes, subject, 7717),
                Question.IssuingOffice => (subject.IssuingOffice ?? string.Empty).ToUpperInvariant(),
                Question.Destination => Pick(Destinations, subject, 4451),
                _ => "…",
            };

            return new Reply(question, PromptFor(question), answer,
                subject.ResponseDelaySeconds, subject.SecondVoiceUnderTheFirst);
        }

        /// <summary>A district that is not the one on the permit, chosen deterministically
        /// so the same subject always misspeaks the same way.</summary>
        private static string DisagreeingDistrict(in SubjectAttributes subject)
        {
            var options = new[]
            {
                "Lowbank", "Harrow", "Stennmark", "Coldwater", "Fenholt", "Marrowgate",
                "Vessel", "Ashgate",
            };

            var hash = Hash(subject, 9091);
            for (var i = 0; i < options.Length; i++)
            {
                var candidate = options[(int)((hash + (uint)i) % options.Length)];
                if (!string.Equals(candidate, subject.OriginDistrict, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            return "UNSTATED";
        }

        private static string Pick(IReadOnlyList<string> pool, in SubjectAttributes subject, uint salt) =>
            pool[(int)(Hash(subject, salt) % (uint)pool.Count)];

        private static uint Hash(in SubjectAttributes subject, uint salt)
        {
            unchecked
            {
                var hash = 2166136261u ^ salt;
                foreach (var c in subject.Name ?? string.Empty)
                {
                    hash = (hash ^ c) * 16777619u;
                }

                foreach (var c in subject.PermitSerial ?? string.Empty)
                {
                    hash = (hash ^ c) * 16777619u;
                }

                return hash;
            }
        }

        /// <summary>The intercom trace, drawn as two rows of block characters.
        ///
        /// A second voice under the first is an audio tell, and audio alone cannot be
        /// verified on a machine with no sound card — nor, more importantly, by a player
        /// with the volume down. Drawing the trace makes the same information available to
        /// the eye without making it any less of a thing you have to look for.</summary>
        public static string VoiceTrace(in Reply reply, int width = 22)
        {
            const string ramp = "\u2581\u2582\u2583\u2584\u2585\u2586\u2587\u2588";

            var primary = new StringBuilder(width);
            var secondary = new StringBuilder(width);
            var hash = 2166136261u ^ (uint)reply.Answer.GetHashCode();

            for (var i = 0; i < width; i++)
            {
                unchecked
                {
                    hash = hash * 16777619u + 2654435761u;
                }

                var level = (int)((hash >> 13) % 8u);
                primary.Append(ramp[level]);

                if (!reply.Layered)
                {
                    secondary.Append(' ');
                    continue;
                }

                // The second voice is quieter and out of step with the first, which is what
                // makes it findable without being obvious.
                var offset = (int)((hash >> 21) % 4u);
                secondary.Append(ramp[Math.Clamp(level - 3 + offset, 0, 4)]);
            }

            return primary + "\n" + secondary;
        }

        public static string FormatDelay(float seconds) =>
            seconds.ToString("F1", CultureInfo.InvariantCulture) + "S";
    }
}
