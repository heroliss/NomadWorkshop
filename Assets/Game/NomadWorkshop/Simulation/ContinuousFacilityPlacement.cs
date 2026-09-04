using System;
using System.Collections.Generic;

namespace Game.NomadWorkshop.Simulation
{
    /// <summary>连续设施摆放失败的首要原因；成功时为 <see cref="None"/>。</summary>
    public enum ContinuousPlacementFailure
    {
        None,
        InvalidRequest,
        DuplicateInstanceId,
        DeckLevelUnavailable,
        FootprintOutOfBounds,
        FootprintOverlapsFacility,
    }

    /// <summary>
    /// 一层车辆甲板的轴对齐可建造边界。边界单位是毫米，最大值是可接触的物理边缘而非整数格上界。
    /// </summary>
    public readonly struct DeckBounds
    {
        public readonly int MinXMillimeters;
        public readonly int MinZMillimeters;
        public readonly int MaxXMillimeters;
        public readonly int MaxZMillimeters;
        public readonly int DeckLevel;

        public DeckBounds(
            int minXMillimeters,
            int minZMillimeters,
            int maxXMillimeters,
            int maxZMillimeters,
            int deckLevel = 0)
        {
            if (maxXMillimeters <= minXMillimeters)
                throw new ArgumentOutOfRangeException(nameof(maxXMillimeters));
            if (maxZMillimeters <= minZMillimeters)
                throw new ArgumentOutOfRangeException(nameof(maxZMillimeters));

            MinXMillimeters = minXMillimeters;
            MinZMillimeters = minZMillimeters;
            MaxXMillimeters = maxXMillimeters;
            MaxZMillimeters = maxZMillimeters;
            DeckLevel = deckLevel;
        }

        internal bool Contains(double xMillimeters, double zMillimeters, double tolerance) =>
            xMillimeters >= MinXMillimeters - tolerance &&
            xMillimeters <= MaxXMillimeters + tolerance &&
            zMillimeters >= MinZMillimeters - tolerance &&
            zMillimeters <= MaxZMillimeters + tolerance;
    }

    /// <summary>
    /// 设施占地的一块局部有向矩形。LocalYaw 会与设施姿态的 Yaw 相加；
    /// 多块矩形可表达 L 形设施，彼此在同一设施内部允许重叠。
    /// </summary>
    public readonly struct DeckFootprintPart
    {
        public readonly int LocalCenterXMillimeters;
        public readonly int LocalCenterZMillimeters;
        public readonly int WidthMillimeters;
        public readonly int DepthMillimeters;
        public readonly int LocalYawDeciDegrees;

        public DeckFootprintPart(
            int localCenterXMillimeters,
            int localCenterZMillimeters,
            int widthMillimeters,
            int depthMillimeters,
            int localYawDeciDegrees = 0)
        {
            if (widthMillimeters <= 0)
                throw new ArgumentOutOfRangeException(nameof(widthMillimeters));
            if (depthMillimeters <= 0)
                throw new ArgumentOutOfRangeException(nameof(depthMillimeters));

            LocalCenterXMillimeters = localCenterXMillimeters;
            LocalCenterZMillimeters = localCenterZMillimeters;
            WidthMillimeters = widthMillimeters;
            DepthMillimeters = depthMillimeters;
            LocalYawDeciDegrees = DeckPose.NormalizeYaw(localYawDeciDegrees);
        }
    }

    /// <summary>
    /// 与渲染 Mesh 解耦的简化连续占地。构造时复制部件，外部数组后续变化不会改写玩法定义。
    /// </summary>
    public sealed class ContinuousFacilityFootprint
    {
        private readonly DeckFootprintPart[] _parts;

        public ContinuousFacilityFootprint(IReadOnlyList<DeckFootprintPart> parts)
        {
            if (parts == null) throw new ArgumentNullException(nameof(parts));
            if (parts.Count == 0)
                throw new ArgumentException("连续设施占地至少需要一个矩形部件。", nameof(parts));

            _parts = new DeckFootprintPart[parts.Count];
            for (var i = 0; i < parts.Count; i++) _parts[i] = parts[i];
        }

        public IReadOnlyList<DeckFootprintPart> Parts => _parts;

