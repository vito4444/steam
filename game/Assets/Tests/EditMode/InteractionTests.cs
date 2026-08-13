using System;
using Maner.Controls;
using NUnit.Framework;

namespace Maner.Tests
{
    /// <summary>
    /// 控件交互数学测试。这一层决定「玩家把手拖到这里，控件转到哪」，
    /// 是本作手感的骨架——虽然手感好不好只能人工试玩，但规则对不对可以测。
    ///
    /// 重点守住三条设计意图：闸刀必须压过行程才咬合、阀轮要转好几圈、
    /// 旋钮的角度累计不能在跨越正负 180 度时跳整圈。
    /// </summary>
    public class InteractionTests
    {
        static Vec2 Polar(double degrees, double radius)
        {
            double rad = degrees * Math.PI / 180.0;
            return new Vec2(Math.Sin(rad) * radius, Math.Cos(rad) * radius);
        }

        [Test]
        public void 闸刀压不到行程阈值就弹回原位()
        {
            var def = ConsoleLayout.Get(ControlId.GeneratorMaster);
            Assert.AreEqual(ControlKind.KnifeSwitch, def.Kind);

            var state = ControlInteraction.BeginDrag(def, 0.0, Vec2.Zero);
            // 只向下拖了一小半行程。
            double value = ControlInteraction.UpdateDrag(def, ref state,
                new Vec2(0.0, -ControlInteraction.LinearTravelMeters * 0.4));
            Assert.Greater(value, 0.0, "拖拽过程中刀片应当跟着动");

            double settled = ControlInteraction.EndDrag(def, ref state, value);
            Assert.AreEqual(0.0, settled, 1e-9, "没压到位，松手应弹回断开");
        }

        [Test]
        public void 闸刀压过行程阈值才合闸()
        {
            var def = ConsoleLayout.Get(ControlId.GeneratorMaster);
            var state = ControlInteraction.BeginDrag(def, 0.0, Vec2.Zero);
            double value = ControlInteraction.UpdateDrag(def, ref state,
                new Vec2(0.0, -ControlInteraction.LinearTravelMeters * 1.1));

            Assert.GreaterOrEqual(value, def.ThrowThreshold);
            Assert.AreEqual(1.0, ControlInteraction.EndDrag(def, ref state, value), 1e-9, "压到底应合闸");
        }

        [Test]
        public void 闸刀向上拖不会合闸()
        {
            var def = ConsoleLayout.Get(ControlId.GeneratorMaster);
            var state = ControlInteraction.BeginDrag(def, 0.0, Vec2.Zero);
            double value = ControlInteraction.UpdateDrag(def, ref state,
                new Vec2(0.0, ControlInteraction.LinearTravelMeters));

            Assert.AreEqual(0.0, value, 1e-9, "闸刀只认向下的行程");
        }

        [Test]
        public void 阀轮转满设定圈数才到全开()
        {
            var def = ConsoleLayout.Get(ControlId.FanSpeedWheel);
            Assert.AreEqual(ControlKind.ValveWheel, def.Kind);
            Assert.Greater(def.TurnsToFull, 1f);

            double radius = def.Size * 0.45;
            var state = ControlInteraction.BeginDrag(def, 0.0, Polar(0.0, radius));

            // 一圈一圈地转，每 30 度采样一次。
            double value = 0.0;
            int stepsPerTurn = 12;
            int totalSteps = (int)(def.TurnsToFull * stepsPerTurn);
            for (int i = 1; i <= totalSteps; i++)
            {
                double angle = i * (360.0 / stepsPerTurn);
                value = ControlInteraction.UpdateDrag(def, ref state, Polar(angle, radius));
            }

            Assert.AreEqual(1.0, value, 0.02, $"转满 {def.TurnsToFull} 圈应到全开");
        }

        [Test]
        public void 阀轮只转一圈时远未到全开()
        {
            var def = ConsoleLayout.Get(ControlId.FanSpeedWheel);
            double radius = def.Size * 0.45;
            var state = ControlInteraction.BeginDrag(def, 0.0, Polar(0.0, radius));

            double value = 0.0;
            for (int i = 1; i <= 12; i++)
            {
                value = ControlInteraction.UpdateDrag(def, ref state, Polar(i * 30.0, radius));
            }

            double expected = 1.0 / def.TurnsToFull;
            Assert.AreEqual(expected, value, 0.03,
                "转一圈只应推进总行程的一部分，这正是阀轮成为最耗时控件的原因");
        }

