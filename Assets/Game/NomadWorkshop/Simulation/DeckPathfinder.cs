using System;
using System.Collections.Generic;

namespace Game.NomadWorkshop.Simulation
{
    /// <summary>
    /// 一条不可变的甲板格路径。首格是查询起点，末格是目标工作位；移动表现可以在格中心之间平滑插值。
    /// </summary>
    public sealed class DeckPath
    {
        private readonly DeckCell[] _cells;

        internal DeckPath(List<DeckCell> cells)
        {
            _cells = cells?.ToArray() ?? throw new ArgumentNullException(nameof(cells));
            if (_cells.Length == 0)
                throw new ArgumentException("路径至少需要一个格子。", nameof(cells));
        }

        public int Count => _cells.Length;
        public DeckCell this[int index] => _cells[index];
        public IReadOnlyList<DeckCell> Cells => _cells;
    }

    /// <summary>
    /// 小规模单层甲板的确定性四方向 A*。它只读取摆放网格，不依赖 Unity、Transform 或物理系统，
    /// 因而可以用于居民移动、建造预览可达性检查、存档回放和无场景测试。
    /// </summary>
    public static class DeckPathfinder
    {
        private static readonly DeckCell[] NeighborOffsets =
        {
            new(0, 1),
            new(1, 0),
            new(0, -1),
            new(-1, 0),
        };

        public static bool TryFindPath(
            FacilityPlacementGrid grid,
            DeckCell start,
            DeckCell goal,
            out DeckPath path) =>
            TryFindPath(grid, start, goal, null, out path);

        /// <summary>
        /// 查询路径但不修改网格。<paramref name="additionalBlockedCells"/> 用于在真正提交建造前，
        /// 把建造幽灵的占格当作临时障碍验证通路。
        /// </summary>
        public static bool TryFindPath(
            FacilityPlacementGrid grid,
            DeckCell start,
            DeckCell goal,
            IReadOnlyCollection<DeckCell> additionalBlockedCells,
            out DeckPath path)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));

            HashSet<DeckCell> additionalBlocked = additionalBlockedCells == null ||
                                                  additionalBlockedCells.Count == 0
                ? null
                : new HashSet<DeckCell>(additionalBlockedCells);
            if (!IsWalkable(grid, start, additionalBlocked) ||
                !IsWalkable(grid, goal, additionalBlocked))
            {
                path = null;
                return false;
            }

            if (start == goal)
            {
                path = new DeckPath(new List<DeckCell> { start });
                return true;
            }

            var open = new List<DeckCell> { start };
            var closed = new HashSet<DeckCell>();
            var cameFrom = new Dictionary<DeckCell, DeckCell>();
            var distanceFromStart = new Dictionary<DeckCell, int>
            {
                [start] = 0,
            };

            while (open.Count > 0)
            {
                int bestIndex = FindBestOpenIndex(open, distanceFromStart, goal);
                DeckCell current = open[bestIndex];
                open.RemoveAt(bestIndex);

                if (current == goal)
                {
                    path = Reconstruct(cameFrom, current);
                    return true;
                }

                closed.Add(current);
                int nextDistance = distanceFromStart[current] + 1;
                for (var i = 0; i < NeighborOffsets.Length; i++)
                {
                    DeckCell neighbor = current.Offset(NeighborOffsets[i]);
                    if (closed.Contains(neighbor) ||
                        !IsWalkable(grid, neighbor, additionalBlocked))
                        continue;

                    if (distanceFromStart.TryGetValue(neighbor, out int knownDistance) &&
                        nextDistance >= knownDistance)
                        continue;

                    cameFrom[neighbor] = current;
                    distanceFromStart[neighbor] = nextDistance;
                    if (!open.Contains(neighbor)) open.Add(neighbor);
                }
            }

            path = null;
            return false;
        }

        private static int FindBestOpenIndex(
            IReadOnlyList<DeckCell> open,
            IReadOnlyDictionary<DeckCell, int> distanceFromStart,
            DeckCell goal)
        {
            var bestIndex = 0;
            var bestTotal = int.MaxValue;
            var bestRemaining = int.MaxValue;
            for (var i = 0; i < open.Count; i++)
            {
                DeckCell candidate = open[i];
                int remaining = Manhattan(candidate, goal);
                int total = distanceFromStart[candidate] + remaining;
                if (total > bestTotal || total == bestTotal && remaining >= bestRemaining)
                    continue;

                bestIndex = i;
                bestTotal = total;
                bestRemaining = remaining;
            }
            return bestIndex;
        }

        private static DeckPath Reconstruct(
            IReadOnlyDictionary<DeckCell, DeckCell> cameFrom,
            DeckCell current)
        {
            var reversed = new List<DeckCell> { current };
            while (cameFrom.TryGetValue(current, out DeckCell previous))
            {
                current = previous;
                reversed.Add(current);
            }
            reversed.Reverse();
            return new DeckPath(reversed);
        }

        private static bool IsWalkable(
            FacilityPlacementGrid grid,
            DeckCell cell,
            HashSet<DeckCell> additionalBlocked) =>
            grid.Contains(cell) &&
            !grid.IsOccupied(cell) &&
            (additionalBlocked == null || !additionalBlocked.Contains(cell));

        private static int Manhattan(DeckCell left, DeckCell right) =>
            Math.Abs(left.X - right.X) + Math.Abs(left.Z - right.Z);
    }
}
