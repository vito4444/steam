using UnityEngine;

namespace Worker.Game
{
    /// <summary>
    /// Spins a part while its machine is working.
    ///
    /// A still factory looks broken. Nothing else in this project communicates "the
    /// simulation is running" as directly as a blade that turns only while a bench is
    /// mid-recipe, and it costs one transform update per machine.
    /// </summary>
    public sealed class MachineAnimator : MonoBehaviour
    {
        public Vector3 AxisLocal = Vector3.up;
        public float DegreesPerSecond = 720f;

        /// <summary>Set by the view each frame; the part idles when the machine is not working.</summary>
        public bool Active;

        public float IdleFraction = 0.06f;

        private void Update()
        {
            float speed = Active ? DegreesPerSecond : DegreesPerSecond * IdleFraction;
            transform.Rotate(AxisLocal, speed * Time.deltaTime, Space.Self);
        }
    }

    /// <summary>
    /// Scrolls a belt's surface texture so cargo appears to ride on a moving band.
    ///
    /// Items already slide along the belt, but without the surface moving underneath
    /// they read as floating. This is the cheapest possible fix and needs its own
    /// material instance, which is why belts do not share one.
    /// </summary>
    public sealed class BeltScroller : MonoBehaviour
    {
        public float Speed = 0.6f;
        public Vector2 Direction = Vector2.right;

        private Material _material;
        private Vector2 _offset;

        private void Awake()
        {
            var renderer = GetComponent<Renderer>();
            if (renderer == null) return;

            // material, not sharedMaterial: each belt scrolls independently.
            _material = renderer.material;
        }

        private void Update()
        {
            if (_material == null) return;

            _offset += Direction * (Speed * Time.deltaTime);

            if (_material.HasProperty("_BaseMap")) _material.SetTextureOffset("_BaseMap", _offset);
            else if (_material.HasProperty("_MainTex")) _material.SetTextureOffset("_MainTex", _offset);
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }
    }
}
