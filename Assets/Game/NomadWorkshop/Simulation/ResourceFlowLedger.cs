using System;
using System.Collections.Generic;

namespace Game.NomadWorkshop.Simulation
{
    /// <summary>资源任务不能开始时的稳定原因，表现层可据此生成面向玩家的解释。</summary>
    public enum ResourceFlowBlockReason
    {
        None,
        SourceInsufficient,
        CarrierFull,
        DestinationFull,
        TaskAlreadyReserved,
        InteractionUnavailable,
    }

    /// <summary>除原因外保留首个阻塞库存与资源，避免 UI 只能显示笼统的“任务失败”。</summary>
    public readonly struct ResourceFlowBlocker
    {
        public ResourceFlowBlocker(
            ResourceFlowBlockReason reason,
            string inventoryId = "",
            ResourceId resource = default)
        {
            Reason = reason;
            InventoryId = inventoryId ?? string.Empty;
            Resource = resource;
        }

        public ResourceFlowBlockReason Reason { get; }
        public string InventoryId { get; }
        public ResourceId Resource { get; }
        public bool IsBlocked => Reason != ResourceFlowBlockReason.None;

        public static ResourceFlowBlocker None => new(ResourceFlowBlockReason.None);
    }

    /// <summary>设施加工的一项真实输入或输出。</summary>
    public readonly struct InventoryResourceQuantity
    {
        public InventoryResourceQuantity(ResourceInventory inventory, ResourceId resource, int amount)
        {
            Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            if (!resource.IsValid) throw new ArgumentException("资源 id 无效。", nameof(resource));
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "资源数量必须大于零。");
            inventory.EnsureCompatible(resource);
            Resource = resource;
            Amount = amount;
        }

