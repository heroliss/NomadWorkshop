using System;
using Game.NomadWorkshop.Simulation;

namespace Game.NomadWorkshop.Foundation
{
    public sealed partial class NomadFoundationSystem
    {
        private const int MinimumSoakStepMilliseconds = 10;
        private const int MaximumSoakStepMilliseconds = 1000;
        private const int MaximumSoakStepCount = 200_000;

        /// <summary>
        /// 在暂停状态下用固定整数步长运行当前已提交世界，并返回结构化证据。它调用与实时
        /// Update 完全相同的模拟步，不推进建造异步事务，也不替代真实输入或玩家试玩。
        /// </summary>
        internal FoundationSoakRunResult RunSoakHarness(
            long durationMilliseconds,
            int stepMilliseconds,
            long observableStallLimitMilliseconds)
        {
            FoundationSoakStopReason rejection = ValidateSoakRequest(
                durationMilliseconds,
                stepMilliseconds,
                observableStallLimitMilliseconds);
            if (rejection != FoundationSoakStopReason.None)
                return CreateRejectedSoakResult(
                    rejection,
                    durationMilliseconds,
                    stepMilliseconds);

            using var movementScope = new SynchronousLocomotionScope(this);
            var accumulator = new FoundationSoakAccumulator(
                this,
                durationMilliseconds,
                stepMilliseconds);
            long remaining = durationMilliseconds;
            long pendingInputMilliseconds = 0L;
            FoundationSoakStopReason stopReason = FoundationSoakStopReason.DurationReached;
            // stepMilliseconds 表达输入分块，实际业务与逐步审计始终为固定 10 ms。
            // 同一分块中也检查死亡 / 停滞，避免粗分块越过应当停止的物理终态。
            while (remaining > 0L)
            {
                long inputMilliseconds = Math.Min(stepMilliseconds, remaining);
                remaining -= inputMilliseconds;
                pendingInputMilliseconds += inputMilliseconds;
                while (pendingInputMilliseconds >= SimulationStepMilliseconds)
                {
                    _simulationClock.AdvanceHarnessStep();
                    AdvanceSimulation(SimulationStepMilliseconds);
                    pendingInputMilliseconds -= SimulationStepMilliseconds;
                    accumulator.Observe(this, SimulationStepMilliseconds);

                    if (!CaptureCohortStats().AllAlive)
                        return accumulator.Complete(this, FoundationSoakStopReason.ResidentDied);
                    if (observableStallLimitMilliseconds > 0L &&
                        accumulator.CurrentObservableStallMilliseconds >=
                        observableStallLimitMilliseconds)
                        return accumulator.Complete(
                            this, FoundationSoakStopReason.ObservableProgressStalled);
                }
            }

            return accumulator.Complete(this, stopReason);
        }

        /// <summary>
        /// 开发 Harness 为下一轮实验切换世界 Seed，并从同一构造路径复位。调用者必须先暂停；
        /// 它会丢弃当前运行进度，因此不作为玩家设置或普通复位的隐式副作用。
        /// </summary>
        internal bool TryResetForSoakHarness(int configuredWorldSeed)
        {
            if (!_initialized || _model == null)
                return false;
            if (!_model.IsPaused.Value)
            {
                _resident.State.LastBlocker.Value =
                    "HarnessRequiresPause · 切换 Seed 会重建当前世界，请先暂停";
                return false;
            }
            if (_model.BuildTransactionPhase.Value != FoundationBuildTransactionPhase.Idle)
            {
                _resident.State.LastBlocker.Value =
                    "BuildTransactionActive · 等待建造导航事务结束后再切换 Seed";
                return false;
            }

            worldSeed = configuredWorldSeed;
            ResetScenarioNow();
            _model.IsPaused.Value = true;
            return true;
        }

