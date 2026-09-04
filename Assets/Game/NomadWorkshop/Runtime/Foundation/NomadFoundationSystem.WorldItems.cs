using System;
using System.Collections.Generic;
using Game.NomadWorkshop.Simulation;
using UnityEngine;

namespace Game.NomadWorkshop.Foundation
{
    public sealed partial class NomadFoundationSystem
    {
        private PlacementRegionMoveLease _activeWorldItemMove;

        /// <summary>
        /// 开发期最小验证入口：让居民把首只杯子移到另一座兼容设施，若不存在则在同一台面换位。
        /// 它只验证通用空间事务和执行器，不把“反复整理杯子”冒充正式 Utility AI 需求。
        /// </summary>
        public bool TryStartCupMoveHarness()
        {
            if (!_initialized || _model == null || _worldItemPlacementLedger == null)
                return false;
            if (!_residentWellbeing.IsAlive)
            {
                _model.LastBlocker.Value = "ResidentDied · 死亡居民不能拿取世界物品";
                return false;
            }
            if (_model.BuildTransactionPhase.Value != FoundationBuildTransactionPhase.Idle ||
                _residentPhase is not (FoundationResidentPhase.Idle or
                    FoundationResidentPhase.WaitingForFacility) ||
                _activeWorldItemMove != null ||
                _activeHaul != null ||
                _activeResidentAction != null)
            {
                _model.LastBlocker.Value = "ActionBusy · 等待当前居民或建造事务回到安全边界";
                return false;
            }
            if (!_worldItemPlacementLedger.TryGetPlacement(
                    StarterCupItemId,
                    out PlacementRegionItem sourcePlacement))
            {
                _model.LastBlocker.Value = "ItemNotLocated · 请先建造带台面的野战厨房";
                return false;
            }
            if (!TryFindFacilityState(
                    sourcePlacement.Region.OwnerEntityId,
                    out FoundationFacilityState sourceFacility))
            {
                _model.LastBlocker.Value =
                    $"OwnerMissing · 杯具支撑设施 {sourcePlacement.Region.OwnerEntityId} 不存在";
                return false;
            }
            if (!TryReserveCupDestination(
                    sourcePlacement,
                    out PlacementRegionMoveLease move,
                    out FoundationFacilityState destinationFacility,
                    out PlacementRegionFailure failure))
            {
                _model.LastBlocker.Value =
                    $"{failure} · 没有可为 {StarterCupItemId} 联合预留的目标台面姿态";
                return false;
            }

            _activeWorldItemMove = move;
            _model.LastBlocker.Value = string.Empty;
            TryBeginMove(
                FoundationResidentPhase.MovingToWorldItemSource,
                $"杯具事务已锁定来源与目标：前往 {sourceFacility.InstanceId} 拿取",
                sourceFacility,
                allowAlternativeFacility: false,
                interactionGroupId: KitchenInteractionGroupId);
            if (_activeWorldItemMove == null)
                return false;

            // 目标设施已被空间事务锁定，随后即使路径重试也不再临时换成另一个台面。
            _model.CurrentTask.Value += $" → 目标 {destinationFacility.InstanceId}";
            return true;
        }

        private bool TryReserveCupDestination(
            PlacementRegionItem source,
            out PlacementRegionMoveLease move,
            out FoundationFacilityState destinationFacility,
            out PlacementRegionFailure failure)
        {
            move = null;
            destinationFacility = default;
            failure = PlacementRegionFailure.RegionUnavailable;
            IReadOnlyList<FoundationFacilityState> facilities = _model.Facilities;

            // 先尝试另一座兼容设施，才能在有两座台面时实际观察携带移动；没有时再在原台面换位。
            for (var sameOwnerPass = 0; sameOwnerPass < 2; sameOwnerPass++)
            {
                bool requireSameOwner = sameOwnerPass == 1;
                for (var facilityIndex = 0; facilityIndex < facilities.Count; facilityIndex++)
                {
                    FoundationFacilityState facility = facilities[facilityIndex];
                    bool isSameOwner = string.Equals(
                        facility.InstanceId,
                        source.Region.OwnerEntityId,
                        StringComparison.Ordinal);
                    if (isSameOwner != requireSameOwner ||
                        !_definitions.TryGetValue(
                            facility.DefinitionId,
                            out NomadFacilityDefinition definition))
                        continue;

                    IReadOnlyList<NomadPlacementRegionDefinition> regions =
                        definition.PlacementRegions;
                    for (var regionIndex = 0; regionIndex < regions.Count; regionIndex++)
                    {
                        string targetRegionId = PlacementRegionLedger.ComposeRegionId(
                            facility.InstanceId,
                            regions[regionIndex].RegionId);
                        if (!_worldItemPlacementLedger.TryReserveMoveStable(
                                source.ItemId,
                                targetRegionId,
                                out move,
                                out failure))
                            continue;

                        destinationFacility = facility;
                        return true;
                    }
                }
            }
            return false;
        }

