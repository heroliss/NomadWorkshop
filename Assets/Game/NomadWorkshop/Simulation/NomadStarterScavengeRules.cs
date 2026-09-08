using System;

namespace Game.NomadWorkshop.Simulation
{
    /// <summary>StarterJourneySample 当前唯一的起步拾荒阻塞原因；正式资源系统接入后仍可作为提示映射边界。</summary>
    public enum NomadStarterScavengeBlocker
    {
        None = 0,
        InvalidAmount = 1,
        CacheEmpty = 2,
        CargoFull = 3,
    }

    /// <summary>一次拾荒意图的守恒结果；不包含 Unity 物件或表现层对象。</summary>
    public readonly struct NomadStarterScavengeResult
    {
        public NomadStarterScavengeResult(
            int collectedAmount,
            int remainingCache,
            int carriedScrap,
            NomadStarterScavengeBlocker blocker)
        {
            CollectedAmount = collectedAmount;
            RemainingCache = remainingCache;
            CarriedScrap = carriedScrap;
            Blocker = blocker;
        }

        public int CollectedAmount { get; }
        public int RemainingCache { get; }
        public int CarriedScrap { get; }
        public NomadStarterScavengeBlocker Blocker { get; }
        public bool Succeeded => CollectedAmount > 0 && Blocker == NomadStarterScavengeBlocker.None;
    }

    /// <summary>
    /// 微型车开局的最小纯模拟状态。当前只表达可搬运废料、座位休息意图和长期软代价，
    /// 不把“看到一堆模型”当成库存，也不提前引入完整车辆载荷系统。
    /// </summary>
    public sealed class NomadStarterScavengeState
    {
        public NomadStarterScavengeState(
            int scrapCapacity,
            int initialScrap = 0,
            int initialCache = 0,
            float initialFatigue = 0.58f,
            float initialHealth = 1f,
            float initialStress = 0.22f,
            float initialMood = 0.7f)
        {
            if (scrapCapacity < 1) throw new ArgumentOutOfRangeException(nameof(scrapCapacity));
            if (initialScrap < 0 || initialScrap > scrapCapacity)
                throw new ArgumentOutOfRangeException(nameof(initialScrap));
            if (initialCache < 0) throw new ArgumentOutOfRangeException(nameof(initialCache));
            ValidateNormalized(initialFatigue, nameof(initialFatigue));
            ValidateNormalized(initialHealth, nameof(initialHealth));
            ValidateNormalized(initialStress, nameof(initialStress));
            ValidateNormalized(initialMood, nameof(initialMood));

            ScrapCapacity = scrapCapacity;
            CarriedScrap = initialScrap;
            CacheRemaining = initialCache;
            Fatigue = initialFatigue;
            Health = initialHealth;
            Stress = initialStress;
            Mood = initialMood;
        }

        public int ScrapCapacity { get; }
        public int CarriedScrap { get; private set; }
        public int CacheRemaining { get; private set; }
        public bool SeatRestIntent { get; private set; }
        public float Fatigue { get; private set; }
        public float Health { get; private set; }
        public float Stress { get; private set; }
        public float Mood { get; private set; }

        public NomadStarterScavengeResult TryCollectScrap(int requestedAmount = 1)
        {
            if (requestedAmount < 1)
                return Result(0, NomadStarterScavengeBlocker.InvalidAmount);
            if (CacheRemaining == 0)
                return Result(0, NomadStarterScavengeBlocker.CacheEmpty);

            int freeCapacity = ScrapCapacity - CarriedScrap;
            if (freeCapacity == 0)
                return Result(0, NomadStarterScavengeBlocker.CargoFull);

            int amount = Math.Min(requestedAmount, Math.Min(CacheRemaining, freeCapacity));
            CarriedScrap = checked(CarriedScrap + amount);
            CacheRemaining = checked(CacheRemaining - amount);
            return Result(amount, NomadStarterScavengeBlocker.None);
        }

        public void SetSeatRestIntent(bool active) => SeatRestIntent = active;

        public void Advance(float seconds)
        {
            if (!float.IsFinite(seconds) || seconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            if (!SeatRestIntent || seconds == 0f || Health <= 0f) return;

            NomadSeatRestOutcome outcome = NomadSeatRestRules.Evaluate(seconds, Fatigue, Health);
            Fatigue = Math.Max(0f, Fatigue - outcome.FatigueRecovered);
            Health = Math.Max(0f, Health - outcome.HealthLost);
            Stress = Math.Max(0f, Stress - outcome.StressRecovered);
            Mood = Clamp01(Mood + outcome.MoodDelta);
        }

        private NomadStarterScavengeResult Result(
            int collectedAmount,
            NomadStarterScavengeBlocker blocker) =>
            new(collectedAmount, CacheRemaining, CarriedScrap, blocker);

        private static void ValidateNormalized(float value, string parameterName)
        {
            if (!float.IsFinite(value) || value < 0f || value > 1f)
                throw new ArgumentOutOfRangeException(parameterName);
        }

        private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));
    }
}