        /// <summary>判断同层甲板点是否落在任一有向占地部件内；可用于选择、避让与开发期诊断。</summary>
        public bool ContainsPoint(
            in DeckPose facilityPose,
            in DeckPose point,
            int paddingMillimeters = 0)
        {
            if (paddingMillimeters < 0)
                throw new ArgumentOutOfRangeException(nameof(paddingMillimeters));
            if (facilityPose.DeckLevel != point.DeckLevel) return false;

            double poseRadians = facilityPose.YawDegrees * Math.PI / 180d;
            double poseCosine = Math.Cos(poseRadians);
            double poseSine = Math.Sin(poseRadians);
            for (var i = 0; i < _parts.Length; i++)
            {
                DeckFootprintPart part = _parts[i];
                double centerX = facilityPose.XMillimeters +
                    poseCosine * part.LocalCenterXMillimeters +
                    poseSine * part.LocalCenterZMillimeters;
                double centerZ = facilityPose.ZMillimeters -
                    poseSine * part.LocalCenterXMillimeters +
                    poseCosine * part.LocalCenterZMillimeters;
                double yawRadians = (
                    facilityPose.YawDegrees + part.LocalYawDeciDegrees / 10d) *
                    Math.PI / 180d;
                double cosine = Math.Cos(yawRadians);
                double sine = Math.Sin(yawRadians);
                double deltaX = point.XMillimeters - centerX;
                double deltaZ = point.ZMillimeters - centerZ;
                double localX = deltaX * cosine - deltaZ * sine;
                double localZ = deltaX * sine + deltaZ * cosine;
                if (Math.Abs(localX) <= part.WidthMillimeters * 0.5d + paddingMillimeters &&
                    Math.Abs(localZ) <= part.DepthMillimeters * 0.5d + paddingMillimeters)
                    return true;
            }
            return false;
        }
    }

    /// <summary>一次连续设施摆放意图；InstanceId 是存档稳定身份。</summary>
    public readonly struct ContinuousFacilityPlacementRequest
    {
        public readonly string InstanceId;
        public readonly string DefinitionId;
        public readonly DeckPose Pose;
        public readonly ContinuousFacilityFootprint Footprint;

        public ContinuousFacilityPlacementRequest(
            string instanceId,
            string definitionId,
            DeckPose pose,
            ContinuousFacilityFootprint footprint)
        {
            InstanceId = instanceId;
            DefinitionId = definitionId;
            Pose = pose;
            Footprint = footprint;
        }
    }

    /// <summary>
    /// 已提交的连续设施记录。世界 Transform、Collider 和 NavMesh 构建输入都应从这份记录派生。
    /// </summary>
    public sealed class ContinuousPlacedFacility
    {
        internal ContinuousPlacedFacility(in ContinuousFacilityPlacementRequest request)
        {
            InstanceId = request.InstanceId;
            DefinitionId = request.DefinitionId;
            Pose = request.Pose;
            Footprint = request.Footprint;
        }

        public string InstanceId { get; }
        public string DefinitionId { get; }
        public DeckPose Pose { get; }
        public ContinuousFacilityFootprint Footprint { get; }
    }

    /// <summary>
    /// 连续设施摆放的纯 C# 权威规则。它只做便宜、确定的几何预检；InteractionGroup / Slot 的
    /// 可达性由上层作为可修复的运行状态诊断。所有几何检查完成后才写入，
    /// 失败不留下半提交记录。
    /// </summary>
    public sealed class ContinuousFacilityPlacementLedger
    {
        private const double GeometryToleranceMillimeters = 0.0001d;

        private readonly Dictionary<int, DeckBounds> _boundsByLevel = new();
        private readonly Dictionary<string, ContinuousPlacedFacility> _placements =
            new(StringComparer.Ordinal);

        public ContinuousFacilityPlacementLedger(DeckBounds bounds)
            : this(new[] { bounds })
        {
        }

