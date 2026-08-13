using System;
using System.Linq;
using NUnit.Framework;

namespace Abyssal.Core.Tests
{
    /// <summary>
    /// 钻进模型的行为测试。
    ///
    /// 每一条测试守护的都是一条**玩法规则**，而不是实现细节。
    /// 判断标准是：如果把被守护的那段逻辑改坏，这条测试必须变红。
    /// </summary>
    [TestFixture]
    public class DrillSimulationTests
    {
        static DrillSimulation MakeSim(Formation formation, double depth = 2000.0)
        {
            var well = new WellProfile(new[] { formation });
            return new DrillSimulation(well, new DrillState { Depth = depth });
        }

        /// <summary>
        /// 推进仿真并收集整段时间里出现过的所有事件。
        /// <see cref="DrillSimulation.Events"/> 每步都会清空，直接读它只能看到最后一步。
        /// </summary>
        static System.Collections.Generic.HashSet<DrillEvent> Run(
            DrillSimulation sim, DrillControls c, double seconds, double dt = 0.1)
        {
            var seen = new System.Collections.Generic.HashSet<DrillEvent>();
            int steps = (int)Math.Round(seconds / dt);
            for (int i = 0; i < steps; i++)
            {
                sim.Step(c, dt);
                foreach (var e in sim.Events) seen.Add(e);
            }
            return seen;
        }

        // ------------------------------------------------------------------ 井控

        [Test]
        public void 停泵后失去环空摩阻会诱发井涌()
        {
            // 松散砂岩在 2000 m 处孔隙压力为 20400 kPa，
            // 1000 kg/m³ 泥浆的静液柱只有 19620 kPa，本身压不住。
            // 但循环时环空摩阻能补上约 970 kPa，反而是过平衡的。
            // 一停泵这部分压力立刻消失——这正是真实钻井里起钻前必须加重泥浆的原因。
            var f = FormationLibrary.LooseSandstone(2500);

            var circulating = MakeSim(f, 2000.0);
            circulating.State.MudDensity = 1000.0;
            var cOn = DrillControls.NominalDrilling;
            cOn.TargetMudDensity = 1000.0;
            Run(circulating, cOn, 60.0);

            Assert.AreEqual(0.0, circulating.State.KickVolume, 1e-9,
                "循环状态下环空摩阻应该把这一层压住");

            var stopped = MakeSim(f, 2000.0);
            stopped.State.MudDensity = 1000.0;
            var cOff = DrillControls.NominalDrilling;
            cOff.TargetMudDensity = 1000.0;
            cOff.PumpRate = 0.0;
            cOff.BitOnBottom = false;

            double pitBefore = stopped.State.PitVolume;
            var events = Run(stopped, cOff, 60.0);

            Assert.Greater(stopped.State.KickVolume, 0.0, "停泵后失去摩阻，应该开始井涌");
            Assert.Greater(stopped.State.PitVolume, pitBefore, "井涌应该抬高泥浆池液位");
            Assert.IsTrue(events.Contains(DrillEvent.KickStarted), "持续欠平衡应该已经报过井涌");
        }

        [Test]
        public void 泥浆密度足够时不发生井涌()
        {
            var f = FormationLibrary.LooseSandstone(2500);
            var sim = MakeSim(f, 2000.0);

            var c = DrillControls.NominalDrilling;
            c.TargetMudDensity = 1250.0;
            sim.State.MudDensity = 1250.0;

            Run(sim, c, 120.0);

            Assert.AreEqual(0.0, sim.State.KickVolume, 1e-9, "过平衡状态不应该有侵入");
        }

        [Test]
        public void 泥浆密度超过破裂压力当量时发生漏失()
        {
            // 石灰岩的破裂压力梯度只有 14.6 kPa/m，对应约 1488 kg/m³。
            var f = FormationLibrary.Limestone(2500);
            var sim = MakeSim(f, 2000.0);

            var c = DrillControls.NominalDrilling;
            c.TargetMudDensity = 1700.0;
            sim.State.MudDensity = 1700.0;

            double pitBefore = sim.State.PitVolume;
            Run(sim, c, 60.0);

            Assert.Greater(sim.State.LossVolume, 0.0, "超过破裂压力应该漏失");
            Assert.Less(sim.State.PitVolume, pitBefore, "漏失应该降低泥浆池液位");
        }

