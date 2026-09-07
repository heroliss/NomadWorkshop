using System;
using NUnit.Framework;

namespace Game.NomadWorkshop.Simulation.Tests
{
    /// <summary>锁定生活日与压缩气候年只从同一模拟毫秒投影，不产生两套漂移时钟。</summary>
    public sealed class NomadSimulationTimeTests
    {
        [Test]
        public void Clock_CarriesSubMillisecondRemainderAcrossFrames()
        {
            var partitioned = new NomadSimulationClock();
            long committedMilliseconds = 0L;
            for (var i = 0; i < 2500; i++)
            {
                partitioned.AccumulateFrame(0.0004f, 1f);
                committedMilliseconds += Drain(partitioned);
            }

            var singleStep = new NomadSimulationClock();
            singleStep.AccumulateFrame(1f, 1f);
            long singleDelta = Drain(singleStep);

            Assert.AreEqual(1000L, committedMilliseconds);
            Assert.AreEqual(singleDelta, committedMilliseconds);
            Assert.AreEqual(singleStep.SimulationTick, partitioned.SimulationTick);
        }

        [Test]
        public void Clock_AppliesSpeedAndRestoresSavedTickWithoutFrameRemainder()
        {
            var clock = new NomadSimulationClock();

            clock.AccumulateFrame(0.5f, 2f);
            Assert.AreEqual(1000L, Drain(clock));
            clock.AccumulateFrame(0.0015f, 0.5f);
            Assert.IsFalse(clock.TryAdvanceStep());
            Assert.AreEqual(1000L, clock.SimulationTick);

            clock.Restore(42_500L);

            Assert.AreEqual(42_500L, clock.SimulationTick);
            clock.AccumulateFrame(0.0005f, 1f);
            Assert.IsFalse(clock.TryAdvanceStep());
            Assert.AreEqual(42_500L, clock.SimulationTick);
        }

        [Test]
        public void Clock_ExactHarnessStepPreservesExistingFractionalRemainder()
        {
            var clock = new NomadSimulationClock(stepMilliseconds: 10);

            clock.AccumulateFrame(0.0095f, 1f);
            Assert.IsFalse(clock.TryAdvanceStep());
            for (var step = 0; step < 25; step++) clock.AdvanceHarnessStep();
            Assert.AreEqual(250L, clock.SimulationTick);
            clock.AccumulateFrame(0.0005f, 1f);
            Assert.AreEqual(
                10L,
                Drain(clock),
                "精确快进不应清除实时入口此前保留的不足一步预算。 ");
            Assert.AreEqual(260L, clock.SimulationTick);
        }

        [Test]
        public void FixedSteps_LongFrameBudgetIsBoundedWithoutLosingOrRescalingDebt()
        {
            var clock = new NomadSimulationClock(stepMilliseconds: 10);
            clock.AccumulateFrame(1f, 16f);
            Assert.That(clock.SimulationTick, Is.Zero, "入账不能先让世界时间跳到帧末。 ");
            Assert.That(Drain(clock, maximumSteps: 100), Is.EqualTo(1000L));
            Assert.That(clock.PendingMilliseconds, Is.EqualTo(15_000m));

            clock.AccumulateFrame(0.5f, 0.5f);
            Assert.That(Drain(clock), Is.EqualTo(15_250L));
            Assert.That(clock.SimulationTick, Is.EqualTo(16_250L));
            Assert.That(clock.PendingMilliseconds, Is.Zero);

            clock.AccumulateFrame(20f, 1f);
            clock.Restore(42_503L);
            Assert.That(clock.PendingMilliseconds, Is.Zero);
            Assert.IsFalse(clock.TryAdvanceStep());
            clock.AdvanceHarnessStep();
            Assert.That(clock.SimulationTick, Is.EqualTo(42_513L),
                "恢复旧毫秒 Tick 不允许先向整步取整或补执行旧世界欠账。 ");
        }

        [TestCase(0.001f, 1000, 1f)]
        [TestCase(0.05f, 20, 1f)]
        [TestCase(0.25f, 1, 4f)]
        [TestCase(0.0625f, 1, 16f)]
        public void FixedSteps_FramePartitionAndSpeedCommitIdenticalTicks(
            float frameSeconds, int frames, float speed)
        {
            var clock = new NomadSimulationClock(stepMilliseconds: 10);
            long expectedTick = 0L;
            for (var frame = 0; frame < frames; frame++)
            {
                clock.AccumulateFrame(frameSeconds, speed);
                while (clock.TryAdvanceStep())
                {
                    expectedTick += 10L;
                    Assert.That(clock.SimulationTick, Is.EqualTo(expectedTick));
                }
            }
            Assert.That(expectedTick, Is.EqualTo(1000L));
            Assert.That(clock.PendingMilliseconds, Is.Zero);
        }

