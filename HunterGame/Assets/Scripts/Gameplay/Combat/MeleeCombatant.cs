using System.Collections;
using System.Collections.Generic;
using Hunter.Gameplay.Actors;
using UnityEngine;

namespace Hunter.Gameplay.Combat
{
    /// Runtime melee. Owns the three layers of hit feedback the design calls for:
    /// audio, camera impulse and a victim flash, all fired on the same frame damage lands,
    /// plus a hitstop freeze so the blow has weight.
    [RequireComponent(typeof(Damageable))]
    public class MeleeCombatant : MonoBehaviour
    {
        [SerializeField] MeleeProfile profile = MeleeProfile.Falchion();
        [SerializeField] LayerMask targetMask = ~0;
        [SerializeField] Transform swingOrigin;

        [Header("Aim assist")]
        [Tooltip("Off in hardcore mode; the player keeps full responsibility for aim.")]
        [SerializeField] bool softLockEnabled = true;
        [SerializeField, Range(0f, 45f)] float softLockDegrees = SoftLock.MaxCorrectionDegrees;

        [Header("Feedback")]
        [SerializeField] AudioSource audioSource;
        [SerializeField] AudioClip swingClip;
        [SerializeField] AudioClip hitClip;

        MeleeStateMachine _machine;
        Damageable _self;
        HunterController _locomotion;
        OverShoulderCamera _camera;
        readonly HashSet<Damageable> _hitThisSwing = new();
        readonly List<SoftLock.Candidate> _candidateBuffer = new();
        static Coroutine _activeHitStop;

        public MeleeProfile Profile => profile;
        public AttackPhase Phase => _machine.Phase;
        public float WeaponDamageBonus { get; set; }

        void Awake()
        {
            _machine = new MeleeStateMachine(profile);
            _self = GetComponent<Damageable>();
            _locomotion = GetComponent<HunterController>();
            if (swingOrigin == null) swingOrigin = transform;
        }

        public void BindCamera(OverShoulderCamera cam) => _camera = cam;

        public bool TryAttack()
        {
            if (_self.IsDead) return false;
            if (!_machine.CanStartAttack) return _machine.TryStartAttack();
            if (!_self.TrySpendStamina(profile.StaminaCost)) return false;

            if (!_machine.TryStartAttack()) return false;

            _hitThisSwing.Clear();
            AimAtNearestTarget();
            if (audioSource != null && swingClip != null) audioSource.PlayOneShot(swingClip, 0.7f);
            return true;
        }

        void AimAtNearestTarget()
        {
            if (!softLockEnabled || softLockDegrees <= 0f) return;

            CollectCandidates();
            var result = SoftLock.Resolve(swingOrigin.position, transform.forward, _candidateBuffer,
                profile.Range * 1.35f, softLockDegrees);

            if (result.Corrected && _locomotion != null)
            {
                // Snap rather than lerp: the correction has to be complete before the
                // hitbox opens, otherwise it just makes the swing feel drifty.
                _locomotion.FaceDirection(result.Direction, 1f, sharpness: 1f);
            }
        }

        void CollectCandidates()
        {
            _candidateBuffer.Clear();
            var hits = Physics.OverlapSphere(swingOrigin.position, profile.Range * 1.35f, targetMask,
                QueryTriggerInteraction.Ignore);

            foreach (var collider in hits)
            {
                var damageable = collider.GetComponentInParent<Damageable>();
                if (damageable == null || damageable == _self || damageable.IsDead) continue;
                _candidateBuffer.Add(new SoftLock.Candidate(damageable.transform.position, damageable));
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            var previousPhase = _machine.Phase;
            _machine.Tick(dt);

            // Attacks commit the hunter: movement is cut hard during startup and active
            // frames, which is what makes whiffing a real cost.
            if (_locomotion != null)
            {
                _locomotion.MovementScale = _machine.Phase switch
                {
                    AttackPhase.Startup => 0.25f,
                    AttackPhase.Active => 0.15f,
                    AttackPhase.Recovery => 0.6f,
                    _ => 1f,
                };
            }

            if (_machine.Phase == AttackPhase.Active) ResolveSweep();
            else if (previousPhase == AttackPhase.Active) _hitThisSwing.Clear();
        }

        void ResolveSweep()
        {
            CollectCandidates();
            foreach (var candidate in _candidateBuffer)
            {
                if (candidate.Owner is not Damageable victim) continue;
                if (_hitThisSwing.Contains(victim)) continue;

                if (!SoftLock.SweepHits(swingOrigin.position, transform.forward,
                        victim.transform.position, profile.Range, profile.ArcDegrees)) continue;

                _hitThisSwing.Add(victim);
                Connect(victim);
            }
        }

        void Connect(Damageable victim)
        {
            bool finisher = _machine.IsFinisher;
            float damage = profile.DamageFor(finisher, WeaponDamageBonus);
            var direction = (victim.transform.position - transform.position).normalized;

            victim.ApplyDamage(damage, direction);
            _machine.NotifyConnected();

            // Layer 1: audio.
            if (audioSource != null && hitClip != null)
                audioSource.PlayOneShot(hitClip, finisher ? 1f : 0.8f);

            // Layer 2: camera impulse.
            if (_camera != null) _camera.AddShake(profile.CameraShake * (finisher ? 1.6f : 1f));

            // Layer 3: the victim flash is raised inside Damageable.ApplyDamage.

            StartHitStop(profile.HitStopFor(finisher));
        }

        /// Global freeze, deliberately. On a single-player game the whole frame stopping is
        /// what sells impact; a per-actor pause reads as a hitch instead.
        void StartHitStop(float seconds)
        {
            if (seconds <= 0f) return;
            if (_activeHitStop != null) return;
            _activeHitStop = StartCoroutine(HitStopRoutine(seconds));
        }

        static IEnumerator HitStopRoutine(float seconds)
        {
            float previous = Time.timeScale;
            Time.timeScale = 0.04f;
            yield return new WaitForSecondsRealtime(seconds);
            Time.timeScale = previous;
            _activeHitStop = null;
        }

        void OnDisable()
        {
            if (_activeHitStop != null)
            {
                StopCoroutine(_activeHitStop);
                _activeHitStop = null;
                Time.timeScale = 1f;
            }
        }
    }
}