        [Test]
        public void 安全泥浆窗口在高压地层显著收窄()
        {
            var normal = MakeSim(FormationLibrary.TightShale(2800), 2600.0);
            var over = MakeSim(FormationLibrary.OverpressuredShale(2800), 2600.0);

            var (nMin, nMax) = normal.SafeMudWindow(34.0);
            var (oMin, oMax) = over.SafeMudWindow(34.0);

            Assert.Greater(oMin, nMin, "高压层的密度下限应该更高");
            Assert.Less(oMax - oMin, nMax - nMin, "高压层的安全窗口应该更窄");
            Assert.Less(oMax - oMin, 80.0, "高压层窗口应窄到几乎没有容错空间");
        }

        [Test]
        public void 含气井涌被标记为含气()
        {
            var sim = MakeSim(FormationLibrary.GasSand(3200), 3000.0);
            var c = DrillControls.NominalDrilling;
            c.TargetMudDensity = 1400.0;   // 远压不住 15.2 kPa/m 的孔隙压力
            sim.State.MudDensity = 1400.0;

            Run(sim, c, 30.0);

            Assert.Greater(sim.State.KickVolume, 0.0);
            Assert.IsTrue(sim.State.KickIsGas, "含气层的井涌必须被标记为含气");
        }

        [Test]
        public void 关闭防喷器能大幅抑制井涌流入()
        {
            Func<bool, double> kickAfter = bopClosed =>
            {
                var sim = MakeSim(FormationLibrary.GasSand(3200), 3000.0);
                var c = DrillControls.NominalDrilling;
                c.TargetMudDensity = 1400.0;
                c.BlowoutPreventerClosed = bopClosed;
                sim.State.MudDensity = 1400.0;
                Run(sim, c, 40.0);
                return sim.State.KickVolume;
            };

            double open = kickAfter(false);
            double closed = kickAfter(true);

            Assert.Greater(open, 0.0);
            Assert.Less(closed, open * 0.5, "关井后流入速率应该显著下降");
        }

        // ------------------------------------------------------------------ 钻进

        [Test]
        public void 钻压超过founder点后钻速反而下降()
        {
            Func<double, double> ropAt = wob =>
            {
                var sim = MakeSim(FormationLibrary.TightShale(2500), 2000.0);
                var c = DrillControls.NominalDrilling;
                c.WeightOnBit = wob;
                c.TargetMudDensity = 1250.0;
                sim.State.MudDensity = 1250.0;
                sim.Step(c, 0.1);
                return sim.State.RateOfPenetration;
            };

            // 页岩硬度 1.55，founder 点约 80 + 120×1.55 = 266 kN。
            double belowFounder = ropAt(200.0);
            double atFounder = ropAt(266.0);
            double wayOver = ropAt(300.0);

            Assert.Greater(atFounder, belowFounder, "到 founder 点之前，加压应该提速");
            Assert.Less(wayOver, atFounder, "越过 founder 点后，继续加压反而降速");
        }

        [Test]
        public void 抬离井底时不进尺()
        {
            var sim = MakeSim(FormationLibrary.LooseSandstone(2500), 2000.0);
            var c = DrillControls.NominalDrilling;
            c.BitOnBottom = false;
            c.TargetMudDensity = 1250.0;
            sim.State.MudDensity = 1250.0;

            double depthBefore = sim.State.Depth;
            Run(sim, c, 30.0);

            Assert.AreEqual(depthBefore, sim.State.Depth, 1e-9);
            Assert.AreEqual(0.0, sim.State.RateOfPenetration, 1e-9);
        }

        [Test]
        public void 钻头磨损会拖慢钻速()
        {
            Func<double, double> ropWithWear = wear =>
            {
                var sim = MakeSim(FormationLibrary.TightShale(2500), 2000.0);
                sim.State.BitWear = wear;
                var c = DrillControls.NominalDrilling;
                c.TargetMudDensity = 1250.0;
                sim.State.MudDensity = 1250.0;
                sim.Step(c, 0.1);
                return sim.State.RateOfPenetration;
            };

            Assert.Greater(ropWithWear(0.0), ropWithWear(0.5));
            Assert.Greater(ropWithWear(0.5), ropWithWear(0.9));
        }

