using System;
using System.Collections.Generic;
using System.Globalization;
using Game.NomadWorkshop.Navigation;
using Game.NomadWorkshop.Simulation;
using Game.NomadWorkshop.Simulation.Persistence;
using UnityEngine;

namespace Game.NomadWorkshop.Foundation
{
    public sealed partial class NomadFoundationSystem
    {
        private const string ResidentStableId = "resident-01";
        private const string ResidentPersonalInventoryId = "resident-01:personal";
        private const string ResidentBodyWaterInventoryId = "resident-01:body-water";
        private const string ResidentBladderInventoryId = "resident-01:bladder";
        private const string VehicleWaterInventoryId = "vehicle-water-tank";
        private const string WaterCanInventoryId = "water-can-01";
        private const string ToiletHoldingInventoryId = "toilet-holding";
        private const string ResidentDecisionRandomStreamId = "resident-decision";
        private const string LeisureOutcomeRandomStreamId =
            "resident-wellbeing:leisure-outcome";
        private const string WorkPaceRandomStreamId =
            "resident-performance:work-pace";
        private const string ResidentActionSequenceStreamId =
            "foundation:resident-action-id";

        /// <summary>
        /// 捕获当前已提交的 Foundation 业务真值。路径、交互位租约、NavMeshData、材质和动画阶段不会落盘；
        /// 未提交行动在快照中回退到最近的守恒边界，加载后由 Utility AI 重新规划。
        /// </summary>
        public NomadWorkshopSaveData CaptureCheckpoint()
        {
            EnsureCheckpointRuntimeReady();
            // 检查点必须把每座设施结算到根 Tick；否则读档会从一个较早状态继续，
            // 在不同保存帧边界下改变故障触发时刻。
            AdvanceFacilityConditionsTo(_simulationClock.SimulationTick);

            int liveVehicleWater = _vehicleWater.GetAmount(NomadResourceIds.Water);
            int liveWaterCanWater = _waterCan.GetAmount(NomadResourceIds.Water);
            bool rewindCarriedWater = _waterCanLocation == FoundationWaterCanLocation.Resident;
            int checkpointVehicleWater = rewindCarriedWater
                ? checked(liveVehicleWater + liveWaterCanWater)
                : liveVehicleWater;
            if (checkpointVehicleWater > _vehicleWater.Capacity)
                throw new InvalidOperationException(
                    "居民携带水回滚到车辆水箱后超过容量，无法形成守恒检查点。");

            string waterCanOwner = rewindCarriedWater
                ? FindRequiredFacilityInstanceId(NomadFacilityFunction.VehicleWaterTank)
                : ResolveWaterCanSaveOwner();
            int checkpointWaterCanWater = rewindCarriedWater ? 0 : liveWaterCanWater;

            var data = new NomadWorkshopSaveData
            {
                WorldSeed = worldSeed,
                SimulationTick = _simulationClock.SimulationTick,
                Vehicle = new NomadVehicleSaveData(),
            };

            IReadOnlyList<FoundationFacilityState> facilities = _model.Facilities;
            for (var i = 0; i < facilities.Count; i++)
            {
                FoundationFacilityState facility = facilities[i];
                if (!_facilityConditions.TryGetValue(
                        facility.InstanceId,
                        out FacilityConditionCycle condition))
                    throw new InvalidOperationException(
                        $"设施 {facility.InstanceId} 缺少状态机，无法形成完整检查点。");
                FacilityConditionCheckpoint conditionCheckpoint =
                    condition.CaptureCheckpoint();
                data.Facilities.Add(new NomadFacilitySaveData
                {
                    InstanceId = facility.InstanceId,
                    DefinitionId = facility.DefinitionId,
                    Pose = QuantizedDeckPose.FromDeckPose(facility.Pose),
                    DurabilityPermille = 1000 - condition.WearPermille,
                    DirtPermille = condition.DustPermille,
                    WearConditionUnits = conditionCheckpoint.WearUnits,
                    MaintenanceDebtConditionUnits =
                        conditionCheckpoint.MaintenanceDebtUnits,
                    DustConditionUnits = conditionCheckpoint.DustUnits,
                    FailureThresholdMicroHazard =
                        conditionCheckpoint.FailureThresholdMicroHazard,
                    AccumulatedFailureMicroHazard =
                        conditionCheckpoint.AccumulatedFailureMicroHazard,
                    FailureHazardSubMicroRemainder =
                        conditionCheckpoint.HazardSubMicroRemainder,
                    FailureCycleSequence = conditionCheckpoint.FailureCycleSequence,
                    ActiveFault = conditionCheckpoint.ActiveFault,
                    FaultSeverityPermille = conditionCheckpoint.FaultSeverityPermille,
                    FaultTriggeredSimulationTick =
                        conditionCheckpoint.FaultTriggeredSimulationTick,
                    ConditionLastSettledSimulationTick =
                        conditionCheckpoint.LastSettledSimulationTick,
                });
            }

            data.Inventories.Add(CreateInventorySaveData(
                _vehicleWater,
                "vehicle-01",
                checkpointVehicleWater));
            data.Inventories.Add(CreateInventorySaveData(
                _waterCan,
                waterCanOwner,
                checkpointWaterCanWater));
            data.Inventories.Add(CreateInventorySaveData(
                _toiletHolding,
                "vehicle-01"));
            data.Inventories.Add(new NomadInventorySaveData
            {
                InventoryId = ResidentPersonalInventoryId,
                OwnerEntityId = ResidentStableId,
                Measure = ResourceMeasure.Item,
                CapacityBaseUnits = 2,
            });
            data.Inventories.Add(CreateInventorySaveData(
                _residentWaterCycle.BodyWater,
                ResidentStableId));
            data.Inventories.Add(CreateInventorySaveData(
                _residentWaterCycle.Bladder,
                ResidentStableId));

            for (var i = 0; i < facilities.Count; i++)
            {
                FoundationFacilityState facility = facilities[i];
                if (_drinkingStationInventories.TryGetValue(
                        facility.InstanceId,
                        out ResourceInventory inventory))
                    data.Inventories.Add(CreateInventorySaveData(inventory, facility.InstanceId));
            }

            ResidentWaterCycleCheckpoint waterCycle = _residentWaterCycle.CaptureCheckpoint();
            data.Residents.Add(new NomadResidentSaveData
            {
                ResidentId = ResidentStableId,
                Pose = QuantizedDeckPose.FromDeckPose(deckLayout.LocalToPose(
                    _model.ResidentLocalPosition.Value,
                    _model.ResidentLocalYawDegrees.Value)),
                PersonalInventoryId = ResidentPersonalInventoryId,
                ThirstPermille = ToPermille(waterCycle.Thirst),
                HealthPermille = ToPermille(_residentWellbeing.Health),
                FatiguePermille = ToPermille(_residentWellbeing.Fatigue),
                StressPermille = ToPermille(_residentWellbeing.Stress),
                EntertainmentPermille = ToPermille(_residentWellbeing.Entertainment),
                MoodPermille = ToPermille(_residentWellbeing.Mood),
                WaterMetabolismPendingNanoliters =
                    waterCycle.PendingMetabolismNanoliters,
                WaterMetabolismSequence = waterCycle.MetabolismSequence,
                // 当前执行器的路径、租约和定时表现均可重建；不保存半个行动，避免重复提交结果。
                ActiveAction = null,
            });

            data.RandomStreams.Add(CreateRandomStream(
                ResidentDecisionRandomStreamId,
                _residentDecisionSequence));
            data.RandomStreams.Add(CreateRandomStream(
                BladderOpportunityRandomStreamId,
                _bladderOpportunitySequence));
            data.RandomStreams.Add(CreateRandomStream(
                LeisureOutcomeRandomStreamId,
                _leisureSequence));
            data.RandomStreams.Add(CreateRandomStream(
                WorkPaceRandomStreamId,
                _workActionSequence));
            data.RandomStreams.Add(CreateRandomStream(
                ResidentActionSequenceStreamId,
                (long)_residentActionSequence + 1L));

            NomadWorkshopSaveContract.ValidateForSave(data);
            return data;
        }

