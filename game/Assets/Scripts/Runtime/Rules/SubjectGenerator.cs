using System;
using System.Collections.Generic;
using System.Linq;

namespace Monster.Rules
{
    /// <summary>One generated case: the subject plus what the generator meant by it.</summary>
    public readonly struct GeneratedSubject
    {
        public GeneratedSubject(SubjectAttributes attributes, IReadOnlyList<string> intendedViolations, int seed)
        {
            Attributes = attributes;
            IntendedViolations = intendedViolations;
            Seed = seed;
        }

        public SubjectAttributes Attributes { get; }

        /// <summary>Criterion ids the generator deliberately made this subject violate.
        /// The evaluator, not this list, decides the correct verdict — a violation can
        /// drag others in with it and that is intended. This exists so a test can assert
        /// that what the generator meant to do actually happened.</summary>
        public IReadOnlyList<string> IntendedViolations { get; }

        public int Seed { get; }
    }

    /// <summary>Builds checkpoint subjects deterministically from a seed.
    ///
    /// Determinism is not a nicety here. It is what makes a bug report reproducible from a
    /// seed string, what lets the automated self-check drive an identical shift on every
    /// commit, and what lets a test generate ten thousand cases and assert a property over
    /// all of them.
    ///
    /// The generator works by building a subject that violates nothing, then applying a
    /// chosen set of violations to it. That order matters: it means a "clean" subject is
    /// clean by construction rather than by luck, so a test asserting that clean subjects
    /// evaluate to Pass is checking the rules, not the dice.</summary>
    public sealed class SubjectGenerator
    {
        private static readonly string[] GivenNames =
        {
            "Halden", "Merrow", "Tace", "Orrin", "Vesna", "Calder", "Iva", "Brune",
            "Sella", "Rook", "Anselm", "Wren", "Tobin", "Marek", "Ilse", "Dov",
        };

        private static readonly string[] FamilyNames =
        {
            "Kastel", "Verrow", "Ondaal", "Pryce", "Sundermann", "Achter", "Blume",
            "Roth", "Iversen", "Manck", "Delacre", "Ostrow", "Fenn", "Yarrow",
        };

        private static readonly string[] CleanDistricts =
        {
            "Lowbank", "Harrow", "Stennmark", "Coldwater", "Fenholt", "Marrowgate",
        };

        private static readonly string[] ClosedDistricts = { "Vessel", "Ashgate" };

        private static readonly string[] CleanOffices =
        {
            "Southgate", "Riverside", "Central", "Eastmarch",
        };

        private static readonly string[] CleanSerialPrefixes = { "AR", "BT", "CN", "DL", "EM", "FV" };

        /// <summary>A way of making a clean subject violate one specific criterion.
        ///
        /// <see cref="FieldGroup"/> names the attribute the violation writes. Two
        /// violations that write the same attribute cannot both be applied, because the
        /// second silently overwrites the first and the subject then only violates one of
        /// them while the generator believes it violates both. That bug shipped in the
        /// first version of this generator — C-08 (respiration too low) and C-09
        /// (respiration too high) both wrote BreathsPerMinute — and was caught by
        /// EveryIntendedViolationActuallyFiresItsCriterion.</summary>
        private readonly struct Violation
        {
            public Violation(string criterionId, string fieldGroup, Action<Random, SubjectAttributes[]> apply)
            {
                CriterionId = criterionId;
                FieldGroup = fieldGroup;
                Apply = apply;
            }

            public string CriterionId { get; }
            public string FieldGroup { get; }
            public Action<Random, SubjectAttributes[]> Apply { get; }
        }

        private static readonly Violation[] Violations =
        {
            new("C-01", nameof(SubjectAttributes.SealPresent),
                (_, s) => s[0].SealPresent = false),
            new("C-02", nameof(SubjectAttributes.PhotoMatchesFace),
                (_, s) => s[0].PhotoMatchesFace = false),
            new("C-03", nameof(SubjectAttributes.PermitAgeDays),
                (r, s) => s[0].PermitAgeDays = 15 + r.Next(0, 90)),
            new("C-04", nameof(SubjectAttributes.PermitSerial),
                (r, s) => s[0].PermitSerial = "KX-" + r.Next(1000, 9999)),
            new("C-05", nameof(SubjectAttributes.OriginDistrict),
                (r, s) => s[0].OriginDistrict = ClosedDistricts[r.Next(ClosedDistricts.Length)]),
            new("C-06", nameof(SubjectAttributes.BlinkRatePerMinute),
                (r, s) => s[0].BlinkRatePerMinute = r.Next(0, 4)),
            new("C-07", nameof(SubjectAttributes.PupilsReactToLight),
                (_, s) => s[0].PupilsReactToLight = false),
            new("C-08", nameof(SubjectAttributes.BreathsPerMinute),
                (r, s) => s[0].BreathsPerMinute = r.Next(0, 6)),
            new("C-09", nameof(SubjectAttributes.BreathsPerMinute),
                (r, s) => s[0].BreathsPerMinute = 27 + r.Next(0, 14)),
            new("C-10", nameof(SubjectAttributes.SecondVoiceUnderTheFirst),
                (_, s) => s[0].SecondVoiceUnderTheFirst = true),
            new("C-11", nameof(SubjectAttributes.ReflectionConsistent),
                (_, s) => s[0].ReflectionConsistent = false),
            new("C-12", nameof(SubjectAttributes.SkinTemperatureC),
                (r, s) => s[0].SkinTemperatureC = 28.0f + (float)r.NextDouble() * 5.9f),
            new("C-13", nameof(SubjectAttributes.VisibleLimbCount),
                (r, s) => s[0].VisibleLimbCount = 5 + r.Next(0, 2)),
            new("C-14", nameof(SubjectAttributes.ResponseDelaySeconds),
                (r, s) => s[0].ResponseDelaySeconds = 2.1f + (float)r.NextDouble() * 3.4f),
            new("C-15", nameof(SubjectAttributes.IssuingOffice),
                (_, s) => s[0].IssuingOffice = "Northgate"),
            new("C-16", nameof(SubjectAttributes.CargoDeclarationMatchesScan),
                (_, s) => s[0].CargoDeclarationMatchesScan = false),
        };

