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

            foreach (var target in highlightTargets)
            {
                if (target == null)
                {
                    continue;
                }

                target.GetPropertyBlock(_properties);
                _properties.SetColor("_EmissionColor",
                    hovered ? new Color(0.16f, 0.13f, 0.07f) : Color.black);
                target.SetPropertyBlock(_properties);
            }
        }

        public void Activate() => Operated?.Invoke(this);
    }
}
