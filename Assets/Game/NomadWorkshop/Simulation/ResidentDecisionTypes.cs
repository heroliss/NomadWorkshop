using System;
using System.Collections.Generic;

namespace Game.NomadWorkshop.Simulation
{
    /// <summary>Foundation Prototype 首批参与居民决策的归一化需求。</summary>
    public enum ResidentNeed
    {
        Thirst = 0,
        Hunger = 1,
        Fatigue = 2,
        Health = 3,
        /// <summary>
        /// 兴趣与有意义刺激的缺口。运行时真值使用正向 Entertainment，只有决策快照在边界转为缺口。
        /// </summary>
        Entertainment = 4,
        Bladder = 5,
        Stress = 6,
        Count = 7,
    }

    /// <summary>候选在一次决策中的筛选和选择终态，用于测试与开发诊断。</summary>
    public enum CandidateDecisionState
    {
        Ineligible,
        SupersededByIntent,
        OutsideRiskPool,
        OutsideShortlist,
        Shortlisted,
        Selected,
    }

    /// <summary>
    /// 候选能够缓解的最高后果层。层级描述当前情境下“不处理会发生什么”，而不是给行动类型
    /// 写死永久顺序：同一个故障在可隔离时可能是 <see cref="Severe"/>，即将危及生命时则是
    /// <see cref="Critical"/>。硬不可执行条件仍由行动方案可行性表达，不能靠提高层级绕过。
    /// </summary>
    public enum ResidentDecisionRiskTier
    {
        /// <summary>日常需求、工作和休闲，可在多个合理选择间保留自然随机。</summary>
        Routine = 0,

        /// <summary>即将失禁、严重口渴等可逆但需要尽快处理的生理或任务失败。</summary>
        Urgent = 1,

        /// <summary>可能造成重伤、显著病情恶化、火势扩散或不可逆资产损失。</summary>
        Severe = 2,

        /// <summary>短时间内可能死亡、窒息或造成车辆毁灭的立即安全威胁。</summary>
        Critical = 3,
    }

    /// <summary>
    /// 某项需求在当前决策时刻的状态。Deficit 为 0 表示满足、1 表示完全缺失；
    /// GrowthPerSecond 描述不采取恢复行动时的自然恶化速度。量表达到上限是否构成
    /// 紧急后果，由 <see cref="CanPromoteToUrgent"/> 显式区分。
    /// </summary>
    public readonly struct ResidentNeedState
    {
        public ResidentNeedState(
            ResidentNeed need,
            float deficit,
            float growthPerSecond,
            float importance = 1f,
            NeedPressureCurve pressureCurve = default,
            bool canPromoteToUrgent = true)
        {
            Need = need;
            Deficit = deficit;
            GrowthPerSecond = growthPerSecond;
            Importance = importance;
            PressureCurve = pressureCurve;
            CanPromoteToUrgent = canPromoteToUrgent;
        }

        public ResidentNeed Need { get; }
        public float Deficit { get; }
        public float GrowthPerSecond { get; }
        public float Importance { get; }
        /// <summary>
        /// 可选的需求专属响应曲线；default 保持既有全局曲线，便于逐项迁移而不改变旧平衡。
        /// </summary>
        public NeedPressureCurve PressureCurve { get; }

        /// <summary>
        /// 达到紧迫点时，能否把恢复该需求的候选至少提升到 Urgent 风险层。
        /// 娱乐等生活质量需求可以产生很高的日常 Utility，但不应压过即将失禁、
        /// 严重口渴等有明确后果的紧急生理需求。
        /// </summary>
        public bool CanPromoteToUrgent { get; }
    }

    /// <summary>候选行动完成时对一项需求产生的恢复量。</summary>
    public readonly struct NeedEffect
    {
        public NeedEffect(ResidentNeed need, float restore)
        {
            Need = need;
            Restore = restore;
        }

        public ResidentNeed Need { get; }
        public float Restore { get; }
    }

    /// <summary>
    /// 一次决策中的具体可执行目标，例如“在 A 饮水机喝水”或“维修动力核心”。
    /// IntentId 表示行为意图；多个同意图目标会先归并，避免设施数量放大该意图的抽中概率。
    /// </summary>
    public sealed class ResidentActionCandidate
    {
        public ResidentActionCandidate(string id, string intentId, string displayName)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("候选 id 不能为空。", nameof(id));
            if (string.IsNullOrWhiteSpace(intentId)) throw new ArgumentException("行动意图 id 不能为空。", nameof(intentId));
            if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("候选显示名不能为空。", nameof(displayName));

            Id = id;
            IntentId = intentId;
            DisplayName = displayName;
        }

        public string Id { get; }
        public string IntentId { get; }
        public string DisplayName { get; }
        public float DurationSeconds { get; set; }
        public float BaseUtility { get; set; }
        public float WorkUrgency { get; set; }
        public float PlayerPriority { get; set; }
        public float DependencyValue { get; set; }
        public float SkillFit { get; set; }
        public float PersonalAffinity { get; set; }
        public float WaitingAge { get; set; }
        public float ContinuityBonus { get; set; }
        public float TravelCost { get; set; }
        public float DurationCost { get; set; }
        public float ResourceCost { get; set; }
        public float RiskCost { get; set; }
        public float SwitchCost { get; set; }
        /// <summary>
        /// 本方案能够缓解的最高风险层。允许风险晋升的需求达到自己的紧急点时，
        /// 选择器会至少自动提升为 <see cref="ResidentDecisionRiskTier.Urgent"/>；
        /// 火灾等环境风险由候选生成器显式赋值。
        /// </summary>
        public ResidentDecisionRiskTier RiskTier { get; set; }