        public static IReadOnlyCollection<string> KnownViolations =>
            Violations.Select(v => v.CriterionId).ToList();

        /// <summary>Deterministic per (campaign seed, shift, position in the queue).</summary>
        public static int SeedFor(int campaignSeed, int shiftIndex, int subjectIndex)
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + campaignSeed;
                hash = hash * 31 + shiftIndex * 977;
                hash = hash * 31 + subjectIndex * 31391;
                return hash;
            }
        }

        /// <summary>How many criteria a subject on this shift should violate. The curve is
        /// the difficulty design: night one is almost all clean paperwork, and by the end
        /// roughly two thirds of arrivals are wrong in some way.</summary>
        public static int ViolationCountFor(int shiftIndex, Random random)
        {
            var roll = random.NextDouble();
            var pressure = Math.Clamp(shiftIndex / 30.0, 0.0, 1.0);

            var cleanChance = 0.70 - 0.45 * pressure;
            if (roll < cleanChance)
            {
                return 0;
            }

            var doubleChance = cleanChance + (0.26 - 0.10 * pressure);
            if (roll < doubleChance)
            {
                return 1;
            }

            return pressure > 0.5 && roll > 0.94 ? 3 : 2;
        }

        public GeneratedSubject Generate(int campaignSeed, int shiftIndex, int subjectIndex)
        {
            var seed = SeedFor(campaignSeed, shiftIndex, subjectIndex);
            var random = new Random(seed);

            var subject = Clean(random);
            var violationCount = ViolationCountFor(shiftIndex, random);

            var applied = new List<string>();
            if (violationCount > 0)
            {
                // Shuffled deterministically rather than sampled with replacement, so a
                // subject never violates the same criterion twice and the count is honest.
                var pool = Violations.OrderBy(v => v.CriterionId, StringComparer.Ordinal).ToList();
                Shuffle(pool, random);

                var usedFields = new HashSet<string>(StringComparer.Ordinal);
                foreach (var violation in pool)
                {
                    if (applied.Count >= violationCount)
                    {
                        break;
                    }

                    // Skipped rather than reordered: two violations writing the same field
                    // would leave the subject violating only the last one applied.
                    if (!usedFields.Add(violation.FieldGroup))
                    {
                        continue;
                    }

                    var buffer = new[] { subject };
                    violation.Apply(random, buffer);
                    subject = buffer[0];
                    applied.Add(violation.CriterionId);
                }

                applied.Sort(StringComparer.Ordinal);
            }

            // Ground truth. Anything that trips a body-signal criterion is not a person;
            // paperwork problems are just paperwork problems, and that distinction is what
            // makes refusing a human a different mistake from passing something else.
            var bodySignals = new[] { "C-06", "C-07", "C-08", "C-09", "C-10", "C-11", "C-12", "C-13" };
            subject.IsHuman = !applied.Any(id => bodySignals.Contains(id));

            return new GeneratedSubject(subject, applied, seed);
        }

        /// <summary>A subject that violates nothing in the manual, by construction.</summary>
        private static SubjectAttributes Clean(Random random)
        {
            return new SubjectAttributes
            {
                Name = $"{GivenNames[random.Next(GivenNames.Length)]} {FamilyNames[random.Next(FamilyNames.Length)]}",
                BirthYear = 1962 + random.Next(0, 45),
                OriginDistrict = CleanDistricts[random.Next(CleanDistricts.Length)],
                PermitSerial = $"{CleanSerialPrefixes[random.Next(CleanSerialPrefixes.Length)]}-{random.Next(1000, 9999)}",
                PermitAgeDays = random.Next(0, 15),
                IssuingOffice = CleanOffices[random.Next(CleanOffices.Length)],
                PhotoMatchesFace = true,
                SealPresent = true,

                BlinkRatePerMinute = 8 + random.Next(0, 15),
                ResponseDelaySeconds = 0.3f + (float)random.NextDouble() * 1.6f,
                PupilsReactToLight = true,
                BreathsPerMinute = 8 + random.Next(0, 17),
                SkinTemperatureC = 36.1f + (float)random.NextDouble() * 1.2f,
                VisibleLimbCount = 4,

                ReflectionConsistent = true,
                SecondVoiceUnderTheFirst = false,
                CargoDeclarationMatchesScan = true,

                IsHuman = true,
            };
        }

        private static void Shuffle<T>(IList<T> list, Random random)
        {
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
