using System;
using System.Collections.Generic;

namespace Game.NomadWorkshop.Simulation
{
    /// <summary>
    /// 建造拖动阶段使用的保守连续通路预检。它把小型甲板临时采样为八方向连通图，并按居民净空扩张
    /// 已建设施与候选设施；结果用于即时提示和拒绝明显死位，最终提交仍由 Unity NavMesh 事务裁决。
    /// </summary>
    public sealed class ContinuousDeckReachabilityProbe
    {
        private readonly DeckBounds _bounds;
        private readonly DeckSupportRegion _support;
        private readonly byte[] _unsupported;
        private readonly byte[] _supportConnections;
        private readonly int _sampleStepMillimeters;
        private readonly int _clearanceMillimeters;
        private readonly int _endpointSampleRadiusMillimeters;
        private readonly int _minimumXMillimeters;
        private readonly int _minimumZMillimeters;
        private readonly int _width;
        private readonly int _depth;
        private readonly byte[] _blocked;
        private readonly byte[] _reachable;
        private readonly int[] _queue;

        public ContinuousDeckReachabilityProbe(
            DeckBounds bounds,
            int sampleStepMillimeters,
            int clearanceMillimeters,
            int endpointSampleRadiusMillimeters)
            : this(new DeckSupportRegion(bounds), sampleStepMillimeters, clearanceMillimeters, endpointSampleRadiusMillimeters)
        {
        }

        /// <summary>缓存非矩形支撑及采样连边；窄缺口也会阻断通路，设施拖动不重建静态几何。</summary>
        public ContinuousDeckReachabilityProbe(
            DeckSupportRegion support,
            int sampleStepMillimeters,
            int clearanceMillimeters,
            int endpointSampleRadiusMillimeters)
        {
            _support = support ?? throw new ArgumentNullException(nameof(support));
            DeckBounds bounds = support.Bounds;
            if (sampleStepMillimeters <= 0)
                throw new ArgumentOutOfRangeException(nameof(sampleStepMillimeters));
            if (clearanceMillimeters < 0)
                throw new ArgumentOutOfRangeException(nameof(clearanceMillimeters));
            if (endpointSampleRadiusMillimeters < 0)
                throw new ArgumentOutOfRangeException(nameof(endpointSampleRadiusMillimeters));

            int maximumX = bounds.MaxXMillimeters - clearanceMillimeters;
            int maximumZ = bounds.MaxZMillimeters - clearanceMillimeters;
            _minimumXMillimeters = bounds.MinXMillimeters + clearanceMillimeters;
            _minimumZMillimeters = bounds.MinZMillimeters + clearanceMillimeters;
            if (maximumX < _minimumXMillimeters || maximumZ < _minimumZMillimeters)
                throw new ArgumentException("居民净空大于甲板可用范围。", nameof(clearanceMillimeters));

            _bounds = bounds;
            _sampleStepMillimeters = sampleStepMillimeters;
            _clearanceMillimeters = clearanceMillimeters;
            _endpointSampleRadiusMillimeters = endpointSampleRadiusMillimeters;
            _width = (maximumX - _minimumXMillimeters) / sampleStepMillimeters + 1;
            _depth = (maximumZ - _minimumZMillimeters) / sampleStepMillimeters + 1;
            _blocked = new byte[_width * _depth];
            _reachable = new byte[_blocked.Length];
            _queue = new int[_blocked.Length];
            _unsupported = new byte[_blocked.Length];
            _supportConnections = new byte[_blocked.Length];
            CacheSupport();
        }

