using System;
using System.Collections.Generic;
using System.Linq;
using Monster.Rules;
using UnityEngine;

namespace Monster.Shift
{
    /// <summary>A saved campaign.
    ///
    /// Only the seed and the verdicts are written down. Everything else — who turned up,
    /// what the manual said that night, which notices are queued and when they land — is
    /// derived by replaying, because all of it is already deterministic from the seed.
    ///
    /// That is the whole design of this file. Serialising the notices themselves would tie
    /// every save to the exact wording of the post, so editing a single line of a district
    /// circular would invalidate every save in existence. Replaying instead means a save is
    /// a few hundred small integers and survives any change that does not alter the rules.
    /// </summary>
    [Serializable]
    public sealed class CampaignSave
    {
        [Serializable]
        public sealed class Night
        {
            public List<int> verdicts = new();
        }

        public int version = 1;
        public int seed;
        public List<Night> nights = new();

        public const int CurrentVersion = 1;

        public string ToJson() => JsonUtility.ToJson(this, prettyPrint: true);

        public static CampaignSave FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("the save file is empty", nameof(json));
            }

            var save = JsonUtility.FromJson<CampaignSave>(json);

            if (save == null)
            {
                throw new FormatException("the save file is not valid JSON");
            }

            if (save.version != CurrentVersion)
            {
                throw new FormatException(
                    $"the save file is version {save.version}, this build reads version {CurrentVersion}");
            }

            save.nights ??= new List<Night>();
            return save;
        }

        /// <summary>True if the verdicts still line up with the queues the seed produces.
        ///
        /// Worth checking rather than assuming: a save written before a change to queue
        /// lengths or the difficulty curve would replay into a different game, and failing
        /// loudly is better than silently resuming somebody else's campaign.</summary>
        public bool IsConsistent(out string problem)
        {
            if (nights.Count > Campaign.TotalShifts)
            {
                problem = $"the save has {nights.Count} nights, the campaign is {Campaign.TotalShifts}";
                return false;
            }

            for (var i = 0; i < nights.Count; i++)
            {
                var expected = new ShiftDirector(seed, i).QueueLength;
                var actual = nights[i].verdicts.Count;

                // The final night may be partly played; earlier ones must be complete.
                var complete = i < nights.Count - 1;

                if (actual > expected || (complete && actual != expected))
                {
                    problem = $"night {i + 1} has {actual} decisions, the queue holds {expected}";
                    return false;
                }

                if (nights[i].verdicts.Any(v => !Enum.IsDefined(typeof(Verdict), v)))
                {
                    problem = $"night {i + 1} contains a verdict this build does not recognise";
                    return false;
                }
            }

            problem = null;
            return true;
        }
    }
}