        [Test]
        public void 高研磨性地层让钻头磨得更快()
        {
            Func<Formation, double> wearAfter = f =>
            {
                var sim = MakeSim(f, 2000.0);
                var c = DrillControls.NominalDrilling;
                c.TargetMudDensity = 1300.0;
                sim.State.MudDensity = 1300.0;
                Run(sim, c, 300.0);
                return sim.State.BitWear;
            };

            // 砾岩研磨性 2.30，页岩 0.90。
            double conglomerate = wearAfter(FormationLibrary.Conglomerate(2500));
            double shale = wearAfter(FormationLibrary.TightShale(2500));

            Assert.Greater(conglomerate, shale * 1.8, "砾岩应该显著更费钻头");
        }

        // ------------------------------------------------------------------ 水力与卡钻

        [Test]
        public void 泵排量不足会导致岩屑堆积()
        {
            Func<double, double> cuttingsAt = pump =>
            {
                var sim = MakeSim(FormationLibrary.LooseSandstone(2500), 2000.0);
                var c = DrillControls.NominalDrilling;
                c.PumpRate = pump;
                c.TargetMudDensity = 1250.0;
                sim.State.MudDensity = 1250.0;
                Run(sim, c, 240.0);
                return sim.State.CuttingsLoad;
            };

            Assert.Greater(cuttingsAt(8.0), cuttingsAt(34.0), "低排量应该积更多岩屑");
            Assert.Greater(cuttingsAt(8.0), 0.4, "严重欠排量应该积到危险水平");
        }

        [Test]
        public void 钻得快但排量跟不上会卡钻()
        {
            // 最危险的组合不是「慢钻不循环」——那样岩屑本来就产得少。
            // 真正会出事的是高转速快速进尺，同时泵排量跟不上，岩屑来不及被带出去。
            var sim = MakeSim(FormationLibrary.LooseSandstone(2500), 2000.0);
            var c = DrillControls.NominalDrilling;
            c.RotarySpeed = 160.0;
            c.PumpRate = 4.0;
            c.TargetMudDensity = 1250.0;
            sim.State.MudDensity = 1250.0;

            var events = Run(sim, c, 600.0);

            Assert.Greater(sim.State.CuttingsLoad, 0.5, "高钻速低排量应该积起岩屑");
            Assert.Greater(sim.State.StuckSeverity, 0.1, "岩屑越过阈值后应该开始卡钻");
            Assert.IsTrue(events.Contains(DrillEvent.StuckPipeWarning), "应该已经报过卡钻预警");
        }

        [Test]
        public void 恢复循环和转动可以缓解卡钻()
        {
            var sim = MakeSim(FormationLibrary.LooseSandstone(2500), 2000.0);
            sim.State.StuckSeverity = 0.6;
            sim.State.CuttingsLoad = 0.2;

            var c = DrillControls.NominalDrilling;
            c.PumpRate = 45.0;
            c.RotarySpeed = 140.0;
            c.TargetMudDensity = 1250.0;
            sim.State.MudDensity = 1250.0;

            Run(sim, c, 60.0);

            Assert.Less(sim.State.StuckSeverity, 0.6, "全力循环加转动应该把卡钻程度降下来");
        }

        [Test]
        public void 岩屑负荷会推高扭矩()
        {
            Func<double, double> torqueAt = cuttings =>
            {
                var sim = MakeSim(FormationLibrary.TightShale(2500), 2000.0);
                sim.State.CuttingsLoad = cuttings;
                var c = DrillControls.NominalDrilling;
                c.TargetMudDensity = 1250.0;
                sim.State.MudDensity = 1250.0;
                sim.Step(c, 0.01);
                return sim.State.Torque;
            };

            Assert.Greater(torqueAt(0.6), torqueAt(0.1));
        }

        // ------------------------------------------------------------------ 温度

