using UnityEngine;

namespace Monster.Interaction
{
    /// <summary>Casts from the centre of the view and drives whatever is under it.</summary>
    [RequireComponent(typeof(BoothCamera))]
    public sealed class DeskInteractor : MonoBehaviour
    {
        [SerializeField] private float reach = 2.4f;
        [SerializeField] private LayerMask mask = ~0;

        private BoothCamera _camera;
        private Camera _view;
        private DeskInteractable _hovered;
        private DeskInteractable _focused;

        public IInputSource Input { get; set; } = new NullInputSource();

        public DeskInteractable Hovered => _hovered;
        public DeskInteractable Focused => _focused;

        private void Awake()
        {
            _camera = GetComponent<BoothCamera>();
            _view = GetComponent<Camera>();
        }

        private void Update()
        {
            Input.Tick(Time.deltaTime);
            _camera.ApplyLook(Input.LookDelta);

            UpdateHover();

            if (Input.BackPressed && _focused != null)
            {
                Release();
                return;
            }

            if (!Input.InteractPressed || _hovered == null)
            {
                return;
            }

            switch (_hovered.Mode)
            {
                case DeskInteractable.Behaviour.Operate:
                    _hovered.Activate();
                    break;
                case DeskInteractable.Behaviour.Inspect:
                case DeskInteractable.Behaviour.Leaf:
                    if (_focused != _hovered)
                    {
                        _focused = _hovered;
                        _camera.Focus(_focused.transform, _focused.FocusDistance, _focused.FocusOffset);
                    }
                    else if (_hovered.Mode == DeskInteractable.Behaviour.Leaf)
                    {
                        _hovered.Activate();
                    }
                    else
                    {
                        Release();
                    }

                    break;
            }
        }

        public void Release()
        {
            _focused = null;
            _camera.ClearFocus();
        }

        private void UpdateHover()
        {
            var origin = _view != null ? _view.transform.position : transform.position;
            var direction = _view != null ? _view.transform.forward : transform.forward;

            DeskInteractable found = null;
            if (Physics.Raycast(origin, direction, out var hit, reach, mask, QueryTriggerInteraction.Collide))
            {
                found = hit.collider.GetComponentInParent<DeskInteractable>();
            }

            if (found == _hovered)
            {
                return;
            }

            if (_hovered != null)
            {
                _hovered.SetHovered(false);
            }

            _hovered = found;

            if (_hovered != null)
            {
                _hovered.SetHovered(true);
            }
        }
    }
}
