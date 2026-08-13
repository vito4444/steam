using UnityEngine;
using UnityEngine.UI;

namespace Monster.Interaction
{
    /// <summary>A three-pixel dot in the middle of the view.
    ///
    /// The booth was built without one on the theory that hiding the system pointer would
    /// make the centre ray discoverable. Playing it says otherwise: on a wide desk under one
    /// lamp there is nothing to aim by, a miss produces no feedback at all, and pressing the
    /// interact key at empty desk reads as a broken game rather than as a miss.
    ///
    /// So: the smallest thing that fixes it. Nearly invisible when there is nothing to touch,
    /// and it opens into a ring when something is under it, which is also how the player
    /// learns that the centre of the view is the thing that points.</summary>
    [RequireComponent(typeof(Image))]
    public sealed class AimDot : MonoBehaviour
    {
        [SerializeField] private float restSize = 4f;
        [SerializeField] private float targetSize = 11f;
        [SerializeField] private float restAlpha = 0.30f;
        [SerializeField] private float targetAlpha = 0.62f;
        [SerializeField] private float speed = 14f;

        private Image _image;
        private RectTransform _rect;
        private float _openness;

        private void Awake()
        {
            _image = GetComponent<Image>();
            _rect = (RectTransform)transform;
        }

        /// <summary>Called every frame by the interactor with whether anything is under the
        /// dot. Driven rather than polling, so the dot can never disagree with what a click
        /// would actually hit.</summary>
        public void SetTargeting(bool targeting)
        {
            _openness = Mathf.MoveTowards(_openness, targeting ? 1f : 0f, Time.deltaTime * speed);

            if (_image == null || _rect == null)
            {
                return;
            }

            var size = Mathf.Lerp(restSize, targetSize, _openness);
            _rect.sizeDelta = new Vector2(size, size);

            var colour = _image.color;
            colour.a = Mathf.Lerp(restAlpha, targetAlpha, _openness);
            _image.color = colour;
        }
    }
}
