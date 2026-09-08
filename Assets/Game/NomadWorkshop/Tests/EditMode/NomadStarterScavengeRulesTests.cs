using NUnit.Framework;

namespace Game.NomadWorkshop.Simulation.Tests
{
    public sealed class NomadStarterScavengeRulesTests
    {
        [Test]
        public void CollectScrap_ConsumesCacheAndRespectsMicroCarCapacity()
        {
            var state = new NomadStarterScavengeState(scrapCapacity: 3, initialCache: 5);

            NomadStarterScavengeResult first = state.TryCollectScrap(2);
            NomadStarterScavengeResult second = state.TryCollectScrap(2);

            Assert.That(first.Succeeded, Is.True);
            Assert.That(first.CollectedAmount, Is.EqualTo(2));
            Assert.That(second.CollectedAmount, Is.EqualTo(1));
            Assert.That(state.CarriedScrap, Is.EqualTo(3));
            Assert.That(state.CacheRemaining, Is.EqualTo(2));
            Assert.That(state.TryCollectScrap().Blocker,
                Is.EqualTo(NomadStarterScavengeBlocker.CargoFull));
        }

        [Test]
        public void CollectScrap_ReportsEmptyCacheAndInvalidIntent()
        {
            var state = new NomadStarterScavengeState(scrapCapacity: 2, initialCache: 1);

            Assert.That(state.TryCollectScrap(0).Blocker,
                Is.EqualTo(NomadStarterScavengeBlocker.InvalidAmount));
            Assert.That(state.TryCollectScrap().Succeeded, Is.True);
            Assert.That(state.TryCollectScrap().Blocker,
                Is.EqualTo(NomadStarterScavengeBlocker.CacheEmpty));
        }

        [Test]
        public void SeatRestIntent_AdvancesSlowRecoveryAndLongTermCost()
        {
            var state = new NomadStarterScavengeState(
                scrapCapacity: 2,
                initialFatigue: .8f,
                initialHealth: .9f,
                initialMood: .8f);
            state.SetSeatRestIntent(true);

            state.Advance(120f);

            Assert.That(state.Fatigue, Is.LessThan(.8f));
            Assert.That(state.Health, Is.LessThan(.9f));
            Assert.That(state.Mood, Is.LessThan(.8f));
        }

        [Test]
        public void SeatRestIntent_DoesNothingWhenInactive()
        {
            var state = new NomadStarterScavengeState(scrapCapacity: 2, initialCache: 1);

            state.Advance(120f);

            Assert.That(state.Fatigue, Is.EqualTo(.58f).Within(.0001f));
            Assert.That(state.Health, Is.EqualTo(1f).Within(.0001f));
            Assert.That(state.Mood, Is.EqualTo(.7f).Within(.0001f));
        }
    }
}
