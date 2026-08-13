using System;
using System.Collections.Generic;
using System.Globalization;
using Monster.Rules;

namespace Monster.Shift
{
    public enum NoticeKind
    {
        /// <summary>Something got through. Reported as an incident, never as your fault.</summary>
        Incident,

        /// <summary>Somebody you turned back has not been seen since.</summary>
        MissingPerson,

        /// <summary>The office noticed. Money comes off the wage.</summary>
        Deduction,

        /// <summary>The office approved. Rare, and worth less than it feels like.</summary>
        Commendation,

        /// <summary>Bureaucracy. Means nothing and is meant to mean nothing.</summary>
        Routine,
    }

    /// <summary>A piece of paper that arrives through the mail slot.
    ///
    /// The design pillar this exists for: the game mostly does not tell the player whether
    /// they were right. A notice reports something that happened, days later, and leaves
    /// the inference to them. Some notices refer to nothing at all, so a player cannot
    /// treat every envelope as a verdict — which is the only thing that keeps the ones that
    /// do mean something from being a scoreboard.</summary>
    public sealed class Notice
    {
        public Notice(NoticeKind kind, int arrivesOnShift, int refersToShift, string heading,
            IReadOnlyList<string> lines, int deduction = 0)
        {
            Kind = kind;
            ArrivesOnShift = arrivesOnShift;
            RefersToShift = refersToShift;
            Heading = heading;
            Lines = lines;
            Deduction = deduction;
        }

        public NoticeKind Kind { get; }
        public int ArrivesOnShift { get; }

        /// <summary>The night it is about, or -1 for the ones that are about nothing.</summary>
        public int RefersToShift { get; }

        public string Heading { get; }
        public IReadOnlyList<string> Lines { get; }
        public int Deduction { get; }
    }

    /// <summary>Turns what the player did into what the world says about it, later.</summary>
    public static class ConsequenceWriter
    {
        /// <summary>How many nights pass before the world reacts. Long enough that the
        /// player has stopped thinking about that vehicle.</summary>
        public const int IncidentDelay = 3;
        public const int MissingPersonDelay = 2;
        public const int DeductionDelay = 4;
        public const int CommendationDelay = 5;

        public const int DeductionPerError = 9;

        private static readonly string[] Places =
        {
            "MILE 40", "THE COAST ROAD", "CENTRAL DEPOT", "THE OLD QUARTER",
            "STENNMARK YARDS", "THE RESERVOIR", "SIDING 9",
        };

        private static readonly string[][] RoutineNotices =
        {
            new[] { "REVISED FUEL ALLOWANCE FOR BOOTH HEATERS.", "NO ACTION REQUIRED." },
            new[] { "STATIONERY REQUISITIONS ARE NOW QUARTERLY.", "FORMS FOLLOW." },
            new[] { "THE CENTRAL OFFICE THANKS ALL PERSONNEL", "FOR THEIR CONTINUED SERVICE." },
            new[] { "LAMP REPLACEMENT SCHEDULE SUSPENDED", "UNTIL FURTHER NOTICE." },
            new[] { "PERSONNEL ARE REMINDED NOT TO DISCUSS", "CHECKPOINT MATTERS OFF DUTY." },
            new[] { "A REPRESENTATIVE MAY VISIT THIS POST.", "NO DATE HAS BEEN SET." },
        };

        /// <summary>Everything a decision will eventually produce. Written when the decision
        /// is made and delivered later, so a campaign can be played out in a test without
        /// simulating the calendar.</summary>
        public static IEnumerable<Notice> For(Decision decision, int shiftIndex)
        {
            var subject = decision.Subject;
            var name = (subject.Name ?? "UNKNOWN").ToUpperInvariant();
            var place = Places[Math.Abs(name.GetHashCode()) % Places.Length];
            var night = (shiftIndex + 1).ToString(CultureInfo.InvariantCulture);

            if (decision.AdmittedANonHuman)
            {
                yield return new Notice(NoticeKind.Incident, shiftIndex + IncidentDelay, shiftIndex,
                    "CHECKPOINT BULLETIN",
                    new[]
                    {
                        $"AN INCIDENT IS REPORTED AT {place}.",
                        "THE VEHICLE INVOLVED IS BELIEVED TO",
                        $"HAVE CLEARED THIS POST ON NIGHT {night}.",
                        "NO FURTHER DETAIL IS AVAILABLE.",
                    });
            }

            if (decision.RefusedAHuman)
            {
                yield return new Notice(NoticeKind.MissingPerson, shiftIndex + MissingPersonDelay, shiftIndex,
                    "MISSING PERSONS",
                    new[]
                    {
                        $"{name}, LAST SEEN AT A DISTRICT",
                        $"CHECKPOINT ON NIGHT {night}.",
                        "THE FAMILY REQUESTS ANY INFORMATION.",
                    });
            }

            // The office is slow and does not explain itself. It never says which decision
            // it is docking you for.
            if (!decision.WasCorrect)
            {
                yield return new Notice(NoticeKind.Deduction, shiftIndex + DeductionDelay, shiftIndex,
                    "NOTICE OF DEDUCTION",
                    new[]
                    {
                        "AN IRREGULARITY HAS BEEN IDENTIFIED",
                        $"IN THE RECORD FOR NIGHT {night}.",
                        $"{DeductionPerError} CREDITS ARE WITHHELD.",
                    },
                    DeductionPerError);
            }

            if (decision.Chosen == Verdict.Alarm && decision.Correct == Verdict.Alarm)
            {
                yield return new Notice(NoticeKind.Commendation, shiftIndex + CommendationDelay, shiftIndex,
                    "INTERNAL MEMORANDUM",
                    new[]
                    {
                        $"THE ACTION TAKEN ON NIGHT {night} IS",
                        "NOTED WITH APPROVAL.",
                        "THIS MEMORANDUM CARRIES NO AWARD.",
                    });
            }
        }

        /// <summary>Paperwork that refers to nothing. Deterministic from the shift, so a
        /// campaign replays identically, and frequent enough that an envelope on the mat is
        /// not by itself news.</summary>
        public static IEnumerable<Notice> RoutineFor(int shiftIndex, int campaignSeed)
        {
            unchecked
            {
                var hash = (uint)(campaignSeed * 31 + shiftIndex * 7919);
                hash ^= hash >> 13;

                if (hash % 5u >= 2u)
                {
                    yield break;
                }

                yield return new Notice(NoticeKind.Routine, shiftIndex, -1,
                    "DISTRICT CIRCULAR",
                    RoutineNotices[(int)(hash % (uint)RoutineNotices.Length)]);
            }
        }
    }
}
