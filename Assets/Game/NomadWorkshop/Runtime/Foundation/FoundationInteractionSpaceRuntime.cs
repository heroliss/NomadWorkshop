using System;
using System.Collections.Generic;
using Game.NomadWorkshop.Simulation;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>
    /// Foundation 设施停靠空间的运行时所有者：从设施定义重建 committed / preview 拓扑，生成 View 掩码，
    /// 并把一个空间的全部 Slot 键作为原子租约占用。每位居民持有自己的租约，互不重叠的空间可并行使用。
    /// 单线程调用；释放租约不会销毁共享拓扑，销毁拓扑会撤销全部租约。
    /// </summary>
    public sealed class FoundationInteractionSpaceRuntime : IDisposable
    {
        private readonly InteractionSpaceTopology _committed;
        private readonly InteractionSpaceTopology _preview;
        private readonly ReservationLedger _reservations = new();
        private readonly List<InteractionSpaceSlot> _committedSlots = new();
        private readonly List<InteractionSpaceSlot> _previewSlots = new();
        private readonly Dictionary<ulong, FoundationInteractionSpaceLease> _leases = new();
        private bool _disposed;

        public FoundationInteractionSpaceRuntime(int mergeDistanceMillimeters)
        {
            _committed = new InteractionSpaceTopology(mergeDistanceMillimeters);
            _preview = new InteractionSpaceTopology(mergeDistanceMillimeters);
        }

        public int ActiveLeaseCount => _leases.Count;

        /// <summary>
        /// 设施集合改变后重建正式拓扑。保留仍可独占的旧地址与句柄；消失的地址和合并后互相冲突的
        /// 所有占用一起撤销，避免设施枚举顺序决定谁抢到位置。调用方随后检查 IsActive 并停止失效行动。
        /// 不保留时撤销所有租约，适用于读取检查点；不恢复任何路径或行动。
        /// </summary>
        public bool RebuildCommitted(
            IReadOnlyList<FoundationFacilityState> facilities,
            IReadOnlyDictionary<string, NomadFacilityDefinition> definitions,
            bool reacquireActiveSpace)
        {
            ThrowIfDisposed();
            if (facilities == null) throw new ArgumentNullException(nameof(facilities));
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));

            _committedSlots.Clear();
            for (var i = 0; i < facilities.Count; i++)
            {
                FoundationFacilityState facility = facilities[i];
                if (definitions.TryGetValue(
                        facility.DefinitionId,
                        out NomadFacilityDefinition definition))
                    AddSlots(
                        _committedSlots,
                        facility.InstanceId,
                        facility.Pose,
                        definition);
            }
            _committed.Rebuild(_committedSlots);

            var previous = new List<FoundationInteractionSpaceLease>(_leases.Values);
            var keyUseCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (FoundationInteractionSpaceLease lease in previous)
            {
                lease.ReleaseReservation();
                if (!reacquireActiveSpace ||
                    !_committed.TryGetSpace(lease.Slot, out InteractionSpace space))
                    continue;
                foreach (string key in space.ReservationKeys)
                {
                    keyUseCounts.TryGetValue(key, out int count);
                    keyUseCounts[key] = count + 1;
                }
            }

            bool allRetained = true;
            foreach (FoundationInteractionSpaceLease lease in previous)
            {
                bool retain = reacquireActiveSpace &&
                              _committed.TryGetSpace(lease.Slot, out _);
                if (retain)
                {
                    _committed.TryGetSpace(lease.Slot, out InteractionSpace space);
                    foreach (string key in space.ReservationKeys)
                        if (keyUseCounts[key] > 1) retain = false;
                    if (retain && _reservations.TryAcquire(
                            lease.OwnerId, space.ReservationKeys, out ReservationLease reservation))
                    {
                        lease.ReplaceReservation(reservation);
                        continue;
                    }
                }
                lease.Dispose();
                allRetained = false;
            }
            return allRetained;
        }

        public void RebuildPreview(
            in ContinuousFacilityPlacementRequest candidate,
            NomadFacilityDefinition candidateDefinition)
        {
            ThrowIfDisposed();
            if (candidateDefinition == null)
                throw new ArgumentNullException(nameof(candidateDefinition));
            _previewSlots.Clear();
            for (var i = 0; i < _committedSlots.Count; i++)
                _previewSlots.Add(_committedSlots[i]);
            AddSlots(
                _previewSlots,
                candidate.InstanceId,
                candidate.Pose,
                candidateDefinition);
            _preview.Rebuild(_previewSlots);
        }

        public ulong BuildCommittedSharedMask(
            string facilityInstanceId,
            NomadFacilityDefinition definition) =>
            BuildSharedMask(facilityInstanceId, definition, _committed);

        public ulong BuildPreviewSharedMask(
            string facilityInstanceId,
            NomadFacilityDefinition definition) =>
            BuildSharedMask(facilityInstanceId, definition, _preview);

        public ulong BuildCommittedOccupiedMask(
            string facilityInstanceId,
            NomadFacilityDefinition definition)
        {
            if (definition == null) return 0UL;
            ulong result = 0UL;
            var flattenedSlotIndex = 0;
            IReadOnlyList<NomadFacilityInteractionGroupDefinition> groups =
                definition.InteractionGroups;
            for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                NomadFacilityInteractionGroupDefinition group = groups[groupIndex];
                for (var slotIndex = 0;
                     slotIndex < group.AlternativeSlots.Count;
                     slotIndex++)
                {
                    InteractionSlotAddress address = CreateAddress(
                        facilityInstanceId,
                        group,
                        group.AlternativeSlots[slotIndex]);
                    if (_committed.TryGetSpace(address, out InteractionSpace space) &&
                        IsOccupied(space))
                        result |= 1UL << flattenedSlotIndex;
                    flattenedSlotIndex++;
                }
            }
            return result;
        }

        public bool IsAvailable(in InteractionSlotAddress address) =>
            !_disposed && _committed.TryGetSpace(address, out InteractionSpace space) &&
            !IsOccupied(space);

        /// <summary>同一居民至多占用一个空间；成功句柄由居民行动负责释放，失败不产生部分预留。</summary>
        public bool TryAcquire(
            ulong ownerId, in InteractionSpaceSlot slot, out FoundationInteractionSpaceLease lease)
        {
            ThrowIfDisposed();
            lease = null;
            if (_leases.ContainsKey(ownerId) ||
                !_committed.TryGetSpace(slot.Address, out InteractionSpace space) ||
                !_reservations.TryAcquire(
                    ownerId,
                    space.ReservationKeys,
                    out ReservationLease reservation))
                return false;

            lease = new FoundationInteractionSpaceLease(this, ownerId, slot.Address, reservation);
            _leases.Add(ownerId, lease);
            return true;
        }

        internal void Release(FoundationInteractionSpaceLease lease)
        {
            if (_leases.TryGetValue(lease.OwnerId, out FoundationInteractionSpaceLease current) &&
                ReferenceEquals(current, lease))
                _leases.Remove(lease.OwnerId);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (FoundationInteractionSpaceLease lease in
                     new List<FoundationInteractionSpaceLease>(_leases.Values))
                lease.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(FoundationInteractionSpaceRuntime));
        }

        public static InteractionSpaceSlot ResolveSlot(
            in FoundationFacilityState facility,
            NomadFacilityInteractionGroupDefinition group,
            in NomadFacilityInteractionSlotDefinition slot)
        {
            if (group == null) throw new ArgumentNullException(nameof(group));
            return new InteractionSpaceSlot(
                CreateAddress(facility.InstanceId, group, slot),
                slot.Resolve(facility.Pose));
        }

        private static void AddSlots(
            ICollection<InteractionSpaceSlot> destination,
            string facilityInstanceId,
            in DeckPose facilityPose,
            NomadFacilityDefinition definition)
        {
            IReadOnlyList<NomadFacilityInteractionGroupDefinition> groups =
                definition.InteractionGroups;
            for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                NomadFacilityInteractionGroupDefinition group = groups[groupIndex];
                for (var slotIndex = 0;
                     slotIndex < group.AlternativeSlots.Count;
                     slotIndex++)
                {
                    NomadFacilityInteractionSlotDefinition slot =
                        group.AlternativeSlots[slotIndex];
                    destination.Add(new InteractionSpaceSlot(
                        CreateAddress(facilityInstanceId, group, slot),
                        slot.Resolve(facilityPose)));
                }
            }
        }

        private static InteractionSlotAddress CreateAddress(
            string facilityInstanceId,
            NomadFacilityInteractionGroupDefinition group,
            in NomadFacilityInteractionSlotDefinition slot) =>
            new(facilityInstanceId, group.GroupId, slot.SlotId);

        private static ulong BuildSharedMask(
            string facilityInstanceId,
            NomadFacilityDefinition definition,
            InteractionSpaceTopology topology)
        {
            if (definition == null) return 0UL;
            ulong result = 0UL;
            var flattenedSlotIndex = 0;
            IReadOnlyList<NomadFacilityInteractionGroupDefinition> groups =
                definition.InteractionGroups;
            for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                NomadFacilityInteractionGroupDefinition group = groups[groupIndex];
                for (var slotIndex = 0;
                     slotIndex < group.AlternativeSlots.Count;
                     slotIndex++)
                {
                    InteractionSlotAddress address = CreateAddress(
                        facilityInstanceId,
                        group,
                        group.AlternativeSlots[slotIndex]);
                    if (topology.HasCrossGroupConflict(address))
                        result |= 1UL << flattenedSlotIndex;
                    flattenedSlotIndex++;
                }
            }
            return result;
        }

        private bool IsOccupied(InteractionSpace space)
        {
            for (var i = 0; i < space.ReservationKeys.Count; i++)
            {
                if (_reservations.TryGetOwner(space.ReservationKeys[i], out _))
                    return true;
            }
            return false;
        }
    }

    /// <summary>
    /// 居民独占的设施交互空间句柄。拓扑重建可撤销它；旧句柄释放幂等，不能释放后来者的占用。
    /// IsActive 只表示空间仍有效，是否继续工作由居民执行层决定。
    /// </summary>
    public sealed class FoundationInteractionSpaceLease : IDisposable
    {
        private FoundationInteractionSpaceRuntime _runtime;
        private ReservationLease _reservation;

        internal FoundationInteractionSpaceLease(
            FoundationInteractionSpaceRuntime runtime,
            ulong ownerId,
            InteractionSlotAddress slot,
            ReservationLease reservation)
        {
            _runtime = runtime;
            OwnerId = ownerId;
            Slot = slot;
            _reservation = reservation;
        }

        public ulong OwnerId { get; }
        public InteractionSlotAddress Slot { get; }
        public bool IsActive => _runtime != null && _reservation is { IsReleased: false };

        internal void ReleaseReservation()
        {
            _reservation?.Dispose();
            _reservation = null;
        }

        internal void ReplaceReservation(ReservationLease reservation) => _reservation = reservation;

        public void Dispose()
        {
            FoundationInteractionSpaceRuntime runtime = _runtime;
            _runtime = null;
            ReleaseReservation();
            runtime?.Release(this);
        }
    }
}
