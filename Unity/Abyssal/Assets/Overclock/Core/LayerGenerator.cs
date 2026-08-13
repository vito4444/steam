using System;

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
            int sinkCount = 1 + (int)Math.Round(difficulty * 1.4);

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

            layer.Budget = (int)Math.Round(46 - difficulty * 12);
            layer.TargetThroughput = 12.0 + difficulty * 16.0;
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
            var cameFrom = new int[w * h];
            for (int i = 0; i < cameFrom.Length; i++) cameFrom[i] = -1;

            var queue = new System.Collections.Generic.Queue<int>();

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (layer.CellAt(x, y) != ComponentKind.Source) continue;
                int i = layer.Index(x, y);
                cameFrom[i] = i;
                queue.Enqueue(i);
            }

            var steps = new[] { (1, 0), (0, 1), (0, -1), (-1, 0) };
            int found = -1;

            while (queue.Count > 0 && found < 0)
            {
                int cur = queue.Dequeue();
                int cx = cur % w, cy = cur / w;

                foreach (var (dx, dy) in steps)
                {
                    int nx = cx + dx, ny = cy + dy;
                    if (!layer.InBounds(nx, ny)) continue;

                    int ni = layer.Index(nx, ny);
                    if (cameFrom[ni] != -1) continue;

                    var kindHere = layer.CellAt(nx, ny);
                    if (kindHere == ComponentKind.DeadCell || layer.IsBurned(nx, ny)) continue;

                    cameFrom[ni] = cur;
                    if (kindHere == ComponentKind.Sink) { found = ni; break; }
                    queue.Enqueue(ni);
                }
            }

            if (found < 0) return false;

            // 回溯路径，只在空格子上铺线，跳过两端的源和汇。
            for (int node = found; cameFrom[node] != node; node = cameFrom[node])
            {
                int x = node % w, y = node / w;
                if (layer.CellAt(x, y) == ComponentKind.Empty)
                    layer.Place(x, y, kind);
            }

            return true;
        }
    }
}
