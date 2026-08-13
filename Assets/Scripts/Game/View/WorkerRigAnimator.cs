using UnityEngine;
using Worker.Core;

namespace Worker.Game
{
    /// <summary>
    /// Animates one worker figure: gait, facing, and separation from the people around
    /// them.
    ///
    /// Everything here is presentation only. The simulation places workers on whole
    /// tiles and knows nothing about any of this, which matters: a visual nudge that
    /// fed back into <see cref="SimWorld"/> would break replay determinism and make
    /// saves diverge. The figure is therefore allowed to stand slightly off its tile,
    /// and the tile remains the truth.
    ///
    /// A video review called the workers "chess pieces sliding around" and flagged them
    /// passing through each other. Both are addressed here rather than in the model.
    /// </summary>
    public sealed class WorkerRigAnimator : MonoBehaviour
    {
        public Transform Body;
        public Transform Head;
        public Transform Helmet;
        public Transform Brim;
        public Transform LeftArm;
        public Transform RightArm;
        public Transform Carried;

        /// <summary>Ground position the simulation says this worker occupies.</summary>
        public Vector3 TargetPosition;

        /// <summary>True while the worker is moving between tiles.</summary>
        public bool Walking;

        /// <summary>Nudge applied to keep figures from standing inside each other.</summary>
        public Vector3 Separation;

        private const float StrideHz = 3.4f;
        private const float BobHeight = 0.055f;
        private const float ArmSwing = 26f;

        private float _phase;
        private float _walkWeight;
        private float _facing;
        private Vector3 _smoothedPosition;
        private Vector3 _baseBodyLocal;
        private Vector3 _baseHeadLocal;
        private Vector3 _baseHelmetLocal;
        private Vector3 _baseBrimLocal;
        private bool _initialised;

        private void LateUpdate()
        {
            if (!_initialised)
            {
                if (Body != null) _baseBodyLocal = Body.localPosition;
                if (Head != null) _baseHeadLocal = Head.localPosition;
                if (Helmet != null) _baseHelmetLocal = Helmet.localPosition;
                if (Brim != null) _baseBrimLocal = Brim.localPosition;
                _smoothedPosition = TargetPosition;
                _initialised = true;
            }

            ApplyPosition();
            ApplyFacing();
            ApplyGait();
        }

        private void ApplyPosition()
        {
            var wanted = TargetPosition + Separation;

            // Smoothing the separation rather than snapping it stops two workers who
            // meet head on from vibrating against each other.
            _smoothedPosition = Vector3.Lerp(_smoothedPosition, wanted, 1f - Mathf.Exp(-14f * Time.deltaTime));
            transform.position = _smoothedPosition;
        }

        private void ApplyFacing()
        {
            var travel = TargetPosition - transform.position;
            travel.y = 0f;

            if (travel.sqrMagnitude > 0.0004f)
            {
                float wanted = Mathf.Atan2(travel.x, travel.z) * Mathf.Rad2Deg;
                _facing = Mathf.LerpAngle(_facing, wanted, 1f - Mathf.Exp(-12f * Time.deltaTime));
            }

            transform.rotation = Quaternion.Euler(0f, _facing, 0f);
        }

        private void ApplyGait()
        {
            // Blend in and out of the walk rather than switching. Starting and stopping
            // on a bool made the figures snap between poses, which a review described as
            // freezing mid-step.
            float wanted = Walking ? 1f : 0f;
            _walkWeight = Mathf.Lerp(_walkWeight, wanted, 1f - Mathf.Exp(-9f * Time.deltaTime));

            // Keep the cycle turning while any weight remains, so the legs finish their
            // stride into the idle pose instead of stopping wherever they happened to be.
            if (_walkWeight > 0.01f) _phase += Time.deltaTime * StrideHz * Mathf.PI * 2f;

            float swing = Mathf.Sin(_phase) * _walkWeight;
            float bob = Mathf.Abs(Mathf.Sin(_phase)) * BobHeight * _walkWeight;

            if (Body != null) Body.localPosition = _baseBodyLocal + new Vector3(0f, bob, 0f);
            if (Head != null) Head.localPosition = _baseHeadLocal + new Vector3(0f, bob, 0f);
            if (Helmet != null) Helmet.localPosition = _baseHelmetLocal + new Vector3(0f, bob, 0f);
            if (Brim != null) Brim.localPosition = _baseBrimLocal + new Vector3(0f, bob, 0f);

            float armAngle = swing * ArmSwing;
            if (LeftArm != null) LeftArm.localRotation = Quaternion.Euler(armAngle, 0f, 0f);
            if (RightArm != null) RightArm.localRotation = Quaternion.Euler(-armAngle, 0f, 0f);

            // Carried loads ride the bob but do not swing, which is how a person
            // actually carries something heavy.
            if (Carried != null && Carried.gameObject.activeSelf)
            {
                var local = Carried.localPosition;
                Carried.localPosition = new Vector3(local.x, _baseBodyLocal.y + 0.28f + bob * 0.6f, local.z);
            }
        }
    }
}
