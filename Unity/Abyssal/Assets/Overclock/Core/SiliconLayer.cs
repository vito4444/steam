using System;
using System.Collections.Generic;

namespace Overclock.Core
{
    /// <summary>
    /// 一层硅片。这是 OVERCLOCK 的核心系统。
    ///
    /// 玩家在网格上铺元件，把数据源连到输出端口。数据一流动，元件就发热；
    /// 热量在网格上扩散，扩散不掉的地方温度会一路涨到元件熔点，然后那个格子
    /// 永久烧毁，流水线当场断掉。
    ///
    /// 关键在于热量只增不减、只能转移。这一条规则同时提供了三样东西：
    /// 空间布局的真实意义（不能把高发热元件堆在一起）、
    /// 时间压力（不能慢慢想）、以及每一局必然走向的终点（roguelite 需要有输的方式）。
    ///
    /// 类里不引用任何 UnityEngine 类型，可以直接在命令行下跑批量参数扫描。
    /// </summary>
    public sealed class SiliconLayer
    {
        // ------------------------------------------------------------------ 系数
        // 这些数字决定了「安全区」有多窄。改动前请先跑 ParameterSweep 看解空间形状。

        /// <summary>热扩散率。越大热量摊得越开，越不容易局部过热。</summary>
        const double DiffusionRate = 0.42;

        /// <summary>向衬底散失的热量比例。这是系统唯一的净散热途径，刻意设得很小。</summary>
        const double SubstrateLoss = 0.020;

        /// <summary>衬底温度，也是每层的起始温度。</summary>
        public const double AmbientTemperature = 24.0;

        /// <summary>流量网络重算的间隔。视觉上的数据包和这个解耦，所以不需要每帧算。</summary>
        const double FlowRecalcInterval = 0.35;

        // ------------------------------------------------------------------ 状态

        public int Width { get; }
        public int Height { get; }

        readonly ComponentKind[] _cells;
        readonly double[] _heat;
        readonly double[] _load;
        readonly double[] _scratch;
        readonly bool[] _burned;

        double _flowTimer;
        double _elapsed;

        /// <summary>本层要求的持续吞吐量。达标并维持住才算通过。</summary>
        public double TargetThroughput { get; set; } = 18.0;

        /// <summary>达标需要维持的秒数。</summary>
        public double RequiredHoldTime { get; set; } = 6.0;

        /// <summary>当前实际吞吐量。</summary>
        public double CurrentThroughput { get; private set; }

        /// <summary>已经连续达标的时间。</summary>
        public double HoldProgress { get; private set; }

        /// <summary>本层剩余的蚀刻预算。</summary>
        public int Budget { get; set; } = 40;

        /// <summary>本层是否已经通过。</summary>
        public bool Cleared => HoldProgress >= RequiredHoldTime;

        /// <summary>本次运行是否已经失败：数据源和输出端口之间再也接不通了。</summary>
        public bool Failed { get; private set; }

        /// <summary>本层累计烧毁的元件数。</summary>
        public int BurnedCount { get; private set; }

        public SiliconLayer(int width, int height)
        {
            if (width < 3 || height < 3)
                throw new ArgumentException("硅层至少要 3x3");

            Width = width;
            Height = height;

            int n = width * height;
            _cells = new ComponentKind[n];
            _heat = new double[n];
            _load = new double[n];
            _scratch = new double[n];
            _burned = new bool[n];

            for (int i = 0; i < n; i++) _heat[i] = AmbientTemperature;
        }

        public int Index(int x, int y) => y * Width + x;
        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public ComponentKind CellAt(int x, int y) => InBounds(x, y) ? _cells[Index(x, y)] : ComponentKind.DeadCell;
        public double HeatAt(int x, int y) => InBounds(x, y) ? _heat[Index(x, y)] : AmbientTemperature;
        public double LoadAt(int x, int y) => InBounds(x, y) ? _load[Index(x, y)] : 0.0;
        public bool IsBurned(int x, int y) => InBounds(x, y) && _burned[Index(x, y)];

