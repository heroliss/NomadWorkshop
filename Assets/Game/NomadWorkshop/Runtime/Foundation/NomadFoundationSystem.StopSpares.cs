using System;
using System.Collections.Generic;
using Game.NomadWorkshop.Simulation;
using Game.NomadWorkshop.Simulation.Persistence;
using UnityEngine;

namespace Game.NomadWorkshop.Foundation
{
    public sealed partial class NomadFoundationSystem
    {
        internal const string StopSupplyOwnerId = "site:dry-river";
        private const string StopSupplyRegionLocalId = "spares";
        private bool _stopSpareRequested;
        private bool IsStopSpareVisit => _resident.StopVisit?.Kind == FoundationStopVisitKind.SpareCollection;
        private Vector3 StopSparePoint => StopWaterPoint + new Vector3(1.2f, 0f, 0.8f);

        /// <summary>派遣一次实体备件补给，或召回外勤；已拿取的包先交付到预留托盘。命令不生成物资。</summary>
        public bool RequestStopSpare(bool collect)
        {
            if (_checkpointOperation != null) return false;
            if (!_initialized) return false;
            var visitor = FindStopVisitor(FoundationStopVisitKind.SpareCollection);
            var anyVisitor = FindStopVisitor();
            if (collect && (_stopWaterRequested || _stopWasteRequested ||
                            anyVisitor != null && visitor == null)) return false;
            if (collect && visitor != null) return _stopSpareRequested;
            if (collect && (!IsAtDryRiver || _journey.Destination == NomadJourneyEndpoint.Origin ||
                            !HasLivingResident ||
                            _model.BuildTransactionPhase.Value != FoundationBuildTransactionPhase.Idle)) return false;
            bool hadRequest = _stopSpareRequested || visitor != null;
            _stopSpareRequested = collect;
            if (!collect && visitor != null) visitor.StopVisit.ReturnRequested = true;
            if (collect || hadRequest) _model.StopWorkFeedback.Value = collect
                ? "备件请求已接受，等待居民完成手头工作" : "已召回补给者；拿取的备件会先送回维护托盘";
            WakeResidents();
            WriteStopSpareProjection();
            return true;
        }

        private void RegisterStopSupplyRegion(PlacementRegionLedger ledger)
        {
            Vector3 center = StopSparePoint + Vector3.forward * 0.55f;
            var region = new PlacementRegionDefinition(
                PlacementRegionLedger.ComposeRegionId(StopSupplyOwnerId, StopSupplyRegionLocalId),
                StopSupplyRegionLocalId, StopSupplyOwnerId, deckLayout.LocalToPose(center),
                1100, 550, 700, 20, new[] { "spare-part" });
            var failure = ledger.RegisterRegion(region);
            if (failure != PlacementRegionFailure.None)
                throw new InvalidOperationException($"驿站备件台无法登记：{failure}。");
        }

        private static string StopSpareItemId(int index) => $"dry-river-valve-kit-{index:00}";

        private void AddInitialStopSpares(PlacementRegionLedger ledger, List<NomadWorldItemSaveData> saved = null)
        {
            for (var i = 1; i <= 2; i++)
            {
                string id = StopSpareItemId(i);
                var pose = new PlacementRegionPose(i == 1 ? -250 : 250, 0, 0);
                string region = PlacementRegionLedger.ComposeRegionId(StopSupplyOwnerId, StopSupplyRegionLocalId);
                if (!ledger.TryRestorePlacement(id, GetRequiredWorldItemFootprint(WaterValveRepairKitDefinitionId),
                        region, pose, out _, out var failure))
                    throw new InvalidOperationException($"驿站初始备件 {id} 无法落位：{failure}。");
                saved?.Add(new NomadWorldItemSaveData { ItemId = id, DefinitionId = WaterValveRepairKitDefinitionId,
                    OwnerEntityId = StopSupplyOwnerId, PlacementRegionId = region,
                    PlacementLocalPose = QuantizedPlacementPose.FromPlacementPose(pose) });
            }
        }

