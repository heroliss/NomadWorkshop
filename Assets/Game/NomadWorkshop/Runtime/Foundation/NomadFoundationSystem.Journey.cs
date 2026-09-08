using System;
using System.Collections.Generic;
using Game.NomadWorkshop.Simulation;
using UnityEngine;

namespace Game.NomadWorkshop.Foundation
{
    public sealed partial class NomadFoundationSystem
    {
        private const string DriverInteractionGroupId = "drive";
        private const long InitialJourneyFuelPicoliters = 20_000_000_000_000L;
        private static readonly NomadJourneyMotionPolicy FoundationMotionPolicy =
            new(accelerationMillimetersPerSecondSquared: 1_600,
                brakingMillimetersPerSecondSquared: 2_400);
        private static readonly NomadJourneyRoute FoundationRoute = new(
            "old-camp:dry-river", "old-camp", "dry-river", 2_000_000L, 8_000, 2_500,
            new[]
            {
                new NomadJourneyRouteAnchor("old-camp-exit", 100),
                new NomadJourneyRouteAnchor("midway-shelter", 500),
                new NomadJourneyRouteAnchor("dry-river-approach", 900),
            });
        private NomadJourneySession _journey;

        /// <summary>
        /// 玩家选择有限路线端点或取消目标。改变目标立即撤销驾驶行动，保留途中位置和燃料；
        /// 其他生活行动继续到其业务边界，再由同一 Utility 决策选择是否到岗。
        /// </summary>
        public void SetJourneyDestination(NomadJourneyEndpoint destination)
        {
            if (_checkpointOperation != null) return;
            if (!_initialized) throw new InvalidOperationException("Foundation 尚未初始化。");
            bool changed = _journey.Destination != destination;
            _journey.SetDestination(destination);
            if (destination == NomadJourneyEndpoint.Origin && IsAtDryRiver)
            {
                RequestStopWater(false);
                RequestStopWaste(false);
                RequestStopSpare(false);
            }
            foreach (var resident in _residents)
            {
                using var scope = UseResident(resident);
                if (changed && IsDrivingAction()) StopDrivingAction("目标已改变，车辆已停车");
            }
            WakeResidents();
            WriteJourneyProjection();
        }

        private void ResetJourney()
        {
            _journey?.Dispose();
            _journey = new NomadJourneySession(
                FoundationRoute,
                InitialJourneyFuelPicoliters,
                FoundationMotionPolicy);
        }

        // 到岗与离岗采用不同阈值，避免需求刚低于危险线就反复走回驾驶台。
        private bool CanStartDriving() => _resident.Wellbeing.IsAlive &&
            _resident.WaterCycle.Thirst < 0.55f &&
            _resident.WaterCycle.ExcretionPressure < 0.65f &&
            _resident.Wellbeing.Fatigue < 0.6f && _resident.Wellbeing.Health > 0.3f;

        private bool MustLeaveDriving() => !_resident.Wellbeing.IsAlive ||
            _resident.WaterCycle.Thirst >= 0.75f ||
            _resident.WaterCycle.ExcretionPressure >= 0.9f ||
            _resident.Wellbeing.Fatigue >= 0.8f || _resident.Wellbeing.Health <= 0.2f;

        private bool IsDrivingAction() =>
            _resident.Phase is FoundationResidentPhase.MovingToDriver or FoundationResidentPhase.Driving ||
            _resident.HasMoveIntent &&
            _resident.MoveIntent.TravelPhase == FoundationResidentPhase.MovingToDriver;

        private void AddDrivingDecisionOption(ICollection<FoundationResidentDecisionOption> options)
        {
            if (_journey.Status != NomadJourneyStatus.AwaitingDriver || !CanStartDriving() ||
                !AreAllResidentsAboard) return;
            foreach (FoundationFacilityState facility in _model.Facilities)
            {
                if (!_definitions.TryGetValue(facility.DefinitionId, out var definition) ||
                    definition.Function != NomadFacilityFunction.DriverStation ||
                    !TrySelectBestInteractionSlot(
                        _resident.State.ResidentLocalPosition.Value, facility, definition,
                        out _, out _, out _, out float travelMeters, out _, DriverInteractionGroupId))
                    continue;

                var proposal = new ResidentActionPlanProposal(
                    $"drive:{_resident.StableId}:{_resident.ActionSequence + 1}", "drive", "前往驾驶台并驾驶至目标地点")
                {
                    Steps = new[]
                    {
                        CreateTravelStep(travelMeters, "前往驾驶台"),
                        new ResidentActionStepEstimate(ResidentActionStepKind.UseFacility,
                            (_journey.Destination == NomadJourneyEndpoint.Origin
                                ? _journey.PositionMicrometers
                                : FoundationRoute.LengthMicrometers - _journey.PositionMicrometers) /
                            (FoundationRoute.SpeedMillimetersPerSecond * 1000f),
                            label: "驾驶剩余路程（必要生活需求会中断）"),
                    },
                    BaseUtility = 0.1f,
                    PlayerPriority = 1f,
                    WorkUrgency = 0.65f,
                    DependencyValue = 0.65f,
                    WorkIntensity = 0.35f,
                    Effort = 0.1f,
                    ReservationKeys = new[] { $"facility:{facility.InstanceId}:{DriverInteractionGroupId}" },
                };
                options.Add(new FoundationResidentDecisionOption(FoundationResidentDecisionKind.Drive,
                    _actionPlanEvaluator.Evaluate(proposal, CreateResidentDecisionCondition(), _actionPlanPolicy))
                {
                    TargetFacility = facility,
                    JourneyDestinationRevision = _journey.DestinationRevision,
                });
            }
        }

