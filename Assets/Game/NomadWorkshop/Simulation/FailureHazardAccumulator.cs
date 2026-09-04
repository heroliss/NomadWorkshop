using System;

namespace Game.NomadWorkshop.Simulation
{
    /// <summary>
    /// 保存一次设施故障前的累计风险。调用方负责把老化、维护欠账、积尘、负载和环境折算为
    /// 非负的“已积分微风险”；本类型只保证分段更新、暂停和读取存档不会改变触发边界。
    /// </summary>
    public sealed class FailureHazardAccumulator
    {
        public FailureHazardAccumulator(
            long thresholdMicroHazard,
            long accumulatedMicroHazard = 0)
        {
            if (thresholdMicroHazard <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(thresholdMicroHazard),
                    "故障阈值必须大于零。");
            if (accumulatedMicroHazard < 0 || accumulatedMicroHazard > thresholdMicroHazard)
                throw new ArgumentOutOfRangeException(
                    nameof(accumulatedMicroHazard),
                    "累计风险必须位于零到故障阈值之间。");

            ThresholdMicroHazard = thresholdMicroHazard;
            AccumulatedMicroHazard = accumulatedMicroHazard;
        }

        /// <summary>本轮故障事件预先取样并需要随存档保存的阈值。</summary>
        public long ThresholdMicroHazard { get; }

        /// <summary>已经积分的风险；触发后钳制在阈值，不静默开始下一轮故障。</summary>
        public long AccumulatedMicroHazard { get; private set; }

        /// <summary>是否已经跨过当前故障阈值。</summary>
        public bool IsTriggered => AccumulatedMicroHazard >= ThresholdMicroHazard;

        /// <summary>在当前环境风险下距离触发还剩多少微风险。</summary>
        public long RemainingMicroHazard => ThresholdMicroHazard - AccumulatedMicroHazard;

        /// <summary>
        /// 累积一段已经积分的风险并返回是否在本次调用中首次触发。零表示暂停或本段无风险；
        /// 触发后继续调用不会制造第二次故障，必须由上层结算模式并创建新的累积器。
        /// </summary>
        public bool Accumulate(long integratedMicroHazard)
        {
            if (integratedMicroHazard < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(integratedMicroHazard),
                    "已积分风险不能为负数。");
            if (integratedMicroHazard == 0 || IsTriggered) return false;

            long remaining = RemainingMicroHazard;
            if (integratedMicroHazard >= remaining)
            {
                AccumulatedMicroHazard = ThresholdMicroHazard;
                return true;
            }

            AccumulatedMicroHazard = checked(AccumulatedMicroHazard + integratedMicroHazard);
            return false;
        }
    }
}