        /// <summary>
        /// 从已校验检查点重建设施、NavMesh、交互空间、真实库存和连续居民状态。
        /// 进行中的建造导航事务不能同步打断；调用方应等待事务回到 Idle 后再加载。
        /// </summary>
        public void RestoreCheckpoint(NomadWorkshopSaveData checkpoint)
        {
            EnsureCheckpointRuntimeReady();
            if (_model.BuildTransactionPhase.Value != FoundationBuildTransactionPhase.Idle)
                throw new InvalidOperationException(
                    "建造导航事务尚未结束；请等待候选提交或回滚完成后再加载检查点。");

            NomadWorkshopSaveData data = NomadWorkshopSaveContract.PrepareAfterLoad(checkpoint) ??
                throw new ArgumentNullException(nameof(checkpoint));
            FoundationRestoreData restore = ValidateAndResolveFoundationCheckpoint(data);
            NomadWorkshopSaveData rollbackCheckpoint = CaptureCheckpoint();
            FoundationRestoreData rollback =
                ValidateAndResolveFoundationCheckpoint(rollbackCheckpoint);
            bool wasPaused = _model.IsPaused.Value;
            float simulationSpeed = _model.SimulationSpeed.Value;
            try
            {
                RestoreValidatedCheckpoint(data, restore, wasPaused, simulationSpeed);
            }
            catch (Exception restoreException)
            {
                try
                {
                    RestoreValidatedCheckpoint(
                        rollbackCheckpoint,
                        rollback,
                        wasPaused,
                        simulationSpeed);
                }
                catch (Exception rollbackException)
                {
                    throw new AggregateException(
                        "Foundation 检查点恢复失败，且加载前业务检查点也无法重建。",
                        restoreException,
                        rollbackException);
                }

                throw new InvalidOperationException(
                    "Foundation 检查点恢复失败；已回到加载前捕获的守恒业务边界。",
                    restoreException);
            }
        }