        public ResourceInventory Inventory { get; }
        public ResourceId Resource { get; }
        public int Amount { get; }
    }

    /// <summary>
    /// 一次可执行搬运的完整契约。来源、居民携带空间、最终目的地、数量、原因和交互位缺一不可。
    /// </summary>
    public sealed class HaulTaskRequest
    {
        public HaulTaskRequest(
            string taskId,
            ulong ownerId,
            ResourceInventory source,
            ResourceInventory carrier,
            ResourceInventory destination,
            ResourceId resource,
            int amount,
            string reason,
            IReadOnlyList<string> interactionKeys)
        {
            if (string.IsNullOrWhiteSpace(taskId))
                throw new ArgumentException("搬运任务 id 不能为空。", nameof(taskId));
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Carrier = carrier ?? throw new ArgumentNullException(nameof(carrier));
            Destination = destination ?? throw new ArgumentNullException(nameof(destination));
            if (ReferenceEquals(source, carrier) || ReferenceEquals(source, destination) ||
                ReferenceEquals(carrier, destination))
                throw new ArgumentException("来源、携带库存和目的地必须是三个不同库存节点。 ");
            if (!resource.IsValid) throw new ArgumentException("资源 id 无效。", nameof(resource));
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "搬运数量必须大于零。");
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("搬运任务必须说明原因。", nameof(reason));

            source.EnsureCompatible(resource);
            carrier.EnsureCompatible(resource);
            destination.EnsureCompatible(resource);

            TaskId = taskId.Trim();
            OwnerId = ownerId;
            Resource = resource;
            Amount = amount;
            Reason = reason.Trim();
            InteractionKeys = CopyInteractionKeys(interactionKeys);
        }

        public string TaskId { get; }
        public ulong OwnerId { get; }
        public ResourceInventory Source { get; }
        public ResourceInventory Carrier { get; }
        /// <summary>
        /// 当前目的库存。任务进入租约后，只能通过
        /// <see cref="HaulTaskLease.TryRetargetDestination"/> 原子改写，调用方不能绕过容量与交互预留。
        /// </summary>
        public ResourceInventory Destination { get; private set; }
        public ResourceId Resource { get; }
        public int Amount { get; }
        public string Reason { get; }
        public IReadOnlyList<string> InteractionKeys { get; private set; }

        internal void RetargetDestination(
            ResourceInventory destination,
            IReadOnlyList<string> interactionKeys)
        {
            Destination = destination ?? throw new ArgumentNullException(nameof(destination));
            InteractionKeys = CopyInteractionKeys(interactionKeys);
        }

        internal static string[] CopyInteractionKeys(IReadOnlyList<string> interactionKeys)
        {
            if (interactionKeys == null) throw new ArgumentNullException(nameof(interactionKeys));
            if (interactionKeys.Count == 0)
                throw new ArgumentException("资源任务至少需要一个交互位。", nameof(interactionKeys));

            var result = new string[interactionKeys.Count];
            for (int i = 0; i < interactionKeys.Count; i++)
            {
                string key = interactionKeys[i];
                if (string.IsNullOrWhiteSpace(key))
                    throw new ArgumentException($"交互位第 {i} 项为空。", nameof(interactionKeys));
                result[i] = key;
            }
            return result;
        }
    }

    /// <summary>一次设施加工的全部输入、输出与工作位契约。</summary>
    public sealed class ProcessTaskRequest
    {
        public ProcessTaskRequest(
            string taskId,
            ulong ownerId,
            string reason,
            IReadOnlyList<InventoryResourceQuantity> inputs,
            IReadOnlyList<InventoryResourceQuantity> outputs,
            IReadOnlyList<string> interactionKeys)
        {
            if (string.IsNullOrWhiteSpace(taskId))
                throw new ArgumentException("加工任务 id 不能为空。", nameof(taskId));
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("加工任务必须说明原因。", nameof(reason));
            Inputs = CopyEntries(inputs, nameof(inputs));
            Outputs = CopyEntries(outputs, nameof(outputs));
            if (Inputs.Count == 0) throw new ArgumentException("加工任务至少需要一项输入。", nameof(inputs));
            if (Outputs.Count == 0) throw new ArgumentException("加工任务至少需要一项输出。", nameof(outputs));

            TaskId = taskId.Trim();
            OwnerId = ownerId;
            Reason = reason.Trim();
            InteractionKeys = HaulTaskRequest.CopyInteractionKeys(interactionKeys);
        }

        public string TaskId { get; }
        public ulong OwnerId { get; }
        public string Reason { get; }
        public IReadOnlyList<InventoryResourceQuantity> Inputs { get; }
        public IReadOnlyList<InventoryResourceQuantity> Outputs { get; }
        public IReadOnlyList<string> InteractionKeys { get; }

        private static InventoryResourceQuantity[] CopyEntries(
            IReadOnlyList<InventoryResourceQuantity> entries,
            string parameterName)
        {
            if (entries == null) throw new ArgumentNullException(parameterName);
            var result = new InventoryResourceQuantity[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                InventoryResourceQuantity entry = entries[i];
                if (entry.Inventory == null || !entry.Resource.IsValid || entry.Amount <= 0)
                    throw new ArgumentException($"资源项第 {i} 项无效。", parameterName);
                result[i] = entry;
            }
            return result;
        }
    }

    public enum HaulTaskState
    {
        Reserved,
        Carrying,
        Delivered,
        Cancelled,
    }

    /// <summary>
    /// 单线程模拟的资源流所有者。它把数量预留与交互位预留作为一个原子操作，
    /// 并保证搬运途中资源真实存在于居民的携带库存中。
    /// </summary>
    public sealed class ResourceFlowLedger
    {
        private readonly ReservationLedger _interactions;
        private readonly Dictionary<InventoryResourceKey, int> _reservedOutgoing = new();
        private readonly Dictionary<ResourceInventory, int> _reservedIncomingCapacity = new();

        public ResourceFlowLedger(ReservationLedger interactions = null)
        {
            _interactions = interactions ?? new ReservationLedger();
        }

        public int GetAvailableAmount(ResourceInventory inventory, ResourceId resource)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            var key = new InventoryResourceKey(inventory, resource);
            return inventory.GetAmount(resource) - GetReservedOutgoing(key);
        }

        public int GetAvailableCapacity(ResourceInventory inventory)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            return inventory.FreeCapacity - GetReservedIncoming(inventory);
        }

        /// <summary>库存仍有进出资源或容量预留时，不得拆除其所有者或提取全部内容。</summary>
        public bool HasReservations(ResourceInventory inventory)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            if (GetReservedIncoming(inventory) > 0) return true;
            foreach (KeyValuePair<InventoryResourceKey, int> entry in _reservedOutgoing)
                if (ReferenceEquals(entry.Key.Inventory, inventory) && entry.Value > 0) return true;
            return false;
        }

        /// <summary>
        /// 原子预留来源数量、携带容量、最终目的容量和全部交互位。失败不会留下任何部分预留。
        /// </summary>
        public bool TryReserveHaul(
            HaulTaskRequest request,
            out HaulTaskLease lease,
            out ResourceFlowBlocker blocker)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            if (GetAvailableAmount(request.Source, request.Resource) < request.Amount)
            {
                lease = null;
                blocker = new ResourceFlowBlocker(
                    ResourceFlowBlockReason.SourceInsufficient,
                    request.Source.Id,
                    request.Resource);
                return false;
            }

            if (GetAvailableCapacity(request.Carrier) < request.Amount)
            {
                lease = null;
                blocker = new ResourceFlowBlocker(
                    ResourceFlowBlockReason.CarrierFull,
                    request.Carrier.Id,
                    request.Resource);
                return false;
            }

            if (GetAvailableCapacity(request.Destination) < request.Amount)
            {
                lease = null;
                blocker = new ResourceFlowBlocker(
                    ResourceFlowBlockReason.DestinationFull,
                    request.Destination.Id,
                    request.Resource);
                return false;
            }

            string taskKey = BuildTaskKey(request.TaskId);
            if (_interactions.TryGetOwner(taskKey, out _))
            {
                lease = null;
                blocker = new ResourceFlowBlocker(ResourceFlowBlockReason.TaskAlreadyReserved);
                return false;
            }

            if (!_interactions.TryAcquire(
                    request.OwnerId,
                    IncludeTaskKey(taskKey, request.InteractionKeys),
                    out ReservationLease interactionLease))
            {
                lease = null;
                blocker = new ResourceFlowBlocker(ResourceFlowBlockReason.InteractionUnavailable);
                return false;
            }

            AddOutgoing(request.Source, request.Resource, request.Amount);
            AddIncoming(request.Carrier, request.Amount);
            AddIncoming(request.Destination, request.Amount);
            lease = new HaulTaskLease(this, request, interactionLease);
            blocker = ResourceFlowBlocker.None;
            return true;
        }

        /// <summary>
        /// 原子预留全部加工输入、输出所需净容量和交互位；提交前不改写任何库存。
        /// 同一库存可同时提供输入和接收输出，输入释放的容量会在本次原子加工内复用。
        /// </summary>
        public bool TryReserveProcess(
            ProcessTaskRequest request,
            out ProcessTaskLease lease,
            out ResourceFlowBlocker blocker)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            List<InventoryResourceQuantity> inputs = Consolidate(request.Inputs);
            List<InventoryResourceQuantity> outputs = Consolidate(request.Outputs);
            for (int i = 0; i < inputs.Count; i++)
            {
                InventoryResourceQuantity input = inputs[i];
                if (GetAvailableAmount(input.Inventory, input.Resource) >= input.Amount) continue;

                lease = null;
                blocker = new ResourceFlowBlocker(
                    ResourceFlowBlockReason.SourceInsufficient,
                    input.Inventory.Id,
                    input.Resource);
                return false;
            }

            Dictionary<ResourceInventory, int> capacityClaims = CalculateCapacityClaims(inputs, outputs);
            foreach (KeyValuePair<ResourceInventory, int> claim in capacityClaims)
            {
                if (GetAvailableCapacity(claim.Key) >= claim.Value) continue;

                lease = null;
                blocker = new ResourceFlowBlocker(
                    ResourceFlowBlockReason.DestinationFull,
                    claim.Key.Id);
                return false;
            }

            string taskKey = BuildTaskKey(request.TaskId);
            if (_interactions.TryGetOwner(taskKey, out _))
            {
                lease = null;
                blocker = new ResourceFlowBlocker(ResourceFlowBlockReason.TaskAlreadyReserved);
                return false;
            }

            if (!_interactions.TryAcquire(
                    request.OwnerId,
                    IncludeTaskKey(taskKey, request.InteractionKeys),
                    out ReservationLease interactionLease))
            {
                lease = null;
                blocker = new ResourceFlowBlocker(ResourceFlowBlockReason.InteractionUnavailable);
                return false;
            }

            for (int i = 0; i < inputs.Count; i++)
                AddOutgoing(inputs[i].Inventory, inputs[i].Resource, inputs[i].Amount);
            foreach (KeyValuePair<ResourceInventory, int> claim in capacityClaims)
                AddIncoming(claim.Key, claim.Value);

            lease = new ProcessTaskLease(this, request, inputs, outputs, capacityClaims, interactionLease);
            blocker = ResourceFlowBlocker.None;
            return true;
        }

        internal void PickUp(HaulTaskLease lease)
        {
            HaulTaskRequest request = lease.Request;
            request.Source.Remove(request.Resource, request.Amount);
            request.Carrier.Add(request.Resource, request.Amount);
            RemoveOutgoing(request.Source, request.Resource, request.Amount);
            RemoveIncoming(request.Carrier, request.Amount);
            AddOutgoing(request.Carrier, request.Resource, request.Amount);
        }

        internal void Deliver(HaulTaskLease lease)
        {
            HaulTaskRequest request = lease.Request;
            request.Carrier.Remove(request.Resource, request.Amount);
            request.Destination.Add(request.Resource, request.Amount);
            RemoveOutgoing(request.Carrier, request.Resource, request.Amount);
            RemoveIncoming(request.Destination, request.Amount);
            lease.ReleaseInteraction();
        }

        internal bool TryRetargetDestination(
            HaulTaskLease lease,
            ResourceInventory destination,
            IReadOnlyList<string> interactionKeys,
            out ResourceFlowBlocker blocker)
        {
            if (lease == null) throw new ArgumentNullException(nameof(lease));
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (!lease.IsOwnedBy(this))
                throw new InvalidOperationException("只能改写仍由当前资源账本持有的搬运租约。 ");
            if (lease.State is not (HaulTaskState.Reserved or HaulTaskState.Carrying))
                throw new InvalidOperationException(
                    $"搬运任务 {lease.Request.TaskId} 不能在 {lease.State} 阶段改写目的地。 ");

            HaulTaskRequest request = lease.Request;
            if (ReferenceEquals(destination, request.Source) ||
                ReferenceEquals(destination, request.Carrier))
                throw new ArgumentException("新目的库存不能与来源或携带库存相同。", nameof(destination));
            destination.EnsureCompatible(request.Resource);
            string[] normalizedInteractionKeys =
                HaulTaskRequest.CopyInteractionKeys(interactionKeys);

            ResourceInventory previousDestination = request.Destination;
            if (!ReferenceEquals(previousDestination, destination) &&
                GetAvailableCapacity(destination) < request.Amount)
            {
                blocker = new ResourceFlowBlocker(
                    ResourceFlowBlockReason.DestinationFull,
                    destination.Id,
                    request.Resource);
                return false;
            }

            string taskKey = BuildTaskKey(request.TaskId);
            IReadOnlyList<string> previousInteractionKeys = request.InteractionKeys;
            lease.ReleaseInteraction();
            if (!_interactions.TryAcquire(
                    request.OwnerId,
                    IncludeTaskKey(taskKey, normalizedInteractionKeys),
                    out ReservationLease replacementInteraction))
            {
                if (!_interactions.TryAcquire(
                        request.OwnerId,
                        IncludeTaskKey(taskKey, previousInteractionKeys),
                        out ReservationLease restoredInteraction))
                    throw new InvalidOperationException(
                        $"搬运任务 {request.TaskId} 改道失败后无法恢复原交互预留，账本已损坏。 ");

                lease.ReplaceInteraction(restoredInteraction);
                blocker = new ResourceFlowBlocker(
                    ResourceFlowBlockReason.InteractionUnavailable);
                return false;
            }

            if (!ReferenceEquals(previousDestination, destination))
            {
                RemoveIncoming(previousDestination, request.Amount);
                AddIncoming(destination, request.Amount);
            }
            request.RetargetDestination(destination, normalizedInteractionKeys);
            lease.ReplaceInteraction(replacementInteraction);
            blocker = ResourceFlowBlocker.None;
            return true;
        }

        internal void Cancel(HaulTaskLease lease, HaulTaskState previousState)
        {
            HaulTaskRequest request = lease.Request;
            if (previousState == HaulTaskState.Reserved)
            {
                RemoveOutgoing(request.Source, request.Resource, request.Amount);
                RemoveIncoming(request.Carrier, request.Amount);
            }
            else if (previousState == HaulTaskState.Carrying)
            {
                RemoveOutgoing(request.Carrier, request.Resource, request.Amount);
            }

            RemoveIncoming(request.Destination, request.Amount);
            lease.ReleaseInteraction();
        }

        internal void Commit(ProcessTaskLease lease)
        {
            IReadOnlyList<InventoryResourceQuantity> inputs = lease.Inputs;
            IReadOnlyList<InventoryResourceQuantity> outputs = lease.Outputs;
            for (int i = 0; i < inputs.Count; i++)
            {
                InventoryResourceQuantity input = inputs[i];
                input.Inventory.Remove(input.Resource, input.Amount);
            }
            for (int i = 0; i < outputs.Count; i++)
            {
                InventoryResourceQuantity output = outputs[i];
                output.Inventory.Add(output.Resource, output.Amount);
            }

            ReleaseProcessReservations(lease);
            lease.ReleaseInteraction();
        }

        internal void Cancel(ProcessTaskLease lease)
        {
            ReleaseProcessReservations(lease);
            lease.ReleaseInteraction();
        }

        private void ReleaseProcessReservations(ProcessTaskLease lease)
        {
            for (int i = 0; i < lease.Inputs.Count; i++)
            {
                InventoryResourceQuantity input = lease.Inputs[i];
                RemoveOutgoing(input.Inventory, input.Resource, input.Amount);
            }
            foreach (KeyValuePair<ResourceInventory, int> claim in lease.CapacityClaims)
                RemoveIncoming(claim.Key, claim.Value);
        }

        private static List<InventoryResourceQuantity> Consolidate(
            IReadOnlyList<InventoryResourceQuantity> entries)
        {
            var totals = new Dictionary<InventoryResourceKey, int>();
            for (int i = 0; i < entries.Count; i++)
            {
                InventoryResourceQuantity entry = entries[i];
                var key = new InventoryResourceKey(entry.Inventory, entry.Resource);
                totals.TryGetValue(key, out int current);
                totals[key] = checked(current + entry.Amount);
            }

            var result = new List<InventoryResourceQuantity>(totals.Count);
            foreach (KeyValuePair<InventoryResourceKey, int> pair in totals)
                result.Add(new InventoryResourceQuantity(pair.Key.Inventory, pair.Key.Resource, pair.Value));
            result.Sort((left, right) =>
            {
                int inventoryOrder = string.Compare(left.Inventory.Id, right.Inventory.Id, StringComparison.Ordinal);
                return inventoryOrder != 0 ? inventoryOrder : left.Resource.CompareTo(right.Resource);
            });
            return result;
        }

        private static Dictionary<ResourceInventory, int> CalculateCapacityClaims(
            IReadOnlyList<InventoryResourceQuantity> inputs,
            IReadOnlyList<InventoryResourceQuantity> outputs)
        {
            var netChanges = new Dictionary<ResourceInventory, int>();
            for (int i = 0; i < inputs.Count; i++)
            {
                InventoryResourceQuantity input = inputs[i];
                netChanges.TryGetValue(input.Inventory, out int current);
                netChanges[input.Inventory] = checked(current - input.Amount);
            }
            for (int i = 0; i < outputs.Count; i++)
            {
                InventoryResourceQuantity output = outputs[i];
                netChanges.TryGetValue(output.Inventory, out int current);
                netChanges[output.Inventory] = checked(current + output.Amount);
            }

            var result = new Dictionary<ResourceInventory, int>();
            foreach (KeyValuePair<ResourceInventory, int> pair in netChanges)
            {
                if (pair.Value > 0) result.Add(pair.Key, pair.Value);
            }
            return result;
        }

        private static string BuildTaskKey(string taskId) => "resource-task:" + taskId;

        private static IReadOnlyList<string> IncludeTaskKey(
            string taskKey,
            IReadOnlyList<string> interactionKeys)
        {
            var result = new string[interactionKeys.Count + 1];
            result[0] = taskKey;
            for (int i = 0; i < interactionKeys.Count; i++) result[i + 1] = interactionKeys[i];
            return result;
        }

        private int GetReservedOutgoing(InventoryResourceKey key)
            => _reservedOutgoing.TryGetValue(key, out int amount) ? amount : 0;

        private int GetReservedIncoming(ResourceInventory inventory)
            => _reservedIncomingCapacity.TryGetValue(inventory, out int amount) ? amount : 0;

        private void AddOutgoing(ResourceInventory inventory, ResourceId resource, int amount)
        {
            var key = new InventoryResourceKey(inventory, resource);
            _reservedOutgoing[key] = checked(GetReservedOutgoing(key) + amount);
        }

        private void RemoveOutgoing(ResourceInventory inventory, ResourceId resource, int amount)
        {
            var key = new InventoryResourceKey(inventory, resource);
            int remaining = GetReservedOutgoing(key) - amount;
            if (remaining < 0) throw new InvalidOperationException("资源出库预留账本损坏。 ");
            if (remaining == 0) _reservedOutgoing.Remove(key);
            else _reservedOutgoing[key] = remaining;
        }

        private void AddIncoming(ResourceInventory inventory, int amount)
            => _reservedIncomingCapacity[inventory] = checked(GetReservedIncoming(inventory) + amount);

        private void RemoveIncoming(ResourceInventory inventory, int amount)
        {
            int remaining = GetReservedIncoming(inventory) - amount;
            if (remaining < 0) throw new InvalidOperationException("资源入库容量预留账本损坏。 ");
            if (remaining == 0) _reservedIncomingCapacity.Remove(inventory);
            else _reservedIncomingCapacity[inventory] = remaining;
        }

        private readonly struct InventoryResourceKey : IEquatable<InventoryResourceKey>
        {
            public InventoryResourceKey(ResourceInventory inventory, ResourceId resource)
            {
                Inventory = inventory;
                Resource = resource;
            }

            public ResourceInventory Inventory { get; }
            public ResourceId Resource { get; }

            public bool Equals(InventoryResourceKey other)
                => ReferenceEquals(Inventory, other.Inventory) && Resource.Equals(other.Resource);

            public override bool Equals(object obj)
                => obj is InventoryResourceKey other && Equals(other);

            public override int GetHashCode()
                => HashCode.Combine(Inventory, Resource);
        }
    }

    /// <summary>搬运预留的阶段句柄；取消搬运时已拾取物仍留在携带库存，不会静默消失。</summary>
    public sealed class HaulTaskLease : IDisposable
    {
        private ResourceFlowLedger _ledger;
        private ReservationLease _interactionLease;

        internal HaulTaskLease(
            ResourceFlowLedger ledger,
            HaulTaskRequest request,
            ReservationLease interactionLease)
        {
            _ledger = ledger;
            Request = request;
            _interactionLease = interactionLease;
            State = HaulTaskState.Reserved;
        }

        public HaulTaskRequest Request { get; }
        public HaulTaskState State { get; private set; }

        /// <summary>取消发生在拾取后时为 true；货物仍在居民携带库存，必须重新派送或落地。</summary>
        public bool CargoRequiresRecovery { get; private set; }

        /// <summary>
        /// 在资源尚未交付时原子改写目的库存和交互预留。失败时原目的容量与全部交互键保持不变，
        /// 携带中的真实货物也不会离开 Carrier。
        /// </summary>
        public bool TryRetargetDestination(
            ResourceInventory destination,
            IReadOnlyList<string> interactionKeys,
            out ResourceFlowBlocker blocker)
        {
            ResourceFlowLedger ledger = _ledger;
            if (ledger == null)
                throw new InvalidOperationException($"搬运任务 {Request.TaskId} 已经结束。 ");
            return ledger.TryRetargetDestination(
                this,
                destination,
                interactionKeys,
                out blocker);
        }

        public void PickUp()
        {
            if (State != HaulTaskState.Reserved)
                throw new InvalidOperationException($"搬运任务 {Request.TaskId} 不能在 {State} 阶段拾取。 ");
            _ledger.PickUp(this);
            State = HaulTaskState.Carrying;
        }

        public void Deliver()
        {
            if (State != HaulTaskState.Carrying)
                throw new InvalidOperationException($"搬运任务 {Request.TaskId} 不能在 {State} 阶段交付。 ");
            _ledger.Deliver(this);
            State = HaulTaskState.Delivered;
            _ledger = null;
        }

        public void Dispose()
        {
            ResourceFlowLedger ledger = _ledger;
            if (ledger == null) return;

            HaulTaskState previousState = State;
            CargoRequiresRecovery = previousState == HaulTaskState.Carrying;
            State = HaulTaskState.Cancelled;
            _ledger = null;
            ledger.Cancel(this, previousState);
        }

        internal void ReleaseInteraction()
        {
            _interactionLease?.Dispose();
            _interactionLease = null;
        }

        internal bool IsOwnedBy(ResourceFlowLedger ledger) => ReferenceEquals(_ledger, ledger);

        internal void ReplaceInteraction(ReservationLease interactionLease)
        {
            if (_interactionLease != null)
                throw new InvalidOperationException(
                    $"搬运任务 {Request.TaskId} 的旧交互预留尚未释放。 ");
            _interactionLease = interactionLease ??
                throw new ArgumentNullException(nameof(interactionLease));
        }
    }

    /// <summary>设施加工的预留句柄；提交前取消不会消耗输入或产生输出。</summary>
    public sealed class ProcessTaskLease : IDisposable
    {
        private ResourceFlowLedger _ledger;
        private ReservationLease _interactionLease;

        internal ProcessTaskLease(
            ResourceFlowLedger ledger,
            ProcessTaskRequest request,
            IReadOnlyList<InventoryResourceQuantity> inputs,
            IReadOnlyList<InventoryResourceQuantity> outputs,
            IReadOnlyDictionary<ResourceInventory, int> capacityClaims,
            ReservationLease interactionLease)
        {
            _ledger = ledger;
            Request = request;
            Inputs = inputs;
            Outputs = outputs;
            CapacityClaims = capacityClaims;
            _interactionLease = interactionLease;
        }

        public ProcessTaskRequest Request { get; }
        public bool IsCommitted { get; private set; }
        public bool IsReleased => _ledger == null;

        internal IReadOnlyList<InventoryResourceQuantity> Inputs { get; }
        internal IReadOnlyList<InventoryResourceQuantity> Outputs { get; }
        internal IReadOnlyDictionary<ResourceInventory, int> CapacityClaims { get; }

        public void Commit()
        {
            if (_ledger == null)
                throw new InvalidOperationException($"加工任务 {Request.TaskId} 已经结束。 ");
            ResourceFlowLedger ledger = _ledger;
            _ledger = null;
            ledger.Commit(this);
            IsCommitted = true;
        }

        public void Dispose()
        {
            ResourceFlowLedger ledger = _ledger;
            if (ledger == null) return;
            _ledger = null;
            ledger.Cancel(this);
        }

        internal void ReleaseInteraction()
        {
            _interactionLease?.Dispose();
            _interactionLease = null;
        }
    }
}
