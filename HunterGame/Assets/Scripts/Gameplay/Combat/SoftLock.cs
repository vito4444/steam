using System.Collections.Generic;
using UnityEngine;

namespace Hunter.Gameplay.Combat
{
    /// Attack-time aim assist.
    ///
    /// Market research 3.1 records the loudest complaint against the closest competitor:
    /// "明明看着已经砍下去，但判定和反馈总是慢一秒". That product ships with no lock-on at
    /// all. Our answer is not full lock-on, which would flatten the melee spacing game,
    /// but a bounded nudge: the swing rotates toward a nearby target by at most
    /// MaxCorrectionDegrees. The player still has to aim; they just stop being punished
    /// for being three degrees off.
    ///
    /// Hardcore players can disable it, which is why this is a pure function taking the
    /// budget as a parameter rather than a global.
    public static class SoftLock
    {
        public const float MaxCorrectionDegrees = 25f;

        public readonly struct Candidate
        {
            public Vector3 Position { get; }
            public object Owner { get; }

            public Candidate(Vector3 position, object owner)
            {
                Position = position;
                Owner = owner;
            }
        }

        public readonly struct Result
        {
            public Vector3 Direction { get; }
            public object Target { get; }
            public float CorrectionDegrees { get; }

            public Result(Vector3 direction, object target, float correctionDegrees)
            {
                Direction = direction;
                Target = target;
                CorrectionDegrees = correctionDegrees;
            }

            public bool Corrected => Target != null;
        }

        /// Picks the candidate that needs the least correction, not the nearest one:
        /// snapping to whatever is physically closest feels like the game overriding the
        /// player, while snapping to whatever they were most plausibly aiming at does not.
        public static Result Resolve(Vector3 origin, Vector3 aimDirection, IReadOnlyList<Candidate> candidates,
            float maxRange, float maxCorrectionDegrees = MaxCorrectionDegrees)
        {
            var aim = aimDirection;
            aim.y = 0f;
            if (aim.sqrMagnitude < 1e-6f) return new Result(aimDirection.normalized, null, 0f);
            aim.Normalize();

            if (candidates == null || candidates.Count == 0 || maxCorrectionDegrees <= 0f)
                return new Result(aim, null, 0f);

            object best = null;
            float bestAngle = maxCorrectionDegrees;
            Vector3 bestDirection = aim;

            foreach (var candidate in candidates)
            {
                var offset = candidate.Position - origin;
                offset.y = 0f;

                float distance = offset.magnitude;
                if (distance > maxRange || distance < 1e-4f) continue;

                var toTarget = offset / distance;
                float angle = Vector3.Angle(aim, toTarget);

                if (angle <= bestAngle)
                {
                    bestAngle = angle;
                    best = candidate.Owner;
                    bestDirection = toTarget;
                }
            }

            return best == null
                ? new Result(aim, null, 0f)
                : new Result(bestDirection, best, bestAngle);
        }

        /// Whether a swing along `direction` connects with a point, using a widening arc so
        /// that contact at the edge of the sweep still registers.
        public static bool SweepHits(Vector3 origin, Vector3 direction, Vector3 point,
            float range, float arcDegrees)
        {
            var offset = point - origin;
            offset.y = 0f;

            float distance = offset.magnitude;
            if (distance > range) return false;
            // Anything on top of the attacker is hit regardless of facing.
            if (distance < 1e-4f) return true;

            var flatDirection = direction;
            flatDirection.y = 0f;
            if (flatDirection.sqrMagnitude < 1e-6f) return false;

            float angle = Vector3.Angle(flatDirection.normalized, offset / distance);
            return angle <= arcDegrees * 0.5f;
        }
    }
}