        public ContinuousFacilityPlacementLedger(IReadOnlyList<DeckBounds> bounds)
        {
            if (bounds == null) throw new ArgumentNullException(nameof(bounds));
            if (bounds.Count == 0)
                throw new ArgumentException("至少需要一层甲板边界。", nameof(bounds));

            for (var i = 0; i < bounds.Count; i++)
            {
                DeckBounds item = bounds[i];
                if (!_boundsByLevel.TryAdd(item.DeckLevel, item))
                    throw new ArgumentException(
                        $"甲板层 {item.DeckLevel} 的边界重复。",
                        nameof(bounds));
            }
        }

        public int Count => _placements.Count;

        public ContinuousPlacementFailure Evaluate(
            in ContinuousFacilityPlacementRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.InstanceId) ||
                string.IsNullOrWhiteSpace(request.DefinitionId) ||
                request.Footprint == null)
                return ContinuousPlacementFailure.InvalidRequest;

            if (_placements.ContainsKey(request.InstanceId))
                return ContinuousPlacementFailure.DuplicateInstanceId;
            if (!_boundsByLevel.TryGetValue(request.Pose.DeckLevel, out DeckBounds bounds))
                return ContinuousPlacementFailure.DeckLevelUnavailable;

            OrientedRectangle[] candidateRectangles = BuildRectangles(
                request.Pose,
                request.Footprint);
            for (var partIndex = 0; partIndex < candidateRectangles.Length; partIndex++)
            {
                if (!IsInsideBounds(candidateRectangles[partIndex], bounds))
                    return ContinuousPlacementFailure.FootprintOutOfBounds;
            }

            foreach (ContinuousPlacedFacility existing in _placements.Values)
            {
                if (existing.Pose.DeckLevel != request.Pose.DeckLevel) continue;

                OrientedRectangle[] existingRectangles = BuildRectangles(
                    existing.Pose,
                    existing.Footprint);
                for (var candidateIndex = 0;
                     candidateIndex < candidateRectangles.Length;
                     candidateIndex++)
                {
                    for (var existingIndex = 0;
                         existingIndex < existingRectangles.Length;
                         existingIndex++)
                    {
                        if (Overlaps(
                                candidateRectangles[candidateIndex],
                                existingRectangles[existingIndex]))
                            return ContinuousPlacementFailure.FootprintOverlapsFacility;
                    }
                }
            }

