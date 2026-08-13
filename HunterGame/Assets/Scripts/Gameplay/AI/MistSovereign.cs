using Hunter.Gameplay.Actors;
using Hunter.Gameplay.Run;
using UnityEngine;

namespace Hunter.Gameplay.AI
{
    /// The unkillable pursuer that wakes partway through a raid.
    ///
    /// Market research 3.4 records why this exists: DRG: Rogue Core turns a flat
    /// procedural map tense with nothing more than a timer and a thing that cannot be
    /// fought. One enemy and one clock is the cheapest tension in the genre, and unlike
    /// a boss it needs no encounter design.
    ///
    /// It never sprints down a fleeing player outright. It closes slowly, forever, so the
    /// pressure is on the decision to keep looting rather than on reflexes.
    public class MistSovereign : MonoBehaviour
    {
        [Header("Pursuit")]
        [Tooltip("Speed on waking. Below a jogging hunter, so looting is what loses ground.")]
        [SerializeField] float baseSpeed = 2.05f;
        [Tooltip("Speed at the end of the raid. Still below a sprint.")]
        [SerializeField] float finalSpeed = 4.5f;
        [SerializeField] float turnSpeed = 2.2f;

        [Header("Pressure")]
        [Tooltip("Within this radius the hunter is 'being hunted' and valuation tightens.")]
        [SerializeField] float dreadRadius = 26f;
        [SerializeField] float killRadius = 2.1f;
        [SerializeField] float threatPerSecondInDread = 0.42f;

        [Header("Presence")]
        [SerializeField] Light auraLight;
        [SerializeField] float auraBaseIntensity = 5f;
        [SerializeField] float auraPulseSpeed = 1.7f;

        [Header("Wiring")]
        [SerializeField] RunController run;
        [SerializeField] Transform quarry;

        RunDirector _director;

        public bool Awakened { get; private set; }
        public float DistanceToQuarry { get; private set; } = float.MaxValue;

        /// True while the sovereign is close enough to change how a hunter values loot.
        public bool IsPressuring => Awakened && DistanceToQuarry <= dreadRadius;

        /// Editor-time wiring: writes serialized references only.
        public void Bind(RunController runController, Transform target)
        {
            run = runController;
            quarry = target;
        }

        void Start()
        {
            if (run == null) run = FindFirstObjectByType<RunController>();
            if (quarry == null)
            {
                var hunter = FindFirstObjectByType<Actors.HunterController>();
                if (hunter != null) quarry = hunter.transform;
            }

            _director = run != null ? run.Director : null;
            if (_director != null) _director.SovereignAwakened += Awaken;

            // Dormant until its hour; the renderer stays off but Start must have run.
            SetDormant(true);
        }

        void SetDormant(bool dormant)
        {
            foreach (var renderer in GetComponentsInChildren<Renderer>(true)) renderer.enabled = !dormant;
            foreach (var light in GetComponentsInChildren<Light>(true)) light.enabled = !dormant;
        }

        void OnDestroy()
        {
            if (_director != null) _director.SovereignAwakened -= Awaken;
        }

        public void Awaken()
        {
            if (Awakened) return;
            Awakened = true;
            SetDormant(false);
        }

        void Update()
        {
            if (!Awakened || quarry == null) return;

            float dt = Time.deltaTime;
            var toQuarry = quarry.position - transform.position;
            toQuarry.y = 0f;
            DistanceToQuarry = toQuarry.magnitude;

            // Speed ramps with the run clock, so late in a raid the walls close in.
            float progress = _director?.RunProgress ?? 0f;
            float speed = Mathf.Lerp(baseSpeed, finalSpeed, Mathf.SmoothStep(0f, 1f, progress));

            if (DistanceToQuarry > 1e-3f)
            {
                var direction = toQuarry / DistanceToQuarry;
                var facing = Vector3.RotateTowards(transform.forward, direction, turnSpeed * dt, 0f);
                transform.rotation = Quaternion.LookRotation(facing, Vector3.up);
                transform.position += direction * (speed * dt);
            }

            if (_director != null && IsPressuring)
            {
                // Closer means more dread, which raises the valuation bar and pushes a
                // loaded hunter toward the bell.
                float closeness = 1f - Mathf.Clamp01(DistanceToQuarry / dreadRadius);
                _director.AddThreat(threatPerSecondInDread * closeness * dt);
            }

            if (DistanceToQuarry <= killRadius)
            {
                var damageable = quarry.GetComponent<Damageable>();
                if (damageable != null && !damageable.IsDead)
                    damageable.ApplyDamage(damageable.MaxHealth, transform.forward);
                else
                    _director?.Kill();
            }

            if (auraLight != null)
            {
                float pulse = 0.75f + Mathf.Sin(Time.time * auraPulseSpeed) * 0.25f;
                auraLight.intensity = auraBaseIntensity * pulse;
            }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, dreadRadius);
            Gizmos.color = new Color(1f, 0.1f, 0.05f, 0.7f);
            Gizmos.DrawWireSphere(transform.position, killRadius);
        }
    }
}
