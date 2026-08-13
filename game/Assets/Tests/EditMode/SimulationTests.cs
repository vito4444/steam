using System.Collections.Generic;
using Maner.Sim;
using NUnit.Framework;

namespace Maner.Tests
{
    /// <summary>
    /// 仿真层测试。这一层完全不依赖渲染，可以在无头环境下全速运行，
    /// 是本项目在没有人工试玩的情况下唯一能证明「系统按规格工作」的手段。
    /// </summary>
    public class SimulationTests
    {
        /// <summary>把一段有代表性的操作序列压进仿真，覆盖启动、加载、提升、制动。</summary>
        static ulong RunScriptedSequence(ulong seed, out ShaftSimulation sim)
        {
            sim = new ShaftSimulation(seed);
            var input = SimInputs.Neutral;

            // 0–10 s：起机组，合分路。
            input.GeneratorMaster = true;
            input.FuelValve = 0.8;
            input.CoolantPump = true;
            input.Excitation = 0.92;
            sim.SetInputs(input);
            StepSeconds(sim, 10.0);

            input.BreakerLighting = true;
            input.BreakerAuxiliary = true;
            input.BreakerVentilation = true;
            input.BreakerHoist = true;
            sim.SetInputs(input);
            StepSeconds(sim, 4.0);

            // 10–30 s：开主扇，开风门。
            input.MainFanSwitch = true;
            input.FanSpeedWheel = 0.7;
            input.Damper1 = 1.0;
            input.Damper2 = 0.8;
            input.Damper3 = 0.6;
            sim.SetInputs(input);
            StepSeconds(sim, 20.0);

            // 30–90 s：解锁罐笼，下放。
            sim.Hoist.PayloadKg = 1800.0;
            sim.Hoist.PersonnelAboard = 3;
            input.HoistPower = true;
            input.CageLock = false;
            input.Direction = 1;
            input.Throttle = 0.6;
            sim.SetInputs(input);
            StepSeconds(sim, 60.0);

            // 90–100 s：制动停车。
            input.Throttle = 0.0;
            input.Brake = 1.0;
            sim.SetInputs(input);
            StepSeconds(sim, 10.0);

            return sim.StateHash();
        }

        static void StepSeconds(ShaftSimulation sim, double seconds)
        {
            int steps = (int)(seconds / ShaftSimulation.FixedDeltaTime);
            for (int i = 0; i < steps; i++)
            {
                sim.Step();
            }
        }

        [Test]
        public void 相同输入序列产生逐位相同的状态哈希()
        {
            ulong first = RunScriptedSequence(0x4D414E4552UL, out var simA);
            ulong second = RunScriptedSequence(0x4D414E4552UL, out var simB);

            Assert.AreEqual(first, second, "相同种子与相同输入序列必须得到相同的仿真状态");
            Assert.AreEqual(simA.StepCount, simB.StepCount);
            Assert.AreEqual(simA.Hoist.Depth, simB.Hoist.Depth, 0.0, "罐笼深度必须逐位相同");
            Assert.AreEqual(simA.Ventilation.GasPercent, simB.Ventilation.GasPercent, 0.0, "瓦斯浓度必须逐位相同");
        }

        /// <summary>
        /// 黄金值回归。上面那条「跑两次哈希一致」的测试只能抓住同一进程、同一时刻的不确定性，
        /// 抓不住依赖系统时间、进程状态或平台浮点差异的不确定性——这一点是通过变异验证发现的：
        /// 往 PRNG 里注入 Environment.TickCount 之后，那条测试依然全绿。
        /// 因此这里把已知正确的输出硬编码下来，任何改变仿真数值行为的改动都必须在此显式更新。
        /// </summary>
        [Test]
        public void PRNG输出序列与黄金值一致()
        {
            var rng = new DeterministicRandom(0x4D414E4552UL);
            ulong[] expected =
            {
                0x2C6CB43052593C0CUL,
                0x4E56C90EB74DB7E7UL,
                0x2A8F0E5A30EAF155UL,
            };

            var actual = new ulong[expected.Length];
            for (int i = 0; i < actual.Length; i++)
            {
                actual[i] = rng.NextULong();
            }

            Assert.AreEqual(
                string.Join(",", System.Array.ConvertAll(expected, v => v.ToString("X16"))),
                string.Join(",", System.Array.ConvertAll(actual, v => v.ToString("X16"))),
                "PRNG 输出序列与黄金值不符");
        }

