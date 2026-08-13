using Maner.Controls;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Maner.Cabin
{
    /// <summary>
    /// 操作员本人。第一人称，只能在控制舱这 5 米见方的范围里走动，
    /// 这既是美术成本的约束，也是叙事装置：你被困在这里，
    /// 井下发生了什么只能靠仪表和电话推断。
    ///
    /// 交互刻意不做成「瞄准后按 E」。抓住控件之后视角会冻结，
    /// 鼠标位移直接驱动手上的动作——阀轮要画圈转好几圈，闸刀要向下压到底。
    /// 操作过程本身就是玩法。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerRig : MonoBehaviour
    {
        [Header("移动")]
        [SerializeField] float walkSpeed = 1.9f;
        [SerializeField] float standEyeHeight = 1.62f;
        [SerializeField] float crouchEyeHeight = 1.05f;
        [SerializeField] float eyeHeightLerp = 8f;

        [Header("视角")]
        [SerializeField] float lookSensitivity = 0.09f;
        [SerializeField] float pitchLimit = 78f;

        [Header("交互")]
        [SerializeField] float reachMeters = 1.35f;
        /// <summary>鼠标每移动一个像素，抓握点在控件平面上移动的距离（米）。</summary>
        [SerializeField] float dragMetersPerPixel = 0.00055f;

        CabinRuntime runtime;
        Camera cam;

        float yaw;
        float pitch;
        float eyeHeight;
        Vector3 headBobSeed;
        float walkPhase;

        ControlVisual hovered;
        ControlVisual grabbed;
        DragState dragState;
        Vec2 pointerLocal;

        public ControlVisual Hovered => hovered;
        public ControlVisual Grabbed => grabbed;
        public bool IsDragging => grabbed != null;

        public void Bind(CabinRuntime cabinRuntime, Camera camera)
        {
            runtime = cabinRuntime;
            cam = camera;
            eyeHeight = standEyeHeight;
            headBobSeed = new Vector3(Random.value * 100f, Random.value * 100f, 0f);

            var euler = camera.transform.rotation.eulerAngles;
            yaw = euler.y;
            pitch = euler.x > 180f ? euler.x - 360f : euler.x;
        }

        void Update()
        {
            if (runtime == null || cam == null || runtime.Builder == null)
            {
                return;
            }

            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (mouse == null || keyboard == null)
            {
                return;
            }

            if (grabbed != null)
            {
                UpdateDragging(mouse);
            }
            else
            {
                UpdateLook(mouse);
                UpdateMove(keyboard);
                UpdateHover();
                TryGrab(mouse);
            }
        }

        void UpdateLook(Mouse mouse)
        {
            Vector2 delta = mouse.delta.ReadValue();
            yaw += delta.x * lookSensitivity;
            pitch = Mathf.Clamp(pitch - delta.y * lookSensitivity, -pitchLimit, pitchLimit);
            cam.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        void UpdateMove(Keyboard keyboard)
        {
            float forward = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
            float strafe = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);

            Vector3 flatForward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            Vector3 flatRight = Vector3.ProjectOnPlane(cam.transform.right, Vector3.up).normalized;
            Vector3 move = (flatForward * forward + flatRight * strafe);

            if (move.sqrMagnitude > 1f)
            {
                move.Normalize();
            }

            Vector3 position = cam.transform.position + move * (walkSpeed * Time.deltaTime);

            // 舱内活动范围。留出余量避免贴脸穿墙，也不让玩家绕到控制台后面去。
            float halfWidth = CabinBuilder.RoomWidth * 0.5f - 0.55f;
            position.x = Mathf.Clamp(position.x, -halfWidth, halfWidth);
            position.z = Mathf.Clamp(position.z, -CabinBuilder.RoomDepth * 0.5f + 0.55f, -0.15f);

            bool crouching = keyboard.leftCtrlKey.isPressed || keyboard.cKey.isPressed;
            eyeHeight = Mathf.Lerp(eyeHeight, crouching ? crouchEyeHeight : standEyeHeight,
                Time.deltaTime * eyeHeightLerp);

            // 走动时的轻微上下浮动。幅度刻意很小，够察觉但不至于晕。
            if (move.sqrMagnitude > 0.01f)
            {
                walkPhase += Time.deltaTime * 6.2f;
            }
            float bob = Mathf.Sin(walkPhase) * 0.012f * Mathf.Clamp01(move.magnitude);
            float breathe = Mathf.Sin(Time.time * 1.35f + headBobSeed.x) * 0.004f;

            position.y = eyeHeight + bob + breathe;
            cam.transform.position = position;
        }

        void UpdateHover()
        {
            hovered = null;
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            if (!Physics.Raycast(ray, out RaycastHit hit, reachMeters))
            {
                return;
            }

            var visual = hit.collider.GetComponentInParent<ControlVisual>();
            if (visual != null && ConsoleLayout.Get(visual.Id).IsInteractive)
            {
                hovered = visual;
            }
        }

        void TryGrab(Mouse mouse)
        {
            if (hovered == null || !mouse.leftButton.wasPressedThisFrame)
            {
                return;
            }

            var def = ConsoleLayout.Get(hovered.Id);

            if (def.Kind == ControlKind.PushButton)
            {
                runtime.PressControl(def.Id);
                return;
            }

            if (!TryProjectPointer(hovered.transform, out Vec2 local))
            {
                return;
            }

            grabbed = hovered;
            pointerLocal = local;
            dragState = ControlInteraction.BeginDrag(def, runtime.Console.Get(def.Id), local);
        }

        void UpdateDragging(Mouse mouse)
        {
            var def = ConsoleLayout.Get(grabbed.Id);

            if (!mouse.leftButton.isPressed)
            {
                double settled = ControlInteraction.EndDrag(def, ref dragState, runtime.Console.Get(def.Id));
                runtime.SetControl(def.Id, settled);
                grabbed = null;
                return;
            }

            // 抓住之后视角冻结，鼠标位移直接变成手上的动作。
            Vector2 delta = mouse.delta.ReadValue();
            pointerLocal = new Vec2(
                pointerLocal.X + delta.x * dragMetersPerPixel,
                pointerLocal.Y + delta.y * dragMetersPerPixel);

            double value = ControlInteraction.UpdateDrag(def, ref dragState, pointerLocal);
            runtime.SetControl(def.Id, value);
        }

        /// <summary>把准星射线投到控件所在的平面上，换算成控件局部坐标。</summary>
        bool TryProjectPointer(Transform controlRoot, out Vec2 local)
        {
            local = Vec2.Zero;
            var plane = new Plane(controlRoot.forward, controlRoot.position);
            var ray = new Ray(cam.transform.position, cam.transform.forward);

            if (!plane.Raycast(ray, out float distance) || distance > reachMeters * 1.5f)
            {
                return false;
            }

            Vector3 point = controlRoot.InverseTransformPoint(ray.GetPoint(distance));
            local = new Vec2(point.x, point.y);
            return true;
        }
    }
}
