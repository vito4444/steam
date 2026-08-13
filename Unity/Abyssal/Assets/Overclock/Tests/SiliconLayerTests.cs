using System;
using NUnit.Framework;

namespace Overclock.Core.Tests
{
    /// <summary>
    /// 硅层的行为测试。每一条守护的都是一条玩法规则。
    /// 判断标准：把被守护的那段逻辑改坏，这条测试必须变红。
    /// </summary>
    [TestFixture]
    public class SiliconLayerTests
    {
        /// <summary>建一层最简单的：左边一个源，右边一个汇，中间空着。</summary>
        static SiliconLayer MakeStrip(int width = 8, int height = 3, int sourceRow = 1)
        {
            var layer = new SiliconLayer(width, height);
            layer.SetTerrain(0, sourceRow, ComponentKind.Source);
            layer.SetTerrain(width - 1, sourceRow, ComponentKind.Sink);
            layer.Budget = 999;
            return layer;
        }

        static void Connect(SiliconLayer layer, int row, ComponentKind kind)
        {
            for (int x = 1; x < layer.Width - 1; x++) layer.Place(x, row, kind);
        }

        static void Run(SiliconLayer layer, double seconds, double dt = 0.05)
        {
            int steps = (int)Math.Round(seconds / dt);
            for (int i = 0; i < steps; i++) layer.Step(dt);
        }

        // ------------------------------------------------------------------ 流量

        [Test]
        public void 源汇之间没有通路时吞吐量为零()
        {
            var layer = MakeStrip();
            Run(layer, 1.0);
            Assert.AreEqual(0.0, layer.CurrentThroughput, 1e-9, "中间什么都没铺，不该有流量");
        }

        [Test]
        public void 铺通导线后产生吞吐量()
        {
            var layer = MakeStrip();
            Connect(layer, 1, ComponentKind.Trace);
            Run(layer, 1.0);
            Assert.Greater(layer.CurrentThroughput, 0.0, "铺通之后必须有流量");
        }

        [Test]
        public void 吞吐量受路径上最细的元件限制()
        {
            // 导线上限 6，总线上限 20。整条路铺总线只在中间留一段导线，
            // 最大流应该被那一段卡在 6。
            var layer = MakeStrip(9);
            Connect(layer, 1, ComponentKind.Bus);
            layer.Place(4, 1, ComponentKind.Trace);
            Run(layer, 1.0);

            Assert.AreEqual(6.0, layer.CurrentThroughput, 0.01,
                "最大流应该等于路径瓶颈的容量");
        }

        [Test]
        public void 并联两条路能提高总吞吐量()
        {
            var layer = new SiliconLayer(8, 5);
            layer.SetTerrain(0, 1, ComponentKind.Source);
            layer.SetTerrain(0, 3, ComponentKind.Source);
            layer.SetTerrain(7, 1, ComponentKind.Sink);
            layer.SetTerrain(7, 3, ComponentKind.Sink);
            layer.Budget = 999;

            Connect(layer, 1, ComponentKind.Trace);
            Run(layer, 1.0);
            double single = layer.CurrentThroughput;

            Connect(layer, 3, ComponentKind.Trace);
            Run(layer, 1.0);
            double dual = layer.CurrentThroughput;

            Assert.Greater(dual, single * 1.5, "两条独立通路的吞吐应该接近翻倍");
        }

        // ------------------------------------------------------------------ 热

        [Test]
        public void 有流量的元件会发热()
        {
            var layer = MakeStrip();
            Connect(layer, 1, ComponentKind.Trace);
            Run(layer, 3.0);

            Assert.Greater(layer.HeatAt(4, 1), SiliconLayer.AmbientTemperature + 5.0,
                "通着数据的导线必须升温");
        }

        [Test]
        public void 空置的格子不会自己发热()
        {
            var layer = MakeStrip();
            Run(layer, 5.0);
            Assert.AreEqual(SiliconLayer.AmbientTemperature, layer.HeatAt(4, 0), 0.5,
                "没有元件也没有流量的格子应该保持衬底温度");
        }

        [Test]
        public void 总线比导线热得快()
        {
            double HeatAfter(ComponentKind kind)
            {
                var layer = MakeStrip();
                Connect(layer, 1, kind);
                Run(layer, 4.0);
                return layer.HeatAt(4, 1);
            }

            Assert.Greater(HeatAfter(ComponentKind.Bus), HeatAfter(ComponentKind.Trace),
                "总线的单位发热远高于导线");
        }