        [Test]
        public void 脚本化序列的状态哈希与黄金值一致()
        {
            const ulong Golden = 0x8165C41E07A3A28BUL;
            ulong actual = RunScriptedSequence(0x4D414E4552UL, out _);
            Assert.AreEqual(Golden, actual,
                "仿真数值行为发生了变化。若为有意调整，请在确认新行为正确后更新此黄金值。");
        }

        [Test]
        public void 不同种子产生不同的状态哈希()
        {
            ulong a = RunScriptedSequence(1UL, out _);
            ulong b = RunScriptedSequence(2UL, out _);
            Assert.AreNotEqual(a, b, "种子不同则瓦斯涌出波动不同，状态哈希应当不同");
        }

        [Test]
        public void 机组启动后母线电压进入额定区间()
        {
            var sim = new ShaftSimulation();
            var input = SimInputs.Neutral;
            input.GeneratorMaster = true;
            input.FuelValve = 0.85;
            input.CoolantPump = true;
            input.Excitation = 0.92;
            sim.SetInputs(input);

            StepSeconds(sim, 15.0);

            Assert.IsTrue(sim.Power.GeneratorRunning, "机组应处于运行状态");
            Assert.That(sim.Power.BusVoltage, Is.InRange(360.0, 400.0), "空载母线电压应落在 380V 附近");
            Assert.That(sim.Power.Frequency, Is.InRange(48.0, 51.0), "频率应接近 50Hz");
        }

        [Test]
        public void 燃油阀未开则机组无法启动()
        {
            var sim = new ShaftSimulation();
            var input = SimInputs.Neutral;
            input.GeneratorMaster = true;
            input.FuelValve = 0.1; // 低于 0.2 的启动门槛
            sim.SetInputs(input);

            StepSeconds(sim, 12.0);

            Assert.IsFalse(sim.Power.GeneratorRunning, "燃油阀开度不足时机组不应启动");
            Assert.Less(sim.Power.BusVoltage, PowerSystem.UndervoltageThreshold);
        }

        [Test]
        public void 主扇满速叠加满载提升会触发过载跳闸()
        {
            var sim = new ShaftSimulation();
            var input = SimInputs.Neutral;
            input.GeneratorMaster = true;
            input.FuelValve = 1.0;
            input.CoolantPump = true;
            input.Excitation = 1.0;
            input.BreakerVentilation = true;
            input.BreakerHoist = true;
            input.BreakerLighting = true;
            input.BreakerAuxiliary = true;
            sim.SetInputs(input);
            StepSeconds(sim, 12.0);

            // 主扇拉满，风机功率吃掉 250 kW。
            input.MainFanSwitch = true;
            input.FanSpeedWheel = 1.0;
            input.Damper1 = 1.0;
            input.Damper2 = 1.0;
            input.Damper3 = 1.0;
            sim.SetInputs(input);
            StepSeconds(sim, 25.0);

            // 同时满载提升，卷扬机需求远超机组余量。
            sim.Hoist.PayloadKg = HoistSystem.MaxPayloadKg;
            sim.Hoist.Depth = 1500.0;
            input.HoistPower = true;
            input.CageLock = false;
            input.Direction = -1;
            input.Throttle = 1.0;
            sim.SetInputs(input);
            StepSeconds(sim, 20.0);

            Assert.IsTrue(sim.Power.MainBreakerTripped, "总负载持续超过额定 110% 应触发主断路器跳闸");
            Assert.AreEqual(0.0, sim.Power.BusVoltage, 1e-9, "跳闸后母线应完全失电");
            Assert.Contains(SimEventKind.MainBreakerTripped, EventKinds(sim.Events));
        }

        [Test]
        public void 过载复位按钮可以恢复母线()
        {
            var sim = new ShaftSimulation();
            sim.Power.MainBreakerTripped = true;
            var input = SimInputs.Neutral;
            input.GeneratorMaster = true;
            input.FuelValve = 0.9;
            input.CoolantPump = true;
            input.Excitation = 0.92;
            sim.SetInputs(input);
            StepSeconds(sim, 3.0);
            Assert.AreEqual(0.0, sim.Power.BusVoltage, 1e-9);

            sim.QueuePulse(SimPulse.ResetOverload);
            StepSeconds(sim, 12.0);

            Assert.IsFalse(sim.Power.MainBreakerTripped);
            Assert.Greater(sim.Power.BusVoltage, PowerSystem.UndervoltageThreshold, "复位后母线应重新带电");
        }

