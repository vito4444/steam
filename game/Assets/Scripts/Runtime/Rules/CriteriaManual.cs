using System;
using System.Collections.Generic;
using System.Linq;

namespace Monster.Rules
{
    /// <summary>The checkpoint manual: every page ever issued, in the order it arrived.
    ///
    /// The manual is not the same thing as the rules in force. Amendments supersede
    /// earlier revisions of the same criterion, but the superseded pages are never
    /// removed from the binder. Reading the binder and working out which page is current
    /// is the player's job and is the central mechanic of this concept, so the two views
    /// are kept explicitly separate here: <see cref="AllPages"/> is what is on the desk,
    /// <see cref="InForce"/> is what the office will actually judge you against.</summary>
    public sealed class CriteriaManual
    {
        private readonly List<Criterion> _pages = new();

        public IReadOnlyList<Criterion> AllPages => _pages;

        public CriteriaManual Issue(Criterion criterion)
        {
            if (criterion == null)
            {
                throw new ArgumentNullException(nameof(criterion));
            }

            if (_pages.Any(p => p.Id == criterion.Id && p.Revision == criterion.Revision))
            {
                throw new InvalidOperationException(
                    $"criterion {criterion.Id} revision {criterion.Revision} was issued twice");
            }

            _pages.Add(criterion);
            return this;
        }

        /// <summary>The highest revision of each criterion, which is what the office
        /// grades against.</summary>
        public IReadOnlyList<Criterion> InForce =>
            _pages.GroupBy(p => p.Id, StringComparer.Ordinal)
                .Select(g => g.OrderByDescending(p => p.Revision).First())
                .OrderBy(p => p.Id, StringComparer.Ordinal)
                .ToList();

        /// <summary>Pages that are still in the binder but no longer current. These are
        /// the ones that make the player wrong for good reasons.</summary>
        public IReadOnlyList<Criterion> Superseded =>
            _pages.GroupBy(p => p.Id, StringComparer.Ordinal)
                .SelectMany(g => g.OrderByDescending(p => p.Revision).Skip(1))
                .ToList();

        /// <summary>The pages available to the player by a given shift. Amendments arrive
        /// through the mail slot, so a page issued on day 9 is not in the binder on day 4.</summary>
        public CriteriaManual AsOfShift(int shiftIndex, IReadOnlyDictionary<string, int> arrivalShiftByPageKey)
        {
            var result = new CriteriaManual();
            foreach (var page in _pages)
            {
                var key = PageKey(page);
                var arrival = arrivalShiftByPageKey != null && arrivalShiftByPageKey.TryGetValue(key, out var s) ? s : 0;
                if (arrival <= shiftIndex)
                {
                    result.Issue(page);
                }
            }

            return result;
        }

        public static string PageKey(Criterion criterion) => $"{criterion.Id}#{criterion.Revision}";
    }
}