        [Test]
        public void 停泵会让井底温度回升到静态地层温度附近()
        {
            var sim = MakeSim(FormationLibrary.TightShale(3200), 3000.0);
            sim.State.BottomholeTemperature = 60.0;

            var c = DrillControls.Idle;
            c.TargetMudDensity = 1300.0;
            sim.State.MudDensity = 1300.0;

            Run(sim, c, 600.0, 0.25);

            double staticTemp = Physics.StaticFormationTemperature(3000.0);
            Assert.Greater(sim.State.BottomholeTemperature, 80.0, "停止循环后温度必须上升");
            Assert.AreEqual(staticTemp, sim.State.BottomholeTemperature, 6.0,
                "停泵足够久后应该逼近静态地层温度");
        }

        [Test]
        public void 加大排量能给钻头降温()
        {
            Func<double, double> tempAt = pump =>
            {
                var sim = MakeSim(FormationLibrary.TightShale(3200), 3000.0);
                sim.State.BottomholeTemperature = 100.0;
                var c = DrillControls.NominalDrilling;
                c.PumpRate = pump;
                c.TargetMudDensity = 1300.0;
                sim.State.MudDensity = 1300.0;
                Run(sim, c, 300.0, 0.25);
                return sim.State.BottomholeTemperature;
            };

            Assert.Less(tempAt(50.0), tempAt(12.0), "排量越大井底越凉");
        }

        [Test]
        public void 高温会加速钻头磨损()
        {
            Func<double, double> wearAt = startTemp =>
            {
                var sim = MakeSim(FormationLibrary.TightShale(3200), 3000.0);
                sim.State.BottomholeTemperature = startTemp;
                var c = DrillControls.NominalDrilling;
                c.PumpRate = 0.0;   // 不循环，让温度维持住
                c.TargetMudDensity = 1300.0;
                sim.State.MudDensity = 1300.0;
                Run(sim, c, 120.0);
                return sim.State.BitWear;
            };

            Assert.Greater(wearAt(160.0), wearAt(60.0) * 1.2, "超过报警线后磨损应该明显加快");
        }

        // ------------------------------------------------------------------ 泥浆池

        [Test]
        public void 正常钻进时泥浆池会缓慢下降()
        {
            var sim = MakeSim(FormationLibrary.TightShale(2500), 2000.0);
            var c = DrillControls.NominalDrilling;
            c.TargetMudDensity = 1250.0;
            sim.State.MudDensity = 1250.0;

            double before = sim.State.PitVolume;
            Run(sim, c, 600.0);
            double drop = before - sim.State.PitVolume;

            Assert.Greater(drop, 0.0, "新钻出的井眼要用泥浆填充，池子应该下降");
            Assert.Less(drop, 1.0, "但正常钻进的下降必须足够慢，才能和真正的漏失区分开");
        }

        [Test]
        public void 漏失导致的泥浆池下降远快于正常钻进()
        {
            double NormalDrop()
            {
                var sim = MakeSim(FormationLibrary.TightShale(2500), 2000.0);
                var c = DrillControls.NominalDrilling;
                c.TargetMudDensity = 1250.0;
                sim.State.MudDensity = 1250.0;
                double b = sim.State.PitVolume;
                Run(sim, c, 300.0);
                return b - sim.State.PitVolume;
            }

            double LossDrop()
            {
                var sim = MakeSim(FormationLibrary.FracturedZone(2500), 2000.0);
                var c = DrillControls.NominalDrilling;
                c.TargetMudDensity = 1500.0;
                sim.State.MudDensity = 1500.0;
                double b = sim.State.PitVolume;
                Run(sim, c, 300.0);
                return b - sim.State.PitVolume;
            }

            Assert.Greater(LossDrop(), NormalDrop() * 5.0, "漏失的速率必须明显区别于正常消耗");
        }

        // ------------------------------------------------------------------ 结构完整性

        [Test]
        public void 扭矩超限会累积钻具疲劳()
        {
            // 不预设岩屑和卡钻，让它们从「砾岩 + 满钻压 + 满转速 + 排量严重不足」
            // 这组操作里自然演化出来，这样测试守护的是整条因果链而不是单个公式。
            var sim = MakeSim(FormationLibrary.Conglomerate(2500), 2000.0);

            var c = DrillControls.NominalDrilling;
            c.WeightOnBit = 300.0;
            c.RotarySpeed = 200.0;
            c.PumpRate = 4.0;
            c.TargetMudDensity = 1300.0;
            sim.State.MudDensity = 1300.0;

            var events = Run(sim, c, 120.0);

            Assert.Greater(sim.State.CuttingsLoad, 0.5, "排量严重不足应该积起岩屑");
            Assert.Greater(sim.State.Torque, Limits.TorqueLimit, "岩屑加满钻压应该把扭矩顶过极限");
            Assert.Greater(sim.State.DrillstringFatigue, 0.0, "超限扭矩必须累积疲劳损伤");
            Assert.IsTrue(events.Contains(DrillEvent.TorqueOverLimit), "应该已经报过扭矩超限");
        }

