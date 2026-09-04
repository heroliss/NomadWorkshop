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

        /// <summary>
        /// 日常选择允许比单一工作决策更宽的合理候选池；Softmax 仍会按成本连续降低概率。
        /// 风险层会在短名单形成前排除更低层候选，因此该宽度也可用于包含休闲与生存候选的
        /// 统一决策入口，不会让休闲重新混入紧急抽样。
        /// </summary>
        public static UtilityDecisionPolicy CreateSelectionPolicy() => new()
        {
            RelativeShortlistThreshold = 0.5f,
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
                PersonalAffinity = 0.065f,
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
                // Foundation 只有两种基础休闲时，用偏好差配合正常 Softmax 温度形成约 2:1 的
                // 发呆/散步选择比。以后加入作画、聊天、观景等候选后，它们会自然分走概率，
                // 不需要另写“空闲时掷固定百分比”的第二套决策规则。
                PersonalAffinity = 0.17f,
                RestQuality = 0.42f,
                NeedEffects = ResidentWellbeing.CreateExpectedEffects(
                    ResidentWellbeingActivity.Daydream,
                    restSeconds),
            };
        }

        private static void ValidatePositiveFinite(float value, string parameterName)
        {
            if (!float.IsFinite(value) || value <= 0f)
                throw new ArgumentOutOfRangeException(parameterName);
        }

    }
}