        [Test]
        public void 散热片能压低相邻元件的温度()
        {
            double PeakWith(bool withSinks)
            {
                var layer = MakeStrip(8, 3);
                Connect(layer, 1, ComponentKind.Bus);
                if (withSinks)
                {
                    for (int x = 1; x < 7; x++)
                    {
                        layer.Place(x, 0, ComponentKind.HeatSink);
                        layer.Place(x, 2, ComponentKind.HeatSink);
                    }
                }
                Run(layer, 6.0);
                return layer.HeatAt(4, 1);
            }

            double bare = PeakWith(false);
            double cooled = PeakWith(true);

            Assert.Less(cooled, bare, "两侧贴满散热片必须把总线的温度压下来");
            Assert.Less(cooled, bare * 0.92, "降温幅度要足够明显，否则散热片就没有存在意义");
        }

        [Test]
        public void 热量会扩散到相邻格子()
        {
            var layer = MakeStrip(8, 3);
            Connect(layer, 1, ComponentKind.Bus);
            for (int x = 1; x < 7; x++) layer.Place(x, 0, ComponentKind.HeatSink);

            Run(layer, 5.0);

            Assert.Greater(layer.HeatAt(4, 0), SiliconLayer.AmbientTemperature + 2.0,
                "散热片本身不发热，它升温只可能来自邻格传过来的热量");
        }

        // ------------------------------------------------------------------ 烧毁

        [Test]
        public void 温度超过熔点的元件会被烧毁()
        {
            var layer = MakeStrip(8, 3);
            Connect(layer, 1, ComponentKind.Bus);

            // 不放任何散热片，总线自己会把自己烧掉。
            Run(layer, 60.0);

            Assert.Greater(layer.BurnedCount, 0, "长时间满载无散热必须烧元件");
        }

        [Test]
        public void 烧毁会切断流量()
        {
            var layer = MakeStrip(8, 3);
            Connect(layer, 1, ComponentKind.Bus);
            Run(layer, 3.0);
            double before = layer.CurrentThroughput;

            Run(layer, 80.0);

            Assert.Greater(before, 0.0);
            Assert.Greater(layer.BurnedCount, 0);
            Assert.Less(layer.CurrentThroughput, before,
                "线路上烧掉元件之后吞吐必须下降");
        }

        [Test]
        public void 烧毁的格子不能再放东西()
        {
            var layer = MakeStrip(8, 3);
            Connect(layer, 1, ComponentKind.Bus);
            Run(layer, 80.0);

            int burnedX = -1;
            for (int x = 1; x < 7; x++)
            {
                if (!layer.IsBurned(x, 1)) continue;
                burnedX = x;
                break;
            }

            Assert.Greater(burnedX, 0, "这组参数应该已经烧掉了至少一格");
            Assert.IsFalse(layer.Place(burnedX, 1, ComponentKind.Trace),
                "烧毁是永久损失，不能靠重铺来修复");
        }

        [Test]
        public void 配了散热的布局能活得更久()
        {
            int BurnedAfter(bool cooled)
            {
                var layer = MakeStrip(8, 3);
                Connect(layer, 1, ComponentKind.Bus);
                if (cooled)
                {
                    for (int x = 1; x < 7; x++)
                    {
                        layer.Place(x, 0, ComponentKind.HeatSink);
                        layer.Place(x, 2, ComponentKind.HeatSink);
                    }
                }
                Run(layer, 60.0);
                return layer.BurnedCount;
            }

            Assert.Greater(BurnedAfter(false), BurnedAfter(true),
                "散热布局的存活元件数必须显著更多");
        }

        // ------------------------------------------------------------------ 预算

        [Test]
        public void 放置会扣预算()
        {
            var layer = MakeStrip();
            layer.Budget = 10;
            Assert.IsTrue(layer.Place(2, 1, ComponentKind.Bus));
            Assert.AreEqual(4, layer.Budget, "总线成本 6，预算应该从 10 降到 4");
        }

        [Test]
        public void 预算不足时放不下()
        {
            var layer = MakeStrip();
            layer.Budget = 2;
            Assert.IsFalse(layer.Place(2, 1, ComponentKind.Bus), "预算 2 放不起成本 6 的总线");
            Assert.IsTrue(layer.Place(2, 1, ComponentKind.Trace), "但放得起成本 1 的导线");
        }

        [Test]
        public void 拆除返还一半成本()
        {
            var layer = MakeStrip();
            layer.Budget = 10;
            layer.Place(2, 1, ComponentKind.Bus);
            layer.Remove(2, 1);
            Assert.AreEqual(7, layer.Budget, "成本 6 的总线拆掉应该返还 3");
        }

        [Test]
        public void 数据源和输出端口不能被覆盖或拆除()
        {
            var layer = MakeStrip();
            Assert.IsFalse(layer.Place(0, 1, ComponentKind.Bus), "数据源属于地形");
            Assert.IsFalse(layer.Remove(0, 1), "数据源不能拆");
            Assert.AreEqual(ComponentKind.Source, layer.CellAt(0, 1));
        }

