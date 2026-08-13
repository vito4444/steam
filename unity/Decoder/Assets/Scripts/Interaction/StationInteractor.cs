using UnityEngine;

namespace Decoder.Interaction
{
    /// <summary>
    /// 可交互物件的基类。工位上的旋钮、拨杆、按钮都从这里派生。
    ///
    /// 视角是固定在椅子上的，玩家只能转头不能走动，
    /// 所以拾取用屏幕中心射线，不需要处理距离衰减和遮挡优先级之外的复杂情况。
    /// </summary>
    public abstract class Interactable : MonoBehaviour
    {
        [Tooltip("准星悬停时显示的提示文字")]
        public string hoverLabel = "";

        public virtual void OnHoverEnter()
        {
        }

        public virtual void OnHoverExit()
        {
        }

        /// <summary>拖动量，单位是屏幕像素。由 StationInteractor 在按住左键时逐帧调用。</summary>
        public virtual void OnDrag(Vector2 delta)
        {
        }

        public virtual void OnClick()
        {
        }

        /// <summary>滚轮增量。旋钮用它做微调。</summary>
        public virtual void OnScroll(float delta)
        {
        }

        /// <summary>当前状态的可读描述，显示在准星旁边。</summary>
        public virtual string StatusText => string.Empty;
    }

    /// <summary>
    /// 工位交互控制器。挂在摄像机上，负责转头、射线拾取和把输入分发给可交互物件。
    /// </summary>
    public sealed class StationInteractor : MonoBehaviour
    {
        [Header("视角")]
        [Tooltip("水平转头范围，正负各这么多度")]
        public float yawLimit = 100f;

        [Tooltip("俯仰范围，正负各这么多度")]
        public float pitchLimit = 60f;

        public float lookSensitivity = 2.2f;

        [Header("拾取")]
        public float reachMeters = 2.5f;
        public LayerMask interactableMask = ~0;

        private float _yaw;
        private float _pitch;
        private Vector3 _baseEuler;
        private Interactable _hovered;
        private Interactable _dragging;

        public Interactable Hovered => _hovered;

        /// <summary>true 时接管鼠标做转头，false 时鼠标用于操作界面。</summary>
        public bool LookEnabled { get; set; } = true;

        private void Awake()
        {
            _baseEuler = transform.localEulerAngles;
        }

        private void Update()
        {
            UpdateLook();
            UpdatePick();
            UpdateInput();
        }

        private void UpdateLook()
        {
            if (!LookEnabled || _dragging != null)
            {
                return;
            }

            // 按住右键才转头。左键留给操作设备，否则调旋钮的时候视角会跟着乱转。
            if (!Input.GetMouseButton(1))
            {
                return;
            }

            _yaw = Mathf.Clamp(_yaw + Input.GetAxis("Mouse X") * lookSensitivity,
                -yawLimit, yawLimit);
            _pitch = Mathf.Clamp(_pitch - Input.GetAxis("Mouse Y") * lookSensitivity,
                -pitchLimit, pitchLimit);

            transform.localEulerAngles = new Vector3(
                _baseEuler.x + _pitch, _baseEuler.y + _yaw, _baseEuler.z);
        }

        private void UpdatePick()
        {
            if (_dragging != null)
            {
                return;
            }

            var ray = new Ray(transform.position, transform.forward);
            Interactable found = null;
            if (Physics.Raycast(ray, out var hit, reachMeters, interactableMask))
            {
                found = hit.collider.GetComponentInParent<Interactable>();
            }

            if (ReferenceEquals(found, _hovered))
            {
                return;
            }

            if (_hovered != null)
            {
                _hovered.OnHoverExit();
            }

            _hovered = found;

            if (_hovered != null)
            {
                _hovered.OnHoverEnter();
            }
        }

        private void UpdateInput()
        {
            if (Input.GetMouseButtonDown(0) && _hovered != null)
            {
                _dragging = _hovered;
                _dragging.OnClick();
            }

            if (_dragging != null)
            {
                var delta = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
                if (delta.sqrMagnitude > 0f)
                {
                    _dragging.OnDrag(delta);
                }

                if (Input.GetMouseButtonUp(0))
                {
                    _dragging = null;
                }
            }

            var scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.001f && _hovered != null)
            {
                _hovered.OnScroll(scroll);
            }
        }
    }
}
