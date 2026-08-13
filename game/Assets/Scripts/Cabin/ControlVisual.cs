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
        AudioSource audioSource;
        ControlDefinition definition;
        bool definitionValid;
        bool sustaining;

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

        /// <summary>
        /// 挂上音源。三段音里的持续段只在活动件真的在动时循环播放，
        /// 起始段与结束段分别在开始动与停下时触发——这样阀轮转到一半松手，
        /// 声音会跟着停，而不是把一整段音效播完。
        /// </summary>
        public void AttachAudio(in ControlDefinition def)
        {
            definition = def;
            definitionValid = true;

            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 1f;
            audioSource.rolloffMode = AudioRolloffMode.Linear;
            audioSource.minDistance = 0.35f;
            audioSource.maxDistance = 4.5f;
            audioSource.dopplerLevel = 0f;
        }

        void Update()
        {
            if (Mathf.Abs(target - displayed) < 1e-5f)
            {
                if (sustaining)
                {
                    StopSustain();
                    PlaySegment(ProceduralAudio.Segment.End, false);
                }
                return;
            }

            if (!sustaining && definitionValid)
            {
                PlaySegment(ProceduralAudio.Segment.Begin, false);
                if (definition.IsContinuous)
                {
                    PlaySegment(ProceduralAudio.Segment.Sustain, true);
                }
                sustaining = true;
            }

            displayed = Mathf.MoveTowards(displayed, target, ResponseSpeed * Time.deltaTime);
            ApplyPose();
        }

        void PlaySegment(ProceduralAudio.Segment segment, bool loop)
        {
            if (audioSource == null || !definitionValid)
            {
                return;
            }

            var clip = ProceduralAudio.Get(definition, segment);
            if (clip == null)
            {
                return;
            }

            audioSource.loop = loop;
            audioSource.clip = clip;
            audioSource.Play();
        }

        void StopSustain()
        {
            sustaining = false;
            if (audioSource != null && audioSource.loop)
            {
                audioSource.Stop();
                audioSource.loop = false;
            }
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
