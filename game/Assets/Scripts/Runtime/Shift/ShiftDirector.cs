using System;
using System.Collections.Generic;
using System.Linq;
using Monster.Rules;

namespace Monster.Shift
{
    public readonly struct Decision
    {
        public Decision(int subjectIndex, SubjectAttributes subject, Verdict chosen, Verdict correct,
            IReadOnlyList<string> firedCriteria)
        {
            SubjectIndex = subjectIndex;
            Subject = subject;
            Chosen = chosen;
            Correct = correct;
            FiredCriteria = firedCriteria;
        }

        public int SubjectIndex { get; }
        public SubjectAttributes Subject { get; }
        public Verdict Chosen { get; }
        public Verdict Correct { get; }
        public IReadOnlyList<string> FiredCriteria { get; }

        public bool WasCorrect => Chosen == Correct;
        public int Severity => RuleEvaluator.ErrorSeverity(Chosen, Correct);

        /// <summary>Refusing a person is a different kind of mistake from admitting
        /// something that is not one, and the endings are scored on the difference.</summary>
        public bool RefusedAHuman => Subject.IsHuman && Chosen >= Verdict.Refer && Correct < Verdict.Refer;

        public bool AdmittedANonHuman => !Subject.IsHuman && Chosen == Verdict.Pass;
    }

    public sealed class ShiftReport
    {
        public ShiftReport(int shiftIndex, IReadOnlyList<Decision> decisions, int quota, int payPerCorrect,
            int penaltyPerSeverity)
        {
            ShiftIndex = shiftIndex;
            Decisions = decisions;
            Quota = quota;

            Processed = decisions.Count;
            Correct = decisions.Count(d => d.WasCorrect);
            TotalSeverity = decisions.Sum(d => d.Severity);
            HumansRefused = decisions.Count(d => d.RefusedAHuman);
            NonHumansAdmitted = decisions.Count(d => d.AdmittedANonHuman);

            GrossPay = Correct * payPerCorrect;
            Penalty = TotalSeverity * penaltyPerSeverity;
            QuotaShortfall = Math.Max(0, quota - Processed);
            NetPay = Math.Max(0, GrossPay - Penalty - QuotaShortfall * payPerCorrect);
        }

        public int ShiftIndex { get; }
        public IReadOnlyList<Decision> Decisions { get; }
        public int Quota { get; }
        public int Processed { get; }
        public int Correct { get; }
        public int TotalSeverity { get; }
        public int HumansRefused { get; }
        public int NonHumansAdmitted { get; }
        public int GrossPay { get; }
        public int Penalty { get; }
        public int QuotaShortfall { get; }
        public int NetPay { get; }

        public bool QuotaMet => Processed >= Quota;
    }

    /// <summary>Runs one night at the checkpoint.
    ///
    /// Deliberately free of Unity types. The whole shift can therefore be played out in a
    /// test in microseconds, which is what makes it possible to assert that a thirty-night
    /// campaign neither becomes unwinnable nor pays out infinitely — properties that are
    /// impossible to check by playing.</summary>
    public sealed class ShiftDirector
    {
        public const int PayPerCorrect = 5;
        public const int PenaltyPerSeverity = 3;

        private readonly SubjectGenerator _generator = new();
        private readonly List<Decision> _decisions = new();
        private readonly List<GeneratedSubject> _queue = new();

        private int _position;

        public ShiftDirector(int campaignSeed, int shiftIndex)
        {
            CampaignSeed = campaignSeed;
            ShiftIndex = shiftIndex;
            Manual = CheckpointManual.AsOfShift(shiftIndex);
            Quota = QuotaFor(shiftIndex);

            // The queue is longer than the quota so there is always another vehicle; the
            // pressure comes from the clock, not from running out of work.
            for (var i = 0; i < Quota + 6; i++)
            {
                _queue.Add(_generator.Generate(campaignSeed, shiftIndex, i));
            }
        }

        public int CampaignSeed { get; }
        public int ShiftIndex { get; }
        public CriteriaManual Manual { get; }
        public int Quota { get; }

        public IReadOnlyList<Decision> Decisions => _decisions;
        public int Position => _position;
        public bool IsFinished => _position >= _queue.Count;

        public GeneratedSubject Current => IsFinished ? _queue[^1] : _queue[_position];

        public DocumentContent Permit => DocumentBuilder.TransitPermit(Current.Attributes);
        public DocumentContent Biometrics => DocumentBuilder.BiometricReadout(Current.Attributes);
        public DocumentContent Cabin => DocumentBuilder.CabinFeed(Current.Attributes);
        public DocumentContent Underside => DocumentBuilder.UndersideScan(Current.Attributes);
        public DocumentContent Reflection => DocumentBuilder.ReflectionFeed(Current.Attributes);

        /// <summary>Nine vehicles on the first night, rising to twenty on the last. The
        /// curve exists so the later nights feel rushed while the manual is also at its
        /// most contradictory.</summary>
        public static int QuotaFor(int shiftIndex) => 9 + (int)Math.Round(shiftIndex * 11 / 29.0);

        public event Action<Decision> DecisionMade;

        public Decision Decide(Verdict chosen)
        {
            if (IsFinished)
            {
                throw new InvalidOperationException("the shift is over; no vehicles remain");
            }

            var subject = Current;
            var evaluation = RuleEvaluator.Evaluate(subject.Attributes, Manual);
            var decision = new Decision(_position, subject.Attributes, chosen, evaluation.CorrectVerdict,
                evaluation.FiredCriteria.Select(c => c.Id).ToList());

            _decisions.Add(decision);
            _position++;

            DecisionMade?.Invoke(decision);
            return decision;
        }

        /// <summary>Jumps straight to a queue position without deciding anything. Used only
        /// by the automated self-check, which needs a specific subject on screen to
        /// photograph and must not accumulate decisions on the way there.</summary>
        public void SkipTo(int position)
        {
            _position = Math.Clamp(position, 0, _queue.Count - 1);
        }

        public ShiftReport BuildReport() =>
            new(ShiftIndex, _decisions, Quota, PayPerCorrect, PenaltyPerSeverity);
    }
}
