using System;

namespace Game.NomadWorkshop.Simulation
{
    /// <summary>
    /// 为没有固定设施目标的低优先级休闲生成完整行动方案。目标点与真实 NavMesh 路径由 Unity Adapter
    /// 提供，本类只负责把行程与真实的休整收益表达成可测试的 Utility 输入。普通发呆和闲逛
    /// 不再伪装成兴趣娱乐；只有未来的爱好、社交或娱乐设施才恢复 Entertainment。
    /// </summary>
    public static class ResidentLeisurePlanFactory
    {
        public const string WanderCandidateId = "leisure:wander-open-deck";
        public const string DaydreamCandidateId = "leisure:daydream-in-place";
        public const string GroundRestCandidateId = "recovery:rest-on-ground";
        public const string HobbyIntentId = "leisure:creative-observation";

        /// <summary>
        /// 日常选择允许比单一工作决策更宽的合理候选池；Softmax 仍会按成本连续降低概率。
        /// 风险层会在短名单形成前排除更低层候选，因此该宽度也可用于包含休闲与生存候选的
        /// 统一决策入口，不会让休闲重新混入紧急抽样。
        /// </summary>
        public static UtilityDecisionPolicy CreateSelectionPolicy() => new()
        {
            RelativeShortlistThreshold = 0.5f,
            // 候选中的偏好保持为 [0, 1] 的人物属性；这里只决定它在 Foundation
            // 日常决策里的最大加分，避免“很喜欢”压过真实需求和整项行动成本。
            PersonalAffinityUtilityScale = 0.35f,
        };

        public static ResidentActionPlanProposal CreateWander(
            float pathDistanceMeters,
            float moveSpeedMetersPerSecond,
            float restSeconds,
            string targetLabel)
        {
            ValidatePositiveFinite(moveSpeedMetersPerSecond, nameof(moveSpeedMetersPerSecond));
            ValidatePositiveFinite(restSeconds, nameof(restSeconds));
            if (!float.IsFinite(pathDistanceMeters) || pathDistanceMeters < 0f)
                throw new ArgumentOutOfRangeException(nameof(pathDistanceMeters));

            float travelSeconds = pathDistanceMeters / moveSpeedMetersPerSecond;
            return new ResidentActionPlanProposal(
                WanderCandidateId,
                "leisure-wander",
                "在甲板空地散步")
            {
                Steps = new[]
                {
                    new ResidentActionStepEstimate(
                        ResidentActionStepKind.Travel,
                        travelSeconds,
                        pathDistanceMeters,
                        string.IsNullOrWhiteSpace(targetLabel)
                            ? "走向一处可达空地"
                            : $"走向空地 {targetLabel}"),
                    new ResidentActionStepEstimate(
                        ResidentActionStepKind.Rest,
                        restSeconds,
                        label: "散步后观察周围"),
                },
                BaseUtility = 0.055f,
                ComfortBenefit = 0.035f,
                PersonalAffinity = 0.18f,
                RestQuality = 0.28f,
                Effort = 0.07f,
                WorkIntensity = 0.04f,
                NeedEffects = ResidentWellbeing.CreateExpectedEffects(
                    ResidentWellbeingActivity.Wander,
                    restSeconds),
            };
        }

        public static ResidentActionPlanProposal CreateDaydream(float restSeconds)
        {
            ValidatePositiveFinite(restSeconds, nameof(restSeconds));
            return new ResidentActionPlanProposal(
                DaydreamCandidateId,
                "leisure-daydream",
                "在原地发呆休息")
            {
                Steps = new[]
                {
                    new ResidentActionStepEstimate(
                        ResidentActionStepKind.Rest,
                        restSeconds,
                        label: "站在安全空地观察四周"),
                },
                BaseUtility = 0.05f,
                ComfortBenefit = 0.03f,
                // Foundation 只有两种基础休闲时，用归一化偏好差配合正常 Softmax 温度形成约 2:1 的
                // 发呆/散步选择比。以后加入作画、聊天、观景等候选后，它们会自然分走概率，
                // 不需要另写“空闲时掷固定百分比”的第二套决策规则。
                PersonalAffinity = 0.48f,
                RestQuality = 0.42f,
                NeedEffects = ResidentWellbeing.CreateExpectedEffects(
                    ResidentWellbeingActivity.Daydream,
                    restSeconds),
            };
        }