        /// <summary>元件当前的负载率 0–1，用来驱动发光强度。</summary>
        public double UtilizationAt(int x, int y)
        {
            if (!InBounds(x, y)) return 0.0;
            var spec = ComponentLibrary.Of(_cells[Index(x, y)]);
            return spec.Throughput <= 0.0 ? 0.0 : Math.Min(1.0, _load[Index(x, y)] / spec.Throughput);
        }

        /// <summary>温度相对熔点的比例 0–1，1 表示马上就要烧了。</summary>
        public double ThermalStressAt(int x, int y)
        {
            if (!InBounds(x, y)) return 0.0;
            int i = Index(x, y);
            var spec = ComponentLibrary.Of(_cells[i]);
            if (spec.MeltingPoint <= AmbientTemperature) return 0.0;
            return Clamp01((_heat[i] - AmbientTemperature) / (spec.MeltingPoint - AmbientTemperature));
        }

        // ------------------------------------------------------------------ 编辑

        /// <summary>直接写入地形。仅供关卡生成使用，不检查预算。</summary>
        public void SetTerrain(int x, int y, ComponentKind kind)
        {
            if (!InBounds(x, y)) return;
            _cells[Index(x, y)] = kind;
        }

        /// <summary>
        /// 玩家放置元件。烧毁的格子放不了东西，预算不够也放不了。
        /// 返回是否放置成功。
        /// </summary>
        public bool Place(int x, int y, ComponentKind kind)
        {
            if (!InBounds(x, y)) return false;
            if (!ComponentLibrary.IsPlaceable(kind)) return false;

            int i = Index(x, y);
            if (_burned[i]) return false;
            if (_cells[i] == ComponentKind.Source || _cells[i] == ComponentKind.Sink) return false;
            if (_cells[i] == ComponentKind.DeadCell) return false;

            int cost = ComponentLibrary.Of(kind).Cost;
            int refund = _cells[i] == ComponentKind.Empty
                ? 0
                : ComponentLibrary.Of(_cells[i]).Cost / 2;

            if (Budget + refund < cost) return false;

            Budget += refund - cost;
            _cells[i] = kind;
            return true;
        }

        /// <summary>拆除元件，返还一半成本。烧毁的格子拆不掉，那是永久损失。</summary>
        public bool Remove(int x, int y)
        {
            if (!InBounds(x, y)) return false;
            int i = Index(x, y);
            if (_burned[i]) return false;
            if (!ComponentLibrary.IsPlaceable(_cells[i])) return false;

            Budget += ComponentLibrary.Of(_cells[i]).Cost / 2;
            _cells[i] = ComponentKind.Empty;
            return true;
        }

        // ------------------------------------------------------------------ 仿真

        /// <summary>
        /// 推进 <paramref name="dt"/> 秒。
        /// 建议步长不超过 0.1 秒，更大的步长会让热扩散的显式差分发散。
        /// </summary>
        public void Step(double dt)
        {
            if (dt <= 0.0 || Failed) return;
            _elapsed += dt;

            _flowTimer += dt;
            if (_flowTimer >= FlowRecalcInterval)
            {
                _flowTimer = 0.0;
                RecalculateFlow();
            }

            DiffuseHeat(dt);
            BurnOverheated();
            UpdateProgress(dt);
        }

