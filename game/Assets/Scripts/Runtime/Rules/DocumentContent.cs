using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Monster.Rules
{
    /// <summary>One printed line on a document: a label and its value.</summary>
    public readonly struct DocumentField
    {
        public DocumentField(string label, string value)
        {
            Label = label;
            Value = value;
        }

        public string Label { get; }
        public string Value { get; }

        public string ToPrintedLine(int labelWidth = 10) =>
            Label.PadRight(labelWidth) + Value;
    }

    /// <summary>Everything printed on one piece of paper or one monitor readout.
    ///
    /// This is built from <see cref="SubjectAttributes"/> and nothing else, which is the
    /// property that keeps the game fair: if a criterion can test a field, the player can
    /// read that field somewhere. A test asserts exactly that.</summary>
    public sealed class DocumentContent
    {
        public DocumentContent(string title, IReadOnlyList<DocumentField> fields, string footer = null,
            PortraitCode? portrait = null, int labelWidth = 10)
        {
            Title = title;
            Fields = fields;
            Footer = footer;
            Portrait = portrait;
            LabelWidth = labelWidth;
        }

        public string Title { get; }
        public IReadOnlyList<DocumentField> Fields { get; }
        public string Footer { get; }
        public PortraitCode? Portrait { get; }

        /// <summary>How far the label column is padded. Paper has room for ten characters;
        /// a CRT does not, and cramming a document layout onto a screen is what made the
        /// monitors unreadable from the seated pose.</summary>
        public int LabelWidth { get; }

        public string ToPrintedPage()
        {
            var builder = new StringBuilder();
            builder.AppendLine(Title);
            builder.AppendLine(new string('-', Title.Length));
            foreach (var field in Fields)
            {
                builder.AppendLine(field.ToPrintedLine(LabelWidth));
            }

            if (!string.IsNullOrEmpty(Footer))
            {
                builder.AppendLine();
                builder.AppendLine(Footer);
            }

            return builder.ToString();
        }
    }

    /// <summary>Turns a subject into the paperwork and readouts on the desk.
    ///
    /// Deliberately no formatting cleverness: the fields are printed as plainly as a
    /// government form would print them, because the difficulty of this game has to come
    /// from cross-referencing the manual, never from struggling to read a value.</summary>
    public static class DocumentBuilder
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        /// <summary>A CRT is 520 mm wide and has to be legible from the seat, so its label
        /// column is three characters narrower than the paperwork's.</summary>
        private const int ScreenLabelWidth = 7;

        public static DocumentContent TransitPermit(in SubjectAttributes subject) =>
            new(
                "DISTRICT TRANSIT PERMIT",
                new[]
                {
                    new DocumentField("BEARER", (subject.Name ?? string.Empty).ToUpperInvariant()),
                    new DocumentField("BORN", subject.BirthYear.ToString(Invariant)),
                    new DocumentField("DISTRICT", (subject.OriginDistrict ?? string.Empty).ToUpperInvariant()),
                    new DocumentField("SERIAL", subject.PermitSerial ?? string.Empty),
                    new DocumentField("ISSUED", subject.PermitAgeDays == 0
                        ? "TODAY"
                        : $"{subject.PermitAgeDays} DAYS AGO"),
                    new DocumentField("OFFICE", (subject.IssuingOffice ?? string.Empty).ToUpperInvariant()),
                    new DocumentField("SEAL", subject.SealPresent ? "AFFIXED" : "NOT AFFIXED"),
                },
                subject.SealPresent ? "DISTRICT SEAL AFFIXED BELOW" : "NO SEAL ON THIS DOCUMENT",
                PortraitCode.FromSubject(subject));

        /// <summary>The biometric strip on the centre monitor. Everything a criterion can
        /// test about the body is legible here, in the same units the manual uses.</summary>
        public static DocumentContent BiometricReadout(in SubjectAttributes subject) =>
            new(
                "BIOMETRIC SWEEP",
                new[]
                {
                    new DocumentField("BLINK", $"{subject.BlinkRatePerMinute}/MIN"),
                    new DocumentField("RESP", $"{subject.BreathsPerMinute}/MIN"),
                    new DocumentField("PUPIL", subject.PupilsReactToLight ? "REACTIVE" : "FIXED"),
                    new DocumentField("SURF", subject.SkinTemperatureC.ToString("F1", Invariant) + "C"),
                    new DocumentField("LIMBS", subject.VisibleLimbCount.ToString(Invariant)),
                },
                null, null, ScreenLabelWidth);

        /// <summary>The rear cabin feed. Shows the bearer as observed and their reflection,
        /// which is where a photograph mismatch and an inconsistent reflection are caught.</summary>
        public static DocumentContent CabinFeed(in SubjectAttributes subject) =>
            new(
                "CABIN 02",
                new[]
                {
                    new DocumentField("SUBJECT", "PRESENT"),
                    new DocumentField("AUDIO", subject.SecondVoiceUnderTheFirst ? "LAYERED" : "SINGLE"),
                    new DocumentField("DELAY", subject.ResponseDelaySeconds.ToString("F1", Invariant) + "S"),
                },
                null,
                PortraitCode.AsObserved(subject),
                ScreenLabelWidth);

        public static DocumentContent UndersideScan(in SubjectAttributes subject) =>
            new(
                "UNDERSIDE",
                new[]
                {
                    new DocumentField("CARGO", subject.CargoDeclarationMatchesScan ? "MATCHED" : "DIVERGENT"),
                    new DocumentField("MASS", subject.CargoDeclarationMatchesScan ? "NOMINAL" : "OVER"),
                },
                null, null, ScreenLabelWidth);

        public static DocumentContent ReflectionFeed(in SubjectAttributes subject) =>
            new(
                "GLASS 01",
                new[]
                {
                    new DocumentField("RETURN", "ACQUIRED"),
                },
                null,
                PortraitCode.AsReflected(subject),
                ScreenLabelWidth);

        /// <summary>Every field name the paperwork and the readouts print, used by a test
        /// to prove nothing a criterion can test is hidden from the player.</summary>
        public static IReadOnlyCollection<string> ObservableFieldCoverage(in SubjectAttributes subject)
        {
            var covered = new HashSet<string>
            {
                nameof(SubjectAttributes.Name),
                nameof(SubjectAttributes.BirthYear),
                nameof(SubjectAttributes.OriginDistrict),
                nameof(SubjectAttributes.PermitSerial),
                nameof(SubjectAttributes.PermitAgeDays),
                nameof(SubjectAttributes.IssuingOffice),
                nameof(SubjectAttributes.SealPresent),
                nameof(SubjectAttributes.BlinkRatePerMinute),
                nameof(SubjectAttributes.BreathsPerMinute),
                nameof(SubjectAttributes.PupilsReactToLight),
                nameof(SubjectAttributes.SkinTemperatureC),
                nameof(SubjectAttributes.VisibleLimbCount),
                nameof(SubjectAttributes.SecondVoiceUnderTheFirst),
                nameof(SubjectAttributes.ResponseDelaySeconds),
                nameof(SubjectAttributes.CargoDeclarationMatchesScan),
            };

            // These two are read by comparing portrait grids rather than by reading a
            // printed value, so they are covered only when both sides are actually shown.
            if (TransitPermit(subject).Portrait.HasValue && CabinFeed(subject).Portrait.HasValue)
            {
                covered.Add(nameof(SubjectAttributes.PhotoMatchesFace));
            }

            if (CabinFeed(subject).Portrait.HasValue && ReflectionFeed(subject).Portrait.HasValue)
            {
                covered.Add(nameof(SubjectAttributes.ReflectionConsistent));
            }

            // Text-field criteria read a prefix of the serial, which the permit prints whole.
            covered.Add("PermitSerialPrefix");
            covered.Add(nameof(TextField.OriginDistrict));
            covered.Add(nameof(TextField.IssuingOffice));

            return covered;
        }
    }
}
