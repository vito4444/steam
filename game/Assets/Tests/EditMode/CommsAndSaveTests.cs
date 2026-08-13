using System.IO;
using Maner.Shift;
using Maner.Sim;
using NUnit.Framework;

namespace Maner.Tests
{
    /// <summary>
    /// 通话与存档测试。对应验收标准第 4、5 条：
    /// 「电话系统可接听，至少 4 段语音，玩家的应答选择会改变后续事件」
    /// 「结算数值与仿真日志一致；重启后读档状态相同」。
    /// </summary>
    public class CommsAndSaveTests
    {
        const double Dt = ShaftSimulation.FixedDeltaTime;

        static void Advance(CommsSystem comms, ref double t, double seconds)
        {
            int steps = (int)(seconds / Dt);
            for (int i = 0; i < steps; i++)
            {
                t += Dt;
                comms.Tick(t, Dt);
            }
        }

        [Test]
        public void 首个班次的通话本至少提供四段语音()
        {
            var comms = CommsSystem.BuildFirstShift();
            var seen = new System.Collections.Generic.HashSet<string>();

            double t = 0.0;
            // 把整个班次的排期跑一遍，把所有会响起的通话都收集出来。
            for (int i = 0; i < 50 * 400; i++)
            {
                t += Dt;
                comms.Tick(t, Dt);
                if (comms.State == CallState.Ringing)
                {
                    seen.Add(comms.Current.VoiceClip);
                    comms.Answer(t);
                    if (comms.State == CallState.AwaitingReply)
                    {
                        comms.Reply(0, t);
                    }
                }
            }

            Assert.GreaterOrEqual(seen.Count, 4,
                $"验收标准要求至少 4 段语音，实际排期中出现 {seen.Count} 段：{string.Join(",", seen)}");
        }

        [Test]
        public void 语音文件全部存在于资源目录()
        {
            string dir = Path.Combine(Path.GetDirectoryName(UnityEngine.Application.dataPath) ?? ".",
                "Assets", "Resources", "Voice");
            Assert.IsTrue(Directory.Exists(dir), $"语音目录不存在：{dir}");

            string[] expected =
            {
                "crew_ready", "command_pressure", "crew_power_lost",
                "crew_calm", "crew_panic", "unknown_whisper",
            };

            foreach (var name in expected)
            {
                string path = Path.Combine(dir, name + ".ogg");
                Assert.IsTrue(File.Exists(path), $"缺少语音文件 {path}，运行 tools/generate_voice.sh 生成");
                Assert.Greater(new FileInfo(path).Length, 2048, $"{name}.ogg 体积异常小，可能合成失败");
            }
        }

        /// <summary>验收标准第 4 条的主测试：两种应答分支各自触发不同的后续事件。</summary>
        [Test]
        public void 两种应答分支触发不同的后续事件与后续通话()
        {
            // 分支 A：承认故障并安抚。
            var commsA = CommsSystem.BuildFirstShift();
            double tA = 0.0;
            Advance(commsA, ref tA, 206.0);
            Assert.AreEqual(CallState.Ringing, commsA.State, "第 205 秒应有来电");
            Assert.AreEqual("crew_power_lost", commsA.Current.Id);
            commsA.Answer(tA);
            Assert.AreEqual(CallState.AwaitingReply, commsA.State);
            Assert.IsTrue(commsA.Reply(0, tA));

            // 分支 B：推卸责任。
            var commsB = CommsSystem.BuildFirstShift();
            double tB = 0.0;
            Advance(commsB, ref tB, 206.0);
            commsB.Answer(tB);
            Assert.IsTrue(commsB.Reply(1, tB));

            Assert.Contains("reply:fault_acknowledged", (System.Collections.ICollection)commsA.TriggeredEvents);
            Assert.Contains("reply:fault_dismissed", (System.Collections.ICollection)commsB.TriggeredEvents);
            CollectionAssert.AreNotEqual(commsA.TriggeredEvents, commsB.TriggeredEvents,
                "两种应答必须触发不同的事件标识");

            // 两个分支各自引出不同的后续通话。
            Advance(commsA, ref tA, 1.0);
            Advance(commsB, ref tB, 1.0);
            Assert.AreEqual("crew_calm", commsA.Current?.Id, "承认故障应引出班组配合的后续通话");
            Assert.AreEqual("crew_panic", commsB.Current?.Id, "推卸责任应引出班组恐慌的后续通话");

            Assert.Greater(commsA.Morale, commsB.Morale, "安抚应比推诿获得更高的士气");
        }

