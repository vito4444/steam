using System;
using System.Collections.Generic;
using System.Linq;

namespace Monster.Rules
{
    /// <summary>What the player writes on the form. Ordered by severity, which is what
    /// <see cref="RuleEvaluator"/> uses to resolve several criteria firing at once.</summary>
    public enum Verdict
    {
        Pass = 0,
        Hold = 1,
        Refer = 2,
        Alarm = 3,
    }

    public enum ComparisonOp
    {
        LessThan,
        LessOrEqual,
        GreaterOrEqual,
        GreaterThan,
    }

    /// <summary>One clause of a criterion. A criterion fires only when every one of its
    /// conditions holds, so a condition describes a way of being *wrong*.</summary>
    [Serializable]
    public readonly struct Condition
    {
        private enum Kind { Numeric, Flag, Text }

        private readonly Kind _kind;
        private readonly NumericField _numericField;
        private readonly ComparisonOp _op;
        private readonly float _threshold;
        private readonly FlagField _flagField;
        private readonly bool _expected;
        private readonly TextField _textField;
        private readonly string[] _allowed;
        private readonly bool _mustBeInList;

        private Condition(Kind kind, NumericField numericField, ComparisonOp op, float threshold,
            FlagField flagField, bool expected, TextField textField, string[] allowed, bool mustBeInList)
        {
            _kind = kind;
            _numericField = numericField;
            _op = op;
            _threshold = threshold;
            _flagField = flagField;
            _expected = expected;
            _textField = textField;
            _allowed = allowed;
            _mustBeInList = mustBeInList;
        }

        public static Condition Numeric(NumericField field, ComparisonOp op, float threshold) =>
            new(Kind.Numeric, field, op, threshold, default, default, default, null, default);

        public static Condition Flag(FlagField field, bool expected) =>
            new(Kind.Flag, default, default, default, field, expected, default, null, default);

        public static Condition TextIn(TextField field, params string[] allowed) =>
            new(Kind.Text, default, default, default, default, default, field, allowed, true);

        public static Condition TextNotIn(TextField field, params string[] disallowed) =>
            new(Kind.Text, default, default, default, default, default, field, disallowed, false);

        public bool Holds(in SubjectAttributes subject)
        {
            switch (_kind)
            {
                case Kind.Numeric:
                    var value = SubjectFieldAccess.Read(subject, _numericField);
                    return _op switch
                    {
                        ComparisonOp.LessThan => value < _threshold,
                        ComparisonOp.LessOrEqual => value <= _threshold,
                        ComparisonOp.GreaterOrEqual => value >= _threshold,
                        ComparisonOp.GreaterThan => value > _threshold,
                        _ => false,
                    };
                case Kind.Flag:
                    return SubjectFieldAccess.Read(subject, _flagField) == _expected;
                case Kind.Text:
                    var text = SubjectFieldAccess.Read(subject, _textField);
                    var present = _allowed != null && _allowed.Contains(text, StringComparer.Ordinal);
                    return present == _mustBeInList;
                default:
                    return false;
            }
        }

        /// <summary>The set of fields this condition reads. Used by the solvability test to
        /// assert that nothing the player cannot observe is ever tested.</summary>
        public IEnumerable<string> ReadFields()
        {
            yield return _kind switch
            {
                Kind.Numeric => _numericField.ToString(),
                Kind.Flag => _flagField.ToString(),
                Kind.Text => _textField.ToString(),
                _ => "unknown",
            };
        }

        public override string ToString() => _kind switch
        {
            Kind.Numeric => $"{_numericField} {_op} {_threshold}",
            Kind.Flag => $"{_flagField} == {_expected}",
            Kind.Text => $"{_textField} {(_mustBeInList ? "in" : "not in")} [{string.Join(", ", _allowed ?? Array.Empty<string>())}]",
            _ => "invalid",
        };
    }

    /// <summary>A single numbered rule from the checkpoint manual.
    ///
    /// A criterion carries the exact text printed on its page as well as its machine
    /// form. Those two must agree; the game is unfair the moment the printed rule and the
    /// evaluated rule diverge, and <see cref="CriteriaSet"/> is where the authored set is
    /// kept so that a test can check them together.</summary>
    public sealed class Criterion
    {
        public Criterion(string id, int revision, string printedText, Verdict demands,
            params Condition[] conditions)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("a criterion needs an id", nameof(id));
            }

            if (conditions == null || conditions.Length == 0)
            {
                throw new ArgumentException($"criterion {id} has no conditions", nameof(conditions));
            }

            Id = id;
            Revision = revision;
            PrintedText = printedText;
            Demands = demands;
            Conditions = conditions;
        }

        public string Id { get; }

        /// <summary>Higher revisions of the same id supersede lower ones. Superseded pages
        /// stay in the physical manual, which is the player's problem, not the
        /// evaluator's.</summary>
        public int Revision { get; }

        public string PrintedText { get; }

        public Verdict Demands { get; }

        public IReadOnlyList<Condition> Conditions { get; }

        public bool Fires(in SubjectAttributes subject)
        {
            for (var i = 0; i < Conditions.Count; i++)
            {
                if (!Conditions[i].Holds(subject))
                {
                    return false;
                }
            }

            return true;
        }

        public override string ToString() => $"{Id} rev{Revision} -> {Demands}";
    }
}
