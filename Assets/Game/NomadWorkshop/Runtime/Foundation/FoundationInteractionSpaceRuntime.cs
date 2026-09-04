using System;
using System.Collections.Generic;
using Game.NomadWorkshop.Simulation;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>
    /// Foundation 设施停靠空间的运行时所有者：从设施定义重建 committed / preview 拓扑，生成 View 掩码，
    /// 并把一个空间的全部 Slot 键作为原子租约占用。System 只决定行动和建造时机，不接触聚类细节。
    /// </summary>
    public sealed class FoundationInteractionSpaceRuntime : IDisposable
    {
        private readonly InteractionSpaceTopology _committed;
        private readonly InteractionSpaceTopology _preview;
        private readonly ReservationLedger _reservations = new();
        private readonly List<InteractionSpaceSlot> _committedSlots = new();
        private readonly List<InteractionSpaceSlot> _previewSlots = new();
        private ReservationLease _activeLease;
        private InteractionSlotAddress _activeSlot;
        private bool _hasActiveSlot;

        public FoundationInteractionSpaceRuntime(int mergeDistanceMillimeters)
        {
            _committed = new InteractionSpaceTopology(mergeDistanceMillimeters);
            _preview = new InteractionSpaceTopology(mergeDistanceMillimeters);
        }

        public bool HasActiveLease => _activeLease != null;

        /// <summary>
        /// 设施集合改变后重建正式拓扑。若居民正占用一个 Slot，可把旧地址迁移到新聚类后的整组键；
        /// 返回 false 表示旧 Slot 已消失或新空间被其他居民占用。
        /// </summary>
        public bool RebuildCommitted(
            IReadOnlyList<FoundationFacilityState> facilities,
            IReadOnlyDictionary<string, NomadFacilityDefinition> definitions,
            bool reacquireActiveSpace,
            ulong activeOwnerId)
        {
            if (facilities == null) throw new ArgumentNullException(nameof(facilities));
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));

            bool restore = reacquireActiveSpace && _hasActiveSlot;
            InteractionSlotAddress previous = _activeSlot;
            Release();
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

            if (!restore) return true;
            if (!_committed.TryGetSpace(previous, out InteractionSpace space) ||
                !_reservations.TryAcquire(
                    activeOwnerId,
                    space.ReservationKeys,
                    out _activeLease))
                return false;

            _activeSlot = previous;
            _hasActiveSlot = true;
            return true;
        }

        public void RebuildPreview(
            in ContinuousFacilityPlacementRequest candidate,
            NomadFacilityDefinition candidateDefinition)
        {
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
            _committed.TryGetSpace(address, out InteractionSpace space) &&
            !IsOccupied(space);

        public bool TryAcquire(ulong ownerId, in InteractionSpaceSlot slot)
        {
            if (_activeLease != null ||
                !_committed.TryGetSpace(slot.Address, out InteractionSpace space) ||
                !_reservations.TryAcquire(
                    ownerId,
                    space.ReservationKeys,
                    out _activeLease))
                return false;

            _activeSlot = slot.Address;
            _hasActiveSlot = true;
            return true;
        }

        /// <summary>释放当前居民占用；返回值可用于决定是否刷新 Inspector / 建造诊断投影。</summary>
        public bool Release()
        {
            bool changed = _activeLease != null || _hasActiveSlot;
            _activeLease?.Dispose();
            _activeLease = null;
            _activeSlot = default;
            _hasActiveSlot = false;
            return changed;
        }

        public void Dispose() => Release();

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
}
