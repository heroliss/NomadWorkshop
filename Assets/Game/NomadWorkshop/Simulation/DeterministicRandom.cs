using System;

namespace Game.NomadWorkshop.Simulation
{
    /// <summary>
    /// 与平台运行时哈希无关的确定性随机采样入口。每个结果由世界、对象、领域、事件序号和样本槽共同决定；
    /// 新增某领域的采样不会消耗另一领域的随机状态，也不会改变已经命名的样本槽。
    /// </summary>
    public static class DeterministicRandom
    {
        /// <summary>
        /// 返回稳定的 64 位样本。<paramref name="streamId"/> 和 <paramref name="sampleSlot"/>
        /// 是持久契约：重命名会有意改变后续随机轨迹，调用方应像对待存档字段一样审查。
        /// </summary>
        public static ulong SampleUInt64(
            int worldSeed,
            ulong ownerId,
            string streamId,
            long eventSequence,
            int sampleSlot)
        {
            ValidateKey(streamId, eventSequence, sampleSlot);
            string normalizedStreamId = streamId.Trim();

            unchecked
            {
                ulong value = (uint)worldSeed;
                value ^= Mix(ownerId + 0x9E3779B97F4A7C15UL);
                value ^= Mix(HashStableString(normalizedStreamId) + 0xD1B54A32D192ED03UL);
                value ^= Mix((ulong)eventSequence + 0x94D049BB133111EBUL);
                value ^= Mix((uint)sampleSlot + 0xBF58476D1CE4E5B9UL);
                return Mix(value);
            }
        }

        /// <summary>返回位于 [0, 1) 的 53 位确定性样本。</summary>
        public static double Sample01(
            int worldSeed,
            ulong ownerId,
            string streamId,
            long eventSequence,
            int sampleSlot = 0)
        {
            ulong value = SampleUInt64(
                worldSeed,
                ownerId,
                streamId,
                eventSequence,
                sampleSlot);
            return (value >> 11) * (1d / 9007199254740992d);
        }

        /// <summary>
        /// 把稳定字符串映射为 64 位身份键。它使用显式 UTF-16 字节顺序和 FNV-1a，
        /// 不依赖进程随机化的 <see cref="string.GetHashCode()"/>。
        /// </summary>
        public static ulong HashStableString(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("参与随机身份的字符串不能为空。", nameof(value));

            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offset;
            unchecked
            {
                for (int i = 0; i < value.Length; i++)
                {
                    char character = value[i];
                    hash ^= (byte)character;
                    hash *= prime;
                    hash ^= (byte)(character >> 8);
                    hash *= prime;
                }
            }
            return hash;
        }

        private static void ValidateKey(string streamId, long eventSequence, int sampleSlot)
        {
            if (string.IsNullOrWhiteSpace(streamId))
                throw new ArgumentException("随机流 id 不能为空。", nameof(streamId));
            if (eventSequence < 0)
                throw new ArgumentOutOfRangeException(nameof(eventSequence), "事件序号不能为负数。");
            if (sampleSlot < 0)
                throw new ArgumentOutOfRangeException(nameof(sampleSlot), "样本槽不能为负数。");
        }

        private static ulong Mix(ulong value)
        {
            unchecked
            {
                value += 0x9E3779B97F4A7C15UL;
                value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
                value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
                return value ^ (value >> 31);
            }
        }
    }

    /// <summary>
    /// 单线程模拟使用的领域随机流游标。只递增事件序号；同一事件内的每个结果通过显式样本槽读取，
    /// 因而调试器多看一次样本或某个分支增加样本槽都不会偷偷移动下一事件。
    /// </summary>
    public sealed class DeterministicRandomStream
    {
        public DeterministicRandomStream(
            int worldSeed,
            ulong ownerId,
            string streamId,
            long nextEventSequence = 0)
        {
            if (string.IsNullOrWhiteSpace(streamId))
                throw new ArgumentException("随机流 id 不能为空。", nameof(streamId));
            if (nextEventSequence < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(nextEventSequence),
                    "下一个事件序号不能为负数。");

            WorldSeed = worldSeed;
            OwnerId = ownerId;
            StreamId = streamId.Trim();
            NextEventSequence = nextEventSequence;
        }

        /// <summary>创建该流时锁定的世界 Seed。</summary>
        public int WorldSeed { get; }

        /// <summary>拥有该领域流的稳定对象键。</summary>
        public ulong OwnerId { get; }

        /// <summary>区分决策、进食、疾病、设施故障等领域的稳定 id。</summary>
        public string StreamId { get; }

        /// <summary>下一次事件将使用的序号；它是存档需要保存的最小游标。</summary>
        public long NextEventSequence { get; private set; }

        /// <summary>
        /// 承诺一个新事件并立即推进游标。调用方应把返回事件和推进后的
        /// <see cref="NextEventSequence"/> 一起纳入行动检查点，取消后也不回退已经承诺的序号。
        /// </summary>
        public DeterministicRandomEvent BeginEvent()
        {
            if (NextEventSequence == long.MaxValue)
                throw new InvalidOperationException($"随机流 {StreamId} 的事件序号已经耗尽。");

            long sequence = NextEventSequence;
            NextEventSequence++;
            return new DeterministicRandomEvent(WorldSeed, OwnerId, StreamId, sequence);
        }
    }

    /// <summary>已经承诺事件的不可变采样上下文；可安全反复读取同一命名样本槽。</summary>
    public readonly struct DeterministicRandomEvent
    {
        internal DeterministicRandomEvent(
            int worldSeed,
            ulong ownerId,
            string streamId,
            long sequence)
        {
            WorldSeed = worldSeed;
            OwnerId = ownerId;
            StreamId = streamId;
            Sequence = sequence;
        }

        /// <summary>该事件所属世界的稳定 Seed。</summary>
        public int WorldSeed { get; }

        /// <summary>拥有该领域流的稳定对象键。</summary>
        public ulong OwnerId { get; }

        /// <summary>该事件所属领域的稳定流 id。</summary>
        public string StreamId { get; }

        /// <summary>该领域流内已经承诺的事件序号。</summary>
        public long Sequence { get; }

        /// <summary>返回该事件指定样本槽中位于 [0, 1) 的稳定样本。</summary>
        public double Sample01(int sampleSlot = 0) => DeterministicRandom.Sample01(
            WorldSeed,
            OwnerId,
            StreamId,
            Sequence,
            sampleSlot);

        /// <summary>
        /// 为累计风险模型生成指数分布阈值 `-ln(1-u)`，并量化为正整数微风险单位。
        /// 阈值应立即写入设施状态；后续只累计风险，不应在每次更新时重新取样。
        /// </summary>
        public long SampleExponentialHazardThreshold(
            int sampleSlot = 0,
            long microHazardScale = 1_000_000L)
        {
            if (microHazardScale <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(microHazardScale),
                    "微风险量化尺度必须大于零。");

            double threshold = -Math.Log(1d - Sample01(sampleSlot)) * microHazardScale;
            if (threshold >= long.MaxValue)
                throw new OverflowException("故障风险阈值超过 Int64 可保存范围。");
            return Math.Max(1L, (long)Math.Round(threshold, MidpointRounding.AwayFromZero));
        }
    }
}
