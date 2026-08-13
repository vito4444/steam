using System;
using System.Collections.Generic;

namespace Overclock.Core
{
    /// <summary>
    /// 硅层的程序化生成。
    ///
    /// 每一层的地形要同时满足两件事：一定存在可行解，
    /// 但那个解不能是「一条直线连过去」。做法是先在左右两侧摆好数据源和输出端口，
    /// 再往中间撒坏块，最后验证连通性——撒到把路完全堵死就退回去。
    /// </summary>
    public static class LayerGenerator
    {
        /// <summary>
        /// 生成一层。<paramref name="difficulty"/> 从 0 到 1，
        /// 越高坏块越多、预算越紧、目标吞吐越高。
        /// </summary>
        public static SiliconLayer Generate(int seed, int width, int height, double difficulty)
        {
            difficulty = Math.Max(0.0, Math.Min(1.0, difficulty));
            var rng = new Random(seed);
            var layer = new SiliconLayer(width, height);

            int sourceCount = 2 + (int)Math.Round(difficulty * 2);
            // 输出端口至少两个。只有一个汇时，所有通路最后都要挤进同一个格子的
            // 三四条邻边里，最大流被死死卡住——玩家铺得再多也涨不上去，
            // 那不是难度，那是设计上的死结。
            int sinkCount = 2 + (int)Math.Round(difficulty * 1.5);

            PlaceColumn(layer, rng, 0, sourceCount, ComponentKind.Source);
            PlaceColumn(layer, rng, width - 1, sinkCount, ComponentKind.Sink);

            // 坏块。中间三列留白多一些，保证总有绕过去的余地。
            int deadTarget = (int)(width * height * (0.06 + 0.14 * difficulty));
            int placed = 0, attempts = 0;

            while (placed < deadTarget && attempts < deadTarget * 12)
            {
                attempts++;
                int x = 1 + rng.Next(width - 2);
                int y = rng.Next(height);
                if (layer.CellAt(x, y) != ComponentKind.Empty) continue;

                layer.SetTerrain(x, y, ComponentKind.DeadCell);
                if (HasPath(layer))
                {
                    placed++;
                }
                else
                {
                    // 这一块把路堵死了，退回去。
                    layer.SetTerrain(x, y, ComponentKind.Empty);
                }
            }

            // 预算必须够铺通至少一条完整通路，否则玩家（和自动玩家）铺出来的
            // 全是断口，一点流量都没有。16 格宽的层用总线铺满要 90 点上下，
            // 这个基数就是按「一条总线，或者两三条导线加散热」定的。
            layer.Budget = (int)Math.Round(96 - difficulty * 22);
            // 目标吞吐的增长斜率直接决定通关率。原来的 16 点跨度让第八层要到 28，
            // 而一个会玩的人打到后期大约能稳定做到 25 上下，于是几乎必卡最后两层。
            // 扫描数据把斜率定在这里：合格打法有明确赢面，但仍要打满全程不出错。
            layer.TargetThroughput = 11.0 + difficulty * 7.5;
            layer.RequiredHoldTime = 5.0 + difficulty * 4.0;
            return layer;
        }

        static void PlaceColumn(SiliconLayer layer, Random rng, int x, int count, ComponentKind kind)
        {
            int height = layer.Height;
            var used = new bool[height];

            for (int i = 0; i < count; i++)
            {
                // 均匀分散再加一点抖动，避免全挤在中间。
                int target = (int)((i + 0.5) / count * height);
                int y = Math.Max(0, Math.Min(height - 1, target + rng.Next(-1, 2)));

                for (int probe = 0; probe < height && used[y]; probe++)
                    y = (y + 1) % height;

                used[y] = true;
                layer.SetTerrain(x, y, kind);
            }
        }

        /// <summary>这个格子四周有没有已经在导数据的元件。</summary>
        static bool IsConnected(SiliconLayer layer, int x, int y)
        {
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = x + dx, ny = y + dy;
                if (!layer.InBounds(nx, ny)) continue;
                if (layer.IsBurned(nx, ny)) continue;

                var kind = layer.CellAt(nx, ny);
                if (kind == ComponentKind.Source || kind == ComponentKind.Sink) continue;
                if (ComponentLibrary.CarriesData(kind)) return true;
            }
            return false;
        }

        /// <summary>
        /// 判断在「所有空格都能铺线」的假设下，是否至少有一条源到汇的通路。
        /// 生成期用它来保证每层都有解。
        /// </summary>
        public static bool HasPath(SiliconLayer layer)
        {
            int w = layer.Width, h = layer.Height;
            var visited = new bool[w * h];
            var stack = new System.Collections.Generic.Stack<(int x, int y)>();

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (layer.CellAt(x, y) != ComponentKind.Source) continue;
                stack.Push((x, y));
                visited[layer.Index(x, y)] = true;
            }