            return ContinuousPlacementFailure.None;
        }

        public bool TryPlace(
            in ContinuousFacilityPlacementRequest request,
            out ContinuousPlacedFacility placement,
            out ContinuousPlacementFailure failure)
        {
            failure = Evaluate(request);
            if (failure != ContinuousPlacementFailure.None)
            {
                placement = null;
                return false;
            }

            placement = new ContinuousPlacedFacility(request);
            _placements.Add(placement.InstanceId, placement);
            return true;
        }

        public bool Remove(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) return false;
            return _placements.Remove(instanceId);
        }

        public bool TryGetPlacement(
            string instanceId,
            out ContinuousPlacedFacility placement)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                placement = null;
                return false;
            }

            return _placements.TryGetValue(instanceId, out placement);
        }

        /// <summary>
        /// 为存档、调试和测试返回按稳定 InstanceId 排序的快照，避免依赖 Dictionary 枚举顺序。
        /// </summary>
        public IReadOnlyList<ContinuousPlacedFacility> CreateStableSnapshot()
        {
            var result = new List<ContinuousPlacedFacility>(_placements.Values);
            result.Sort((left, right) =>
                string.Compare(left.InstanceId, right.InstanceId, StringComparison.Ordinal));
            return result;
        }

        /// <summary>
        /// 把稳定顺序快照写入调用方复用的缓冲区。实时建造预检可借此避免每次指针移动都分配新列表。
        /// </summary>
        public void CopyStableSnapshotTo(List<ContinuousPlacedFacility> destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            destination.Clear();
            destination.AddRange(_placements.Values);
            destination.Sort((left, right) =>
                string.Compare(left.InstanceId, right.InstanceId, StringComparison.Ordinal));
        }

        private static OrientedRectangle[] BuildRectangles(
            in DeckPose pose,
            ContinuousFacilityFootprint footprint)
        {
            var result = new OrientedRectangle[footprint.Parts.Count];
            double poseRadians = DegreesToRadians(pose.YawDegrees);
            double poseCosine = Math.Cos(poseRadians);
            double poseSine = Math.Sin(poseRadians);

            for (var i = 0; i < footprint.Parts.Count; i++)
            {
                DeckFootprintPart part = footprint.Parts[i];
                double centerX = pose.XMillimeters +
                    poseCosine * part.LocalCenterXMillimeters +
                    poseSine * part.LocalCenterZMillimeters;
                double centerZ = pose.ZMillimeters -
                    poseSine * part.LocalCenterXMillimeters +
                    poseCosine * part.LocalCenterZMillimeters;
                double worldYawDegrees = pose.YawDegrees +
                    part.LocalYawDeciDegrees / 10d;
                result[i] = new OrientedRectangle(
                    centerX,
                    centerZ,
                    part.WidthMillimeters / 2d,
                    part.DepthMillimeters / 2d,
                    DegreesToRadians(worldYawDegrees));
            }

            return result;
        }

        private static bool IsInsideBounds(in OrientedRectangle rectangle, in DeckBounds bounds)
        {
            double extentX = Math.Abs(rectangle.AxisXX) * rectangle.HalfWidth +
                Math.Abs(rectangle.AxisZX) * rectangle.HalfDepth;
            double extentZ = Math.Abs(rectangle.AxisXZ) * rectangle.HalfWidth +
                Math.Abs(rectangle.AxisZZ) * rectangle.HalfDepth;
            return bounds.Contains(
                       rectangle.CenterX - extentX,
                       rectangle.CenterZ - extentZ,
                       GeometryToleranceMillimeters) &&
                   bounds.Contains(
                       rectangle.CenterX + extentX,
                       rectangle.CenterZ + extentZ,
                       GeometryToleranceMillimeters);
        }

        private static bool Overlaps(
            in OrientedRectangle left,
            in OrientedRectangle right)
        {
            double deltaX = right.CenterX - left.CenterX;
            double deltaZ = right.CenterZ - left.CenterZ;
            return OverlapsOnAxis(left.AxisXX, left.AxisXZ, deltaX, deltaZ, left, right) &&
                   OverlapsOnAxis(left.AxisZX, left.AxisZZ, deltaX, deltaZ, left, right) &&
                   OverlapsOnAxis(right.AxisXX, right.AxisXZ, deltaX, deltaZ, left, right) &&
                   OverlapsOnAxis(right.AxisZX, right.AxisZZ, deltaX, deltaZ, left, right);
        }

        private static bool OverlapsOnAxis(
            double axisX,
            double axisZ,
            double deltaX,
            double deltaZ,
            in OrientedRectangle left,
            in OrientedRectangle right)
        {
            double distance = Math.Abs(deltaX * axisX + deltaZ * axisZ);
            double leftRadius =
                left.HalfWidth * Math.Abs(left.AxisXX * axisX + left.AxisXZ * axisZ) +
                left.HalfDepth * Math.Abs(left.AxisZX * axisX + left.AxisZZ * axisZ);
            double rightRadius =
                right.HalfWidth * Math.Abs(right.AxisXX * axisX + right.AxisXZ * axisZ) +
                right.HalfDepth * Math.Abs(right.AxisZX * axisX + right.AxisZZ * axisZ);
            return distance + GeometryToleranceMillimeters < leftRadius + rightRadius;
        }

        private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180d;

        private readonly struct OrientedRectangle
        {
            public readonly double CenterX;
            public readonly double CenterZ;
            public readonly double HalfWidth;
            public readonly double HalfDepth;
            public readonly double AxisXX;
            public readonly double AxisXZ;
            public readonly double AxisZX;
            public readonly double AxisZZ;

            public OrientedRectangle(
                double centerX,
                double centerZ,
                double halfWidth,
                double halfDepth,
                double yawRadians)
            {
                CenterX = centerX;
                CenterZ = centerZ;
                HalfWidth = halfWidth;
                HalfDepth = halfDepth;

                double cosine = Math.Cos(yawRadians);
                double sine = Math.Sin(yawRadians);
                AxisXX = cosine;
                AxisXZ = -sine;
                AxisZX = sine;
                AxisZZ = cosine;
            }
        }
    }
}
