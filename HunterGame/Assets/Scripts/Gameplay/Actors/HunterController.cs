using Hunter.Gameplay.Items;
using UnityEngine;

namespace Hunter.Gameplay.Actors
{
    /// Player locomotion. Movement speed is driven by the same Inventory the loot systems
    /// use, so the weight of a haul is felt in the hands rather than read off a menu.
    [RequireComponent(typeof(CharacterController))]
    public class HunterController : MonoBehaviour
    {
        [Header("Speeds (m/s)")]
        [SerializeField] float walkSpeed = 2.4f;
        [SerializeField] float jogSpeed = 4.3f;
        [SerializeField] float sprintSpeed = 6.6f;
        [SerializeField] float acceleration = 22f;
        [SerializeField] float turnSharpness = 14f;

        [Header("Sprint")]
        [SerializeField] float sprintStaminaPerSecond = 12f;
        [SerializeField] float minStaminaToSprint = 12f;

        [Header("Physics")]
        [SerializeField] float gravity = -22f;
        [SerializeField] float groundedStick = -2f;

        CharacterController _controller;
        Damageable _damageable;
        Vector3 _velocity;
        float _verticalVelocity;
        float _currentSpeed;

        /// Assigned by whatever owns the run so the carried weight affects movement.
        public Inventory Inventory { get; set; }

        public bool IsSprinting { get; private set; }
        public float SpeedNormalised => sprintSpeed <= 0f ? 0f : _currentSpeed / sprintSpeed;
        public Vector3 PlanarVelocity => new(_velocity.x, 0f, _velocity.z);

        /// Set by the combat component during committed attack frames.
        public float MovementScale { get; set; } = 1f;

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _damageable = GetComponent<Damageable>();
        }

        /// <param name="moveInput">Camera-relative input on the XZ plane, magnitude 0..1.</param>
        public void Move(Vector2 moveInput, bool sprintHeld, float deltaTime)
        {
            var input = new Vector3(moveInput.x, 0f, moveInput.y);
            if (input.sqrMagnitude > 1f) input.Normalize();

            bool wantsSprint = sprintHeld && input.sqrMagnitude > 0.25f;
            bool hasStamina = _damageable == null || _damageable.Stamina > minStaminaToSprint;
            IsSprinting = wantsSprint && hasStamina;

            if (IsSprinting && _damageable != null)
                _damageable.TrySpendStamina(sprintStaminaPerSecond * deltaTime);

            float targetSpeed = input.sqrMagnitude < 0.04f
                ? 0f
                : (IsSprinting ? sprintSpeed : (input.magnitude < 0.6f ? walkSpeed : jogSpeed));

            // Encumbrance and attack commitment both scale the same value, so a heavy
            // hunter mid-swing is exactly as sluggish as both penalties imply.
            targetSpeed *= Inventory?.SpeedMultiplier ?? 1f;
            targetSpeed *= Mathf.Clamp01(MovementScale);

            _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSpeed, acceleration * deltaTime);

            var desired = input.sqrMagnitude > 1e-4f ? input.normalized : Vector3.zero;
            _velocity = Vector3.MoveTowards(_velocity, desired * _currentSpeed, acceleration * deltaTime);

            if (_controller.isGrounded && _verticalVelocity < 0f) _verticalVelocity = groundedStick;
            else _verticalVelocity += gravity * deltaTime;

            var motion = _velocity;
            motion.y = _verticalVelocity;
            _controller.Move(motion * deltaTime);

            if (desired.sqrMagnitude > 1e-4f)
            {
                var look = Quaternion.LookRotation(desired, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, look, turnSharpness * deltaTime);
            }
        }

        /// Snap the facing without moving, used when an attack soft-locks onto a target.
        public void FaceDirection(Vector3 direction, float deltaTime, float sharpness = 22f)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-4f) return;

            var look = Quaternion.LookRotation(direction.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, sharpness * deltaTime);
        }
    }
}
