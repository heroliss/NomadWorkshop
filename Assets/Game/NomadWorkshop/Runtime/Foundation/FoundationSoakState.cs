using System;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>长时模拟 Harness 的明确停止原因；成功、业务终态与输入拒绝不会混成一个 bool。</summary>
    public enum FoundationSoakStopReason
    {
        None,
        DurationReached,
        ResidentDied,
        ObservableProgressStalled,
        NotReady,
        RequiresPause,
        BuildTransactionActive,
        InvalidRequest,
        StepBudgetExceeded,
        CheckpointOperationActive,
    }

    /// <summary>
    /// 一次 Foundation 长时跑数的结构化摘要。它只记录可比较的业务证据，不宣称一组阈值
    /// 已经证明玩法平衡或好玩；跨 Seed 分布和真实试玩仍需要单独判断。
    /// </summary>
    public readonly struct FoundationSoakRunResult
    {
        public const string StableExperimentId = "nomad-foundation-soak";
        public const string HarnessVersion = "0.10.0";
        /// <summary>本报告只来自同步角点快进，不代表 PlayerLoop 的原生避让已验证。</summary>
        public string MovementMode => "deterministic-corners";

        internal FoundationSoakRunResult(
            FoundationSoakStopReason stopReason,
            int worldSeed,
            long requestedDurationMilliseconds,
            int stepMilliseconds,
            int stepCount,
            long startSimulationTick,
            long endSimulationTick,
            long firstFacilityFaultSimulationTick,
            bool residentAlive,
            float minimumHealth,
            float maximumThirst,
            float minimumEntertainment,
            float minimumMood,
            float maximumFatigue,
            float maximumStress,
            long initialWaterTotalMilliliters,
            long finalWaterTotalMilliliters,
            long maximumAbsoluteWaterDeviationMilliliters,
            int completedDrinkDelta,
            int completedToiletUseDelta,
            int completedDaydreamDelta,
            int completedWanderDelta,
            int completedGroundRestDelta,
            int completedHobbyDelta,
            long longestObservableStallMilliseconds,
            long trajectoryChecksum,
            string finalTask,
            string finalBlocker)
        {
            StopReason = stopReason;
            WorldSeed = worldSeed;
            RequestedDurationMilliseconds = requestedDurationMilliseconds;
            StepMilliseconds = stepMilliseconds;
            StepCount = stepCount;
            StartSimulationTick = startSimulationTick;
            EndSimulationTick = endSimulationTick;
            FirstFacilityFaultSimulationTick = firstFacilityFaultSimulationTick;
            ResidentAlive = residentAlive;
            MinimumHealth = minimumHealth;
            MaximumThirst = maximumThirst;
            MinimumEntertainment = minimumEntertainment;
            MinimumMood = minimumMood;
            MaximumFatigue = maximumFatigue;
            MaximumStress = maximumStress;
            InitialWaterTotalMilliliters = initialWaterTotalMilliliters;
            FinalWaterTotalMilliliters = finalWaterTotalMilliliters;
            MaximumAbsoluteWaterDeviationMilliliters =
                maximumAbsoluteWaterDeviationMilliliters;
            CompletedDrinkDelta = completedDrinkDelta;
            CompletedToiletUseDelta = completedToiletUseDelta;
            CompletedDaydreamDelta = completedDaydreamDelta;
            CompletedWanderDelta = completedWanderDelta;
            CompletedGroundRestDelta = completedGroundRestDelta;
            CompletedHobbyDelta = completedHobbyDelta;
            LongestObservableStallMilliseconds = longestObservableStallMilliseconds;
            TrajectoryChecksum = trajectoryChecksum;
            FinalTask = finalTask ?? string.Empty;
            FinalBlocker = finalBlocker ?? string.Empty;
        }

        public FoundationSoakStopReason StopReason { get; }
        public int WorldSeed { get; }
        public long RequestedDurationMilliseconds { get; }
        /// <summary>输入分块大小；用于检验不同调用节奏，不改变内部固定业务步。</summary>
        public int StepMilliseconds { get; }
        /// <summary>实际执行且审计的业务步数；终态发生后不会继续补满当前输入分块。</summary>
        public int StepCount { get; }
        public long StartSimulationTick { get; }
        public long EndSimulationTick { get; }
        public long ElapsedSimulationMilliseconds => EndSimulationTick - StartSimulationTick;
        public long FirstFacilityFaultSimulationTick { get; }
        public bool ResidentAlive { get; }
        public float MinimumHealth { get; }
        public float MaximumThirst { get; }
        public float MinimumEntertainment { get; }
        public float MinimumMood { get; }
        public float MaximumFatigue { get; }
        public float MaximumStress { get; }
        public long InitialWaterTotalMilliliters { get; }
        public long FinalWaterTotalMilliliters { get; }
        public long MaximumAbsoluteWaterDeviationMilliliters { get; }
        public int CompletedDrinkDelta { get; }
        public int CompletedToiletUseDelta { get; }
        public int CompletedDaydreamDelta { get; }
        public int CompletedWanderDelta { get; }
        public int CompletedGroundRestDelta { get; }
        public int CompletedHobbyDelta { get; }
        public long LongestObservableStallMilliseconds { get; }
        public long TrajectoryChecksum { get; }
        public string FinalTask { get; }
        public string FinalBlocker { get; }

        public bool Accepted => StopReason is FoundationSoakStopReason.DurationReached or
            FoundationSoakStopReason.ResidentDied or
            FoundationSoakStopReason.ObservableProgressStalled;
        public bool CompletedRequestedDuration =>
            StopReason == FoundationSoakStopReason.DurationReached;
        public bool PreservedWater => MaximumAbsoluteWaterDeviationMilliliters == 0L;

        public override string ToString()
        {
            string fault = FirstFacilityFaultSimulationTick < 0L
                ? "无故障"
                : $"首故障@{FirstFacilityFaultSimulationTick}ms";
            return $"{StableExperimentId}@{HarnessVersion} · move={MovementMode} · seed={WorldSeed} · " +
                   $"{StopReason} · {ElapsedSimulationMilliseconds}/{RequestedDurationMilliseconds}ms · " +
                   $"input={StepMilliseconds}ms · steps={StepCount} · alive={ResidentAlive} · " +
                   $"waterΔmax={MaximumAbsoluteWaterDeviationMilliliters}mL · {fault} · " +
                   $"drink={CompletedDrinkDelta}, toilet={CompletedToiletUseDelta}, " +
                   $"daydream={CompletedDaydreamDelta}, wander={CompletedWanderDelta}, " +
                   $"ground-rest={CompletedGroundRestDelta}, hobby={CompletedHobbyDelta} · " +
                   $"stallMax={LongestObservableStallMilliseconds}ms · " +
                   $"checksum={TrajectoryChecksum}";
        }
    }
}
