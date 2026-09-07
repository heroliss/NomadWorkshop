using System;
using System.Collections.Generic;
using Game.NomadWorkshop.Simulation;
using Game.NomadWorkshop.Simulation.Persistence;
using UnityEngine;

namespace Game.NomadWorkshop.Foundation
{
    public sealed partial class NomadFoundationSystem
    {
        private const int InitialStopWasteCapacityMilliliters = 24_000;
        private ResourceInventory _stopWaste;
        private bool _stopWasteRequested;
        private Vector3 StopWastePoint => StopWaterPoint + Vector3.back;

        /// <summary>请求清运一座有内容且可达的旱厕污物桶，或召回当前清运者。命令不倾倒污物。</summary>
        public bool RequestStopWaste(bool dispose)
        {
            if (_checkpointOperation != null) return false;
            if (!_initialized) return false;
            var visitor = FindStopVisitor(FoundationStopVisitKind.WasteDisposal);
            var anyVisitor = FindStopVisitor();
            if (dispose && (_stopWaterRequested || FindStopVisitor(FoundationStopVisitKind.WaterCollection) != null || _stopSpareRequested || FindStopVisitor(FoundationStopVisitKind.SpareCollection) != null)) return false;
            if (dispose && visitor != null) return _stopWasteRequested;
            if (dispose && (!IsAtDryRiver || _journey.Destination == NomadJourneyEndpoint.Origin ||
                            !HasLivingResident ||
                            _model.BuildTransactionPhase.Value != FoundationBuildTransactionPhase.Idle))
            {
                _model.StopWorkFeedback.Value = "需要在干河驿站停车并结束建造，才能清运污物";
                return false;
            }
            bool hadRequest = _stopWasteRequested || visitor != null;
            _stopWasteRequested = dispose;
            if (dispose || hadRequest)
                _model.StopWorkFeedback.Value = dispose ? "清运请求已接受，等待居民完成手头工作" : "已停止清运，污物桶将回装原旱厕";
            if (!dispose && visitor != null) visitor.StopVisit.ReturnRequested = true;
            WakeResidents();
            WriteStopWasteProjection();
            return true;
        }

        private void ResetStopWaste()
        {
            _stopWaste = new ResourceInventory("site:dry-river:waste-receiver", ResourceMeasure.Milliliter,
                InitialStopWasteCapacityMilliliters);
            _stopWasteRequested = false;
        }

        private void RestoreStopWaste(NomadStopSaveData saved)
        {
            _stopWaste = new ResourceInventory("site:dry-river:waste-receiver", ResourceMeasure.Milliliter,
                saved.WasteCapacityMilliliters, saved.WasteMilliliters > 0
                    ? new[] { new ResourceQuantity(NomadResourceIds.HumanWaste, saved.WasteMilliliters) }
                    : Array.Empty<ResourceQuantity>());
            _stopWasteRequested = saved.WasteDisposalRequested;
        }

        private bool IsToiletReadyForUse(string facilityId) =>
            FindStopVisitor(FoundationStopVisitKind.WasteDisposal)?.StopVisit.HomeFacilityId != facilityId;

