using System;
using System.Collections.Generic;

namespace Overclock.Core
{
    /// <summary>
    /// 一个会玩这个游戏的程序。
    ///
    /// 它的用途不是替玩家玩，而是当作平衡性的测量仪器：
    /// 让它用几种不同水平的策略跑几千局，看通关率落在哪、卡在第几层、
    /// 有没有哪条策略强到能无脑通关。这些问题靠人工试玩要几十小时才能回答，
    /// 而这里几秒钟就能跑完，因为整套核心逻辑不依赖 Unity。
    /// </summary>
    public sealed class AutoPlayer
    {
        public enum Strategy
        {
            /// <summary>只铺一条最短路的导线。最保守，理论上永不烧毁。</summary>
            Minimal,

            /// <summary>只铺总线，不管散热。最激进。</summary>
            Greedy,

            /// <summary>铺主路加散热，温度高了再补。这是一个合格玩家该有的水平。</summary>
            Balanced,
        }

        readonly Strategy _strategy;
        readonly Random _rng;

        public AutoPlayer(Strategy strategy, int seed)
        {
            _strategy = strategy;
            _rng = new Random(seed);
        }

        /// <summary>一层的结算结果。</summary>
        public struct LayerResult
        {
            public bool Cleared;
            public double Seconds;
            public double PeakTemperature;
            public int Burned;
            public double FinalThroughput;
            public double TargetThroughput;
            public int BudgetLeft;
        }

        /// <summary>
        /// 玩一层，最多 <paramref name="timeLimit"/> 秒游戏内时间。
        /// 超时视为没打过。
        /// </summary>
        public LayerResult PlayLayer(SiliconLayer layer, double timeLimit = 120.0)
        {
            const double dt = 0.05;
            double elapsed = 0.0;
            double sinceReview = 0.0;

            LayOutInitial(layer);

            while (elapsed < timeLimit && !layer.Cleared && !layer.Failed)
            {
                layer.Step(dt);
                elapsed += dt;
                sinceReview += dt;

                // 每半秒检查一次要不要补东西。真人不会每一帧都改布局。
                if (sinceReview >= 0.5)
                {
                    sinceReview = 0.0;
                    Review(layer);
                }
            }

            return new LayerResult
            {
                Cleared = layer.Cleared,
                Seconds = elapsed,
                PeakTemperature = layer.HistoricalPeak,
                Burned = layer.BurnedCount,
                FinalThroughput = layer.CurrentThroughput,
                TargetThroughput = layer.TargetThroughput,
                BudgetLeft = layer.Budget,
            };
        }

        void LayOutInitial(SiliconLayer layer)
        {
            switch (_strategy)
            {
                case Strategy.Minimal:
                    // 只铺一条，铺完就不管了。这是「不敢冒险」的极端。
                    LayerGenerator.LayNaiveRoute(layer, ComponentKind.Trace);
                    break;

                case Strategy.Greedy:
                    // 有多少预算就铺多少总线，完全不考虑散热。
                    for (int attempt = 0; attempt < 6; attempt++)
                        if (!LayerGenerator.LayNaiveRoute(layer, ComponentKind.Bus)) break;
                    break;

                case Strategy.Balanced:
                    // 先用最便宜的导线铺通若干条独立通路。
                    // 一条总线要花掉 80 多点，铺完就没钱做别的了；
                    // 同样的预算能铺五六条导线，总吞吐更高而且没有单点故障。
                    // 贵元件留到确实需要突破瓶颈时再用。
                    for (int attempt = 0; attempt < 6; attempt++)
                    {
                        if (layer.Budget < 20) break;
                        if (!LayerGenerator.LayNaiveRoute(layer, ComponentKind.Trace)) break;
                    }
                    break;
            }
        }