        private FoundationSoakStopReason ValidateSoakRequest(
            long durationMilliseconds,
            int stepMilliseconds,
            long observableStallLimitMilliseconds)
        {
            if (!_initialized || _model == null || _resident.WaterCycle == null)
                return FoundationSoakStopReason.NotReady;
            if (_checkpointOperation != null)
                return FoundationSoakStopReason.CheckpointOperationActive;
            if (!_model.IsPaused.Value)
                return FoundationSoakStopReason.RequiresPause;
            if (_model.BuildTransactionPhase.Value != FoundationBuildTransactionPhase.Idle)
                return FoundationSoakStopReason.BuildTransactionActive;
            if (durationMilliseconds <= 0L ||
                stepMilliseconds < MinimumSoakStepMilliseconds ||
                stepMilliseconds > MaximumSoakStepMilliseconds ||
                durationMilliseconds % SimulationStepMilliseconds != 0L ||
                observableStallLimitMilliseconds < 0L ||
                durationMilliseconds > long.MaxValue - _simulationClock.SimulationTick -
                                       _simulationClock.PendingMilliseconds)
                return FoundationSoakStopReason.InvalidRequest;

            long stepCount = durationMilliseconds / SimulationStepMilliseconds;
            if (stepCount > MaximumSoakStepCount)
                return FoundationSoakStopReason.StepBudgetExceeded;
            return FoundationSoakStopReason.None;
        }

        private FoundationSoakRunResult CreateRejectedSoakResult(
            FoundationSoakStopReason reason,
            long durationMilliseconds,
            int stepMilliseconds)
        {
            long tick = _simulationClock?.SimulationTick ?? 0L;
            bool alive = _resident?.Wellbeing?.IsAlive ?? false;
            long water = _resident == null ? 0L : CalculateConservedWaterMilliliters();
            return new FoundationSoakRunResult(
                reason,
                worldSeed,
                durationMilliseconds,
                stepMilliseconds,
                0,
                tick,
                tick,
                FindFirstFacilityFaultTick(),
                alive,
                _resident?.State.ResidentHealth.Value ?? 0f,
                _resident?.State.ResidentThirst.Value ?? 0f,
                _resident?.State.ResidentEntertainment.Value ?? 0f,
                _resident?.State.ResidentMood.Value ?? 0f,
                _resident?.State.ResidentFatigue.Value ?? 0f,
                _resident?.State.ResidentStress.Value ?? 0f,
                water,
                water,
                0L,
                0,
                0,
                0,
                0,
                0,
                0,
                0L,
                0L,
                _resident?.State.CurrentTask.Value,
                _resident?.State.LastBlocker.Value);
        }

        private long CalculateConservedWaterMilliliters()
        {
            if (_model == null) return 0L;
            long total = (long)_model.VehicleWaterMilliliters.Value + _model.WaterCanWaterMilliliters.Value +
                _model.DrinkingStationWaterMilliliters.Value + _model.ToiletHoldingWasteMilliliters.Value +
                _model.StopWaterMilliliters.Value + _model.StopWasteMilliliters.Value;
            foreach (var resident in _residents)
                total += (long)resident.State.BodyWaterMilliliters.Value + resident.State.BladderWasteMilliliters.Value;
            return total;
        }

        private CohortStats CaptureCohortStats()
        {
            var stats = new CohortStats { AllAlive = _residents.Count > 0, Health = 1f, Entertainment = 1f, Mood = 1f };
            foreach (var resident in _residents)
            {
                var state = resident.State;
                stats.AllAlive &= resident.Wellbeing is { IsAlive: true };
                stats.Health = Math.Min(stats.Health, state.ResidentHealth.Value);
                stats.Thirst = Math.Max(stats.Thirst, state.ResidentThirst.Value);
                stats.Entertainment = Math.Min(stats.Entertainment, state.ResidentEntertainment.Value);
                stats.Mood = Math.Min(stats.Mood, state.ResidentMood.Value);
                stats.Fatigue = Math.Max(stats.Fatigue, state.ResidentFatigue.Value);
                stats.Stress = Math.Max(stats.Stress, state.ResidentStress.Value);
                stats.Drinks += state.CompletedDrinkCount.Value;
                stats.ToiletUses += state.CompletedToiletUseCount.Value;
                stats.Daydreams += state.CompletedDaydreamCount.Value;
                stats.Wanders += state.CompletedWanderCount.Value;
                stats.GroundRests += state.CompletedGroundRestCount.Value;
                stats.Hobbies += state.CompletedHobbyCount.Value;
            }
            return stats;
        }

