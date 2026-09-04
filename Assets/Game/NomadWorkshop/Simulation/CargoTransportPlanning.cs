using System;
using System.Collections.Generic;

namespace Game.NomadWorkshop.Simulation
{
    /// <summary>容器对货物提供的能力；多个能力可组合，避免按具体物品类型写分支。</summary>
    [Flags]
    public enum CargoContainerCapability
    {
        None = 0,
        LiquidTight = 1 << 0,
        FoodSafe = 1 << 1,
        Sealable = 1 << 2,
        Insulated = 1 << 3,
    }

    public enum CargoCarrierKind
    {
        BareHands,
        Container,
    }

    /// <summary>一个搬运方式不能服务当前货物时的稳定原因。</summary>
    public enum CargoTransportBlockReason
    {
        None,
        CarrierUnavailable,
        BareHandsForbidden,
        ContainerForbidden,
        CapacityInsufficient,
        MissingContainerCapability,
    }

    /// <summary>
    /// 资源对一次搬运的约束。RequiredContainerCapabilities 只约束容器；若 AllowBareHands 为真，
    /// 徒手方案按 BareHandsMaxAmount 单独判定，并承担手部洁净度带来的污染风险。
    /// </summary>
    public readonly struct CargoTransportRequirement
    {
        public CargoTransportRequirement(
            ResourceId resource,
            int amount,
            CargoContainerCapability requiredContainerCapabilities,
            bool allowBareHands,
            int bareHandsMaxAmount,
            float contaminationSensitivity,
            bool allowContainers = true)
        {
            if (!resource.IsValid) throw new ArgumentException("资源 id 无效。", nameof(resource));
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (bareHandsMaxAmount < 0) throw new ArgumentOutOfRangeException(nameof(bareHandsMaxAmount));
            if (!allowBareHands && !allowContainers)
                throw new ArgumentException("至少需要允许一种搬运方式。 ");
            if (float.IsNaN(contaminationSensitivity) || float.IsInfinity(contaminationSensitivity) ||
                contaminationSensitivity < 0f || contaminationSensitivity > 1f)
                throw new ArgumentOutOfRangeException(
                    nameof(contaminationSensitivity),
                    "污染敏感度必须位于 [0, 1]。");

            Resource = resource;
            Amount = amount;
            RequiredContainerCapabilities = requiredContainerCapabilities;
            AllowBareHands = allowBareHands;
            BareHandsMaxAmount = bareHandsMaxAmount;
            ContaminationSensitivity = contaminationSensitivity;
            AllowContainers = allowContainers;
        }

        public ResourceId Resource { get; }
        public int Amount { get; }
        public CargoContainerCapability RequiredContainerCapabilities { get; }
        public bool AllowBareHands { get; }
        public int BareHandsMaxAmount { get; }
        public float ContaminationSensitivity { get; }
        public bool AllowContainers { get; }
    }

