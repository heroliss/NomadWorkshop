using System;

namespace Game.NomadWorkshop.Simulation
{
    /// <summary>
    /// 水循环的最小运行检查点。库存仍以整数 mL 保存；尚未提交成 1 mL 转移的连续代谢量以 nL
    /// 量化，避免保存 / 加载或不同帧步长让小数余量凭空消失。
    /// </summary>
    public readonly struct ResidentWaterCycleCheckpoint
    {
        public ResidentWaterCycleCheckpoint(
            float thirst,
            int bodyWaterMilliliters,
            int bladderWasteMilliliters,
            long pendingMetabolismNanoliters,
            int metabolismSequence)
        {
            if (!float.IsFinite(thirst) || thirst < 0f || thirst > 1f)
                throw new ArgumentOutOfRangeException(nameof(thirst));
            if (bodyWaterMilliliters < 0)
                throw new ArgumentOutOfRangeException(nameof(bodyWaterMilliliters));
            if (bladderWasteMilliliters < 0)
                throw new ArgumentOutOfRangeException(nameof(bladderWasteMilliliters));
            if (pendingMetabolismNanoliters < 0)
                throw new ArgumentOutOfRangeException(nameof(pendingMetabolismNanoliters));
            if (metabolismSequence < 0)
                throw new ArgumentOutOfRangeException(nameof(metabolismSequence));

            Thirst = thirst;
            BodyWaterMilliliters = bodyWaterMilliliters;
            BladderWasteMilliliters = bladderWasteMilliliters;
            PendingMetabolismNanoliters = pendingMetabolismNanoliters;
            MetabolismSequence = metabolismSequence;
        }

        public float Thirst { get; }
        public int BodyWaterMilliliters { get; }
        public int BladderWasteMilliliters { get; }
        public long PendingMetabolismNanoliters { get; }
        public int MetabolismSequence { get; }
    }

    /// <summary>一次生理推进转化的真实毫升数与首个阻塞原因。</summary>
    public readonly struct ResidentWaterCycleTick
    {
        public ResidentWaterCycleTick(int metabolizedMilliliters, ResourceFlowBlocker blocker)
        {
            if (metabolizedMilliliters < 0)
                throw new ArgumentOutOfRangeException(nameof(metabolizedMilliliters));
            MetabolizedMilliliters = metabolizedMilliliters;
            Blocker = blocker;
        }

        public int MetabolizedMilliliters { get; }
        public ResourceFlowBlocker Blocker { get; }
    }

    /// <summary>
    /// 一位居民的最小饮水与排泄物质链。口渴是连续需求，摄入水和膀胱内容物以整数 mL 保存；
    /// 喝水、代谢和如厕均借用同一资源账本，因而取消或容量不足时不会吞掉物质。
    /// </summary>
    public sealed class ResidentWaterCycle
    {
        public const int DefaultDrinkServingMilliliters = 300;
        public const int DefaultBodyWaterCapacityMilliliters = 900;
        public const int DefaultBladderCapacityMilliliters = 500;

        private readonly string _id;
        private readonly ulong _ownerId;
        private readonly double _metabolismMillilitersPerSecond;
        private readonly int _drinkServingMilliliters;
        private readonly float _thirstIncreasePerSecond;
        private readonly float _thirstReliefPerServing;
        private double _pendingMetabolismMilliliters;
        private int _metabolismSequence;