        private struct CohortStats
        {
            internal bool AllAlive;
            internal float Health, Thirst, Entertainment, Mood, Fatigue, Stress;
            internal int Drinks, ToiletUses, Daydreams, Wanders, GroundRests, Hobbies;
        }

        private long FindFirstFacilityFaultTick()
        {
            long first = -1L;
            foreach (FacilityConditionCycle condition in _facilityConditions.Values)
            {
                if (condition.ActiveFault == FacilityFaultKind.None ||
                    condition.FaultTriggeredSimulationTick < 0L)
                    continue;
                if (first < 0L || condition.FaultTriggeredSimulationTick < first)
                    first = condition.FaultTriggeredSimulationTick;
            }
            return first;
        }

        private long ComputeObservableActionSignature()
        {
            long hash = 17L;
            // 驾驶时居民留在岗位，车辆行进本身也是业务进展；不能把“人没移动”误判成卡死。
            hash = Fold(hash, _model.JourneyPositionMicrometers.Value);
            hash = Fold(hash, _model.JourneyFuelPicoliters.Value);
            hash = Fold(hash, (int)_model.JourneyDestination.Value);
            hash = Fold(hash, (int)_model.JourneyStatus.Value);
            hash = Fold(hash, _model.StopWaterMilliliters.Value);
            hash = Fold(hash, _model.StopWaterRequested.Value ? 1 : 0);
            hash = Fold(hash, _model.StopWasteMilliliters.Value);
            hash = Fold(hash, _stopWater.Capacity);
            hash = Fold(hash, _stopWaste.Capacity);
            hash = Fold(hash, _model.StopWasteRequested.Value ? 1 : 0);
            hash = Fold(hash, _model.StopSpareRequested.Value ? 1 : 0);
            hash = Fold(hash, StableStringHash(_model.CarriedWasteBucketFacilityId.Value));
            hash = Fold(hash, StableStringHash(_waterCanCarrierId));
            hash = Fold(hash, StableStringHash(_model.WasteBucketCarrierId.Value));
            foreach (var resident in _residents)
            {
                hash = Fold(hash, StableStringHash(resident.StableId));
                hash = Fold(hash, (long)resident.OwnerId);
                hash = Fold(hash, (int)resident.Phase);
                hash = Fold(hash, BitConverter.SingleToInt32Bits(
                    resident.State.ResidentLocalPosition.Value.x));
                hash = Fold(hash, BitConverter.SingleToInt32Bits(
                    resident.State.ResidentLocalPosition.Value.z));
                hash = Fold(hash, BitConverter.SingleToInt32Bits(resident.State.ActionProgress.Value));
                hash = Fold(hash, BitConverter.SingleToInt32Bits(resident.State.RemainingPathMeters.Value));
                hash = Fold(hash, resident.State.CompletedDrinkCount.Value);
                hash = Fold(hash, resident.State.CompletedToiletUseCount.Value);
                hash = Fold(hash, resident.State.CompletedLeisureCount.Value);
                hash = Fold(hash, resident.State.CompletedGroundRestCount.Value);
                hash = Fold(hash, resident.State.CompletedHobbyCount.Value);
                hash = Fold(hash, resident.State.CompletedWaterTankRepairCount.Value);
                hash = Fold(hash, resident.State.ResidentCarriedWorldItem.Value.Active ? 1 : 0);
                hash = Fold(hash, StableStringHash(resident.State.CurrentTask.Value));
            }
            hash = Fold(hash, (int)_model.CurrentWeather.Value);
            hash = Fold(hash, _model.SandstormIntensityPermille.Value);
            hash = Fold(hash, (int)_model.WaterCanLocation.Value);
            return hash;
        }