        [Test]
        public void 无人接听会记为未接来电并扣士气()
        {
            var comms = CommsSystem.BuildFirstShift();
            double t = 0.0;
            Advance(comms, ref t, 46.0);
            Assert.AreEqual(CallState.Ringing, comms.State);

            // 一直不接，等超过响铃时限。
            Advance(comms, ref t, 25.0);

            Assert.AreEqual(1, comms.MissedCalls);
            Assert.Less(comms.Morale, 0);
            Assert.Contains("missed:crew_ready", (System.Collections.ICollection)comms.TriggeredEvents);
        }

        [Test]
        public void 未响铃时接听与应答都无效()
        {
            var comms = CommsSystem.BuildFirstShift();
            Assert.IsFalse(comms.Answer(0.0));
            Assert.IsFalse(comms.Reply(0, 0.0));
        }

        [Test]
        public void 存档序列化后再读回状态完全一致()
        {
            var save = new CampaignSave
            {
                ShiftsCompleted = 3,
                TotalOrdersCompleted = 17,
                TotalOrdersFailed = 1,
                TotalIncidents = 1,
                CrewMorale = -4,
                GeneratorWear = 0.1234567890123,
                BrakePadWear = 0.4210,
                MotorWear = 0.0009,
                FanBearingWear = 0.0175,
                FuelRemaining = 0.6321,
                LastGrade = "B",
            };
            save.UnlockedEvents.Add("reply:fault_acknowledged");
            save.UnlockedEvents.Add("missed:crew_ready");

            var restored = SaveSystem.Deserialize(SaveSystem.Serialize(save));

            Assert.AreEqual(save.ShiftsCompleted, restored.ShiftsCompleted);
            Assert.AreEqual(save.TotalOrdersCompleted, restored.TotalOrdersCompleted);
            Assert.AreEqual(save.TotalOrdersFailed, restored.TotalOrdersFailed);
            Assert.AreEqual(save.TotalIncidents, restored.TotalIncidents);
            Assert.AreEqual(save.CrewMorale, restored.CrewMorale);
            Assert.AreEqual(save.GeneratorWear, restored.GeneratorWear, 0.0, "磨损必须逐位还原");
            Assert.AreEqual(save.BrakePadWear, restored.BrakePadWear, 0.0);
            Assert.AreEqual(save.MotorWear, restored.MotorWear, 0.0);
            Assert.AreEqual(save.FanBearingWear, restored.FanBearingWear, 0.0);
            Assert.AreEqual(save.FuelRemaining, restored.FuelRemaining, 0.0);
            Assert.AreEqual(save.LastGrade, restored.LastGrade);
            CollectionAssert.AreEqual(save.UnlockedEvents, restored.UnlockedEvents);
        }

