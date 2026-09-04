using NUnit.Framework;

namespace Game.NomadWorkshop.Simulation.Tests
{
    public sealed class NomadEnvironmentScheduleTests
    {
        [Test]
        public void Project_UsesExactHalfOpenSandstormBoundaries()
        {
            NomadEnvironmentSchedule schedule = NomadEnvironmentSchedule.Default;
            NomadEnvironmentSnapshot before = schedule.Project(1729, 0L);

            Assert.That(before.Weather, Is.EqualTo(NomadWeatherKind.Clear));
            Assert.That(before.NextTransitionSimulationTick,
                Is.EqualTo(before.SandstormStartSimulationTick));
            Assert.That(
                schedule.Project(1729, before.SandstormStartSimulationTick - 1L).Weather,
                Is.EqualTo(NomadWeatherKind.Clear));

            NomadEnvironmentSnapshot during = schedule.Project(
                1729,
                before.SandstormStartSimulationTick);
            Assert.That(during.Weather, Is.EqualTo(NomadWeatherKind.Sandstorm));
            Assert.That(during.IntensityPermille, Is.InRange(600, 1000));
            Assert.That(during.NextTransitionSimulationTick,
                Is.EqualTo(during.SandstormEndSimulationTick));
            Assert.That(
                schedule.Project(1729, during.SandstormEndSimulationTick - 1L).Weather,
                Is.EqualTo(NomadWeatherKind.Sandstorm));
            Assert.That(
                schedule.Project(1729, during.SandstormEndSimulationTick).Weather,
                Is.EqualTo(NomadWeatherKind.Clear));
        }

        [Test]
        public void Project_SameSeedAndTickRebuildIdenticalEventWithoutMutableCursor()
        {
            NomadEnvironmentSchedule schedule = NomadEnvironmentSchedule.Default;
            const long tick = NomadCalendarPolicy.DefaultLifeDayDurationMilliseconds * 17L +
                              250_000L;

            NomadEnvironmentSnapshot first = schedule.Project(42, tick);
            NomadEnvironmentSnapshot repeated = schedule.Project(42, tick);

            Assert.That(repeated.Weather, Is.EqualTo(first.Weather));
            Assert.That(repeated.IntensityPermille, Is.EqualTo(first.IntensityPermille));
            Assert.That(repeated.SandstormStartSimulationTick,
                Is.EqualTo(first.SandstormStartSimulationTick));
            Assert.That(repeated.SandstormEndSimulationTick,
                Is.EqualTo(first.SandstormEndSimulationTick));
            Assert.That(repeated.NextTransitionSimulationTick,
                Is.EqualTo(first.NextTransitionSimulationTick));
        }

        [Test]
        public void Project_AfterStormPointsAtNextLifeDaysActualTransition()
        {
            NomadEnvironmentSchedule schedule = NomadEnvironmentSchedule.Default;
            NomadEnvironmentSnapshot first = schedule.Project(99, 0L);
            NomadEnvironmentSnapshot after = schedule.Project(
                99,
                first.SandstormEndSimulationTick);

            Assert.That(after.Weather, Is.EqualTo(NomadWeatherKind.Clear));
            Assert.That(after.NextTransitionSimulationTick,
                Is.GreaterThan(first.SandstormEndSimulationTick));
            Assert.That(
                schedule.Project(99, after.NextTransitionSimulationTick).Weather,
                Is.EqualTo(NomadWeatherKind.Sandstorm));
        }
    }
}
