using System;

namespace Game.NomadWorkshop.Simulation
{
    /// <summary>
    /// 把一项归一化缺口转换为平滑、非线性的行动压力。低于 <see cref="OnsetDeficit"/>
    /// 时不产生驱动力，到达 <see cref="UrgentDeficit"/> 时成为必然需要处理的紧急需求；
    /// 中间使用端点斜率为零的曲线，避免阈值附近优先级突然跳变。
    /// </summary>
    public readonly struct NeedPressureCurve
    {
        private readonly bool _configured;

        public NeedPressureCurve(
            float onsetDeficit,
            float urgentDeficit,
            float responseExponent = 2f,
            float maximumPressure = 3.5f)
        {
            ValidateNormalized(onsetDeficit, nameof(onsetDeficit));
            ValidateNormalized(urgentDeficit, nameof(urgentDeficit));
            if (urgentDeficit <= onsetDeficit)
                throw new ArgumentOutOfRangeException(
                    nameof(urgentDeficit),
                    "紧急缺口必须大于开始响应的缺口。 ");
            if (!float.IsFinite(responseExponent) || responseExponent < 1f)
                throw new ArgumentOutOfRangeException(
                    nameof(responseExponent),
                    "为保持开始点斜率平滑，响应指数必须是至少为 1 的有限数。 ");
            if (!float.IsFinite(maximumPressure) || maximumPressure < 1f)
                throw new ArgumentOutOfRangeException(
                    nameof(maximumPressure),
                    "最大压力必须至少为 1。 ");

            OnsetDeficit = onsetDeficit;
            UrgentDeficit = urgentDeficit;
            ResponseExponent = responseExponent;
            MaximumPressure = maximumPressure;
            _configured = true;
        }

        /// <summary>低于或等于该缺口时，本需求不会单独推动行动。</summary>
        public float OnsetDeficit { get; }

        /// <summary>达到该缺口时，行动机会为 100%，并进入 Utility 的紧急候选池。</summary>
        public float UrgentDeficit { get; }

        /// <summary>大于 1 会压低中段早期响应，使需求先缓慢、后快速上升。</summary>
        public float ResponseExponent { get; }

        /// <summary>
        /// 紧急点低于 1 时，缺口达到 1 的压力上限；大于 1 的部分用于压过非紧急候选。
        /// </summary>
        public float MaximumPressure { get; }

        /// <summary>default 值表示沿用 Utility 全局旧曲线，而不是一条有效的自定义曲线。</summary>
        public bool IsConfigured => _configured;

        /// <summary>
        /// 返回非负压力：开始点为 0、紧急点为 1、满缺口时为
        /// <see cref="MaximumPressure"/>。曲线及一阶导数在两个接缝处连续。
        /// </summary>
        public float EvaluatePressure(float deficit)
        {
            EnsureConfigured();
            ValidateNormalized(deficit, nameof(deficit));
            if (deficit <= OnsetDeficit) return 0f;

            if (deficit <= UrgentDeficit)
            {
                float normalized = (deficit - OnsetDeficit) /
                                   (UrgentDeficit - OnsetDeficit);
                return (float)Math.Pow(SmootherStep(normalized), ResponseExponent);
            }

            if (UrgentDeficit >= 1f) return 1f;
            float urgentRange = (deficit - UrgentDeficit) / (1f - UrgentDeficit);
            return 1f + (MaximumPressure - 1f) * SmootherStep(urgentRange);
        }

        /// <summary>
        /// 把当前压力解释为“本次重大行动边界是否生成该需求意图”的概率。
        /// 这不是每帧掷骰；调用方只应在行动完成、失效或低频复评等决策边界采样。
        /// </summary>
        public float EvaluateOpportunityProbability(float deficit) =>
            Math.Min(1f, EvaluatePressure(deficit));

        /// <summary>判断当前缺口是否已经到达该需求自己的紧急边界。</summary>
        public bool IsUrgent(float deficit)
        {
            EnsureConfigured();
            ValidateNormalized(deficit, nameof(deficit));
            return deficit >= UrgentDeficit;
        }

        /// <summary>使用外部提供的确定性样本判断本次是否生成需求意图。</summary>
        public bool ShouldOfferAction(float deficit, double roll)
        {
            if (double.IsNaN(roll) || double.IsInfinity(roll) || roll < 0d || roll >= 1d)
                throw new ArgumentOutOfRangeException(nameof(roll), "概率样本必须位于 [0, 1)。");
            return roll < EvaluateOpportunityProbability(deficit);
        }

        private void EnsureConfigured()
        {
            if (!_configured)
                throw new InvalidOperationException("default NeedPressureCurve 没有可计算的参数。 ");
        }

        private static float SmootherStep(float value)
        {
            float t = Math.Max(0f, Math.Min(1f, value));
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        private static void ValidateNormalized(float value, string parameterName)
        {
            if (!float.IsFinite(value) || value < 0f || value > 1f)
                throw new ArgumentOutOfRangeException(parameterName, "归一化缺口必须位于 [0, 1]。 ");
        }
    }
}