        [Test]
        public void 风门全关时瓦斯浓度持续上升并触发报警()
        {
            var sim = new ShaftSimulation();
            var input = SimInputs.Neutral;
            input.GeneratorMaster = true;
            input.FuelValve = 0.9;
            input.CoolantPump = true;
            input.Excitation = 0.92;
            input.BreakerVentilation = true;
            input.MainFanSwitch = true;
            input.FanSpeedWheel = 1.0;
            // 风门全关：风机在转，但风送不进去。
            input.Damper1 = 0.0;
            input.Damper2 = 0.0;
            input.Damper3 = 0.0;
            sim.SetInputs(input);

            double before = sim.Ventilation.GasPercent;
            StepSeconds(sim, 240.0);

            Assert.Greater(sim.Ventilation.GasPercent, before, "无有效风量时瓦斯应累积");
            Assert.IsTrue(sim.Ventilation.GasAlarmActive, "瓦斯越过 0.5% 应触发报警");
            Assert.Contains(SimEventKind.GasAlarmRaised, EventKinds(sim.Events));
        }

        [Test]
        public void 足量通风能把瓦斯压在报警线以下()
        {
            var sim = new ShaftSimulation();
            var input = SimInputs.Neutral;
            input.GeneratorMaster = true;
            input.FuelValve = 0.9;
            input.CoolantPump = true;
            input.Excitation = 0.95;
            input.BreakerVentilation = true;
            input.MainFanSwitch = true;
            input.FanSpeedWheel = 0.85;
            input.Damper1 = 1.0;
            input.Damper2 = 1.0;
            input.Damper3 = 1.0;
            sim.SetInputs(input);

            StepSeconds(sim, 600.0);

            Assert.Less(sim.Ventilation.GasPercent, VentilationSystem.GasAlarmPercent,
                "0.85 转速加全开风门应能把瓦斯稳定压在 0.5% 以下");
            Assert.Greater(sim.Ventilation.Airflow, 40.0, "风量应达到设计值");
        }

        [Test]
        public void 消音按钮只静音不清除报警状态()
        {
            var sim = new ShaftSimulation();
            sim.Ventilation.GasPercent = 0.9;
            var input = SimInputs.Neutral;
            sim.SetInputs(input);
            sim.Step();

            Assert.IsTrue(sim.Ventilation.GasAlarmActive);
            Assert.IsFalse(sim.Ventilation.GasAlarmSilenced);

            sim.QueuePulse(SimPulse.SilenceGasAlarm);
            sim.Step();

            Assert.IsTrue(sim.Ventilation.GasAlarmSilenced, "消音后应标记为已静音");
            Assert.IsTrue(sim.Ventilation.GasAlarmActive, "消音不应清除报警本身");
        }

        [Test]
        public void 罐笼锁定时无论油门多大都不会移动()
        {
            var sim = new ShaftSimulation();
            var input = SimInputs.Neutral;
            input.GeneratorMaster = true;
            input.FuelValve = 0.9;
            input.CoolantPump = true;
            input.Excitation = 0.92;
            input.BreakerHoist = true;
            input.HoistPower = true;
            input.CageLock = true;
            input.Direction = 1;
            input.Throttle = 1.0;
            sim.SetInputs(input);

            StepSeconds(sim, 30.0);

            Assert.AreEqual(0.0, sim.Hoist.Depth, 1e-6, "锁定状态下罐笼不得移动");
            Assert.AreEqual(0.0, sim.Hoist.Velocity, 1e-6);
        }

