using System;
using NUnit.Framework;

namespace Game.NomadWorkshop.Simulation.Tests
{
    /// <summary>锁定设施风险积分不因帧率、暂停或存档分段而改变触发边界。</summary>
    public sealed class FailureHazardAccumulatorTests
    {
        [Test]
        public void SplitAndCombinedRisk_TriggerAtSameBoundary()
        {
            var split = new FailureHazardAccumulator(1000);
            var combined = new FailureHazardAccumulator(1000);

            Assert.IsFalse(split.Accumulate(250));
            Assert.IsFalse(split.Accumulate(749));
            Assert.IsTrue(split.Accumulate(1));
            Assert.IsTrue(combined.Accumulate(1000));

            Assert.IsTrue(split.IsTriggered);
            Assert.IsTrue(combined.IsTriggered);
            Assert.AreEqual(combined.AccumulatedMicroHazard, split.AccumulatedMicroHazard);
        }

        [Test]
        public void PauseAndPostTriggerUpdates_DoNotCreateExtraFailure()
        {
            var hazard = new FailureHazardAccumulator(100);

            Assert.IsFalse(hazard.Accumulate(0));
            Assert.AreEqual(100, hazard.RemainingMicroHazard);
            Assert.IsTrue(hazard.Accumulate(150));
            Assert.IsFalse(hazard.Accumulate(1000));

            Assert.AreEqual(100, hazard.AccumulatedMicroHazard);
            Assert.AreEqual(0, hazard.RemainingMicroHazard);
        }

        [Test]
        public void RestoredAccumulator_ContinuesSameThreshold()
        {
            var beforeSave = new FailureHazardAccumulator(1500);
            Assert.IsFalse(beforeSave.Accumulate(940));

            var restored = new FailureHazardAccumulator(
                beforeSave.ThresholdMicroHazard,
                beforeSave.AccumulatedMicroHazard);

            Assert.IsFalse(restored.Accumulate(559));
            Assert.IsTrue(restored.Accumulate(1));
            Assert.AreEqual(1500, restored.AccumulatedMicroHazard);
        }

        [Test]
        public void InvalidHazardStateOrNegativeInput_IsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new FailureHazardAccumulator(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FailureHazardAccumulator(10, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FailureHazardAccumulator(10, 11));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new FailureHazardAccumulator(10).Accumulate(-1));
        }
    }
}
