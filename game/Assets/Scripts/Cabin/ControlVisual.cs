using Maner.Controls;
using UnityEngine;

namespace Maner.Cabin
{
    public enum MovementKind
    {
        None,
        RotateLocalAxis,
        TranslateLocalAxis,
    }

    /// <summary>
    /// 一个控件的运行时表现。它只负责把 0..1 的逻辑值翻译成活动件的位姿，
    /// 不持有任何游戏逻辑——逻辑全部在 ConsoleState 与仿真里。
    ///
    /// 位姿采用带阻尼的追赶而不是直接赋值：阀轮转到底需要时间，
    /// 闸刀合下去有回弹，这些「机器不是瞬间响应」的细节是本作手感的一部分。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ControlVisual : MonoBehaviour
    {
        public ControlId Id;
        public ControlKind Kind;
        public Transform MovingPart;
        public MovementKind Movement = MovementKind.None;
        public Vector3 LocalAxis = Vector3.right;
        public float RangeStart;
        public float RangeEnd;
        public float ResponseSpeed = 9f;
        public Renderer LampRenderer;

        Vector3 movingBasePosition;
        Quaternion movingBaseRotation;
        float displayed;
        float target;

        public float Displayed => displayed;

        void Awake()
        {
            if (MovingPart != null)
            {
                movingBasePosition = MovingPart.localPosition;
                movingBaseRotation = MovingPart.localRotation;
            }
        }

        /// <summary>设置目标值，表现会以有限速度追上去。</summary>
        public void SetValue(double normalized)
        {
            target = Mathf.Clamp01((float)normalized);
        }

        /// <summary>不经过阻尼直接落位。用于场景初始化与自动化截图。</summary>
        public void SnapValue(double normalized)
        {
            target = Mathf.Clamp01((float)normalized);
            displayed = target;
            ApplyPose();
        }

        void Update()
        {
            if (Mathf.Abs(target - displayed) < 1e-5f)
            {
                return;
            }

            displayed = Mathf.MoveTowards(displayed, target, ResponseSpeed * Time.deltaTime);
            ApplyPose();
        }

        void ApplyPose()
        {
            if (MovingPart == null || Movement == MovementKind.None)
            {
                return;
            }

            float amount = Mathf.Lerp(RangeStart, RangeEnd, displayed);

            if (Movement == MovementKind.RotateLocalAxis)
            {
                MovingPart.localRotation = movingBaseRotation * Quaternion.AngleAxis(amount, LocalAxis);
            }
            else
            {
                MovingPart.localPosition = movingBasePosition + LocalAxis.normalized * amount;
            }
        }

        public void SetLamp(CabinMaterials materials, LampState state)
        {
            if (LampRenderer != null)
            {
                LampRenderer.sharedMaterial = materials.Lamp(state);
            }
        }
    }
}
