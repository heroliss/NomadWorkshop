using System;
using System.Collections.Generic;

namespace Game.NomadWorkshop.Simulation
{
    /// <summary>
    /// 纯 C#、无 Unity 依赖的居民效用选择器。它只回答“下一步做什么”，不拥有寻路、动画、
    /// 资源结算或任务生命周期；调用方应在选中后原子预留目标，再交给行动执行器。
    /// </summary>
    public sealed class UtilityDecisionEngine
    {
        private const string DecisionRandomStreamId = "resident-decision";

        /// <summary>
        /// 在不可变快照上完成候选过滤、同意图归并、四级风险仲裁、短名单与确定性 Softmax 抽样。
        /// 配置错误抛出异常；没有达到最小效用的日常候选、也没有可行风险缓解方案时，
        /// 返回无选择结果并保留全部诊断轨迹。
        /// </summary>
        public ResidentDecisionResult Decide(ResidentDecisionContext context, UtilityDecisionPolicy policy = null)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            policy ??= new UtilityDecisionPolicy();
            ValidatePolicy(policy);

            ResidentNeedState[] needs = BuildNeedTable(context.Needs);
            var orderedCandidates = new List<ResidentActionCandidate>(context.Candidates.Count);
            for (int i = 0; i < context.Candidates.Count; i++)
            {
                ResidentActionCandidate candidate = context.Candidates[i]
                    ?? throw new ArgumentException($"候选列表第 {i} 项为 null。", nameof(context));
                orderedCandidates.Add(candidate);
            }
            orderedCandidates.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));
            EnsureUniqueCandidateIds(orderedCandidates);

            var traces = new List<CandidateDecisionTrace>(orderedCandidates.Count);
            for (int i = 0; i < orderedCandidates.Count; i++)
            {
                ResidentActionCandidate candidate = orderedCandidates[i];
                var trace = new CandidateDecisionTrace(candidate);
                traces.Add(trace);
                ValidateCandidateRisk(candidate);
                trace.RiskTier = candidate.RiskTier;
                trace.RiskPriority = candidate.RiskPriority;

                if (!candidate.IsAvailable)
                {
                    trace.State = CandidateDecisionState.Ineligible;
                    trace.Reason = string.IsNullOrWhiteSpace(candidate.BlockReason)
                        ? "候选当前不可执行"
                        : candidate.BlockReason;
                    continue;
                }

                trace.Score = ScoreCandidate(
                    candidate,
                    needs,
                    policy,
                    out bool urgentNeed,
                    out float urgentNeedPriority);
                trace.RiskTier = urgentNeed && candidate.RiskTier < ResidentDecisionRiskTier.Urgent
                    ? ResidentDecisionRiskTier.Urgent
                    : candidate.RiskTier;
                trace.RiskPriority = Math.Max(candidate.RiskPriority, urgentNeedPriority);
                // 可行的风险缓解方案即使代价很高，也必须作为“最小伤害”候选留下；
                // MinimumUtility 只阻止没有足够收益的日常行为。
                trace.State = trace.HasElevatedRisk || trace.Score.Total >= policy.MinimumUtility
                    ? CandidateDecisionState.Shortlisted
                    : CandidateDecisionState.OutsideShortlist;
                if (trace.State == CandidateDecisionState.OutsideShortlist)
                    trace.Reason = "总效用低于最小阈值";
            }

            List<CandidateDecisionTrace> intentWinners = SelectIntentWinners(traces, policy);
            if (intentWinners.Count == 0)
                return new ResidentDecisionResult(null, SampleDecisionRoll(context), traces);

            ResidentDecisionRiskTier highestRiskTier = ResidentDecisionRiskTier.Routine;
            for (int i = 0; i < intentWinners.Count; i++)
            {
                if (intentWinners[i].RiskTier > highestRiskTier)
                    highestRiskTier = intentWinners[i].RiskTier;
            }

            var selectionPool = new List<CandidateDecisionTrace>(intentWinners.Count);
            for (int i = 0; i < intentWinners.Count; i++)
            {
                CandidateDecisionTrace trace = intentWinners[i];
                if (trace.RiskTier == highestRiskTier)
                {
                    selectionPool.Add(trace);
                    continue;
                }

                trace.State = CandidateDecisionState.OutsideRiskPool;
                trace.Reason = $"存在更高风险层候选：{highestRiskTier}";
            }

            if (highestRiskTier != ResidentDecisionRiskTier.Routine)
            {
                float highestRiskPriority = 0f;
                for (int i = 0; i < selectionPool.Count; i++)
                    highestRiskPriority = Math.Max(
                        highestRiskPriority,
                        selectionPool[i].RiskPriority);
                float minimumComparablePriority = Math.Max(
                    0f,
                    highestRiskPriority - policy.RiskPrioritySlack);
                for (int i = selectionPool.Count - 1; i >= 0; i--)
                {
                    CandidateDecisionTrace trace = selectionPool[i];
                    if (trace.RiskPriority >= minimumComparablePriority) continue;
                    trace.State = CandidateDecisionState.OutsideRiskPool;
                    trace.Reason =
                        $"同为 {highestRiskTier}，紧迫度 {trace.RiskPriority:F3} 低于可比较边界 {minimumComparablePriority:F3}";
                    selectionPool.RemoveAt(i);
                }
            }

            selectionPool.Sort(CompareByUtilityThenId);
            List<CandidateDecisionTrace> shortlist = BuildShortlist(selectionPool, policy);
            double roll = SampleDecisionRoll(context);
            ResidentActionCandidate selected = SelectBySoftmax(
                shortlist,
                SelectTemperature(highestRiskTier, policy),
                roll);

            for (int i = 0; i < shortlist.Count; i++)
            {
                CandidateDecisionTrace trace = shortlist[i];
                if (ReferenceEquals(trace.Candidate, selected))
                {
                    trace.State = CandidateDecisionState.Selected;
                    trace.Reason = "命中确定性加权随机区间";
                }
                else
                {
                    trace.State = CandidateDecisionState.Shortlisted;
                    trace.Reason = "进入短名单但本次未抽中";
                }
            }

            return new ResidentDecisionResult(selected, roll, traces);
        }

        private static ResidentNeedState[] BuildNeedTable(IReadOnlyList<ResidentNeedState> source)
        {
            int count = (int)ResidentNeed.Count;
            var table = new ResidentNeedState[count];
            var seen = new bool[count];
            for (int i = 0; i < count; i++)
                table[i] = new ResidentNeedState((ResidentNeed)i, 0f, 0f, 1f);
            for (int i = 0; i < source.Count; i++)
            {
                ResidentNeedState state = source[i];
                int index = (int)state.Need;
                if (index < 0 || index >= count)
                    throw new ArgumentOutOfRangeException(nameof(source), state.Need, "未知的居民需求类型。");
                if (seen[index])
                    throw new ArgumentException($"需求 {state.Need} 在同一快照中重复。", nameof(source));
                if (state.GrowthPerSecond < 0f)
                    throw new ArgumentOutOfRangeException(nameof(source), "需求自然变化速度不能为负。恢复应由行动效果表达。");
                if (state.Importance < 0f)
                    throw new ArgumentOutOfRangeException(nameof(source), "需求重要度不能为负。");
                if (state.Deficit < 0f || state.Deficit > 1f)
                    throw new ArgumentOutOfRangeException(nameof(source), "归一化需求缺口必须在 [0, 1] 内。");

                table[index] = state;
                seen[index] = true;
            }
            return table;
        }

        private static void EnsureUniqueCandidateIds(IReadOnlyList<ResidentActionCandidate> candidates)
        {
            for (int i = 1; i < candidates.Count; i++)
            {
                if (string.Equals(candidates[i - 1].Id, candidates[i].Id, StringComparison.Ordinal))
                    throw new ArgumentException($"候选 id '{candidates[i].Id}' 重复。", nameof(candidates));
            }
        }

        private static UtilityScoreBreakdown ScoreCandidate(
            ResidentActionCandidate candidate,
            IReadOnlyList<ResidentNeedState> needs,
            UtilityDecisionPolicy policy,
            out bool urgentNeed,
            out float urgentNeedPriority)
        {
            if (candidate.DurationSeconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(candidate), candidate.DurationSeconds, "行动时长不能为负。");
            int needCount = (int)ResidentNeed.Count;
            NeedEffect[] effects = candidate.NeedEffects ?? Array.Empty<NeedEffect>();
            for (int i = 0; i < effects.Length; i++)
            {
                NeedEffect effect = effects[i];
                int index = (int)effect.Need;
                if (index < 0 || index >= needCount)
                    throw new ArgumentOutOfRangeException(nameof(candidate), effect.Need, "候选包含未知需求效果。");
                if (effect.Restore < 0f)
                    throw new ArgumentOutOfRangeException(nameof(candidate), effect.Restore, "恢复量不能为负。");
            }

            float needBenefit = 0f;
            urgentNeed = false;
            urgentNeedPriority = 0f;
            for (int i = 0; i < needCount; i++)
            {
                float restore = 0f;
                for (int effectIndex = 0; effectIndex < effects.Length; effectIndex++)
                {
                    if ((int)effects[effectIndex].Need == i) restore += effects[effectIndex].Restore;
                }
                if (restore <= 0f) continue;

                ResidentNeedState state = needs[i];
                float predicted = Clamp01(state.Deficit + state.GrowthPerSecond * candidate.DurationSeconds);
                float after = Clamp01(predicted - restore);
                float beforePressure = EvaluatePressure(predicted, state, policy);
                float afterPressure = EvaluatePressure(after, state, policy);
                needBenefit += Math.Max(0f, beforePressure - afterPressure) * state.Importance;
                bool isUrgent = state.CanPromoteToUrgent &&
                    (state.PressureCurve.IsConfigured
                        ? state.PressureCurve.IsUrgent(predicted)
                        : predicted >= policy.UrgentNeedDeficit);
                if (isUrgent)
                {
                    urgentNeed = true;
                    urgentNeedPriority = Math.Max(urgentNeedPriority, predicted);
                }
            }

            float workBenefit = candidate.WorkUrgency + candidate.PlayerPriority + candidate.DependencyValue;
            float personalBenefit = candidate.SkillFit +
                                    candidate.PersonalAffinity *
                                    policy.PersonalAffinityUtilityScale;
            float persistenceBenefit = candidate.WaitingAge + candidate.ContinuityBonus;
            float executionCost = candidate.TravelCost + candidate.DurationCost + candidate.ResourceCost
                                  + candidate.RiskCost + candidate.SwitchCost;
            return new UtilityScoreBreakdown(
                candidate.BaseUtility,
                needBenefit,
                workBenefit,
                personalBenefit,
                persistenceBenefit,
                executionCost);
        }

        private static float EvaluatePressure(
            float deficit,
            in ResidentNeedState state,
            UtilityDecisionPolicy policy)
        {
            if (state.PressureCurve.IsConfigured)
                return state.PressureCurve.EvaluatePressure(deficit);

            float value = Clamp01(deficit);
            double pressure = Math.Pow(value, policy.NeedPressureExponent);
            if (value > policy.UrgentNeedDeficit)
            {
                float urgentRange = 1f - policy.UrgentNeedDeficit;
                float normalized = (value - policy.UrgentNeedDeficit) / urgentRange;
                pressure += policy.UrgentPressureBoost * normalized * normalized;
            }
            return (float)pressure;
        }

        private static List<CandidateDecisionTrace> SelectIntentWinners(
            IReadOnlyList<CandidateDecisionTrace> traces,
            UtilityDecisionPolicy policy)
        {
            var winnersByIntent = new Dictionary<string, CandidateDecisionTrace>(StringComparer.Ordinal);
            for (int i = 0; i < traces.Count; i++)
            {
                CandidateDecisionTrace candidate = traces[i];
                if (candidate.State != CandidateDecisionState.Shortlisted) continue;

                if (!winnersByIntent.TryGetValue(candidate.Candidate.IntentId, out CandidateDecisionTrace current))
                {
                    winnersByIntent.Add(candidate.Candidate.IntentId, candidate);
                    continue;
                }

                if (IsBetter(candidate, current, policy.RiskPrioritySlack))
                {
                    current.State = CandidateDecisionState.SupersededByIntent;
                    current.Reason = $"同意图存在更优目标：{candidate.Candidate.DisplayName}";
                    winnersByIntent[candidate.Candidate.IntentId] = candidate;
                }
                else
                {
                    candidate.State = CandidateDecisionState.SupersededByIntent;
                    candidate.Reason = $"同意图存在更优目标：{current.Candidate.DisplayName}";
                }
            }

            var winners = new List<CandidateDecisionTrace>(winnersByIntent.Count);
            foreach (CandidateDecisionTrace winner in winnersByIntent.Values) winners.Add(winner);
            return winners;
        }

        private static List<CandidateDecisionTrace> BuildShortlist(
            IReadOnlyList<CandidateDecisionTrace> orderedPool,
            UtilityDecisionPolicy policy)
        {
            var shortlist = new List<CandidateDecisionTrace>(Math.Min(policy.MaxShortlistCount, orderedPool.Count));
            float best = orderedPool[0].Score.Total;
            float allowedDrop = Math.Abs(best) * (1f - policy.RelativeShortlistThreshold);
            float threshold = best - allowedDrop;
            for (int i = 0; i < orderedPool.Count; i++)
            {
                CandidateDecisionTrace trace = orderedPool[i];
                if (shortlist.Count < policy.MaxShortlistCount && trace.Score.Total >= threshold)
                {
                    shortlist.Add(trace);
                    continue;
                }

                trace.State = CandidateDecisionState.OutsideShortlist;
                trace.Reason = shortlist.Count >= policy.MaxShortlistCount
                    ? "超过短名单数量上限"
                    : "效用低于本次相对阈值";
            }
            return shortlist;
        }

        private static ResidentActionCandidate SelectBySoftmax(
            IReadOnlyList<CandidateDecisionTrace> shortlist,
            float temperature,
            double roll)
        {
            if (shortlist.Count == 0) return null;
            if (shortlist.Count == 1 || temperature <= 0.000001f)
            {
                shortlist[0].Probability = 1d;
                return shortlist[0].Candidate;
            }

            double best = shortlist[0].Score.Total;
            var weights = new double[shortlist.Count];
            double sum = 0d;
            for (int i = 0; i < shortlist.Count; i++)
            {
                double weight = Math.Exp((shortlist[i].Score.Total - best) / temperature);
                weights[i] = weight;
                sum += weight;
            }

            double cumulative = 0d;
            for (int i = 0; i < shortlist.Count; i++)
            {
                double probability = weights[i] / sum;
                shortlist[i].Probability = probability;
                cumulative += probability;
                if (roll < cumulative) return shortlist[i].Candidate;
            }

            return shortlist[shortlist.Count - 1].Candidate;
        }

        private static int CompareByUtilityThenId(CandidateDecisionTrace left, CandidateDecisionTrace right)
        {
            int score = right.Score.Total.CompareTo(left.Score.Total);
            return score != 0 ? score : string.CompareOrdinal(left.Candidate.Id, right.Candidate.Id);
        }

        private static bool IsBetter(
            CandidateDecisionTrace candidate,
            CandidateDecisionTrace current,
            float riskPrioritySlack)
        {
            int tier = candidate.RiskTier.CompareTo(current.RiskTier);
            if (tier != 0) return tier > 0;
            if (candidate.RiskTier != ResidentDecisionRiskTier.Routine)
            {
                float priorityDifference = candidate.RiskPriority - current.RiskPriority;
                if (Math.Abs(priorityDifference) > riskPrioritySlack)
                    return priorityDifference > 0f;
            }
            int score = candidate.Score.Total.CompareTo(current.Score.Total);
            return score > 0 || score == 0 && string.CompareOrdinal(candidate.Candidate.Id, current.Candidate.Id) < 0;
        }

        private static float SelectTemperature(
            ResidentDecisionRiskTier riskTier,
            UtilityDecisionPolicy policy)
        {
            return riskTier switch
            {
                ResidentDecisionRiskTier.Critical => policy.CriticalRiskTemperature,
                ResidentDecisionRiskTier.Severe or ResidentDecisionRiskTier.Urgent =>
                    policy.ElevatedRiskTemperature,
                _ => policy.NormalTemperature,
            };
        }

        private static void ValidatePolicy(UtilityDecisionPolicy policy)
        {
            if (policy.NeedPressureExponent <= 0f)
                throw new ArgumentOutOfRangeException(nameof(policy), "需求压力指数必须大于零。");
            if (!float.IsFinite(policy.PersonalAffinityUtilityScale) ||
                policy.PersonalAffinityUtilityScale < 0f)
                throw new ArgumentOutOfRangeException(
                    nameof(policy),
                    "个人偏好效用比例必须是非负有限值。");
            if (policy.UrgentNeedDeficit <= 0f || policy.UrgentNeedDeficit >= 1f)
                throw new ArgumentOutOfRangeException(nameof(policy), "紧迫需求阈值必须在 (0, 1) 内。");
            if (policy.UrgentPressureBoost < 0f)
                throw new ArgumentOutOfRangeException(nameof(policy), "紧迫压力增益不能为负。");
            if (policy.RiskPrioritySlack < 0f || policy.RiskPrioritySlack > 1f)
                throw new ArgumentOutOfRangeException(nameof(policy), "风险紧迫度容差必须在 [0, 1] 内。");
            if (policy.RelativeShortlistThreshold < 0f || policy.RelativeShortlistThreshold > 1f)
                throw new ArgumentOutOfRangeException(nameof(policy), "短名单相对阈值必须在 [0, 1] 内。");
            if (policy.MaxShortlistCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(policy), "短名单数量必须大于零。");
            if (policy.NormalTemperature < 0f || policy.ElevatedRiskTemperature < 0f ||
                policy.CriticalRiskTemperature < 0f)
                throw new ArgumentOutOfRangeException(nameof(policy), "随机温度不能为负。");
        }

        private static void ValidateCandidateRisk(ResidentActionCandidate candidate)
        {
            if (!Enum.IsDefined(typeof(ResidentDecisionRiskTier), candidate.RiskTier))
                throw new ArgumentOutOfRangeException(
                    nameof(candidate),
                    candidate.RiskTier,
                    "未知的居民决策风险层。");
            if (!float.IsFinite(candidate.RiskPriority) ||
                candidate.RiskPriority < 0f || candidate.RiskPriority > 1f)
                throw new ArgumentOutOfRangeException(
                    nameof(candidate),
                    candidate.RiskPriority,
                    "风险紧迫度必须位于 [0, 1]。");
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }

        private static double SampleDecisionRoll(ResidentDecisionContext context) =>
            DeterministicRandom.Sample01(
                context.WorldSeed,
                context.ResidentId,
                DecisionRandomStreamId,
                context.DecisionSequence);
    }
}
