using System;
using System.Collections.Generic;
using Game.NomadWorkshop.Simulation;
using UnityEngine;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>
    /// 水箱首条实体维修闭环。维修包仍由通用放置账本拥有；本适配层只把 Utility 方案、
    /// 精确功能点移动、工作时长和设施状态提交串成可中断行动。
    /// </summary>
    public sealed partial class NomadFoundationSystem
    {
        private PlacementRegionUseLease _activeRepairPartUse;
        private string _activeRepairTargetFacilityInstanceId = string.Empty;

        private void AddPrimaryWaterTankRepairDecisionOption(
            ICollection<FoundationResidentDecisionOption> options,
            ref PendingDecisionDiagnostic pendingDiagnostic)
        {
            if (!TryGetPrimaryWaterTankCondition(
                    out FoundationFacilityState waterTank,
                    out FacilityConditionCycle condition) ||
                condition.ActiveFault != FacilityFaultKind.OutletValveJammed)
                return;

            bool hasStockedDrinkingStation = TrySelectDrinkingStation(
                requireDrinkServing: true,
                requireRestock: false,
                out _,
                out _,
                out _);
            bool urgent = !hasStockedDrinkingStation &&
                          _residentWaterCycle.Thirst >=
                          _residentDecisionPolicy.UrgentNeedDeficit;
            ResidentDecisionRiskTier riskTier = urgent
                ? ResidentDecisionRiskTier.Urgent
                : ResidentDecisionRiskTier.Routine;
            float riskPriority = urgent ? _residentWaterCycle.Thirst : 0f;

            if (!_worldItemPlacementLedger.TryGetPlacement(
                    WaterValveRepairKitItemId,
                    out PlacementRegionItem repairKit))
            {
                AddBlockedRepairOption(
                    options,
                    waterTank,
                    ResidentActionPlanBlockReason.MissingResource,
                    "缺少水箱出水阀维修包",
                    riskTier,
                    riskPriority);
                if (urgent)
                    pendingDiagnostic.Consider(
                        $"MissingResource · {WaterValveRepairKitDefinitionId} · " +
                        "缺水风险已紧急，但维修包已经耗尽",
                        riskTier,
                        riskPriority);
                return;
            }
            if (!TryFindFacilityState(
                    repairKit.Region.OwnerEntityId,
                    out FoundationFacilityState supplyFacility) ||
                !_definitions.TryGetValue(
                    supplyFacility.DefinitionId,
                    out NomadFacilityDefinition supplyDefinition) ||
                !_definitions.TryGetValue(
                    waterTank.DefinitionId,
                    out NomadFacilityDefinition tankDefinition))
            {
                AddBlockedRepairOption(
                    options,
                    waterTank,
                    ResidentActionPlanBlockReason.PrerequisiteUnavailable,
                    "维修包所在支撑设施不存在或缺少定义",
                    riskTier,
                    riskPriority);
                return;
            }

            Vector3 start = ToNavigationPoint(_model.ResidentLocalPosition.Value);
            if (!TrySelectBestInteractionSlot(
                    start,
                    supplyFacility,
                    supplyDefinition,
                    out Vector3 supplyGoal,
                    out _,
                    out _,
                    out float supplyTravelMeters,
                    out _,
                    MaintenanceSupplyInteractionGroupId) ||
                !TrySelectBestInteractionSlot(
                    supplyGoal,
                    waterTank,
                    tankDefinition,
                    out _,
                    out _,
                    out _,
                    out float repairTravelMeters,
                    out _,
                    ServiceValveInteractionGroupId))
            {
                AddBlockedRepairOption(
                    options,
                    waterTank,
                    ResidentActionPlanBlockReason.TargetUnreachable,
                    "维修包拿取位或出水阀维修位不可达",
                    riskTier,
                    riskPriority);
                if (urgent)
                    pendingDiagnostic.Consider(
                        $"RouteUnavailable · {waterTank.InstanceId}/" +
                        ServiceValveInteractionGroupId,
                        riskTier,
                        riskPriority);
                return;
            }

            var proposal = new ResidentActionPlanProposal(
                $"repair-water-tank:{_residentActionSequence + 1}",
                "repair-water-tank",
                "拿取维修包并修复车辆水箱出水阀")
            {
                Steps = new[]
                {
                    CreateTravelStep(supplyTravelMeters, "前往水箱维护托盘"),
                    new ResidentActionStepEstimate(
                        ResidentActionStepKind.AcquireItem,
                        EstimateWorkDuration(pickupSeconds),
                        label: "拿取出水阀维修包"),
                    CreateTravelStep(repairTravelMeters, "携带维修包前往出水阀"),
                    new ResidentActionStepEstimate(
                        ResidentActionStepKind.Repair,
                        EstimateWorkDuration(waterTankRepairSeconds),
                        label: "更换卡滞的出水阀部件"),
                },
                BaseUtility = 0.08f,
                WorkUrgency = urgent ? 0.86f : 0.42f,
                DependencyValue = urgent ? 0.42f : 0.24f,
                RiskTier = riskTier,
                RiskPriority = riskPriority,
                DelayUrgency = urgent ? _residentWaterCycle.Thirst : 0.22f,
                Effort = 0.38f,
                WorkIntensity = 0.55f,
                ResourceCost = 0.08f,
                ReservationKeys = new[]
                {
                    $"item:{repairKit.ItemId}:consume",
                    $"facility:{supplyFacility.InstanceId}:{MaintenanceSupplyInteractionGroupId}",
                    $"facility:{waterTank.InstanceId}:{ServiceValveInteractionGroupId}",
                },
            };
            options.Add(new FoundationResidentDecisionOption(
                FoundationResidentDecisionKind.RepairWaterTank,
                _actionPlanEvaluator.Evaluate(
                    proposal,
                    CreateResidentDecisionCondition(),
                    _actionPlanPolicy))
            {
                SourceFacility = supplyFacility,
                TargetFacility = waterTank,
                WorldItemId = repairKit.ItemId,
            });
        }

        private void AddBlockedRepairOption(
            ICollection<FoundationResidentDecisionOption> options,
            in FoundationFacilityState waterTank,
            ResidentActionPlanBlockReason reason,
            string detail,
            ResidentDecisionRiskTier riskTier,
            float riskPriority)
        {
            var proposal = new ResidentActionPlanProposal(
                $"repair-water-tank:{_residentActionSequence + 1}:blocked",
                "repair-water-tank",
                "修复车辆水箱出水阀")
            {
                Feasibility = ResidentActionPlanFeasibility.Blocked(reason, detail),
                BaseUtility = 0.04f,
                WorkUrgency = riskTier == ResidentDecisionRiskTier.Urgent ? 0.86f : 0.42f,
                DependencyValue = 0.24f,
                RiskTier = riskTier,
                RiskPriority = riskPriority,
                DelayUrgency = riskPriority,
                ReservationKeys = new[]
                {
                    $"facility:{waterTank.InstanceId}:{ServiceValveInteractionGroupId}",
                },
            };
            options.Add(new FoundationResidentDecisionOption(
                FoundationResidentDecisionKind.RepairWaterTank,
                _actionPlanEvaluator.Evaluate(
                    proposal,
                    CreateResidentDecisionCondition(),
                    _actionPlanPolicy))
            {
                TargetFacility = waterTank,
            });
        }

        private void BeginPrimaryWaterTankRepair(
            FoundationResidentDecisionOption option)
        {
            if (!_facilityConditions.TryGetValue(
                    option.TargetFacility.InstanceId,
                    out FacilityConditionCycle condition) ||
                condition.ActiveFault != FacilityFaultKind.OutletValveJammed)
            {
                SetResidentPhase(
                    FoundationResidentPhase.Idle,
                    "水箱故障在维修开始前已经消失，重新评估");
                return;
            }
            if (!_worldItemPlacementLedger.TryReserveUse(
                    option.WorldItemId,
                    out PlacementRegionUseLease use,
                    out PlacementRegionFailure failure))
            {
                _model.LastBlocker.Value =
                    $"{failure} · 维修包在方案提交前已被占用或移走";
                SetResidentPhase(
                    FoundationResidentPhase.Idle,
                    "维修方案预留失效，稍后重新决策");
                return;
            }

            _activeRepairPartUse = use;
            _activeRepairTargetFacilityInstanceId = option.TargetFacility.InstanceId;
            _residentActionSequence++;
            _model.LastBlocker.Value = string.Empty;
            TryBeginMove(
                FoundationResidentPhase.MovingToRepairPart,
                "统一 Utility 已选择维修：先前往维护托盘拿取实体备件",
                option.SourceFacility,
                allowAlternativeFacility: false,
                interactionGroupId: MaintenanceSupplyInteractionGroupId);
        }

        private void CompleteRepairPartPickup()
        {
            PlacementRegionFailure failure = PlacementRegionFailure.InvalidRequest;
            if (_activeRepairPartUse == null ||
                !_activeRepairPartUse.TryPickUp(out failure))
            {
                Block(
                    $"维修包拿取事务已经失效：{failure}",
                    ResourceFlowBlocker.None);
                return;
            }

            PlacementRegionItem source = _activeRepairPartUse.Source;
            _model.ResidentCarriedWorldItem.Value = new FoundationCarriedWorldItemState(
                source.ItemId,
                source.Footprint.DefinitionId);
            PublishWorldItemPlacements();
            if (!TryFindFacilityState(
                    _activeRepairTargetFacilityInstanceId,
                    out FoundationFacilityState target) ||
                !_facilityConditions.TryGetValue(
                    target.InstanceId,
                    out FacilityConditionCycle condition) ||
                condition.ActiveFault != FacilityFaultKind.OutletValveJammed)
            {
                ReleaseActiveTasks();
                ClearActivePath();
                SetResidentPhase(
                    FoundationResidentPhase.Idle,
                    "水箱维修目标已经失效，维修包已放回原位");
                _residentDecisionRetryRemaining = 0f;
                return;
            }

            TryBeginMove(
                FoundationResidentPhase.MovingToRepairTarget,
                "携带维修包前往水箱出水阀维修位",
                target,
                allowAlternativeFacility: false,
                interactionGroupId: ServiceValveInteractionGroupId);
        }

        private void CompletePrimaryWaterTankRepair()
        {
            if (_activeRepairPartUse == null ||
                _activeRepairPartUse.State != PlacementRegionUseState.Carrying ||
                !_facilityConditions.TryGetValue(
                    _activeRepairTargetFacilityInstanceId,
                    out FacilityConditionCycle condition) ||
                condition.ActiveFault != FacilityFaultKind.OutletValveJammed)
            {
                ReleaseActiveTasks();
                ClearActivePath();
                SetResidentPhase(
                    FoundationResidentPhase.Idle,
                    "维修提交条件已经变化，维修包已安全回滚");
                _residentDecisionRetryRemaining = 0f;
                return;
            }

            SettleCondition(condition, NomadFacilityFunction.VehicleWaterTank);
            if (!_activeRepairPartUse.TryConsume(out PlacementRegionFailure failure))
                throw new InvalidOperationException(
                    $"水箱维修已到提交点，但维修包消耗事务失效：{failure}。");
            _activeRepairPartUse = null;
            if (!condition.Repair())
                throw new InvalidOperationException(
                    "水箱维修包已经提交，但设施故障状态未能修复。");

            _activeRepairTargetFacilityInstanceId = string.Empty;
            _model.ResidentCarriedWorldItem.Value = default;
            PublishWorldItemPlacements();
            WriteFacilityConditionProjection();
            _model.CompletedWaterTankRepairCount.Value++;
            _model.LastBlocker.Value = string.Empty;
            _lastPublishedDecisionDiagnostic = string.Empty;
            ReleaseActiveInteractionSpace(publishProjection: true);
            SetResidentPhase(
                FoundationResidentPhase.Idle,
                "已消耗一份维修包并完成水箱出水阀维修");
            _residentDecisionRetryRemaining = 0f;
        }

        private void CancelActiveFacilityRepair()
        {
            if (_activeRepairPartUse != null)
            {
                _activeRepairPartUse.Dispose();
                _activeRepairPartUse = null;
                if (_model != null &&
                    string.Equals(
                        _model.ResidentCarriedWorldItem.Value.ItemId,
                        WaterValveRepairKitItemId,
                        StringComparison.Ordinal))
                    _model.ResidentCarriedWorldItem.Value = default;
                PublishWorldItemPlacements();
            }
            _activeRepairTargetFacilityInstanceId = string.Empty;
        }
    }
}
