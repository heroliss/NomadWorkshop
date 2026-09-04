using System;
using System.Collections.Generic;

namespace Game.NomadWorkshop.Simulation
{
    /// <summary>行动方案中的步骤类型；类型用于成本归类，具体执行仍由行动执行器负责。</summary>
    public enum ResidentActionStepKind
    {
        AcquireItem,
        Wash,
        Travel,
        Queue,
        Transfer,
        UseFacility,
        Consume,
        CleanUp,
        Rest,
    }

    /// <summary>完整行动方案在进入效用选择前被硬性排除的稳定原因。</summary>
    public enum ResidentActionPlanBlockReason
    {
        None,
        TargetUnreachable,
        PrerequisiteUnavailable,
        MissingResource,
        MissingCompatibleCarrier,
        CarrierCapacityInsufficient,
        DestinationFull,
        InteractionUnavailable,
        UnsafeEnvironment,
        CapabilityUnavailable,
    }

    /// <summary>行动方案的硬可行性。软偏好、风险和距离不能伪装成不可执行。</summary>
    public readonly struct ResidentActionPlanFeasibility
    {
        public ResidentActionPlanFeasibility(
            ResidentActionPlanBlockReason reason,
            string detail = "")
        {
            if (!Enum.IsDefined(typeof(ResidentActionPlanBlockReason), reason))
                throw new ArgumentOutOfRangeException(nameof(reason));
            Reason = reason;
            Detail = detail ?? string.Empty;
        }

        public ResidentActionPlanBlockReason Reason { get; }
        public string Detail { get; }
        public bool IsFeasible => Reason == ResidentActionPlanBlockReason.None;

        public static ResidentActionPlanFeasibility Available =>
            new(ResidentActionPlanBlockReason.None);

        public static ResidentActionPlanFeasibility Blocked(
            ResidentActionPlanBlockReason reason,
            string detail = "")
        {
            if (reason == ResidentActionPlanBlockReason.None)
                throw new ArgumentException("阻塞原因不能为 None。", nameof(reason));
            return new ResidentActionPlanFeasibility(reason, detail);
        }
    }