        private void RestoreValidatedCheckpoint(
            NomadWorkshopSaveData data,
            FoundationRestoreData restore,
            bool wasPaused,
            float simulationSpeed)
        {
            ResetScenarioNow();
            _initialized = false;
            _model.IsReady.Value = false;

            ReleaseActiveTasks();
            ClearActivePath();
            ClearActiveMoveIntent();
            ClearPendingPlacement();
            foreach (DeckNavigationObstacleHandle obstacle in _navigationObstacles.Values)
                _navigation.DeactivateAndDestroyObstacle(obstacle);
            _navigationObstacles.Clear();
            _facilityConditions.Clear();
            _committedFacilityAccess.Clear();
            _drinkingStationInventories.Clear();
            _facilityInventoryProjection.Clear();
            _placementLedger = deckLayout.CreatePlacementLedger();

            var restoredFacilities = new List<FoundationFacilityState>(data.Facilities.Count);
            for (var i = 0; i < data.Facilities.Count; i++)
            {
                NomadFacilitySaveData saved = data.Facilities[i];
                NomadFacilityDefinition definition = _definitions[saved.DefinitionId];
                DeckPose pose = saved.Pose.ToDeckPose();
                var request = new ContinuousFacilityPlacementRequest(
                    saved.InstanceId,
                    saved.DefinitionId,
                    pose,
                    definition.CreateFootprint());
                if (!_placementLedger.TryPlace(
                        request,
                        out _,
                        out ContinuousPlacementFailure failure))
                    throw new InvalidOperationException(
                        $"检查点设施 {saved.InstanceId} 无法恢复到甲板：{failure}。");

                _navigationObstacles.Add(
                    saved.InstanceId,
                    _navigation.CreateFacilityObstacle(
                        saved.InstanceId,
                        pose,
                        request.Footprint));
                FacilityConditionCycle condition = RestoreFacilityCondition(
                    data.WorldSeed,
                    data.SimulationTick,
                    saved,
                    definition.Function);
                _facilityConditions.Add(saved.InstanceId, condition);
                restoredFacilities.Add(new FoundationFacilityState(
                    saved.InstanceId,
                    saved.DefinitionId,
                    pose));
            }
            _model.ReplaceFacilities(restoredFacilities);

            _navigation.BuildNow();
            Vector3 requestedResidentPosition = ToNavigationPoint(
                deckLayout.PoseToLocal(restore.Resident.Pose.ToDeckPose()));
            if (!_navigation.TrySampleLocalPosition(
                    requestedResidentPosition,
                    MaximumTravelSampleOffset,
                    out Vector3 sampledResidentPosition) ||
                HorizontalDistance(requestedResidentPosition, sampledResidentPosition) >
                MaximumTravelSampleOffset ||
                !IsResidentPoseClear(deckLayout.LocalToPose(sampledResidentPosition)))
                throw new InvalidOperationException(
                    $"检查点居民位置 {restore.Resident.Pose.XMillimeters}, " +
                    $"{restore.Resident.Pose.ZMillimeters} mm 已不在可站立甲板上。");
            _model.ResidentLocalPosition.Value = ToNavigationPoint(sampledResidentPosition);
            _model.ResidentLocalYawDegrees.Value = restore.Resident.Pose.YawDegrees;

            RebuildCommittedInteractionSpaces(reacquireActiveSpace: false);
            RefreshCommittedFacilityAccess();

            _resourceFlow = new ResourceFlowLedger();
            int vehicleWaterAmount = restore.VehicleWaterAmount;
            int waterCanAmount = restore.WaterCanAmount;
            FoundationWaterCanLocation waterCanLocation = restore.WaterCanLocation;
            string waterCanAnchor = restore.WaterCanAnchor;
            if (waterCanLocation == FoundationWaterCanLocation.Resident)
            {
                vehicleWaterAmount = checked(vehicleWaterAmount + waterCanAmount);
                if (vehicleWaterAmount > restore.VehicleWater.CapacityBaseUnits)
                    throw new InvalidOperationException(
                        "检查点中居民携带水无法回滚到车辆水箱，恢复会破坏容量守恒。");
                waterCanAmount = 0;
                waterCanLocation = FoundationWaterCanLocation.VehicleWaterTank;
                waterCanAnchor = FindRequiredFacilityInstanceId(
                    restoredFacilities,
                    NomadFacilityFunction.VehicleWaterTank);
            }

            _vehicleWater = CreateSingleResourceInventory(
                restore.VehicleWater,
                NomadResourceIds.Water,
                vehicleWaterAmount);
            _waterCan = CreateSingleResourceInventory(
                restore.WaterCan,
                NomadResourceIds.Water,
                waterCanAmount);
            _toiletHolding = CreateSingleResourceInventory(
                restore.ToiletHolding,
                NomadResourceIds.HumanWaste,
                restore.ToiletHoldingAmount);

            for (var i = 0; i < restoredFacilities.Count; i++)
            {
                FoundationFacilityState facility = restoredFacilities[i];
                if (!_definitions.TryGetValue(
                        facility.DefinitionId,
                        out NomadFacilityDefinition definition) ||
                    definition.Function != NomadFacilityFunction.DrinkingStation)
                    continue;
                NomadInventorySaveData station = restore.StationInventories[facility.InstanceId];
                _drinkingStationInventories.Add(
                    facility.InstanceId,
                    CreateSingleResourceInventory(
                        station,
                        NomadResourceIds.Water,
                        GetSingleResourceAmount(station, NomadResourceIds.Water)));
            }

            _residentWaterCycle = new ResidentWaterCycle(
                ResidentStableId,
                ResidentOwnerId,
                ResidentWaterCycle.DefaultDrinkServingMilliliters /
                Mathf.Max(0.01f, drinkMetabolismSeconds),
                drinkServingMilliliters: ResidentWaterCycle.DefaultDrinkServingMilliliters,
                initialThirst: restore.Resident.ThirstPermille / 1000f,
                thirstIncreasePerSecond: thirstIncreasePerSecond,
                thirstReliefPerServing: 0.72f,
                bodyWaterCapacityMilliliters: restore.BodyWater.CapacityBaseUnits,
                bladderCapacityMilliliters: restore.Bladder.CapacityBaseUnits,
                checkpoint: new ResidentWaterCycleCheckpoint(
                    restore.Resident.ThirstPermille / 1000f,
                    restore.BodyWaterAmount,
                    restore.BladderAmount,
                    restore.Resident.WaterMetabolismPendingNanoliters,
                    restore.Resident.WaterMetabolismSequence));
            _residentWellbeing = new ResidentWellbeing(
                restore.Resident.EntertainmentPermille / 1000f,
                restore.Resident.MoodPermille / 1000f,
                restore.Resident.FatiguePermille / 1000f,
                restore.Resident.StressPermille / 1000f,
                restore.Resident.HealthPermille / 1000f);

            worldSeed = data.WorldSeed;
            _simulationClock.Restore(data.SimulationTick);
            _residentDecisionSequence = GetRandomStreamCursor(
                data,
                ResidentDecisionRandomStreamId,
                fallback: 1L);
            _bladderOpportunitySequence = GetRandomStreamCursor(
                data,
                BladderOpportunityRandomStreamId,
                fallback: 0L);
            _leisureSequence = ToIntCursor(GetRandomStreamCursor(
                data,
                LeisureOutcomeRandomStreamId,
                fallback: 0L), LeisureOutcomeRandomStreamId);
            _workActionSequence = GetRandomStreamCursor(
                data,
                WorkPaceRandomStreamId,
                fallback: 0L);
            long nextActionSequence = GetRandomStreamCursor(
                data,
                ResidentActionSequenceStreamId,
                fallback: 1L);
            _residentActionSequence = Math.Max(
                0,
                ToIntCursor(nextActionSequence, ResidentActionSequenceStreamId) - 1);
            _nextFacilitySequence = FindNextFacilitySequence(restoredFacilities);
            _lastPublishedDecisionDiagnostic = string.Empty;
            _routeRetryRemaining = 0f;
            _residentDecisionRetryRemaining = 0f;
            _phaseDuration = 0f;
            _phaseRemaining = 0f;
            _activeWorkEfficiency = CalculateExpectedWorkEfficiency();
            _activeLeisureOutcomeScale = 1f;
            _activeLeisureKind = FoundationLeisureKind.None;
            _activeWaterSourceFacilityInstanceId = string.Empty;
            _activeWaterTargetFacilityInstanceId = string.Empty;
            _waterCanPickupWasAtSource = false;
            _drinkAfterActiveHaul = false;

            _model.SimulationSpeed.Value = Mathf.Clamp(simulationSpeed, 0.25f, 16f);
            _model.IsPaused.Value = wasPaused;
            _model.WaterCanCapacityMilliliters.Value = _waterCan.Capacity;
            _model.VehicleWaterCapacityMilliliters.Value = _vehicleWater.Capacity;
            _model.BodyWaterCapacityMilliliters.Value = _residentWaterCycle.BodyWater.Capacity;
            _model.BladderCapacityMilliliters.Value = _residentWaterCycle.Bladder.Capacity;
            _model.ToiletHoldingCapacityMilliliters.Value = _toiletHolding.Capacity;
            _model.LatestActionPlan.Value = FoundationActionPlanProjection.None;
            _model.ActionProgress.Value = 0f;
            _model.LastBlocker.Value = string.Empty;
            _model.CompletedDrinkCount.Value = 0;
            _model.CompletedToiletUseCount.Value = 0;
            _model.CompletedLeisureCount.Value = 0;
            _model.CompletedDaydreamCount.Value = 0;
            _model.CompletedWanderCount.Value = 0;
            _model.CompletedGroundRestCount.Value = 0;
            _model.CompletedHobbyCount.Value = 0;
            SetWaterCanLocation(waterCanLocation, waterCanAnchor);
            ClearPlacementSelection(exitBuildMode: true);
            SetResidentPhase(
                _residentWellbeing.IsAlive
                    ? FoundationResidentPhase.Idle
                    : FoundationResidentPhase.Dead,
                _residentWellbeing.IsAlive
                    ? "已恢复运行检查点；瞬时路径与租约已重建，正在重新评估行动"
                    : "已恢复运行检查点；居民健康为零，保持死亡状态");
            WriteSimulationProjection();

            _initialized = true;
            _model.IsReady.Value = true;
        }