        private long ComputeTrajectorySampleHash()
        {
            long hash = ComputeObservableActionSignature();
            hash = Fold(hash, _simulationClock.SimulationTick);
            foreach (var resident in _residents)
            {
                hash = Fold(hash, BitConverter.SingleToInt32Bits(resident.State.ResidentHealth.Value));
                hash = Fold(hash, BitConverter.SingleToInt32Bits(resident.State.ResidentThirst.Value));
                hash = Fold(hash, BitConverter.SingleToInt32Bits(
                    resident.State.ResidentEntertainment.Value));
                hash = Fold(hash, BitConverter.SingleToInt32Bits(resident.State.ResidentMood.Value));
                hash = Fold(hash, BitConverter.SingleToInt32Bits(resident.State.ResidentFatigue.Value));
                hash = Fold(hash, BitConverter.SingleToInt32Bits(resident.State.ResidentStress.Value));
                hash = Fold(hash, resident.State.BodyWaterMilliliters.Value);
                hash = Fold(hash, resident.State.BladderWasteMilliliters.Value);
                hash = Fold(hash, StableStringHash(resident.State.ResidentCarriedWorldItem.Value.ItemId));
            }
            hash = Fold(hash, _model.VehicleWaterMilliliters.Value);
            hash = Fold(hash, _model.WaterCanWaterMilliliters.Value);
            hash = Fold(hash, _model.DrinkingStationWaterMilliliters.Value);
            hash = Fold(hash, _model.ToiletHoldingWasteMilliliters.Value);
            // 投影按稳定库存 id 排序；总量相同但分布在不同厕所的世界应得到不同轨迹诊断。
            foreach (FoundationFacilityInventoryState inventory in _facilityInventoryProjection)
            {
                hash = Fold(hash, StableStringHash(inventory.InventoryId));
                hash = Fold(hash, StableStringHash(inventory.ResourceId));
                hash = Fold(hash, (int)inventory.Measure);
                hash = Fold(hash, inventory.Capacity);
                hash = Fold(hash, inventory.Amount);
            }
            hash = Fold(hash, FindFirstFacilityFaultTick());
            // 件数相同不代表相同物资；有限补给必须留下来源、稳定身份与精确落位证据。
            foreach (var item in _worldItemPlacementLedger.CreateCheckpointSnapshot())
            {
                hash = Fold(hash, StableStringHash(item.ItemId));
                hash = Fold(hash, StableStringHash(item.Footprint.DefinitionId));
                hash = Fold(hash, StableStringHash(item.Region.RegionId));
                hash = Fold(hash, item.LocalPose.LocalXMillimeters);
                hash = Fold(hash, item.LocalPose.LocalZMillimeters);
                hash = Fold(hash, item.LocalPose.LocalYawDeciDegrees);
            }
            return hash;
        }

        private static long Fold(long hash, long value)
        {
            unchecked
            {
                return (hash ^ value) * 1099511628211L;
            }
        }

        private static long StableStringHash(string value)
        {
            long hash = 1469598103934665603L;
            if (string.IsNullOrEmpty(value)) return hash;
            unchecked
            {
                for (var i = 0; i < value.Length; i++)
                    hash = (hash ^ value[i]) * 1099511628211L;
            }
            return hash;
        }

        private sealed class FoundationSoakAccumulator
        {
            private readonly long _requestedDurationMilliseconds;
            private readonly int _stepMilliseconds;
            private readonly long _startTick;
            private readonly long _initialWater;
            private readonly int _initialDrinks;
            private readonly int _initialToiletUses;
            private readonly int _initialDaydreams;
            private readonly int _initialWanders;
            private readonly int _initialGroundRests;
            private readonly int _initialHobbies;
            private long _previousActionSignature;
            private long _currentObservableStall;
            private long _longestObservableStall;
            private long _maximumWaterDeviation;
            private long _firstFaultTick;
            private long _trajectoryChecksum = 17L;
            private int _stepCount;
            private float _minimumHealth;
            private float _maximumThirst;
            private float _minimumEntertainment;
            private float _minimumMood;
            private float _maximumFatigue;
            private float _maximumStress;

