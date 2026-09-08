using System;

namespace Game.NomadWorkshop.Simulation
{
    /// <summary>
    /// 居民当前主要活动对身心状态的负荷类型。它只描述模拟语义，不绑定动画、设施或 Unity 状态机。
    /// </summary>
    public enum ResidentWellbeingActivity
    {
        Routine,
        Travel,
        Work,
        PersonalCare,
        GroundRest,
        /// <summary>微型车只有驾驶位时的低质量休息；恢复慢且会产生长期身体代价。</summary>
        SeatRest,
        Daydream,
        Wander,
        Hobby,
    }

    /// <summary>本次连续推进时会额外影响压力的外部生理与执行条件。</summary>
    public readonly struct ResidentWellbeingDrivers
    {
        public ResidentWellbeingDrivers(
            float thirstDeficit,
            float bladderPressure,
            bool isBlocked)
        {
            ValidateNormalized(thirstDeficit, nameof(thirstDeficit));
            ValidateNormalized(bladderPressure, nameof(bladderPressure));
            ThirstDeficit = thirstDeficit;
            BladderPressure = bladderPressure;
            IsBlocked = isBlocked;
        }

        public float ThirstDeficit { get; }
        public float BladderPressure { get; }
        public bool IsBlocked { get; }

        private static void ValidateNormalized(float value, string parameterName)
        {
            if (!float.IsFinite(value) || value < 0f || value > 1f)
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "居民身心驱动值必须位于 [0, 1]。");
        }
    }

    /// <summary>
    /// 纯 C# 的居民身心连续状态。娱乐与心情是“越高越好”的满足值；疲劳和压力是“越高越糟”的负担值。
    /// Utility AI 需要缺口时，只在 <see cref="CreateDecisionNeedSnapshot"/> 边界转换，避免运行时真值正负混用。
    /// </summary>
    public sealed class ResidentWellbeing
    {
        private const string LeisureOutcomeRandomStreamId =
            "resident-wellbeing:leisure-outcome";

        // 这些是 Foundation 首版可观察节奏，不宣称是真实生理常数。后续平衡稳定后再迁入数据配置。
        public const float EntertainmentDecayPerSecond = 0.0007f;
        public const float BaseFatigueGrowthPerSecond = 0.0008f;
        public const float MaximumEntertainmentFatigueBuffer = 0.18f;
        public const float HobbyEntertainmentGainPerSecond = 0.028f;
        public const float DehydrationDamageOnset = 0.82f;
        public const float MaximumDehydrationHealthLossPerSecond = 0.004f;
        public const float GroundRestHealthRecoveryPerSecond = 0.0012f;

        private const float GroundRestFatigueRecoveryPerSecond = 0.014f;
        private const float GroundRestStressRecoveryPerSecond = 0.008f;
        private const float GroundRestMoodLossPerSecond = 0.00045f;
        private const float DaydreamFatigueRecoveryPerSecond = 0.010f;
        private const float WanderFatigueRecoveryPerSecond = 0.004f;
        private const float HobbyFatigueRecoveryPerSecond = 0.003f;
        private const float DaydreamStressRecoveryPerSecond = 0.010f;
        private const float WanderStressRecoveryPerSecond = 0.008f;
        private const float HobbyStressRecoveryPerSecond = 0.007f;
        private const float DaydreamMoodGainPerSecond = 0.0015f;
        private const float WanderMoodGainPerSecond = 0.002f;
        private const float HobbyMoodGainPerSecond = 0.006f;

        public ResidentWellbeing(
            float entertainment,
            float mood,
            float fatigue,
            float stress,
            float health = 1f)
        {
            Entertainment = ValidateNormalized(entertainment, nameof(entertainment));
            Mood = ValidateNormalized(mood, nameof(mood));
            Fatigue = ValidateNormalized(fatigue, nameof(fatigue));
            Stress = ValidateNormalized(stress, nameof(stress));
            Health = ValidateNormalized(health, nameof(health));
        }

        /// <summary>兴趣与有意义刺激的满足程度；发呆和普通闲逛不会补充它。</summary>
        public float Entertainment { get; private set; }

        /// <summary>当前总体情绪基线；低娱乐、持续压力和疲劳会让它缓慢下降。</summary>
        public float Mood { get; private set; }

        /// <summary>身体疲劳负担；高娱乐只能小幅减缓增长，不能替代睡眠或休息。</summary>
        public float Fatigue { get; private set; }

        /// <summary>心理与生理压力负担；缺水、憋尿、阻塞和高疲劳会增加它。</summary>
        public float Stress { get; private set; }

        /// <summary>
        /// 居民的正向生命健康；严重缺水会连续损害它，归零即死亡且不能靠普通休息复活。
        /// 疾病、受伤、营养和医疗以后都应汇入这一结果状态，而不是各自另造一条“生命值”。
        /// </summary>
        public float Health { get; private set; }

        public bool IsAlive => Health > 0f;

        /// <summary>
        /// 连续推进身心状态。随机差异由调用方在行动开始时固定采样为
        /// <paramref name="outcomeScale"/>，而不是逐帧抖动，因此暂停、帧率和观察器不会改变随机轨迹。
        /// </summary>
        public void Advance(
            float seconds,
            ResidentWellbeingActivity activity,
            in ResidentWellbeingDrivers drivers,
            float outcomeScale = 1f)
        {
            if (!float.IsFinite(seconds) || seconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            if (!Enum.IsDefined(typeof(ResidentWellbeingActivity), activity))
                throw new ArgumentOutOfRangeException(nameof(activity));
            if (!float.IsFinite(outcomeScale) || outcomeScale <= 0f)
                throw new ArgumentOutOfRangeException(nameof(outcomeScale));
            if (seconds == 0f || !IsAlive) return;

            float effectScale = Math.Min(1.5f, Math.Max(0.5f, outcomeScale));
            float entertainmentDelta = activity == ResidentWellbeingActivity.Hobby
                ? HobbyEntertainmentGainPerSecond * effectScale
                : -EntertainmentDecayPerSecond;
            Entertainment = Clamp01(Entertainment + entertainmentDelta * seconds);

            float fatigueLoad = GetFatigueLoad(activity);
            float entertainmentBuffer = MaximumEntertainmentFatigueBuffer *
                                        SmootherStep(InverseLerp(0.55f, 0.9f, Entertainment));
            float fatigueDelta = BaseFatigueGrowthPerSecond * fatigueLoad *
                                 (1f - entertainmentBuffer);
            fatigueDelta -= GetFatigueRecovery(activity) * effectScale;
            Fatigue = Clamp01(Fatigue + fatigueDelta * seconds);

            float lowEntertainment = 1f - InverseLerp(0.1f, 0.42f, Entertainment);
            float highFatigue = InverseLerp(0.62f, 1f, Fatigue);
            float highThirst = InverseLerp(0.55f, 1f, drivers.ThirstDeficit);
            float highBladder = InverseLerp(0.65f, 1f, drivers.BladderPressure);
            float stressDelta = 0.0012f * lowEntertainment +
                                0.0014f * highFatigue +
                                0.0022f * highThirst +
                                0.0022f * highBladder +
                                (drivers.IsBlocked ? 0.0035f : -0.00035f) +
                                GetActivityStress(activity);
            stressDelta -= GetStressRecovery(activity) * effectScale;
            Stress = Clamp01(Stress + stressDelta * seconds);

            // 心情只缓慢响应长期条件；一次发呆可以稍微平复情绪，但不会伪装成兴趣娱乐。
            float moodDelta = -0.0014f * lowEntertainment -
                              0.0011f * InverseLerp(0.5f, 1f, Stress) -
                              0.0008f * InverseLerp(0.7f, 1f, Fatigue) +
                              GetMoodGain(activity) * effectScale;
            if (Entertainment >= 0.55f && Stress <= 0.45f && Mood < 0.72f)
                moodDelta += 0.00025f * InverseLerp(0f, 0.72f, 0.72f - Mood);
            Mood = Clamp01(Mood + moodDelta * seconds);

            // 轻微口渴只驱动饮水，不直接扣生命；越过严重缺水点后用平滑曲线加速损害。
            // 只有补足水分且仍活着时才允许缓慢恢复，地面休息是低质量兜底，未来床铺可提供更高恢复率。
            float dehydrationSeverity = SmootherStep(
                InverseLerp(DehydrationDamageOnset, 1f, drivers.ThirstDeficit));
            float healthDelta = -MaximumDehydrationHealthLossPerSecond *
                                dehydrationSeverity;
            if (activity == ResidentWellbeingActivity.SeatRest)
            {
                healthDelta -= NomadSeatRestRules.HealthLossPerSecond;
            }
            else if (drivers.ThirstDeficit < 0.55f)
            {
                float recoveryReadiness =
                    1f - 0.65f * SmootherStep(InverseLerp(0.55f, 1f, Fatigue));
                recoveryReadiness *=
                    1f - 0.45f * SmootherStep(InverseLerp(0.55f, 1f, Stress));
                healthDelta += GetHealthRecovery(activity) * recoveryReadiness * effectScale;
            }
            Health = Clamp01(Health + healthDelta * seconds);
        }

        /// <summary>
        /// 把正向状态转换为 Utility AI 的缺口快照。娱乐没有被发呆或闲逛恢复，因此没有真实爱好候选时，
        /// 它会诚实地继续降低，而不会因为“居民没在工作”自动归零。
        /// </summary>
        public ResidentNeedState[] CreateDecisionNeedSnapshot() => new[]
        {
            new ResidentNeedState(
                ResidentNeed.Entertainment,
                1f - Entertainment,
                EntertainmentDecayPerSecond,
                importance: 0.68f,
                canPromoteToUrgent: false),
            new ResidentNeedState(
                ResidentNeed.Fatigue,
                Fatigue,
                BaseFatigueGrowthPerSecond,
                importance: 0.82f),
            new ResidentNeedState(
                ResidentNeed.Health,
                1f - Health,
                0f,
                importance: 1.2f,
                // 健康是多个病因共同形成的结果状态；不能让“普通休息”仅因能少量恢复健康
                // 就自动晋升为急救。严重缺水、火灾和未来疾病应由各自的针对性方案声明风险层。
                canPromoteToUrgent: false),
            new ResidentNeedState(
                ResidentNeed.Stress,
                Stress,
                0f,
                importance: 0.74f),
        };

        /// <summary>返回候选评估使用的预期恢复；实际结算仍按连续推进和行动级随机系数发生。</summary>
        public static NeedEffect[] CreateExpectedEffects(
            ResidentWellbeingActivity activity,
            float seconds)
        {
            if (!float.IsFinite(seconds) || seconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(seconds));

            return activity switch
            {
                ResidentWellbeingActivity.GroundRest => new[]
                {
                    new NeedEffect(
                        ResidentNeed.Health,
                        Clamp01(GroundRestHealthRecoveryPerSecond * seconds)),
                    new NeedEffect(
                        ResidentNeed.Fatigue,
                        Clamp01(GroundRestFatigueRecoveryPerSecond * seconds)),
                    new NeedEffect(
                        ResidentNeed.Stress,
                        Clamp01(GroundRestStressRecoveryPerSecond * seconds)),
                },
                ResidentWellbeingActivity.SeatRest => new[]
                {
                    new NeedEffect(
                        ResidentNeed.Fatigue,
                        Clamp01(NomadSeatRestRules.FatigueRecoveryPerSecond * seconds)),
                    new NeedEffect(
                        ResidentNeed.Stress,
                        Clamp01(NomadSeatRestRules.StressRecoveryPerSecond * seconds)),
                },
                ResidentWellbeingActivity.Daydream => new[]
                {
                    new NeedEffect(
                        ResidentNeed.Fatigue,
                        Clamp01(DaydreamFatigueRecoveryPerSecond * seconds)),
                    new NeedEffect(
                        ResidentNeed.Stress,
                        Clamp01(DaydreamStressRecoveryPerSecond * seconds)),
                },
                ResidentWellbeingActivity.Wander => new[]
                {
                    new NeedEffect(
                        ResidentNeed.Fatigue,
                        Clamp01(WanderFatigueRecoveryPerSecond * seconds)),
                    new NeedEffect(
                        ResidentNeed.Stress,
                        Clamp01(WanderStressRecoveryPerSecond * seconds)),
                },
                ResidentWellbeingActivity.Hobby => new[]
                {
                    new NeedEffect(
                        ResidentNeed.Entertainment,
                        Clamp01(HobbyEntertainmentGainPerSecond * seconds)),
                    new NeedEffect(
                        ResidentNeed.Fatigue,
                        Clamp01(HobbyFatigueRecoveryPerSecond * seconds)),
                    new NeedEffect(
                        ResidentNeed.Stress,
                        Clamp01(HobbyStressRecoveryPerSecond * seconds)),
                },
                _ => Array.Empty<NeedEffect>(),
            };
        }

        /// <summary>为一次休闲行动固定采样 [0.85, 1.15) 的个人效果差异。</summary>
        public static float SampleLeisureOutcomeScale(
            int worldSeed,
            ulong residentId,
            long leisureSequence)
        {
            double sample = DeterministicRandom.Sample01(
                worldSeed,
                residentId,
                LeisureOutcomeRandomStreamId,
                leisureSequence);
            return 0.85f + (float)sample * 0.3f;
        }

        private static float GetFatigueLoad(ResidentWellbeingActivity activity) => activity switch
        {
            ResidentWellbeingActivity.Travel => 1.15f,
            ResidentWellbeingActivity.Work => 1.35f,
            ResidentWellbeingActivity.PersonalCare => 0.75f,
            ResidentWellbeingActivity.GroundRest => 0.1f,
            ResidentWellbeingActivity.SeatRest => 0.08f,
            ResidentWellbeingActivity.Daydream => 0.2f,
            ResidentWellbeingActivity.Wander => 0.55f,
            ResidentWellbeingActivity.Hobby => 0.45f,
            _ => 0.65f,
        };

        private static float GetFatigueRecovery(ResidentWellbeingActivity activity) => activity switch
        {
            ResidentWellbeingActivity.GroundRest => GroundRestFatigueRecoveryPerSecond,
            ResidentWellbeingActivity.SeatRest => NomadSeatRestRules.FatigueRecoveryPerSecond,
            ResidentWellbeingActivity.Daydream => DaydreamFatigueRecoveryPerSecond,
            ResidentWellbeingActivity.Wander => WanderFatigueRecoveryPerSecond,
            ResidentWellbeingActivity.Hobby => HobbyFatigueRecoveryPerSecond,
            _ => 0f,
        };

        private static float GetStressRecovery(ResidentWellbeingActivity activity) => activity switch
        {
            ResidentWellbeingActivity.GroundRest => GroundRestStressRecoveryPerSecond,
            ResidentWellbeingActivity.SeatRest => NomadSeatRestRules.StressRecoveryPerSecond,
            ResidentWellbeingActivity.Daydream => DaydreamStressRecoveryPerSecond,
            ResidentWellbeingActivity.Wander => WanderStressRecoveryPerSecond,
            ResidentWellbeingActivity.Hobby => HobbyStressRecoveryPerSecond,
            _ => 0f,
        };

        private static float GetMoodGain(ResidentWellbeingActivity activity) => activity switch
        {
            ResidentWellbeingActivity.GroundRest => -GroundRestMoodLossPerSecond,
            ResidentWellbeingActivity.SeatRest => -NomadSeatRestRules.MoodLossPerSecond,
            ResidentWellbeingActivity.Daydream => DaydreamMoodGainPerSecond,
            ResidentWellbeingActivity.Wander => WanderMoodGainPerSecond,
            ResidentWellbeingActivity.Hobby => HobbyMoodGainPerSecond,
            _ => 0f,
        };

        private static float GetHealthRecovery(ResidentWellbeingActivity activity) =>
            activity switch
            {
                ResidentWellbeingActivity.GroundRest => GroundRestHealthRecoveryPerSecond,
                ResidentWellbeingActivity.SeatRest => 0f,
                ResidentWellbeingActivity.Daydream => 0.00022f,
                ResidentWellbeingActivity.Hobby => 0.00018f,
                ResidentWellbeingActivity.PersonalCare => 0.00012f,
                _ => 0.00006f,
            };

        private static float GetActivityStress(ResidentWellbeingActivity activity) => activity switch
        {
            ResidentWellbeingActivity.Travel => 0.00015f,
            ResidentWellbeingActivity.Work => 0.0005f,
            ResidentWellbeingActivity.PersonalCare => -0.0002f,
            _ => 0f,
        };

        private static float ValidateNormalized(float value, string parameterName)
        {
            if (!float.IsFinite(value) || value < 0f || value > 1f)
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "居民身心状态必须位于 [0, 1]。");
            return value;
        }

        private static float InverseLerp(float minimum, float maximum, float value)
        {
            if (maximum <= minimum) return 0f;
            return Clamp01((value - minimum) / (maximum - minimum));
        }

        private static float SmootherStep(float value)
        {
            float t = Clamp01(value);
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        private static float Clamp01(float value) => Math.Min(1f, Math.Max(0f, value));
    }

    /// <summary>
    /// 把居民长期能力与短期身心状态折算为工作速度。公式只产生连续乘数，不拥有行动计时器；
    /// 执行器应在一次工作开始时固定采样个人波动，避免每帧随机造成进度抖动和存档分歧。
    /// </summary>
    public static class ResidentPerformance
    {
        private const string WorkPaceRandomStreamId = "resident-performance:work-pace";

        public static float CalculateExpectedWorkEfficiency(
            float baseEfficiency,
            float health,
            float fatigue,
            float stress)
        {
            ValidatePositiveFinite(baseEfficiency, nameof(baseEfficiency));
            ValidateNormalized(health, nameof(health));
            ValidateNormalized(fatigue, nameof(fatigue));
            ValidateNormalized(stress, nameof(stress));

            // 健康与疲劳是主要限制；中高压力提供短时动员增益，接近崩溃时则开始回落，
            // 但首版仍保留少量应激加速。失误、事故与长期健康代价属于质量 / 风险通道，
            // 不能因为这里“做得更快”就被吞掉。
            float healthFactor = 0.25f + 0.75f * SmootherStep(health);
            float fatigueFactor = 1f - 0.58f * SmootherStep(fatigue);
            float stressMobilization = 0.22f * SmootherStep(
                InverseLerp(0.15f, 0.7f, stress));
            float stressOverload = 0.1f * SmootherStep(
                InverseLerp(0.82f, 1f, stress));
            float stressFactor = 1f + stressMobilization - stressOverload;
            return Clamp(baseEfficiency * healthFactor * fatigueFactor * stressFactor, 0.1f, 2f);
        }

        public static float SampleWorkEfficiency(
            int worldSeed,
            ulong residentId,
            long actionSequence,
            float expectedEfficiency,
            float variation = 0.08f)
        {
            ValidatePositiveFinite(expectedEfficiency, nameof(expectedEfficiency));
            if (!float.IsFinite(variation) || variation < 0f || variation > 0.5f)
                throw new ArgumentOutOfRangeException(nameof(variation));

            double sample = DeterministicRandom.Sample01(
                worldSeed,
                residentId,
                WorkPaceRandomStreamId,
                actionSequence);
            float pace = 1f - variation + (float)sample * variation * 2f;
            return Clamp(expectedEfficiency * pace, 0.1f, 2f);
        }

        /// <summary>健康欠佳与疲劳增加费力行动成本，因此更倾向原地或有承托的休息。</summary>
        public static float CalculateEffortAversion(float health, float fatigue)
        {
            ValidateNormalized(health, nameof(health));
            ValidateNormalized(fatigue, nameof(fatigue));
            return 1f + (1f - health) * 1.4f + fatigue * 0.9f;
        }

        private static void ValidateNormalized(float value, string parameterName)
        {
            if (!float.IsFinite(value) || value < 0f || value > 1f)
                throw new ArgumentOutOfRangeException(parameterName, "归一化居民状态必须位于 [0, 1]。");
        }

        private static void ValidatePositiveFinite(float value, string parameterName)
        {
            if (!float.IsFinite(value) || value <= 0f)
                throw new ArgumentOutOfRangeException(parameterName, "工作效率必须是有限正数。");
        }

        private static float InverseLerp(float minimum, float maximum, float value) =>
            maximum <= minimum ? 0f : Clamp((value - minimum) / (maximum - minimum), 0f, 1f);

        private static float SmootherStep(float value)
        {
            float t = Clamp(value, 0f, 1f);
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        private static float Clamp(float value, float minimum, float maximum) =>
            Math.Min(maximum, Math.Max(minimum, value));
    }
}
