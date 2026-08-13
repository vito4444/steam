using System.Collections.Generic;

namespace Monster.Rules
{
    /// <summary>The authored content of the checkpoint manual.
    ///
    /// Two things are true of every page here and are enforced by tests. The printed text
    /// says exactly what the conditions do, because a game where the page and the rule
    /// disagree is not difficult, it is broken. And every page arrives on a stated shift,
    /// because the escalation of this concept is the manual growing contradictory over
    /// time rather than the graphics getting scarier.</summary>
    public static class CheckpointManual
    {
        /// <summary>Which shift each page arrives through the mail slot on. Pages with no
        /// entry are in the binder from the first night.</summary>
        public static readonly IReadOnlyDictionary<string, int> ArrivalShift = new Dictionary<string, int>
        {
            ["C-05#2"] = 6,
            ["C-14#2"] = 9,
            ["C-03#2"] = 12,
            ["C-16#1"] = 15,
            ["C-06#2"] = 18,
        };

        public static CriteriaManual Build()
        {
            var manual = new CriteriaManual();

            manual.Issue(new Criterion("C-01", 1,
                "A transit permit bearing no district seal is not a permit. REFER.",
                Verdict.Refer,
                Condition.Flag(FlagField.SealPresent, false)));

            manual.Issue(new Criterion("C-02", 1,
                "Where the photograph does not correspond to the bearer, REFER.",
                Verdict.Refer,
                Condition.Flag(FlagField.PhotoMatchesFace, false)));

            manual.Issue(new Criterion("C-03", 1,
                "Permits issued more than 30 days ago have lapsed. HOLD.",
                Verdict.Hold,
                Condition.Numeric(NumericField.PermitAgeDays, ComparisonOp.GreaterThan, 30f)));

            // Tightened without warning, which is the point.
            manual.Issue(new Criterion("C-03", 2,
                "AMENDMENT. Permits issued more than 14 days ago have lapsed. HOLD.",
                Verdict.Hold,
                Condition.Numeric(NumericField.PermitAgeDays, ComparisonOp.GreaterThan, 14f)));

            manual.Issue(new Criterion("C-04", 1,
                "Serial prefix KX was recalled in its entirety. REFER.",
                Verdict.Refer,
                Condition.TextIn(TextField.PermitSerialPrefix, "KX")));

            manual.Issue(new Criterion("C-05", 1,
                "No transit is permitted out of Vessel District. HOLD.",
                Verdict.Hold,
                Condition.TextIn(TextField.OriginDistrict, "Vessel")));

            manual.Issue(new Criterion("C-05", 2,
                "AMENDMENT. No transit out of Vessel District or Ashgate. HOLD.",
                Verdict.Hold,
                Condition.TextIn(TextField.OriginDistrict, "Vessel", "Ashgate")));

            manual.Issue(new Criterion("C-06", 1,
                "A bearer blinking fewer than 4 times per minute is REFERRED for assessment.",
                Verdict.Refer,
                Condition.Numeric(NumericField.BlinkRatePerMinute, ComparisonOp.LessThan, 4f)));

            manual.Issue(new Criterion("C-06", 2,
                "AMENDMENT. Fewer than 4 blinks per minute is now an ALARM condition.",
                Verdict.Alarm,
                Condition.Numeric(NumericField.BlinkRatePerMinute, ComparisonOp.LessThan, 4f)));

            manual.Issue(new Criterion("C-07", 1,
                "Pupils that do not contract under the lamp. ALARM.",
                Verdict.Alarm,
                Condition.Flag(FlagField.PupilsReactToLight, false)));

            manual.Issue(new Criterion("C-08", 1,
                "Respiration below 6 breaths per minute. REFER.",
                Verdict.Refer,
                Condition.Numeric(NumericField.BreathsPerMinute, ComparisonOp.LessThan, 6f)));

            manual.Issue(new Criterion("C-09", 1,
                "Respiration above 26 breaths per minute. REFER.",
                Verdict.Refer,
                Condition.Numeric(NumericField.BreathsPerMinute, ComparisonOp.GreaterThan, 26f)));

            manual.Issue(new Criterion("C-10", 1,
                "Where a second voice is audible beneath the first. ALARM.",
                Verdict.Alarm,
                Condition.Flag(FlagField.SecondVoiceUnderTheFirst, true)));

            manual.Issue(new Criterion("C-11", 1,
                "Where the cabin monitor shows a reflection inconsistent with the bearer. ALARM.",
                Verdict.Alarm,
                Condition.Flag(FlagField.ReflectionConsistent, false)));

            manual.Issue(new Criterion("C-12", 1,
                "Surface temperature below 34.0 degrees. REFER.",
                Verdict.Refer,
                Condition.Numeric(NumericField.SkinTemperatureC, ComparisonOp.LessThan, 34.0f)));

            manual.Issue(new Criterion("C-13", 1,
                "More than four limbs visible on any feed. ALARM.",
                Verdict.Alarm,
                Condition.Numeric(NumericField.VisibleLimbCount, ComparisonOp.GreaterThan, 4f)));

            manual.Issue(new Criterion("C-14", 1,
                "A pause exceeding 3.0 seconds before answering. HOLD.",
                Verdict.Hold,
                Condition.Numeric(NumericField.ResponseDelaySeconds, ComparisonOp.GreaterThan, 3.0f)));

            manual.Issue(new Criterion("C-14", 2,
                "AMENDMENT. A pause exceeding 2.0 seconds before answering. REFER.",
                Verdict.Refer,
                Condition.Numeric(NumericField.ResponseDelaySeconds, ComparisonOp.GreaterThan, 2.0f)));

            manual.Issue(new Criterion("C-15", 1,
                "Permits issued by the Northgate office are no longer honoured. REFER.",
                Verdict.Refer,
                Condition.TextIn(TextField.IssuingOffice, "Northgate")));

            manual.Issue(new Criterion("C-16", 1,
                "Where the cargo declaration does not match the underside scan. HOLD.",
                Verdict.Hold,
                Condition.Flag(FlagField.CargoDeclarationMatchesScan, false)));

            return manual;
        }

        /// <summary>The binder as it exists on a given night.</summary>
        public static CriteriaManual AsOfShift(int shiftIndex) =>
            Build().AsOfShift(shiftIndex, ArrivalShift);
    }
}
