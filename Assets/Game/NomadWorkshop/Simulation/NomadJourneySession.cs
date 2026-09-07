using System;

namespace Game.NomadWorkshop.Simulation
{
    /// <summary>当前实验仅允许在一条有限路线的两个端点之间设定目标。</summary>
    public enum NomadJourneyEndpoint { None, Origin, Destination }

    /// <summary>行驶与停车的领域原因；不把“有目标”误当成“有人实际驾驶”。</summary>
    public enum NomadJourneyStatus { NoDestination, AwaitingDriver, Moving, Arrived, FuelExhausted, Disposed }

    /// <summary>
    /// 有稳定身份的有限路线与固定行驶参数。速度为毫米 / 秒，油耗为纳升 / 毫米；
    /// 内部用微米和皮升精确积分，因此一毫秒也无需浮点取整。它不拥有地图或甲板表现。
    /// </summary>
    public sealed class NomadJourneyRoute
    {
        public NomadJourneyRoute(
            string id, string originId, string destinationId, long lengthMillimeters,
            int speedMillimetersPerSecond, int fuelNanolitersPerMillimeter)
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
        }

        public string Id { get; }
        public string OriginId { get; }
        public string DestinationId { get; }
        public long LengthMicrometers { get; }
        public int SpeedMillimetersPerSecond { get; }
        public int FuelNanolitersPerMillimeter { get; }

        internal bool Matches(NomadJourneyRoute other) => other != null &&
            Id == other.Id && OriginId == other.OriginId && DestinationId == other.DestinationId &&
            LengthMicrometers == other.LengthMicrometers &&
            SpeedMillimetersPerSecond == other.SpeedMillimetersPerSecond &&
            FuelNanolitersPerMillimeter == other.FuelNanolitersPerMillimeter;
    }

    /// <summary>
    /// 实验的不可变内存快照，包含路线契约、位置、燃料和玩家目标；刻意不包含驾驶租约。
    /// 它不是正式游戏存档 DTO，接入产品存储时须另行定义版本与迁移。
    /// </summary>
    public readonly struct NomadJourneySnapshot
    {
        public NomadJourneySnapshot(
            NomadJourneyRoute route, long positionMicrometers, long fuelPicoliters,
            NomadJourneyEndpoint destination)
        {
            Route = route;
            PositionMicrometers = positionMicrometers;
            FuelPicoliters = fuelPicoliters;
            Destination = destination;
        }

        public NomadJourneyRoute Route { get; }
        public long PositionMicrometers { get; }
        public long FuelPicoliters { get; }
        public NomadJourneyEndpoint Destination { get; }
    }

    /// <summary>
    /// 旅途的单线程规则 owner。宿主用统一模拟时间推进，居民执行 owner 持有唯一驾驶租约；
    /// 没有有效租约就不移动、不耗油，也不累积待补执行的行驶时间。Session 不引用 Unity 或 Framework。
    /// 更换目标、恢复与 Dispose 会撤销旧租约；恢复失败不改变当前世界。读状态在 Dispose 后仍可诊断，写操作抛 ODE。
    /// </summary>
    public sealed class NomadJourneySession : IDisposable
    {
        private NomadDriverLease _driver;
        private bool _disposed;

        public NomadJourneySession(NomadJourneyRoute route, long fuelPicoliters)
        {
            Route = route ?? throw new ArgumentNullException(nameof(route));
            if (fuelPicoliters < 0) throw new ArgumentOutOfRangeException(nameof(fuelPicoliters));
            FuelPicoliters = fuelPicoliters;
        }

        public NomadJourneyRoute Route { get; }
        public long PositionMicrometers { get; private set; }
        public long FuelPicoliters { get; private set; }
        public NomadJourneyEndpoint Destination { get; private set; }
        /// <summary>
        /// Session 内的目标版本。执行 owner 开始前往驾驶岗位时捕获，到岗时原样提交；
        /// 即使取消后选回同一地点或读取同一快照也会变化，不随世界快照回退。
        /// </summary>
        public long DestinationRevision { get; private set; }
        public string DriverId => _driver?.ResidentId ?? string.Empty;
        private long DestinationPosition => Destination == NomadJourneyEndpoint.Origin ? 0L : Route.LengthMicrometers;

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
            if (destination == Destination) return;
            long nextRevision = checked(DestinationRevision + 1L);
            RevokeDriver();
            Destination = destination;
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
            // (mm/s) × ms = µm；(nL/mm) × µm = pL，避免分帧丢余量。
            long requested = checked(Route.SpeedMillimetersPerSecond * deltaMilliseconds);
            long remaining = Math.Abs(DestinationPosition - PositionMicrometers);
            long affordable = FuelPicoliters / Route.FuelNanolitersPerMillimeter;
            long distance = Math.Min(requested, Math.Min(remaining, affordable));
            FuelPicoliters -= distance * Route.FuelNanolitersPerMillimeter;
            PositionMicrometers += DestinationPosition > PositionMicrometers ? distance : -distance;
            if (Status is NomadJourneyStatus.Arrived or NomadJourneyStatus.FuelExhausted) RevokeDriver();
            return distance;
        }

        /// <summary>捕获已执行的世界状态；保存本身不打断当前驾驶员。</summary>
        public NomadJourneySnapshot Capture() => new(Route, PositionMicrometers, FuelPicoliters, Destination);

        /// <summary>完整验证后恢复并撤销驾驶租约；新 owner 重新到岗前不会自动续驶。</summary>
        public void Restore(in NomadJourneySnapshot snapshot)
        {
            ThrowIfDisposed();
            if (!Route.Matches(snapshot.Route)) throw new ArgumentException("快照路线身份或行驶参数不匹配。", nameof(snapshot));
            if (snapshot.PositionMicrometers < 0 || snapshot.PositionMicrometers > Route.LengthMicrometers ||
                snapshot.FuelPicoliters < 0)
                throw new ArgumentOutOfRangeException(nameof(snapshot));
            ValidateDestination(snapshot.Destination);
            long nextRevision = checked(DestinationRevision + 1L);
            RevokeDriver();
            PositionMicrometers = snapshot.PositionMicrometers;
            FuelPicoliters = snapshot.FuelPicoliters;
            Destination = snapshot.Destination;
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