        private void CompleteWorldItemPickup()
        {
            PlacementRegionFailure failure = PlacementRegionFailure.InvalidRequest;
            if (_activeWorldItemMove == null ||
                !_activeWorldItemMove.TryPickUp(out failure))
            {
                Block(
                    $"杯具拿取事务已经失效：{failure}",
                    ResourceFlowBlocker.None);
                return;
            }

            PlacementRegionItem source = _activeWorldItemMove.Source;
            _model.ResidentCarriedWorldItem.Value = new FoundationCarriedWorldItemState(
                source.ItemId,
                source.Footprint.DefinitionId);
            PublishWorldItemPlacements();

            PlacementRegionItem destination = _activeWorldItemMove.Destination;
            if (string.Equals(
                    source.Region.OwnerEntityId,
                    destination.Region.OwnerEntityId,
                    StringComparison.Ordinal))
            {
                BeginTimedPhase(
                    FoundationResidentPhase.PlacingWorldItem,
                    deliverySeconds,
                    "已拿起杯具，正在同一台面的预留姿态放下");
                return;
            }
            if (!TryFindFacilityState(
                    destination.Region.OwnerEntityId,
                    out FoundationFacilityState destinationFacility))
            {
                Block(
                    $"杯具目标设施 {destination.Region.OwnerEntityId} 在搬运中消失",
                    ResourceFlowBlocker.None);
                return;
            }

            TryBeginMove(
                FoundationResidentPhase.MovingToWorldItemDestination,
                $"手持杯具前往已预留目标 {destinationFacility.InstanceId}",
                destinationFacility,
                allowAlternativeFacility: false,
                interactionGroupId: KitchenInteractionGroupId);
        }

        private void CompleteWorldItemPlacement()
        {
            PlacementRegionMoveLease move = _activeWorldItemMove;
            PlacementRegionItem placement = null;
            PlacementRegionFailure failure = PlacementRegionFailure.InvalidRequest;
            if (move == null ||
                !move.TryDeliver(
                    out placement,
                    out failure))
            {
                Block(
                    $"杯具放下事务已经失效：{failure}",
                    ResourceFlowBlocker.None);
                return;
            }

            _activeWorldItemMove = null;
            _model.ResidentCarriedWorldItem.Value = default;
            PublishWorldItemPlacements();
            _model.CompletedWorldItemMoveCount.Value++;
            _model.LastBlocker.Value = string.Empty;
            ReleaseActiveInteractionSpace(publishProjection: true);
            SetResidentPhase(
                FoundationResidentPhase.Idle,
                $"杯具已原子放到 {placement.Region.OwnerEntityId}/{placement.Region.LocalRegionId}");
            _residentDecisionRetryRemaining = Mathf.Max(0.1f, residentDecisionRetrySeconds);
        }

        private void CancelActiveWorldItemMove()
        {
            if (_activeWorldItemMove == null) return;
            _activeWorldItemMove.Dispose();
            _activeWorldItemMove = null;
            if (_model != null)
                _model.ResidentCarriedWorldItem.Value = default;
            PublishWorldItemPlacements();
        }

        private bool TryFindFacilityState(
            string instanceId,
            out FoundationFacilityState state)
        {
            if (!string.IsNullOrWhiteSpace(instanceId))
            {
                IReadOnlyList<FoundationFacilityState> facilities = _model.Facilities;
                for (var i = 0; i < facilities.Count; i++)
                {
                    if (!string.Equals(
                            facilities[i].InstanceId,
                            instanceId,
                            StringComparison.Ordinal))
                        continue;
                    state = facilities[i];
                    return true;
                }
            }

            state = default;
            return false;
        }
    }
}