        /// <summary>
        /// 中途调整。平衡策略会盯着最热的格子，在它旁边补散热片；
        /// 吞吐不够时再尝试加一条通路。
        /// </summary>
        void Review(SiliconLayer layer)
        {
            if (_strategy == Strategy.Minimal) return;

            if (_strategy == Strategy.Balanced)
            {
                double stress = layer.PeakThermalStress();
                if (stress > 0.42)
                {
                    CoolHottestNeighbourhood(layer);
                    return;
                }
            }

            if (layer.CurrentThroughput < layer.TargetThroughput)
            {
                var kind = _strategy == Strategy.Greedy ? ComponentKind.Bus : ComponentKind.Trace;
                LayerGenerator.LayNaiveRoute(layer, kind);
            }
        }

        /// <summary>找到最烫的格子，在它四周还空着的地方放散热片。</summary>
        void CoolHottestNeighbourhood(SiliconLayer layer)
        {
            // 按温度排序，从最烫的几处一起下手。
            // 一次只贴一片跟不上升温速度——温度从告警冲到熔点只要几秒。
            var hotspots = new List<(double stress, int x, int y)>();
            for (int y = 0; y < layer.Height; y++)
            for (int x = 0; x < layer.Width; x++)
            {
                double s = layer.ThermalStressAt(x, y);
                if (s > 0.4) hotspots.Add((s, x, y));
            }

            if (hotspots.Count == 0) return;
            hotspots.Sort((a, b) => b.stress.CompareTo(a.stress));

            var steps = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
            int placed = 0;

            foreach (var (_, hx, hy) in hotspots)
            {
                if (placed >= 4) break;
                foreach (var (dx, dy) in steps)
                {
                    int nx = hx + dx, ny = hy + dy;
                    if (!layer.InBounds(nx, ny)) continue;
                    if (layer.CellAt(nx, ny) != ComponentKind.Empty) continue;
                    if (!layer.Place(nx, ny, ComponentKind.HeatSink)) continue;
                    placed++;
                    break;
                }
            }
        }

        /// <summary>一整局的结算结果。</summary>
        public struct RunResult
        {
            public bool Completed;
            public int LayersCleared;
            public int TotalBurned;
            public double WorstPeak;
            public BuildAxis Axis;
        }

        /// <summary>
        /// 跑完整的一局。升级按「优先补当前最缺的东西」来选，
        /// 这模拟的是一个会看数值的玩家，而不是随机点。
        /// </summary>
        public RunResult PlayRun(int seed, int width = 16, int height = 10, int totalLayers = 8)
        {
            var run = new RunState(seed, totalLayers);
            int cleared = 0, burned = 0;
            double worstPeak = 0.0;

            while (!run.RunComplete)
            {
                var layer = run.CreateLayer(width, height);
                var result = PlayLayer(layer);

                burned += result.Burned;
                if (result.PeakTemperature > worstPeak) worstPeak = result.PeakTemperature;

                if (!result.Cleared)
                {
                    return new RunResult
                    {
                        Completed = false,
                        LayersCleared = cleared,
                        TotalBurned = burned,
                        WorstPeak = worstPeak,
                        Axis = run.DominantAxis(),
                    };
                }

                cleared++;

                var offers = run.DrawUpgrades();
                if (offers.Count > 0) run.TakeUpgrade(ChooseUpgrade(offers, result));
                run.AdvanceLayer();
            }

            return new RunResult
            {
                Completed = true,
                LayersCleared = cleared,
                TotalBurned = burned,
                WorstPeak = worstPeak,
                Axis = run.DominantAxis(),
            };
        }

        /// <summary>
        /// 选升级。上一层烧过东西就优先降温，吞吐没达标就优先提吞吐，
        /// 都还行就拿预算。随机策略会把平衡性数据搅浑，所以这里是确定性的。
        /// </summary>
        Upgrade ChooseUpgrade(List<Upgrade> offers, LayerResult lastLayer)
        {
            bool overheated = lastLayer.Burned > 0 || lastLayer.PeakTemperature > 140.0;
            bool starved = lastLayer.FinalThroughput < lastLayer.TargetThroughput * 1.15;

            BuildAxis want;
            if (overheated) want = BuildAxis.Superconductor;
            else if (starved) want = BuildAxis.BruteForce;
            else want = BuildAxis.Parallel;

            foreach (var u in offers) if (u.Axis == want) return u;
            return offers[_rng.Next(offers.Count)];
        }
    }
}