        /// <summary>
        /// 同一风险层内的情境紧迫度，范围 [0, 1]。它先用于带容差的风险比较，而不是直接混入
        /// Utility 总分；例如同为 Critical 的两个方案可以先比较预计失效时间，再比较行动成本。
        /// </summary>
        public float RiskPriority { get; set; }
        public bool IsAvailable { get; set; } = true;
        public string BlockReason { get; set; } = string.Empty;
        public NeedEffect[] NeedEffects { get; set; } = Array.Empty<NeedEffect>();
        public string[] ReservationKeys { get; set; } = Array.Empty<string>();

        /// <summary>
        /// 若候选由完整行动方案估算器生成，这里保留其步骤、硬阻塞与命名成本分项；
        /// 手写的早期 Spike 候选可以为空。
        /// </summary>
        public ResidentActionPlanEvaluation PlanEvaluation { get; internal set; }
    }

    /// <summary>一次居民决策的不可变输入快照。</summary>
    public sealed class ResidentDecisionContext
    {
        public ResidentDecisionContext(
            int worldSeed,
            ulong residentId,
            long decisionSequence,
            IReadOnlyList<ResidentNeedState> needs,
            IReadOnlyList<ResidentActionCandidate> candidates)
        {
            WorldSeed = worldSeed;
            ResidentId = residentId;
            DecisionSequence = decisionSequence;
            Needs = needs ?? throw new ArgumentNullException(nameof(needs));
            Candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
        }

        public int WorldSeed { get; }
        public ulong ResidentId { get; }
        public long DecisionSequence { get; }
        public IReadOnlyList<ResidentNeedState> Needs { get; }
        public IReadOnlyList<ResidentActionCandidate> Candidates { get; }
    }

    /// <summary>Utility AI 的可调选择政策；数值是原型假设，不是永久平衡契约。</summary>
    public sealed class UtilityDecisionPolicy
    {
        public float NeedPressureExponent { get; set; } = 2.4f;
        /// <summary>未配置专属曲线且允许风险晋升的需求，在达到该缺口后至少进入 Urgent 层。</summary>
        public float UrgentNeedDeficit { get; set; } = 0.82f;
        /// <summary>默认需求曲线越过紧迫点后的附加非线性压力。</summary>
        public float UrgentPressureBoost { get; set; } = 2.5f;
        /// <summary>
        /// 同一非日常风险层内允许作为“近似等价”继续比较 Utility 的紧迫度差值。
        /// 超出容差时更紧迫方案先验支配；容差内才允许时间、健康和资源等后果进行取舍。
        /// </summary>
        public float RiskPrioritySlack { get; set; } = 0.05f;
        public float RelativeShortlistThreshold { get; set; } = 0.62f;
        public int MaxShortlistCount { get; set; } = 4;
        public float NormalTemperature { get; set; } = 0.16f;
        /// <summary>Urgent / Severe 层的低温度；仍允许近似等价方案有少量变化。</summary>
        public float ElevatedRiskTemperature { get; set; } = 0.035f;
        /// <summary>Critical 层默认确定性选择；只在风险与 Utility 都相同时用稳定 id 打破平局。</summary>
        public float CriticalRiskTemperature { get; set; }
        public float MinimumUtility { get; set; } = 0.0001f;
    }

    /// <summary>候选总效用的分项快照，供开发面板解释选择依据。</summary>
    public readonly struct UtilityScoreBreakdown
    {
        public UtilityScoreBreakdown(
            float baseBenefit,
            float needBenefit,
            float workBenefit,
            float personalBenefit,
            float persistenceBenefit,
            float executionCost)
        {
            BaseBenefit = baseBenefit;
            NeedBenefit = needBenefit;
            WorkBenefit = workBenefit;
            PersonalBenefit = personalBenefit;
            PersistenceBenefit = persistenceBenefit;
            ExecutionCost = executionCost;
        }

        public float BaseBenefit { get; }
        public float NeedBenefit { get; }
        public float WorkBenefit { get; }
        public float PersonalBenefit { get; }
        public float PersistenceBenefit { get; }
        public float ExecutionCost { get; }
        public float Total => BaseBenefit + NeedBenefit + WorkBenefit + PersonalBenefit + PersistenceBenefit - ExecutionCost;
    }

    /// <summary>一个候选在本次决策中的完整可解释轨迹。</summary>
    public sealed class CandidateDecisionTrace
    {
        internal CandidateDecisionTrace(ResidentActionCandidate candidate)
        {
            Candidate = candidate;
        }

        public ResidentActionCandidate Candidate { get; }
        public UtilityScoreBreakdown Score { get; internal set; }
        public CandidateDecisionState State { get; internal set; }
        public ResidentDecisionRiskTier RiskTier { get; internal set; }
        public float RiskPriority { get; internal set; }
        public bool HasElevatedRisk => RiskTier != ResidentDecisionRiskTier.Routine;
        public double Probability { get; internal set; }
        public string Reason { get; internal set; } = string.Empty;
    }

    /// <summary>一次决策的选中项、确定性随机数和全部候选轨迹。</summary>
    public sealed class ResidentDecisionResult
    {
        internal ResidentDecisionResult(
            ResidentActionCandidate selected,
            double randomRoll,
            IReadOnlyList<CandidateDecisionTrace> traces)
        {
            Selected = selected;
            RandomRoll = randomRoll;
            Traces = traces;
        }

        public ResidentActionCandidate Selected { get; }
        public double RandomRoll { get; }
        public IReadOnlyList<CandidateDecisionTrace> Traces { get; }
        public bool HasSelection => Selected != null;
    }
}
