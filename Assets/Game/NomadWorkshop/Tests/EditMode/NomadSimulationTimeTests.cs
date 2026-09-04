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
                committedMilliseconds += partitioned.Advance(0.0004f, 1f);

            var singleStep = new NomadSimulationClock();
            long singleDelta = singleStep.Advance(1f, 1f);

            Assert.AreEqual(1000L, committedMilliseconds);
            Assert.AreEqual(singleDelta, committedMilliseconds);
            Assert.AreEqual(singleStep.SimulationTick, partitioned.SimulationTick);
        }

        [Test]
        public void Clock_AppliesSpeedAndRestoresSavedTickWithoutFrameRemainder()
        {
            var clock = new NomadSimulationClock();

            Assert.AreEqual(1000L, clock.Advance(0.5f, 2f));
            Assert.AreEqual(0L, clock.Advance(0.0015f, 0.5f));
            Assert.AreEqual(1000L, clock.SimulationTick);

            clock.Restore(42_500L);

            Assert.AreEqual(42_500L, clock.SimulationTick);
            Assert.AreEqual(0L, clock.Advance(0.0005f, 1f));
            Assert.AreEqual(42_500L, clock.SimulationTick);
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
                new NomadSimulationClock().Advance(-0.01f, 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new NomadSimulationClock().Advance(0.01f, float.NaN));
        }
    }
}