        private FoundationRestoreData ValidateAndResolveFoundationCheckpoint(
            NomadWorkshopSaveData data)
        {
            if (data.Blueprints.Count > 0)
                throw new NotSupportedException(
                    "当前 Foundation 还没有蓝图执行器，不能静默丢弃检查点中的未完成蓝图。");
            if (data.Residents.Count != 1 ||
                !string.Equals(
                    data.Residents[0].ResidentId,
                    ResidentStableId,
                    StringComparison.Ordinal))
                throw new NotSupportedException(
                    $"当前 Foundation 只支持单居民 {ResidentStableId} 的运行检查点。");
            if (data.Vehicle.MapXCentimeters != 0 ||
                data.Vehicle.MapZCentimeters != 0 ||
                !string.IsNullOrEmpty(data.Vehicle.CurrentRegionId) ||
                !string.IsNullOrEmpty(data.Vehicle.DestinationId) ||
                data.Vehicle.TravelProgressPermille != 0 ||
                data.Vehicle.FuelMilliUnits != 0 ||
                data.Vehicle.IsTraveling)
                throw new NotSupportedException(
                    "当前甲板 Foundation 尚未接入宏观旅途执行器，不能静默丢弃车辆地图状态。");

            NomadResidentSaveData resident = data.Residents[0];
            if (resident.ActiveAction != null)
                throw new NotSupportedException(
                    "当前 Foundation 只恢复到最近的安全业务边界，尚不能从半个居民行动继续；" +
                    "拒绝静默丢弃 ActiveAction。");
            if (resident.HungerPermille != 0 ||
                resident.BodyHygieneDeficitPermille != 0 ||
                resident.HandContaminationPermille != 0 ||
                resident.MotionSicknessPermille != 0)
                throw new NotSupportedException(
                    "当前 Foundation 尚未接入饥饿、卫生或晕车运行状态，不能静默丢弃这些值。");

            var validationLedger = deckLayout.CreatePlacementLedger();
            var facilityFunctions = new Dictionary<string, NomadFacilityFunction>(
                StringComparer.Ordinal);
            for (var i = 0; i < data.Facilities.Count; i++)
            {
                NomadFacilitySaveData facility = data.Facilities[i];
                if (!_definitions.TryGetValue(
                        facility.DefinitionId,
                        out NomadFacilityDefinition definition))
                    throw new NotSupportedException(
                        $"检查点设施 {facility.InstanceId} 使用当前版本不存在的定义 " +
                        $"{facility.DefinitionId}。");
                var request = new ContinuousFacilityPlacementRequest(
                    facility.InstanceId,
                    facility.DefinitionId,
                    facility.Pose.ToDeckPose(),
                    definition.CreateFootprint());
                if (!validationLedger.TryPlace(
                        request,
                        out _,
                        out ContinuousPlacementFailure failure))
                    throw new InvalidOperationException(
                        $"检查点设施 {facility.InstanceId} 的连续摆放真值无效：{failure}。");
                facilityFunctions.Add(facility.InstanceId, definition.Function);
            }
            if (!ContainsFunction(facilityFunctions, NomadFacilityFunction.VehicleWaterTank))
                throw new InvalidOperationException("Foundation 检查点缺少已放置的车辆水箱。");

            var inventories = new Dictionary<string, NomadInventorySaveData>(
                StringComparer.Ordinal);
            for (var i = 0; i < data.Inventories.Count; i++)
            {
                NomadInventorySaveData inventory = data.Inventories[i];
                ValidateFoundationInventoryMetadata(inventory);
                inventories.Add(inventory.InventoryId, inventory);
            }

            if (!string.Equals(
                    resident.PersonalInventoryId,
                    ResidentPersonalInventoryId,
                    StringComparison.Ordinal))
                throw new NotSupportedException(
                    $"当前 Foundation 需要随身库存 {ResidentPersonalInventoryId}。");
            NomadInventorySaveData personal = RequireInventory(
                inventories,
                ResidentPersonalInventoryId,
                ResourceMeasure.Item);
            if (personal.Contents.Count > 0)
                throw new NotSupportedException(
                    "当前 Foundation 尚未接入随身物品执行器，不能静默丢弃随身库存内容。");
            NomadInventorySaveData vehicleWater = RequireInventory(
                inventories,
                VehicleWaterInventoryId,
                ResourceMeasure.Milliliter);
            NomadInventorySaveData waterCan = RequireInventory(
                inventories,
                WaterCanInventoryId,
                ResourceMeasure.Milliliter);
            NomadInventorySaveData toiletHolding = RequireInventory(
                inventories,
                ToiletHoldingInventoryId,
                ResourceMeasure.Milliliter);
            NomadInventorySaveData bodyWater = RequireInventory(
                inventories,
                ResidentBodyWaterInventoryId,
                ResourceMeasure.Milliliter);
            NomadInventorySaveData bladder = RequireInventory(
                inventories,
                ResidentBladderInventoryId,
                ResourceMeasure.Milliliter);
            if (vehicleWater.CapacityBaseUnits <= 0 ||
                waterCan.CapacityBaseUnits <= 0 ||
                toiletHolding.CapacityBaseUnits <= 0 ||
                bodyWater.CapacityBaseUnits <= 0 ||
                bladder.CapacityBaseUnits <= 0)
                throw new InvalidOperationException(
                    "Foundation 的车辆水箱、水罐、厕所、体内水和膀胱容量都必须大于零。");

            int vehicleWaterAmount = GetSingleResourceAmount(
                vehicleWater,
                NomadResourceIds.Water);
            int waterCanAmount = GetSingleResourceAmount(waterCan, NomadResourceIds.Water);
            int toiletHoldingAmount = GetSingleResourceAmount(
                toiletHolding,
                NomadResourceIds.HumanWaste);
            int bodyWaterAmount = GetSingleResourceAmount(bodyWater, NomadResourceIds.Water);
            int bladderAmount = GetSingleResourceAmount(bladder, NomadResourceIds.HumanWaste);
            long maximumPendingNanoliters = checked((long)bodyWaterAmount * 1_000_000L);
            if (resident.WaterMetabolismPendingNanoliters > maximumPendingNanoliters)
                throw new InvalidOperationException(
                    "居民待提交水代谢量超过体内仍存在的水量。");

            var stations = new Dictionary<string, NomadInventorySaveData>(StringComparer.Ordinal);
            var consumedInventoryIds = new HashSet<string>(StringComparer.Ordinal)
            {
                ResidentPersonalInventoryId,
                VehicleWaterInventoryId,
                WaterCanInventoryId,
                ToiletHoldingInventoryId,
                ResidentBodyWaterInventoryId,
                ResidentBladderInventoryId,
            };
            foreach (KeyValuePair<string, NomadFacilityFunction> facility in facilityFunctions)
            {
                if (facility.Value != NomadFacilityFunction.DrinkingStation) continue;
                string inventoryId = BuildDrinkingStationInventoryId(facility.Key);
                NomadInventorySaveData station = RequireInventory(
                    inventories,
                    inventoryId,
                        ResourceMeasure.Milliliter);
                if (station.CapacityBaseUnits <= 0)
                    throw new InvalidOperationException(
                        $"饮水站库存 {inventoryId} 容量必须大于零。");
                GetSingleResourceAmount(station, NomadResourceIds.Water);
                stations.Add(facility.Key, station);
                consumedInventoryIds.Add(inventoryId);
            }
            foreach (string inventoryId in inventories.Keys)
            {
                if (!consumedInventoryIds.Contains(inventoryId))
                    throw new NotSupportedException(
                        $"当前 Foundation 尚不能恢复库存 {inventoryId}，拒绝静默丢弃。 ");
            }

            ValidateFoundationRandomStreams(data);
            ToIntCursor(GetRandomStreamCursor(
                data,
                LeisureOutcomeRandomStreamId,
                fallback: 0L), LeisureOutcomeRandomStreamId);
            ToIntCursor(GetRandomStreamCursor(
                data,
                ResidentActionSequenceStreamId,
                fallback: 1L), ResidentActionSequenceStreamId);

            ResolveWaterCanLocation(
                waterCan.OwnerEntityId,
                facilityFunctions,
                out FoundationWaterCanLocation waterCanLocation,
                out string waterCanAnchor);
            return new FoundationRestoreData(
                resident,
                vehicleWater,
                waterCan,
                toiletHolding,
                bodyWater,
                bladder,
                vehicleWaterAmount,
                waterCanAmount,
                toiletHoldingAmount,
                bodyWaterAmount,
                bladderAmount,
                waterCanLocation,
                waterCanAnchor,
                stations);
        }

