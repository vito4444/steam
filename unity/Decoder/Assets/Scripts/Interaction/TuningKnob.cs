using Decoder.Signal;
using UnityEngine;

namespace Decoder.Interaction
{
    /// <summary>
    /// 调频旋钮。这是玩家最常摸的一个部件，所以它的手感直接决定整个游戏的手感。
    ///
    /// 三个设计要点：
    ///
    /// 1. 粗调与微调分开。主旋钮一圈扫过很宽的频段，用来快速找信号；
    ///    滚轮做微调，用来把载波对到中心。真实电台就是这么两级的。
    /// 2. 旋钮的可视角度与频率严格绑定。玩家转到哪、频率就在哪，
    ///    不能出现旋钮转了但频率没动，或者反过来。
    /// 3. 拖动方向是水平的。竖直拖动在第一人称下容易和转头混淆。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class TuningKnob : Interactable
    {
        [Tooltip("被这个旋钮控制的接收机")]
        public RadioReceiver receiver;

        [Tooltip("水平拖动一个屏幕像素对应多少千赫")]
        public float kHzPerPixel = 1.6f;

        [Tooltip("滚轮一格对应多少千赫。用于把载波精确对到中心")]
        public float kHzPerScrollNotch = 0.05f;

        [Tooltip("旋钮模型绕哪个轴转")]
        public Vector3 rotationAxis = Vector3.forward;

        [Tooltip("扫完整个频段旋钮转多少度")]
        public float degreesAcrossBand = 1080f;

        private Quaternion _restRotation;

        private void Awake()
        {
            _restRotation = transform.localRotation;
            if (string.IsNullOrEmpty(hoverLabel))
            {
                hoverLabel = "调谐";
            }
        }

        private void LateUpdate()
        {
            if (receiver == null)
            {
                return;
            }

            // 旋钮角度始终由频率反推，而不是各自维护一份状态。
            // 这样无论频率是被拖动、滚轮还是脚本改的，旋钮都不会和它对不上。
            var angle = receiver.NormalizedDialPosition * degreesAcrossBand;
            transform.localRotation = _restRotation * Quaternion.AngleAxis(angle, rotationAxis);
        }

        public override void OnDrag(Vector2 delta)
        {
            if (receiver == null)
            {
                return;
            }

            receiver.Tune(delta.x * kHzPerPixel);
        }

        public override void OnScroll(float delta)
        {
            if (receiver == null)
            {
                return;
            }

            receiver.Tune(delta * kHzPerScrollNotch);
        }

        public override string StatusText
        {
            get
            {
                if (receiver == null)
                {
                    return string.Empty;
                }

                return $"{receiver.tunedKHz:F2} kHz";
            }
        }
    }

    /// <summary>
    /// 电源拨杆。
    ///
    /// 这里刻意用一个可序列化的对象引用而不是委托回调。委托无法被 Unity 序列化，
    /// 在编辑器里给它赋一个捕获了场景对象的闭包，保存出来的场景数据会损坏，
    /// 表现是运行时报 level0 corrupted 直接崩溃，而构建过程一声不吭。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class PowerSwitch : Interactable
    {
        [Tooltip("受这个拨杆控制的接收机")]
        public RadioReceiver receiver;

        [Tooltip("拨杆在开与关两个位置之间转多少度")]
        public float throwDegrees = 48f;

        public Vector3 rotationAxis = Vector3.right;

        [SerializeField] private bool isOn = true;

        private Quaternion _restRotation;

        public bool IsOn
        {
            get => isOn;
            set
            {
                isOn = value;
                Apply();
            }
        }

        private void Awake()
        {
            _restRotation = transform.localRotation;
            Apply();
        }

        private void LateUpdate()
        {
            var angle = isOn ? throwDegrees * 0.5f : -throwDegrees * 0.5f;
            transform.localRotation = _restRotation * Quaternion.AngleAxis(angle, rotationAxis);
        }

        public override void OnClick()
        {
            isOn = !isOn;
            Apply();
        }

        private void Apply()
        {
            if (receiver != null)
            {
                receiver.powered = isOn;
            }
        }

        public override string StatusText => isOn ? "电源 开" : "电源 关";
    }
}