        /// <summary>
        /// 重算流量。用 Edmonds-Karp 求最大流：所有数据源接一个超级源，
        /// 所有输出端口接一个超级汇，相邻的导数据格子之间连边，
        /// 容量取两格吞吐上限的较小值。
        ///
        /// 视觉上的数据包只是按各条边的流量密度生成的粒子，和这里完全解耦——
        /// 这是自动化游戏能做到大规模而不掉帧的关键技巧。
        /// </summary>
        void RecalculateFlow()
        {
            Array.Clear(_load, 0, _load.Length);

            int n = Width * Height;
            int superSource = n;
            int superSink = n + 1;
            int nodeCount = n + 2;

            // 邻接表。每条无向边拆成两条有向边，互为反向边。
            var head = new int[nodeCount];
            for (int i = 0; i < nodeCount; i++) head[i] = -1;

            var edgeTo = new List<int>();
            var edgeCap = new List<double>();
            var edgeNext = new List<int>();

            void AddEdge(int from, int to, double cap)
            {
                edgeTo.Add(to); edgeCap.Add(cap); edgeNext.Add(head[from]); head[from] = edgeTo.Count - 1;
                edgeTo.Add(from); edgeCap.Add(0.0); edgeNext.Add(head[to]); head[to] = edgeTo.Count - 1;
            }

            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                int i = Index(x, y);
                var kind = _cells[i];
                if (_burned[i] || !ComponentLibrary.CarriesData(kind)) continue;

                double capHere = ComponentLibrary.Of(kind).Throughput;

                if (kind == ComponentKind.Source) AddEdge(superSource, i, capHere);
                if (kind == ComponentKind.Sink) AddEdge(i, superSink, capHere);

                // 只往右和往下连，避免同一条边被加两次。
                foreach (var (dx, dy) in Neighbours)
                {
                    if (dx < 0 || dy < 0) continue;
                    int nx = x + dx, ny = y + dy;
                    if (!InBounds(nx, ny)) continue;

                    int j = Index(nx, ny);
                    if (_burned[j] || !ComponentLibrary.CarriesData(_cells[j])) continue;

                    double cap = Math.Min(capHere, ComponentLibrary.Of(_cells[j]).Throughput);
                    AddEdge(i, j, cap);
                    AddEdge(j, i, cap);
                }
            }

            double maxFlow = 0.0;
            var parentEdge = new int[nodeCount];
            var queue = new Queue<int>();

            while (true)
            {
                for (int i = 0; i < nodeCount; i++) parentEdge[i] = -1;
                parentEdge[superSource] = -2;
                queue.Clear();
                queue.Enqueue(superSource);

                while (queue.Count > 0 && parentEdge[superSink] == -1)
                {
                    int u = queue.Dequeue();
                    for (int e = head[u]; e != -1; e = edgeNext[e])
                    {
                        int v = edgeTo[e];
                        if (parentEdge[v] != -1 || edgeCap[e] <= 1e-9) continue;
                        parentEdge[v] = e;
                        queue.Enqueue(v);
                    }
                }

                if (parentEdge[superSink] == -1) break;

                // 找增广路上的瓶颈。
                double bottleneck = double.MaxValue;
                for (int v = superSink; v != superSource;)
                {
                    int e = parentEdge[v];
                    bottleneck = Math.Min(bottleneck, edgeCap[e]);
                    v = edgeTo[e ^ 1];
                }

                for (int v = superSink; v != superSource;)
                {
                    int e = parentEdge[v];
                    edgeCap[e] -= bottleneck;
                    edgeCap[e ^ 1] += bottleneck;
                    v = edgeTo[e ^ 1];
                }

                maxFlow += bottleneck;
                if (maxFlow > 1e6) break;   // 防御性上限，正常情况下不会触发
            }

            CurrentThroughput = maxFlow;

            // 从反向边的残量反推每条边的实际流量，累加到两端格子上。
            for (int u = 0; u < n; u++)
            {
                for (int e = head[u]; e != -1; e = edgeNext[e])
                {
                    if ((e & 1) != 0) continue;              // 只看正向边
                    int v = edgeTo[e];
                    double flow = edgeCap[e ^ 1];            // 反向边的残量就是正向流量
                    if (flow <= 1e-9) continue;

                    if (u < n) _load[u] += flow;
                    if (v < n) _load[v] += flow;
                }
            }

            // 每格被算了进和出两次，取一半。
            for (int i = 0; i < n; i++)
            {
                _load[i] *= 0.5;
                var spec = ComponentLibrary.Of(_cells[i]);
                if (spec.Throughput > 0.0) _load[i] = Math.Min(_load[i], spec.Throughput);
            }
        }

