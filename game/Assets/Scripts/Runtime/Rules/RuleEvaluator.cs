using System.Collections.Generic;
using System.Linq;

namespace Monster.Rules
{
    public readonly struct Evaluation
    {
        public Evaluation(Verdict correctVerdict, IReadOnlyList<Criterion> firedCriteria)
        {
            CorrectVerdict = correctVerdict;
            FiredCriteria = firedCriteria;
        }

        public Verdict CorrectVerdict { get; }

        /// <summary>Every in-force criterion the subject violated, for the morning report
        /// and for explaining a mistake back to the player.</summary>
        public IReadOnlyList<Criterion> FiredCriteria { get; }

        public bool IsClean => FiredCriteria.Count == 0;
    }

    /// <summary>Turns a subject plus the rules in force into the one verdict the office
    /// considers correct.
    ///
    /// The resolution rule when several criteria fire is "most severe wins". That is a
    /// deliberate design choice and not an implementation detail: it means a player who
    /// spots any one of a subject's problems reaches the right answer as long as it is the
    /// worst one, which keeps the difficulty in *noticing* rather than in exhaustively
    /// enumerating.</summary>
    public static class RuleEvaluator
    {
        public static Evaluation Evaluate(in SubjectAttributes subject, CriteriaManual manual)
        {
            var fired = new List<Criterion>();
            var verdict = Verdict.Pass;

            foreach (var criterion in manual.InForce)
            {
                if (!criterion.Fires(subject))
                {
                    continue;
                }

                fired.Add(criterion);
                if (criterion.Demands > verdict)
                {
                    verdict = criterion.Demands;
                }
            }

            return new Evaluation(verdict, fired);
        }

        /// <summary>How badly a decision missed. Passing something that should have been
        /// alarmed is a different kind of wrong from holding something that should have
        /// passed, and the morning report grades them differently.</summary>
        public static int ErrorSeverity(Verdict chosen, Verdict correct)
        {
            if (chosen == correct)
            {
                return 0;
            }

            var gap = (int)correct - (int)chosen;

            // Letting something through that should have been stopped is the dangerous
            // direction and is weighted twice as heavily as being over-cautious.
            return gap > 0 ? gap * 2 : -gap;
        }

        public static IReadOnlyList<string> Explain(in Evaluation evaluation) =>
            evaluation.FiredCriteria.Select(c => $"{c.Id} rev{c.Revision}: {c.PrintedText}").ToList();
    }
}