        private void ResetStopSpares()
        {
            _stopSpareRequested = false;
            RegisterStopSupplyRegion(_worldItemPlacementLedger);
            AddInitialStopSpares(_worldItemPlacementLedger);
        }

        private void AddStopSpareDecisionOption(ICollection<FoundationResidentDecisionOption> options)
        {
            if (FindStopVisitor() != null) return;
            if (!_stopSpareRequested || !_stopAccessOpen || !IsAtDryRiver ||
                _journey.Destination == NomadJourneyEndpoint.Origin || !_resident.Wellbeing.IsAlive ||
                _resident.Wellbeing.Health <= 0.1f || _resident.Wellbeing.Fatigue >= 0.9f ||
                _model.InteractionMode.Value == FoundationInteractionMode.Build) return;

            PlacementRegionItem source = null;
            foreach (var item in _worldItemPlacementLedger.CreateStableSnapshot())
                if (item.Region.OwnerEntityId == StopSupplyOwnerId &&
                    item.Footprint.DefinitionId == WaterValveRepairKitDefinitionId &&
                    (source == null || string.CompareOrdinal(item.ItemId, source.ItemId) < 0)) source = item;
            if (source == null) { RejectStopSpare("驿站备件已耗尽"); return; }
            if (!TryGetPrimaryWaterTankCondition(out var tank, out _) || !_definitions.TryGetValue(tank.DefinitionId, out var definition))
            { RejectStopSpare("没有可接收备件的车辆水箱"); return; }
            string targetRegion = PlacementRegionLedger.ComposeRegionId(tank.InstanceId, MaintenanceTrayRegionLocalId);
            // 候选只探测可落位性，立即释放；行动被选中后才持有联合预留。
            if (!_worldItemPlacementLedger.TryReserveMoveStable(source.ItemId, targetRegion, out var probe, out var failure))
            { RejectStopSpare($"维护托盘无法容纳备件：{failure}"); return; }
            probe.Dispose();
            if (!TryCalculateTravelPath(ToNavigationPoint(_resident.State.ResidentLocalPosition.Value), StopSparePoint, out var outward) ||
                !TryMeasureFacilityPath(StopSparePoint, tank, definition,
                    out float home, MaintenanceSupplyInteractionGroupId))
            {
                _model.StopWorkFeedback.Value = "等待可用的备件通路与维护托盘工作位，保留补给请求";
                return;
            }
            bool urgent = _resident.WaterCycle.Thirst >= _residentDecisionPolicy.UrgentNeedDeficit &&
                          TryGetPrimaryWaterTankCondition(out _, out var condition) &&
                          condition.ActiveFault == FacilityFaultKind.OutletValveJammed;
            var proposal = new ResidentActionPlanProposal($"stop-spare:{_resident.StableId}:{_resident.ActionSequence + 1}",
                "stop-spare", "取回驿站实体维修包并放入维护托盘")
            {
                Steps = new[] { CreateTravelStep(outward.PathLength + home, "往返驿站备件台"),
                    new ResidentActionStepEstimate(ResidentActionStepKind.Transfer,
                        EstimateWorkDuration(pickupSeconds + deliverySeconds), label: "拿取与交付备件") },
                BaseUtility = 0.12f, PlayerPriority = 1f, WorkUrgency = 0.8f, DependencyValue = 0.9f,
                Effort = 0.2f, WorkIntensity = 0.5f,
                RiskTier = urgent ? ResidentDecisionRiskTier.Urgent : ResidentDecisionRiskTier.Routine,
                RiskPriority = urgent ? _resident.WaterCycle.Thirst : 0f,
            };
            options.Add(new FoundationResidentDecisionOption(FoundationResidentDecisionKind.StopSpare,
                _actionPlanEvaluator.Evaluate(proposal, CreateResidentDecisionCondition(), _actionPlanPolicy))
            { TargetFacility = tank, WorldItemId = source.ItemId });
        }

        private void RejectStopSpare(string reason)
        {
            _stopSpareRequested = false;
            _model.StopWorkFeedback.Value = reason;
            _resident.State.LastBlocker.Value = reason;
        }