        private void BeginDrivingApproach(FoundationResidentDecisionOption option)
        {
            _resident.ActionSequence++;
            _resident.DriverDestinationRevision = option.JourneyDestinationRevision;
            TryBeginMove(FoundationResidentPhase.MovingToDriver, "前往驾驶台",
                option.TargetFacility, allowAlternativeFacility: false,
                interactionGroupId: DriverInteractionGroupId);
        }

        private void CompleteDrivingApproach()
        {
            ClearActiveMoveIntent();
            if (MustLeaveDriving() || !AreAllResidentsAboard ||
                _resident.InteractionSpace is not { IsActive: true } ||
                !_journey.TryAcquireDriver(_resident.StableId, _resident.DriverDestinationRevision,
                    out _resident.DriverLease))
            {
                StopDrivingAction("到岗条件已改变，车辆保持停车");
                return;
            }
            SetStopAccess(false);
            SetResidentPhase(FoundationResidentPhase.Driving, "在驾驶台驾驶车辆");
        }

        private void AdvanceDriving(long deltaMilliseconds)
        {
            if (_resident.DriverLease is not { IsActive: true } ||
                !IsResidentAtDriverSlot() || !AreAllResidentsAboard || MustLeaveDriving())
            {
                StopDrivingAction("已停车离岗，先处理居民需求");
                return;
            }
            _journey.Advance(deltaMilliseconds);
            if (_journey.Status != NomadJourneyStatus.Moving)
            {
                StopDrivingAction(_journey.Status == NomadJourneyStatus.Arrived
                    ? "车辆已到达目标，驾驶员离岗" : "燃料不足，车辆已安全停车");
                if (IsAtDryRiver) SetStopAccess(true);
            }
        }

        private bool IsResidentAtDriverSlot()
        {
            if (_resident.InteractionSpace is not { IsActive: true } space ||
                !TryFindFacilityState(space.Slot.FacilityInstanceId, out var facility) ||
                !_definitions.TryGetValue(facility.DefinitionId, out var definition) ||
                definition.Function != NomadFacilityFunction.DriverStation)
                return false;
            foreach (var group in definition.InteractionGroups)
            {
                if (group.GroupId != DriverInteractionGroupId) continue;
                foreach (var slot in group.AlternativeSlots)
                {
                    if (slot.SlotId != space.Slot.SlotId) continue;
                    DeckPose pose = slot.Resolve(facility.Pose);
                    return HorizontalDistance(_resident.State.ResidentLocalPosition.Value,
                               deckLayout.PoseToLocal(pose)) <= ResidentExactNavMeshTolerance &&
                           Mathf.Abs(Mathf.DeltaAngle(_resident.State.ResidentLocalYawDegrees.Value,
                               (float)pose.YawDegrees)) <= 1f;
                }
            }
            return false;
        }

        private void StopDrivingAction(string task)
        {
            ReleaseActiveTasks();
            SetStopAccess(IsAtDryRiver);
            PublishCurrentFacilityAccessProjection();
            SetResidentPhase(FoundationResidentPhase.Idle, task);
            _resident.DecisionRetryRemaining = 0f;
        }

        private void WriteJourneyProjection()
        {
            if (_journey == null) return;
            _model.DepartureFeedback.Value = _journey.Destination != NomadJourneyEndpoint.None && !AreAllResidentsAboard
                ? "等待所有车外人员与容器归车，车辆保持停车" : string.Empty;
            SetLong(_model.JourneyPositionMicrometers, _journey.PositionMicrometers);
            SetLong(_model.JourneyFuelPicoliters, _journey.FuelPicoliters);
            _model.JourneyDestination.Value = _journey.Destination;
            _model.JourneyStatus.Value = _journey.Status;
        }
    }
}