    /// <summary>
    /// 一个可估算的行动步骤。DistanceMeters 只做诊断；路径查询得到的 DurationSeconds 才进入决策，
    /// 因为拥堵、疲劳和移动速度都会让相同距离产生不同时间成本。
    /// </summary>
    public readonly struct ResidentActionStepEstimate
    {
        public ResidentActionStepEstimate(
            ResidentActionStepKind kind,
            float durationSeconds,
            float distanceMeters = 0f,
            string label = "")
        {
            if (!Enum.IsDefined(typeof(ResidentActionStepKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            ValidateNonNegativeFinite(durationSeconds, nameof(durationSeconds));
            ValidateNonNegativeFinite(distanceMeters, nameof(distanceMeters));

            Kind = kind;
            DurationSeconds = durationSeconds;
            DistanceMeters = distanceMeters;
            Label = label ?? string.Empty;
        }

        public ResidentActionStepKind Kind { get; }
        public float DurationSeconds { get; }
        public float DistanceMeters { get; }
        public string Label { get; }

        private static void ValidateNonNegativeFinite(float value, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    /// <summary>
    /// 与具体行动无关的居民决策状态。需求本身仍由 <see cref="ResidentNeedState"/> 表达；
    /// 这里保存会同时修正多类行动的个人差异和短期身体状态。
    /// </summary>
    public readonly struct ResidentDecisionCondition
    {
        public ResidentDecisionCondition(
            float motionSickness,
            float timeSensitivity = 1f,
            float effortAversion = 1f,
            float riskAversion = 1f)
        {
            ValidateNormalized(motionSickness, nameof(motionSickness));
            ValidateNonNegativeFinite(timeSensitivity, nameof(timeSensitivity));
            ValidateNonNegativeFinite(effortAversion, nameof(effortAversion));
            ValidateNonNegativeFinite(riskAversion, nameof(riskAversion));

            MotionSickness = motionSickness;
            TimeSensitivity = timeSensitivity;
            EffortAversion = effortAversion;
            RiskAversion = riskAversion;
        }

        public float MotionSickness { get; }
        public float TimeSensitivity { get; }
        public float EffortAversion { get; }
        public float RiskAversion { get; }

        private static void ValidateNormalized(float value, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f || value > 1f)
                throw new ArgumentOutOfRangeException(parameterName, "归一化状态必须位于 [0, 1]。");
        }

        private static void ValidateNonNegativeFinite(float value, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    /// <summary>方案估算的原型参数；以后应由可观察配置提供，不把平衡数值写死在候选生成器中。</summary>
    public sealed class ResidentActionPlanPolicy
    {
        public float TravelTimeCostPerSecond { get; set; } = 0.006f;
        public float QueueTimeCostPerSecond { get; set; } = 0.008f;
        public float ActiveTimeCostPerSecond { get; set; } = 0.002f;
        public float UrgentDelayCostMultiplier { get; set; } = 2f;
        public float EffortCostScale { get; set; } = 0.14f;
        public float ExpectedRiskCostScale { get; set; } = 0.6f;
        public float MotionSicknessWorkCostScale { get; set; } = 0.45f;
        public float MotionSicknessRestBenefitScale { get; set; } = 0.25f;
    }

    /// <summary>
    /// 候选生成器对一个完整行动方案的可变描述。先列出拿容器、洗手、移动、等待、使用和收尾等步骤，
    /// 再统一估算；不要在各设施脚本中各写一套距离或卫生权重公式。
    /// </summary>
    public sealed class ResidentActionPlanProposal
    {
        public ResidentActionPlanProposal(string id, string intentId, string displayName)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("方案 id 不能为空。", nameof(id));
            if (string.IsNullOrWhiteSpace(intentId)) throw new ArgumentException("意图 id 不能为空。", nameof(intentId));
            if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("显示名不能为空。", nameof(displayName));

            Id = id;
            IntentId = intentId;
            DisplayName = displayName;
        }

        public string Id { get; }
        public string IntentId { get; }
        public string DisplayName { get; }
        public ResidentActionStepEstimate[] Steps { get; set; } = Array.Empty<ResidentActionStepEstimate>();
        public ResidentActionPlanFeasibility Feasibility { get; set; } = ResidentActionPlanFeasibility.Available;
        public float BaseUtility { get; set; }
        public float ComfortBenefit { get; set; }
        public float CleanlinessBenefit { get; set; }
        public float PrivacyBenefit { get; set; }
        public float SocialBenefit { get; set; }
        public float WorkUrgency { get; set; }
        public float PlayerPriority { get; set; }
        public float DependencyValue { get; set; }
        public float SkillFit { get; set; }
        public float PersonalAffinity { get; set; }
        public float WaitingAge { get; set; }
        public float ContinuityBonus { get; set; }
        public float ResourceCost { get; set; }
        public float SwitchCost { get; set; }
        public ResidentDecisionRiskTier RiskTier { get; set; }
        public float RiskPriority { get; set; }
        public float DelayUrgency { get; set; }
        public float Effort { get; set; }
        public float WorkIntensity { get; set; }
        public float RestQuality { get; set; }
        public float FailureProbability { get; set; }
        public float FailureSeverity { get; set; }
        public NeedEffect[] NeedEffects { get; set; } = Array.Empty<NeedEffect>();
        public string[] ReservationKeys { get; set; } = Array.Empty<string>();
    }

    /// <summary>完整方案转为 Utility 候选时的可解释分项，便于调试“为什么选这里”。</summary>
    public readonly struct ActionPlanUtilityBreakdown
    {
        public ActionPlanUtilityBreakdown(
            float totalDurationSeconds,
            float travelSeconds,
            float travelDistanceMeters,
            float comfortBenefit,
            float cleanlinessBenefit,
            float privacyBenefit,
            float socialBenefit,
            float motionRecoveryBenefit,
            float travelCost,
            float queueCost,
            float activeTimeCost,
            float effortCost,
            float expectedRiskCost,
            float motionWorkCost,
            float resourceCost,
            float switchCost)
        {
            TotalDurationSeconds = totalDurationSeconds;
            TravelSeconds = travelSeconds;
            TravelDistanceMeters = travelDistanceMeters;
            ComfortBenefit = comfortBenefit;
            CleanlinessBenefit = cleanlinessBenefit;
            PrivacyBenefit = privacyBenefit;
            SocialBenefit = socialBenefit;
            MotionRecoveryBenefit = motionRecoveryBenefit;
            TravelCost = travelCost;
            QueueCost = queueCost;
            ActiveTimeCost = activeTimeCost;
            EffortCost = effortCost;
            ExpectedRiskCost = expectedRiskCost;
            MotionWorkCost = motionWorkCost;
            ResourceCost = resourceCost;
            SwitchCost = switchCost;
        }

        public float TotalDurationSeconds { get; }
        public float TravelSeconds { get; }
        public float TravelDistanceMeters { get; }
        public float ComfortBenefit { get; }
        public float CleanlinessBenefit { get; }
        public float PrivacyBenefit { get; }
        public float SocialBenefit { get; }
        public float MotionRecoveryBenefit { get; }
        public float TravelCost { get; }
        public float QueueCost { get; }
        public float ActiveTimeCost { get; }
        public float EffortCost { get; }
        public float ExpectedRiskCost { get; }
        public float MotionWorkCost { get; }
        public float ResourceCost { get; }
        public float SwitchCost { get; }

        public float ContextBenefit => ComfortBenefit + CleanlinessBenefit + PrivacyBenefit +
                                       SocialBenefit + MotionRecoveryBenefit;

        public float PlanCost => TravelCost + QueueCost + ActiveTimeCost + EffortCost +
                                 ExpectedRiskCost + MotionWorkCost + ResourceCost + SwitchCost;

        public float NetAdjustment => ContextBenefit - PlanCost;
    }

    /// <summary>完整方案的不可变估算结果，以及交给现有 Utility AI 的候选。</summary>
    public sealed class ResidentActionPlanEvaluation
    {
        internal ResidentActionPlanEvaluation(
            ResidentActionCandidate candidate,
            IReadOnlyList<ResidentActionStepEstimate> steps,
            ResidentActionPlanFeasibility feasibility,
            ActionPlanUtilityBreakdown utility)
        {
            Candidate = candidate;
            Steps = steps;
            Feasibility = feasibility;
            Utility = utility;
        }

        public ResidentActionCandidate Candidate { get; }
        public IReadOnlyList<ResidentActionStepEstimate> Steps { get; }
        public ResidentActionPlanFeasibility Feasibility { get; }
        public ActionPlanUtilityBreakdown Utility { get; }
    }

    /// <summary>
    /// 把完整行动步骤、环境质量、个人差异和风险折算为现有 Utility 候选。
    /// 所有因子使用命名的加减项；硬阻塞先过滤，概率抽样仍只由 <see cref="UtilityDecisionEngine"/> 负责。
    /// </summary>
    public sealed class ResidentActionPlanEvaluator
    {
        public ResidentActionPlanEvaluation Evaluate(
            ResidentActionPlanProposal proposal,
            ResidentDecisionCondition condition,
            ResidentActionPlanPolicy policy = null)
        {
            if (proposal == null) throw new ArgumentNullException(nameof(proposal));
            policy ??= new ResidentActionPlanPolicy();
            Validate(proposal, policy);

            ResidentActionStepEstimate[] steps = proposal.Steps == null
                ? throw new ArgumentException("行动步骤不能为 null。", nameof(proposal))
                : (ResidentActionStepEstimate[])proposal.Steps.Clone();

            float totalSeconds = 0f;
            float travelSeconds = 0f;
            float queueSeconds = 0f;
            float activeSeconds = 0f;
            float travelMeters = 0f;
            for (int i = 0; i < steps.Length; i++)
            {
                ResidentActionStepEstimate step = steps[i];
                totalSeconds += step.DurationSeconds;
                if (step.Kind == ResidentActionStepKind.Travel)
                {
                    travelSeconds += step.DurationSeconds;
                    travelMeters += step.DistanceMeters;
                }
                else if (step.Kind == ResidentActionStepKind.Queue)
                {
                    queueSeconds += step.DurationSeconds;
                }
                else
                {
                    activeSeconds += step.DurationSeconds;
                }
            }

            float delayScale = condition.TimeSensitivity *
                               (1f + proposal.DelayUrgency * policy.UrgentDelayCostMultiplier);
            float travelCost = travelSeconds * policy.TravelTimeCostPerSecond * delayScale;
            float queueCost = queueSeconds * policy.QueueTimeCostPerSecond * delayScale;
            float activeTimeCost = activeSeconds * policy.ActiveTimeCostPerSecond * delayScale;
            float effortCost = proposal.Effort * condition.EffortAversion * policy.EffortCostScale;
            float expectedRiskCost = proposal.FailureProbability * proposal.FailureSeverity *
                                     condition.RiskAversion * policy.ExpectedRiskCostScale;
            float motionWorkCost = condition.MotionSickness * proposal.WorkIntensity *
                                   policy.MotionSicknessWorkCostScale;
            float motionRecoveryBenefit = condition.MotionSickness * proposal.RestQuality *
                                          policy.MotionSicknessRestBenefitScale;

            var breakdown = new ActionPlanUtilityBreakdown(
                totalSeconds,
                travelSeconds,
                travelMeters,
                proposal.ComfortBenefit,
                proposal.CleanlinessBenefit,
                proposal.PrivacyBenefit,
                proposal.SocialBenefit,
                motionRecoveryBenefit,
                travelCost,
                queueCost,
                activeTimeCost,
                effortCost,
                expectedRiskCost,
                motionWorkCost,
                proposal.ResourceCost,
                proposal.SwitchCost);

            string blockReason = proposal.Feasibility.IsFeasible
                ? string.Empty
                : BuildBlockReason(proposal.Feasibility);
            var candidate = new ResidentActionCandidate(proposal.Id, proposal.IntentId, proposal.DisplayName)
            {
                DurationSeconds = totalSeconds,
                BaseUtility = proposal.BaseUtility + breakdown.ContextBenefit,
                WorkUrgency = proposal.WorkUrgency,
                PlayerPriority = proposal.PlayerPriority,
                DependencyValue = proposal.DependencyValue,
                SkillFit = proposal.SkillFit,
                PersonalAffinity = proposal.PersonalAffinity,
                WaitingAge = proposal.WaitingAge,
                ContinuityBonus = proposal.ContinuityBonus,
                TravelCost = breakdown.TravelCost,
                DurationCost = breakdown.QueueCost + breakdown.ActiveTimeCost +
                               breakdown.EffortCost + breakdown.MotionWorkCost,
                ResourceCost = proposal.ResourceCost,
                RiskCost = breakdown.ExpectedRiskCost,
                SwitchCost = proposal.SwitchCost,
                RiskTier = proposal.RiskTier,
                RiskPriority = proposal.RiskPriority,
                IsAvailable = proposal.Feasibility.IsFeasible,
                BlockReason = blockReason,
                NeedEffects = proposal.NeedEffects == null
                    ? Array.Empty<NeedEffect>()
                    : (NeedEffect[])proposal.NeedEffects.Clone(),
                ReservationKeys = proposal.ReservationKeys == null
                    ? Array.Empty<string>()
                    : (string[])proposal.ReservationKeys.Clone(),
            };

            var evaluation = new ResidentActionPlanEvaluation(
                candidate,
                Array.AsReadOnly(steps),
                proposal.Feasibility,
                breakdown);
            candidate.PlanEvaluation = evaluation;
            return evaluation;
        }

        private static string BuildBlockReason(ResidentActionPlanFeasibility feasibility)
        {
            if (!string.IsNullOrWhiteSpace(feasibility.Detail)) return feasibility.Detail;
            return feasibility.Reason switch
            {
                ResidentActionPlanBlockReason.TargetUnreachable => "目标不可达",
                ResidentActionPlanBlockReason.PrerequisiteUnavailable => "前置行动不可执行",
                ResidentActionPlanBlockReason.MissingResource => "缺少所需资源",
                ResidentActionPlanBlockReason.MissingCompatibleCarrier => "缺少兼容容器或搬运工具",
                ResidentActionPlanBlockReason.CarrierCapacityInsufficient => "搬运容量不足",
                ResidentActionPlanBlockReason.DestinationFull => "目标储存空间已满",
                ResidentActionPlanBlockReason.InteractionUnavailable => "交互位置当前不可用",
                ResidentActionPlanBlockReason.UnsafeEnvironment => "目标或路径环境当前不安全",
                ResidentActionPlanBlockReason.CapabilityUnavailable => "居民当前不具备执行能力",
                _ => "行动方案当前不可执行",
            };
        }

        private static void Validate(
            ResidentActionPlanProposal proposal,
            ResidentActionPlanPolicy policy)
        {
            ValidateNonNegative(proposal.BaseUtility, nameof(proposal.BaseUtility));
            ValidateNonNegative(proposal.ComfortBenefit, nameof(proposal.ComfortBenefit));
            ValidateNonNegative(proposal.CleanlinessBenefit, nameof(proposal.CleanlinessBenefit));
            ValidateNonNegative(proposal.PrivacyBenefit, nameof(proposal.PrivacyBenefit));
            ValidateNonNegative(proposal.SocialBenefit, nameof(proposal.SocialBenefit));
            ValidateNonNegative(proposal.WorkUrgency, nameof(proposal.WorkUrgency));
            ValidateNonNegative(proposal.PlayerPriority, nameof(proposal.PlayerPriority));
            ValidateNonNegative(proposal.DependencyValue, nameof(proposal.DependencyValue));
            ValidateNonNegative(proposal.SkillFit, nameof(proposal.SkillFit));
            ValidateNonNegative(proposal.PersonalAffinity, nameof(proposal.PersonalAffinity));
            ValidateNonNegative(proposal.WaitingAge, nameof(proposal.WaitingAge));
            ValidateNonNegative(proposal.ContinuityBonus, nameof(proposal.ContinuityBonus));
            ValidateNonNegative(proposal.ResourceCost, nameof(proposal.ResourceCost));
            ValidateNonNegative(proposal.SwitchCost, nameof(proposal.SwitchCost));
            if (!Enum.IsDefined(typeof(ResidentDecisionRiskTier), proposal.RiskTier))
                throw new ArgumentOutOfRangeException(nameof(proposal.RiskTier));
            ValidateNormalized(proposal.RiskPriority, nameof(proposal.RiskPriority));
            ValidateNormalized(proposal.DelayUrgency, nameof(proposal.DelayUrgency));
            ValidateNormalized(proposal.Effort, nameof(proposal.Effort));
            ValidateNormalized(proposal.WorkIntensity, nameof(proposal.WorkIntensity));
            ValidateNormalized(proposal.RestQuality, nameof(proposal.RestQuality));
            ValidateNormalized(proposal.FailureProbability, nameof(proposal.FailureProbability));
            ValidateNonNegative(proposal.FailureSeverity, nameof(proposal.FailureSeverity));

            ValidateNonNegative(policy.TravelTimeCostPerSecond, nameof(policy.TravelTimeCostPerSecond));
            ValidateNonNegative(policy.QueueTimeCostPerSecond, nameof(policy.QueueTimeCostPerSecond));
            ValidateNonNegative(policy.ActiveTimeCostPerSecond, nameof(policy.ActiveTimeCostPerSecond));
            ValidateNonNegative(policy.UrgentDelayCostMultiplier, nameof(policy.UrgentDelayCostMultiplier));
            ValidateNonNegative(policy.EffortCostScale, nameof(policy.EffortCostScale));
            ValidateNonNegative(policy.ExpectedRiskCostScale, nameof(policy.ExpectedRiskCostScale));
            ValidateNonNegative(policy.MotionSicknessWorkCostScale, nameof(policy.MotionSicknessWorkCostScale));
            ValidateNonNegative(policy.MotionSicknessRestBenefitScale, nameof(policy.MotionSicknessRestBenefitScale));
        }

        private static void ValidateNormalized(float value, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f || value > 1f)
                throw new ArgumentOutOfRangeException(parameterName, "归一化方案参数必须位于 [0, 1]。");
        }

        private static void ValidateNonNegative(float value, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                throw new ArgumentOutOfRangeException(parameterName, "方案参数必须是有限非负数。 ");
        }
    }
}