        static readonly (int dx, int dy)[] Neighbours = { (1, 0), (-1, 0), (0, 1), (0, -1) };

        /// <summary>
        /// 热扩散。显式差分，每格和四邻交换热量，同时向衬底漏掉一点。
        /// 散热片的导热系数是普通元件的六倍多，它就是靠这个把热量从热点搬走的。
        /// </summary>
        void DiffuseHeat(double dt)
        {
            int n = Width * Height;
            Array.Copy(_heat, _scratch, n);

            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                int i = Index(x, y);
                var kind = _cells[i];
                var spec = ComponentLibrary.Of(kind);

                double exchange = 0.0;
                foreach (var (dx, dy) in Neighbours)
                {
                    int nx = x + dx, ny = y + dy;
                    if (!InBounds(nx, ny)) continue;

                    int j = Index(nx, ny);
                    // 两格之间的导热取较小值：一块散热片贴着坏块也传不出去热量。
                    double k = Math.Min(spec.Conductivity, ComponentLibrary.Of(_cells[j]).Conductivity);
                    exchange += k * (_scratch[j] - _scratch[i]);
                }

                double generated = 0.0;
                if (!_burned[i] && spec.Throughput > 0.0)
                {
                    double utilization = Math.Min(1.0, _load[i] / spec.Throughput);
                    generated = spec.HeatPerLoad * utilization * 60.0 + spec.IdleHeat * 12.0;
                }

                double loss = SubstrateLoss * (_scratch[i] - AmbientTemperature) * 12.0;

                _heat[i] = _scratch[i] + dt * (DiffusionRate * exchange + generated - loss);
                if (_heat[i] < AmbientTemperature) _heat[i] = AmbientTemperature;
            }
        }

        void BurnOverheated()
        {
            for (int i = 0; i < _cells.Length; i++)
            {
                if (_burned[i]) continue;
                var spec = ComponentLibrary.Of(_cells[i]);
                if (spec.MeltingPoint <= 0.0 || _heat[i] < spec.MeltingPoint) continue;
                if (_cells[i] == ComponentKind.Empty || _cells[i] == ComponentKind.DeadCell) continue;

                _burned[i] = true;
                _cells[i] = ComponentKind.DeadCell;
                _load[i] = 0.0;
                BurnedCount++;

                // 烧毁会改变拓扑，下一步必须重算流量，不能等到定时器到点。
                _flowTimer = FlowRecalcInterval;
            }
        }

        void UpdateProgress(double dt)
        {
            if (CurrentThroughput >= TargetThroughput)
            {
                HoldProgress += dt;
            }
            else
            {
                // 达标中断会退掉一部分进度，但不清零。
                // 全清零会让玩家在临门一脚被一次小波动打回原点，那不是有难度，那是折磨。
                HoldProgress = Math.Max(0.0, HoldProgress - dt * 1.6);
            }

            // 所有数据源都烧了，或者再也没有通路，这一局就结束了。
            if (CurrentThroughput <= 1e-9 && _elapsed > 2.0 && !HasIntactSource())
                Failed = true;
        }

        bool HasIntactSource()
        {
            for (int i = 0; i < _cells.Length; i++)
                if (_cells[i] == ComponentKind.Source && !_burned[i]) return true;
            return false;
        }

        // ------------------------------------------------------------------ 查询

        /// <summary>全层最高温度，界面上用来做全局告警。</summary>
        public double PeakTemperature()
        {
            double peak = AmbientTemperature;
            for (int i = 0; i < _heat.Length; i++) if (_heat[i] > peak) peak = _heat[i];
            return peak;
        }

        /// <summary>距离熔点最近的那个格子的应力比例，1 表示下一刻就要烧。</summary>
        public double PeakThermalStress()
        {
            double peak = 0.0;
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                double s = ThermalStressAt(x, y);
                if (s > peak) peak = s;
            }
            return peak;
        }

        static double Clamp01(double v) => v < 0.0 ? 0.0 : (v > 1.0 ? 1.0 : v);
    }
}
