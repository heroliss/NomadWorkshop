using System;

namespace Game.NomadWorkshop.Simulation
{
    public enum NomadWeatherKind
    {
        Clear = 0,
        Sandstorm = 1,
    }

    /// <summary>
    /// 统一模拟 Tick 上的天气投影。它不拥有第二只时钟，也不保存“当前天气”布尔值；
    /// 世界种子、生活日序号与稳定随机槽足以在读档和不同帧步长下重建同一事件。
    /// </summary>
    public readonly struct NomadEnvironmentSnapshot
    {
        public NomadEnvironmentSnapshot(
            NomadWeatherKind weather,
            int intensityPermille,
            long sandstormStartSimulationTick,
            long sandstormEndSimulationTick,
            long nextTransitionSimulationTick)
        {
            if (!Enum.IsDefined(typeof(NomadWeatherKind), weather))
                throw new ArgumentOutOfRangeException(nameof(weather));
            if (intensityPermille < 0 || intensityPermille > 1000)
                throw new ArgumentOutOfRangeException(nameof(intensityPermille));
            if (sandstormStartSimulationTick < 0L ||
                sandstormEndSimulationTick <= sandstormStartSimulationTick)
                throw new ArgumentOutOfRangeException(nameof(sandstormStartSimulationTick));
            if (nextTransitionSimulationTick <= 0L)
                throw new ArgumentOutOfRangeException(nameof(nextTransitionSimulationTick));

            Weather = weather;
            IntensityPermille = intensityPermille;
            SandstormStartSimulationTick = sandstormStartSimulationTick;
            SandstormEndSimulationTick = sandstormEndSimulationTick;
            NextTransitionSimulationTick = nextTransitionSimulationTick;
        }

        public NomadWeatherKind Weather { get; }
        public int IntensityPermille { get; }
        public long SandstormStartSimulationTick { get; }
        public long SandstormEndSimulationTick { get; }
        public long NextTransitionSimulationTick { get; }
        public bool IsSandstorm => Weather == NomadWeatherKind.Sandstorm;
    }

    /// <summary>
    /// 首版确定性天气表：每个十分钟生活日内有一段 90 秒沙尘暴，开始时刻与强度由
    /// 世界种子和生活日独立采样。以后可替换为区域气候数据，但设施结算只依赖分段暴露契约。
    /// </summary>
    public readonly struct NomadEnvironmentSchedule
    {
        private const string StartRandomStreamId = "environment:sandstorm-start";
        private const string IntensityRandomStreamId = "environment:sandstorm-intensity";
        private static readonly ulong EnvironmentOwnerId =
            DeterministicRandom.HashStableString("nomad-workshop:global-environment");

        public NomadEnvironmentSchedule(
            long lifeDayDurationMilliseconds,
            long sandstormDurationMilliseconds,
            long earliestStartMilliseconds,
            long latestStartMilliseconds)
        {
            if (lifeDayDurationMilliseconds <= 0L)
                throw new ArgumentOutOfRangeException(nameof(lifeDayDurationMilliseconds));
            if (sandstormDurationMilliseconds <= 0L ||
                sandstormDurationMilliseconds >= lifeDayDurationMilliseconds)
                throw new ArgumentOutOfRangeException(nameof(sandstormDurationMilliseconds));
            if (earliestStartMilliseconds < 0L ||
                latestStartMilliseconds < earliestStartMilliseconds ||
                latestStartMilliseconds + sandstormDurationMilliseconds >=
                lifeDayDurationMilliseconds)
                throw new ArgumentOutOfRangeException(nameof(latestStartMilliseconds));

            LifeDayDurationMilliseconds = lifeDayDurationMilliseconds;
            SandstormDurationMilliseconds = sandstormDurationMilliseconds;
            EarliestStartMilliseconds = earliestStartMilliseconds;
            LatestStartMilliseconds = latestStartMilliseconds;
        }

        public long LifeDayDurationMilliseconds { get; }
        public long SandstormDurationMilliseconds { get; }
        public long EarliestStartMilliseconds { get; }
        public long LatestStartMilliseconds { get; }

        public static NomadEnvironmentSchedule Default => new(
            NomadCalendarPolicy.DefaultLifeDayDurationMilliseconds,
            sandstormDurationMilliseconds: 90_000L,
            earliestStartMilliseconds: 120_000L,
            latestStartMilliseconds: 390_000L);

        public NomadEnvironmentSnapshot Project(int worldSeed, long simulationTick)
        {
            if (simulationTick < 0L)
                throw new ArgumentOutOfRangeException(nameof(simulationTick));

            long lifeDayIndex = simulationTick / LifeDayDurationMilliseconds;
            ResolveEvent(
                worldSeed,
                lifeDayIndex,
                out long startTick,
                out long endTick,
                out int intensityPermille);
            if (simulationTick < startTick)
            {
                return new NomadEnvironmentSnapshot(
                    NomadWeatherKind.Clear,
                    0,
                    startTick,
                    endTick,
                    startTick);
            }
            if (simulationTick < endTick)
            {
                return new NomadEnvironmentSnapshot(
                    NomadWeatherKind.Sandstorm,
                    intensityPermille,
                    startTick,
                    endTick,
                    endTick);
            }

            ResolveEvent(
                worldSeed,
                checked(lifeDayIndex + 1L),
                out long nextStartTick,
                out long nextEndTick,
                out _);
            return new NomadEnvironmentSnapshot(
                NomadWeatherKind.Clear,
                0,
                nextStartTick,
                nextEndTick,
                nextStartTick);
        }

        private void ResolveEvent(
            int worldSeed,
            long lifeDayIndex,
            out long startTick,
            out long endTick,
            out int intensityPermille)
        {
            long startRange = LatestStartMilliseconds - EarliestStartMilliseconds;
            double startSample = DeterministicRandom.Sample01(
                worldSeed,
                EnvironmentOwnerId,
                StartRandomStreamId,
                lifeDayIndex);
            long startOffset = EarliestStartMilliseconds +
                               (long)Math.Floor(startSample * (startRange + 1L));
            long lifeDayStart = checked(lifeDayIndex * LifeDayDurationMilliseconds);
            startTick = checked(lifeDayStart + startOffset);
            endTick = checked(startTick + SandstormDurationMilliseconds);

            double intensitySample = DeterministicRandom.Sample01(
                worldSeed,
                EnvironmentOwnerId,
                IntensityRandomStreamId,
                lifeDayIndex);
            intensityPermille = 600 + (int)Math.Floor(intensitySample * 401d);
            if (intensityPermille > 1000) intensityPermille = 1000;
        }
    }
}