            public FoundationSoakAccumulator(
                NomadFoundationSystem system,
                long requestedDurationMilliseconds,
                int stepMilliseconds)
            {
                CohortStats cohort = system.CaptureCohortStats();
                _requestedDurationMilliseconds = requestedDurationMilliseconds;
                _stepMilliseconds = stepMilliseconds;
                _startTick = system._simulationClock.SimulationTick;
                _initialWater = system.CalculateConservedWaterMilliliters();
                _initialDrinks = cohort.Drinks;
                _initialToiletUses = cohort.ToiletUses;
                _initialDaydreams = cohort.Daydreams;
                _initialWanders = cohort.Wanders;
                _initialGroundRests = cohort.GroundRests;
                _initialHobbies = cohort.Hobbies;
                _previousActionSignature = system.ComputeObservableActionSignature();
                _firstFaultTick = system.FindFirstFacilityFaultTick();
                _minimumHealth = cohort.Health;
                _maximumThirst = cohort.Thirst;
                _minimumEntertainment = cohort.Entertainment;
                _minimumMood = cohort.Mood;
                _maximumFatigue = cohort.Fatigue;
                _maximumStress = cohort.Stress;
            }

            public long CurrentObservableStallMilliseconds => _currentObservableStall;

            public void Observe(NomadFoundationSystem system, int deltaMilliseconds)
            {
                CohortStats cohort = system.CaptureCohortStats();
                _stepCount++;
                _minimumHealth = Math.Min(_minimumHealth, cohort.Health);
                _maximumThirst = Math.Max(_maximumThirst, cohort.Thirst);
                _minimumEntertainment = Math.Min(
                    _minimumEntertainment,
                    cohort.Entertainment);
                _minimumMood = Math.Min(_minimumMood, cohort.Mood);
                _maximumFatigue = Math.Max(_maximumFatigue, cohort.Fatigue);
                _maximumStress = Math.Max(_maximumStress, cohort.Stress);

                long water = system.CalculateConservedWaterMilliliters();
                long deviation = Math.Abs(water - _initialWater);
                _maximumWaterDeviation = Math.Max(_maximumWaterDeviation, deviation);
                long faultTick = system.FindFirstFacilityFaultTick();
                if (_firstFaultTick < 0L && faultTick >= 0L)
                    _firstFaultTick = faultTick;

                long signature = system.ComputeObservableActionSignature();
                if (signature == _previousActionSignature)
                {
                    _currentObservableStall += deltaMilliseconds;
                    _longestObservableStall = Math.Max(
                        _longestObservableStall,
                        _currentObservableStall);
                }
                else
                {
                    _previousActionSignature = signature;
                    _currentObservableStall = 0L;
                }

                _trajectoryChecksum = Fold(
                    _trajectoryChecksum,
                    system.ComputeTrajectorySampleHash());
            }

            public FoundationSoakRunResult Complete(
                NomadFoundationSystem system,
                FoundationSoakStopReason stopReason)
            {
                CohortStats cohort = system.CaptureCohortStats();
                return new FoundationSoakRunResult(
                    stopReason,
                    system.worldSeed,
                    _requestedDurationMilliseconds,
                    _stepMilliseconds,
                    _stepCount,
                    _startTick,
                    system._simulationClock.SimulationTick,
                    _firstFaultTick,
                    cohort.AllAlive,
                    _minimumHealth,
                    _maximumThirst,
                    _minimumEntertainment,
                    _minimumMood,
                    _maximumFatigue,
                    _maximumStress,
                    _initialWater,
                    system.CalculateConservedWaterMilliliters(),
                    _maximumWaterDeviation,
                    cohort.Drinks - _initialDrinks,
                    cohort.ToiletUses - _initialToiletUses,
                    cohort.Daydreams - _initialDaydreams,
                    cohort.Wanders - _initialWanders,
                    cohort.GroundRests - _initialGroundRests,
                    cohort.Hobbies - _initialHobbies,
                    _longestObservableStall,
                    _trajectoryChecksum,
                    system._resident.State.CurrentTask.Value,
                    system._resident.State.LastBlocker.Value);
            }
        }
    }
}