        [Test]
        public void 正常参数下不会累积疲劳()
        {
            var sim = MakeSim(FormationLibrary.TightShale(2500), 2000.0);
            var c = DrillControls.NominalDrilling;
            c.TargetMudDensity = 1250.0;
            sim.State.MudDensity = 1250.0;

            Run(sim, c, 600.0);

            Assert.LessOrEqual(sim.State.Torque, Limits.TorqueLimit);
            Assert.AreEqual(0.0, sim.State.DrillstringFatigue, 1e-9);
        }

        // ------------------------------------------------------------------ 地层与换浆

        [Test]
        public void 钻穿地层边界会触发地层变化事件()
        {
            var well = new WellProfile(new[]
            {
                FormationLibrary.LooseSandstone(2000.5),
                FormationLibrary.TightShale(2200.0),
            });
            var sim = new DrillSimulation(well, new DrillState { Depth = 2000.0 });

            var c = DrillControls.NominalDrilling;
            c.TargetMudDensity = 1250.0;
            sim.State.MudDensity = 1250.0;

            bool sawChange = false;
            for (int i = 0; i < 6000 && !sawChange; i++)
            {
                sim.Step(c, 0.1);
                if (sim.Events.Contains(DrillEvent.FormationChanged)) sawChange = true;
            }

            Assert.IsTrue(sawChange, "钻过边界后必须报地层变化");
            Assert.AreEqual("致密页岩", sim.State.CurrentFormation.Name);
        }

        [Test]
        public void 换浆有滞后不会瞬间生效()
        {
            var sim = MakeSim(FormationLibrary.TightShale(2500), 2000.0);
            sim.State.MudDensity = 1200.0;

            var c = DrillControls.NominalDrilling;
            c.TargetMudDensity = 1600.0;

            sim.Step(c, 1.0);

            Assert.Less(sim.State.MudDensity, 1300.0, "一秒钟不可能把泥浆从 1200 换到 1600");
            Assert.Greater(sim.State.MudDensity, 1200.0, "但应该在往目标值靠");
        }

        // ------------------------------------------------------------------ 数值健壮性

        [Test]
        public void 极端输入不产生非有限数()
        {
            var sim = MakeSim(FormationLibrary.GasSand(3200), 3000.0);
            var c = new DrillControls
            {
                WeightOnBit = 1e9,
                RotarySpeed = -1e9,
                TargetMudDensity = 1e9,
                PumpRate = -1e9,
                BitOnBottom = true,
                ChokeOpening = 5.0,
            };

            Run(sim, c, 60.0);
            var s = sim.State;

            foreach (var (name, v) in new (string, double)[]
                     {
                         ("Depth", s.Depth), ("ROP", s.RateOfPenetration), ("Torque", s.Torque),
                         ("BitWear", s.BitWear), ("Temp", s.BottomholeTemperature),
                         ("BHP", s.BottomholePressure), ("ECD", s.EquivalentCirculatingDensity),
                         ("Cuttings", s.CuttingsLoad), ("Pit", s.PitVolume),
                         ("Kick", s.KickVolume), ("Stuck", s.StuckSeverity),
                     })
            {
                Assert.IsFalse(double.IsNaN(v) || double.IsInfinity(v), $"{name} 变成了非有限数");
            }
        }

        [Test]
        public void 零时间步不改变状态()
        {
            var sim = MakeSim(FormationLibrary.TightShale(2500), 2000.0);
            var c = DrillControls.NominalDrilling;
            sim.Step(c, 0.1);

            double depth = sim.State.Depth;
            double wear = sim.State.BitWear;

            sim.Step(c, 0.0);

            Assert.AreEqual(depth, sim.State.Depth, 1e-12);
            Assert.AreEqual(wear, sim.State.BitWear, 1e-12);
        }
    }
}