    /// <summary>居民当前可以尝试取得的一种搬运方式；Cleanliness 为 0 表示完全污染、1 表示洁净。</summary>
    public sealed class CargoCarrierOption
    {
        public CargoCarrierOption(
            string id,
            CargoCarrierKind kind,
            int capacity,
            float cleanliness,
            float acquireDurationSeconds,
            CargoContainerCapability capabilities = CargoContainerCapability.None,
            bool isAvailable = true)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("搬运方式 id 不能为空。", nameof(id));
            if (!Enum.IsDefined(typeof(CargoCarrierKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (float.IsNaN(cleanliness) || float.IsInfinity(cleanliness) ||
                cleanliness < 0f || cleanliness > 1f)
                throw new ArgumentOutOfRangeException(nameof(cleanliness));
            if (float.IsNaN(acquireDurationSeconds) || float.IsInfinity(acquireDurationSeconds) ||
                acquireDurationSeconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(acquireDurationSeconds));
            if (kind == CargoCarrierKind.BareHands && capabilities != CargoContainerCapability.None)
                throw new ArgumentException("徒手搬运不能声明容器能力。", nameof(capabilities));
            CargoContainerCapability knownCapabilities = CargoContainerCapability.LiquidTight |
                                                         CargoContainerCapability.FoodSafe |
                                                         CargoContainerCapability.Sealable |
                                                         CargoContainerCapability.Insulated;
            if ((capabilities & ~knownCapabilities) != 0)
                throw new ArgumentOutOfRangeException(nameof(capabilities));

            Id = id.Trim();
            Kind = kind;
            Capacity = capacity;
            Cleanliness = cleanliness;
            AcquireDurationSeconds = acquireDurationSeconds;
            Capabilities = capabilities;
            IsAvailable = isAvailable;
        }

        public string Id { get; }
        public CargoCarrierKind Kind { get; }
        public int Capacity { get; }
        public float Cleanliness { get; }
        public float AcquireDurationSeconds { get; }
        public CargoContainerCapability Capabilities { get; }
        public bool IsAvailable { get; }
    }

    /// <summary>一个货物—搬运方式组合的可解释判定；可行项再扩成完整行动方案参与效用比较。</summary>
    public readonly struct CargoTransportOptionEvaluation
    {
        public CargoTransportOptionEvaluation(
            CargoCarrierOption carrier,
            CargoTransportBlockReason blockReason,
            float contaminationTransferRisk)
        {
            Carrier = carrier ?? throw new ArgumentNullException(nameof(carrier));
            BlockReason = blockReason;
            ContaminationTransferRisk = contaminationTransferRisk;
        }

        public CargoCarrierOption Carrier { get; }
        public CargoTransportBlockReason BlockReason { get; }
        public float ContaminationTransferRisk { get; }
        public bool IsFeasible => BlockReason == CargoTransportBlockReason.None;
    }

    /// <summary>
    /// 纯规则层的货物—容器匹配器。它不自行挑中“最快容器”；每个可行项都应把取得容器、
    /// 往返和清洗时间带入完整方案，以免局部最优破坏全局决策。
    /// </summary>
    public static class CargoTransportPlanner
    {
        private const CargoContainerCapability AllCapabilities =
            CargoContainerCapability.LiquidTight |
            CargoContainerCapability.FoodSafe |
            CargoContainerCapability.Sealable |
            CargoContainerCapability.Insulated;

        public static CargoTransportOptionEvaluation Evaluate(
            CargoTransportRequirement requirement,
            CargoCarrierOption carrier)
        {
            if (carrier == null) throw new ArgumentNullException(nameof(carrier));
            if (!requirement.Resource.IsValid || requirement.Amount <= 0 ||
                requirement.BareHandsMaxAmount < 0 ||
                (!requirement.AllowBareHands && !requirement.AllowContainers) ||
                (requirement.RequiredContainerCapabilities & ~AllCapabilities) != 0 ||
                float.IsNaN(requirement.ContaminationSensitivity) ||
                float.IsInfinity(requirement.ContaminationSensitivity) ||
                requirement.ContaminationSensitivity < 0f ||
                requirement.ContaminationSensitivity > 1f)
            {
                throw new ArgumentException("货物搬运约束无效。", nameof(requirement));
            }

            CargoTransportBlockReason blockReason = GetBlockReason(requirement, carrier);
            float contaminationRisk = blockReason == CargoTransportBlockReason.None
                ? Clamp01(requirement.ContaminationSensitivity * (1f - carrier.Cleanliness))
                : 0f;
            return new CargoTransportOptionEvaluation(carrier, blockReason, contaminationRisk);
        }

        public static IReadOnlyList<CargoTransportOptionEvaluation> EvaluateAll(
            CargoTransportRequirement requirement,
            IReadOnlyList<CargoCarrierOption> carriers)
        {
            if (carriers == null) throw new ArgumentNullException(nameof(carriers));
            var result = new CargoTransportOptionEvaluation[carriers.Count];
            for (int i = 0; i < carriers.Count; i++)
            {
                CargoCarrierOption carrier = carriers[i]
                    ?? throw new ArgumentException($"搬运方式列表第 {i} 项为 null。", nameof(carriers));
                result[i] = Evaluate(requirement, carrier);
            }
            return result;
        }

        private static CargoTransportBlockReason GetBlockReason(
            CargoTransportRequirement requirement,
            CargoCarrierOption carrier)
        {
            if (!carrier.IsAvailable) return CargoTransportBlockReason.CarrierUnavailable;

            if (carrier.Kind == CargoCarrierKind.BareHands)
            {
                if (!requirement.AllowBareHands) return CargoTransportBlockReason.BareHandsForbidden;
                int effectiveCapacity = Math.Min(carrier.Capacity, requirement.BareHandsMaxAmount);
                return effectiveCapacity < requirement.Amount
                    ? CargoTransportBlockReason.CapacityInsufficient
                    : CargoTransportBlockReason.None;
            }

            if (!requirement.AllowContainers) return CargoTransportBlockReason.ContainerForbidden;
            CargoContainerCapability missing = requirement.RequiredContainerCapabilities &
                                               ~carrier.Capabilities;
            if (missing != CargoContainerCapability.None)
                return CargoTransportBlockReason.MissingContainerCapability;
            return carrier.Capacity < requirement.Amount
                ? CargoTransportBlockReason.CapacityInsufficient
                : CargoTransportBlockReason.None;
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }
    }
}
