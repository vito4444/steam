using UnityEngine;

namespace Worker.Game
{
    /// <summary>
    /// A finished item hopping out of the machine that made it.
    ///
    /// A video review noted that cargo simply vanishes on entering a machine and
    /// reappears in a buffer, which makes production feel like bookkeeping rather than
    /// like something happening. This gives every completed craft a visible moment: the
    /// item pops up out of the housing, arcs over and settles.
    ///
    /// Driven by the simulation's own CraftCompleted event, so it fires exactly as often
    /// as production actually occurs and cannot drift out of step with it.
    /// </summary>
    public sealed class OutputPop : MonoBehaviour
    {
        private Vector3 _from;
        private Vector3 _to;
        private float _elapsed;
        private float _duration;
        private float _spin;

        public static void Spawn(Transform parent, Material material, Vector3 localFrom, Vector3 localTo, float size)
        {
            var holder = MeshObjects.Box("Pop", parent, material,
                new Vector3(size, size, size), localFrom);

            var pop = holder.gameObject.AddComponent<OutputPop>();
            pop._from = localFrom;
            pop._to = localTo;
            pop._duration = 0.55f;
            pop._spin = Random.Range(180f, 420f);

            var renderer = holder.GetComponent<Renderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_elapsed / _duration);

            // Flat arc: horizontal travel is linear, height follows a parabola. Anything
            // fancier is invisible over half a second at this scale.
            var flat = Vector3.Lerp(_from, _to, t);
            float lift = Mathf.Sin(t * Mathf.PI) * 0.55f;

            transform.localPosition = new Vector3(flat.x, flat.y + lift, flat.z);
            transform.Rotate(Vector3.up, _spin * Time.deltaTime, Space.Self);

            // Shrink into nothing at the end rather than blinking out.
            if (t > 0.75f)
            {
                float fade = 1f - (t - 0.75f) / 0.25f;
                transform.localScale = transform.localScale.normalized * Mathf.Max(0.01f, fade * 0.24f);
            }

            if (t >= 1f) Destroy(gameObject);
        }
    }
}
