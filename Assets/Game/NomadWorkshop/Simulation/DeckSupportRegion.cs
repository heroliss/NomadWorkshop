using System;
using System.Collections.Generic;

namespace Game.NomadWorkshop.Simulation
{
    /// <summary>
    /// 单层楼板的不可变支撑区域：整数毫米板片并集减去开口。构建时按边坐标分解，
    /// 对外只提供互不重叠的支撑面；它是结构的派生几何，不拥有结构 id、材料或存档。
    /// 设施必须被完整覆盖，跨板缝合法，凹口与孔洞内部不提供支撑。
    /// </summary>
    public sealed class DeckSupportRegion
    {
        private const double Tolerance = 0.0001d;
        private readonly PlanarOrientedRectangle[] _voids;

        /// <summary>将旧矩形布局转为同一支撑规则；默认/退化边界会被拒绝。</summary>
        public DeckSupportRegion(DeckBounds bounds) : this(new[] { bounds }) { }

        /// <summary>
        /// 复制并规范化同层板片/开口；输入顺序及重复板不改变覆盖。开口可伸出板外，但不能扣掉全部支撑。
        /// 构建成本随不同 X/Z 边坐标的乘积增长，适用于模块板片；不要在逐帧指针查询中重建。
        /// </summary>
        public DeckSupportRegion(IReadOnlyList<DeckBounds> plates, IReadOnlyList<DeckBounds> openings = null)
        {
            if (plates == null) throw new ArgumentNullException(nameof(plates));
            if (plates.Count == 0) throw new ArgumentException("至少需要一块楼板。", nameof(plates));
            var level = plates[0].DeckLevel;
            var xs = new SortedSet<int>();
            var zs = new SortedSet<int>();
            var minX = int.MaxValue; var minZ = int.MaxValue;
            var maxX = int.MinValue; var maxZ = int.MinValue;
            for (var i = 0; i < plates.Count; i++)
            {
                DeckBounds p = plates[i];
                Validate(p, level);
                minX = Math.Min(minX, p.MinXMillimeters); minZ = Math.Min(minZ, p.MinZMillimeters);
                maxX = Math.Max(maxX, p.MaxXMillimeters); maxZ = Math.Max(maxZ, p.MaxZMillimeters);
                xs.Add(p.MinXMillimeters); xs.Add(p.MaxXMillimeters);
                zs.Add(p.MinZMillimeters); zs.Add(p.MaxZMillimeters);
            }
            Bounds = new DeckBounds(minX, minZ, maxX, maxZ, level);
            if (openings != null)
                for (var i = 0; i < openings.Count; i++)
                {
                    DeckBounds hole = openings[i]; Validate(hole, level);
                    xs.Add(Math.Clamp(hole.MinXMillimeters, minX, maxX));
                    xs.Add(Math.Clamp(hole.MaxXMillimeters, minX, maxX));
                    zs.Add(Math.Clamp(hole.MinZMillimeters, minZ, maxZ));
                    zs.Add(Math.Clamp(hole.MaxZMillimeters, minZ, maxZ));
                }
            var x = new int[xs.Count]; xs.CopyTo(x);
            var z = new int[zs.Count]; zs.CopyTo(z);
            var supported = new bool[x.Length - 1, z.Length - 1];
            for (var iz = 0; iz < z.Length - 1; iz++)
                for (var ix = 0; ix < x.Length - 1; ix++)
                {
                    double cx = ((double)x[ix] + x[ix + 1]) * 0.5d;
                    double cz = ((double)z[iz] + z[iz + 1]) * 0.5d;
                    supported[ix, iz] = ContainsAny(plates, cx, cz) && !ContainsAny(openings, cx, cz);
                }

            List<DeckBounds> surfaces = Merge(supported, true, x, z, level);
            if (surfaces.Count == 0) throw new ArgumentException("开口扣除了全部楼板支撑。", nameof(openings));
            Surfaces = surfaces.AsReadOnly();
            List<DeckBounds> gaps = Merge(supported, false, x, z, level);
            _voids = new PlanarOrientedRectangle[gaps.Count];
            for (var i = 0; i < gaps.Count; i++)
            {
                DeckBounds gap = gaps[i];
                _voids[i] = new PlanarOrientedRectangle(
                    ((double)gap.MinXMillimeters + gap.MaxXMillimeters) * 0.5d,
                    ((double)gap.MinZMillimeters + gap.MaxZMillimeters) * 0.5d,
                    ((double)gap.MaxXMillimeters - gap.MinXMillimeters) * 0.5d,
                    ((double)gap.MaxZMillimeters - gap.MinZMillimeters) * 0.5d, 0d);
            }
        }

