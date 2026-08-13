using System;

namespace Monster.Rules
{
    /// <summary>Everything the player can observe about a subject at the checkpoint.
    ///
    /// This is deliberately a flat value type with no behaviour. It is the single source
    /// of truth that both the paperwork on the desk and the criteria in the manual read
    /// from, which is what guarantees that every case the game presents is actually
    /// solvable: the player can see, one way or another, every field a criterion tests.
    ///
    /// <see cref="IsHuman"/> is the exception. It is ground truth, it is never shown, and
    /// no criterion may reference it.</summary>
    [Serializable]
    public struct SubjectAttributes : IEquatable<SubjectAttributes>
    {
        // ---------------------------------------------------------------- paperwork --
        public string Name;
        public int BirthYear;
        public string OriginDistrict;
        public string PermitSerial;

        /// <summary>How many days before tonight the permit was issued. Printed on the
        /// permit as a date; the player does the subtraction.</summary>
        public int PermitAgeDays;

        public string IssuingOffice;
        public bool PhotoMatchesFace;
        public bool SealPresent;

        // ------------------------------------------------------- observed at the glass --
        public int BlinkRatePerMinute;
        public float ResponseDelaySeconds;
        public bool PupilsReactToLight;
        public int BreathsPerMinute;
        public float SkinTemperatureC;
        public int VisibleLimbCount;

        // -------------------------------------------------------- seen on the monitors --
        public bool ReflectionConsistent;
        public bool SecondVoiceUnderTheFirst;
        public bool CargoDeclarationMatchesScan;

        // -------------------------------------------------------------- ground truth --
        /// <summary>Never displayed and never referenced by a criterion. Used only to
        /// score the ending and to write the delayed consequences.</summary>
        public bool IsHuman;

        public bool Equals(SubjectAttributes other) =>
            Name == other.Name
            && BirthYear == other.BirthYear
            && OriginDistrict == other.OriginDistrict
            && PermitSerial == other.PermitSerial
            && PermitAgeDays == other.PermitAgeDays
            && IssuingOffice == other.IssuingOffice
            && PhotoMatchesFace == other.PhotoMatchesFace
            && SealPresent == other.SealPresent
            && BlinkRatePerMinute == other.BlinkRatePerMinute
            && Math.Abs(ResponseDelaySeconds - other.ResponseDelaySeconds) < 0.0001f
            && PupilsReactToLight == other.PupilsReactToLight
            && BreathsPerMinute == other.BreathsPerMinute
            && Math.Abs(SkinTemperatureC - other.SkinTemperatureC) < 0.0001f
            && VisibleLimbCount == other.VisibleLimbCount
            && ReflectionConsistent == other.ReflectionConsistent
            && SecondVoiceUnderTheFirst == other.SecondVoiceUnderTheFirst
            && CargoDeclarationMatchesScan == other.CargoDeclarationMatchesScan
            && IsHuman == other.IsHuman;

        public override bool Equals(object obj) => obj is SubjectAttributes other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Name);
            hash.Add(BirthYear);
            hash.Add(OriginDistrict);
            hash.Add(PermitSerial);
            hash.Add(PermitAgeDays);
            hash.Add(IssuingOffice);
            hash.Add(PhotoMatchesFace);
            hash.Add(SealPresent);
            hash.Add(BlinkRatePerMinute);
            hash.Add(ResponseDelaySeconds);
            hash.Add(PupilsReactToLight);
            hash.Add(BreathsPerMinute);
            hash.Add(SkinTemperatureC);
            hash.Add(VisibleLimbCount);
            hash.Add(ReflectionConsistent);
            hash.Add(SecondVoiceUnderTheFirst);
            hash.Add(CargoDeclarationMatchesScan);
            hash.Add(IsHuman);
            return hash.ToHashCode();
        }

        public override string ToString() =>
            $"{Name} ({OriginDistrict}, b.{BirthYear}) permit {PermitSerial}";
    }

    /// <summary>Every field a criterion is allowed to test. Keeping this as an enum rather
    /// than reflection over field names means an unknown field is a compile error instead
    /// of a criterion that silently never fires.</summary>
    public enum NumericField
    {
        BirthYear,
        PermitAgeDays,
        BlinkRatePerMinute,
        ResponseDelaySeconds,
        BreathsPerMinute,
        SkinTemperatureC,
        VisibleLimbCount,
    }

    public enum FlagField
    {
        PhotoMatchesFace,
        SealPresent,
        PupilsReactToLight,
        ReflectionConsistent,
        SecondVoiceUnderTheFirst,
        CargoDeclarationMatchesScan,
    }

    public enum TextField
    {
        OriginDistrict,
        IssuingOffice,
        PermitSerialPrefix,
    }

    public static class SubjectFieldAccess
    {
        public static float Read(in SubjectAttributes subject, NumericField field) => field switch
        {
            NumericField.BirthYear => subject.BirthYear,
            NumericField.PermitAgeDays => subject.PermitAgeDays,
            NumericField.BlinkRatePerMinute => subject.BlinkRatePerMinute,
            NumericField.ResponseDelaySeconds => subject.ResponseDelaySeconds,
            NumericField.BreathsPerMinute => subject.BreathsPerMinute,
            NumericField.SkinTemperatureC => subject.SkinTemperatureC,
            NumericField.VisibleLimbCount => subject.VisibleLimbCount,
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
        };

        public static bool Read(in SubjectAttributes subject, FlagField field) => field switch
        {
            FlagField.PhotoMatchesFace => subject.PhotoMatchesFace,
            FlagField.SealPresent => subject.SealPresent,
            FlagField.PupilsReactToLight => subject.PupilsReactToLight,
            FlagField.ReflectionConsistent => subject.ReflectionConsistent,
            FlagField.SecondVoiceUnderTheFirst => subject.SecondVoiceUnderTheFirst,
            FlagField.CargoDeclarationMatchesScan => subject.CargoDeclarationMatchesScan,
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
        };

        public static string Read(in SubjectAttributes subject, TextField field) => field switch
        {
            TextField.OriginDistrict => subject.OriginDistrict,
            TextField.IssuingOffice => subject.IssuingOffice,
            TextField.PermitSerialPrefix => string.IsNullOrEmpty(subject.PermitSerial)
                ? string.Empty
                : subject.PermitSerial.Substring(0, Math.Min(2, subject.PermitSerial.Length)),
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
        };
    }
}
