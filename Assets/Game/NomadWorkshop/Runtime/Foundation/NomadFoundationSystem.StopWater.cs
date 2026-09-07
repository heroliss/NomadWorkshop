using System;
using System.Collections.Generic;
using Game.NomadWorkshop.Simulation;
using Game.NomadWorkshop.Simulation.Persistence;
using UnityEngine;

namespace Game.NomadWorkshop.Foundation
{
    public sealed partial class NomadFoundationSystem
    {
        private const string DryRiverSiteId = "dry-river";
        private const int InitialStopWaterMilliliters = 20_000;
        private ResourceInventory _stopWater;
        private bool _stopWaterRequested;
        private bool _stopAccessOpen;

        // 取水区在甲板右侧，只在停靠时接通。它不是可建造的甲板设施。
        private Vector3 StopWaterPoint => deckLayout.DeckCenterLocal +
            new Vector3(deckLayout.DeckSize.x * 0.5f + 3f, 0f, 0f);
        private bool IsAtDryRiver => _journey.PositionMicrometers == FoundationRoute.LengthMicrometers;
        private bool IsResidentAboard => IsResidentPoseClear(deckLayout.LocalToPose(_resident.State.ResidentLocalPosition.Value));
        private bool IsStopWaterVisit => _resident.StopVisit?.Kind == FoundationStopVisitKind.WaterCollection;
        private bool IsStopWasteVisit => _resident.StopVisit?.Kind == FoundationStopVisitKind.WasteDisposal;

        /// <summary>请求一次至多 2 L 的实体取水往返，或召回当前取水者。命令不转移资源。</summary>
        public bool RequestStopWater(bool collect)
        {
            if (_checkpointOperation != null) return false;
            if (!_initialized) return false;
            var visitor = FindStopVisitor(FoundationStopVisitKind.WaterCollection);
            var anyVisitor = FindStopVisitor();
            if (collect && (_stopWasteRequested || FindStopVisitor(FoundationStopVisitKind.WasteDisposal) != null || _stopSpareRequested || FindStopVisitor(FoundationStopVisitKind.SpareCollection) != null)) return false;
            if (collect && anyVisitor != null) return _stopWaterRequested;
            if (collect && (!IsAtDryRiver || _journey.Destination == NomadJourneyEndpoint.Origin ||
                            _model.BuildTransactionPhase.Value != FoundationBuildTransactionPhase.Idle))
            {
                _model.StopWorkFeedback.Value = "需要先停靠干河驿站并结束建造，才能派出取水者";
                return false;
            }
            bool hadRequest = _stopWaterRequested || visitor != null;
            _stopWaterRequested = collect;
            if (collect || hadRequest)
                _model.StopWorkFeedback.Value = collect ? "取水请求已接受，等待居民完成手头工作" : "已停止取水，作业者将带水罐归车";
            if (!collect && visitor != null) visitor.StopVisit.ReturnRequested = true;
            WakeResidents();
            WriteStopWaterProjection();
            return true;
        }

        private void ResetStopWater()
        {
            _stopWater = new ResourceInventory("site:dry-river:water", ResourceMeasure.Milliliter,
                InitialStopWaterMilliliters, new ResourceQuantity(NomadResourceIds.Water, InitialStopWaterMilliliters));
            _stopWaterRequested = false;
            _model.StopWorkFeedback.Value = string.Empty;
            ResetStopWaste();
            ResetStopSpares();
            SetStopAccess(false, rebuild: false);
        }

        private void SetStopAccess(bool accessible, bool rebuild = true)
        {
            bool changed = _stopAccessOpen != accessible;
            _stopAccessOpen = accessible;
            _navigation.SetDockedApron(StopWaterPoint - Vector3.right, new Vector3(6f, 0.2f, 4f), accessible);
            if (changed && rebuild)
            {
                SuspendNativeLocomotion();
                _navigation.BuildNow();
                RebindNativeLocomotion();
            }
            _model.StopAccessOpen.Value = accessible;
        }

