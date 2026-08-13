using System;
using System.Collections.Generic;
using Undertown.Core.World;

namespace Undertown.Core.Sim
{
    /// <summary>
    /// Cells the player has marked for excavation, and how much work each has absorbed.
    /// Digging is the real cost of the illicit side of the town: the chambers themselves are
    /// cheap, but every cell of earth takes a worker off other duties and produces a load of
    /// spoil that has to go somewhere an inspector will not see it.
    /// </summary>
    [Serializable]
    public sealed class DigOrders
    {
        private readonly Dictionary<Coord, int> _progress = new Dictionary<Coord, int>();
        private readonly List<Coord> _order = new List<Coord>();

        public int Count => _order.Count;
        public IReadOnlyList<Coord> Pending => _order;

        public bool IsOrdered(Coord cell) => _progress.ContainsKey(cell);

        public int ProgressAt(Coord cell) => _progress.TryGetValue(cell, out int done) ? done : 0;

        public bool Order(GridMap map, Coord cell)
        {
            if (cell.IsSurface) return false;
            if (!map.InBounds(cell)) return false;
            if (!Tiles.IsDiggable(map.Get(cell))) return false;
            if (_progress.ContainsKey(cell)) return false;

            _progress[cell] = 0;
            _order.Add(cell);
            return true;
        }

        public bool Cancel(Coord cell)
        {
            if (!_progress.Remove(cell)) return false;
            _order.Remove(cell);
            return true;
        }

        public void Clear()
        {
            _progress.Clear();
            _order.Clear();
        }

        /// <summary>
        /// Adds work to a cell. Returns true when the cell is finished and should be carved
        /// out; the caller owns the map edit and the spoil it produces.
        /// </summary>
        public bool AddWork(GridMap map, Coord cell, int ticks)
        {
            if (!_progress.TryGetValue(cell, out int done)) return false;

            done += ticks;
            int cost = Tiles.DigCost(map.Get(cell));
            if (done < cost)
            {
                _progress[cell] = done;
                return false;
            }

            _progress.Remove(cell);
            _order.Remove(cell);
            return true;
        }

        /// <summary>
        /// The pending cell nearest the worker on the same layer. Ties break towards the
        /// order the player issued them, so a queue of digs is worked through predictably
        /// rather than in whatever order a dictionary happens to enumerate.
        /// </summary>
        public bool TryFindNearest(Coord from, out Coord nearest)
        {
            nearest = default;
            int best = int.MaxValue;

            for (int i = 0; i < _order.Count; i++)
            {
                var candidate = _order[i];
                if (candidate.Depth != from.Depth) continue;

                int distance = candidate.ManhattanTo(from);
                if (distance < 0 || distance >= best) continue;

                best = distance;
                nearest = candidate;
            }

            return best != int.MaxValue;
        }
    }
}