        // ------------------------------------------------------------------ 通关

        [Test]
        public void 持续达标才算通过()
        {
            var layer = MakeStrip(8, 3);
            layer.TargetThroughput = 5.0;
            layer.RequiredHoldTime = 4.0;
            Connect(layer, 1, ComponentKind.Trace);

            Run(layer, 2.0);
            Assert.IsFalse(layer.Cleared, "达标时间还不够，不能算通过");

            Run(layer, 5.0);
            Assert.IsTrue(layer.Cleared, "持续达标足够久之后应该通过");
        }

        [Test]
        public void 吞吐不达标时不会累积进度()
        {
            var layer = MakeStrip(8, 3);
            layer.TargetThroughput = 50.0;   // 导线最多 6，永远够不着
            layer.RequiredHoldTime = 2.0;
            Connect(layer, 1, ComponentKind.Trace);

            Run(layer, 10.0);

            Assert.IsFalse(layer.Cleared);
            Assert.AreEqual(0.0, layer.HoldProgress, 1e-9);
        }

        // ------------------------------------------------------------------ 关卡生成

        [Test]
        public void 生成的层一定存在可行解()
        {
            for (int seed = 0; seed < 60; seed++)
            {
                var layer = LayerGenerator.Generate(seed, 16, 10, seed / 60.0);
                Assert.IsTrue(LayerGenerator.HasPath(layer),
                    $"种子 {seed} 生成的层把路堵死了");
            }
        }

        [Test]
        public void 难度越高坏块越多()
        {
            int DeadCells(double difficulty)
            {
                var layer = LayerGenerator.Generate(7, 20, 12, difficulty);
                int count = 0;
                for (int y = 0; y < layer.Height; y++)
                for (int x = 0; x < layer.Width; x++)
                    if (layer.CellAt(x, y) == ComponentKind.DeadCell) count++;
                return count;
            }

            Assert.Greater(DeadCells(1.0), DeadCells(0.0), "高难度的层应该更破碎");
        }

        [Test]
        public void 朴素单线安全但达不到目标吞吐()
        {
            // 这是整个游戏的核心张力：最保守的解跑得动，但跑不够快。
            // 单条导线的平衡温度远低于熔点，永远不会烧；
            // 代价是它的吞吐上限只有 6，够不到任何一层的目标。
            var layer = LayerGenerator.Generate(3, 16, 9, 0.3);
            layer.Budget = 999;
            Assert.IsTrue(LayerGenerator.LayNaiveRoute(layer), "应该能找到一条可行路径");

            Run(layer, 90.0);

            Assert.Greater(layer.CurrentThroughput, 0.0, "朴素连线至少要能通");
            Assert.AreEqual(0, layer.BurnedCount, "低发热的单线不该把自己烧掉");
            Assert.Less(layer.CurrentThroughput, layer.TargetThroughput,
                "但单线的吞吐必须够不到目标，否则玩家没有理由承担任何风险");
        }

        [Test]
        public void 想要高吞吐就得承担烧毁风险()
        {
            // 换成总线，吞吐上去了，但没有散热配套的话它会把自己烧断。
            // 这一条和上一条合起来定义了玩家每一层要做的决策。
            var layer = MakeStrip(10, 3);
            Connect(layer, 1, ComponentKind.Bus);

            Run(layer, 3.0);
            double peak = layer.CurrentThroughput;
            Assert.GreaterOrEqual(peak, 12.0, "总线的吞吐应该远高于导线");

            Run(layer, 80.0);
            Assert.Greater(layer.BurnedCount, 0, "没有散热的总线必须烧毁");
            Assert.Less(layer.CurrentThroughput, peak, "烧毁之后吞吐必须掉下来");
        }

        // ------------------------------------------------------------------ 健壮性

        [Test]
        public void 极端步长不产生非有限数()
        {
            var layer = MakeStrip(10, 5);
            Connect(layer, 2, ComponentKind.Bus);

            for (int i = 0; i < 200; i++) layer.Step(0.1);

            for (int y = 0; y < layer.Height; y++)
            for (int x = 0; x < layer.Width; x++)
            {
                double h = layer.HeatAt(x, y);
                Assert.IsFalse(double.IsNaN(h) || double.IsInfinity(h),
                    $"({x},{y}) 的温度变成了非有限数");
            }
        }

        [Test]
        public void 零时间步不改变状态()
        {
            var layer = MakeStrip();
            Connect(layer, 1, ComponentKind.Trace);
            Run(layer, 1.0);

            double heat = layer.HeatAt(4, 1);
            layer.Step(0.0);

            Assert.AreEqual(heat, layer.HeatAt(4, 1), 1e-12);
        }
    }
}