        private static void ValidateFoundationInventoryMetadata(
            NomadInventorySaveData inventory)
        {
            if (inventory.ContaminationPermille != 0)
                throw new NotSupportedException(
                    $"当前 Foundation 尚未接入库存污染，不能恢复 {inventory.InventoryId} 的污染值。");
            for (var i = 0; i < inventory.Contents.Count; i++)
            {
                NomadResourceStackSaveData stack = inventory.Contents[i];
                if (stack.ConditionPermille != 1000 || stack.ContaminationPermille != 0)
                    throw new NotSupportedException(
                        $"当前 Foundation 尚未接入资源批次状态 / 污染，不能恢复 {stack.StackId}。");
            }
        }

        private static void ValidateFoundationRandomStreams(NomadWorkshopSaveData data)
        {
            for (var i = 0; i < data.RandomStreams.Count; i++)
            {
                NomadRandomStreamSaveData stream = data.RandomStreams[i];
                bool knownOwner = string.Equals(
                    stream.OwnerEntityId,
                    ResidentStableId,
                    StringComparison.Ordinal);
                bool knownStream = string.Equals(
                                       stream.StreamId,
                                       ResidentDecisionRandomStreamId,
                                       StringComparison.Ordinal) ||
                                   string.Equals(
                                       stream.StreamId,
                                       BladderOpportunityRandomStreamId,
                                       StringComparison.Ordinal) ||
                                   string.Equals(
                                       stream.StreamId,
                                       LeisureOutcomeRandomStreamId,
                                       StringComparison.Ordinal) ||
                                   string.Equals(
                                       stream.StreamId,
                                       WorkPaceRandomStreamId,
                                       StringComparison.Ordinal) ||
                                   string.Equals(
                                       stream.StreamId,
                                       ResidentActionSequenceStreamId,
                                       StringComparison.Ordinal);
                if (!knownOwner || !knownStream)
                    throw new NotSupportedException(
                        $"当前 Foundation 尚不能消费随机流 " +
                        $"{stream.OwnerEntityId}/{stream.StreamId}，拒绝静默丢弃。 ");
            }
        }

