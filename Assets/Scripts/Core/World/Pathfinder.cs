using System;
using System.Collections.Generic;

namespace Worker.Core
{
    /// <summary>
    /// 4-directional A* over <see cref="TileMap"/>. Buffers are allocated once and
    /// reused, so a steady-state tick performs no heap allocation. Not thread-safe:
    /// the simulation is single-threaded by design.
    /// </summary>
    public sealed class Pathfinder
    {
        private readonly TileMap _map;
        private readonly int[] _cameFrom;
        private readonly int[] _gScore;
        private readonly int[] _visitStamp;
        private readonly BinaryHeap _open;
        private int _stamp;

        /// <summary>Nodes expanded by the most recent search. Exposed for the self-test metrics.</summary>
        public int LastExpandedNodes { get; private set; }

        public Pathfinder(TileMap map)
        {
            _map = map ?? throw new ArgumentNullException(nameof(map));
            int count = map.TileCount;
            _cameFrom = new int[count];
            _gScore = new int[count];
            _visitStamp = new int[count];
            _open = new BinaryHeap(count);
        }

        /// <summary>
        /// Finds a walkable path from <paramref name="start"/> to <paramref name="goal"/>.
        /// The result excludes the start tile and includes the goal. Returns false when
        /// no route exists; <paramref name="result"/> is cleared either way.
        /// </summary>
        public bool TryFindPath(GridPos start, GridPos goal, List<GridPos> result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            result.Clear();
            LastExpandedNodes = 0;

            if (!_map.InBounds(start) || !_map.InBounds(goal)) return false;
            if (!_map.IsWalkable(goal)) return false;
            if (start == goal) return true;

            _stamp++;
            _open.Clear();

            int startIndex = _map.IndexOf(start);
            int goalIndex = _map.IndexOf(goal);

            _gScore[startIndex] = 0;
            _cameFrom[startIndex] = -1;
            _visitStamp[startIndex] = _stamp;
            _open.Push(startIndex, start.ManhattanTo(goal));

            while (_open.Count > 0)
            {
                int current = _open.Pop();
                if (current == goalIndex)
                {
                    Reconstruct(current, startIndex, result);
                    return true;
                }

                LastExpandedNodes++;
                var currentPos = _map.PosOf(current);
                int currentG = _gScore[current];

                for (int d = 0; d < 4; d++)
                {
                    var neighbourPos = currentPos.Step((Direction)d);
                    if (!_map.IsWalkable(neighbourPos)) continue;

                    int neighbour = _map.IndexOf(neighbourPos);
                    int tentativeG = currentG + 1;

                    if (_visitStamp[neighbour] == _stamp && _gScore[neighbour] <= tentativeG) continue;

                    _visitStamp[neighbour] = _stamp;
                    _gScore[neighbour] = tentativeG;
                    _cameFrom[neighbour] = current;
                    _open.Push(neighbour, tentativeG + neighbourPos.ManhattanTo(goal));
                }
            }

            return false;
        }

        /// <summary>
        /// Finds a path to whichever of <paramref name="goals"/> is cheapest to reach.
        /// Used for "walk up to this building" where any adjacent tile will do.
        /// </summary>
        public bool TryFindPathToAny(GridPos start, IReadOnlyList<GridPos> goals, List<GridPos> result)
        {
            result.Clear();
            if (goals == null || goals.Count == 0) return false;

            // Cheap pre-pass: if we are already standing on a goal there is nothing to do.
            for (int i = 0; i < goals.Count; i++)
            {
                if (goals[i] == start) return true;
            }

            bool found = false;
            int bestLength = int.MaxValue;
            var scratch = new List<GridPos>();

            for (int i = 0; i < goals.Count; i++)
            {
                if (!_map.IsWalkable(goals[i])) continue;
                // Manhattan distance lower-bounds path length, so a goal that cannot beat
                // the incumbent is not worth a full search.
                if (start.ManhattanTo(goals[i]) >= bestLength) continue;
                if (!TryFindPath(start, goals[i], scratch)) continue;
                if (scratch.Count >= bestLength) continue;

                bestLength = scratch.Count;
                result.Clear();
                result.AddRange(scratch);
                found = true;
            }

            return found;
        }

        private void Reconstruct(int goalIndex, int startIndex, List<GridPos> result)
        {
            int cursor = goalIndex;
            while (cursor != startIndex && cursor >= 0)
            {
                result.Add(_map.PosOf(cursor));
                cursor = _cameFrom[cursor];
            }
            result.Reverse();
        }

        /// <summary>Min-heap keyed on f-score, storing tile indices.</summary>
        private sealed class BinaryHeap
        {
            private readonly int[] _items;
            private readonly int[] _priorities;
            private int _count;

            public BinaryHeap(int capacity)
            {
                _items = new int[capacity + 1];
                _priorities = new int[capacity + 1];
            }

            public int Count => _count;

            public void Clear() => _count = 0;

            public void Push(int item, int priority)
            {
                if (_count >= _items.Length) return;

                int i = _count++;
                _items[i] = item;
                _priorities[i] = priority;

                while (i > 0)
                {
                    int parent = (i - 1) >> 1;
                    if (_priorities[parent] <= _priorities[i]) break;
                    Swap(parent, i);
                    i = parent;
                }
            }

            public int Pop()
            {
                int top = _items[0];
                _count--;
                if (_count > 0)
                {
                    _items[0] = _items[_count];
                    _priorities[0] = _priorities[_count];
                    int i = 0;
                    while (true)
                    {
                        int left = (i << 1) + 1;
                        if (left >= _count) break;
                        int right = left + 1;
                        int smallest = (right < _count && _priorities[right] < _priorities[left]) ? right : left;
                        if (_priorities[i] <= _priorities[smallest]) break;
                        Swap(i, smallest);
                        i = smallest;
                    }
                }
                return top;
            }

            private void Swap(int a, int b)
            {
                (_items[a], _items[b]) = (_items[b], _items[a]);
                (_priorities[a], _priorities[b]) = (_priorities[b], _priorities[a]);
            }
        }
    }
}
