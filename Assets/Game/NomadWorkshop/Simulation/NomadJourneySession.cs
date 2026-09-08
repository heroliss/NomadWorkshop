using System;
using System.Collections.Generic;

namespace Game.NomadWorkshop.Simulation
{
    /// <summary>现有存档兼容的有限路线端点；路线锚点目标通过独立身份字段表示。</summary>
    public enum NomadJourneyEndpoint { None, Origin, Destination }

    /// <summary>行驶与停车的领域原因；不把“有目标”误当成“有人实际驾驶”。</summary>
    public enum NomadJourneyStatus { NoDestination, AwaitingDriver, Moving, Arrived, FuelExhausted, Disposed }

    /// <summary>
    /// 路线上的稳定兴趣点进度。进度只属于宏观路线，不绑定 Unity 世界坐标；表现和停靠系统
    /// 可以据此把交互点放在车辆必经的路线段上。路线锚点必须按进度递增且身份唯一。
    /// </summary>
    public readonly struct NomadJourneyRouteAnchor
    {
        public NomadJourneyRouteAnchor(string id, int progressPermille)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("路线锚点身份不能为空。", nameof(id));
            if (progressPermille < 0 || progressPermille > 1000)
                throw new ArgumentOutOfRangeException(
                    nameof(progressPermille), "路线锚点进度必须在 0–1000‰ 之间。");

            Id = id;
            ProgressPermille = progressPermille;
        }

        public string Id { get; }
        public int ProgressPermille { get; }