        private static NomadInventorySaveData CreateInventorySaveData(
            ResourceInventory inventory,
            string ownerEntityId,
            int? amountOverride = null)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            var result = new NomadInventorySaveData
            {
                InventoryId = inventory.Id,
                OwnerEntityId = ownerEntityId ?? string.Empty,
                Measure = inventory.Measure,
                CapacityBaseUnits = inventory.Capacity,
            };

            IReadOnlyList<ResourceQuantity> contents = inventory.GetContentsSnapshot();
            if (amountOverride.HasValue)
            {
                if (contents.Count > 1)
                    throw new InvalidOperationException(
                        $"库存 {inventory.Id} 有多种资源，不能用单一数量覆盖形成检查点。");
                if (amountOverride.Value < 0 || amountOverride.Value > inventory.Capacity)
                    throw new ArgumentOutOfRangeException(nameof(amountOverride));
                if (amountOverride.Value == 0) return result;
                if (contents.Count == 0)
                    contents = new[]
                    {
                        new ResourceQuantity(NomadResourceIds.Water, amountOverride.Value),
                    };
                else
                    contents = new[]
                    {
                        new ResourceQuantity(contents[0].Resource, amountOverride.Value),
                    };
            }

            for (var i = 0; i < contents.Count; i++)
            {
                ResourceQuantity quantity = contents[i];
                result.Contents.Add(new NomadResourceStackSaveData
                {
                    StackId = $"{inventory.Id}:{quantity.Resource.Value}",
                    ResourceId = quantity.Resource.Value,
                    Measure = quantity.Resource.Measure,
                    AmountBaseUnits = quantity.Amount,
                });
            }
            return result;
        }