        private void BeginStopSpare(FoundationResidentDecisionOption option)
        {
            string targetRegion = PlacementRegionLedger.ComposeRegionId(option.TargetFacility.InstanceId, MaintenanceTrayRegionLocalId);
            if (!_worldItemPlacementLedger.TryReserveMoveStable(option.WorldItemId, targetRegion,
                    out var move, out var failure)) { RejectStopSpare($"备件预留失效：{failure}"); return; }
            _resident.ActiveWorldItemMove = move;
            _resident.StopVisit = new FoundationStopVisitExecution(FoundationStopVisitKind.SpareCollection,
                deckLayout.LocalToPose(_resident.State.ResidentLocalPosition.Value, _resident.State.ResidentLocalYawDegrees.Value),
                option.TargetFacility.InstanceId);
            _resident.LastDeckCheckpointPose = _resident.StopVisit.CheckpointPose;
            _resident.ActionSequence++;
            ReleaseActiveInteractionSpace(publishProjection: true);
            ClearActiveMoveIntent();
            if (!TryAssignTravelPath(StopSparePoint)) { BeginStopSpareReturn(); return; }
            _resident.HasDockingYaw = true;
            _resident.DockingYawDegrees = 0f;
            SetResidentPhase(FoundationResidentPhase.MovingToStopSpare, "前往驿站备件台；已预留车辆落位");
            _model.StopWorkFeedback.Value = "补给者正在前往驿站，来源与维护托盘已预留";
        }

        private void CompleteStopSparePickup()
        {
            if (!_resident.ActiveWorldItemMove.TryPickUp(out var failure))
                throw new InvalidOperationException($"驿站备件拿取失效：{failure}。");
            var source = _resident.ActiveWorldItemMove.Source;
            _resident.State.ResidentCarriedWorldItem.Value = new FoundationCarriedWorldItemState(source.ItemId, source.Footprint.DefinitionId);
            PublishWorldItemPlacements();
            BeginStopSpareReturn();
        }

        private void BeginStopSpareReturn()
        {
            ClearActivePath();
            if (!TryFindFacilityState(_resident.StopVisit.HomeFacilityId, out var tank))
                throw new InvalidOperationException("补给期间维护托盘被移除。");
            TryBeginMove(FoundationResidentPhase.ReturningStopSpare, "返回车辆维护托盘；归车之前保持停车",
                tank, allowAlternativeFacility: false, interactionGroupId: MaintenanceSupplyInteractionGroupId);
        }

        private void AdvanceStopSpareRecall()
        {
            if (_resident.StopVisit.ReturnRequested && _resident.Phase is
                FoundationResidentPhase.MovingToStopSpare or FoundationResidentPhase.PickingUpStopSpare)
                BeginStopSpareReturn();
        }

        private void CompleteStopSpareDelivery()
        {
            bool carried = _resident.ActiveWorldItemMove.State == PlacementRegionMoveState.Carrying;
            if (carried && !_resident.ActiveWorldItemMove.TryDeliver(out _, out var failure))
                throw new InvalidOperationException($"已到维护托盘但备件无法交付：{failure}。");
            ReleaseActiveTasks();
            _stopSpareRequested = false;
            SetResidentPhase(FoundationResidentPhase.Idle, carried ? "备件已放入维护托盘，补给者已归车" : "补给已取消，备件仍在驿站");
            _model.StopWorkFeedback.Value = _resident.State.CurrentTask.Value;
            _resident.DecisionRetryRemaining = 0f;
        }

        private void WriteStopSpareProjection()
        {
            int stock = 0;
            if (_worldItemPlacementLedger != null)
                foreach (var item in _worldItemPlacementLedger.CreateStableSnapshot())
                    if (item.Region.OwnerEntityId == StopSupplyOwnerId && item.Footprint.DefinitionId == WaterValveRepairKitDefinitionId) stock++;
            _model.StopSpareCount.Value = stock;
            _model.StopSpareRequested.Value = _stopSpareRequested;
            _model.StopSpareActive.Value = FindStopVisitor(FoundationStopVisitKind.SpareCollection) != null;
        }
    }
}
