using System;
using System.Collections.Generic;
using System.Reflection;
using Maner.Controls;
using Maner.Sim;
using NUnit.Framework;

namespace Maner.Tests
{
    /// <summary>
    /// 控制台层测试。核心是验收标准第 1 条：
    /// 「不少于 24 个可交互控件，自动化测试逐个触发并断言其绑定参数发生预期变化」。
    /// </summary>
    public class ConsoleTests
    {
        static readonly FieldInfo[] InputFields = typeof(SimInputs).GetFields(BindingFlags.Public | BindingFlags.Instance);

        static List<string> DiffFields(SimInputs a, SimInputs b)
        {
            var changed = new List<string>();
            foreach (var f in InputFields)
            {
                object va = f.GetValue(a);
                object vb = f.GetValue(b);
                if (!Equals(va, vb))
                {
                    changed.Add(f.Name);
                }
            }
            return changed;
        }

        [Test]
        public void 可交互控件数量满足验收下限()
        {
            Assert.GreaterOrEqual(ConsoleLayout.InteractiveCount, 24,
                "验收标准要求不少于 24 个可交互控件");
        }

        [Test]
        public void 八种控件原型全部被使用()
        {
            var used = new HashSet<ControlKind>();
            foreach (var d in ConsoleLayout.All)
            {
                used.Add(d.Kind);
            }

            foreach (ControlKind kind in Enum.GetValues(typeof(ControlKind)))
            {
                Assert.IsTrue(used.Contains(kind), $"控件原型 {kind} 没有任何实例，说明它是多余的定义");
            }
        }

        [Test]
        public void 每个ControlId都有布局定义()
        {
            foreach (ControlId id in Enum.GetValues(typeof(ControlId)))
            {
                Assert.IsTrue(ConsoleLayout.TryGet(id, out _), $"{id} 缺少布局定义");
            }
        }

        [Test]
        public void 控件全部落在面板范围内且互不重叠()
        {
            var byPanel = new Dictionary<PanelId, List<ControlDefinition>>();
            foreach (var d in ConsoleLayout.All)
            {
                Assert.That(d.X, Is.InRange(0f, ConsoleLayout.PanelWidth), $"{d.Id} 的 X 超出面板");
                Assert.That(d.Y, Is.InRange(0f, ConsoleLayout.PanelHeight), $"{d.Id} 的 Y 超出面板");

                if (!byPanel.TryGetValue(d.Panel, out var list))
                {
                    list = new List<ControlDefinition>();
                    byPanel[d.Panel] = list;
                }
                list.Add(d);
            }

            foreach (var kv in byPanel)
            {
                var list = kv.Value;
                for (int i = 0; i < list.Count; i++)
                {
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        float dx = Math.Abs(list[i].X - list[j].X);
                        float dy = Math.Abs(list[i].Y - list[j].Y);
                        float minGap = (list[i].Size + list[j].Size) * 0.5f * 0.72f;
                        Assert.IsTrue(dx >= minGap || dy >= minGap,
                            $"{kv.Key} 面板上 {list[i].Id} 与 {list[j].Id} 距离过近（dx={dx:0.###} dy={dy:0.###} 需要 {minGap:0.###}）");
                    }
                }
            }
        }

        /// <summary>
        /// 验收标准第 1 条的主测试：逐个触发每一个可交互控件，
        /// 断言它确实改变了仿真输入，并且只改变了它自己那一个字段。
        /// </summary>
        [Test]
        public void 逐个触发每个可交互控件都恰好改变一个仿真输入字段()
        {
            var state = new ConsoleState();
            int verified = 0;
            var untouchedFields = new HashSet<string>();
            foreach (var f in InputFields)
            {
                untouchedFields.Add(f.Name);
            }

            foreach (var def in ConsoleLayout.All)
            {
                if (!def.IsInteractive)
                {
                    continue;
                }

                state.ResetToDefaults();
                var before = state.BuildInputs();

                if (def.Kind == ControlKind.PushButton)
                {
                    Assert.IsTrue(state.Press(def.Id), $"{def.Id} 是按钮但按下无效");
                    Assert.AreEqual(1, state.PendingPulseCount, $"{def.Id} 按下后应产生恰好一个脉冲");
                    var sim = new ShaftSimulation();
                    state.DrainPulses(sim);
                    Assert.AreEqual(0, state.PendingPulseCount, $"{def.Id} 的脉冲应在交付仿真后清空");
                    verified++;
                    continue;
                }

                // 把控件推到与默认位置相反的一端。
                double target = def.DefaultValue >= 0.5 ? 0.0 : 1.0;
                state.Set(def.Id, target);
                var after = state.BuildInputs();

                var changed = DiffFields(before, after);
                Assert.AreEqual(1, changed.Count,
                    $"{def.Id} 从 {def.DefaultValue} 推到 {target} 后，仿真输入应恰好变化 1 个字段，" +
                    $"实际变化 {changed.Count} 个：{string.Join(",", changed)}");

                untouchedFields.Remove(changed[0]);
                verified++;
            }

            Assert.GreaterOrEqual(verified, 24, "实际验证过的可交互控件数量不足 24");
            Assert.IsEmpty(untouchedFields,
                $"以下仿真输入字段没有任何控件能够改变，属于悬空输入：{string.Join(",", untouchedFields)}");
        }

        [Test]
        public void 每个控件的音色基频互不相同()
        {
            var seen = new Dictionary<float, ControlId>();
            foreach (var d in ConsoleLayout.All)
            {
                if (!d.IsInteractive)
                {
                    continue;
                }
                Assert.Greater(d.AudioBaseHz, 0f, $"{d.Id} 缺少音色基频");
                Assert.IsFalse(seen.ContainsKey(d.AudioBaseHz),
                    $"{d.Id} 与 {(seen.TryGetValue(d.AudioBaseHz, out var other) ? other.ToString() : "?")} 音色基频相同，玩家无法靠声音区分");
                seen[d.AudioBaseHz] = d.Id;
            }
        }

