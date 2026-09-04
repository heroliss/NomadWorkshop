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

                FacilityConditionExposure exposure = GetConditionExposure(
                    definition.Function);
                if (condition.AdvanceTo(simulationTick, exposure))
                    HandleNewFacilityFault(facility.InstanceId, condition.ActiveFault);
            }
        }

        private FacilityConditionExposure GetConditionExposure(
            NomadFacilityFunction function) =>
            function == NomadFacilityFunction.VehicleWaterTank
                ? new FacilityConditionExposure(
                    waterTankWearUnitsPerMillisecond,
                    waterTankMaintenanceDebtUnitsPerMillisecond,
                    waterTankDustUnitsPerMillisecond,
                    waterTankBaseMicroHazardPerSecond,
                    waterTankWearMicroHazardPerPermilleSecond,
                    waterTankMaintenanceMicroHazardPerPermilleSecond,
                    waterTankDustMicroHazardPerPermilleSecond)
                : default;

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
                    GetConditionExposure(definition.Function));
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

            string diagnosticPrefix = BuildWaterTankFaultDiagnostic(waterTank.InstanceId);
            if (_model.LastBlocker.Value.StartsWith(
                    diagnosticPrefix,
                    StringComparison.Ordinal) ||
                _model.LastBlocker.Value.Contains(
                    "出水阀卡滞",
                    StringComparison.Ordinal))
                _model.LastBlocker.Value = string.Empty;
            _lastPublishedDecisionDiagnostic = string.Empty;
            _residentDecisionRetryRemaining = 0f;
            WriteFacilityConditionProjection();
            return true;
        }

        private void SettleCondition(
            FacilityConditionCycle condition,
            NomadFacilityFunction function) =>
            condition.AdvanceTo(
                _simulationClock.SimulationTick,
                GetConditionExposure(function));

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
            if (fault != FacilityFaultKind.OutletValveJammed ||
                _activeHaul == null ||
                _activeHaul.State != HaulTaskState.Reserved ||
                !string.Equals(
                    facilityInstanceId,
                    _activeWaterSourceFacilityInstanceId,
                    StringComparison.Ordinal))
                return;

            CancelPendingWaterHaulForFault(facilityInstanceId);
        }

        private void CancelPendingWaterHaulForFault(string facilityInstanceId)
        {
            ReleaseActiveTasks();
            ClearActivePath();
            PublishCurrentFacilityAccessProjection();
            _residentDecisionRetryRemaining = 0f;
            string diagnostic = BuildWaterTankFaultDiagnostic(facilityInstanceId);
            _model.LastBlocker.Value = diagnostic;
            SetResidentPhase(
                FoundationResidentPhase.Idle,
                "车辆水箱出水阀在取水前卡滞；已安全释放预留并重新决策");
        }

        private static string BuildWaterTankFaultDiagnostic(string facilityInstanceId) =>
            $"FacilityFault · {facilityInstanceId} · 车辆水箱出水阀卡滞，需要修理";
    }
}