        private void AddStopWaterDecisionOption(ICollection<FoundationResidentDecisionOption> options)
        {
            if (FindStopVisitor() != null) return;
            if (!_stopWaterRequested || !IsAtDryRiver || !_stopAccessOpen || !CanStartDriving() ||
                _journey.Destination == NomadJourneyEndpoint.Origin ||
                _model.InteractionMode.Value == FoundationInteractionMode.Build ||
                _waterCanLocation == FoundationWaterCanLocation.Resident ||
                _waterCan.TotalAmount != 0 ||
                !TryFindFacilityState(_waterCanAnchorFacilityInstanceId, out var canFacility) ||
                !TryFindFacilityByInstanceId(FindRequiredFacilityInstanceId(NomadFacilityFunction.VehicleWaterTank),
                    NomadFacilityFunction.VehicleWaterTank, out var tank)) return;

            int amount = Math.Min(WaterHaulBatchMilliliters, Math.Min(_resourceFlow.GetAvailableAmount(
                _stopWater, NomadResourceIds.Water), Math.Min(_resourceFlow.GetAvailableCapacity(_waterCan),
                _resourceFlow.GetAvailableCapacity(_vehicleWater))));
            if (amount <= 0)
            {
                _stopWaterRequested = false;
                _resident.State.LastBlocker.Value = _stopWater.TotalAmount == 0
                    ? "干河驿站的水源已取尽" : "车辆水箱或水罐没有可用容量";
                _model.StopWorkFeedback.Value = _resident.State.LastBlocker.Value;
                return;
            }
            if (!EvaluateWaterCanCargo(amount).IsFeasible)
            {
                _stopWaterRequested = false;
                _resident.State.LastBlocker.Value = "需要可用的防漏水罐才能从驿站搬运水";
                _model.StopWorkFeedback.Value = _resident.State.LastBlocker.Value;
                return;
            }
            if (!TrySelectBestInteractionSlot(_resident.State.ResidentLocalPosition.Value, canFacility,
                    _definitions[canFacility.DefinitionId], out var canPoint, out _, out _, out float toCan,
                    out _, GetWaterCanAccessInteractionGroup(canFacility)) ||
                !TryCalculateTravelPath(canPoint, StopWaterPoint, out var outward) ||
                !TrySelectBestInteractionSlot(StopWaterPoint, tank, _definitions[tank.DefinitionId],
                    out Vector3 homePoint, out _, out _, out float home, out _, WaterPickupInteractionGroupId) ||
                !TryMeasureFacilityPath(homePoint, tank, _definitions[tank.DefinitionId],
                    out float toParking, WaterCanAccessInteractionGroupId)) return;
            var proposal = new ResidentActionPlanProposal($"stop-water:{_resident.StableId}:{_resident.ActionSequence + 1}",
                "stop-water", "携带水罐到驿站取水并送回车辆")
            {
                Steps = new[] { CreateTravelStep(toCan + outward.PathLength + home + toParking, "取罐、出车、返回水箱并走到停放区"),
                    new ResidentActionStepEstimate(ResidentActionStepKind.Transfer,
                        EstimateWorkDuration(pickupSeconds * 2 + WaterCanLiftSeconds + deliverySeconds * 2 + WaterCanReleaseSeconds),
                        label: "抬罐、装水、注入车辆水箱并落罐松手") },
                BaseUtility = 0.1f, PlayerPriority = 1f, WorkUrgency = 0.65f, DependencyValue = 0.65f,
                WorkIntensity = 0.45f, Effort = 0.1f,
            };
            options.Add(new FoundationResidentDecisionOption(FoundationResidentDecisionKind.StopWater,
                _actionPlanEvaluator.Evaluate(proposal, CreateResidentDecisionCondition(), _actionPlanPolicy))
            { WaterCanFacility = canFacility, TargetFacility = tank, TransferMilliliters = amount });
        }

        private void BeginStopWater(FoundationResidentDecisionOption option)
        {
            var request = new HaulTaskRequest($"stop-water:{_resident.StableId}:{++_resident.ActionSequence}",
                _resident.OwnerId, _stopWater, _waterCan, _vehicleWater, NomadResourceIds.Water,
                option.TransferMilliliters, "从有限驿站水源向车辆补给", new[] { $"carrier:{_waterCan.Id}", "site:dry-river:draw-water" });
            if (!_resourceFlow.TryReserveHaul(request, out _resident.ActiveHaul, out var blocker))
            {
                _resident.State.LastBlocker.Value = $"取水预留失败：{blocker.Reason}";
                return;
            }
            _resident.StopVisit = new FoundationStopVisitExecution(
                FoundationStopVisitKind.WaterCollection,
                deckLayout.LocalToPose(_resident.State.ResidentLocalPosition.Value, _resident.State.ResidentLocalYawDegrees.Value),
                option.TargetFacility.InstanceId);
            _resident.LastDeckCheckpointPose = _resident.StopVisit.CheckpointPose;
            TryBeginMove(FoundationResidentPhase.MovingToWaterCan, "取水前先取得唯一水罐",
                option.WaterCanFacility, allowAlternativeFacility: false,
                interactionGroupId: GetWaterCanAccessInteractionGroup(option.WaterCanFacility));
        }

