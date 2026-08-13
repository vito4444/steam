using System;

namespace Maner.Controls
{
    /// <summary>二维向量。控制台层不引用 UnityEngine，所以自带一个最小实现。</summary>
    public readonly struct Vec2
    {
        public readonly double X;
        public readonly double Y;

        public Vec2(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double Length => Math.Sqrt(X * X + Y * Y);
        public static Vec2 Zero => new Vec2(0.0, 0.0);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
    }

    /// <summary>一次拖拽过程中需要跨帧保留的状态。</summary>
    public struct DragState
    {
        /// <summary>抓握点相对控件中心的位置，用于旋钮与阀轮计算转过的角度。</summary>
        public Vec2 GrabOffset;
        /// <summary>已累计转过的角度，单位度。阀轮要转好几圈，必须累计而不是取模。</summary>
        public double AccumulatedDegrees;
        /// <summary>上一帧抓握点的极角，单位度。</summary>
        public double LastAngleDegrees;
        /// <summary>拖拽开始时控件的值。</summary>
        public double StartValue;
        public bool Active;
    }

    /// <summary>
    /// 控件的交互数学。这一层刻意与输入设备和渲染完全分离：
    /// 它只回答「玩家把手拖到了这里，控件应该转到哪个位置」。
    ///
    /// 本作不提供「按 E 交互」的统一快捷方式，操作过程本身就是玩法：
    /// 阀轮要连着转好几圈，闸刀要压过行程阈值才咬合，旋钮要按住画圆。
    /// 把这些规则放在纯 C# 层，是为了让它们能被完整单元测试。
    /// </summary>
    public static class ControlInteraction
    {
        /// <summary>闸刀与手柄这类沿轨道拖拽的控件，走完全程所需的拖拽距离（米）。</summary>
        public const double LinearTravelMeters = 0.16;

        public static DragState BeginDrag(in ControlDefinition def, double currentValue, Vec2 grabLocal)
        {
            return new DragState
            {
                GrabOffset = grabLocal,
                AccumulatedDegrees = 0.0,
                LastAngleDegrees = Angle(grabLocal),
                StartValue = currentValue,
                Active = true,
            };
        }

        /// <summary>
        /// 根据当前抓握点更新控件值。pointerLocal 是指针在控件局部平面上的位置，
        /// 原点为控件中心，单位米，Y 轴向上。
        /// </summary>
        public static double UpdateDrag(in ControlDefinition def, ref DragState state, Vec2 pointerLocal)
        {
            if (!state.Active)
            {
                return state.StartValue;
            }

            switch (def.Kind)
            {
                case ControlKind.RotaryKnob:
                    return UpdateRotary(def, ref state, pointerLocal, 270.0);

                case ControlKind.ValveWheel:
                    // 阀轮的总行程是若干整圈，这也是它成为全场最耗时控件的原因。
                    return UpdateRotary(def, ref state, pointerLocal, Math.Max(0.25f, def.TurnsToFull) * 360.0);

                case ControlKind.KnifeSwitch:
                    // 闸刀只认向下的行程，往上拖不会合闸。
                    return Clamp01(state.StartValue + -(pointerLocal.Y - state.GrabOffset.Y) / LinearTravelMeters);

                case ControlKind.ThrottleHandle:
                    return Clamp01(state.StartValue + (pointerLocal.Y - state.GrabOffset.Y) / LinearTravelMeters);

                case ControlKind.ToggleLever:
                case ControlKind.Breaker:
                    return Clamp01(state.StartValue + (pointerLocal.Y - state.GrabOffset.Y) / (LinearTravelMeters * 0.55));

                default:
                    return state.StartValue;
            }
        }

        static double UpdateRotary(in ControlDefinition def, ref DragState state, Vec2 pointerLocal, double fullTravelDegrees)
        {
            // 指针离轴心太近时极角会剧烈跳变，这时保持原值，避免手一抖转半圈。
            if (pointerLocal.Length < def.Size * 0.12)
            {
                return state.StartValue + state.AccumulatedDegrees / fullTravelDegrees;
            }

            double angle = Angle(pointerLocal);
            double delta = NormalizeDegrees(angle - state.LastAngleDegrees);
            state.LastAngleDegrees = angle;
            state.AccumulatedDegrees += delta;

            return Clamp01(state.StartValue + state.AccumulatedDegrees / fullTravelDegrees);
        }

        /// <summary>
        /// 松手。闸刀是唯一有回弹的控件：没压过行程阈值就弹回原位，
        /// 这让「合闸」变成一个需要下决心的动作，而不是随手一碰。
        /// </summary>
        public static double EndDrag(in ControlDefinition def, ref DragState state, double currentValue)
        {
            state.Active = false;

            if (def.Kind == ControlKind.KnifeSwitch)
            {
                bool closing = state.StartValue < 0.5;
                if (closing)
                {
                    return currentValue >= def.ThrowThreshold ? 1.0 : 0.0;
                }
                // 拉开同样要拉到位。
                return currentValue <= 1.0 - def.ThrowThreshold ? 0.0 : 1.0;
            }

            if (def.Kind == ControlKind.ToggleLever || def.Kind == ControlKind.Breaker)
            {
                int detents = Math.Max(2, def.Detents);
                return Math.Round(Clamp01(currentValue) * (detents - 1)) / (detents - 1);
            }

            return Clamp01(currentValue);
        }

        /// <summary>指针在控件局部平面上的极角，0 度指向 +Y，顺时针为正。</summary>
        public static double Angle(Vec2 v) => Math.Atan2(v.X, v.Y) * (180.0 / Math.PI);

        /// <summary>把角度差折到 [-180, 180)，避免跨越 ±180 时出现整圈跳变。</summary>
        public static double NormalizeDegrees(double degrees)
        {
            while (degrees > 180.0)
            {
                degrees -= 360.0;
            }
            while (degrees <= -180.0)
            {
                degrees += 360.0;
            }
            return degrees;
        }

        /// <summary>面向玩家的操作提示，显示在准星旁边。</summary>
        public static string HintFor(ControlKind kind) => kind switch
        {
            ControlKind.KnifeSwitch => "按住并向下压到底",
            ControlKind.ToggleLever => "按住并上下拨动",
            ControlKind.RotaryKnob => "按住并画圈旋转",
            ControlKind.ValveWheel => "按住并转动，需转数圈",
            ControlKind.PushButton => "按下",
            ControlKind.Breaker => "按住并上下扳动",
            ControlKind.ThrottleHandle => "按住并沿槽推拉",
            _ => string.Empty,
        };

        static double Clamp01(double v) => v < 0.0 ? 0.0 : v > 1.0 ? 1.0 : v;
    }
}