        public ResidentWaterCycle(
            string id,
            ulong ownerId,
            float metabolismMillilitersPerSecond,
            int drinkServingMilliliters = DefaultDrinkServingMilliliters,
            float initialThirst = 0.72f,
            float thirstIncreasePerSecond = 0.004f,
            float thirstReliefPerServing = 0.72f,
            int bodyWaterCapacityMilliliters = DefaultBodyWaterCapacityMilliliters,
            int bladderCapacityMilliliters = DefaultBladderCapacityMilliliters,
            ResidentWaterCycleCheckpoint? checkpoint = null)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("居民水循环 id 不能为空。", nameof(id));
            if (metabolismMillilitersPerSecond <= 0f)
                throw new ArgumentOutOfRangeException(
                    nameof(metabolismMillilitersPerSecond),
                    "每秒代谢毫升数必须大于零。");
            if (drinkServingMilliliters <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(drinkServingMilliliters),
                    "一份饮水的毫升数必须大于零。");
            if (initialThirst < 0f || initialThirst > 1f)
                throw new ArgumentOutOfRangeException(nameof(initialThirst), "初始口渴必须位于 0 到 1。 ");
            if (thirstIncreasePerSecond < 0f)
                throw new ArgumentOutOfRangeException(nameof(thirstIncreasePerSecond));
            if (thirstReliefPerServing <= 0f || thirstReliefPerServing > 1f)
                throw new ArgumentOutOfRangeException(nameof(thirstReliefPerServing));
            if (bodyWaterCapacityMilliliters <= 0)
                throw new ArgumentOutOfRangeException(nameof(bodyWaterCapacityMilliliters));
            if (bladderCapacityMilliliters <= 0)
                throw new ArgumentOutOfRangeException(nameof(bladderCapacityMilliliters));

            _id = id.Trim();
            _ownerId = ownerId;
            _metabolismMillilitersPerSecond = metabolismMillilitersPerSecond;
            _drinkServingMilliliters = drinkServingMilliliters;
            _thirstIncreasePerSecond = thirstIncreasePerSecond;
            _thirstReliefPerServing = thirstReliefPerServing;
            ResidentWaterCycleCheckpoint restored = checkpoint ??
                new ResidentWaterCycleCheckpoint(initialThirst, 0, 0, 0L, 0);
            if (restored.BodyWaterMilliliters > bodyWaterCapacityMilliliters)
                throw new ArgumentOutOfRangeException(
                    nameof(checkpoint),
                    "检查点中的体内水超过当前容量。");
            if (restored.BladderWasteMilliliters > bladderCapacityMilliliters)
                throw new ArgumentOutOfRangeException(
                    nameof(checkpoint),
                    "检查点中的膀胱内容物超过当前容量。");
            long maximumPendingNanoliters =
                checked((long)restored.BodyWaterMilliliters * 1_000_000L);
            if (restored.PendingMetabolismNanoliters > maximumPendingNanoliters)
                throw new ArgumentOutOfRangeException(
                    nameof(checkpoint),
                    "待提交代谢量不能超过体内仍存在的水量。");

            Thirst = restored.Thirst;
            BodyWater = new ResourceInventory(
                $"{_id}:body-water",
                ResourceMeasure.Milliliter,
                bodyWaterCapacityMilliliters,
                CreateInitialContents(
                    NomadResourceIds.Water,
                    restored.BodyWaterMilliliters));
            Bladder = new ResourceInventory(
                $"{_id}:bladder",
                ResourceMeasure.Milliliter,
                bladderCapacityMilliliters,
                CreateInitialContents(
                    NomadResourceIds.HumanWaste,
                    restored.BladderWasteMilliliters));
            _pendingMetabolismMilliliters =
                restored.PendingMetabolismNanoliters / 1_000_000d;
            _metabolismSequence = restored.MetabolismSequence;
        }

        /// <summary>0 表示不渴，1 表示口渴达到当前原型上限。</summary>
        public float Thirst { get; private set; }

        /// <summary>
        /// 0 到 1 的排泄压力，直接由膀胱内真实 mL 与容量计算。代谢本身按 mL 连续形成内容物，
        /// 因而无需再把尚在体内的水重复折算成一份虚拟压力。
        /// </summary>
        public float ExcretionPressure
        {
            get
            {
                return Math.Min(1f, Bladder.TotalAmount / (float)Bladder.Capacity);
            }
        }

        /// <summary>已经喝下但尚未代谢的水；它不是可供其他居民领取的公共库存。</summary>
        public ResourceInventory BodyWater { get; }

        /// <summary>已形成且需要通过厕所排出的真实内容物。</summary>
        public ResourceInventory Bladder { get; }

        /// <summary>当前玩法参数下每个模拟秒最多转化的水量。</summary>
        public double MetabolismMillilitersPerSecond => _metabolismMillilitersPerSecond;