        private void AddStopWasteDecisionOption(ICollection<FoundationResidentDecisionOption> options)
        {
            // 清运是满桶 → 无法如厕的解锁前置，不能复用驾驶的低排泄压力门槛形成死锁。
            if (FindStopVisitor() != null) return;
            if (!_stopWasteRequested || !_stopAccessOpen || !IsAtDryRiver ||
                _journey.Destination == NomadJourneyEndpoint.Origin || !_resident.Wellbeing.IsAlive ||
                _resident.Wellbeing.Health <= 0.1f || _resident.Wellbeing.Fatigue >= 0.9f ||
                _model.InteractionMode.Value == FoundationInteractionMode.Build) return;

            FoundationFacilityState selected = default;
            float selectedTravel = 0f;
            float bestFill = -1f;
            int amount = 0;
            bool hasWaste = false;
            bool hasCapacity = false;
            foreach (var facility in _model.Facilities)
            {
                if (!_toiletInventories.TryGetValue(facility.InstanceId, out var bucket) || bucket.TotalAmount == 0)
                    continue;
                hasWaste = true;
                int available = _resourceFlow.GetAvailableAmount(bucket, NomadResourceIds.HumanWaste);
                // 整桶移动，只有接收罐能接下全部内容才派出；不把未搬出的另一半留在虚假库存节点。
                if (available != bucket.TotalAmount || _resourceFlow.GetAvailableCapacity(_stopWaste) < available)
                    continue;
                hasCapacity = true;
                if (!_definitions.TryGetValue(facility.DefinitionId, out var definition) ||
                    !TrySelectBestInteractionSlot(_resident.State.ResidentLocalPosition.Value, facility, definition,
                        out var pickupPoint, out _, out _, out float approach, out _, ToiletInteractionGroupId) ||
                    !TryCalculateTravelPath(pickupPoint, StopWastePoint, out var outward) ||
                    !TryMeasureFacilityPath(StopWastePoint, facility, definition, out float home, ToiletInteractionGroupId))
                    continue;
                float fill = bucket.TotalAmount / (float)bucket.Capacity;
                if (fill < bestFill || fill == bestFill &&
                    string.CompareOrdinal(facility.InstanceId, selected.InstanceId) >= 0) continue;
                selected = facility;
                selectedTravel = approach + outward.PathLength + home;
                bestFill = fill;
                amount = available;
            }
            if (amount == 0)
            {
                if (hasWaste && hasCapacity)
                {
                    // 此人的路径/工作位候选失败，不代表共享清运意图已结束。
                    // 如厕者释放工作位或另一人可达后继续竞争，不能由一次局部评估吞掉玩家请求。
                    _model.StopWorkFeedback.Value = "等待可用的旱厕工作位与清运通路，保留清运请求";
                    return;
                }
                _stopWasteRequested = false;
                _resident.State.LastBlocker.Value = !hasWaste ? "旱厕污物桶均为空，无需清运" :
                    "驿站接收余量不足，无法接下任何一整桶污物";
                _model.StopWorkFeedback.Value = _resident.State.LastBlocker.Value;
                return;
            }
            bool urgent = _resident.WaterCycle.ExcretionPressure >= 0.65f;
            var proposal = new ResidentActionPlanProposal($"stop-waste:{_resident.StableId}:{_resident.ActionSequence + 1}",
                "stop-waste", "取走旱厕污物桶、送至接收点并回装")
            {
                Steps = new[] { CreateTravelStep(selectedTravel, "拆桶、车外清运并回装"),
                    new ResidentActionStepEstimate(ResidentActionStepKind.Transfer,
                        EstimateWorkDuration(pickupSeconds + deliverySeconds * 2), label: "取桶、倾倒与回装") },
                BaseUtility = 0.12f, PlayerPriority = 1f, WorkUrgency = urgent ? 0.9f : 0.7f,
                DependencyValue = 0.9f, WorkIntensity = 0.5f, Effort = 0.2f,
                RiskTier = urgent ? ResidentDecisionRiskTier.Urgent : ResidentDecisionRiskTier.Routine,
                RiskPriority = urgent ? _resident.WaterCycle.ExcretionPressure : 0f,
            };
            options.Add(new FoundationResidentDecisionOption(FoundationResidentDecisionKind.StopWaste,
                _actionPlanEvaluator.Evaluate(proposal, CreateResidentDecisionCondition(), _actionPlanPolicy))
            { SourceFacility = selected, TransferMilliliters = amount });
        }

        private void BeginStopWaste(FoundationResidentDecisionOption option)
        {
            ResourceInventory bucket = _toiletInventories[option.SourceFacility.InstanceId];
            var request = new ProcessTaskRequest($"stop-waste:{_resident.StableId}:{++_resident.ActionSequence}",
                _resident.OwnerId, "将实体污物桶中的内容倒入驿站接收罐",
                new[] { new InventoryResourceQuantity(bucket, NomadResourceIds.HumanWaste, option.TransferMilliliters) },
                new[] { new InventoryResourceQuantity(_stopWaste, NomadResourceIds.HumanWaste, option.TransferMilliliters) },
                new[] { $"facility:{option.SourceFacility.InstanceId}:toilet-use", "site:dry-river:dispose-waste" });
            if (!_resourceFlow.TryReserveProcess(request, out var transfer, out var blocker))
            {
                _resident.State.LastBlocker.Value = $"无法预留污物清运：{blocker.Reason}";
                return;
            }
            _resident.StopVisit = new FoundationStopVisitExecution(FoundationStopVisitKind.WasteDisposal,
                deckLayout.LocalToPose(_resident.State.ResidentLocalPosition.Value, _resident.State.ResidentLocalYawDegrees.Value),
                option.SourceFacility.InstanceId, transfer);
            _resident.LastDeckCheckpointPose = _resident.StopVisit.CheckpointPose;
            TryBeginMove(FoundationResidentPhase.MovingToWasteBucket, "前往旱厕取出密封污物桶",
                option.SourceFacility, allowAlternativeFacility: false, interactionGroupId: ToiletInteractionGroupId);
        }