        [Test]
        public void 角度累计跨越正负一百八十度时不会跳整圈()
        {
            // 直接测底层的角度归一化：从 170 度转到 -170 度实际只转了 20 度。
            Assert.AreEqual(20.0, ControlInteraction.NormalizeDegrees(-170.0 - 170.0), 1e-9);
            Assert.AreEqual(-20.0, ControlInteraction.NormalizeDegrees(170.0 - (-170.0)), 1e-9);

            var def = ConsoleLayout.Get(ControlId.Excitation);
            double radius = def.Size * 0.4;
            var state = ControlInteraction.BeginDrag(def, 0.5, Polar(170.0, radius));
            double value = ControlInteraction.UpdateDrag(def, ref state, Polar(-170.0, radius));

            // 270 度总行程，转 20 度约推进 0.074。
            Assert.AreEqual(0.5 + 20.0 / 270.0, value, 0.01, "跨越 ±180 度不应产生整圈跳变");
        }

        [Test]
        public void 指针贴近轴心时不响应旋转()
        {
            var def = ConsoleLayout.Get(ControlId.Excitation);
            var state = ControlInteraction.BeginDrag(def, 0.5, Polar(0.0, def.Size * 0.4));
            double value = ControlInteraction.UpdateDrag(def, ref state, new Vec2(0.0001, 0.0001));

            Assert.AreEqual(0.5, value, 1e-9, "指针在轴心附近时极角会剧烈跳变，应保持原值");
        }

        [Test]
        public void 拨杆松手后吸附到最近档位()
        {
            var def = ConsoleLayout.Get(ControlId.Direction);
            Assert.AreEqual(3, def.Detents);

            var state = ControlInteraction.BeginDrag(def, 0.5, Vec2.Zero);
            double raw = ControlInteraction.UpdateDrag(def, ref state, new Vec2(0.0, 0.02));
            double settled = ControlInteraction.EndDrag(def, ref state, raw);

            Assert.That(settled, Is.EqualTo(0.0).Within(1e-9)
                .Or.EqualTo(0.5).Within(1e-9)
                .Or.EqualTo(1.0).Within(1e-9), "三档拨杆只能停在三个位置上");
        }

        [Test]
        public void 调速手柄是连续量不吸附()
        {
            var def = ConsoleLayout.Get(ControlId.Throttle);
            var state = ControlInteraction.BeginDrag(def, 0.0, Vec2.Zero);
            double value = ControlInteraction.UpdateDrag(def, ref state,
                new Vec2(0.0, ControlInteraction.LinearTravelMeters * 0.37));

            Assert.AreEqual(0.37, value, 0.01, "手柄要能停在任意位置，这是精细控速的前提");
            Assert.AreEqual(value, ControlInteraction.EndDrag(def, ref state, value), 1e-9);
        }

        [Test]
        public void 拖拽结果始终落在合法区间内()
        {
            foreach (var def in ConsoleLayout.All)
            {
                if (!def.IsInteractive || def.Kind == ControlKind.PushButton)
                {
                    continue;
                }

                var state = ControlInteraction.BeginDrag(def, 0.5, Polar(0.0, def.Size * 0.4));
                foreach (var offset in new[] { -5.0, -0.3, 0.3, 5.0 })
                {
                    double v = ControlInteraction.UpdateDrag(def, ref state, new Vec2(offset, offset));
                    Assert.That(v, Is.InRange(0.0, 1.0), $"{def.Id} 的拖拽结果越界：{v}");
                }
            }
        }

        [Test]
        public void 每种可交互控件都有操作提示()
        {
            foreach (var def in ConsoleLayout.All)
            {
                if (!def.IsInteractive)
                {
                    continue;
                }
                Assert.IsNotEmpty(ControlInteraction.HintFor(def.Kind), $"{def.Kind} 缺少操作提示");
            }
        }

        [Test]
        public void 极角约定为零度朝上顺时针为正()
        {
            Assert.AreEqual(0.0, ControlInteraction.Angle(new Vec2(0.0, 1.0)), 1e-9);
            Assert.AreEqual(90.0, ControlInteraction.Angle(new Vec2(1.0, 0.0)), 1e-9);
            Assert.AreEqual(-90.0, ControlInteraction.Angle(new Vec2(-1.0, 0.0)), 1e-9);
        }
    }
}