        private static ResourceInventory CreateSingleResourceInventory(
            NomadInventorySaveData saved,
            ResourceId resource,
            int amount)
        {
            ResourceQuantity[] contents = amount <= 0
                ? Array.Empty<ResourceQuantity>()
                : new[] { new ResourceQuantity(resource, amount) };
            return new ResourceInventory(
                saved.InventoryId,
                saved.Measure,
                saved.CapacityBaseUnits,
                contents);
        }

        private static int GetSingleResourceAmount(
            NomadInventorySaveData inventory,
            ResourceId expectedResource)
        {
            if (inventory.Measure != expectedResource.Measure)
                throw new InvalidOperationException(
                    $"库存 {inventory.InventoryId} 的量纲不是 {expectedResource.Measure}。");
            var amount = 0;
            for (var i = 0; i < inventory.Contents.Count; i++)
            {
                NomadResourceStackSaveData stack = inventory.Contents[i];
                if (!string.Equals(
                        stack.ResourceId,
                        expectedResource.Value,
                        StringComparison.Ordinal) ||
                    stack.Measure != expectedResource.Measure)
                    throw new NotSupportedException(
                        $"Foundation 库存 {inventory.InventoryId} 尚不支持资源 " +
                        $"{stack.ResourceId}/{stack.Measure}。");
                amount = checked(amount + stack.AmountBaseUnits);
            }
            return amount;
        }

        private static NomadInventorySaveData RequireInventory(
            IReadOnlyDictionary<string, NomadInventorySaveData> inventories,
            string inventoryId,
            ResourceMeasure measure)
        {
            if (!inventories.TryGetValue(inventoryId, out NomadInventorySaveData inventory))
                throw new InvalidOperationException($"Foundation 检查点缺少库存 {inventoryId}。");
            if (inventory.Measure != measure)
                throw new InvalidOperationException(
                    $"Foundation 库存 {inventoryId} 应使用 {measure}，实际为 {inventory.Measure}。");
            return inventory;
        }

        private string ResolveWaterCanSaveOwner()
        {
            if (_waterCanLocation == FoundationWaterCanLocation.Resident)
                return ResidentStableId;
            if (string.IsNullOrWhiteSpace(_waterCanAnchorFacilityInstanceId))
                throw new InvalidOperationException("非携带状态的水罐缺少精确设施锚点。");
            return _waterCanAnchorFacilityInstanceId;
        }

        private static void ResolveWaterCanLocation(
            string ownerEntityId,
            IReadOnlyDictionary<string, NomadFacilityFunction> facilityFunctions,
            out FoundationWaterCanLocation location,
            out string anchor)
        {
            if (string.Equals(ownerEntityId, ResidentStableId, StringComparison.Ordinal))
            {
                location = FoundationWaterCanLocation.Resident;
                anchor = string.Empty;
                return;
            }
            if (!facilityFunctions.TryGetValue(
                    ownerEntityId ?? string.Empty,
                    out NomadFacilityFunction function))
                throw new InvalidOperationException(
                    $"水罐 owner '{ownerEntityId}' 不是居民或已保存设施。");
            location = function switch
            {
                NomadFacilityFunction.VehicleWaterTank =>
                    FoundationWaterCanLocation.VehicleWaterTank,
                NomadFacilityFunction.DrinkingStation =>
                    FoundationWaterCanLocation.DrinkingStation,
                _ => throw new InvalidOperationException(
                    $"水罐不能锚定到功能为 {function} 的设施 {ownerEntityId}。"),
            };
            anchor = ownerEntityId;
        }

        private string FindRequiredFacilityInstanceId(NomadFacilityFunction function) =>
            FindRequiredFacilityInstanceId(_model.Facilities, function);

        private string FindRequiredFacilityInstanceId(
            IReadOnlyList<FoundationFacilityState> facilities,
            NomadFacilityFunction function)
        {
            for (var i = 0; i < facilities.Count; i++)
            {
                FoundationFacilityState facility = facilities[i];
                if (_definitions.TryGetValue(
                        facility.DefinitionId,
                        out NomadFacilityDefinition definition) &&
                    definition.Function == function)
                    return facility.InstanceId;
            }
            throw new InvalidOperationException($"已提交世界缺少功能为 {function} 的设施。");
        }

        private static bool ContainsFunction(
            IReadOnlyDictionary<string, NomadFacilityFunction> facilities,
            NomadFacilityFunction expected)
        {
            foreach (NomadFacilityFunction function in facilities.Values)
            {
                if (function == expected) return true;
            }
            return false;
        }

        private static NomadRandomStreamSaveData CreateRandomStream(
            string streamId,
            long nextEventSequence) => new()
        {
            OwnerEntityId = ResidentStableId,
            StreamId = streamId,
            NextEventSequence = nextEventSequence,
        };

