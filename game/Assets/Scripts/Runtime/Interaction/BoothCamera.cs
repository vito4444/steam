using UnityEngine;

namespace Monster.Interaction
{
    /// <summary>The seated camera.
    ///
    /// The player never walks. Look is clamped to the arc a person can turn their head and
    /// eyes through while sitting at the desk, which is both the concept's largest scope
    /// saving and the reason the screenshot self-check is precise: the camera has one home
    /// pose it always returns to, so run-to-run image comparison is meaningful.
    ///
    /// Focusing an object does not move the player. The camera swings and pushes in
    /// towards the object as if the person leaned over it, then returns.</summary>
    public sealed class BoothCamera : MonoBehaviour
    {
        [Header("Home pose")]
        [SerializeField] private Vector3 homePosition = new(-0.03f, 1.44f, -0.76f);
        [SerializeField] private Vector2 homeAngles = new(13f, 0f);

        [Header("Look limits, degrees from home")]
        [SerializeField] private float yawLimit = 62f;
        [SerializeField] private float pitchUpLimit = 26f;
        [SerializeField] private float pitchDownLimit = 30f;

        [Header("Motion")]
        [SerializeField] private float focusApproach = 7.5f;
        [SerializeField] private float returnApproach = 5.5f;

        private Vector2 _lookAngles;
        private Transform _focusTarget;
        private float _focusDistance;
        private Vector3 _focusUpBias;

        public bool IsFocused => _focusTarget != null;

        public Vector3 HomePosition => homePosition;

        private void Awake() => ResetToHome();

        /// <summary>Puts the camera back exactly where it started. Called by the self-check
        /// before every screenshot so no capture depends on what happened before it.</summary>
        public void ResetToHome()
        {
            _focusTarget = null;
            _lookAngles = Vector2.zero;
            transform.SetPositionAndRotation(homePosition, Quaternion.Euler(homeAngles.x, homeAngles.y, 0f));
        }

        public void ApplyLook(Vector2 delta)
        {
            if (IsFocused)
            {
                return;
            }

            _lookAngles.x = Mathf.Clamp(_lookAngles.x - delta.y, -pitchUpLimit, pitchDownLimit);
            _lookAngles.y = Mathf.Clamp(_lookAngles.y + delta.x, -yawLimit, yawLimit);
        }

        /// <summary>Aims the camera at a world point without any smoothing. Used by the
        /// self-check to reach a named checkpoint pose deterministically.</summary>
        public void SnapLookAt(Vector3 worldPoint)
        {
            _focusTarget = null;
            var direction = worldPoint - homePosition;
            if (direction.sqrMagnitude < 1e-6f)
            {
                return;
            }

            var rotation = Quaternion.LookRotation(direction, Vector3.up).eulerAngles;
            var pitch = Mathf.DeltaAngle(homeAngles.x, rotation.x);
            var yaw = Mathf.DeltaAngle(homeAngles.y, rotation.y);

            _lookAngles.x = Mathf.Clamp(pitch, -pitchUpLimit, pitchDownLimit);
            _lookAngles.y = Mathf.Clamp(yaw, -yawLimit, yawLimit);

            transform.SetPositionAndRotation(homePosition, TargetRotation());
        }

        /// <summary>Leans over a target with no easing. The self-check needs the pose to
        /// be identical on the frame it photographs, and an eased approach is only ever
        /// asymptotically there.</summary>
        public void SnapFocus(Transform target, float distance, Vector3 upBias)
        {
            Focus(target, distance, upBias);
            var lookAt = target.position;
            var eye = lookAt + upBias.normalized * distance;
            transform.SetPositionAndRotation(eye, Quaternion.LookRotation(lookAt - eye, Vector3.up));
        }

        public void Focus(Transform target, float distance, Vector3 upBias)
        {
            _focusTarget = target;
            _focusDistance = distance;
            _focusUpBias = upBias;
        }

        public void ClearFocus() => _focusTarget = null;

        private Quaternion TargetRotation() =>
            Quaternion.Euler(homeAngles.x + _lookAngles.x, homeAngles.y + _lookAngles.y, 0f);

        private void LateUpdate()
        {
            if (_focusTarget != null)
            {
                var lookAt = _focusTarget.position;
                var eye = lookAt + _focusUpBias.normalized * _focusDistance;
                var rotation = Quaternion.LookRotation(lookAt - eye, Vector3.up);

                var t = 1f - Mathf.Exp(-focusApproach * Time.deltaTime);
                transform.SetPositionAndRotation(
                    Vector3.Lerp(transform.position, eye, t),
                    Quaternion.Slerp(transform.rotation, rotation, t));
                return;
            }

            var back = 1f - Mathf.Exp(-returnApproach * Time.deltaTime);
            transform.SetPositionAndRotation(
                Vector3.Lerp(transform.position, homePosition, back),
                Quaternion.Slerp(transform.rotation, TargetRotation(), back));
        }
    }
}
