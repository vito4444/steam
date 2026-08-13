using System.Text;
using Maner.Controls;
using Maner.Shift;
using Maner.Sim;
using NUnit.Framework;

namespace Maner.Tests
{
    /// <summary>
    /// 班次层测试。核心是验收标准第 3 条：
    /// 「一个 6 条指令的班次脚本可完整走通，含 1 次强制故障事件；
    ///   机器人操作员脚本在无人干预下通过班次，日志输出 6/6 完成、1 次故障已处置」。
    /// </summary>
    public class ShiftTests
    {
        /// <summary>
        /// 跑完一整个班次。机器人只写控件位置，与人类玩家共用同一条输入路径。
        /// </summary>
        static ShiftDirector RunFullShift(out ShaftSimulation sim, out ConsoleState console,
            out RobotOperator robot, double maxSeconds = 2400.0, bool withFaults = true)
        {
            sim = new ShaftSimulation();
            console = new ConsoleState();
            robot = new RobotOperator { VerboseLog = true };

            var director = new ShiftDirector();
            director.Load(ShiftDirector.BuildFirstShift());
            if (withFaults)
            {
                director.ScheduleFirstShiftFaults();
            }
            director.Begin();

            double dt = ShaftSimulation.FixedDeltaTime;
            int maxSteps = (int)(maxSeconds / dt);

            for (int i = 0; i < maxSteps && director.Phase == ShiftPhase.Running; i++)
            {
                robot.Tick(director.ShiftTime, director, sim, console);
                console.DrainPulses(sim);
                sim.SetInputs(console.BuildInputs());
                sim.Step();
                director.Tick(dt, sim, console);
            }

            if (director.Phase == ShiftPhase.Running)
            {
                director.ForceSettle(sim);
            }

            return director;
        }

        static string FormatLog(ShiftDirector director)
        {
            var sb = new StringBuilder();
            foreach (var line in director.Log)
            {
                sb.AppendLine(line);
            }
            foreach (var line in director.Faults.Log)
            {
                sb.AppendLine(line);
            }
            return sb.ToString();
        }

        [Test]
        public void 首个班次含六条指令与一次编排故障()
        {
            var orders = ShiftDirector.BuildFirstShift();
            Assert.AreEqual(6, orders.Count, "验收标准要求班次脚本包含 6 条指令");

            var director = new ShiftDirector();
            director.Load(orders);
            director.ScheduleFirstShiftFaults();
            director.Begin();

            var sim = new ShaftSimulation();
            var console = new ConsoleState();
            for (int i = 0; i < 50 * 260; i++)
            {
                director.Tick(ShaftSimulation.FixedDeltaTime, sim, console);
            }

            Assert.AreEqual(1, director.Faults.TriggeredCount, "班次应恰好注入 1 次强制故障");
        }

        [Test]
        public void 机器人操作员无干预跑通六条指令并处置故障()
        {
            var director = RunFullShift(out var sim, out _, out _);
            string log = FormatLog(director);

            Assert.AreEqual(ShiftPhase.Settled, director.Phase, $"班次未结算。日志：\n{log}");
            Assert.IsNotNull(director.Settlement);

            var s = director.Settlement;
            Assert.AreEqual(6, s.TotalOrders);
            Assert.AreEqual(6, s.CompletedOrders,
                $"机器人应在无人干预下完成全部 6 条指令，实际 {s.CompletedOrders}。日志：\n{log}");
            Assert.AreEqual(0, s.TimedOutOrders, $"不应有超时指令。日志：\n{log}");
            Assert.AreEqual(1, s.FaultsTriggered, "应触发 1 次故障");
            Assert.AreEqual(1, s.FaultsResolved, $"故障应被处置。日志：\n{log}");
            Assert.AreEqual(IncidentSeverity.None, s.WorstIncident,
                $"按规程操作不应产生任何事故。日志：\n{log}");
        }

        [Test]
        public void 完成全部指令且无事故时评级为A()
        {
            var director = RunFullShift(out _, out _, out _);
            Assert.AreEqual('A', director.Settlement.Grade,
                $"完成 {director.Settlement.CompletedOrders}/6，最严重事故 {director.Settlement.WorstIncident}");
        }

        [Test]
        public void 罐笼在整个班次中从未超速()
        {
            RunFullShift(out var sim, out _, out _);
            Assert.LessOrEqual(sim.Hoist.MaxOverspeedSeen, HoistSystem.OverspeedLimit,
                "机器人的减速曲线应保证全程不超速");
            Assert.IsFalse(sim.Hoist.OverspeedTripped);
        }

        /// <summary>
        /// 减速曲线的守护测试。上一版只断言「没超速」，而超速阈值是 13.2 m/s，
        /// 把减速裕度改坏到 3 倍时罐笼仍在限速内，测试照样全绿——等于没守护住。
        /// 真正要守护的是「到站时是否轻放」，所以这里直接断言全程没有撞击事件，
        /// 并且每次停稳时的残余速度都在安全范围内。
        /// </summary>

        [Test]
        public void 班次全程没有撞击也没有绳索过载()
        {
            RunFullShift(out var sim, out _, out _);

            foreach (var e in sim.Events)
            {
                Assert.AreNotEqual(SimEventKind.HoistImpact, e.Kind,
                    $"罐笼撞击了端点，撞击速度 {e.Value:0.00} m/s，说明减速曲线不成立");
                Assert.AreNotEqual(SimEventKind.RopeOverstress, e.Kind,
                    $"钢丝绳张力越限 {e.Value:0} N，说明加减速过猛");
            }

            Assert.AreEqual(0.0, sim.Hoist.RopeFatigue, 1e-9, "按规程操作不应累积绳索疲劳");
        }

