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
        FunctionalClearanceOutOfBounds,
        FunctionalClearanceOverlapsFacility,
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
        public readonly ContinuousFacilityFootprint FunctionalClearance;

        public ContinuousFacilityPlacementRequest(
            string instanceId,
            string definitionId,
            DeckPose pose,
            ContinuousFacilityFootprint footprint,
            ContinuousFacilityFootprint functionalClearance = null)
        {
            InstanceId = instanceId;
            DefinitionId = definitionId;
            Pose = pose;
            Footprint = footprint;
            FunctionalClearance = functionalClearance;
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
            FunctionalClearance = request.FunctionalClearance;
        }

        public string InstanceId { get; }
        public string DefinitionId { get; }
        public DeckPose Pose { get; }
        public ContinuousFacilityFootprint Footprint { get; }
        /// <summary>
        /// 不生成 NavMesh 障碍、但其他设施不得侵占的功能净空，例如水罐停放区。
        /// </summary>
        public ContinuousFacilityFootprint FunctionalClearance { get; }
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

            PlanarOrientedRectangle[] candidateRectangles =
                PlanarOrientedRectangleGeometry.BuildRectangles(
                request.Pose,
                request.Footprint);
            for (var partIndex = 0; partIndex < candidateRectangles.Length; partIndex++)
            {
                if (!IsInsideBounds(candidateRectangles[partIndex], bounds))
                    return ContinuousPlacementFailure.FootprintOutOfBounds;
            }

            PlanarOrientedRectangle[] candidateClearance = request.FunctionalClearance == null
                ? Array.Empty<PlanarOrientedRectangle>()
                : PlanarOrientedRectangleGeometry.BuildRectangles(
                    request.Pose,
                    request.FunctionalClearance);
            for (var partIndex = 0; partIndex < candidateClearance.Length; partIndex++)
            {
                if (!IsInsideBounds(candidateClearance[partIndex], bounds))
                    return ContinuousPlacementFailure.FunctionalClearanceOutOfBounds;
            }

            foreach (ContinuousPlacedFacility existing in _placements.Values)
            {
                if (existing.Pose.DeckLevel != request.Pose.DeckLevel) continue;

                PlanarOrientedRectangle[] existingRectangles =
                    PlanarOrientedRectangleGeometry.BuildRectangles(
                    existing.Pose,
                    existing.Footprint);
                if (AnyOverlap(candidateRectangles, existingRectangles))
                    return ContinuousPlacementFailure.FootprintOverlapsFacility;

                PlanarOrientedRectangle[] existingClearance =
                    existing.FunctionalClearance == null
                        ? Array.Empty<PlanarOrientedRectangle>()
                        : PlanarOrientedRectangleGeometry.BuildRectangles(
                            existing.Pose,
                            existing.FunctionalClearance);
                if (AnyOverlap(candidateClearance, existingRectangles) ||
                    AnyOverlap(candidateRectangles, existingClearance) ||
                    AnyOverlap(candidateClearance, existingClearance))
                    return ContinuousPlacementFailure.FunctionalClearanceOverlapsFacility;
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

        private static bool IsInsideBounds(
            in PlanarOrientedRectangle rectangle,
            in DeckBounds bounds)
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

        private static bool AnyOverlap(
            IReadOnlyList<PlanarOrientedRectangle> left,
            IReadOnlyList<PlanarOrientedRectangle> right)
        {
            for (var leftIndex = 0; leftIndex < left.Count; leftIndex++)
            {
                for (var rightIndex = 0; rightIndex < right.Count; rightIndex++)
                {
                    if (PlanarOrientedRectangleGeometry.Overlaps(
                            left[leftIndex],
                            right[rightIndex]))
                        return true;
                }
            }
            return false;
        }
    }
}
