using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Game.NomadWorkshop.Simulation.Tests
{
    /// <summary>锁定领域随机隔离、显式样本槽和只保存事件游标即可恢复的契约。</summary>
    public sealed class DeterministicRandomTests
    {
        [Test]
        public void SameKey_ReproducesBitExactSample()
        {
            ulong first = DeterministicRandom.SampleUInt64(
                94217,
                0xADA01UL,
                "resident-meal",
                17,
                3);
            ulong second = DeterministicRandom.SampleUInt64(
                94217,
                0xADA01UL,
                "resident-meal",
                17,
                3);

            Assert.AreEqual(first, second);
            Assert.AreEqual(
                0x56C167B0CB600BDDUL,
                first,
                "随机算法或持久键改变会重写已有存档轨迹；如需调整，应显式设计迁移策略。");
            Assert.AreEqual(
                DeterministicRandom.Sample01(94217, 0xADA01UL, "resident-meal", 17, 3),
                DeterministicRandom.Sample01(94217, 0xADA01UL, "resident-meal", 17, 3));
        }

        [Test]
        public void StreamId_UsesSameTrimmedNormalizationAsStreamCursor()
        {
            double direct = DeterministicRandom.Sample01(5, 8, " meal ", 0);
            DeterministicRandomEvent fromStream =
                new DeterministicRandomStream(5, 8, " meal ").BeginEvent();

            Assert.AreEqual(direct, fromStream.Sample01());
            Assert.AreEqual("meal", fromStream.StreamId);
        }

        [Test]
        public void DomainSequenceAndSlot_ProduceIndependentNamedSamples()
        {
            ulong baseline = DeterministicRandom.SampleUInt64(7, 9, "meal", 2, 0);
            var samples = new HashSet<ulong>
            {
                baseline,
                DeterministicRandom.SampleUInt64(7, 9, "illness", 2, 0),
                DeterministicRandom.SampleUInt64(7, 9, "meal", 3, 0),
                DeterministicRandom.SampleUInt64(7, 9, "meal", 2, 1),
                DeterministicRandom.SampleUInt64(8, 9, "meal", 2, 0),
                DeterministicRandom.SampleUInt64(7, 10, "meal", 2, 0),
            };

            Assert.AreEqual(6, samples.Count);
        }

        [Test]
        public void EventSlots_AreRepeatableAndDoNotAdvanceStream()
        {
            var stream = new DeterministicRandomStream(17, 0xADA01UL, "facility-failure");
            DeterministicRandomEvent firstEvent = stream.BeginEvent();

            double modeBefore = firstEvent.Sample01(1);
            _ = firstEvent.Sample01(7);
            double modeAfter = firstEvent.Sample01(1);

            Assert.AreEqual(modeBefore, modeAfter);
            Assert.AreEqual(1, stream.NextEventSequence);
        }

        [Test]
        public void SavedNextSequence_RestoresFollowingEvent()
        {
            var original = new DeterministicRandomStream(17, 0xADA01UL, "facility-failure");
            _ = original.BeginEvent();
            long savedNextSequence = original.NextEventSequence;
            DeterministicRandomEvent expected = original.BeginEvent();

            var restored = new DeterministicRandomStream(
                17,
                0xADA01UL,
                "facility-failure",
                savedNextSequence);
            DeterministicRandomEvent actual = restored.BeginEvent();

            Assert.AreEqual(expected.Sequence, actual.Sequence);
            Assert.AreEqual(expected.Sample01(0), actual.Sample01(0));
            Assert.AreEqual(expected.Sample01(4), actual.Sample01(4));
        }

        [Test]
        public void ExponentialHazardThreshold_IsPositiveAndRepeatable()
        {
            var stream = new DeterministicRandomStream(31, 77, "facility-failure");
            DeterministicRandomEvent failure = stream.BeginEvent();

            long first = failure.SampleExponentialHazardThreshold();
            long second = failure.SampleExponentialHazardThreshold();

            Assert.Greater(first, 0);
            Assert.AreEqual(first, second);
        }

        [Test]
        public void InvalidPersistentKeys_AreRejected()
        {
            Assert.Throws<ArgumentException>(() =>
                DeterministicRandom.Sample01(1, 2, " ", 0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                DeterministicRandom.Sample01(1, 2, "meal", -1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                DeterministicRandom.Sample01(1, 2, "meal", 0, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new DeterministicRandomStream(1, 2, "meal", -1));
        }
    }
}