        private void CacheSupport()
        {
            for (var z = 0; z < _depth; z++)
                for (var x = 0; x < _width; x++)
                {
                    int index = ToIndex(x, z);
                    DeckPose pose = ToPose(index, 0);
                    if (!_support.Contains(pose, _clearanceMillimeters))
                    {
                        _unsupported[index] = 1;
                        continue;
                    }
                    var direction = 0;
                    for (var dz = -1; dz <= 1; dz++)
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dz == 0) continue;
                            if (IsInside(x + dx, z + dz) && _support.CoversSweep(
                                pose.XMillimeters, pose.ZMillimeters,
                                (double)pose.XMillimeters + dx * _sampleStepMillimeters,
                                (double)pose.ZMillimeters + dz * _sampleStepMillimeters, _clearanceMillimeters))
                                _supportConnections[index] |= (byte)(1 << direction);
                            direction++;
                        }
                }
        }

        public int CellCount => _blocked.Length;
        public int VisitedCellCount { get; private set; }

        /// <summary>重建候选状态并从固定安全锚点执行一次 Flood Fill；不修改传入设施或正式摆放账本。</summary>
        public bool Rebuild(
            IReadOnlyList<ContinuousPlacedFacility> committedFacilities,
            ContinuousFacilityPlacementRequest? candidate,
            in DeckPose origin)
        {
            return Rebuild(
                committedFacilities,
                candidate,
                origin,
                allowOriginRelocation: false,
                out _);
        }

        /// <summary>
        /// 重建候选状态；若动态居民恰好站在候选占地中，可把连通区起点解析到最近的空闲采样点。
        /// 这只模拟施工前的自动避让，不会放宽设施边界、实体重叠或交互位可达规则。
        /// </summary>
        public bool RebuildAllowingOriginRelocation(
            IReadOnlyList<ContinuousPlacedFacility> committedFacilities,
            ContinuousFacilityPlacementRequest? candidate,
            in DeckPose origin,
            out DeckPose resolvedOrigin)
        {
            return Rebuild(
                committedFacilities,
                candidate,
                origin,
                allowOriginRelocation: true,
                out resolvedOrigin);
        }

        private bool Rebuild(
            IReadOnlyList<ContinuousPlacedFacility> committedFacilities,
            ContinuousFacilityPlacementRequest? candidate,
            in DeckPose origin,
            bool allowOriginRelocation,
            out DeckPose resolvedOrigin)
        {
            if (committedFacilities == null)
                throw new ArgumentNullException(nameof(committedFacilities));

            Array.Copy(_unsupported, _blocked, _blocked.Length);
            Array.Clear(_reachable, 0, _reachable.Length);
            VisitedCellCount = 0;

            for (var i = 0; i < committedFacilities.Count; i++)
            {
                ContinuousPlacedFacility facility = committedFacilities[i];
                if (facility != null && facility.Pose.DeckLevel == _bounds.DeckLevel)
                    Rasterize(facility.Pose, facility.Footprint);
            }
            if (candidate.HasValue && candidate.Value.Pose.DeckLevel == _bounds.DeckLevel)
                Rasterize(candidate.Value.Pose, candidate.Value.Footprint);

            resolvedOrigin = default;
            if (origin.DeckLevel != _bounds.DeckLevel)
                return false;

            bool foundStart = TryFindSampleIndex(
                origin.XMillimeters,
                origin.ZMillimeters,
                false,
                out int start);
            if (!foundStart && allowOriginRelocation)
                foundStart = TryFindClosestUnblockedSampleIndex(
                    origin.XMillimeters,
                    origin.ZMillimeters,
                    out start);
            if (!foundStart) return false;

            FloodFill(start);
            resolvedOrigin = ToPose(start, origin.YawDeciDegrees);
            return true;
        }

        /// <summary>判断目标附近是否存在处于同一连通区的采样点。</summary>
        public bool IsReachable(in DeckPose target) =>
            target.DeckLevel == _bounds.DeckLevel &&
            TryFindSampleIndex(
                target.XMillimeters,
                target.ZMillimeters,
                true,
                out _);

        private void Rasterize(in DeckPose pose, ContinuousFacilityFootprint footprint)
        {
            if (footprint == null) return;

            double poseRadians = DegreesToRadians(pose.YawDegrees);
            double poseCosine = Math.Cos(poseRadians);
            double poseSine = Math.Sin(poseRadians);
            for (var partIndex = 0; partIndex < footprint.Parts.Count; partIndex++)
            {
                DeckFootprintPart part = footprint.Parts[partIndex];
                double centerX = pose.XMillimeters +
                    poseCosine * part.LocalCenterXMillimeters +
                    poseSine * part.LocalCenterZMillimeters;
                double centerZ = pose.ZMillimeters -
                    poseSine * part.LocalCenterXMillimeters +
                    poseCosine * part.LocalCenterZMillimeters;
                double yawRadians = DegreesToRadians(
                    pose.YawDegrees + part.LocalYawDeciDegrees / 10d);
                double cosine = Math.Cos(yawRadians);
                double sine = Math.Sin(yawRadians);
                double halfWidth = part.WidthMillimeters * 0.5d + _clearanceMillimeters;
                double halfDepth = part.DepthMillimeters * 0.5d + _clearanceMillimeters;

                for (var z = 0; z < _depth; z++)
                {
                    double sampleZ = _minimumZMillimeters + z * _sampleStepMillimeters;
                    for (var x = 0; x < _width; x++)
                    {
                        int index = ToIndex(x, z);
                        if (_blocked[index] != 0) continue;

                        double deltaX = _minimumXMillimeters + x * _sampleStepMillimeters - centerX;
                        double deltaZ = sampleZ - centerZ;
                        double localX = deltaX * cosine - deltaZ * sine;
                        double localZ = deltaX * sine + deltaZ * cosine;
                        if (Math.Abs(localX) <= halfWidth && Math.Abs(localZ) <= halfDepth)
                            _blocked[index] = 1;
                    }
                }
            }
        }

        private void FloodFill(int start)
        {
            var head = 0;
            var tail = 0;
            _queue[tail++] = start;
            _reachable[start] = 1;

            while (head < tail)
            {
                int current = _queue[head++];
                VisitedCellCount++;
                int x = current % _width;
                int z = current / _width;
                var direction = 0;
                for (var zOffset = -1; zOffset <= 1; zOffset++)
                {
                    for (var xOffset = -1; xOffset <= 1; xOffset++)
                    {
                        if (xOffset == 0 && zOffset == 0) continue;
                        bool supported = (_supportConnections[current] & (1 << direction++)) != 0;
                        if (!supported) continue;
                        int nextX = x + xOffset;
                        int nextZ = z + zOffset;
                        if (!IsInside(nextX, nextZ)) continue;

                        int next = ToIndex(nextX, nextZ);
                        if (_blocked[next] != 0 || _reachable[next] != 0) continue;
                        if (xOffset != 0 && zOffset != 0 &&
                            (_blocked[ToIndex(x + xOffset, z)] != 0 ||
                             _blocked[ToIndex(x, z + zOffset)] != 0))
                            continue;

                        _reachable[next] = 1;
                        _queue[tail++] = next;
                    }
                }
            }
        }

        private bool TryFindSampleIndex(
            int xMillimeters,
            int zMillimeters,
            bool requireReachable,
            out int result)
        {
            int approximateX = RoundToNearestIndex(xMillimeters, _minimumXMillimeters);
            int approximateZ = RoundToNearestIndex(zMillimeters, _minimumZMillimeters);
            int searchRadius = _endpointSampleRadiusMillimeters / _sampleStepMillimeters + 1;
            long maximumDistanceSquared =
                (long)_endpointSampleRadiusMillimeters * _endpointSampleRadiusMillimeters;
            long bestDistanceSquared = long.MaxValue;
            result = -1;

            // 端点可在采样半径内对齐，但不能从孔洞或另一块断开的板吸附到可达点。
            if (!_support.Contains(new DeckPose(xMillimeters, zMillimeters, 0, _bounds.DeckLevel)))
                return false;

            for (int z = approximateZ - searchRadius; z <= approximateZ + searchRadius; z++)
            {
                if (z < 0 || z >= _depth) continue;
                int sampleZ = _minimumZMillimeters + z * _sampleStepMillimeters;
                for (int x = approximateX - searchRadius; x <= approximateX + searchRadius; x++)
                {
                    if (x < 0 || x >= _width) continue;
                    int index = ToIndex(x, z);
                    if (_blocked[index] != 0 || requireReachable && _reachable[index] == 0)
                        continue;

                    int deltaX = _minimumXMillimeters + x * _sampleStepMillimeters - xMillimeters;
                    int deltaZ = sampleZ - zMillimeters;
                    long distanceSquared = (long)deltaX * deltaX + (long)deltaZ * deltaZ;
                    if (distanceSquared > maximumDistanceSquared ||
                        distanceSquared >= bestDistanceSquared)
                        continue;

                    if (!_support.CoversSweep(xMillimeters, zMillimeters,
                        _minimumXMillimeters + x * _sampleStepMillimeters, sampleZ, 0)) continue;

                    bestDistanceSquared = distanceSquared;
                    result = index;
                }
            }

            return result >= 0;
        }

        private int RoundToNearestIndex(int coordinate, int minimum) =>
            (int)Math.Round(
                (coordinate - minimum) / (double)_sampleStepMillimeters,
                MidpointRounding.AwayFromZero);

        private bool TryFindClosestUnblockedSampleIndex(
            int xMillimeters,
            int zMillimeters,
            out int result)
        {
            long bestDistanceSquared = long.MaxValue;
            result = -1;
            for (var z = 0; z < _depth; z++)
            {
                int sampleZ = _minimumZMillimeters + z * _sampleStepMillimeters;
                for (var x = 0; x < _width; x++)
                {
                    int index = ToIndex(x, z);
                    if (_blocked[index] != 0) continue;

                    int deltaX = _minimumXMillimeters + x * _sampleStepMillimeters - xMillimeters;
                    int deltaZ = sampleZ - zMillimeters;
                    long distanceSquared = (long)deltaX * deltaX + (long)deltaZ * deltaZ;
                    if (distanceSquared >= bestDistanceSquared) continue;

                    bestDistanceSquared = distanceSquared;
                    result = index;
                }
            }
            return result >= 0;
        }

        private DeckPose ToPose(int index, int yawDeciDegrees)
        {
            int x = index % _width;
            int z = index / _width;
            return new DeckPose(
                _minimumXMillimeters + x * _sampleStepMillimeters,
                _minimumZMillimeters + z * _sampleStepMillimeters,
                yawDeciDegrees,
                _bounds.DeckLevel);
        }

        private bool IsInside(int x, int z) => x >= 0 && x < _width && z >= 0 && z < _depth;
        private int ToIndex(int x, int z) => z * _width + x;
        private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180d;
    }
}
