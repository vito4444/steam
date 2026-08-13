using UnityEngine;

namespace Monster.Interaction
{
    /// <summary>Casts from the centre of the view and drives whatever is under it.</summary>
    [RequireComponent(typeof(BoothCamera))]
    public sealed class DeskInteractor : MonoBehaviour
    {
        [SerializeField] private float reach = 2.4f;

        /// <summary>How far off a thing the player may aim and still be pointing at it. Three
        /// centimetres at arm's length, which is about a switch cap wide.</summary>
        [SerializeField] private float forgiveness = 0.075f;

        [SerializeField] private LayerMask mask = ~0;
        [SerializeField] private AimDot aim;

        private BoothCamera _camera;
        private Camera _view;
        private DeskInteractable _hovered;
        private DeskInteractable _focused;

        /// <summary>Where look and click come from.
        ///
        /// Built in Awake rather than assigned by the scene generator. A plain property is
        /// not serialised, so the generator's assignment existed only inside the editor and
        /// every build shipped with the null source: no looking, no clicking, nothing. The
        /// automated self-check never saw it because it drives the camera and the presenter
        /// directly and never goes through here.
        ///
        /// Still settable, because the self-check now installs a scripted source to prove
        /// this path works at all.</summary>
        public IInputSource Input { get; set; } = new NullInputSource();

        public DeskInteractable Hovered => _hovered;
        public DeskInteractable Focused => _focused;

        private void Awake()
        {
            _camera = GetComponent<BoothCamera>();
            _view = GetComponent<Camera>();

            if (Input is NullInputSource)
            {
                Input = new LegacyInputSource();
            }
        }

        private void OnEnable()
        {
            // Without this the operating system's pointer sits on top of the booth and
            // every player assumes it is what they are aiming with. The ray comes from the
            // centre of the view; hiding the pointer is what makes that discoverable
            // instead of baffling. It is not enough on its own -- see AimDot.
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Update()
        {
            Input.Tick(Time.deltaTime);
            _camera.ApplyLook(Input.LookDelta);

            UpdateHover();

            UpdateAim();

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

        /// <summary>Puts something under the player's nose without them having asked. Used
        /// once, to open the binder on the first night; a game that does this often has taken
        /// the camera away from the player.</summary>
        public void FocusOn(DeskInteractable target)
        {
            if (target == null || _camera == null)
            {
                return;
            }

            _focused = target;
            _camera.Focus(target.transform, target.FocusDistance, target.FocusOffset);
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

            // A thin ray demands the player put an invisible point exactly on a switch cap
            // two centimetres across, with no crosshair to aim by. Missing then produces
            // nothing at all -- no highlight, no sound, no refusal -- which reads as a broken
            // game rather than as a miss. A swept sphere forgives the near miss, and the
            // exact ray is still tried first so that overlapping objects resolve to whatever
            // is actually being pointed at.
            DeskInteractable found = null;

            if (Physics.Raycast(origin, direction, out var hit, reach, mask, QueryTriggerInteraction.Collide))
            {
                found = hit.collider.GetComponentInParent<DeskInteractable>();
            }

            if (found == null && Physics.SphereCast(origin, forgiveness, direction, out var near, reach, mask,
                    QueryTriggerInteraction.Collide))
            {
                found = near.collider.GetComponentInParent<DeskInteractable>();
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

        /// <summary>The dot follows what a click would actually hit, including the forgiven
        /// near miss, so it can never promise something the interact key will not deliver.</summary>
        private void UpdateAim()
        {
            if (aim != null)
            {
                aim.SetTargeting(_hovered != null);
            }
        }
    }
}
