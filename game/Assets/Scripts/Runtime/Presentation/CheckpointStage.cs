using System;
using Monster.Rules;
using Monster.Shift;
using UnityEngine;

namespace Monster.Presentation
{
    /// <summary>Everything that happens on the other side of the glass.
    ///
    /// Until now the only sign that a new vehicle had arrived was that the paperwork on the
    /// desk changed. That is the difference between a form-filling exercise and a
    /// checkpoint: the player has to see something pull up, wait while they read, and
    /// either drive through or back away.
    ///
    /// Every pose is reachable directly through <see cref="SetPhase"/> with
    /// <c>immediate</c>, so the automated self-check can photograph a raised barrier or a
    /// departing vehicle without waiting on an animation and without the image depending on
    /// exactly which frame it landed on.</summary>
    public sealed class CheckpointStage : MonoBehaviour
    {
        public enum Phase
        {
            /// <summary>Empty road. The barrier is down.</summary>
            Clear,

            /// <summary>Headlights in the fog, closing on the barrier.</summary>
            Approaching,

            /// <summary>Stopped at the line with the subject out of the cab.</summary>
            AtTheWindow,

            /// <summary>Barrier up, driving through.</summary>
            Admitted,

            /// <summary>Barrier down, reversing back into the fog.</summary>
            TurnedAway,
        }

        [Header("Rigging")]
        [SerializeField] private Transform barrierArm;
        [SerializeField] private Transform vehicle;
        [SerializeField] private Transform subject;
        [SerializeField] private Light[] headlights = Array.Empty<Light>();
        [SerializeField] private Light[] taillights = Array.Empty<Light>();

        [Header("Road positions, metres along +Z from the booth")]
        [SerializeField] private float fogLine = 27f;
        [SerializeField] private float stopLine = 8.2f;
        [SerializeField] private float pastTheBarrier = -7f;

        [Header("Timing, seconds")]
        [SerializeField] private float approachSeconds = 3.4f;
        [SerializeField] private float departSeconds = 3.0f;
        [SerializeField] private float barrierSeconds = 1.6f;

        [Header("Barrier")]
        [SerializeField] private float raisedAngle = -82f;

        private Phase _phase = Phase.Clear;
        private float _phaseTime;
        private float _barrier;          // 0 down, 1 up
        private float _barrierTarget;
        private float _headlightBase = 1f;
        private Vector3 _subjectHome;

        public Phase Current => _phase;

        /// <summary>True once the current phase has run its course, so the presenter knows
        /// when a departing vehicle has cleared the frame.</summary>
        public bool PhaseComplete => _phase switch
        {
            Phase.Approaching => _phaseTime >= approachSeconds,
            Phase.Admitted or Phase.TurnedAway => _phaseTime >= departSeconds,
            _ => true,
        };

        public void Rig(Transform arm, Transform vehicleRoot, Transform subjectRoot,
            Light[] head, Light[] tail)
        {
            barrierArm = arm;
            vehicle = vehicleRoot;
            subject = subjectRoot;
            headlights = head ?? Array.Empty<Light>();
            taillights = tail ?? Array.Empty<Light>();
        }

        private void Awake()
        {
            if (subject != null)
            {
                _subjectHome = subject.localPosition;
            }

            if (headlights.Length > 0 && headlights[0] != null)
            {
                _headlightBase = headlights[0].intensity;
            }

            SetPhase(Phase.AtTheWindow, immediate: true);
        }

        public void SetPhase(Phase phase, bool immediate = false)
        {
            _phase = phase;
            _phaseTime = 0f;

            _barrierTarget = phase == Phase.Admitted ? 1f : 0f;
            if (immediate)
            {
                _barrier = _barrierTarget;
                _phaseTime = phase switch
                {
                    Phase.Admitted or Phase.TurnedAway => departSeconds * 0.55f,
                    Phase.Approaching => approachSeconds * 0.80f,
                    _ => 0f,
                };
                Apply();
            }
        }

        /// <summary>Chooses the phase a verdict leads to. Only a pass opens the barrier;
        /// everything else backs the vehicle out, which is deliberately the same picture
        /// whether the player held, referred or alarmed. The player is not told which of
        /// their refusals was the right one.</summary>
        public static Phase PhaseFor(Verdict verdict) =>
            verdict == Verdict.Pass ? Phase.Admitted : Phase.TurnedAway;

        private void Update()
        {
            _phaseTime += Time.deltaTime;

            var rate = barrierSeconds > 0.01f ? Time.deltaTime / barrierSeconds : 1f;
            _barrier = Mathf.MoveTowards(_barrier, _barrierTarget, rate);

            Apply();
        }

        private void Apply()
        {
            if (barrierArm != null)
            {
                // Eased, because a boom gate is heavy and a linear lift reads as weightless.
                var eased = _barrier * _barrier * (3f - 2f * _barrier);
                barrierArm.localRotation = Quaternion.Euler(0f, 0f, raisedAngle * eased);
            }

            var z = RoadPosition();

            if (vehicle != null)
            {
                var position = vehicle.localPosition;
                vehicle.localPosition = new Vector3(position.x, position.y, z);
            }

            if (subject != null)
            {
                // The subject is only out of the cab while the papers are being read.
                var present = _phase == Phase.AtTheWindow;
                subject.gameObject.SetActive(present);

                if (present)
                {
                    // A very slight sway, the same for everyone. It is atmosphere, not
                    // information: a tell that is not in the manual would make the game
                    // unfair, and the fairness property is enforced by a test.
                    var sway = Mathf.Sin(Time.time * 0.55f) * 0.006f;
                    subject.localPosition = _subjectHome + new Vector3(sway, 0f, sway * 0.4f);
                }
            }

            var distance = Mathf.InverseLerp(stopLine, fogLine, z);
            foreach (var light in headlights)
            {
                if (light == null)
                {
                    continue;
                }

                light.enabled = _phase != Phase.Clear;

                // Brighter the further away they are, which is how oncoming headlights read
                // through fog: a diffuse glare at distance that resolves into two lamps.
                light.intensity = _headlightBase * Mathf.Lerp(1f, 2.4f, distance);
            }

            foreach (var light in taillights)
            {
                if (light != null)
                {
                    light.enabled = _phase != Phase.Clear;
                }
            }
        }

        private float RoadPosition()
        {
            switch (_phase)
            {
                case Phase.Clear:
                    return fogLine;
                case Phase.Approaching:
                    return Mathf.Lerp(fogLine, stopLine, Smooth(_phaseTime / approachSeconds));
                case Phase.AtTheWindow:
                    return stopLine;
                case Phase.Admitted:
                    return Mathf.Lerp(stopLine, pastTheBarrier, Smooth(_phaseTime / departSeconds));
                case Phase.TurnedAway:
                    return Mathf.Lerp(stopLine, fogLine, Smooth(_phaseTime / departSeconds));
                default:
                    return stopLine;
            }
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