        /// <summary>板片输入的外包围盒，仅用于取景/粗筛，不能当作有效楼板。</summary>
        public DeckBounds Bounds { get; }
        /// <summary>确定顺序、互不重叠的物理支撑面，可供渲染/碰撞派生；不包含稳定结构身份。</summary>
        public IReadOnlyList<DeckBounds> Surfaces { get; }

        /// <summary>检查同层点及其轴对齐方形净空；padding 是毫米半宽，负数拒绝。</summary>
        public bool Contains(in DeckPose pose, int paddingMillimeters = 0)
        {
            if (paddingMillimeters < 0) throw new ArgumentOutOfRangeException(nameof(paddingMillimeters));
            if (pose.DeckLevel != Bounds.DeckLevel) return false;
            if (paddingMillimeters == 0) return ContainsAny(Surfaces, pose.XMillimeters, pose.ZMillimeters);
            return Covers(new PlanarOrientedRectangle(
                pose.XMillimeters, pose.ZMillimeters, paddingMillimeters, paddingMillimeters, 0d));
        }

        /// <summary>
        /// 检查同层短直线移动的完整支撑。正净空采用扫掠方形的外包围矩形，允许保守拒绝凹角；
        /// 零净空精确检查线段覆盖。端点有地板不代表途中可走，不能用于跨层连接。
        /// </summary>
        public bool CoversSweep(in DeckPose start, in DeckPose end, int paddingMillimeters = 0)
        {
            if (paddingMillimeters < 0) throw new ArgumentOutOfRangeException(nameof(paddingMillimeters));
            return start.DeckLevel == Bounds.DeckLevel && end.DeckLevel == Bounds.DeckLevel &&
                   CoversSweep(start.XMillimeters, start.ZMillimeters, end.XMillimeters, end.ZMillimeters, paddingMillimeters);
        }

        /// <summary>
        /// 检查同层圆形身体投影的完整支撑，允许与缺口相切。用于恢复实际站姿；
        /// 与工作位方形预留空间不同，圆形可以合法站在凹角的对角外侧。
        /// </summary>
        public bool ContainsDisc(in DeckPose pose, int radiusMillimeters)
        {
            if (radiusMillimeters < 0) throw new ArgumentOutOfRangeException(nameof(radiusMillimeters));
            if (radiusMillimeters == 0) return Contains(pose);
            if (pose.DeckLevel != Bounds.DeckLevel ||
                !Bounds.Contains((double)pose.XMillimeters - radiusMillimeters, (double)pose.ZMillimeters - radiusMillimeters, Tolerance) ||
                !Bounds.Contains((double)pose.XMillimeters + radiusMillimeters, (double)pose.ZMillimeters + radiusMillimeters, Tolerance)) return false;
            foreach (var gap in _voids)
            {
                // 支撑分解产生的缺口均轴对齐；最近点距离精确判断圆是否进入空洞。
                double dx = Math.Max(0d, Math.Abs(pose.XMillimeters - gap.CenterX) - gap.HalfWidth);
                double dz = Math.Max(0d, Math.Abs(pose.ZMillimeters - gap.CenterZ) - gap.HalfDepth);
                if (dx * dx + dz * dz < (double)radiusMillimeters * radiusMillimeters - Tolerance) return false;
            }
            return true;
        }

        /// <summary>完整检查每块旋转占地；null 拒绝，其他楼层返回 false。</summary>
        public bool Covers(in DeckPose pose, ContinuousFacilityFootprint footprint)
        {
            if (footprint == null) throw new ArgumentNullException(nameof(footprint));
            if (pose.DeckLevel != Bounds.DeckLevel) return false;
            foreach (var rectangle in PlanarOrientedRectangleGeometry.BuildRectangles(pose, footprint))
                if (!Covers(rectangle)) return false;
            return true;
        }