        // 车内补水和车外补给共同使用同一搬运能力契约，避免新来源绕过防漏与污染评估。
        private CargoTransportOptionEvaluation EvaluateWaterCanCargo(int amount) => CargoTransportPlanner.Evaluate(
            new CargoTransportRequirement(NomadResourceIds.Water, amount, CargoContainerCapability.LiquidTight,
                allowBareHands: false, bareHandsMaxAmount: 0, contaminationSensitivity: 0.8f),
            new CargoCarrierOption(_waterCan.Id, CargoCarrierKind.Container, _waterCan.FreeCapacity,
                waterCanCleanliness, EstimateWorkDuration(pickupSeconds), waterCanCapabilities, isAvailable: string.IsNullOrEmpty(_waterCanCarrierId) || _waterCanCarrierId == _resident.StableId));

        private void BeginStopWaterOutbound()
        {
            ReleaseActiveInteractionSpace(publishProjection: true);
            ClearActiveMoveIntent();
            if (_resident.StopVisit.ReturnRequested || !TryAssignTravelPath(StopWaterPoint))
            {
                BeginStopWaterReturn();
                return;
            }
            _resident.HasDockingYaw = true;
            _resident.DockingYawDegrees = 0f;
            SetResidentPhase(FoundationResidentPhase.MovingToStopWater, "携带空水罐出车前往驿站水源");
        }

        private void BeginStopWaterReturn()
        {
            ClearActivePath();
            if (!TryFindFacilityState(_resident.StopVisit.HomeFacilityId, out var tank))
                throw new InvalidOperationException("取水往返期间车辆水箱被移除。");
            TryBeginMove(FoundationResidentPhase.ReturningFromStopWater, "返回车辆水箱；到车前保持停车",
                tank, allowAlternativeFacility: false, interactionGroupId: WaterPickupInteractionGroupId);
        }

        private void AdvanceStopVisitRecall()
        {
            if (_resident.StopVisit == null) return;
            if (IsStopSpareVisit)
            {
                AdvanceStopSpareRecall();
                return;
            }
            if (IsStopWasteVisit)
            {
                AdvanceStopWasteRecall();
                return;
            }
            if (MustLeaveDriving()) _resident.StopVisit.ReturnRequested = true;
            if (_resident.StopVisit.ReturnRequested && _resident.Phase is
                FoundationResidentPhase.MovingToStopWater or FoundationResidentPhase.FillingAtStop)
                BeginStopWaterReturn();
        }

        private void CompleteStopWaterPickup()
        {
            _resident.ActiveHaul.PickUp();
            BeginStopWaterReturn();
        }

        private void CompleteStopWaterDelivery()
        {
            string tankId = _resident.StopVisit.HomeFacilityId;
            bool carried = _resident.ActiveHaul.State == HaulTaskState.Carrying;
            // 身体需要或临时路线问题只结束这一名居民的尝试，不能伪装为玩家取消。
            // 已取到水则本次请求已履行；玩家撤回/车辆启程仍沿原有召回边界终止。
            bool retryRequested = !carried && _stopWaterRequested && IsAtDryRiver &&
                _journey.Destination != NomadJourneyEndpoint.Origin;
            if (carried) _resident.ActiveHaul.Deliver();
            else _resident.ActiveHaul.Dispose();
            _resident.ActiveHaul = null;
            _resident.StopWaterDeliveredBeforeParking = carried;
            _resident.RetryStopWaterAfterParking = retryRequested;
            _stopWaterRequested = retryRequested;
            BeginWaterCanParking(tankId, FoundationWaterCanLocation.VehicleWaterTank);
        }