        [Test]
        public void 三档方向拨杆映射到提升停下放()
        {
            var state = new ConsoleState();

            state.Set(ControlId.Direction, 0.0);
            Assert.AreEqual(-1, state.BuildInputs().Direction, "0 档应为提升");

            state.Set(ControlId.Direction, 0.5);
            Assert.AreEqual(0, state.BuildInputs().Direction, "中档应为停");

            state.Set(ControlId.Direction, 1.0);
            Assert.AreEqual(1, state.BuildInputs().Direction, "2 档应为下放");
        }

        [Test]
        public void 有档位的控件会吸附到最近档位()
        {
            var state = new ConsoleState();

            state.Set(ControlId.Direction, 0.34);
            Assert.AreEqual(0.5, state.Get(ControlId.Direction), 1e-9, "0.34 应吸附到中档 0.5");

            state.Set(ControlId.CoolantPump, 0.61);
            Assert.AreEqual(1.0, state.Get(ControlId.CoolantPump), 1e-9, "两档拨杆应吸附到 0 或 1");
        }

        [Test]
        public void 连续控件保留任意中间值()
        {
            var state = new ConsoleState();
            state.Set(ControlId.FanSpeedWheel, 0.37);
            Assert.AreEqual(0.37, state.Get(ControlId.FanSpeedWheel), 1e-9, "阀轮应保留连续值");

            state.Set(ControlId.Throttle, 0.615);
            Assert.AreEqual(0.615, state.Get(ControlId.Throttle), 1e-9, "调速手柄应保留连续值");
        }

        [Test]
        public void 被跳闸的断路器在复位前拒绝合闸()
        {
            var state = new ConsoleState();
            state.Set(ControlId.BreakerHoist, 1.0);
            Assert.IsTrue(state.GetBool(ControlId.BreakerHoist));

            state.TripBreaker(ControlId.BreakerHoist);
            Assert.IsFalse(state.GetBool(ControlId.BreakerHoist), "跳闸后应处于断开位");
            Assert.IsTrue(state.IsTripped(ControlId.BreakerHoist));

            state.Set(ControlId.BreakerHoist, 1.0);
            Assert.IsFalse(state.GetBool(ControlId.BreakerHoist), "未复位时不得合闸");

            state.ClearTrip(ControlId.BreakerHoist);
            state.Set(ControlId.BreakerHoist, 1.0);
            Assert.IsTrue(state.GetBool(ControlId.BreakerHoist), "复位后应能合闸");
        }

        [Test]
        public void 表盘控件不可被玩家操作()
        {
            var state = new ConsoleState();
            double before = state.Get(ControlId.GaugeBusVoltage);
            state.Set(ControlId.GaugeBusVoltage, 1.0);
            Assert.AreEqual(before, state.Get(ControlId.GaugeBusVoltage), 1e-9, "表盘是只读的");
        }

        [Test]
        public void 表盘读数由仿真回写并落在量程内()
        {
            var sim = new ShaftSimulation();
            var state = new ConsoleState();

            state.Set(ControlId.GeneratorMaster, 1.0);
            state.Set(ControlId.FuelValve, 0.9);
            state.Set(ControlId.CoolantPump, 1.0);
            var inputs = state.BuildInputs();
            sim.SetInputs(inputs);

            for (int i = 0; i < 750; i++)
            {
                sim.Step();
            }

            state.PushGaugeReadings(sim);

            Assert.Greater(state.Get(ControlId.GaugeBusVoltage), 300.0, "电压表应反映已带电的母线");
            Assert.AreEqual(sim.Power.CoolantTemp, state.Get(ControlId.GaugeCoolantTemp), 1e-9);
            Assert.AreEqual(sim.Ventilation.GasPercent, state.Get(ControlId.GaugeGas), 1e-9);

            foreach (var d in ConsoleLayout.All)
            {
                if (d.Kind != ControlKind.Gauge)
                {
                    continue;
                }
                var (min, max) = ConsoleState.GaugeRange(d.Id);
                Assert.Less(min, max, $"{d.Id} 的量程定义无效");
            }
        }

        [Test]
        public void 控制台输入经仿真后能真实驱动机器()
        {
            var sim = new ShaftSimulation();
            var state = new ConsoleState();

            // 完全通过控制台操作把机器带起来，不直接碰 SimInputs。
            state.Set(ControlId.GeneratorMaster, 1.0);
            state.Set(ControlId.FuelValve, 0.85);
            state.Set(ControlId.CoolantPump, 1.0);
            state.Set(ControlId.Excitation, 0.92);
            state.Set(ControlId.BreakerVentilation, 1.0);
            state.Set(ControlId.MainFanSwitch, 1.0);
            state.Set(ControlId.FanSpeedWheel, 0.8);
            state.Set(ControlId.Damper1, 1.0);
            state.Set(ControlId.Damper2, 1.0);
            state.Set(ControlId.Damper3, 1.0);

            sim.SetInputs(state.BuildInputs());
            for (int i = 0; i < 50 * 60; i++)
            {
                sim.Step();
            }

            Assert.IsTrue(sim.Power.GeneratorRunning, "通过控制台应能启动机组");
            Assert.Greater(sim.Ventilation.FanSpeed, 0.5, "通过控制台应能把主扇开起来");
            Assert.Greater(sim.Ventilation.Airflow, 50.0, "风量应达到设计值");
        }
    }
}
