using System;
using System.Collections.Generic;

namespace Game.NomadWorkshop.Simulation
{
    /// <summary>放置区域申请失败的首要原因；失败必须保持账本不变。</summary>
    public enum PlacementRegionFailure
    {
        None,
        InvalidRequest,
        DuplicateRegionId,
        RegionUnavailable,
        ItemAlreadyLocated,
        CategoryRejected,
        FootprintTooLarge,
        OrientationRejected,
        PoseOutsideRegion,
        PoseOverlapsItem,
        RegionFull,
        ReservationNotActive,
    }

    /// <summary>
    /// 支撑对象在甲板上提供的一片矩形放置面。<see cref="Pose"/> 是区域中心的世界姿态，
    /// Item 的存档姿态仍相对该区域保存；支撑高度只影响 View，不参与二维容量计算。
    /// </summary>
    public sealed class PlacementRegionDefinition
    {
        private readonly string[] _acceptedCategories;

        public PlacementRegionDefinition(
            string regionId,
            string localRegionId,
            string ownerEntityId,
            in DeckPose pose,
            int widthMillimeters,
            int depthMillimeters,
            int supportHeightMillimeters,
            int edgeInsetMillimeters,
            IReadOnlyList<string> acceptedCategories)
        {
            if (string.IsNullOrWhiteSpace(regionId))
                throw new ArgumentException("放置区域必须有全局稳定 id。", nameof(regionId));
            if (string.IsNullOrWhiteSpace(localRegionId))
                throw new ArgumentException("放置区域必须有所有者内稳定 id。", nameof(localRegionId));
            if (string.IsNullOrWhiteSpace(ownerEntityId))
                throw new ArgumentException("放置区域必须有支撑对象。", nameof(ownerEntityId));
            if (widthMillimeters <= 0)
                throw new ArgumentOutOfRangeException(nameof(widthMillimeters));
            if (depthMillimeters <= 0)
                throw new ArgumentOutOfRangeException(nameof(depthMillimeters));
            if (supportHeightMillimeters < 0)
                throw new ArgumentOutOfRangeException(nameof(supportHeightMillimeters));
            if (edgeInsetMillimeters < 0 || edgeInsetMillimeters * 2 >= widthMillimeters ||
                edgeInsetMillimeters * 2 >= depthMillimeters)
                throw new ArgumentOutOfRangeException(nameof(edgeInsetMillimeters));
            if (acceptedCategories == null || acceptedCategories.Count == 0)
                throw new ArgumentException("放置区域至少接受一种物品类别。", nameof(acceptedCategories));

            RegionId = regionId.Trim();
            LocalRegionId = localRegionId.Trim();
            OwnerEntityId = ownerEntityId.Trim();
            Pose = pose;
            WidthMillimeters = widthMillimeters;
            DepthMillimeters = depthMillimeters;
            SupportHeightMillimeters = supportHeightMillimeters;
            EdgeInsetMillimeters = edgeInsetMillimeters;

            var unique = new SortedSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < acceptedCategories.Count; i++)
            {
                string category = acceptedCategories[i]?.Trim();
                if (string.IsNullOrWhiteSpace(category))
                    throw new ArgumentException($"放置区域接受类别第 {i} 项为空。", nameof(acceptedCategories));
                unique.Add(category);
            }
            _acceptedCategories = new string[unique.Count];
            unique.CopyTo(_acceptedCategories);
        }

        public string RegionId { get; }
        public string LocalRegionId { get; }
        public string OwnerEntityId { get; }
        public DeckPose Pose { get; }
        public int WidthMillimeters { get; }
        public int DepthMillimeters { get; }
        public int SupportHeightMillimeters { get; }
        public int EdgeInsetMillimeters { get; }
        public IReadOnlyList<string> AcceptedCategories => _acceptedCategories;

