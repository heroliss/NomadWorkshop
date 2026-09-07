using System;
using System.Collections.Generic;
using Game.NomadWorkshop.Simulation;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>
    /// Foundation 首条设施状态闭环。纯状态机负责确定性积分，本 partial 只负责设施类型配置、
    /// 玩法后果和 Model 投影，不让 Debug View 直接持有或改写模拟对象。
    /// </summary>
    public sealed partial class NomadFoundationSystem
    {
        private readonly Dictionary<string, FacilityConditionCycle> _facilityConditions =
            new(StringComparer.Ordinal);
        private readonly List<FoundationFacilityConditionState> _facilityConditionProjection =
            new();

        private FacilityConditionCycle CreateFacilityCondition(string facilityInstanceId) =>
            FacilityConditionCycle.Create(
                worldSeed,
                facilityInstanceId,
                _simulationClock.SimulationTick);

        /// <summary>
        /// 把全部设施结算到同一个绝对 Tick。暂停时统一时钟不前进，因此这里天然不会累计；
        /// 变速只改变达到目标 Tick 的墙钟时间，不改变事故轨迹。
        /// </summary>
        private void AdvanceFacilityConditionsTo(long simulationTick)
        {
            IReadOnlyList<FoundationFacilityState> facilities = _model.Facilities;
            for (var i = 0; i < facilities.Count; i++)
            {
                FoundationFacilityState facility = facilities[i];
                if (!_facilityConditions.TryGetValue(
                        facility.InstanceId,
                        out FacilityConditionCycle condition) ||
                    !_definitions.TryGetValue(
                        facility.DefinitionId,
                        out NomadFacilityDefinition definition))
                    continue;

                if (AdvanceConditionThroughEnvironment(
                        condition,
                        definition.Function,
                        simulationTick))
                    HandleNewFacilityFault(facility.InstanceId, condition.ActiveFault);
            }
        }

        private FacilityConditionExposure GetConditionExposure(
            NomadFacilityFunction function,
            in NomadEnvironmentSnapshot environment) =>
            function == NomadFacilityFunction.VehicleWaterTank
                ? new FacilityConditionExposure(
                    waterTankWearUnitsPerMillisecond + ScaleByPermille(
                        waterTankSandstormWearUnitsPerMillisecond,
                        environment.IntensityPermille),
                    waterTankMaintenanceDebtUnitsPerMillisecond + ScaleByPermille(
                        waterTankSandstormMaintenanceDebtUnitsPerMillisecond,
                        environment.IntensityPermille),
                    waterTankDustUnitsPerMillisecond + ScaleByPermille(
                        waterTankSandstormDustUnitsPerMillisecond,
                        environment.IntensityPermille),
                    waterTankBaseMicroHazardPerSecond + ScaleByPermille(
                        waterTankSandstormBaseMicroHazardPerSecond,
                        environment.IntensityPermille),
                    waterTankWearMicroHazardPerPermilleSecond,
                    waterTankMaintenanceMicroHazardPerPermilleSecond,
                    waterTankDustMicroHazardPerPermilleSecond)
                : default;

        /// <summary>
        /// 按天气边界拆分设施积分。这样一次跨过整场沙尘暴与逐帧经过它得到完全相同的整数状态，
        /// Harness、倍速和读档都不会因为步长恰好落在哪一帧而改变事故轨迹。
        /// </summary>
        private bool AdvanceConditionThroughEnvironment(
            FacilityConditionCycle condition,
            NomadFacilityFunction function,
            long targetSimulationTick) =>
            AdvanceConditionThroughEnvironment(
                condition,
                function,
                targetSimulationTick,
                worldSeed);

        private bool AdvanceConditionThroughEnvironment(
            FacilityConditionCycle condition,
            NomadFacilityFunction function,
            long targetSimulationTick,
            int environmentWorldSeed)
        {
            bool triggered = false;
            while (condition.LastSettledSimulationTick < targetSimulationTick)
            {
                NomadEnvironmentSnapshot environment = EnvironmentSchedule.Project(
                    environmentWorldSeed,
                    condition.LastSettledSimulationTick);
                long segmentEnd = Math.Min(
                    targetSimulationTick,
                    environment.NextTransitionSimulationTick);
                if (segmentEnd <= condition.LastSettledSimulationTick)
                    throw new InvalidOperationException(
                        "天气时间表没有向前提供下一个状态边界。 ");
                triggered |= condition.AdvanceTo(
                    segmentEnd,
                    GetConditionExposure(function, environment));
            }
            return triggered;
        }

        private static long ScaleByPermille(long value, int permille) =>
            checked(value * permille / 1000L);

        private void WriteFacilityConditionProjection()
        {
            _facilityConditionProjection.Clear();
            IReadOnlyList<FoundationFacilityState> facilities = _model.Facilities;
            for (var i = 0; i < facilities.Count; i++)
            {
                FoundationFacilityState facility = facilities[i];
                if (!_facilityConditions.TryGetValue(
                        facility.InstanceId,
                        out FacilityConditionCycle condition) ||
                    !_definitions.TryGetValue(
                        facility.DefinitionId,
                        out NomadFacilityDefinition definition))
                    continue;

                decimal exactRiskRate = condition.GetCurrentRiskRate(
                    GetConditionExposure(
                        definition.Function,
                        EnvironmentSchedule.Project(
                            worldSeed,
                            _simulationClock.SimulationTick)));
                long displayedRiskRate = exactRiskRate >= long.MaxValue
                    ? long.MaxValue
                    : (long)decimal.Round(
                        exactRiskRate,
                        0,
                        MidpointRounding.AwayFromZero);
                _facilityConditionProjection.Add(new FoundationFacilityConditionState(
                    facility.InstanceId,
                    definition.Function,
                    condition.WearPermille,
                    condition.MaintenanceDebtPermille,
                    condition.DustPermille,
                    condition.FailureRiskProgressPermille,
                    displayedRiskRate,
                    condition.FailureThresholdMicroHazard,
                    condition.AccumulatedFailureMicroHazard,
                    condition.Warning,
                    condition.ActiveFault,
                    condition.FaultSeverityPermille,
                    condition.FaultTriggeredSimulationTick));
            }
            _model.ReplaceFacilityConditions(_facilityConditionProjection);
        }

        internal bool ApplyPrimaryWaterTankSandstormStress()
        {
            if (!TryGetPrimaryWaterTankCondition(
                    out _,
                    out FacilityConditionCycle condition))
                return false;
            SettleCondition(condition, NomadFacilityFunction.VehicleWaterTank);
            condition.ApplyConditionShock(
                wearPermille: 10,
                maintenanceDebtPermille: 80,
                dustPermille: 250);
            WriteFacilityConditionProjection();
            return true;
        }

        internal bool PerformPrimaryWaterTankMaintenance()
        {
            if (!TryGetPrimaryWaterTankCondition(
                    out _,
                    out FacilityConditionCycle condition))
                return false;
            SettleCondition(condition, NomadFacilityFunction.VehicleWaterTank);
            bool changed = condition.PerformMaintenance(
                maintenanceDebtReductionPermille: 550,
                dustReductionPermille: 500);
            WriteFacilityConditionProjection();
            return changed;
        }

        internal bool ForcePrimaryWaterTankFault()
        {
            if (_checkpointOperation != null) return false;
            if (!TryGetPrimaryWaterTankCondition(
                    out FoundationFacilityState waterTank,
                    out FacilityConditionCycle condition))
                return false;
            SettleCondition(condition, NomadFacilityFunction.VehicleWaterTank);
            long remaining = condition.FailureThresholdMicroHazard -
                             condition.AccumulatedFailureMicroHazard;
            bool triggered = condition.ApplyHazardShock(remaining);
            if (triggered)
                HandleNewFacilityFault(waterTank.InstanceId, condition.ActiveFault);
            WriteFacilityConditionProjection();
            return triggered;
        }

        internal bool RepairPrimaryWaterTankFault()
        {
            if (!TryGetPrimaryWaterTankCondition(
                    out FoundationFacilityState waterTank,
                    out FacilityConditionCycle condition))
                return false;
            SettleCondition(condition, NomadFacilityFunction.VehicleWaterTank);
            if (!condition.Repair()) return false;
            foreach (var resident in _residents)
            {
                using var scope = UseResident(resident);
                bool interruptedResidentRepair = string.Equals(
                    _resident.ActiveRepairTargetFacilityInstanceId,
                    waterTank.InstanceId,
                    StringComparison.Ordinal);
                if (interruptedResidentRepair)
                {
                    // 开发 Harness 允许跳过正式维修链，但不能只拿走 Lease 后留下旧路径 / 阶段。
                    // 整体撤销会把已拿起的维修包恢复到精确托盘来源，并释放功能点容量。
                    ReleaseActiveTasks();
                    ClearActivePath();
                    _resident.PhaseDuration = 0f;
                    _resident.PhaseRemaining = 0f;
                }

                string diagnosticPrefix = BuildWaterTankFaultDiagnostic(waterTank.InstanceId);
                if (_resident.State.LastBlocker.Value.StartsWith(
                        diagnosticPrefix,
                        StringComparison.Ordinal) ||
                    _resident.State.LastBlocker.Value.Contains(
                        "出水阀卡滞",
                        StringComparison.Ordinal))
                    _resident.State.LastBlocker.Value = string.Empty;
                _resident.LastPublishedDecisionDiagnostic = string.Empty;
                _resident.DecisionRetryRemaining = 0f;
                if (interruptedResidentRepair)
                {
                    PublishCurrentFacilityAccessProjection();
                    SetResidentPhase(
                        FoundationResidentPhase.Idle,
                        "Harness 已瞬时修复水箱；居民维修已取消，实体备件已放回托盘");
                }
            }
            WriteFacilityConditionProjection();
            return true;
        }

        private void SettleCondition(
            FacilityConditionCycle condition,
            NomadFacilityFunction function)
        {
            if (AdvanceConditionThroughEnvironment(
                    condition,
                    function,
                    _simulationClock.SimulationTick))
                HandleNewFacilityFault(condition.FacilityId, condition.ActiveFault);
        }

        private bool TryGetPrimaryWaterTankCondition(
            out FoundationFacilityState waterTank,
            out FacilityConditionCycle condition)
        {
            if (TryFindPlacedFacility(
                    NomadFacilityFunction.VehicleWaterTank,
                    out waterTank,
                    out _) &&
                _facilityConditions.TryGetValue(waterTank.InstanceId, out condition))
                return true;

            waterTank = default;
            condition = null;
            return false;
        }

        private bool TryFindOperationalWaterSource(out FoundationFacilityState source)
        {
            IReadOnlyList<FoundationFacilityState> facilities = _model.Facilities;
            for (var i = 0; i < facilities.Count; i++)
            {
                FoundationFacilityState candidate = facilities[i];
                if (!_definitions.TryGetValue(
                        candidate.DefinitionId,
                        out NomadFacilityDefinition definition) ||
                    definition.Function != NomadFacilityFunction.VehicleWaterTank ||
                    !IsWaterSourceOperational(candidate.InstanceId))
                    continue;
                source = candidate;
                return true;
            }

            source = default;
            return false;
        }

        private bool IsWaterSourceOperational(string facilityInstanceId) =>
            !string.IsNullOrWhiteSpace(facilityInstanceId) &&
            _facilityConditions.TryGetValue(
                facilityInstanceId,
                out FacilityConditionCycle condition) &&
            condition.ActiveFault != FacilityFaultKind.OutletValveJammed;

        private void HandleNewFacilityFault(
            string facilityInstanceId,
            FacilityFaultKind fault)
        {
            if (fault != FacilityFaultKind.OutletValveJammed) return;
            foreach (var resident in _residents)
            {
                using var scope = UseResident(resident);
                if (_resident.ActiveHaul == null ||
                _resident.ActiveHaul.State != HaulTaskState.Reserved ||
                !string.Equals(
                    facilityInstanceId,
                    _resident.ActiveWaterSourceFacilityInstanceId,
                    StringComparison.Ordinal))
                    continue;

                CancelPendingWaterHaulForFault(facilityInstanceId);
            }
        }

        private void CancelPendingWaterHaulForFault(string facilityInstanceId)
        {
            ReleaseActiveTasks();
            ClearActivePath();
            PublishCurrentFacilityAccessProjection();
            _resident.DecisionRetryRemaining = 0f;
            string diagnostic = BuildWaterTankFaultDiagnostic(facilityInstanceId);
            _resident.State.LastBlocker.Value = diagnostic;
            SetResidentPhase(
                FoundationResidentPhase.Idle,
                "车辆水箱出水阀在取水前卡滞；已安全释放预留并重新决策");
        }

        private static string BuildWaterTankFaultDiagnostic(string facilityInstanceId) =>
            $"FacilityFault · {facilityInstanceId} · 车辆水箱出水阀卡滞，需要修理";
    }
}