        /// <summary>捕获库存、口渴和连续代谢余量；不包含任何运行时资源预留。</summary>
        public ResidentWaterCycleCheckpoint CaptureCheckpoint()
        {
            double scaled = _pendingMetabolismMilliliters * 1_000_000d;
            if (scaled > long.MaxValue)
                throw new OverflowException("待提交代谢量超过可保存范围。");
            long pendingNanoliters = Math.Max(
                0L,
                (long)Math.Round(scaled, MidpointRounding.AwayFromZero));
            return new ResidentWaterCycleCheckpoint(
                Thirst,
                BodyWater.GetAmount(NomadResourceIds.Water),
                Bladder.GetAmount(NomadResourceIds.HumanWaste),
                pendingNanoliters,
                _metabolismSequence);
        }

        /// <summary>预留一次喝水行动；只有提交才会同时转移水并缓解口渴。</summary>
        public bool TryReserveDrink(
            ResourceFlowLedger ledger,
            ResourceInventory drinkingSource,
            string taskId,
            string interactionKey,
            int amount,
            out ResidentWaterActionLease lease,
            out ResourceFlowBlocker blocker)
        {
            if (ledger == null) throw new ArgumentNullException(nameof(ledger));
            if (drinkingSource == null) throw new ArgumentNullException(nameof(drinkingSource));
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));

            var request = new ProcessTaskRequest(
                taskId,
                _ownerId,
                "居民饮水并把水转入体内",
                new[]
                {
                    new InventoryResourceQuantity(drinkingSource, NomadResourceIds.Water, amount),
                },
                new[]
                {
                    new InventoryResourceQuantity(BodyWater, NomadResourceIds.Water, amount),
                },
                new[] { interactionKey });

            if (!ledger.TryReserveProcess(request, out ProcessTaskLease process, out blocker))
            {
                lease = null;
                return false;
            }

            lease = new ResidentWaterActionLease(
                process,
                () =>
                {
                    float servingRatio = amount / (float)_drinkServingMilliliters;
                    Thirst = Math.Max(0f, Thirst - _thirstReliefPerServing * servingRatio);
                });
            return true;
        }

        /// <summary>预留一次如厕行动；提交时才把膀胱内容物转入厕所暂存桶。</summary>
        public bool TryReserveToiletUse(
            ResourceFlowLedger ledger,
            ResourceInventory toiletHolding,
            string taskId,
            string interactionKey,
            int amount,
            out ResidentWaterActionLease lease,
            out ResourceFlowBlocker blocker)
        {
            if (ledger == null) throw new ArgumentNullException(nameof(ledger));
            if (toiletHolding == null) throw new ArgumentNullException(nameof(toiletHolding));
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));

            var request = new ProcessTaskRequest(
                taskId,
                _ownerId,
                "居民如厕并把排泄物留在厕所暂存桶",
                new[]
                {
                    new InventoryResourceQuantity(Bladder, NomadResourceIds.HumanWaste, amount),
                },
                new[]
                {
                    new InventoryResourceQuantity(toiletHolding, NomadResourceIds.HumanWaste, amount),
                },
                new[] { interactionKey });

            if (!ledger.TryReserveProcess(request, out ProcessTaskLease process, out blocker))
            {
                lease = null;
                return false;
            }

            lease = new ResidentWaterActionLease(process);
            return true;
        }

        /// <summary>
        /// 推进连续口渴和水代谢。速率先累积为不足 1 mL 的小数余量，再以整数 mL 原子转移；
        /// 因而小步更新不会丢量；<see cref="CaptureCheckpoint"/> 会把余量量化为 nL 随运行检查点保存。
        /// 当前 Foundation 为守恒验证采用
        /// 1 mL 摄入水 → 1 mL 排泄物，未来呼吸、汗液等损失应作为显式去向加入，不能偷偷乘系数消失。
        /// </summary>
        public ResidentWaterCycleTick Advance(float deltaSeconds, ResourceFlowLedger ledger)
        {
            if (deltaSeconds < 0f) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            if (ledger == null) throw new ArgumentNullException(nameof(ledger));

            Thirst = Math.Min(1f, Thirst + deltaSeconds * _thirstIncreasePerSecond);
            int bodyWaterMilliliters = BodyWater.GetAmount(NomadResourceIds.Water);
            if (bodyWaterMilliliters <= 0)
            {
                _pendingMetabolismMilliliters = 0d;
                return new ResidentWaterCycleTick(0, ResourceFlowBlocker.None);
            }

            _pendingMetabolismMilliliters = Math.Min(
                bodyWaterMilliliters,
                _pendingMetabolismMilliliters + deltaSeconds * _metabolismMillilitersPerSecond);
            int readyMilliliters = Math.Min(
                bodyWaterMilliliters,
                (int)Math.Floor(_pendingMetabolismMilliliters));
            if (readyMilliliters <= 0)
                return new ResidentWaterCycleTick(0, ResourceFlowBlocker.None);

            int transferableMilliliters = Math.Min(readyMilliliters, Bladder.FreeCapacity);
            if (transferableMilliliters <= 0)
            {
                return new ResidentWaterCycleTick(
                    0,
                    new ResourceFlowBlocker(
                        ResourceFlowBlockReason.DestinationFull,
                        Bladder.Id,
                        NomadResourceIds.HumanWaste));
            }

            var request = new ProcessTaskRequest(
                $"{_id}:metabolism:{_metabolismSequence + 1}",
                _ownerId,
                "按毫升把摄入水转为需要排出的真实物质",
                new[]
                {
                    new InventoryResourceQuantity(
                        BodyWater,
                        NomadResourceIds.Water,
                        transferableMilliliters),
                },
                new[]
                {
                    new InventoryResourceQuantity(
                        Bladder,
                        NomadResourceIds.HumanWaste,
                        transferableMilliliters),
                },
                new[] { $"resident:{_id}:metabolism" });

            if (!ledger.TryReserveProcess(
                    request,
                    out ProcessTaskLease process,
                    out ResourceFlowBlocker blocker))
                return new ResidentWaterCycleTick(0, blocker);

            process.Commit();
            _pendingMetabolismMilliliters -= transferableMilliliters;
            _metabolismSequence++;

            if (BodyWater.GetAmount(NomadResourceIds.Water) <= 0)
                _pendingMetabolismMilliliters = 0d;

            ResourceFlowBlocker remainingBlocker =
                readyMilliliters > transferableMilliliters && Bladder.FreeCapacity <= 0
                    ? new ResourceFlowBlocker(
                        ResourceFlowBlockReason.DestinationFull,
                        Bladder.Id,
                        NomadResourceIds.HumanWaste)
                    : ResourceFlowBlocker.None;
            return new ResidentWaterCycleTick(transferableMilliliters, remainingBlocker);
        }

        private static ResourceQuantity[] CreateInitialContents(
            ResourceId resource,
            int amount) =>
            amount <= 0
                ? Array.Empty<ResourceQuantity>()
                : new[] { new ResourceQuantity(resource, amount) };
    }

    /// <summary>喝水或如厕的原子行动句柄；取消不改变库存，也不提前修改连续需求。</summary>
    public sealed class ResidentWaterActionLease : IDisposable
    {
        private ProcessTaskLease _process;
        private Action _onCommitted;

        internal ResidentWaterActionLease(ProcessTaskLease process, Action onCommitted = null)
        {
            _process = process ?? throw new ArgumentNullException(nameof(process));
            Request = process.Request;
            _onCommitted = onCommitted;
        }

        public ProcessTaskRequest Request { get; }
        public bool IsCommitted { get; private set; }
        public bool IsReleased => _process == null;

        public void Commit()
        {
            if (_process == null)
                throw new InvalidOperationException("居民水循环行动已经结束。 ");

            ProcessTaskLease process = _process;
            Action onCommitted = _onCommitted;
            _process = null;
            _onCommitted = null;
            process.Commit();
            onCommitted?.Invoke();
            IsCommitted = true;
        }

        public void Dispose()
        {
            ProcessTaskLease process = _process;
            _process = null;
            _onCommitted = null;
            process?.Dispose();
        }
    }
}
