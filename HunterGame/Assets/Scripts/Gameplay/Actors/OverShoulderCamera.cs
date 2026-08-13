using UnityEngine;

namespace Hunter.Gameplay.Actors
{
    /// The over-the-shoulder rig specified in docs/01-game-concepts.md A.3: 2.8m back,
    /// 0.4m above chest height, 0.55m right shoulder offset, 60 degree FOV widening to 68
    /// on a sprint and narrowing to 55 when aiming. Spring arm pulls in to 0.9m minimum
    /// against geometry, which the ruin's narrow colonnade needs constantly.
    public class OverShoulderCamera : MonoBehaviour
    {
        [Header("Rig")]
        [SerializeField] Transform target;
        [SerializeField] float distance = 2.8f;
        [SerializeField] float minDistance = 0.9f;
        [SerializeField] float heightAboveChest = 0.4f;
        [SerializeField] float chestHeight = 1.35f;
        [SerializeField] float shoulderOffset = 0.55f;

        [Header("Field of view")]
        [SerializeField] float baseFov = 60f;
        [SerializeField] float sprintFov = 68f;
        [SerializeField] float aimFov = 55f;
        [SerializeField] float fovLerp = 7f;

        [Header("Look")]
        [SerializeField] float pitchMin = -35f;
        [SerializeField] float pitchMax = 62f;
        [SerializeField] float sensitivity = 0.14f;
        [SerializeField] float followSharpness = 18f;

        [Header("Collision")]
        [SerializeField] LayerMask collisionMask = ~0;
        [SerializeField] float probeRadius = 0.22f;

        [Header("Shake")]
        [SerializeField] float shakeDecay = 6.5f;
        [SerializeField] float shakeMagnitude = 0.28f;

        Camera _camera;
        float _yaw;
        float _pitch = 8f;
        float _currentDistance;
        float _shake;
        Vector3 _shakeOffset;

        public Transform Target { get => target; set => target = value; }
        public float Yaw => _yaw;

        /// Flattened camera forward, which is what movement input is expressed relative to.
        public Vector3 PlanarForward
        {
            get
            {
                var forward = transform.forward;
                forward.y = 0f;
                return forward.sqrMagnitude < 1e-5f ? Vector3.forward : forward.normalized;
            }
        }

        public Vector3 PlanarRight
        {
            get
            {
                var right = transform.right;
                right.y = 0f;
                return right.sqrMagnitude < 1e-5f ? Vector3.right : right.normalized;
            }
        }

        void Awake()
        {
            _camera = GetComponent<Camera>();
            _currentDistance = distance;
            if (target != null) _yaw = target.eulerAngles.y;
        }

        public void AddLook(Vector2 delta)
        {
            _yaw += delta.x * sensitivity;
            _pitch = Mathf.Clamp(_pitch - delta.y * sensitivity, pitchMin, pitchMax);
        }

        /// Layer 2 of the hit feedback: an impulse the player feels rather than reads.
        public void AddShake(float amount) => _shake = Mathf.Min(1.5f, _shake + amount);

        public void Tick(float deltaTime, bool sprinting, bool aiming)
        {
            if (target == null) return;

            var rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            var pivot = target.position + Vector3.up * (chestHeight + heightAboveChest);
            pivot += rotation * Vector3.right * shoulderOffset;

            // Spring arm. Without this the camera buries itself in a column every time the
            // player backs into one, which in a corridor-heavy ruin is constantly.
            float desiredDistance = distance;
            var back = rotation * Vector3.back;
            if (Physics.SphereCast(pivot, probeRadius, back, out var hit, distance, collisionMask,
                    QueryTriggerInteraction.Ignore))
            {
                desiredDistance = Mathf.Max(minDistance, hit.distance - probeRadius * 0.5f);
            }

            // Pull in instantly, ease back out: snapping outward reveals geometry pops.
            _currentDistance = desiredDistance < _currentDistance
                ? desiredDistance
                : Mathf.Lerp(_currentDistance, desiredDistance, 6f * deltaTime);

            var wanted = pivot + back * _currentDistance;

            if (_shake > 0f)
            {
                _shake = Mathf.MoveTowards(_shake, 0f, shakeDecay * deltaTime);
                _shakeOffset = new Vector3(
                    (Mathf.PerlinNoise(Time.time * 34f, 0f) - 0.5f),
                    (Mathf.PerlinNoise(0f, Time.time * 31f) - 0.5f),
                    0f) * (_shake * shakeMagnitude);
            }
            else _shakeOffset = Vector3.zero;

            transform.position = Vector3.Lerp(transform.position, wanted, followSharpness * deltaTime)
                                 + rotation * _shakeOffset;
            transform.rotation = rotation;

            if (_camera != null)
            {
                float targetFov = aiming ? aimFov : (sprinting ? sprintFov : baseFov);
                _camera.fieldOfView = Mathf.Lerp(_camera.fieldOfView, targetFov, fovLerp * deltaTime);
            }
        }

        /// Places the rig at its resting pose without interpolation. Used when spawning and
        /// by the screenshot tooling, which renders a single frame and cannot wait for a
        /// lerp to settle.
        public void SnapToTarget()
        {
            if (target == null) return;

            var rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            var pivot = target.position + Vector3.up * (chestHeight + heightAboveChest);
            pivot += rotation * Vector3.right * shoulderOffset;

            _currentDistance = distance;
            transform.position = pivot + rotation * Vector3.back * distance;
            transform.rotation = rotation;
            if (_camera != null) _camera.fieldOfView = baseFov;
        }
    }
}
