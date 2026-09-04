using System;

namespace Game.NomadWorkshop.Simulation
{
    /// <summary>首条设施故障链当前允许产生的具体模式；不使用笼统的 Broken 布尔值。</summary>
    public enum FacilityFaultKind
    {
        None = 0,
        OutletValveJammed = 1,
    }

    /// <summary>面向玩家与开发 Harness 的分档预警；连续真值仍保留在状态机中。</summary>
    public enum FacilityConditionWarning
    {
        Normal = 0,
        Watch = 1,
        ServiceDue = 2,
        Critical = 3,
        Faulted = 4,
    }

    /// <summary>
    /// 一段时间内保持不变的设施暴露速率。连续状态使用“一千分点的百万分之一”为整数单位；
    /// 风险系数表示每个状态千分点、每模拟秒贡献多少微风险。
    /// </summary>
    public readonly struct FacilityConditionExposure
    {
        public FacilityConditionExposure(
            long wearUnitsPerMillisecond,
            long maintenanceDebtUnitsPerMillisecond,
            long dustUnitsPerMillisecond,
            long baseMicroHazardPerSecond,
            long wearMicroHazardPerPermilleSecond,
            long maintenanceMicroHazardPerPermilleSecond,
            long dustMicroHazardPerPermilleSecond)
        {
            ValidateNonNegative(wearUnitsPerMillisecond, nameof(wearUnitsPerMillisecond));
            ValidateNonNegative(
                maintenanceDebtUnitsPerMillisecond,
                nameof(maintenanceDebtUnitsPerMillisecond));
            ValidateNonNegative(dustUnitsPerMillisecond, nameof(dustUnitsPerMillisecond));
            ValidateNonNegative(baseMicroHazardPerSecond, nameof(baseMicroHazardPerSecond));
            ValidateNonNegative(
                wearMicroHazardPerPermilleSecond,
                nameof(wearMicroHazardPerPermilleSecond));
            ValidateNonNegative(
                maintenanceMicroHazardPerPermilleSecond,
                nameof(maintenanceMicroHazardPerPermilleSecond));
            ValidateNonNegative(
                dustMicroHazardPerPermilleSecond,
                nameof(dustMicroHazardPerPermilleSecond));

            WearUnitsPerMillisecond = wearUnitsPerMillisecond;
            MaintenanceDebtUnitsPerMillisecond = maintenanceDebtUnitsPerMillisecond;
            DustUnitsPerMillisecond = dustUnitsPerMillisecond;
            BaseMicroHazardPerSecond = baseMicroHazardPerSecond;
            WearMicroHazardPerPermilleSecond = wearMicroHazardPerPermilleSecond;
            MaintenanceMicroHazardPerPermilleSecond =
                maintenanceMicroHazardPerPermilleSecond;
            DustMicroHazardPerPermilleSecond = dustMicroHazardPerPermilleSecond;
        }

        public long WearUnitsPerMillisecond { get; }
        public long MaintenanceDebtUnitsPerMillisecond { get; }
        public long DustUnitsPerMillisecond { get; }
        public long BaseMicroHazardPerSecond { get; }
        public long WearMicroHazardPerPermilleSecond { get; }
        public long MaintenanceMicroHazardPerPermilleSecond { get; }
        public long DustMicroHazardPerPermilleSecond { get; }

        private static void ValidateNonNegative(long value, string parameterName)
        {
            if (value < 0L) throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    /// <summary>
    /// 设施状态机的完整持久边界。故障阈值、累计风险、亚微风险余量和故障周期序号必须一起保存，
    /// 才能保证暂停、倍速、不同帧步长与读档继续同一条事故轨迹。
    /// </summary>
    public readonly struct FacilityConditionCheckpoint
    {
        public FacilityConditionCheckpoint(
            string facilityId,
            long lastSettledSimulationTick,
            long wearUnits,
            long maintenanceDebtUnits,
            long dustUnits,
            long failureThresholdMicroHazard,
            long accumulatedFailureMicroHazard,
            long hazardSubMicroRemainder,
            long failureCycleSequence,
            FacilityFaultKind activeFault,
            int faultSeverityPermille,
            long faultTriggeredSimulationTick)
        {
            if (string.IsNullOrWhiteSpace(facilityId))
                throw new ArgumentException("设施 id 不能为空。", nameof(facilityId));
            if (lastSettledSimulationTick < 0L)
                throw new ArgumentOutOfRangeException(nameof(lastSettledSimulationTick));
            ValidateConditionUnits(wearUnits, nameof(wearUnits));
            ValidateConditionUnits(maintenanceDebtUnits, nameof(maintenanceDebtUnits));
            ValidateConditionUnits(dustUnits, nameof(dustUnits));
            if (failureThresholdMicroHazard <= 0L)
                throw new ArgumentOutOfRangeException(nameof(failureThresholdMicroHazard));
            if (accumulatedFailureMicroHazard < 0L ||
                accumulatedFailureMicroHazard > failureThresholdMicroHazard)
                throw new ArgumentOutOfRangeException(nameof(accumulatedFailureMicroHazard));
            if (hazardSubMicroRemainder < 0L ||
                hazardSubMicroRemainder >= FacilityConditionCycle.HazardSubUnitsPerMicroHazard)
                throw new ArgumentOutOfRangeException(nameof(hazardSubMicroRemainder));
            if (failureCycleSequence < 0L)
                throw new ArgumentOutOfRangeException(nameof(failureCycleSequence));
            if (!Enum.IsDefined(typeof(FacilityFaultKind), activeFault))
                throw new ArgumentOutOfRangeException(nameof(activeFault));
            if (faultSeverityPermille < 0 || faultSeverityPermille > 1000)
                throw new ArgumentOutOfRangeException(nameof(faultSeverityPermille));
            if (faultTriggeredSimulationTick < 0L ||
                faultTriggeredSimulationTick > lastSettledSimulationTick)
                throw new ArgumentOutOfRangeException(nameof(faultTriggeredSimulationTick));
            if (activeFault == FacilityFaultKind.None)
            {
                if (faultSeverityPermille != 0 || faultTriggeredSimulationTick != 0L ||
                    accumulatedFailureMicroHazard == failureThresholdMicroHazard)
                    throw new ArgumentException("无故障状态不能携带故障结果或已触发风险。", nameof(activeFault));
            }
            else if (faultSeverityPermille <= 0 ||
                     accumulatedFailureMicroHazard != failureThresholdMicroHazard)
            {
                throw new ArgumentException("具体故障必须携带严重度和已触发风险。", nameof(activeFault));
            }

            FacilityId = facilityId.Trim();
            LastSettledSimulationTick = lastSettledSimulationTick;
            WearUnits = wearUnits;
            MaintenanceDebtUnits = maintenanceDebtUnits;
            DustUnits = dustUnits;
            FailureThresholdMicroHazard = failureThresholdMicroHazard;
            AccumulatedFailureMicroHazard = accumulatedFailureMicroHazard;
            HazardSubMicroRemainder = hazardSubMicroRemainder;
            FailureCycleSequence = failureCycleSequence;
            ActiveFault = activeFault;
            FaultSeverityPermille = faultSeverityPermille;
            FaultTriggeredSimulationTick = faultTriggeredSimulationTick;
        }

        public string FacilityId { get; }
        public long LastSettledSimulationTick { get; }
        public long WearUnits { get; }
        public long MaintenanceDebtUnits { get; }
        public long DustUnits { get; }
        public long FailureThresholdMicroHazard { get; }
        public long AccumulatedFailureMicroHazard { get; }
        public long HazardSubMicroRemainder { get; }
        public long FailureCycleSequence { get; }
        public FacilityFaultKind ActiveFault { get; }
        public int FaultSeverityPermille { get; }
        public long FaultTriggeredSimulationTick { get; }

        private static void ValidateConditionUnits(long value, string parameterName)
        {
            if (value < 0L || value > FacilityConditionCycle.MaximumConditionUnits)
                throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    /// <summary>
    /// 把等效磨损、维护欠账、积尘与离散故障分开保存的纯 C# 状态机。连续状态按模拟毫秒
    /// 离散积分，风险使用可保存余量；相同 Tick 区间无论一次推进或拆成多帧都得到同一结果。
    /// </summary>
    public sealed class FacilityConditionCycle
    {
        public const long ConditionUnitsPerPermille = 1_000_000L;
        public const long MaximumConditionUnits = 1_000L * ConditionUnitsPerPermille;
        public const long HazardSubUnitsPerMicroHazard = 1_000_000_000L;

        private const string FailureRandomStreamId = "facility-condition:failure";
        private const long MinimumFailureThresholdMicroHazard = 250_000L;
        private const long ExponentialThresholdScale = 1_000_000L;

        private readonly int _worldSeed;
        private readonly string _facilityId;
        private readonly ulong _ownerId;
        private FailureHazardAccumulator _hazard;
        private long _lastSettledSimulationTick;
        private long _wearUnits;
        private long _maintenanceDebtUnits;
        private long _dustUnits;
        private long _hazardSubMicroRemainder;
        private long _failureCycleSequence;
        private FacilityFaultKind _activeFault;
        private int _faultSeverityPermille;
        private long _faultTriggeredSimulationTick;

        public FacilityConditionCycle(
            int worldSeed,
            in FacilityConditionCheckpoint checkpoint)
        {
            _worldSeed = worldSeed;
            _facilityId = checkpoint.FacilityId;
            _ownerId = DeterministicRandom.HashStableString(_facilityId);
            _lastSettledSimulationTick = checkpoint.LastSettledSimulationTick;
            _wearUnits = checkpoint.WearUnits;
            _maintenanceDebtUnits = checkpoint.MaintenanceDebtUnits;
            _dustUnits = checkpoint.DustUnits;
            _hazard = new FailureHazardAccumulator(
                checkpoint.FailureThresholdMicroHazard,
                checkpoint.AccumulatedFailureMicroHazard);
            _hazardSubMicroRemainder = checkpoint.HazardSubMicroRemainder;
            _failureCycleSequence = checkpoint.FailureCycleSequence;
            _activeFault = checkpoint.ActiveFault;
            _faultSeverityPermille = checkpoint.FaultSeverityPermille;
            _faultTriggeredSimulationTick = checkpoint.FaultTriggeredSimulationTick;
        }

        public string FacilityId => _facilityId;
        public long LastSettledSimulationTick => _lastSettledSimulationTick;
        public int WearPermille => ToPermille(_wearUnits);
        public int MaintenanceDebtPermille => ToPermille(_maintenanceDebtUnits);
        public int DustPermille => ToPermille(_dustUnits);
        public long FailureThresholdMicroHazard => _hazard.ThresholdMicroHazard;
        public long AccumulatedFailureMicroHazard => _hazard.AccumulatedMicroHazard;
        public int FailureRiskProgressPermille => (int)Math.Min(
            1000m,
            decimal.Floor(
                (decimal)_hazard.AccumulatedMicroHazard * 1000m /
                _hazard.ThresholdMicroHazard));
        public long FailureCycleSequence => _failureCycleSequence;
        public FacilityFaultKind ActiveFault => _activeFault;
        public int FaultSeverityPermille => _faultSeverityPermille;
        public long FaultTriggeredSimulationTick => _faultTriggeredSimulationTick;
        public bool IsFaulted => _activeFault != FacilityFaultKind.None;

        public FacilityConditionWarning Warning => IsFaulted
            ? FacilityConditionWarning.Faulted
            : FailureRiskProgressPermille >= 800 || MaintenanceDebtPermille >= 850
                ? FacilityConditionWarning.Critical
                : MaintenanceDebtPermille >= 500 || DustPermille >= 500
                    ? FacilityConditionWarning.ServiceDue
                    : FailureRiskProgressPermille >= 500 || WearPermille >= 300 ||
                      DustPermille >= 300
                        ? FacilityConditionWarning.Watch
                        : FacilityConditionWarning.Normal;

        public static FacilityConditionCycle Create(
            int worldSeed,
            string facilityId,
            long initialSimulationTick = 0L)
        {
            if (string.IsNullOrWhiteSpace(facilityId))
                throw new ArgumentException("设施 id 不能为空。", nameof(facilityId));
            if (initialSimulationTick < 0L)
                throw new ArgumentOutOfRangeException(nameof(initialSimulationTick));
            string normalizedId = facilityId.Trim();
            long threshold = SampleFailureThreshold(worldSeed, normalizedId, 0L);
            return new FacilityConditionCycle(
                worldSeed,
                new FacilityConditionCheckpoint(
                    normalizedId,
                    initialSimulationTick,
                    0L,
                    0L,
                    0L,
                    threshold,
                    0L,
                    0L,
                    0L,
                    FacilityFaultKind.None,
                    0,
                    0L));
        }

        /// <summary>
        /// 懒结算到绝对模拟 Tick。返回 true 仅表示本段首次触发故障；故障后仍继续积累灰尘、欠保养和磨损，
        /// 但不会静默开启下一轮风险，必须先完成修理。
        /// </summary>
        public bool AdvanceTo(
            long targetSimulationTick,
            in FacilityConditionExposure exposure)
        {
            if (targetSimulationTick < _lastSettledSimulationTick)
                throw new ArgumentOutOfRangeException(
                    nameof(targetSimulationTick),
                    "设施状态不能倒退结算。");
            long elapsedMilliseconds = targetSimulationTick - _lastSettledSimulationTick;
            if (elapsedMilliseconds == 0L) return false;

            long startTick = _lastSettledSimulationTick;
            long startWear = _wearUnits;
            long startMaintenance = _maintenanceDebtUnits;
            long startDust = _dustUnits;
            bool triggered = false;

            if (!IsFaulted)
            {
                decimal hazardSubUnits = ComputeHazardSubUnits(
                    elapsedMilliseconds,
                    startWear,
                    startMaintenance,
                    startDust,
                    exposure);
                decimal totalSubUnits = _hazardSubMicroRemainder + hazardSubUnits;
                decimal integratedHazard = decimal.Floor(
                    totalSubUnits / HazardSubUnitsPerMicroHazard);
                if (integratedHazard >= _hazard.RemainingMicroHazard)
                {
                    long triggerOffset = FindTriggerOffsetMilliseconds(
                        elapsedMilliseconds,
                        startWear,
                        startMaintenance,
                        startDust,
                        exposure);
                    _hazard.Accumulate(_hazard.RemainingMicroHazard);
                    _hazardSubMicroRemainder = 0L;
                    TriggerFault(checked(startTick + triggerOffset));
                    triggered = true;
                }
                else
                {
                    long wholeMicroHazard = (long)integratedHazard;
                    _hazard.Accumulate(wholeMicroHazard);
                    decimal remainder = totalSubUnits -
                                        integratedHazard * HazardSubUnitsPerMicroHazard;
                    _hazardSubMicroRemainder = (long)remainder;
                }
            }

            _wearUnits = AdvanceCondition(
                startWear,
                exposure.WearUnitsPerMillisecond,
                elapsedMilliseconds);
            _maintenanceDebtUnits = AdvanceCondition(
                startMaintenance,
                exposure.MaintenanceDebtUnitsPerMillisecond,
                elapsedMilliseconds);
            _dustUnits = AdvanceCondition(
                startDust,
                exposure.DustUnitsPerMillisecond,
                elapsedMilliseconds);
            _lastSettledSimulationTick = targetSimulationTick;
            return triggered;
        }

        /// <summary>一次明确环境冲击，例如沙尘暴；它改变具体来源状态，不直接伪造已经发生的故障。</summary>
        public void ApplyConditionShock(
            int wearPermille,
            int maintenanceDebtPermille,
            int dustPermille)
        {
            ValidatePermilleDelta(wearPermille, nameof(wearPermille));
            ValidatePermilleDelta(maintenanceDebtPermille, nameof(maintenanceDebtPermille));
            ValidatePermilleDelta(dustPermille, nameof(dustPermille));
            _wearUnits = AddCondition(_wearUnits, wearPermille);
            _maintenanceDebtUnits = AddCondition(
                _maintenanceDebtUnits,
                maintenanceDebtPermille);
            _dustUnits = AddCondition(_dustUnits, dustPermille);
        }

        /// <summary>
        /// 完成普通清洁与保养。它降低积尘和维护欠账，但不倒扣已经暴露的累计风险，
        /// 也不会把等效磨损或已经发生的故障自动变没。
        /// </summary>
        public bool PerformMaintenance(
            int maintenanceDebtReductionPermille,
            int dustReductionPermille)
        {
            ValidatePermilleDelta(
                maintenanceDebtReductionPermille,
                nameof(maintenanceDebtReductionPermille));
            ValidatePermilleDelta(dustReductionPermille, nameof(dustReductionPermille));
            long previousMaintenance = _maintenanceDebtUnits;
            long previousDust = _dustUnits;
            _maintenanceDebtUnits = SubtractCondition(
                _maintenanceDebtUnits,
                maintenanceDebtReductionPermille);
            _dustUnits = SubtractCondition(_dustUnits, dustReductionPermille);
            return previousMaintenance != _maintenanceDebtUnits || previousDust != _dustUnits;
        }

        /// <summary>
        /// 累加一次离散冲击风险。开发 Harness 用它可靠抵达故障；以后颠簸、撞击也可复用同一入口。
        /// </summary>
        public bool ApplyHazardShock(long integratedMicroHazard)
        {
            if (integratedMicroHazard < 0L)
                throw new ArgumentOutOfRangeException(nameof(integratedMicroHazard));
            if (IsFaulted || integratedMicroHazard == 0L) return false;
            if (!_hazard.Accumulate(integratedMicroHazard)) return false;
            _hazardSubMicroRemainder = 0L;
            TriggerFault(_lastSettledSimulationTick);
            return true;
        }

        /// <summary>
        /// 修复已经发生的具体故障并开始新的预取样风险周期。普通修理不清零磨损、欠保养或积尘。
        /// </summary>
        public bool Repair()
        {
            if (!IsFaulted) return false;
            _failureCycleSequence = checked(_failureCycleSequence + 1L);
            _hazard = new FailureHazardAccumulator(SampleFailureThreshold(
                _worldSeed,
                _facilityId,
                _failureCycleSequence));
            _hazardSubMicroRemainder = 0L;
            _activeFault = FacilityFaultKind.None;
            _faultSeverityPermille = 0;
            _faultTriggeredSimulationTick = 0L;
            return true;
        }

        public decimal GetCurrentRiskRate(in FacilityConditionExposure exposure) =>
            exposure.BaseMicroHazardPerSecond +
            exposure.WearMicroHazardPerPermilleSecond *
            (_wearUnits / (decimal)ConditionUnitsPerPermille) +
            exposure.MaintenanceMicroHazardPerPermilleSecond *
            (_maintenanceDebtUnits / (decimal)ConditionUnitsPerPermille) +
            exposure.DustMicroHazardPerPermilleSecond *
            (_dustUnits / (decimal)ConditionUnitsPerPermille);

        public FacilityConditionCheckpoint CaptureCheckpoint() => new(
            _facilityId,
            _lastSettledSimulationTick,
            _wearUnits,
            _maintenanceDebtUnits,
            _dustUnits,
            _hazard.ThresholdMicroHazard,
            _hazard.AccumulatedMicroHazard,
            _hazardSubMicroRemainder,
            _failureCycleSequence,
            _activeFault,
            _faultSeverityPermille,
            _faultTriggeredSimulationTick);

        private long FindTriggerOffsetMilliseconds(
            long elapsedMilliseconds,
            long startWear,
            long startMaintenance,
            long startDust,
            in FacilityConditionExposure exposure)
        {
            long low = 1L;
            long high = elapsedMilliseconds;
            while (low < high)
            {
                long middle = low + (high - low) / 2L;
                decimal subUnits = _hazardSubMicroRemainder + ComputeHazardSubUnits(
                    middle,
                    startWear,
                    startMaintenance,
                    startDust,
                    exposure);
                decimal integrated = decimal.Floor(
                    subUnits / HazardSubUnitsPerMicroHazard);
                if (integrated >= _hazard.RemainingMicroHazard)
                    high = middle;
                else
                    low = middle + 1L;
            }
            return low;
        }

        private static decimal ComputeHazardSubUnits(
            long elapsedMilliseconds,
            long startWear,
            long startMaintenance,
            long startDust,
            in FacilityConditionExposure exposure)
        {
            decimal result = (decimal)exposure.BaseMicroHazardPerSecond *
                             elapsedMilliseconds * 1_000_000m;
            result += exposure.WearMicroHazardPerPermilleSecond *
                      SumConditionStartValues(
                          startWear,
                          exposure.WearUnitsPerMillisecond,
                          elapsedMilliseconds);
            result += exposure.MaintenanceMicroHazardPerPermilleSecond *
                      SumConditionStartValues(
                          startMaintenance,
                          exposure.MaintenanceDebtUnitsPerMillisecond,
                          elapsedMilliseconds);
            result += exposure.DustMicroHazardPerPermilleSecond *
                      SumConditionStartValues(
                          startDust,
                          exposure.DustUnitsPerMillisecond,
                          elapsedMilliseconds);
            return result;
        }

        /// <summary>求每个离散模拟毫秒起点的条件值之和；拆分区间时仍严格可加。</summary>
        private static decimal SumConditionStartValues(
            long startUnits,
            long unitsPerMillisecond,
            long elapsedMilliseconds)
        {
            if (elapsedMilliseconds <= 0L) return 0m;
            if (startUnits >= MaximumConditionUnits)
                return (decimal)MaximumConditionUnits * elapsedMilliseconds;
            if (unitsPerMillisecond == 0L)
                return (decimal)startUnits * elapsedMilliseconds;

            long remaining = MaximumConditionUnits - startUnits;
            long millisecondsBeforeCap = remaining / unitsPerMillisecond;
            if (remaining % unitsPerMillisecond != 0L)
                millisecondsBeforeCap++;
            long linearCount = Math.Min(elapsedMilliseconds, millisecondsBeforeCap);
            decimal linearSum = (decimal)linearCount * startUnits +
                                (decimal)unitsPerMillisecond * linearCount *
                                (linearCount - 1L) / 2m;
            return linearSum +
                   (decimal)(elapsedMilliseconds - linearCount) * MaximumConditionUnits;
        }

        private static long AdvanceCondition(
            long startUnits,
            long unitsPerMillisecond,
            long elapsedMilliseconds)
        {
            decimal result = startUnits +
                             (decimal)unitsPerMillisecond * elapsedMilliseconds;
            return result >= MaximumConditionUnits
                ? MaximumConditionUnits
                : (long)result;
        }

        private void TriggerFault(long simulationTick)
        {
            double severitySample = DeterministicRandom.Sample01(
                _worldSeed,
                _ownerId,
                FailureRandomStreamId,
                _failureCycleSequence,
                sampleSlot: 2);
            _activeFault = FacilityFaultKind.OutletValveJammed;
            _faultSeverityPermille = Math.Clamp(
                450 + (int)Math.Round(
                    severitySample * 450d,
                    MidpointRounding.AwayFromZero),
                450,
                900);
            _faultTriggeredSimulationTick = simulationTick;
        }

        private static long SampleFailureThreshold(
            int worldSeed,
            string facilityId,
            long failureCycleSequence)
        {
            ulong ownerId = DeterministicRandom.HashStableString(facilityId);
            double sample = DeterministicRandom.Sample01(
                worldSeed,
                ownerId,
                FailureRandomStreamId,
                failureCycleSequence,
                sampleSlot: 0);
            double exponential = -Math.Log(1d - sample) * ExponentialThresholdScale;
            if (exponential >= long.MaxValue - MinimumFailureThresholdMicroHazard)
                throw new OverflowException("设施故障阈值超过 Int64 可保存范围。");
            return checked(
                MinimumFailureThresholdMicroHazard +
                Math.Max(1L, (long)Math.Round(
                    exponential,
                    MidpointRounding.AwayFromZero)));
        }

        private static int ToPermille(long units) =>
            (int)Math.Clamp(
                (units + ConditionUnitsPerPermille / 2L) / ConditionUnitsPerPermille,
                0L,
                1000L);

        private static long AddCondition(long current, int permille)
        {
            decimal value = current + (decimal)permille * ConditionUnitsPerPermille;
            return value >= MaximumConditionUnits ? MaximumConditionUnits : (long)value;
        }

        private static long SubtractCondition(long current, int permille) => Math.Max(
            0L,
            current - (long)permille * ConditionUnitsPerPermille);

        private static void ValidatePermilleDelta(int value, string parameterName)
        {
            if (value < 0 || value > 1000)
                throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
