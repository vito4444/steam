using System;
using UnityEngine;

namespace Monster.Interaction
{
    /// <summary>Something on the desk the player can look at and operate.
    ///
    /// There is no cursor and no tooltip. Hovering brightens the object's emissive tint,
    /// which is the only affordance the game gives, because a floating "Press E" label
    /// would break the rule that everything the player reads is a physical thing in the
    /// booth.</summary>
    [RequireComponent(typeof(Collider))]
    public sealed class DeskInteractable : MonoBehaviour
    {
        public enum Behaviour
        {
            /// <summary>Lean in to read it, lean back out again.</summary>
            Inspect,

            /// <summary>Lean in to read it, and click again to turn the page rather than
            /// leaning back. For anything with more than one sheet: the post, the binder.
            /// Leaning back is still the right mouse button.</summary>
            Leaf,

            /// <summary>Throw it. Fires <see cref="Operated"/> immediately.</summary>
            Operate,
        }

        [SerializeField] private Behaviour mode = Behaviour.Inspect;
        [SerializeField] private string payload;
        [SerializeField] private float focusDistance = 0.34f;
        [SerializeField] private Vector3 focusOffset = new(0f, 1f, -0.55f);
        [SerializeField] private Renderer[] highlightTargets = Array.Empty<Renderer>();

        private MaterialPropertyBlock _properties;
        private bool _hovered;

        public Behaviour Mode => mode;
        public string Payload => payload;
        public float FocusDistance => focusDistance;
        public Vector3 FocusOffset => focusOffset;

        public event Action<DeskInteractable> Operated;

        private static readonly Color HighlightEmission = new(0.16f, 0.13f, 0.07f);

        private Color[] _resting;

        /// <summary>Remembers what each renderer emits when nothing is looking at it. Read
        /// from the shared material once, lazily, because the scene generator assigns the
        /// renderers and the materials are not final until it has finished.</summary>
        private void CaptureRestingEmission()
        {
            if (_resting != null && _resting.Length == highlightTargets.Length)
            {
                return;
            }

            _resting = new Color[highlightTargets.Length];

            for (var i = 0; i < highlightTargets.Length; i++)
            {
                var material = highlightTargets[i] != null ? highlightTargets[i].sharedMaterial : null;
                _resting[i] = material != null && material.HasProperty("_EmissionColor")
                    ? material.GetColor("_EmissionColor")
                    : Color.black;
            }
        }

        public void Configure(Behaviour behaviour, string interactionPayload, float distance,
            Vector3 offset, Renderer[] highlights)
        {
            mode = behaviour;
            payload = interactionPayload;
            focusDistance = distance;
            focusOffset = offset;
            highlightTargets = highlights ?? Array.Empty<Renderer>();
        }

        public void SetHovered(bool hovered)
        {
            if (_hovered == hovered)
            {
                return;
            }

            _hovered = hovered;
            _properties ??= new MaterialPropertyBlock();

            CaptureRestingEmission();

            for (var i = 0; i < highlightTargets.Length; i++)
            {
                var target = highlightTargets[i];
                if (target == null)
                {
                    continue;
                }

                target.GetPropertyBlock(_properties);

                // Un-hovering restores what the material was emitting rather than forcing
                // black. Forcing black meant any interactable that glows on its own -- a
                // lit key, a reflector, a screen -- went permanently dark the first time
                // the player looked at it and away again.
                _properties.SetColor("_EmissionColor",
                    hovered ? _resting[i] + HighlightEmission : _resting[i]);
                target.SetPropertyBlock(_properties);
            }
        }

        public void Activate() => Operated?.Invoke(this);
    }
}