        /// <summary>
        /// 床铺与座椅不可用时的最低质量恢复方案。它无需设施或路径，因此不会因空间布局让低健康居民
        /// 完全失去休息机会；较低舒适度和实际心情损失由连续身心模型表达，未来更好的承托物会自然胜出。
        /// </summary>
        public static ResidentActionPlanProposal CreateGroundRest(float restSeconds)
        {
            ValidatePositiveFinite(restSeconds, nameof(restSeconds));
            return new ResidentActionPlanProposal(
                GroundRestCandidateId,
                "recovery-rest",
                "在地面坐下或躺下休息")
            {
                Steps = new[]
                {
                    new ResidentActionStepEstimate(
                        ResidentActionStepKind.Rest,
                        restSeconds,
                        label: "在当前安全位置进行低质量休息"),
                },
                BaseUtility = 0.018f,
                RestQuality = 0.34f,
                NeedEffects = ResidentWellbeing.CreateExpectedEffects(
                    ResidentWellbeingActivity.GroundRest,
                    restSeconds),
            };
        }

        /// <summary>
        /// 为一个真实设施上的创作爱好生成完整方案。候选保留设施实例 id 并共享同一意图；
        /// 调用方若提供多个目标，Utility 会先按路程与收益归并。当前 Foundation Adapter
        /// 会先选择最近可达实例；InteractionGroup 的瞬时占用仍由 Unity Adapter 在执行时取得。
        /// </summary>
        public static ResidentActionPlanProposal CreateHobbyAtFacility(
            string facilityInstanceId,
            string facilityDisplayName,
            float pathDistanceMeters,
            float moveSpeedMetersPerSecond,
            float hobbySeconds,
            float personalAffinity)
        {
            if (string.IsNullOrWhiteSpace(facilityInstanceId))
                throw new ArgumentException("爱好设施实例 id 不能为空。", nameof(facilityInstanceId));
            if (string.IsNullOrWhiteSpace(facilityDisplayName))
                throw new ArgumentException("爱好设施显示名不能为空。", nameof(facilityDisplayName));
            if (!float.IsFinite(pathDistanceMeters) || pathDistanceMeters < 0f)
                throw new ArgumentOutOfRangeException(nameof(pathDistanceMeters));
            ValidatePositiveFinite(moveSpeedMetersPerSecond, nameof(moveSpeedMetersPerSecond));
            ValidatePositiveFinite(hobbySeconds, nameof(hobbySeconds));
            ValidateNormalized(personalAffinity, nameof(personalAffinity));

            float travelSeconds = pathDistanceMeters / moveSpeedMetersPerSecond;
            return new ResidentActionPlanProposal(
                $"hobby:{facilityInstanceId}",
                HobbyIntentId,
                $"在{facilityDisplayName}作画并观察远方")
            {
                Steps = new[]
                {
                    new ResidentActionStepEstimate(
                        ResidentActionStepKind.Travel,
                        travelSeconds,
                        pathDistanceMeters,
                        $"前往{facilityDisplayName}"),
                    new ResidentActionStepEstimate(
                        ResidentActionStepKind.UseFacility,
                        hobbySeconds,
                        label: "作画并观察车外景色"),
                },
                BaseUtility = 0.045f,
                ComfortBenefit = 0.055f,
                PersonalAffinity = personalAffinity,
                RestQuality = 0.5f,
                Effort = 0.035f,
                NeedEffects = ResidentWellbeing.CreateExpectedEffects(
                    ResidentWellbeingActivity.Hobby,
                    hobbySeconds),
                ReservationKeys = new[] { $"facility:{facilityInstanceId}:hobby" },
            };
        }

        private static void ValidatePositiveFinite(float value, string parameterName)
        {
            if (!float.IsFinite(value) || value <= 0f)
                throw new ArgumentOutOfRangeException(parameterName);
        }

        private static void ValidateNormalized(float value, string parameterName)
        {
            if (!float.IsFinite(value) || value < 0f || value > 1f)
                throw new ArgumentOutOfRangeException(parameterName);
        }

    }
}