        internal long ResolvePosition(long lengthMicrometers) =>
            checked((long)((decimal)lengthMicrometers * ProgressPermille / 1000m));
    }

    /// <summary>
    /// 连续旅途的纯规则运动参数。速度仍是路线的巡航速度；加速 / 制动为零时保留旧恒速语义，
    /// 非零时按整数纳米/毫秒积分并在到达前自动进入制动段。它不拥有 Unity 表现或输入。
    /// </summary>
    public readonly struct NomadJourneyMotionPolicy
    {
        public NomadJourneyMotionPolicy(
            int accelerationMillimetersPerSecondSquared,
            int brakingMillimetersPerSecondSquared)
        {
            if (accelerationMillimetersPerSecondSquared < 0)
                throw new ArgumentOutOfRangeException(nameof(accelerationMillimetersPerSecondSquared));
            if (brakingMillimetersPerSecondSquared < 0)
                throw new ArgumentOutOfRangeException(nameof(brakingMillimetersPerSecondSquared));
            if ((accelerationMillimetersPerSecondSquared == 0) !=
                (brakingMillimetersPerSecondSquared == 0))
                throw new ArgumentException("加速和制动必须同时为零或同时大于零。");

            AccelerationMillimetersPerSecondSquared = accelerationMillimetersPerSecondSquared;
            BrakingMillimetersPerSecondSquared = brakingMillimetersPerSecondSquared;
        }

        public int AccelerationMillimetersPerSecondSquared { get; }
        public int BrakingMillimetersPerSecondSquared { get; }
        public bool UsesSmoothing => AccelerationMillimetersPerSecondSquared > 0;

        public static NomadJourneyMotionPolicy ConstantSpeed => new(0, 0);
    }

    /// <summary>
    /// 有稳定身份的有限路线与固定行驶参数。速度为毫米 / 秒，油耗为纳升 / 毫米；
    /// 内部用微米和皮升精确积分，因此一毫秒也无需浮点取整。它不拥有地图或甲板表现。
    /// </summary>
    public sealed class NomadJourneyRoute
    {
        public NomadJourneyRoute(
            string id, string originId, string destinationId, long lengthMillimeters,
            int speedMillimetersPerSecond, int fuelNanolitersPerMillimeter)
            : this(id, originId, destinationId, lengthMillimeters,
                speedMillimetersPerSecond, fuelNanolitersPerMillimeter,
                Array.Empty<NomadJourneyRouteAnchor>())
        {
        }

        public NomadJourneyRoute(
            string id, string originId, string destinationId, long lengthMillimeters,
            int speedMillimetersPerSecond, int fuelNanolitersPerMillimeter,
            IReadOnlyList<NomadJourneyRouteAnchor> anchors)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("路线身份不能为空。", nameof(id));
            if (string.IsNullOrWhiteSpace(originId)) throw new ArgumentException("起点身份不能为空。", nameof(originId));
            if (string.IsNullOrWhiteSpace(destinationId) || destinationId == originId)
                throw new ArgumentException("终点身份须与起点不同且非空。", nameof(destinationId));
            if (lengthMillimeters <= 0) throw new ArgumentOutOfRangeException(nameof(lengthMillimeters));
            if (speedMillimetersPerSecond <= 0) throw new ArgumentOutOfRangeException(nameof(speedMillimetersPerSecond));
            if (fuelNanolitersPerMillimeter <= 0) throw new ArgumentOutOfRangeException(nameof(fuelNanolitersPerMillimeter));
            LengthMicrometers = checked(lengthMillimeters * 1000L);
            Id = id;
            OriginId = originId;
            DestinationId = destinationId;
            SpeedMillimetersPerSecond = speedMillimetersPerSecond;
            FuelNanolitersPerMillimeter = fuelNanolitersPerMillimeter;
            Anchors = CopyAndValidateAnchors(anchors, LengthMicrometers);
        }

        public string Id { get; }
        public string OriginId { get; }
        public string DestinationId { get; }
        public long LengthMicrometers { get; }
        public int SpeedMillimetersPerSecond { get; }
        public int FuelNanolitersPerMillimeter { get; }
        public IReadOnlyList<NomadJourneyRouteAnchor> Anchors { get; }

        public bool TryResolveAnchorPosition(string anchorId, out long positionMicrometers)
        {
            if (string.IsNullOrWhiteSpace(anchorId))
                throw new ArgumentException("路线锚点身份不能为空。", nameof(anchorId));
            for (var i = 0; i < Anchors.Count; i++)
            {
                NomadJourneyRouteAnchor anchor = Anchors[i];
                if (!string.Equals(anchor.Id, anchorId, StringComparison.Ordinal)) continue;
                positionMicrometers = anchor.ResolvePosition(LengthMicrometers);
                return true;
            }

            positionMicrometers = 0L;
            return false;
        }

        internal bool Matches(NomadJourneyRoute other) => other != null &&
            Id == other.Id && OriginId == other.OriginId && DestinationId == other.DestinationId &&
            LengthMicrometers == other.LengthMicrometers &&
            SpeedMillimetersPerSecond == other.SpeedMillimetersPerSecond &&
            FuelNanolitersPerMillimeter == other.FuelNanolitersPerMillimeter;

        private static IReadOnlyList<NomadJourneyRouteAnchor> CopyAndValidateAnchors(
            IReadOnlyList<NomadJourneyRouteAnchor> anchors,
            long lengthMicrometers)
        {
            if (anchors == null || anchors.Count == 0)
                return Array.Empty<NomadJourneyRouteAnchor>();

            var copy = new NomadJourneyRouteAnchor[anchors.Count];
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var previousProgress = -1;
            for (var i = 0; i < anchors.Count; i++)
            {
                NomadJourneyRouteAnchor anchor = anchors[i];
                if (!ids.Add(anchor.Id))
                    throw new ArgumentException("路线锚点身份不能重复。", nameof(anchors));
                if (anchor.ProgressPermille < previousProgress)
                    throw new ArgumentException("路线锚点必须按进度递增排列。", nameof(anchors));
                _ = anchor.ResolvePosition(lengthMicrometers);
                copy[i] = anchor;
                previousProgress = anchor.ProgressPermille;
            }

            return copy;
        }
    }

    /// <summary>
    /// 实验的不可变内存快照，包含路线契约、位置、燃料、端点方向和可选锚点目标；刻意不包含驾驶租约。
    /// 它不是正式游戏存档 DTO，接入产品存储时须另行定义版本与迁移。
    /// </summary>
    public readonly struct NomadJourneySnapshot
    {
        public NomadJourneySnapshot(
            NomadJourneyRoute route, long positionMicrometers, long fuelPicoliters,
            NomadJourneyEndpoint destination)
            : this(route, positionMicrometers, fuelPicoliters, destination, string.Empty, 0L, 0L)
        {
        }

        public NomadJourneySnapshot(
            NomadJourneyRoute route, long positionMicrometers, long fuelPicoliters,
            NomadJourneyEndpoint destination,
            string destinationAnchorId,
            long currentSpeedNanometersPerMillisecond,
            long distanceRemainderHalfNanometers)
        {
            Route = route;
            PositionMicrometers = positionMicrometers;
            FuelPicoliters = fuelPicoliters;
            Destination = destination;
            DestinationAnchorId = destinationAnchorId ?? string.Empty;
            CurrentSpeedNanometersPerMillisecond = currentSpeedNanometersPerMillisecond;
            DistanceRemainderHalfNanometers = distanceRemainderHalfNanometers;
        }

        public NomadJourneyRoute Route { get; }
        public long PositionMicrometers { get; }
        public long FuelPicoliters { get; }
        public NomadJourneyEndpoint Destination { get; }
        public string DestinationAnchorId { get; }
        public long CurrentSpeedNanometersPerMillisecond { get; }
        public long DistanceRemainderHalfNanometers { get; }
    }

    /// <summary>
    /// 旅途的单线程规则 owner。宿主用统一模拟时间推进，居民执行 owner 持有唯一驾驶租约；
    /// 没有有效租约就不移动、不耗油，也不累积待补执行的行驶时间。Session 不引用 Unity 或 Framework。
    /// 更换目标、恢复与 Dispose 会撤销旧租约；恢复失败不改变当前世界。读状态在 Dispose 后仍可诊断，写操作抛 ODE。
    /// </summary>
    public sealed class NomadJourneySession : IDisposable
    {
        private const long DistanceDenominatorHalfNanometers = 2_000L;
        private NomadDriverLease _driver;
        private long _currentSpeedNanometersPerMillisecond;
        private long _distanceRemainderHalfNanometers;
        private bool _disposed;

        public NomadJourneySession(NomadJourneyRoute route, long fuelPicoliters)
            : this(route, fuelPicoliters, NomadJourneyMotionPolicy.ConstantSpeed)
        {
        }

        public NomadJourneySession(
            NomadJourneyRoute route,
            long fuelPicoliters,
            NomadJourneyMotionPolicy motionPolicy)
        {
            Route = route ?? throw new ArgumentNullException(nameof(route));
            if (fuelPicoliters < 0) throw new ArgumentOutOfRangeException(nameof(fuelPicoliters));
            MotionPolicy = motionPolicy;
            FuelPicoliters = fuelPicoliters;
        }

        public NomadJourneyRoute Route { get; }
        public NomadJourneyMotionPolicy MotionPolicy { get; }
        public long PositionMicrometers { get; private set; }
        public long FuelPicoliters { get; private set; }
        public NomadJourneyEndpoint Destination { get; private set; }
        public long CurrentSpeedNanometersPerMillisecond => _currentSpeedNanometersPerMillisecond;
        /// <summary>
        /// Session 内的目标版本。执行 owner 开始前往驾驶岗位时捕获，到岗时原样提交；
        /// 即使取消后选回同一地点或读取同一快照也会变化，不随世界快照回退。
        /// </summary>
        public long DestinationRevision { get; private set; }
        public string DriverId => _driver?.ResidentId ?? string.Empty;
        public string DestinationAnchorId { get; private set; } = string.Empty;
        public long DestinationPositionMicrometers => ResolveDestinationPosition();
        private long DestinationPosition => ResolveDestinationPosition();

        public NomadJourneyStatus Status => _disposed ? NomadJourneyStatus.Disposed :
            Destination == NomadJourneyEndpoint.None ? NomadJourneyStatus.NoDestination :
            PositionMicrometers == DestinationPosition ? NomadJourneyStatus.Arrived :
            FuelPicoliters < Route.FuelNanolitersPerMillimeter ? NomadJourneyStatus.FuelExhausted :
            _driver == null ? NomadJourneyStatus.AwaitingDriver : NomadJourneyStatus.Moving;

        /// <summary>设定或取消目标；相同意图幂等，改变意图立即停车，保留途中位置和燃料。</summary>
        public void SetDestination(NomadJourneyEndpoint destination)
        {
            ThrowIfDisposed();
            ValidateDestination(destination);
            if (destination == Destination && string.IsNullOrEmpty(DestinationAnchorId)) return;
            long nextRevision = checked(DestinationRevision + 1L);
            RevokeDriver();
            ResetMotion();
            Destination = destination;
            DestinationAnchorId = string.Empty;
            DestinationRevision = nextRevision;
        }

        /// <summary>
        /// 把目标设为路线上的稳定锚点。锚点只描述路线进度，不创建第二套坐标或移动系统；
        /// 方向由当前位置与锚点位置决定，抵达后仍由同一驾驶租约和到站语义收口。
        /// </summary>
        public void SetAnchorDestination(string anchorId)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(anchorId))
                throw new ArgumentException("路线锚点身份不能为空。", nameof(anchorId));
            if (!Route.TryResolveAnchorPosition(anchorId, out long targetPosition))
                throw new ArgumentException("路线不存在指定锚点。", nameof(anchorId));
            if (string.Equals(DestinationAnchorId, anchorId, StringComparison.Ordinal)) return;

            long nextRevision = checked(DestinationRevision + 1L);
            RevokeDriver();
            ResetMotion();
            Destination = targetPosition < PositionMicrometers
                ? NomadJourneyEndpoint.Origin
                : NomadJourneyEndpoint.Destination;
            DestinationAnchorId = anchorId;
            DestinationRevision = nextRevision;
        }

        /// <summary>
        /// 由已经实际到岗的居民执行 owner 领取独占租约，并登记到自己的生命周期 Bag。
        /// 必须提交开始前往岗位时捕获的目标版本；迟到旧任务、无目标、已到达、缺油或已有驾驶员
        /// 返回 false。不要在到岗时重新查询版本替换旧值，否则会把旧行动冒充为新意图。
        /// </summary>
        public bool TryAcquireDriver(string residentId, long expectedDestinationRevision, out NomadDriverLease lease)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(residentId)) throw new ArgumentException("居民身份不能为空。", nameof(residentId));
            lease = null;
            if (expectedDestinationRevision != DestinationRevision || Status != NomadJourneyStatus.AwaitingDriver) return false;
            if (!MotionPolicy.UsesSmoothing)
                _currentSpeedNanometersPerMillisecond = CruiseSpeedNanometersPerMillisecond;
            else if (_currentSpeedNanometersPerMillisecond > CruiseSpeedNanometersPerMillisecond)
                _currentSpeedNanometersPerMillisecond = CruiseSpeedNanometersPerMillisecond;
            lease = _driver = new NomadDriverLease(this, residentId);
            return true;
        }

        /// <summary>
        /// 消费调用方已提交的非负模拟毫秒。返回真实移动的微米；到达或燃料不足时精确截断，
        /// 多余时间不会预扣燃料或留给下一目标。参数乘积溢出时原子拒绝。
        /// </summary>
        public long Advance(long deltaMilliseconds)
        {
            ThrowIfDisposed();
            if (deltaMilliseconds < 0) throw new ArgumentOutOfRangeException(nameof(deltaMilliseconds));
            if (Status != NomadJourneyStatus.Moving || deltaMilliseconds == 0) return 0L;
            // 保留旧入口的溢出语义；平滑积分内部使用纳米/毫秒，但同样先拒绝不可能的超长步。
            _ = checked((long)Route.SpeedMillimetersPerSecond * deltaMilliseconds);
            return MotionPolicy.UsesSmoothing
                ? AdvanceWithSmoothing(deltaMilliseconds)
                : AdvanceAtConstantSpeed(deltaMilliseconds);
        }

        /// <summary>平滑策略的加速 / 制动积分；内部决策量子为 1 ms，巡航段可整段跳过。</summary>
        private long AdvanceWithSmoothing(long deltaMilliseconds)
        {
            long movedTotal = 0L;
            long remainingMilliseconds = deltaMilliseconds;
            while (remainingMilliseconds > 0L && Status == NomadJourneyStatus.Moving)
            {
                long remainingDistance = Math.Abs(DestinationPosition - PositionMicrometers);
                long stoppingDistance = ResolveStoppingDistanceMicrometers(
                    _currentSpeedNanometersPerMillisecond);
                long acceleration = remainingDistance <= stoppingDistance &&
                                    _currentSpeedNanometersPerMillisecond > 0L
                    ? -MotionPolicy.BrakingMillimetersPerSecondSquared
                    : _currentSpeedNanometersPerMillisecond < CruiseSpeedNanometersPerMillisecond
                        ? MotionPolicy.AccelerationMillimetersPerSecondSquared
                        : 0L;
                long stepMilliseconds = acceleration == 0L
                    ? ResolveCruiseStepMilliseconds(remainingDistance, stoppingDistance, remainingMilliseconds)
                    : Math.Min(1L, remainingMilliseconds);
                movedTotal = checked(movedTotal + IntegrateMotion(stepMilliseconds, acceleration));
                remainingMilliseconds -= stepMilliseconds;
            }

            return movedTotal;
        }

        private long AdvanceAtConstantSpeed(long deltaMilliseconds)
        {
            // (mm/s) × ms = µm；(nL/mm) × µm = pL，避免分帧丢余量。
            long requested = checked(Route.SpeedMillimetersPerSecond * deltaMilliseconds);
            long remaining = Math.Abs(DestinationPosition - PositionMicrometers);
            long affordable = FuelPicoliters / Route.FuelNanolitersPerMillimeter;
            long distance = Math.Min(requested, Math.Min(remaining, affordable));
            FuelPicoliters -= distance * Route.FuelNanolitersPerMillimeter;
            PositionMicrometers += DestinationPosition > PositionMicrometers ? distance : -distance;
            _currentSpeedNanometersPerMillisecond = CruiseSpeedNanometersPerMillisecond;
            if (Status is NomadJourneyStatus.Arrived or NomadJourneyStatus.FuelExhausted) RevokeDriver();
            return distance;
        }

        /// <summary>捕获已执行的世界状态；保存本身不打断当前驾驶员。</summary>
        public NomadJourneySnapshot Capture() => new(
            Route,
            PositionMicrometers,
            FuelPicoliters,
            Destination,
            DestinationAnchorId,
            _currentSpeedNanometersPerMillisecond,
            _distanceRemainderHalfNanometers);

        /// <summary>完整验证后恢复并撤销驾驶租约；新 owner 重新到岗前不会自动续驶。</summary>
        public void Restore(in NomadJourneySnapshot snapshot)
        {
            ThrowIfDisposed();
            if (!Route.Matches(snapshot.Route)) throw new ArgumentException("快照路线身份或行驶参数不匹配。", nameof(snapshot));
            if (snapshot.PositionMicrometers < 0 || snapshot.PositionMicrometers > Route.LengthMicrometers ||
                snapshot.FuelPicoliters < 0 ||
                snapshot.CurrentSpeedNanometersPerMillisecond < 0L ||
                snapshot.CurrentSpeedNanometersPerMillisecond > CruiseSpeedNanometersPerMillisecond ||
                snapshot.DistanceRemainderHalfNanometers < 0L ||
                snapshot.DistanceRemainderHalfNanometers >= DistanceDenominatorHalfNanometers)
                throw new ArgumentOutOfRangeException(nameof(snapshot));
            ValidateDestination(snapshot.Destination);
            if (!string.IsNullOrEmpty(snapshot.DestinationAnchorId))
            {
                if (snapshot.Destination == NomadJourneyEndpoint.None ||
                    !Route.TryResolveAnchorPosition(snapshot.DestinationAnchorId, out long targetPosition))
                    throw new ArgumentException("快照路线锚点目标无效。", nameof(snapshot));
                if ((targetPosition < snapshot.PositionMicrometers &&
                     snapshot.Destination != NomadJourneyEndpoint.Origin) ||
                    (targetPosition > snapshot.PositionMicrometers &&
                     snapshot.Destination != NomadJourneyEndpoint.Destination))
                    throw new ArgumentException("快照锚点目标方向与位置不一致。", nameof(snapshot));
            }
            long nextRevision = checked(DestinationRevision + 1L);
            RevokeDriver();
            PositionMicrometers = snapshot.PositionMicrometers;
            FuelPicoliters = snapshot.FuelPicoliters;
            Destination = snapshot.Destination;
            DestinationAnchorId = snapshot.DestinationAnchorId ?? string.Empty;
            _currentSpeedNanometersPerMillisecond = snapshot.CurrentSpeedNanometersPerMillisecond;
            _distanceRemainderHalfNanometers = snapshot.DistanceRemainderHalfNanometers;
            DestinationRevision = nextRevision;
        }

        /// <summary>幂等撤销岗位并关闭 session；旧租约迟到释放不能改变后续 session。</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            RevokeDriver();
        }

        private void RevokeDriver()
        {
            _driver?.Detach();
            _driver = null;
        }

        private long CruiseSpeedNanometersPerMillisecond =>
            checked((long)Route.SpeedMillimetersPerSecond * 1000L);

        private long ResolveDestinationPosition()
        {
            if (!string.IsNullOrEmpty(DestinationAnchorId))
            {
                if (!Route.TryResolveAnchorPosition(DestinationAnchorId, out long position))
                    throw new InvalidOperationException("当前路线缺少已保存的目标锚点。");
                return position;
            }

            return Destination == NomadJourneyEndpoint.Origin ? 0L : Route.LengthMicrometers;
        }

        private long IntegrateMotion(long deltaMilliseconds, long acceleration)
        {
            long before = _currentSpeedNanometersPerMillisecond;
            long after = before;
            if (acceleration > 0L)
                after = Math.Min(CruiseSpeedNanometersPerMillisecond,
                    checked(before + acceleration * deltaMilliseconds));
            else if (acceleration < 0L)
                after = Math.Max(0L, checked(before + acceleration * deltaMilliseconds));

            long halfNanometers = checked((before + after) * deltaMilliseconds);
            long totalHalfNanometers = checked(halfNanometers + _distanceRemainderHalfNanometers);
            long distanceMicrometers = totalHalfNanometers / DistanceDenominatorHalfNanometers;
            _distanceRemainderHalfNanometers = totalHalfNanometers % DistanceDenominatorHalfNanometers;

            long remaining = Math.Abs(DestinationPosition - PositionMicrometers);
            long affordable = FuelPicoliters / Route.FuelNanolitersPerMillimeter;
            long distance = Math.Min(distanceMicrometers, Math.Min(remaining, affordable));
            FuelPicoliters -= checked(distance * Route.FuelNanolitersPerMillimeter);
            PositionMicrometers += DestinationPosition > PositionMicrometers ? distance : -distance;
            _currentSpeedNanometersPerMillisecond = after;

            if (distance < distanceMicrometers ||
                Status is NomadJourneyStatus.Arrived or NomadJourneyStatus.FuelExhausted)
            {
                ResetMotion();
                if (Status is NomadJourneyStatus.Arrived or NomadJourneyStatus.FuelExhausted)
                    RevokeDriver();
            }

            return distance;
        }

        private long ResolveCruiseStepMilliseconds(
            long remainingDistance,
            long stoppingDistance,
            long remainingMilliseconds)
        {
            if (remainingDistance <= stoppingDistance) return 1L;
            long distanceBeforeBrake = remainingDistance - stoppingDistance;
            long cruiseMilliseconds = checked(
                distanceBeforeBrake * 1000L /
                Math.Max(1L, _currentSpeedNanometersPerMillisecond));
            return Math.Min(remainingMilliseconds, Math.Max(1L, cruiseMilliseconds));
        }

        private long ResolveStoppingDistanceMicrometers(long speedNanometersPerMillisecond)
        {
            if (speedNanometersPerMillisecond <= 0L) return 0L;
            decimal numerator = (decimal)speedNanometersPerMillisecond * speedNanometersPerMillisecond;
            decimal denominator = 2m * MotionPolicy.BrakingMillimetersPerSecondSquared * 1000m;
            return checked((long)Math.Ceiling(numerator / denominator));
        }

        private void ResetMotion()
        {
            _currentSpeedNanometersPerMillisecond = 0L;
            _distanceRemainderHalfNanometers = 0L;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(NomadJourneySession));
        }

        private static void ValidateDestination(NomadJourneyEndpoint destination)
        {
            if (destination != NomadJourneyEndpoint.None && destination != NomadJourneyEndpoint.Origin &&
                destination != NomadJourneyEndpoint.Destination)
                throw new ArgumentOutOfRangeException(nameof(destination));
        }

        /// <summary>
        /// 一次到岗的可释放权利。属于居民行动生命周期；Dispose 立即停车且幂等。
        /// 目标变化或恢复会令 IsActive 变为 false，迟到 Dispose 不会撤销新租约。
        /// </summary>
        public sealed class NomadDriverLease : IDisposable
        {
            private NomadJourneySession _session;
            internal NomadDriverLease(NomadJourneySession session, string residentId)
            {
                _session = session;
                ResidentId = residentId;
            }
            public string ResidentId { get; }
            public bool IsActive => _session != null && ReferenceEquals(_session._driver, this);
            internal void Detach() => _session = null;
            public void Dispose()
            {
                if (IsActive) _session._driver = null;
                Detach();
            }
        }
    }
}
