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
                observableStallLimitMilliseconds,
                out int requestedStepCount);
            if (rejection != FoundationSoakStopReason.None)
                return CreateRejectedSoakResult(
                    rejection,
                    durationMilliseconds,
                    stepMilliseconds);

            var accumulator = new FoundationSoakAccumulator(
                this,
                durationMilliseconds,
                stepMilliseconds);
            long remaining = durationMilliseconds;
            FoundationSoakStopReason stopReason = FoundationSoakStopReason.DurationReached;
            for (var index = 0; index < requestedStepCount && remaining > 0L; index++)
            {
                int deltaMilliseconds = (int)Math.Min(stepMilliseconds, remaining);
                _simulationClock.AdvanceMilliseconds(deltaMilliseconds);
                AdvanceSimulation(deltaMilliseconds);
                remaining -= deltaMilliseconds;
                accumulator.Observe(this, deltaMilliseconds);

                if (!_residentWellbeing.IsAlive)
                {
                    stopReason = FoundationSoakStopReason.ResidentDied;
                    break;
                }
                if (observableStallLimitMilliseconds > 0L &&
                    accumulator.CurrentObservableStallMilliseconds >=
                    observableStallLimitMilliseconds)
                {
                    stopReason = FoundationSoakStopReason.ObservableProgressStalled;
                    break;
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
                _model.LastBlocker.Value =
                    "HarnessRequiresPause · 切换 Seed 会重建当前世界，请先暂停";
                return false;
            }
            if (_model.BuildTransactionPhase.Value != FoundationBuildTransactionPhase.Idle)
            {
                _model.LastBlocker.Value =
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
            long observableStallLimitMilliseconds,
            out int requestedStepCount)
        {
            requestedStepCount = 0;
            if (!_initialized || _model == null || _residentWaterCycle == null)
                return FoundationSoakStopReason.NotReady;
            if (!_model.IsPaused.Value)
                return FoundationSoakStopReason.RequiresPause;
            if (_model.BuildTransactionPhase.Value != FoundationBuildTransactionPhase.Idle)
                return FoundationSoakStopReason.BuildTransactionActive;
            if (durationMilliseconds <= 0L ||
                stepMilliseconds < MinimumSoakStepMilliseconds ||
                stepMilliseconds > MaximumSoakStepMilliseconds ||
                observableStallLimitMilliseconds < 0L ||
                durationMilliseconds > long.MaxValue - _simulationClock.SimulationTick)
                return FoundationSoakStopReason.InvalidRequest;

            long stepCount = (durationMilliseconds - 1L) / stepMilliseconds + 1L;
            if (stepCount > MaximumSoakStepCount)
                return FoundationSoakStopReason.StepBudgetExceeded;
            requestedStepCount = (int)stepCount;
            return FoundationSoakStopReason.None;
        }

        private FoundationSoakRunResult CreateRejectedSoakResult(
            FoundationSoakStopReason reason,
            long durationMilliseconds,
            int stepMilliseconds)
        {
            long tick = _simulationClock?.SimulationTick ?? 0L;
            bool alive = _residentWellbeing?.IsAlive ?? false;
            long water = _model == null ? 0L : CalculateConservedWaterMilliliters();
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
                _model?.ResidentHealth.Value ?? 0f,
                _model?.ResidentThirst.Value ?? 0f,
                _model?.ResidentEntertainment.Value ?? 0f,
                _model?.ResidentMood.Value ?? 0f,
                _model?.ResidentFatigue.Value ?? 0f,
                _model?.ResidentStress.Value ?? 0f,
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
                _model?.CurrentTask.Value,
                _model?.LastBlocker.Value);
        }

        private long CalculateConservedWaterMilliliters()
        {
            if (_model == null) return 0L;
            return (long)_model.VehicleWaterMilliliters.Value +
                   _model.WaterCanWaterMilliliters.Value +
                   _model.DrinkingStationWaterMilliliters.Value +
                   _model.BodyWaterMilliliters.Value +
                   _model.BladderWasteMilliliters.Value +
                   _model.ToiletHoldingWasteMilliliters.Value;
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
            hash = Fold(hash, (int)_residentPhase);
            hash = Fold(hash, BitConverter.SingleToInt32Bits(
                _model.ResidentLocalPosition.Value.x));
            hash = Fold(hash, BitConverter.SingleToInt32Bits(
                _model.ResidentLocalPosition.Value.z));
            hash = Fold(hash, BitConverter.SingleToInt32Bits(_model.ActionProgress.Value));
            hash = Fold(hash, BitConverter.SingleToInt32Bits(_model.RemainingPathMeters.Value));
            hash = Fold(hash, _model.CompletedDrinkCount.Value);
            hash = Fold(hash, _model.CompletedToiletUseCount.Value);
            hash = Fold(hash, _model.CompletedLeisureCount.Value);
            hash = Fold(hash, _model.CompletedGroundRestCount.Value);
            hash = Fold(hash, _model.CompletedHobbyCount.Value);
            hash = Fold(hash, _model.CompletedWaterTankRepairCount.Value);
            hash = Fold(hash, (int)_model.CurrentWeather.Value);
            hash = Fold(hash, _model.SandstormIntensityPermille.Value);
            hash = Fold(hash, (int)_model.WaterCanLocation.Value);
            hash = Fold(hash, _model.ResidentCarriedWorldItem.Value.Active ? 1 : 0);
            return Fold(hash, StableStringHash(_model.CurrentTask.Value));
        }

        private long ComputeTrajectorySampleHash()
        {
            long hash = ComputeObservableActionSignature();
            hash = Fold(hash, _simulationClock.SimulationTick);
            hash = Fold(hash, BitConverter.SingleToInt32Bits(_model.ResidentHealth.Value));
            hash = Fold(hash, BitConverter.SingleToInt32Bits(_model.ResidentThirst.Value));
            hash = Fold(hash, BitConverter.SingleToInt32Bits(
                _model.ResidentEntertainment.Value));
            hash = Fold(hash, BitConverter.SingleToInt32Bits(_model.ResidentMood.Value));
            hash = Fold(hash, BitConverter.SingleToInt32Bits(_model.ResidentFatigue.Value));
            hash = Fold(hash, BitConverter.SingleToInt32Bits(_model.ResidentStress.Value));
            hash = Fold(hash, _model.VehicleWaterMilliliters.Value);
            hash = Fold(hash, _model.WaterCanWaterMilliliters.Value);
            hash = Fold(hash, _model.DrinkingStationWaterMilliliters.Value);
            hash = Fold(hash, _model.BodyWaterMilliliters.Value);
            hash = Fold(hash, _model.BladderWasteMilliliters.Value);
            hash = Fold(hash, _model.ToiletHoldingWasteMilliliters.Value);
            hash = Fold(hash, FindFirstFacilityFaultTick());
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
                _requestedDurationMilliseconds = requestedDurationMilliseconds;
                _stepMilliseconds = stepMilliseconds;
                _startTick = system._simulationClock.SimulationTick;
                _initialWater = system.CalculateConservedWaterMilliliters();
                _initialDrinks = system._model.CompletedDrinkCount.Value;
                _initialToiletUses = system._model.CompletedToiletUseCount.Value;
                _initialDaydreams = system._model.CompletedDaydreamCount.Value;
                _initialWanders = system._model.CompletedWanderCount.Value;
                _initialGroundRests = system._model.CompletedGroundRestCount.Value;
                _initialHobbies = system._model.CompletedHobbyCount.Value;
                _previousActionSignature = system.ComputeObservableActionSignature();
                _firstFaultTick = system.FindFirstFacilityFaultTick();
                _minimumHealth = system._model.ResidentHealth.Value;
                _maximumThirst = system._model.ResidentThirst.Value;
                _minimumEntertainment = system._model.ResidentEntertainment.Value;
                _minimumMood = system._model.ResidentMood.Value;
                _maximumFatigue = system._model.ResidentFatigue.Value;
                _maximumStress = system._model.ResidentStress.Value;
            }

            public long CurrentObservableStallMilliseconds => _currentObservableStall;

            public void Observe(NomadFoundationSystem system, int deltaMilliseconds)
            {
                _stepCount++;
                _minimumHealth = Math.Min(_minimumHealth, system._model.ResidentHealth.Value);
                _maximumThirst = Math.Max(_maximumThirst, system._model.ResidentThirst.Value);
                _minimumEntertainment = Math.Min(
                    _minimumEntertainment,
                    system._model.ResidentEntertainment.Value);
                _minimumMood = Math.Min(_minimumMood, system._model.ResidentMood.Value);
                _maximumFatigue = Math.Max(_maximumFatigue, system._model.ResidentFatigue.Value);
                _maximumStress = Math.Max(_maximumStress, system._model.ResidentStress.Value);

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
                return new FoundationSoakRunResult(
                    stopReason,
                    system.worldSeed,
                    _requestedDurationMilliseconds,
                    _stepMilliseconds,
                    _stepCount,
                    _startTick,
                    system._simulationClock.SimulationTick,
                    _firstFaultTick,
                    system._residentWellbeing.IsAlive,
                    _minimumHealth,
                    _maximumThirst,
                    _minimumEntertainment,
                    _minimumMood,
                    _maximumFatigue,
                    _maximumStress,
                    _initialWater,
                    system.CalculateConservedWaterMilliliters(),
                    _maximumWaterDeviation,
                    system._model.CompletedDrinkCount.Value - _initialDrinks,
                    system._model.CompletedToiletUseCount.Value - _initialToiletUses,
                    system._model.CompletedDaydreamCount.Value - _initialDaydreams,
                    system._model.CompletedWanderCount.Value - _initialWanders,
                    system._model.CompletedGroundRestCount.Value - _initialGroundRests,
                    system._model.CompletedHobbyCount.Value - _initialHobbies,
                    _longestObservableStall,
                    _trajectoryChecksum,
                    system._model.CurrentTask.Value,
                    system._model.LastBlocker.Value);
            }
        }
    }
}