        private static long GetRandomStreamCursor(
            NomadWorkshopSaveData data,
            string streamId,
            long fallback)
        {
            for (var i = 0; i < data.RandomStreams.Count; i++)
            {
                NomadRandomStreamSaveData stream = data.RandomStreams[i];
                if (string.Equals(
                        stream.OwnerEntityId,
                        ResidentStableId,
                        StringComparison.Ordinal) &&
                    string.Equals(stream.StreamId, streamId, StringComparison.Ordinal))
                    return stream.NextEventSequence;
            }
            return fallback;
        }

        private static int ToIntCursor(long value, string streamId)
        {
            if (value < 0L || value > int.MaxValue)
                throw new InvalidOperationException(
                    $"随机流 {streamId} 的游标 {value} 超出当前 Foundation 执行器范围。");
            return (int)value;
        }

        private static int FindNextFacilitySequence(
            IReadOnlyList<FoundationFacilityState> facilities)
        {
            var maximum = 0;
            for (var i = 0; i < facilities.Count; i++)
            {
                string id = facilities[i].InstanceId;
                if (id == null || !id.StartsWith("facility-", StringComparison.Ordinal) ||
                    !int.TryParse(
                        id.AsSpan("facility-".Length),
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out int sequence))
                    continue;
                maximum = Math.Max(maximum, sequence);
            }
            return checked(maximum + 1);
        }

        private static int ToPermille(float value) =>
            Mathf.Clamp(Mathf.RoundToInt(value * 1000f), 0, 1000);

        private void EnsureCheckpointRuntimeReady()
        {
            if (!_initialized || _model == null || _navigation == null ||
                _residentWaterCycle == null || _residentWellbeing == null)
                throw new InvalidOperationException("Foundation 尚未完成初始化，不能捕获或恢复运行检查点。");
        }

        private FacilityConditionCycle RestoreFacilityCondition(
            int savedWorldSeed,
            long rootSimulationTick,
            NomadFacilitySaveData saved,
            NomadFacilityFunction function)
        {
            if (saved.FailureThresholdMicroHazard == 0L)
            {
                // v3 早期存档只有耐久 / 污染投影。加载时从根 Tick 开始新风险周期，
                // 不把过去没有记录的风险补算出来；已显示的磨损和积尘则保留下来。
                FacilityConditionCycle legacy = FacilityConditionCycle.Create(
                    savedWorldSeed,
                    saved.InstanceId,
                    rootSimulationTick);
                legacy.ApplyConditionShock(
                    wearPermille: 1000 - saved.DurabilityPermille,
                    maintenanceDebtPermille: 0,
                    dustPermille: saved.DirtPermille);
                return legacy;
            }

            var checkpoint = new FacilityConditionCheckpoint(
                saved.InstanceId,
                saved.ConditionLastSettledSimulationTick,
                saved.WearConditionUnits,
                saved.MaintenanceDebtConditionUnits,
                saved.DustConditionUnits,
                saved.FailureThresholdMicroHazard,
                saved.AccumulatedFailureMicroHazard,
                saved.FailureHazardSubMicroRemainder,
                saved.FailureCycleSequence,
                saved.ActiveFault,
                saved.FaultSeverityPermille,
                saved.FaultTriggeredSimulationTick);
            var restored = new FacilityConditionCycle(savedWorldSeed, checkpoint);
            restored.AdvanceTo(rootSimulationTick, GetConditionExposure(function));
            return restored;
        }

        private sealed class FoundationRestoreData
        {
            public FoundationRestoreData(
                NomadResidentSaveData resident,
                NomadInventorySaveData vehicleWater,
                NomadInventorySaveData waterCan,
                NomadInventorySaveData toiletHolding,
                NomadInventorySaveData bodyWater,
                NomadInventorySaveData bladder,
                int vehicleWaterAmount,
                int waterCanAmount,
                int toiletHoldingAmount,
                int bodyWaterAmount,
                int bladderAmount,
                FoundationWaterCanLocation waterCanLocation,
                string waterCanAnchor,
                Dictionary<string, NomadInventorySaveData> stationInventories)
            {
                Resident = resident;
                VehicleWater = vehicleWater;
                WaterCan = waterCan;
                ToiletHolding = toiletHolding;
                BodyWater = bodyWater;
                Bladder = bladder;
                VehicleWaterAmount = vehicleWaterAmount;
                WaterCanAmount = waterCanAmount;
                ToiletHoldingAmount = toiletHoldingAmount;
                BodyWaterAmount = bodyWaterAmount;
                BladderAmount = bladderAmount;
                WaterCanLocation = waterCanLocation;
                WaterCanAnchor = waterCanAnchor;
                StationInventories = stationInventories;
            }

            public NomadResidentSaveData Resident { get; }
            public NomadInventorySaveData VehicleWater { get; }
            public NomadInventorySaveData WaterCan { get; }
            public NomadInventorySaveData ToiletHolding { get; }
            public NomadInventorySaveData BodyWater { get; }
            public NomadInventorySaveData Bladder { get; }
            public int VehicleWaterAmount { get; }
            public int WaterCanAmount { get; }
            public int ToiletHoldingAmount { get; }
            public int BodyWaterAmount { get; }
            public int BladderAmount { get; }
            public FoundationWaterCanLocation WaterCanLocation { get; }
            public string WaterCanAnchor { get; }
            public Dictionary<string, NomadInventorySaveData> StationInventories { get; }
        }
    }
}
