using System;

namespace Game.NomadWorkshop.Simulation
{
    /// <summary>一次生理推进的离散物质变化与首个阻塞原因。</summary>
    public readonly struct ResidentWaterCycleTick
    {
        public ResidentWaterCycleTick(int metabolizedUnits, ResourceFlowBlocker blocker)
        {
            if (metabolizedUnits < 0)
                throw new ArgumentOutOfRangeException(nameof(metabolizedUnits));
            MetabolizedUnits = metabolizedUnits;
            Blocker = blocker;
        }

        public int MetabolizedUnits { get; }
        public ResourceFlowBlocker Blocker { get; }
    }

    /// <summary>
    /// 一位居民的最小饮水与排泄物质链。口渴是连续需求，摄入水和膀胱内容物是离散库存；
    /// 喝水、代谢和如厕均借用同一资源账本，因而取消或容量不足时不会吞掉物质。
    /// </summary>
    public sealed class ResidentWaterCycle
    {
        private readonly string _id;
        private readonly ulong _ownerId;
        private readonly float _metabolismSecondsPerUnit;
        private readonly float _thirstIncreasePerSecond;
        private readonly float _thirstReliefPerUnit;
        private float _metabolismProgressSeconds;
        private int _metabolismSequence;

        public ResidentWaterCycle(
            string id,
            ulong ownerId,
            float metabolismSecondsPerUnit,
            float initialThirst = 0.72f,
            float thirstIncreasePerSecond = 0.004f,
            float thirstReliefPerUnit = 0.72f,
            int bodyWaterCapacity = 2,
            int bladderCapacity = 2)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("居民水循环 id 不能为空。", nameof(id));
            if (metabolismSecondsPerUnit <= 0f)
                throw new ArgumentOutOfRangeException(
                    nameof(metabolismSecondsPerUnit),
                    "每单位代谢时间必须大于零。");
            if (initialThirst < 0f || initialThirst > 1f)
                throw new ArgumentOutOfRangeException(nameof(initialThirst), "初始口渴必须位于 0 到 1。 ");
            if (thirstIncreasePerSecond < 0f)
                throw new ArgumentOutOfRangeException(nameof(thirstIncreasePerSecond));
            if (thirstReliefPerUnit <= 0f || thirstReliefPerUnit > 1f)
                throw new ArgumentOutOfRangeException(nameof(thirstReliefPerUnit));
            if (bodyWaterCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(bodyWaterCapacity));
            if (bladderCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(bladderCapacity));

            _id = id.Trim();
            _ownerId = ownerId;
            _metabolismSecondsPerUnit = metabolismSecondsPerUnit;
            _thirstIncreasePerSecond = thirstIncreasePerSecond;
            _thirstReliefPerUnit = thirstReliefPerUnit;
            Thirst = initialThirst;
            BodyWater = new ResourceInventory($"{_id}:body-water", bodyWaterCapacity);
            Bladder = new ResourceInventory($"{_id}:bladder", bladderCapacity);
        }

        /// <summary>0 表示不渴，1 表示口渴达到当前原型上限。</summary>
        public float Thirst { get; private set; }

        /// <summary>
        /// 0 到 1 的排泄压力。已形成的排泄物占主要部分，正在代谢的水提供连续的前兆，
        /// 便于效用 AI 在硬性“满”之前做出选择。
        /// </summary>
        public float ExcretionPressure
        {
            get
            {
                float forming = BodyWater.GetAmount(NomadResourceIds.Water) > 0
                    ? Math.Min(1f, _metabolismProgressSeconds / _metabolismSecondsPerUnit)
                    : 0f;
                return Math.Min(1f, (Bladder.TotalAmount + forming) / Bladder.Capacity);
            }
        }

        /// <summary>已经喝下但尚未代谢的水；它不是可供其他居民领取的公共库存。</summary>
        public ResourceInventory BodyWater { get; }

        /// <summary>已形成且需要通过厕所排出的真实内容物。</summary>
        public ResourceInventory Bladder { get; }

        public float MetabolismProgress01 => Math.Min(
            1f,
            _metabolismProgressSeconds / _metabolismSecondsPerUnit);

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
                () => Thirst = Math.Max(0f, Thirst - _thirstReliefPerUnit * amount));
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
        /// 推进连续口渴和延迟代谢。每满一个代谢周期，才原子地把一单位体内水变成排泄物；
        /// 膀胱无容量时保留水与累计进度，并返回可解释阻塞。
        /// </summary>
        public ResidentWaterCycleTick Advance(float deltaSeconds, ResourceFlowLedger ledger)
        {
            if (deltaSeconds < 0f) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            if (ledger == null) throw new ArgumentNullException(nameof(ledger));

            Thirst = Math.Min(1f, Thirst + deltaSeconds * _thirstIncreasePerSecond);
            if (BodyWater.GetAmount(NomadResourceIds.Water) <= 0)
            {
                _metabolismProgressSeconds = 0f;
                return new ResidentWaterCycleTick(0, ResourceFlowBlocker.None);
            }

            _metabolismProgressSeconds += deltaSeconds;
            int metabolized = 0;
            while (_metabolismProgressSeconds >= _metabolismSecondsPerUnit &&
                   BodyWater.GetAmount(NomadResourceIds.Water) > 0)
            {
                var request = new ProcessTaskRequest(
                    $"{_id}:metabolism:{_metabolismSequence + 1}",
                    _ownerId,
                    "延迟代谢把摄入水转为需要排出的真实物质",
                    new[]
                    {
                        new InventoryResourceQuantity(BodyWater, NomadResourceIds.Water, 1),
                    },
                    new[]
                    {
                        new InventoryResourceQuantity(Bladder, NomadResourceIds.HumanWaste, 1),
                    },
                    new[] { $"resident:{_id}:metabolism" });

                if (!ledger.TryReserveProcess(request, out ProcessTaskLease process, out ResourceFlowBlocker blocker))
                    return new ResidentWaterCycleTick(metabolized, blocker);

                process.Commit();
                _metabolismProgressSeconds -= _metabolismSecondsPerUnit;
                _metabolismSequence++;
                metabolized++;
            }

            if (BodyWater.GetAmount(NomadResourceIds.Water) <= 0)
                _metabolismProgressSeconds = 0f;
            return new ResidentWaterCycleTick(metabolized, ResourceFlowBlocker.None);
        }
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
