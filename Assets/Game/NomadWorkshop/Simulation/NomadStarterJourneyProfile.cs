using System;

namespace Game.NomadWorkshop.Simulation
{
    /// <summary>玩家化身可以持久化的性别选项；Unspecified 保留给旧档和不想设定的玩家。</summary>
    public enum NomadCharacterGender
    {
        Unspecified = 0,
        Female = 1,
        Male = 2,
        NonBinary = 3,
    }

    /// <summary>微型车开局只声明已经存在的空间能力，不把渲染 Prefab 当成玩法真值。</summary>
    [Flags]
    public enum NomadStarterVehicleCapability
    {
        None = 0,
        DriverSeat = 1 << 0,
    }

    /// <summary>
    /// 居民身份的纯数据入口。显示名允许为空以兼容旧存档；StarterJourneyProfile 的默认玩家化身
    /// 会提供可直接显示的名字。外观只保存稳定种子，具体资产由 View 按种子选择。
    /// </summary>
    public readonly struct NomadResidentIdentity : IEquatable<NomadResidentIdentity>
    {
        public const int MaximumDisplayNameLength = 32;

        public NomadResidentIdentity(
            string displayName,
            NomadCharacterGender gender,
            int appearanceSeed,
            bool isPlayerAvatar)
        {
            DisplayName = ValidateDisplayName(displayName, allowEmpty: true);
            if (!Enum.IsDefined(typeof(NomadCharacterGender), gender))
                throw new ArgumentOutOfRangeException(nameof(gender));
            Gender = gender;
            AppearanceSeed = appearanceSeed;
            IsPlayerAvatar = isPlayerAvatar;
        }

        public string DisplayName { get; }
        public NomadCharacterGender Gender { get; }
        public int AppearanceSeed { get; }
        public bool IsPlayerAvatar { get; }

        public bool Equals(NomadResidentIdentity other) =>
            string.Equals(DisplayName, other.DisplayName, StringComparison.Ordinal) &&
            Gender == other.Gender &&
            AppearanceSeed == other.AppearanceSeed &&
            IsPlayerAvatar == other.IsPlayerAvatar;

        public override bool Equals(object obj) =>
            obj is NomadResidentIdentity other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(DisplayName, Gender, AppearanceSeed, IsPlayerAvatar);

        public static string ValidateDisplayName(string value, bool allowEmpty)
        {
            value ??= string.Empty;
            if (!allowEmpty && string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("居民显示名不能为空。", nameof(value));
            if (value.Length > MaximumDisplayNameLength)
                throw new ArgumentException(
                    $"居民显示名不能超过 {MaximumDisplayNameLength} 个字符。", nameof(value));
            for (var i = 0; i < value.Length; i++)
                if (char.IsControl(value[i]))
                    throw new ArgumentException("居民显示名不能包含控制字符。", nameof(value));
            return value;
        }
    }

    /// <summary>StarterJourneySample 的最小车辆能力；后续小卡车和 NW5 通过新的配置替换它。</summary>
    public readonly struct NomadStarterVehicleProfile : IEquatable<NomadStarterVehicleProfile>
    {
        public const string MicroCarId = "starter-micro-car";

        public NomadStarterVehicleProfile(
            string vehicleId,
            int seatCount,
            bool hasSleepBerth,
            int facilitySlotCapacity,
            NomadStarterVehicleCapability capabilities)
        {
            if (string.IsNullOrWhiteSpace(vehicleId))
                throw new ArgumentException("起步车辆 id 不能为空。", nameof(vehicleId));
            if (seatCount < 1) throw new ArgumentOutOfRangeException(nameof(seatCount));
            if (facilitySlotCapacity < 0)
                throw new ArgumentOutOfRangeException(nameof(facilitySlotCapacity));
            if (!Enum.IsDefined(typeof(NomadStarterVehicleCapability), capabilities))
                throw new ArgumentOutOfRangeException(nameof(capabilities));
            VehicleId = vehicleId;
            SeatCount = seatCount;
            HasSleepBerth = hasSleepBerth;
            FacilitySlotCapacity = facilitySlotCapacity;
            Capabilities = capabilities;
        }

        public string VehicleId { get; }
        public int SeatCount { get; }
        public bool HasSleepBerth { get; }
        public int FacilitySlotCapacity { get; }
        public NomadStarterVehicleCapability Capabilities { get; }

        public bool Equals(NomadStarterVehicleProfile other) =>
            string.Equals(VehicleId, other.VehicleId, StringComparison.Ordinal) &&
            SeatCount == other.SeatCount &&
            HasSleepBerth == other.HasSleepBerth &&
            FacilitySlotCapacity == other.FacilitySlotCapacity &&
            Capabilities == other.Capabilities;

        public override bool Equals(object obj) =>
            obj is NomadStarterVehicleProfile other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(VehicleId, SeatCount, HasSleepBerth, FacilitySlotCapacity, Capabilities);

        public static NomadStarterVehicleProfile MicroCar => new(
            MicroCarId,
            seatCount: 1,
            hasSleepBerth: false,
            facilitySlotCapacity: 0,
            capabilities: NomadStarterVehicleCapability.DriverSeat);
    }

    /// <summary>
    /// N1 的最小开局配置。它是纯配置接缝，不创建 Unity 场景，也不替换已验证的 ReferenceVehicleSample。
    /// </summary>
    public sealed class NomadStarterJourneyProfile
    {
        public const string DefaultProfileId = "starter-micro-car-v1";

        public NomadStarterJourneyProfile(
            string profileId,
            NomadResidentIdentity playerIdentity,
            NomadStarterVehicleProfile vehicle)
        {
            if (string.IsNullOrWhiteSpace(profileId))
                throw new ArgumentException("起步配置 id 不能为空。", nameof(profileId));
            if (!playerIdentity.IsPlayerAvatar)
                throw new ArgumentException("起步配置必须明确一名玩家化身。", nameof(playerIdentity));
            if (string.IsNullOrWhiteSpace(playerIdentity.DisplayName))
                throw new ArgumentException("玩家化身必须有可显示姓名。", nameof(playerIdentity));
            ProfileId = profileId;
            PlayerIdentity = playerIdentity;
            Vehicle = vehicle;
        }

        public string ProfileId { get; }
        public NomadResidentIdentity PlayerIdentity { get; }
        public NomadStarterVehicleProfile Vehicle { get; }

        public static NomadStarterJourneyProfile Default => new(
            DefaultProfileId,
            new NomadResidentIdentity("旅人", NomadCharacterGender.Unspecified, 1, true),
            NomadStarterVehicleProfile.MicroCar);
    }

    /// <summary>只有驾驶位的微型车没有床铺；座位休息恢复慢，并付出长期健康与心情代价。</summary>
    public static class NomadSeatRestRules
    {
        public const float FatigueRecoveryPerSecond = 0.0035f;
        public const float StressRecoveryPerSecond = 0.0015f;
        public const float HealthLossPerSecond = 0.00012f;
        public const float MoodLossPerSecond = 0.00035f;

        public static NomadSeatRestOutcome Evaluate(
            float seconds,
            float currentFatigue,
            float currentHealth)
        {
            ValidateNonNegativeFinite(seconds, nameof(seconds));
            ValidateNormalized(currentFatigue, nameof(currentFatigue));
            ValidateNormalized(currentHealth, nameof(currentHealth));
            return new NomadSeatRestOutcome(
                Math.Min(currentFatigue, FatigueRecoveryPerSecond * seconds),
                Math.Min(currentHealth, HealthLossPerSecond * seconds),
                Math.Min(1f, StressRecoveryPerSecond * seconds),
                -Math.Min(1f, MoodLossPerSecond * seconds));
        }

        private static void ValidateNonNegativeFinite(float value, string parameterName)
        {
            if (!float.IsFinite(value) || value < 0f)
                throw new ArgumentOutOfRangeException(parameterName);
        }

        private static void ValidateNormalized(float value, string parameterName)
        {
            if (!float.IsFinite(value) || value < 0f || value > 1f)
                throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    public readonly struct NomadSeatRestOutcome
    {
        public NomadSeatRestOutcome(
            float fatigueRecovered,
            float healthLost,
            float stressRecovered,
            float moodDelta)
        {
            FatigueRecovered = fatigueRecovered;
            HealthLost = healthLost;
            StressRecovered = stressRecovered;
            MoodDelta = moodDelta;
        }

        public float FatigueRecovered { get; }
        public float HealthLost { get; }
        public float StressRecovered { get; }
        public float MoodDelta { get; }
    }
}
