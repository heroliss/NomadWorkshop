using System;

namespace Game.NomadWorkshop.Simulation
{
    /// <summary>
    /// 《游牧工坊》生活日与压缩气候年的时间投影参数。参数只解释统一模拟毫秒，
    /// 不拥有暂停、加速或 Unity 帧循环；因此需求、旅途和季节不会各自维护会漂移的时钟。
    /// </summary>
    public readonly struct NomadCalendarPolicy
    {
        /// <summary>`1x` 下十分钟完成一个生活日的首轮原型值。</summary>
        public const long DefaultLifeDayDurationMilliseconds = 10L * 60L * 1000L;

        /// <summary>每季包含十二个气候周；一个生活日恰好推进一个气候周。</summary>
        public const int DefaultClimateWeeksPerSeason = 12;

        /// <summary>当前气候年使用四个连续季节相位。</summary>
        public const int DefaultSeasonCount = 4;

        public NomadCalendarPolicy(
            long lifeDayDurationMilliseconds,
            int climateWeeksPerSeason,
            int seasonCount)
        {
            if (lifeDayDurationMilliseconds <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(lifeDayDurationMilliseconds),
                    "生活日墙钟时长必须大于零毫秒。");
            if (climateWeeksPerSeason <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(climateWeeksPerSeason),
                    "每季气候周数必须大于零。");
            if (seasonCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(seasonCount), "季节数量必须大于零。");

            LifeDayDurationMilliseconds = lifeDayDurationMilliseconds;
            ClimateWeeksPerSeason = climateWeeksPerSeason;
            SeasonCount = seasonCount;
            ClimateWeeksPerYear = checked(climateWeeksPerSeason * seasonCount);
        }

        /// <summary>一个 24 小时生活日在统一模拟时钟上占用的毫秒数。</summary>
        public long LifeDayDurationMilliseconds { get; }

        /// <summary>一个季节包含的气候周数；当前约定一个生活日推进一个气候周。</summary>
        public int ClimateWeeksPerSeason { get; }

        /// <summary>一个完整气候年包含的季节数量。</summary>
        public int SeasonCount { get; }

        /// <summary>一个完整气候年包含的气候周数。</summary>
        public int ClimateWeeksPerYear { get; }

        /// <summary>返回当前讨论确定的十分钟生活日、十二周一季、四季一年的原型参数。</summary>
        public static NomadCalendarPolicy Default => new(
            DefaultLifeDayDurationMilliseconds,
            DefaultClimateWeeksPerSeason,
            DefaultSeasonCount);

        /// <summary>
        /// 从已保存的统一模拟毫秒投影生活日与气候相位。季节不拥有第二份累计时间；
        /// 改变气候表现也不会反向改变居民已经经历的饮食、睡眠或行动时长。
        /// </summary>
        public NomadCalendarSnapshot Project(long simulationMilliseconds)
        {
            if (simulationMilliseconds < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(simulationMilliseconds),
                    "统一模拟毫秒不能为负数。");

            long completedLifeDays = simulationMilliseconds / LifeDayDurationMilliseconds;
            long millisecondsIntoLifeDay = simulationMilliseconds % LifeDayDurationMilliseconds;
            int lifeDayProgressPermille = RatioPermille(
                millisecondsIntoLifeDay,
                LifeDayDurationMilliseconds);
            int lifeMinuteOfDay = RatioFloor(
                millisecondsIntoLifeDay,
                LifeDayDurationMilliseconds,
                24 * 60);

            // 当前设计中一个生活日推进一个气候周，昼夜内部以连续小数周插值。
            long completedClimateWeeks = completedLifeDays;
            long climateYearIndex = completedClimateWeeks / ClimateWeeksPerYear;
            int weekInYear = (int)(completedClimateWeeks % ClimateWeeksPerYear);
            int seasonIndex = weekInYear / ClimateWeeksPerSeason;
            int climateWeekInSeason = weekInYear % ClimateWeeksPerSeason + 1;
            int seasonProgressPermille = PhaseProgressPermille(
                weekInYear % ClimateWeeksPerSeason,
                millisecondsIntoLifeDay,
                ClimateWeeksPerSeason);
            int climateYearProgressPermille = PhaseProgressPermille(
                weekInYear,
                millisecondsIntoLifeDay,
                ClimateWeeksPerYear);

            return new NomadCalendarSnapshot(
                checked(completedLifeDays + 1),
                lifeMinuteOfDay,
                lifeDayProgressPermille,
                checked(climateYearIndex + 1),
                seasonIndex,
                climateWeekInSeason,
                seasonProgressPermille,
                climateYearProgressPermille);
        }

        private int PhaseProgressPermille(
            long completedWeeksInPhase,
            long millisecondsIntoLifeDay,
            int weeksInPhase)
        {
            decimal numerator =
                (decimal)completedWeeksInPhase * LifeDayDurationMilliseconds +
                millisecondsIntoLifeDay;
            decimal denominator = (decimal)weeksInPhase * LifeDayDurationMilliseconds;
            return (int)decimal.Floor(numerator * 1000m / denominator);
        }

        private static int RatioPermille(long numerator, long denominator) =>
            (int)decimal.Floor((decimal)numerator * 1000m / denominator);

        private static int RatioFloor(long numerator, long denominator, int scale) =>
            (int)decimal.Floor((decimal)numerator * scale / denominator);
    }

    /// <summary>
    /// 某一统一模拟时刻的只读日历投影。日、年和周均为便于 UI 阅读的一基索引；
    /// <see cref="SeasonIndex"/> 为便于数据表索引的零基相位，不在模拟层硬编码季节名称。
    /// </summary>
    public readonly struct NomadCalendarSnapshot
    {
        internal NomadCalendarSnapshot(
            long lifeDay,
            int lifeMinuteOfDay,
            int lifeDayProgressPermille,
            long climateYear,
            int seasonIndex,
            int climateWeekInSeason,
            int seasonProgressPermille,
            int climateYearProgressPermille)
        {
            LifeDay = lifeDay;
            LifeMinuteOfDay = lifeMinuteOfDay;
            LifeDayProgressPermille = lifeDayProgressPermille;
            ClimateYear = climateYear;
            SeasonIndex = seasonIndex;
            ClimateWeekInSeason = climateWeekInSeason;
            SeasonProgressPermille = seasonProgressPermille;
            ClimateYearProgressPermille = climateYearProgressPermille;
        }

        /// <summary>从 1 开始的居民生活日。</summary>
        public long LifeDay { get; }

        /// <summary>生活日内的分钟，范围为 0–1439。</summary>
        public int LifeMinuteOfDay { get; }

        /// <summary>当前生活日进度，范围为 0–999。</summary>
        public int LifeDayProgressPermille { get; }

        /// <summary>从 1 开始的压缩气候年。</summary>
        public long ClimateYear { get; }

        /// <summary>当前季节的零基相位索引。</summary>
        public int SeasonIndex { get; }

        /// <summary>当前季节内从 1 开始的气候周。</summary>
        public int ClimateWeekInSeason { get; }

        /// <summary>当前季节的连续进度，范围为 0–999。</summary>
        public int SeasonProgressPermille { get; }

        /// <summary>当前气候年的连续进度，范围为 0–999。</summary>
        public int ClimateYearProgressPermille { get; }
    }
}