            var steps = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };

            while (stack.Count > 0)
            {
                var (cx, cy) = stack.Pop();
                if (layer.CellAt(cx, cy) == ComponentKind.Sink) return true;

                foreach (var (dx, dy) in steps)
                {
                    int nx = cx + dx, ny = cy + dy;
                    if (!layer.InBounds(nx, ny)) continue;

                    int ni = layer.Index(nx, ny);
                    if (visited[ni]) continue;

                    var kind = layer.CellAt(nx, ny);
                    if (kind == ComponentKind.DeadCell) continue;

                    visited[ni] = true;
                    stack.Push((nx, ny));
                }
            }

            return false;
        }

        /// <summary>
        /// 用最省事的方式把源和汇连起来：沿最短路铺一条单线。
        ///
        /// 不能简单地沿直线铺——地形里有坏块，撞上就断。这里用广度优先搜出
        /// 一条实际可行的最短路，再沿路铺导线。
        ///
        /// 这个解一定能通，但热量会全部堆在那一条线上，撑不了多久。
        /// 它是玩家的起点，也是教学关里演示「为什么需要散热」的反面教材。
        /// 同时它也是自动布线辅助功能的基础。
        /// </summary>
        public static bool LayNaiveRoute(SiliconLayer layer, ComponentKind kind = ComponentKind.Trace)
        {
            int w = layer.Width, h = layer.Height;
            int n = w * h;

            var cameFrom = new int[n];
            var cost = new double[n];
            for (int i = 0; i < n; i++) { cameFrom[i] = -1; cost[i] = double.MaxValue; }

            // 用 Dijkstra 而不是 BFS：走空格子代价低，走已经铺好的格子代价高。
            // 纯 BFS 每次都会返回同一条最短路，第二次调用时那条路上全被占满，
            // 结果是一格都铺不下去——连着调用十次和调用一次没有任何区别。
            // 代价分层之后，后续调用会自动绕开已有线路去开新通道。
            const double freeCost = 1.0;
            const double occupiedCost = 9.0;

            var frontier = new SortedSet<(double cost, int node)>();

            // 起点优先选还没接上任何东西的源。
            // 不区分的话，每次布线都会从同一个源出发（它离汇最近），
            // 其余的源永远闲置，最大流被死死压在单个源的容量上——
            // 玩家铺得再多，吞吐也不会涨。
            bool anyUnconnected = false;
            for (int y = 0; y < h && !anyUnconnected; y++)
            for (int x = 0; x < w && !anyUnconnected; x++)
            {
                if (layer.CellAt(x, y) != ComponentKind.Source) continue;
                if (!IsConnected(layer, x, y)) anyUnconnected = true;
            }

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (layer.CellAt(x, y) != ComponentKind.Source) continue;
                if (anyUnconnected && IsConnected(layer, x, y)) continue;

                int i = layer.Index(x, y);
                cameFrom[i] = i;
                cost[i] = 0.0;
                frontier.Add((0.0, i));
            }

            var steps = new[] { (1, 0), (0, 1), (0, -1), (-1, 0) };
            int found = -1;

            while (frontier.Count > 0 && found < 0)
            {
                var current = frontier.Min;
                frontier.Remove(current);

                int cur = current.node;
                if (current.cost > cost[cur]) continue;

                int cx = cur % w, cy = cur / w;

                foreach (var (dx, dy) in steps)
                {
                    int nx = cx + dx, ny = cy + dy;
                    if (!layer.InBounds(nx, ny)) continue;

                    int ni = layer.Index(nx, ny);
                    var kindHere = layer.CellAt(nx, ny);
                    if (kindHere == ComponentKind.DeadCell || layer.IsBurned(nx, ny)) continue;

                    double step = kindHere == ComponentKind.Empty ? freeCost : occupiedCost;
                    double next = cost[cur] + step;
                    if (next >= cost[ni]) continue;

                    frontier.Remove((cost[ni], ni));
                    cost[ni] = next;
                    cameFrom[ni] = cur;

                    if (kindHere == ComponentKind.Sink) { found = ni; break; }
                    frontier.Add((next, ni));
                }
            }

            if (found < 0) return false;

            // 回溯路径铺线。预算不够就退回最便宜的导线，
            // 宁可铺一条细的也不要留一个断口——断口意味着整条路白铺。
            bool placedAnything = false;
            for (int node = found; cameFrom[node] != node; node = cameFrom[node])
            {
                int x = node % w, y = node / w;
                if (layer.CellAt(x, y) != ComponentKind.Empty) continue;

                if (layer.Place(x, y, kind) || layer.Place(x, y, ComponentKind.Trace))
                    placedAnything = true;
                else
                    return false;   // 连导线都放不起，这一条铺不完
            }

            return placedAnything;
        }
    }
}
