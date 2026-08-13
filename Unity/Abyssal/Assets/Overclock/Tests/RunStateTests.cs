using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Overclock.Core.Tests
{
    /// <summary>
    /// 局内进度与升级的测试。
    ///
    /// 最重要的一组是「升级确实全局生效」：如果某个系统忘了走 <see cref="SiliconLayer.Spec"/>
    /// 而直接查了元件原始数值，玩家会拿到一个不起作用的升级，
    /// 而这种 bug 在游玩时几乎不可能被发现。
    /// </summary>
    [TestFixture]
    public class RunStateTests
    {
        static SiliconLayer MakeStrip(RunModifiers mods, int width = 8, int row = 1)
        {
            var layer = new SiliconLayer(width, 3) { Modifiers = mods, Budget = 999 };
            layer.SetTerrain(0, row, ComponentKind.Source);
            layer.SetTerrain(width - 1, row, ComponentKind.Sink);
            for (int x = 1; x < width - 1; x++) layer.Place(x, row, ComponentKind.Bus);
            return layer;
        }

        static void Run(SiliconLayer layer, double seconds, double dt = 0.05)
        {
            int steps = (int)Math.Round(seconds / dt);
            for (int i = 0; i < steps; i++) layer.Step(dt);
        }

        // ------------------------------------------------------------------ 修正生效

        [Test]
        public void 发热修正会影响升温速度()
        {
            // 在任何元件烧毁之前取样。烧毁之后温度会回落，
            // 那时候比当前温度反而是发热高的更低。
            double TempAfter(double heatMultiplier, double seconds)
            {
                var layer = MakeStrip(new RunModifiers { HeatMultiplier = heatMultiplier });
                Run(layer, seconds);
                Assert.AreEqual(0, layer.BurnedCount, "取样点必须早于第一次烧毁");
                return layer.PeakTemperature();
            }

            double cool = TempAfter(0.5, 2.0);
            double normal = TempAfter(1.0, 2.0);
            double hot = TempAfter(1.5, 2.0);

            Assert.Less(cool, normal, "降低发热必须升温更慢");
            Assert.Greater(hot, normal, "提高发热必须升温更快");
        }

        [Test]
        public void 发热越高烧得越早()
        {
            double TimeToFirstBurn(double heatMultiplier)
            {
                var layer = MakeStrip(new RunModifiers { HeatMultiplier = heatMultiplier });
                for (int i = 0; i < 4000; i++)
                {
                    layer.Step(0.05);
                    if (layer.BurnedCount > 0) return i * 0.05;
                }
                return double.PositiveInfinity;
            }

            double hot = TimeToFirstBurn(1.6);
            double normal = TimeToFirstBurn(1.0);

            Assert.Less(hot, normal, "发热倍率越高，第一次烧毁应该来得越早");
            Assert.IsTrue(double.IsFinite(hot), "1.6 倍发热必须能烧起来");
        }

        [Test]
        public void 历史峰值不会随烧毁回落()
        {
            var layer = MakeStrip(new RunModifiers { HeatMultiplier = 1.6 });
            Run(layer, 70.0);

            Assert.Greater(layer.BurnedCount, 0, "这组参数应该已经烧了");
            Assert.Greater(layer.HistoricalPeak, layer.PeakTemperature(),
                "烧毁后当前温度会掉下来，但历史峰值必须留住");
        }

        [Test]
        public void 吞吐修正会影响最大流()
        {
            double FlowWith(double multiplier)
            {
                var layer = MakeStrip(new RunModifiers { ThroughputMultiplier = multiplier });
                Run(layer, 1.0);
                return layer.CurrentThroughput;
            }

            double baseline = FlowWith(1.0);
            Assert.Greater(FlowWith(1.5), baseline * 1.3, "吞吐加成必须体现在最大流上");
            Assert.Less(FlowWith(0.5), baseline * 0.7);
        }

        [Test]
        public void 熔点加成会推迟烧毁()
        {
            int BurnedWith(double bonus)
            {
                var layer = MakeStrip(new RunModifiers { MeltingPointBonus = bonus });
                Run(layer, 70.0);
                return layer.BurnedCount;
            }

            Assert.Greater(BurnedWith(0.0), 0, "没有加成时这组参数应该烧");
            Assert.Less(BurnedWith(120.0), BurnedWith(0.0), "大幅提高熔点必须减少烧毁");
        }

        [Test]
        public void 导热修正会影响散热片效果()
        {
            double PeakWith(double conductivity)
            {
                var layer = new SiliconLayer(8, 3)
                {
                    Modifiers = new RunModifiers { ConductivityMultiplier = conductivity },
                    Budget = 999,
                };
                layer.SetTerrain(0, 1, ComponentKind.Source);
                layer.SetTerrain(7, 1, ComponentKind.Sink);
                for (int x = 1; x < 7; x++)
                {
                    layer.Place(x, 1, ComponentKind.Bus);
                    layer.Place(x, 0, ComponentKind.HeatSink);
                    layer.Place(x, 2, ComponentKind.HeatSink);
                }
                Run(layer, 8.0);
                return layer.HeatAt(4, 1);
            }

            Assert.Less(PeakWith(2.0), PeakWith(1.0), "导热加成必须让散热片更管用");
        }

        [Test]
        public void 成本折扣会影响放置扣费()
        {
            var layer = new SiliconLayer(6, 3)
            {
                Modifiers = new RunModifiers { CostDiscount = 0.5 },
                Budget = 10,
            };

            // 总线原价 6，五折后 3。
            Assert.IsTrue(layer.Place(2, 1, ComponentKind.Bus));
            Assert.AreEqual(7, layer.Budget);
        }

        [Test]
        public void 成本折扣不会把价格压到零()
        {
            var layer = new SiliconLayer(6, 3)
            {
                Modifiers = new RunModifiers { CostDiscount = 0.99 },
                Budget = 10,
            };

            layer.Place(2, 1, ComponentKind.Trace);
            Assert.Less(layer.Budget, 10, "再大的折扣，元件也不能白拿");
        }

        // ------------------------------------------------------------------ 升级抽取

        [Test]
        public void 抽三个不重复的升级()
        {
            var taken = new List<string>();
            var drawn = UpgradeLibrary.Draw(42, taken);

            Assert.AreEqual(3, drawn.Count);
            Assert.AreEqual(3, drawn.Select(u => u.Id).Distinct().Count(), "同一次抽取不能有重复项");
        }

        [Test]
        public void 已拿过的升级不再出现()
        {
            var taken = new List<string> { "cryo_etch", "refractory", "wide_rail" };
            for (int seed = 0; seed < 40; seed++)
            {
                var drawn = UpgradeLibrary.Draw(seed, taken);
                foreach (var u in drawn)
                    Assert.IsFalse(taken.Contains(u.Id), $"种子 {seed} 抽到了已拿过的 {u.Id}");
            }
        }

        [Test]
        public void 升级池抽干后返回剩余项而不是报错()
        {
            var taken = UpgradeLibrary.All.Select(u => u.Id).Take(UpgradeLibrary.All.Length - 1).ToList();
            var drawn = UpgradeLibrary.Draw(1, taken);
            Assert.AreEqual(1, drawn.Count, "池子里只剩一个时就只给一个，不能抛异常");
        }

        [Test]
        public void 每个升级都真的改变了修正值()
        {
            foreach (var upgrade in UpgradeLibrary.All)
            {
                var mods = new RunModifiers();
                var before = Snapshot(mods);
                upgrade.Apply(mods);
                Assert.AreNotEqual(before, Snapshot(mods),
                    $"升级 {upgrade.Id} 应用后什么都没变，玩家会觉得这个选项是假的");
            }
        }

        static string Snapshot(RunModifiers m) =>
            $"{m.HeatMultiplier:F4}|{m.ThroughputMultiplier:F4}|{m.ConductivityMultiplier:F4}|" +
            $"{m.MeltingPointBonus:F4}|{m.BudgetBonus}|{m.CostDiscount:F4}";

        // ------------------------------------------------------------------ 局进度

        [Test]
        public void 层数推进与难度递增()
        {
            var run = new RunState(7, totalLayers: 8);

            Assert.AreEqual(1, run.LayerIndex);
            Assert.AreEqual(0.0, run.Difficulty, 1e-9, "第一层应该是零难度");

            for (int i = 0; i < 7; i++) run.AdvanceLayer();

            Assert.AreEqual(8, run.LayerIndex);
            Assert.AreEqual(1.0, run.Difficulty, 1e-9, "最后一层应该是满难度");
            Assert.IsFalse(run.RunComplete);

            run.AdvanceLayer();
            Assert.IsTrue(run.RunComplete);
        }

        [Test]
        public void 拿升级后修正累积到局状态上()
        {
            var run = new RunState(3);
            double before = run.Modifiers.HeatMultiplier;

            var cryo = UpgradeLibrary.All.First(u => u.Id == "cryo_etch");
            run.TakeUpgrade(cryo);

            Assert.Less(run.Modifiers.HeatMultiplier, before);
            Assert.Contains("cryo_etch", run.TakenUpgrades);
        }

        [Test]
        public void 局状态生成的层带着当前修正()
        {
            var run = new RunState(5);
            run.TakeUpgrade(UpgradeLibrary.All.First(u => u.Id == "extra_mask"));

            var layer = run.CreateLayer(14, 9);

            Assert.AreSame(run.Modifiers, layer.Modifiers, "层必须引用同一份修正");
            Assert.Greater(layer.Budget, 40, "追加掩模的预算加成要体现在这一层上");
        }

        [Test]
        public void 主导轴线反映玩家的选择倾向()
        {
            var run = new RunState(9);
            run.TakeUpgrade(UpgradeLibrary.All.First(u => u.Id == "cryo_etch"));
            run.TakeUpgrade(UpgradeLibrary.All.First(u => u.Id == "lattice_align"));
            run.TakeUpgrade(UpgradeLibrary.All.First(u => u.Id == "refractory"));

            Assert.AreEqual(BuildAxis.Superconductor, run.DominantAxis(),
                "拿了两个超导系一个暴力系，主导轴线应该是超导");
        }

        [Test]
        public void 每层生成的地形都有解()
        {
            var run = new RunState(17, totalLayers: 8);
            for (int i = 0; i < 8; i++)
            {
                var layer = run.CreateLayer(16, 10);
                Assert.IsTrue(LayerGenerator.HasPath(layer), $"第 {run.LayerIndex} 层无解");
                run.AdvanceLayer();
            }
        }
    }
}