        internal bool Covers(in PlanarOrientedRectangle rectangle)
        {
            double ex = rectangle.ProjectedRadius(1d, 0d);
            double ez = rectangle.ProjectedRadius(0d, 1d);
            if (!Bounds.Contains(rectangle.CenterX - ex, rectangle.CenterZ - ez, Tolerance) ||
                !Bounds.Contains(rectangle.CenterX + ex, rectangle.CenterZ + ez, Tolerance)) return false;
            for (var i = 0; i < _voids.Length; i++)
                if (PlanarOrientedRectangleGeometry.Overlaps(rectangle, _voids[i])) return false;
            return true;
        }

        // 预览的短连边采用扫掠方形的外包围矩形，允许保守拒绝凹角，绝不能跳过窄孔洞。
        internal bool CoversSweep(double x0, double z0, double x1, double z1, int padding)
        {
            if (padding == 0) return CoversSegment(x0, z0, x1, z1);
            return Covers(new PlanarOrientedRectangle((x0 + x1) * 0.5d, (z0 + z1) * 0.5d,
                Math.Abs(x1 - x0) * 0.5d + padding, Math.Abs(z1 - z0) * 0.5d + padding, 0d));
        }

        // 零面积的线段不能用“未与缺口内部相交”代替覆盖：开口到外边缘时，沿该边仍可能跨空。
        // 逐步合并各支撑面在线段参数上的闭区间；只要存在正长度空隙，就不能推进到终点。
        private bool CoversSegment(double x0, double z0, double x1, double z1)
        {
            double reached = 0d;
            for (var pass = 0; pass <= Surfaces.Count; pass++)
            {
                double next = reached;
                for (var i = 0; i < Surfaces.Count; i++)
                {
                    DeckBounds p = Surfaces[i];
                    double start = 0d, end = 1d;
                    if (!ClipInterval(x0, x1 - x0, p.MinXMillimeters, p.MaxXMillimeters, ref start, ref end) ||
                        !ClipInterval(z0, z1 - z0, p.MinZMillimeters, p.MaxZMillimeters, ref start, ref end) ||
                        start > reached) continue;
                    if (end >= 1d) return true;
                    next = Math.Max(next, end);
                }
                if (next <= reached) return false;
                reached = next;
            }
            return false;
        }

        private static bool ClipInterval(double origin, double delta, int minimum, int maximum, ref double start, ref double end)
        {
            if (delta == 0d) return origin >= minimum && origin <= maximum;
            double a = (minimum - origin) / delta;
            double b = (maximum - origin) / delta;
            start = Math.Max(start, Math.Min(a, b));
            end = Math.Min(end, Math.Max(a, b));
            return start <= end;
        }

        private static void Validate(in DeckBounds p, int level)
        {
            if (p.DeckLevel != level || p.MaxXMillimeters <= p.MinXMillimeters || p.MaxZMillimeters <= p.MinZMillimeters)
                throw new ArgumentException("楼板与开口必须是同层、非退化的毫米矩形。");
        }

        private static bool ContainsAny(IReadOnlyList<DeckBounds> parts, double x, double z)
        {
            if (parts == null) return false;
            for (var i = 0; i < parts.Count; i++)
                if (parts[i].Contains(x, z, 0d)) return true;
            return false;
        }

        private static List<DeckBounds> Merge(bool[,] cells, bool value, int[] xs, int[] zs, int level)
        {
            var result = new List<DeckBounds>();
            var previous = new Dictionary<(int, int), int>();
            for (var z = 0; z < zs.Length - 1; z++)
            {
                var current = new Dictionary<(int, int), int>();
                for (var x = 0; x < xs.Length - 1;)
                {
                    if (cells[x, z] != value) { x++; continue; }
                    int start = x++;
                    while (x < xs.Length - 1 && cells[x, z] == value) x++;
                    var key = (xs[start], xs[x]);
                    if (previous.TryGetValue(key, out int index))
                    {
                        DeckBounds old = result[index];
                        result[index] = new DeckBounds(old.MinXMillimeters, old.MinZMillimeters,
                            old.MaxXMillimeters, zs[z + 1], level);
                    }
                    else
                    {
                        index = result.Count;
                        result.Add(new DeckBounds(xs[start], zs[z], xs[x], zs[z + 1], level));
                    }
                    current.Add(key, index);
                }
                previous = current;
            }
            return result;
        }
    }
}