        [Test]
        public void 存档写盘后重新读取状态相同()
        {
            string dir = Path.Combine(Path.GetTempPath(), "maner_save_test_" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var save = new CampaignSave { ShiftsCompleted = 2, GeneratorWear = 0.375, LastGrade = "A" };
                save.UnlockedEvents.Add("reply:by_the_book");

                Assert.IsFalse(SaveSystem.Exists(dir));
                SaveSystem.Write(dir, save);
                Assert.IsTrue(SaveSystem.Exists(dir));

                var reloaded = SaveSystem.Read(dir);
                Assert.AreEqual(2, reloaded.ShiftsCompleted);
                Assert.AreEqual(0.375, reloaded.GeneratorWear, 0.0);
                Assert.AreEqual("A", reloaded.LastGrade);
                CollectionAssert.AreEqual(save.UnlockedEvents, reloaded.UnlockedEvents);

                // 覆盖写入不应残留临时文件。
                SaveSystem.Write(dir, save);
                Assert.IsFalse(File.Exists(Path.Combine(dir, SaveSystem.FileName + ".tmp")));
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
        }

        [Test]
        public void 读取不存在的存档返回全新档()
        {
            string dir = Path.Combine(Path.GetTempPath(), "maner_missing_" + System.Guid.NewGuid().ToString("N"));
            var save = SaveSystem.Read(dir);
            Assert.AreEqual(0, save.ShiftsCompleted);
            Assert.AreEqual(1.0, save.FuelRemaining, 1e-9);
            Assert.IsEmpty(save.UnlockedEvents);
        }

        [Test]
        public void 结算写入存档后各项与仿真状态一致()
        {
            var sim = new ShaftSimulation();
            var console = new Maner.Controls.ConsoleState();
            var director = new ShiftDirector();
            director.Load(ShiftDirector.BuildFirstShift());
            director.ScheduleFirstShiftFaults();
            director.Begin();
            var robot = new RobotOperator();

            for (int i = 0; i < 50 * 2400 && director.Phase == ShiftPhase.Running; i++)
            {
                robot.Tick(director.ShiftTime, director, sim, console);
                console.DrainPulses(sim);
                sim.SetInputs(console.BuildInputs());
                sim.Step();
                director.Tick(Dt, sim, console);
            }
            director.ForceSettle(sim);

            var save = new CampaignSave();
            save.AbsorbSettlement(director.Settlement, -3, new[] { "reply:fault_acknowledged" });

            Assert.AreEqual(1, save.ShiftsCompleted);
            Assert.AreEqual(director.Settlement.CompletedOrders, save.TotalOrdersCompleted);
            Assert.AreEqual(director.Settlement.TimedOutOrders, save.TotalOrdersFailed);
            Assert.AreEqual(sim.Power.GeneratorWear, save.GeneratorWear, 1e-12);
            Assert.AreEqual(sim.Hoist.BrakePadWear, save.BrakePadWear, 1e-12);
            Assert.AreEqual(sim.Ventilation.FanBearingWear, save.FanBearingWear, 1e-12);
            Assert.AreEqual(sim.Power.FuelLevel, save.FuelRemaining, 1e-12);
            Assert.AreEqual(-3, save.CrewMorale);
            Assert.AreEqual(director.Settlement.Grade.ToString(), save.LastGrade);

            // 走一遍写盘与读回，确认结算数值在重启后仍然一致。
            var restored = SaveSystem.Deserialize(SaveSystem.Serialize(save));
            Assert.AreEqual(save.GeneratorWear, restored.GeneratorWear, 0.0);
            Assert.AreEqual(save.FuelRemaining, restored.FuelRemaining, 0.0);
            Assert.AreEqual(save.LastGrade, restored.LastGrade);
        }

        [Test]
        public void 磨损只增不减()
        {
            var save = new CampaignSave { GeneratorWear = 0.5, BrakePadWear = 0.3 };
            var lighter = new ShiftSettlement
            {
                TotalOrders = 6,
                CompletedOrders = 6,
                GeneratorWear = 0.1,
                BrakePadWear = 0.9,
                WorstIncident = IncidentSeverity.None,
            };

            save.AbsorbSettlement(lighter, 0, System.Array.Empty<string>());

            Assert.AreEqual(0.5, save.GeneratorWear, 1e-9, "设备不会自己变好");
            Assert.AreEqual(0.9, save.BrakePadWear, 1e-9, "更严重的磨损应被记录");
        }
    }
}