        [Test]
        public void FixedSteps_OverflowAndInvalidInputDoNotPartiallyCommit()
        {
            var clock = new NomadSimulationClock(long.MaxValue - 10L, 10);
            clock.AccumulateFrame(0.005f, 1f);
            Assert.Throws<OverflowException>(() => clock.AccumulateFrame(0.006f, 1f));
            Assert.Throws<OverflowException>(() => clock.AdvanceHarnessStep());
            Assert.Throws<ArgumentOutOfRangeException>(() => clock.AccumulateFrame(float.NaN, 1f));
            Assert.That(clock.SimulationTick, Is.EqualTo(long.MaxValue - 10L));
            Assert.That(clock.PendingMilliseconds, Is.EqualTo(5m));
            clock.AccumulateFrame(0.005f, 1f);
            Assert.IsTrue(clock.TryAdvanceStep());
            Assert.That(clock.SimulationTick, Is.EqualTo(long.MaxValue));
        }

        [Test]
        public void DefaultPolicy_MapsOneLifeDayToOneClimateWeek()
        {
            NomadCalendarPolicy policy = NomadCalendarPolicy.Default;
            long day = policy.LifeDayDurationMilliseconds;

            NomadCalendarSnapshot start = policy.Project(0);
            NomadCalendarSnapshot nextDay = policy.Project(day);
            NomadCalendarSnapshot nextSeason = policy.Project(day * 12);
            NomadCalendarSnapshot nextYear = policy.Project(day * 48);

            Assert.AreEqual(1, start.LifeDay);
            Assert.AreEqual(1, start.ClimateYear);
            Assert.AreEqual(0, start.SeasonIndex);
            Assert.AreEqual(1, start.ClimateWeekInSeason);

            Assert.AreEqual(2, nextDay.LifeDay);
            Assert.AreEqual(2, nextDay.ClimateWeekInSeason);
            Assert.AreEqual(0, nextDay.SeasonIndex);

            Assert.AreEqual(13, nextSeason.LifeDay);
            Assert.AreEqual(1, nextSeason.SeasonIndex);
            Assert.AreEqual(1, nextSeason.ClimateWeekInSeason);

            Assert.AreEqual(49, nextYear.LifeDay);
            Assert.AreEqual(2, nextYear.ClimateYear);
            Assert.AreEqual(0, nextYear.SeasonIndex);
            Assert.AreEqual(1, nextYear.ClimateWeekInSeason);
        }

        [Test]
        public void Project_KeepsDayAndSeasonProgressContinuousInsideSameTick()
        {
            NomadCalendarPolicy policy = NomadCalendarPolicy.Default;
            NomadCalendarSnapshot halfDay = policy.Project(
                policy.LifeDayDurationMilliseconds / 2);

            Assert.AreEqual(720, halfDay.LifeMinuteOfDay);
            Assert.AreEqual(500, halfDay.LifeDayProgressPermille);
            Assert.AreEqual(41, halfDay.SeasonProgressPermille);
            Assert.AreEqual(10, halfDay.ClimateYearProgressPermille);
        }

        [Test]
        public void CustomPolicy_ChangesProjectionWithoutChangingSavedElapsedTime()
        {
            const long savedMilliseconds = 20L * 60L * 1000L;
            var tenMinuteDay = new NomadCalendarPolicy(10L * 60L * 1000L, 12, 4);
            var twelveMinuteDay = new NomadCalendarPolicy(12L * 60L * 1000L, 12, 4);

            NomadCalendarSnapshot faster = tenMinuteDay.Project(savedMilliseconds);
            NomadCalendarSnapshot slower = twelveMinuteDay.Project(savedMilliseconds);

            Assert.AreEqual(3, faster.LifeDay);
            Assert.AreEqual(2, slower.LifeDay);
            Assert.AreEqual(3, faster.ClimateWeekInSeason);
            Assert.AreEqual(2, slower.ClimateWeekInSeason);
            Assert.AreEqual(960, slower.LifeMinuteOfDay);
        }

        [Test]
        public void InvalidPolicyOrNegativeTime_IsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new NomadCalendarPolicy(0, 12, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => new NomadCalendarPolicy(1, 0, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => new NomadCalendarPolicy(1, 12, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => NomadCalendarPolicy.Default.Project(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new NomadSimulationClock(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new NomadSimulationClock().AccumulateFrame(-0.01f, 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new NomadSimulationClock().AccumulateFrame(0.01f, float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new NomadSimulationClock(stepMilliseconds: 0));
        }

        private static long Drain(NomadSimulationClock clock, int maximumSteps = int.MaxValue)
        {
            long before = clock.SimulationTick;
            for (var step = 0; step < maximumSteps && clock.TryAdvanceStep(); step++) { }
            return clock.SimulationTick - before;
        }
    }
}
