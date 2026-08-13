using Hunter.Gameplay.Combat;
using UnityEngine;

namespace Hunter.Gameplay.Actors
{
    /// Movement and combat animation driven by code rather than by skeletal clips.
    ///
    /// The project has no animator, no rig and no motion capture. A static mesh sliding
    /// across the floor is the single most obvious tell that something is a prototype, and
    /// it is also the one thing procedural generation cannot fix at the mesh level. What it
    /// can fix is the body's motion: a walk cycle is mostly vertical bob, lateral sway,
    /// forward lean and a swinging arm, all of which are cheap functions of speed and time.
    ///
    /// This is not a substitute for real animation on a released game, but it removes the
    /// "sliding statue" read and gives the hunter weight.
    public class ProceduralHunterAnimator : MonoBehaviour
    {
        [Header("Bindings")]
        [SerializeField] Transform body;
        [SerializeField] Transform lantern;
        [SerializeField] HunterController locomotion;
        [SerializeField] MeleeCombatant combat;

        [Header("Walk cycle")]
        [Tooltip("Strides per second at full jog.")]
        [SerializeField] float strideFrequency = 1.85f;
        [SerializeField] float bobHeight = 0.055f;
        [SerializeField] float swayAngle = 3.4f;
        [SerializeField] float leanAngle = 7.5f;

        [Header("Lantern")]
        [Tooltip("The lantern lags the body, which is what makes it read as hanging.")]
        [SerializeField] float lanternLag = 6.5f;
        [SerializeField] float lanternSwing = 0.075f;

        [Header("Attack")]
        [SerializeField] float swingTwist = 42f;
        [SerializeField] float swingLunge = 0.22f;

        Vector3 _bodyRest;
        Vector3 _lanternRest;
        float _phase;
        Vector3 _lanternVelocity;
        Vector3 _lanternOffset;
        float _attackBlend;

        void Awake()
        {
            if (body != null) _bodyRest = body.localPosition;
            if (lantern != null) _lanternRest = lantern.localPosition;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            float speed01 = locomotion != null ? Mathf.Clamp01(locomotion.SpeedNormalised * 1.6f) : 0f;

            // Stride rate rises with speed, so a sprint reads as a sprint rather than a
            // fast-forwarded walk.
            _phase += dt * strideFrequency * Mathf.PI * 2f * Mathf.Lerp(0.55f, 1.5f, speed01);

            AnimateBody(speed01, dt);
            AnimateLantern(speed01, dt);
        }

        void AnimateBody(float speed01, float dt)
        {
            if (body == null) return;

            // Two bobs per stride: the body rises on each footfall.
            float bob = Mathf.Abs(Mathf.Sin(_phase)) * bobHeight * speed01;
            float sway = Mathf.Sin(_phase * 0.5f) * swayAngle * speed01;

            bool attacking = combat != null && combat.Phase != AttackPhase.Idle;
            float attackTarget = attacking ? 1f : 0f;
            _attackBlend = Mathf.MoveTowards(_attackBlend, attackTarget, dt * (attacking ? 14f : 5f));

            float twist = 0f;
            float lunge = 0f;
            if (_attackBlend > 0.001f && combat != null)
            {
                // Wind up away from the target, then rotate through it on the active frames.
                twist = combat.Phase switch
                {
                    AttackPhase.Startup => -swingTwist * 0.55f,
                    AttackPhase.Active => swingTwist,
                    AttackPhase.Recovery => swingTwist * 0.35f,
                    _ => 0f,
                } * _attackBlend;

                lunge = combat.Phase == AttackPhase.Active ? swingLunge * _attackBlend : 0f;
            }

            body.localPosition = _bodyRest + new Vector3(0f, bob, lunge);
            body.localRotation = Quaternion.Euler(
                Mathf.Lerp(0f, leanAngle, speed01),
                twist,
                sway);
        }

        void AnimateLantern(float speed01, float dt)
        {
            if (lantern == null) return;

            // Critically damped spring toward a target that swings with the stride. Letting
            // the lamp trail the body is most of what makes it look carried.
            var target = new Vector3(
                Mathf.Sin(_phase * 0.5f) * lanternSwing * speed01,
                -Mathf.Abs(Mathf.Cos(_phase)) * lanternSwing * 0.6f * speed01,
                Mathf.Cos(_phase * 0.5f) * lanternSwing * 0.5f * speed01);

            _lanternOffset = Vector3.SmoothDamp(_lanternOffset, target, ref _lanternVelocity,
                1f / lanternLag, Mathf.Infinity, dt);

            lantern.localPosition = _lanternRest + _lanternOffset;
            lantern.localRotation = Quaternion.Euler(_lanternOffset.z * 180f, 0f, -_lanternOffset.x * 220f);
        }

        /// Used by the screenshot tooling to freeze a representative mid-stride pose
        /// without entering play mode.
        public void PoseForCapture(float phase, float speed01)
        {
            if (body != null && _bodyRest == Vector3.zero) _bodyRest = body.localPosition;
            if (lantern != null && _lanternRest == Vector3.zero) _lanternRest = lantern.localPosition;

            _phase = phase;

            if (body != null)
            {
                body.localPosition = _bodyRest + new Vector3(0f, Mathf.Abs(Mathf.Sin(phase)) * bobHeight * speed01, 0f);
                body.localRotation = Quaternion.Euler(
                    Mathf.Lerp(0f, leanAngle, speed01), 0f,
                    Mathf.Sin(phase * 0.5f) * swayAngle * speed01);
            }

            if (lantern != null)
            {
                var offset = new Vector3(
                    Mathf.Sin(phase * 0.5f) * lanternSwing * speed01,
                    -Mathf.Abs(Mathf.Cos(phase)) * lanternSwing * 0.6f * speed01,
                    Mathf.Cos(phase * 0.5f) * lanternSwing * 0.5f * speed01);
                lantern.localPosition = _lanternRest + offset;
                lantern.localRotation = Quaternion.Euler(offset.z * 180f, 0f, -offset.x * 220f);
            }
        }
    }
}