        [Test]
        public void 断开卷扬分路后罐笼失去动力()
        {
            var sim = new ShaftSimulation();
            var input = SimInputs.Neutral;
            input.GeneratorMaster = true;
            input.FuelValve = 0.9;
            input.CoolantPump = true;
            input.Excitation = 0.92;
            input.BreakerHoist = true;
            input.HoistPower = true;
            input.CageLock = false;
            input.Direction = 1;
            input.Throttle = 0.5;
            sim.SetInputs(input);
            StepSeconds(sim, 20.0);
            double movedDepth = sim.Hoist.Depth;
            Assert.Greater(movedDepth, 10.0, "通电状态下罐笼应当已经下放");

            input.BreakerHoist = false;
            input.Brake = 1.0;
            sim.SetInputs(input);
            StepSeconds(sim, 15.0);

            Assert.IsFalse(sim.Hoist.Energized, "断开分路后卷扬机应失电");
            Assert.AreEqual(0.0, sim.Hoist.Velocity, 0.02, "失电并制动后罐笼应停住");
        }

        [Test]
        public void 全速撞底会登记重大事故()
        {
            var sim = new ShaftSimulation();
            var input = SimInputs.Neutral;
            input.GeneratorMaster = true;
            input.FuelValve = 1.0;
            input.CoolantPump = true;
            input.Excitation = 1.0;
            input.BreakerHoist = true;
            input.HoistPower = true;
            input.CageLock = false;
            input.Direction = 1;
            input.Throttle = 1.0;
            sim.SetInputs(input);
            sim.Hoist.Depth = HoistSystem.MaxDepthMeters - 60.0;

            StepSeconds(sim, 30.0);

            Assert.AreEqual(HoistSystem.MaxDepthMeters, sim.Hoist.Depth, 1e-6, "罐笼深度应被钳制在井底");
            Assert.Contains(SimEventKind.HoistImpact, EventKinds(sim.Events));
            Assert.GreaterOrEqual((int)sim.Hoist.LastIncident, (int)IncidentSeverity.Serious,
                "高速撞底至少应记为重大事故");
        }

        [Test]
        public void 卷扬机大负载会拉低母线电压进而压低风机转速()
        {
            var sim = new ShaftSimulation();
            var input = SimInputs.Neutral;
            input.GeneratorMaster = true;
            input.FuelValve = 1.0;
            input.CoolantPump = true;
            input.Excitation = 0.95;
            input.BreakerVentilation = true;
            input.BreakerHoist = true;
            input.MainFanSwitch = true;
            input.FanSpeedWheel = 0.8;
            input.Damper1 = 1.0;
            input.Damper2 = 1.0;
            input.Damper3 = 1.0;
            sim.SetInputs(input);
            StepSeconds(sim, 60.0);

            double idleVoltage = sim.Power.BusVoltage;
            double idleFanSpeed = sim.Ventilation.FanSpeed;

            sim.Hoist.PayloadKg = HoistSystem.MaxPayloadKg;
            sim.Hoist.Depth = 1200.0;
            input.HoistPower = true;
            input.CageLock = false;
            input.Direction = -1;
            input.Throttle = 0.9;
            sim.SetInputs(input);
            StepSeconds(sim, 6.0);

            Assert.Less(sim.Power.BusVoltage, idleVoltage - 5.0,
                "卷扬机接入大负载后母线电压应明显跌落");
            Assert.Less(sim.Ventilation.FanSpeed, idleFanSpeed,
                "母线跌落应传导为风机转速下降，这是三套系统耦合的直接证据");
        }

        [Test]
        public void 冷却泵关闭会让机组过热()
        {
            var sim = new ShaftSimulation();
            var input = SimInputs.Neutral;
            input.GeneratorMaster = true;
            input.FuelValve = 1.0;
            input.CoolantPump = false;
            input.Excitation = 0.95;
            input.BreakerVentilation = true;
            input.MainFanSwitch = true;
            input.FanSpeedWheel = 1.0;
            input.Damper1 = 1.0;
            sim.SetInputs(input);

            StepSeconds(sim, 900.0);

            Assert.Greater(sim.Power.CoolantTemp, PowerSystem.CoolantTripCelsius,
                "长时间高负载且不开冷却泵，水温应越过跳机阈值");
            Assert.Greater(sim.Power.GeneratorWear, 0.0, "过热应当留下永久磨损");
        }

        static List<SimEventKind> EventKinds(List<SimEvent> events)
        {
            var kinds = new List<SimEventKind>(events.Count);
            foreach (var e in events)
            {
                kinds.Add(e.Kind);
            }
            return kinds;
        }
    }
}
