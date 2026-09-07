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
                          _resident.WaterCycle.Thirst >=
                          _residentDecisionPolicy.UrgentNeedDeficit;
            ResidentDecisionRiskTier riskTier = urgent
                ? ResidentDecisionRiskTier.Urgent
                : ResidentDecisionRiskTier.Routine;
            float riskPriority = urgent ? _resident.WaterCycle.Thirst : 0f;

            if (!TrySelectRepairKit(waterTank, out PlacementRegionItem repairKit))
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

            Vector3 start = ToNavigationPoint(_resident.State.ResidentLocalPosition.Value);
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
                $"repair-water-tank:{_resident.StableId}:{_resident.ActionSequence + 1}",
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
                DelayUrgency = urgent ? _resident.WaterCycle.Thirst : 0.22f,
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

        private bool TrySelectRepairKit(in FoundationFacilityState waterTank, out PlacementRegionItem selected)
        {
            selected = null;
            PlacementRegionItem fallback = null;
            float best = float.PositiveInfinity;
            foreach (var item in _worldItemPlacementLedger.CreateStableSnapshot())
            {
                if (item.Footprint.DefinitionId != WaterValveRepairKitDefinitionId ||
                    !TryFindFacilityState(item.Region.OwnerEntityId, out var facility) ||
                    !_definitions.TryGetValue(facility.DefinitionId, out var definition)) continue;
                if (fallback == null || string.CompareOrdinal(item.ItemId, fallback.ItemId) < 0) fallback = item;
                if (!TrySelectBestInteractionSlot(ToNavigationPoint(_resident.State.ResidentLocalPosition.Value), facility, definition,
                        out var point, out _, out _, out float approach, out _, MaintenanceSupplyInteractionGroupId) ||
                    !TryMeasureFacilityPath(point, waterTank, _definitions[waterTank.DefinitionId],
                        out float work, ServiceValveInteractionGroupId)) continue;
                float travel = approach + work;
                if (travel > best || travel == best && selected != null &&
                    string.CompareOrdinal(item.ItemId, selected.ItemId) >= 0) continue;
                best = travel;
                selected = item;
            }
            // 有物资却不可达时仍交给原方案生成精确的路径阻塞，不能冒充已耗尽。
            selected ??= fallback;
            return selected != null;
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
                $"repair-water-tank:{_resident.StableId}:{_resident.ActionSequence + 1}:blocked",
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
                _resident.State.LastBlocker.Value =
                    $"{failure} · 维修包在方案提交前已被占用或移走";
                SetResidentPhase(
                    FoundationResidentPhase.Idle,
                    "维修方案预留失效，稍后重新决策");
                return;
            }

            _resident.ActiveRepairPartUse = use;
            _resident.ActiveRepairTargetFacilityInstanceId = option.TargetFacility.InstanceId;
            _resident.ActionSequence++;
            _resident.State.LastBlocker.Value = string.Empty;
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
            if (_resident.ActiveRepairPartUse == null ||
                !_resident.ActiveRepairPartUse.TryPickUp(out failure))
            {
                Block(
                    $"维修包拿取事务已经失效：{failure}",
                    ResourceFlowBlocker.None);
                return;
            }

            PlacementRegionItem source = _resident.ActiveRepairPartUse.Source;
            _resident.State.ResidentCarriedWorldItem.Value = new FoundationCarriedWorldItemState(
                source.ItemId,
                source.Footprint.DefinitionId);
            PublishWorldItemPlacements();
            if (!TryFindFacilityState(
                    _resident.ActiveRepairTargetFacilityInstanceId,
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
                _resident.DecisionRetryRemaining = 0f;
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
            if (_resident.ActiveRepairPartUse == null ||
                _resident.ActiveRepairPartUse.State != PlacementRegionUseState.Carrying ||
                !_facilityConditions.TryGetValue(
                    _resident.ActiveRepairTargetFacilityInstanceId,
                    out FacilityConditionCycle condition) ||
                condition.ActiveFault != FacilityFaultKind.OutletValveJammed)
            {
                ReleaseActiveTasks();
                ClearActivePath();
                SetResidentPhase(
                    FoundationResidentPhase.Idle,
                    "维修提交条件已经变化，维修包已安全回滚");
                _resident.DecisionRetryRemaining = 0f;
                return;
            }

            SettleCondition(condition, NomadFacilityFunction.VehicleWaterTank);
            if (!_resident.ActiveRepairPartUse.TryConsume(out PlacementRegionFailure failure))
                throw new InvalidOperationException(
                    $"水箱维修已到提交点，但维修包消耗事务失效：{failure}。");
            _resident.ActiveRepairPartUse = null;
            if (!condition.Repair())
                throw new InvalidOperationException(
                    "水箱维修包已经提交，但设施故障状态未能修复。");

            _resident.ActiveRepairTargetFacilityInstanceId = string.Empty;
            _resident.State.ResidentCarriedWorldItem.Value = default;
            PublishWorldItemPlacements();
            WriteFacilityConditionProjection();
            _resident.State.CompletedWaterTankRepairCount.Value++;
            _resident.State.LastBlocker.Value = string.Empty;
            _resident.LastPublishedDecisionDiagnostic = string.Empty;
            ReleaseActiveInteractionSpace(publishProjection: true);
            SetResidentPhase(
                FoundationResidentPhase.Idle,
                "已消耗一份维修包并完成水箱出水阀维修");
            _resident.DecisionRetryRemaining = 0f;
        }

    }
}