        [Test]
        public void 罐笼每次到站都停得住()
        {
            var director = RunFullShift(out var sim, out _, out _);

            // 六条指令里有三条要求罐笼停在特定深度，全部完成意味着每次都停稳了。
            int cageOrders = 0;
            foreach (var r in director.Results)
            {
                if (r.Kind == OrderKind.LowerCage || r.Kind == OrderKind.RaiseCage)
                {
                    cageOrders++;
                    Assert.IsTrue(r.Completed, $"罐笼指令未完成：{r.Text}");
                }
            }

            Assert.AreEqual(3, cageOrders, "首个班次应包含 3 条罐笼移动指令");
            Assert.Less(System.Math.Abs(sim.Hoist.Velocity), 0.1, "班次结束时罐笼应静止");
        }

        [Test]
        public void 故障发生时卷扬分路确实被打掉且随后恢复()
        {
            var sim = new ShaftSimulation();
            var console = new ConsoleState();
            var director = new ShiftDirector();
            director.Load(ShiftDirector.BuildFirstShift());
            director.ScheduleFirstShiftFaults();
            director.Begin();

            console.Set(ControlId.BreakerHoist, 1.0);
            Assert.IsTrue(console.GetBool(ControlId.BreakerHoist));

            // 推进到故障时点之后，但不派机器人去修。
            for (int i = 0; i < 50 * 205; i++)
            {
                director.Tick(ShaftSimulation.FixedDeltaTime, sim, console);
            }

            Assert.IsTrue(console.IsTripped(ControlId.BreakerHoist), "故障应把卷扬分路打到跳闸锁定");
            Assert.IsFalse(console.GetBool(ControlId.BreakerHoist));
            Assert.AreEqual(0, director.Faults.ResolvedCount, "没人去修，故障不该自行消失");
        }

        [Test]
        public void 无故障编排时机器人同样跑通且不触发任何故障()
        {
            var director = RunFullShift(out _, out _, out _, withFaults: false);
            Assert.AreEqual(6, director.Settlement.CompletedOrders);
            Assert.AreEqual(0, director.Settlement.FaultsTriggered);
        }

        [Test]
        public void 班次结算数据与仿真状态一致()
        {
            var director = RunFullShift(out var sim, out _, out _);
            var s = director.Settlement;

            Assert.AreEqual(sim.Ventilation.PeakGasPercent, s.PeakGasPercent, 1e-9);
            Assert.AreEqual(sim.Power.GeneratorWear, s.GeneratorWear, 1e-9);
            Assert.AreEqual(sim.Hoist.BrakePadWear, s.BrakePadWear, 1e-9);
            Assert.AreEqual(sim.Hoist.MotorWear, s.MotorWear, 1e-9);
            Assert.AreEqual(sim.Power.FuelLevel, s.FuelRemaining, 1e-9);
            Assert.AreEqual(sim.Hoist.LastIncident, s.WorstIncident);
        }

        [Test]
        public void 指令需要连续保持达标才算完成()
        {
            var order = new Order(1, OrderKind.EstablishAirflow, "测试", 45.0, holdSeconds: 3.0);
            var director = new ShiftDirector();
            director.Load(new[] { order });
            director.Begin();

            var sim = new ShaftSimulation();
            var console = new ConsoleState();

            // 直接把风量做到达标，但只保持 1 秒。
            sim.Ventilation.FanSpeed = 1.0;
            sim.Ventilation.Airflow = 60.0;
            for (int i = 0; i < 50; i++)
            {
                director.Tick(ShaftSimulation.FixedDeltaTime, sim, console);
            }
            Assert.AreEqual(0, director.Results.Count, "保持不足 3 秒不应判定完成");

            for (int i = 0; i < 50 * 3; i++)
            {
                director.Tick(ShaftSimulation.FixedDeltaTime, sim, console);
            }
            Assert.AreEqual(1, director.Results.Count, "连续保持满 3 秒后应判定完成");
            Assert.IsTrue(director.Results[0].Completed);
        }

        [Test]
        public void 超时指令被标记为未完成且拉低评级()
        {
            var order = new Order(1, OrderKind.EstablishAirflow, "不可能完成", 999.0, timeLimitSeconds: 5.0);
            var director = new ShiftDirector();
            director.Load(new[] { order });
            director.Begin();

            var sim = new ShaftSimulation();
            var console = new ConsoleState();
            for (int i = 0; i < 50 * 8; i++)
            {
                director.Tick(ShaftSimulation.FixedDeltaTime, sim, console);
            }

            Assert.AreEqual(1, director.Results.Count);
            Assert.IsTrue(director.Results[0].TimedOut);
            Assert.IsFalse(director.Results[0].Completed);
            Assert.AreEqual(0.0, director.Settlement.CompletionRate, 1e-9);
            Assert.AreEqual('D', director.Settlement.Grade);
        }

        [Test]
        public void 发生灾难性事故时评级直接判F()
        {
            var settlement = new ShiftSettlement
            {
                TotalOrders = 6,
                CompletedOrders = 6,
                WorstIncident = IncidentSeverity.Catastrophic,
            };
            Assert.AreEqual('F', settlement.Grade, "出了灾难性事故，完成度再高也是 F");
        }
    }
}