        private void CompleteStopWaterParking()
        {
            bool carried = _resident.StopWaterDeliveredBeforeParking;
            bool retryRequested = _resident.RetryStopWaterAfterParking && _stopWaterRequested && IsAtDryRiver &&
                _journey.Destination != NomadJourneyEndpoint.Origin;
            ReleaseActiveTasks();
            _stopWaterRequested = retryRequested;
            SetResidentPhase(FoundationResidentPhase.Idle, carried ? "水已送回车辆，取水者已上车" :
                retryRequested ? "已带空水罐归车，取水请求保留，等待可出车居民" : "已带空水罐返回车辆，取水已取消");
            _model.StopWorkFeedback.Value = _resident.State.CurrentTask.Value;
            _resident.DecisionRetryRemaining = 0f;
        }

        private NomadStopSaveData CaptureStopWater() => new()
        {
            SiteId = DryRiverSiteId,
            WaterCapacityMilliliters = _stopWater.Capacity,
            // 捕获只投影守恒边界，不取消正在运行的实体往返，也不把途中水奖励给车辆。
            WaterMilliliters = checked(_stopWater.TotalAmount + (FindStopVisitor(FoundationStopVisitKind.WaterCollection) != null ? _waterCan.TotalAmount : 0)),
            WaterCollectionRequested = _stopWaterRequested,
            WasteMilliliters = _stopWaste.TotalAmount,
            WasteCapacityMilliliters = _stopWaste.Capacity,
            WasteDisposalRequested = _stopWasteRequested,
            SpareStockInitialized = true,
            SpareCollectionRequested = _stopSpareRequested,
        };

        private static NomadStopSaveData ResolveStopWater(NomadStopSaveData saved)
        {
            if (saved == null || saved.IsEmpty) return new NomadStopSaveData
            { SiteId = DryRiverSiteId, WaterMilliliters = InitialStopWaterMilliliters,
                WaterCapacityMilliliters = InitialStopWaterMilliliters,
                WasteCapacityMilliliters = InitialStopWasteCapacityMilliliters };
            saved.Validate();
            if (saved.SiteId != DryRiverSiteId)
                throw new NotSupportedException($"当前路线不包含驿站 {saved.SiteId}。");
            return saved;
        }

        private void RestoreStopWater(NomadStopSaveData saved)
        {
            saved = ResolveStopWater(saved);
            _stopWater = new ResourceInventory("site:dry-river:water", ResourceMeasure.Milliliter,
                saved.WaterCapacityMilliliters, saved.WaterMilliliters > 0
                    ? new[] { new ResourceQuantity(NomadResourceIds.Water, saved.WaterMilliliters) }
                    : Array.Empty<ResourceQuantity>());
            _stopWaterRequested = saved.WaterCollectionRequested;
            RestoreStopWaste(saved);
            _stopSpareRequested = saved.SpareCollectionRequested;
            _model.StopWorkFeedback.Value = "地点库存已恢复，未完成作业将重新规划";
            SetStopAccess(IsAtDryRiver);
        }

        private void WriteStopWaterProjection()
        {
            if (_stopWater == null) return;
            SetInt(_model.StopWaterMilliliters, _stopWater.TotalAmount);
            _model.StopWaterRequested.Value = _stopWaterRequested;
            _model.StopWaterActive.Value = FindStopVisitor(FoundationStopVisitKind.WaterCollection) != null;
            WriteStopWasteProjection();
            WriteStopSpareProjection();
        }
    }

    internal enum FoundationStopVisitKind { WaterCollection, WasteDisposal, SpareCollection }

    /// <summary>
    /// 一次车外访问的执行 owner。即便资源已交付，仍持有返回意图，直到人和容器回装；
    /// 输入 / 输出预留在终止时释放，已提交倾倒不回滚。检查点回到出发前甲板姿态。
    /// </summary>
    internal sealed class FoundationStopVisitExecution : IDisposable
    {
        internal FoundationStopVisitExecution(FoundationStopVisitKind kind, DeckPose checkpointPose,
            string homeFacilityId, ProcessTaskLease transfer = null)
        { Kind = kind; CheckpointPose = checkpointPose; HomeFacilityId = homeFacilityId; Transfer = transfer; }
        internal FoundationStopVisitKind Kind { get; }
        internal DeckPose CheckpointPose { get; }
        internal string HomeFacilityId { get; }
        internal bool ReturnRequested;
        internal bool ContainerDetached;
        internal ProcessTaskLease Transfer { get; }
        public void Dispose() => Transfer?.Dispose();
    }
}