        private void CompleteWasteBucketPickup()
        {
            _resident.StopVisit.ContainerDetached = true;
            ReleaseActiveInteractionSpace(publishProjection: true);
            ClearActiveMoveIntent();
            if (_resident.StopVisit.ReturnRequested || !TryAssignTravelPath(StopWastePoint))
            {
                BeginWasteBucketReturn();
                return;
            }
            _resident.HasDockingYaw = true;
            _resident.DockingYawDegrees = 180f;
            SetResidentPhase(FoundationResidentPhase.MovingToWasteReceiver, "携带密封污物桶前往驿站接收点");
        }

        private void CompleteWasteDisposal()
        {
            _resident.StopVisit.Transfer.Commit();
            _stopWasteRequested = false;
            BeginWasteBucketReturn();
        }

        private void BeginWasteBucketReturn()
        {
            ClearActivePath();
            if (!TryFindFacilityState(_resident.StopVisit.HomeFacilityId, out var toilet))
                throw new InvalidOperationException("清运期间来源旱厕被移除，无法回装污物桶。");
            TryBeginMove(FoundationResidentPhase.ReturningWasteBucket,
                _resident.StopVisit.Transfer.IsCommitted ? "携带空桶回车装回旱厕" : "召回清运者，保留桶内污物并回装",
                toilet, allowAlternativeFacility: false, interactionGroupId: ToiletInteractionGroupId);
        }

        private void AdvanceStopWasteRecall()
        {
            // 该任务本身解除满桶造成的生理背压；不因膀胱紧急就反复取消同一个必要前置。
            if (!_resident.StopVisit.ReturnRequested) return;
            if (_resident.Phase is FoundationResidentPhase.MovingToWasteBucket or FoundationResidentPhase.DetachingWasteBucket ||
                _resident.Phase == FoundationResidentPhase.WaitingForRoute && !_resident.StopVisit.ContainerDetached)
            {
                ReleaseActiveTasks();
                _stopWasteRequested = false;
                SetResidentPhase(FoundationResidentPhase.Idle, "已停止清运，污物桶仍在原旱厕");
                _model.StopWorkFeedback.Value = _resident.State.CurrentTask.Value;
                _resident.DecisionRetryRemaining = 0f;
            }
            else if (_resident.Phase is FoundationResidentPhase.MovingToWasteReceiver or FoundationResidentPhase.EmptyingWasteBucket)
                BeginWasteBucketReturn();
        }

        private void CompleteWasteBucketInstallation()
        {
            bool emptied = _resident.StopVisit.Transfer.IsCommitted;
            ReleaseActiveTasks();
            _stopWasteRequested = false;
            SetResidentPhase(FoundationResidentPhase.Idle, emptied ? "污物已交付，空桶已装回旱厕" : "清运已取消，原污物桶已回装");
            _model.StopWorkFeedback.Value = _resident.State.CurrentTask.Value;
            _resident.DecisionRetryRemaining = 0f;
        }

        private void WriteStopWasteProjection()
        {
            if (_stopWaste == null) return;
            SetInt(_model.StopWasteMilliliters, _stopWaste.TotalAmount);
            SetInt(_model.StopWasteCapacityMilliliters, _stopWaste.Capacity);
            _model.StopWasteRequested.Value = _stopWasteRequested;
            _model.StopWasteActive.Value = FindStopVisitor(FoundationStopVisitKind.WasteDisposal) != null;
            var visitor = FindStopVisitor(FoundationStopVisitKind.WasteDisposal);
            string away = visitor?.StopVisit.ContainerDetached == true
                ? visitor.StopVisit.HomeFacilityId : string.Empty;
            _model.WasteBucketCarrierId.Value = string.IsNullOrEmpty(away) ? string.Empty : visitor.StableId;
            _model.CarriedWasteMilliliters.Value = !string.IsNullOrEmpty(away)
                ? _toiletInventories[away].TotalAmount : 0;
            _model.CarriedWasteBucketFacilityId.Value = away;
        }
    }
}