        public bool Accepts(string categoryId) =>
            !string.IsNullOrWhiteSpace(categoryId) &&
            Array.BinarySearch(_acceptedCategories, categoryId, StringComparer.Ordinal) >= 0;
    }

    /// <summary>一种物品的二维占地与允许朝向；尺寸和安全边距都使用毫米。</summary>
    public sealed class PlacementFootprint
    {
        private readonly int[] _allowedYawDeciDegrees;
        private readonly bool _allowsAnyYaw;

        public PlacementFootprint(
            string definitionId,
            string categoryId,
            int widthMillimeters,
            int depthMillimeters,
            int heightMillimeters,
            int safetyMarginMillimeters,
            IReadOnlyList<int> allowedYawDeciDegrees,
            bool allowsAnyYaw = false)
        {
            if (string.IsNullOrWhiteSpace(definitionId))
                throw new ArgumentException("物品占地必须有定义 id。", nameof(definitionId));
            if (string.IsNullOrWhiteSpace(categoryId))
                throw new ArgumentException("物品占地必须有类别 id。", nameof(categoryId));
            if (widthMillimeters <= 0)
                throw new ArgumentOutOfRangeException(nameof(widthMillimeters));
            if (depthMillimeters <= 0)
                throw new ArgumentOutOfRangeException(nameof(depthMillimeters));
            if (heightMillimeters <= 0)
                throw new ArgumentOutOfRangeException(nameof(heightMillimeters));
            if (safetyMarginMillimeters < 0)
                throw new ArgumentOutOfRangeException(nameof(safetyMarginMillimeters));
            if (allowedYawDeciDegrees == null || allowedYawDeciDegrees.Count == 0)
                throw new ArgumentException("物品至少需要一个允许朝向。", nameof(allowedYawDeciDegrees));

            DefinitionId = definitionId.Trim();
            CategoryId = categoryId.Trim();
            WidthMillimeters = widthMillimeters;
            DepthMillimeters = depthMillimeters;
            HeightMillimeters = heightMillimeters;
            SafetyMarginMillimeters = safetyMarginMillimeters;
            _allowsAnyYaw = allowsAnyYaw;

            var unique = new SortedSet<int>();
            for (var i = 0; i < allowedYawDeciDegrees.Count; i++)
                unique.Add(DeckPose.NormalizeYaw(allowedYawDeciDegrees[i]));
            _allowedYawDeciDegrees = new int[unique.Count];
            unique.CopyTo(_allowedYawDeciDegrees);
        }

        public string DefinitionId { get; }
        public string CategoryId { get; }
        public int WidthMillimeters { get; }
        public int DepthMillimeters { get; }
        public int HeightMillimeters { get; }
        public int SafetyMarginMillimeters { get; }
        public bool AllowsAnyYaw => _allowsAnyYaw;
        public IReadOnlyList<int> AllowedYawDeciDegrees => _allowedYawDeciDegrees;

        public bool AllowsYaw(int yawDeciDegrees) =>
            _allowsAnyYaw ||
            Array.BinarySearch(_allowedYawDeciDegrees, DeckPose.NormalizeYaw(yawDeciDegrees)) >= 0;
    }

    /// <summary>物品相对放置区域中心的量化姿态；它与区域 id 一起构成存档真值。</summary>
    public readonly struct PlacementRegionPose : IEquatable<PlacementRegionPose>
    {
        public PlacementRegionPose(
            int localXMillimeters,
            int localZMillimeters,
            int localYawDeciDegrees)
        {
            LocalXMillimeters = localXMillimeters;
            LocalZMillimeters = localZMillimeters;
            LocalYawDeciDegrees = DeckPose.NormalizeYaw(localYawDeciDegrees);
        }

        public int LocalXMillimeters { get; }
        public int LocalZMillimeters { get; }
        public int LocalYawDeciDegrees { get; }
        public static PlacementRegionPose Centered => default;

        public bool Equals(PlacementRegionPose other) =>
            LocalXMillimeters == other.LocalXMillimeters &&
            LocalZMillimeters == other.LocalZMillimeters &&
            LocalYawDeciDegrees == other.LocalYawDeciDegrees;

        public override bool Equals(object obj) => obj is PlacementRegionPose other && Equals(other);
        public override int GetHashCode() =>
            HashCode.Combine(LocalXMillimeters, LocalZMillimeters, LocalYawDeciDegrees);
        public static bool operator ==(PlacementRegionPose left, PlacementRegionPose right) =>
            left.Equals(right);
        public static bool operator !=(PlacementRegionPose left, PlacementRegionPose right) =>
            !left.Equals(right);
    }

    /// <summary>已经提交或正在预留的一件物品候选；世界姿态可由区域与局部姿态确定性重建。</summary>
    public sealed class PlacementRegionItem
    {
        internal PlacementRegionItem(
            string itemId,
            PlacementFootprint footprint,
            PlacementRegionDefinition region,
            in PlacementRegionPose localPose)
        {
            ItemId = itemId;
            Footprint = footprint;
            Region = region;
            LocalPose = localPose;
            WorldPose = region.Pose.TransformLocal(
                localPose.LocalXMillimeters,
                localPose.LocalZMillimeters,
                localPose.LocalYawDeciDegrees);
        }

        public string ItemId { get; }
        public PlacementFootprint Footprint { get; }
        public PlacementRegionDefinition Region { get; }
        public PlacementRegionPose LocalPose { get; }
        public DeckPose WorldPose { get; }
    }

    /// <summary>
    /// 区域放置的纯 C# 权威账本。候选查询不写入；Reservation 会占用精确姿态，只有 Commit
    /// 才改变物品所有权。Dispose 等价于取消，陈旧句柄不能删除或提交后来者的预留。
    /// </summary>
    public sealed class PlacementRegionLedger
    {
        private readonly Dictionary<string, PlacementRegionDefinition> _regions =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, PlacementRegionItem> _placedByItem =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, PendingReservation> _reservedByItem =
            new(StringComparer.Ordinal);
        private long _nextReservationId = 1;

        public int RegionCount => _regions.Count;
        public int PlacedItemCount => _placedByItem.Count;
        public int ReservationCount => _reservedByItem.Count;

        public static string ComposeRegionId(string ownerEntityId, string localRegionId)
        {
            if (string.IsNullOrWhiteSpace(ownerEntityId))
                throw new ArgumentException("区域所有者不能为空。", nameof(ownerEntityId));
            if (string.IsNullOrWhiteSpace(localRegionId))
                throw new ArgumentException("区域局部 id 不能为空。", nameof(localRegionId));
            return $"{ownerEntityId.Trim()}/placement/{localRegionId.Trim()}";
        }

        public PlacementRegionFailure RegisterRegion(PlacementRegionDefinition region)
        {
            if (region == null) return PlacementRegionFailure.InvalidRequest;
            if (!_regions.TryAdd(region.RegionId, region))
                return PlacementRegionFailure.DuplicateRegionId;
            return PlacementRegionFailure.None;
        }

        public bool RemoveRegion(string regionId)
        {
            if (string.IsNullOrWhiteSpace(regionId) || !_regions.ContainsKey(regionId))
                return false;

            foreach (PlacementRegionItem item in _placedByItem.Values)
            {
                if (string.Equals(item.Region.RegionId, regionId, StringComparison.Ordinal))
                    return false;
            }
            foreach (PendingReservation reservation in _reservedByItem.Values)
            {
                if (string.Equals(
                        reservation.Candidate.Region.RegionId,
                        regionId,
                        StringComparison.Ordinal))
                    return false;
            }
            return _regions.Remove(regionId);
        }

        public bool TryGetRegion(string regionId, out PlacementRegionDefinition region) =>
            _regions.TryGetValue(regionId ?? string.Empty, out region);

        public bool TryGetPlacement(string itemId, out PlacementRegionItem placement) =>
            _placedByItem.TryGetValue(itemId ?? string.Empty, out placement);

        public bool TryReserveStable(
            string itemId,
            PlacementFootprint footprint,
            string regionId,
            out PlacementRegionReservation reservation,
            out PlacementRegionFailure failure)
        {
            failure = ValidateRequest(itemId, footprint, regionId, out PlacementRegionDefinition region);
            if (failure != PlacementRegionFailure.None)
            {
                reservation = null;
                return false;
            }

            if (!TryFindStablePose(region, footprint, out PlacementRegionPose pose))
            {
                reservation = null;
                failure = CanFitRegionBounds(region, footprint)
                    ? PlacementRegionFailure.RegionFull
                    : PlacementRegionFailure.FootprintTooLarge;
                return false;
            }

            return ReserveCandidate(itemId.Trim(), footprint, region, pose, out reservation, out failure);
        }

        public bool TryReserveExact(
            string itemId,
            PlacementFootprint footprint,
            string regionId,
            in PlacementRegionPose pose,
            out PlacementRegionReservation reservation,
            out PlacementRegionFailure failure)
        {
            failure = ValidateRequest(itemId, footprint, regionId, out PlacementRegionDefinition region);
            if (failure != PlacementRegionFailure.None)
            {
                reservation = null;
                return false;
            }

            if (!footprint.AllowsYaw(pose.LocalYawDeciDegrees))
            {
                reservation = null;
                failure = PlacementRegionFailure.OrientationRejected;
                return false;
            }

            if (!Contains(region, footprint, pose))
            {
                reservation = null;
                failure = PlacementRegionFailure.PoseOutsideRegion;
                return false;
            }
            if (OverlapsAny(footprint, region, pose))
            {
                reservation = null;
                failure = PlacementRegionFailure.PoseOverlapsItem;
                return false;
            }

            return ReserveCandidate(itemId.Trim(), footprint, region, pose, out reservation, out failure);
        }

        public bool TryRestorePlacement(
            string itemId,
            PlacementFootprint footprint,
            string regionId,
            in PlacementRegionPose pose,
            out PlacementRegionItem placement,
            out PlacementRegionFailure failure)
        {
            if (!TryReserveExact(
                    itemId,
                    footprint,
                    regionId,
                    pose,
                    out PlacementRegionReservation reservation,
                    out failure))
            {
                placement = null;
                return false;
            }

            using (reservation)
                return reservation.TryCommit(out placement, out failure);
        }

        public bool RemoveItem(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId)) return false;
            return _placedByItem.Remove(itemId);
        }

        public IReadOnlyList<PlacementRegionItem> CreateStableSnapshot()
        {
            var result = new List<PlacementRegionItem>(_placedByItem.Values);
            result.Sort((left, right) =>
                string.Compare(left.ItemId, right.ItemId, StringComparison.Ordinal));
            return result;
        }

        internal bool TryCommit(
            long reservationId,
            string itemId,
            out PlacementRegionItem placement,
            out PlacementRegionFailure failure)
        {
            if (!_reservedByItem.TryGetValue(itemId, out PendingReservation pending) ||
                pending.ReservationId != reservationId)
            {
                placement = null;
                failure = PlacementRegionFailure.ReservationNotActive;
                return false;
            }

            _reservedByItem.Remove(itemId);
            placement = pending.Candidate;
            _placedByItem.Add(itemId, placement);
            failure = PlacementRegionFailure.None;
            return true;
        }

        internal void Cancel(long reservationId, string itemId)
        {
            if (_reservedByItem.TryGetValue(itemId, out PendingReservation pending) &&
                pending.ReservationId == reservationId)
                _reservedByItem.Remove(itemId);
        }

        private PlacementRegionFailure ValidateRequest(
            string itemId,
            PlacementFootprint footprint,
            string regionId,
            out PlacementRegionDefinition region)
        {
            if (string.IsNullOrWhiteSpace(itemId) || footprint == null ||
                string.IsNullOrWhiteSpace(regionId))
            {
                region = null;
                return PlacementRegionFailure.InvalidRequest;
            }
            if (_placedByItem.ContainsKey(itemId) || _reservedByItem.ContainsKey(itemId))
            {
                region = null;
                return PlacementRegionFailure.ItemAlreadyLocated;
            }
            if (!_regions.TryGetValue(regionId, out region))
                return PlacementRegionFailure.RegionUnavailable;
            if (!region.Accepts(footprint.CategoryId))
                return PlacementRegionFailure.CategoryRejected;
            return PlacementRegionFailure.None;
        }

        private bool ReserveCandidate(
            string itemId,
            PlacementFootprint footprint,
            PlacementRegionDefinition region,
            in PlacementRegionPose pose,
            out PlacementRegionReservation reservation,
            out PlacementRegionFailure failure)
        {
            long reservationId = _nextReservationId++;
            var candidate = new PlacementRegionItem(itemId, footprint, region, pose);
            _reservedByItem.Add(itemId, new PendingReservation(reservationId, candidate));
            reservation = new PlacementRegionReservation(this, reservationId, candidate);
            failure = PlacementRegionFailure.None;
            return true;
        }

        private bool TryFindStablePose(
            PlacementRegionDefinition region,
            PlacementFootprint footprint,
            out PlacementRegionPose result)
        {
            for (var yawIndex = 0; yawIndex < footprint.AllowedYawDeciDegrees.Count; yawIndex++)
            {
                int yaw = footprint.AllowedYawDeciDegrees[yawIndex];
                GetLocalExtents(footprint, yaw, out double extentX, out double extentZ);
                double halfUsableWidth = region.WidthMillimeters * 0.5d -
                                         region.EdgeInsetMillimeters;
                double halfUsableDepth = region.DepthMillimeters * 0.5d -
                                         region.EdgeInsetMillimeters;
                double minX = -halfUsableWidth + extentX;
                double maxX = halfUsableWidth - extentX;
                double minZ = -halfUsableDepth + extentZ;
                double maxZ = halfUsableDepth - extentZ;
                if (minX > maxX || minZ > maxZ) continue;

                List<double> xCandidates = CreateAxisCandidates(
                    region,
                    axisIsX: true,
                    extentX,
                    minX,
                    maxX);
                List<double> zCandidates = CreateAxisCandidates(
                    region,
                    axisIsX: false,
                    extentZ,
                    minZ,
                    maxZ);
                for (var zIndex = 0; zIndex < zCandidates.Count; zIndex++)
                {
                    for (var xIndex = 0; xIndex < xCandidates.Count; xIndex++)
                    {
                        var candidate = new PlacementRegionPose(
                            RoundCoordinate(xCandidates[xIndex]),
                            RoundCoordinate(zCandidates[zIndex]),
                            yaw);
                        if (Contains(region, footprint, candidate) &&
                            !OverlapsAny(footprint, region, candidate))
                        {
                            result = candidate;
                            return true;
                        }
                    }
                }
            }

            result = default;
            return false;
        }

        private List<double> CreateAxisCandidates(
            PlacementRegionDefinition region,
            bool axisIsX,
            double candidateExtent,
            double minimum,
            double maximum)
        {
            var values = new List<double> { minimum, maximum };
            if (minimum <= 0d && maximum >= 0d) values.Add(0d);

            foreach (PlacementRegionItem occupied in EnumerateOccupied())
            {
                PlanarOrientedRectangle rectangle = CreateWorldRectangle(
                    occupied.Footprint,
                    occupied.WorldPose);
                double axisX = axisIsX ? RegionAxisX(region).x : RegionAxisZ(region).x;
                double axisZ = axisIsX ? RegionAxisX(region).z : RegionAxisZ(region).z;
                double deltaX = rectangle.CenterX - region.Pose.XMillimeters;
                double deltaZ = rectangle.CenterZ - region.Pose.ZMillimeters;
                double occupiedCenter = deltaX * axisX + deltaZ * axisZ;
                double occupiedExtent = rectangle.ProjectedRadius(axisX, axisZ);
                AddIfWithin(values, occupiedCenter - occupiedExtent - candidateExtent, minimum, maximum);
                AddIfWithin(values, occupiedCenter + occupiedExtent + candidateExtent, minimum, maximum);
            }

            values.Sort();
            for (var i = values.Count - 1; i > 0; i--)
            {
                if (Math.Abs(values[i] - values[i - 1]) < 0.0001d)
                    values.RemoveAt(i);
            }
            return values;
        }

        private bool OverlapsAny(
            PlacementFootprint footprint,
            PlacementRegionDefinition region,
            in PlacementRegionPose localPose)
        {
            var candidate = new PlacementRegionItem("candidate", footprint, region, localPose);
            PlanarOrientedRectangle candidateRectangle = CreateWorldRectangle(
                footprint,
                candidate.WorldPose);
            foreach (PlacementRegionItem occupied in EnumerateOccupied())
            {
                PlanarOrientedRectangle occupiedRectangle = CreateWorldRectangle(
                    occupied.Footprint,
                    occupied.WorldPose);
                if (PlanarOrientedRectangleGeometry.Overlaps(
                        candidateRectangle,
                        occupiedRectangle))
                    return true;
            }
            return false;
        }

        private IEnumerable<PlacementRegionItem> EnumerateOccupied()
        {
            foreach (PlacementRegionItem placement in _placedByItem.Values)
                yield return placement;
            foreach (PendingReservation reservation in _reservedByItem.Values)
                yield return reservation.Candidate;
        }

        private static bool CanFitRegionBounds(
            PlacementRegionDefinition region,
            PlacementFootprint footprint)
        {
            for (var i = 0; i < footprint.AllowedYawDeciDegrees.Count; i++)
            {
                GetLocalExtents(
                    footprint,
                    footprint.AllowedYawDeciDegrees[i],
                    out double extentX,
                    out double extentZ);
                if (extentX <= region.WidthMillimeters * 0.5d - region.EdgeInsetMillimeters &&
                    extentZ <= region.DepthMillimeters * 0.5d - region.EdgeInsetMillimeters)
                    return true;
            }
            return false;
        }

        private static bool Contains(
            PlacementRegionDefinition region,
            PlacementFootprint footprint,
            in PlacementRegionPose pose)
        {
            GetLocalExtents(
                footprint,
                pose.LocalYawDeciDegrees,
                out double extentX,
                out double extentZ);
            double halfWidth = region.WidthMillimeters * 0.5d - region.EdgeInsetMillimeters;
            double halfDepth = region.DepthMillimeters * 0.5d - region.EdgeInsetMillimeters;
            return Math.Abs(pose.LocalXMillimeters) + extentX <= halfWidth + 0.0001d &&
                   Math.Abs(pose.LocalZMillimeters) + extentZ <= halfDepth + 0.0001d;
        }

        private static PlanarOrientedRectangle CreateWorldRectangle(
            PlacementFootprint footprint,
            in DeckPose worldPose) =>
            new(
                worldPose.XMillimeters,
                worldPose.ZMillimeters,
                footprint.WidthMillimeters * 0.5d + footprint.SafetyMarginMillimeters,
                footprint.DepthMillimeters * 0.5d + footprint.SafetyMarginMillimeters,
                worldPose.YawDegrees);

        private static void GetLocalExtents(
            PlacementFootprint footprint,
            int yawDeciDegrees,
            out double extentX,
            out double extentZ)
        {
            double radians = yawDeciDegrees / 10d * Math.PI / 180d;
            double cosine = Math.Abs(Math.Cos(radians));
            double sine = Math.Abs(Math.Sin(radians));
            double halfWidth = footprint.WidthMillimeters * 0.5d +
                               footprint.SafetyMarginMillimeters;
            double halfDepth = footprint.DepthMillimeters * 0.5d +
                               footprint.SafetyMarginMillimeters;
            extentX = cosine * halfWidth + sine * halfDepth;
            extentZ = sine * halfWidth + cosine * halfDepth;
        }

        private static (double x, double z) RegionAxisX(PlacementRegionDefinition region)
        {
            double radians = region.Pose.YawDegrees * Math.PI / 180d;
            return (Math.Cos(radians), -Math.Sin(radians));
        }

        private static (double x, double z) RegionAxisZ(PlacementRegionDefinition region)
        {
            double radians = region.Pose.YawDegrees * Math.PI / 180d;
            return (Math.Sin(radians), Math.Cos(radians));
        }

        private static void AddIfWithin(
            ICollection<double> values,
            double value,
            double minimum,
            double maximum)
        {
            if (value >= minimum - 0.0001d && value <= maximum + 0.0001d)
                values.Add(Math.Max(minimum, Math.Min(maximum, value)));
        }

        private static int RoundCoordinate(double value)
        {
            if (value < int.MinValue || value > int.MaxValue)
                throw new OverflowException("放置候选坐标超出毫米量化范围。");
            return (int)Math.Round(value, MidpointRounding.AwayFromZero);
        }

        private readonly struct PendingReservation
        {
            public PendingReservation(long reservationId, PlacementRegionItem candidate)
            {
                ReservationId = reservationId;
                Candidate = candidate;
            }

            public long ReservationId { get; }
            public PlacementRegionItem Candidate { get; }
        }
    }

    /// <summary>精确放置姿态的所有权句柄；提交或取消后均失效。</summary>
    public sealed class PlacementRegionReservation : IDisposable
    {
        private PlacementRegionLedger _ledger;
        private readonly long _reservationId;

        internal PlacementRegionReservation(
            PlacementRegionLedger ledger,
            long reservationId,
            PlacementRegionItem candidate)
        {
            _ledger = ledger;
            _reservationId = reservationId;
            Candidate = candidate;
        }

        public PlacementRegionItem Candidate { get; }
        public bool IsActive => _ledger != null;

        public bool TryCommit(
            out PlacementRegionItem placement,
            out PlacementRegionFailure failure)
        {
            PlacementRegionLedger ledger = _ledger;
            if (ledger == null)
            {
                placement = null;
                failure = PlacementRegionFailure.ReservationNotActive;
                return false;
            }

            bool committed = ledger.TryCommit(
                _reservationId,
                Candidate.ItemId,
                out placement,
                out failure);
            _ledger = null;
            return committed;
        }

        public void Dispose()
        {
            PlacementRegionLedger ledger = _ledger;
            if (ledger == null) return;
            _ledger = null;
            ledger.Cancel(_reservationId, Candidate.ItemId);
        }
    }
}
