using Game.NomadWorkshop.Simulation;
using NUnit.Framework;

namespace Game.NomadWorkshop.Tests
{
    public sealed class ResidentWellbeingTests
    {
        private static readonly ResidentWellbeingDrivers SafeDrivers =
            new(thirstDeficit: 0.2f, bladderPressure: 0.1f, isBlocked: false);

        [TestCase(ResidentWellbeingActivity.Daydream)]
        [TestCase(ResidentWellbeingActivity.Wander)]
        public void BasicRest_ReducesFatigueAndStressWithoutRestoringEntertainment(
            ResidentWellbeingActivity activity)
        {
            var wellbeing = new ResidentWellbeing(
                entertainment: 0.62f,
                mood: 0.58f,
                fatigue: 0.52f,
                stress: 0.48f);

            wellbeing.Advance(10f, activity, SafeDrivers, outcomeScale: 1f);

            Assert.That(wellbeing.Entertainment, Is.LessThan(0.62f));
            Assert.That(wellbeing.Fatigue, Is.LessThan(0.52f));
            Assert.That(wellbeing.Stress, Is.LessThan(0.48f));
            Assert.That(wellbeing.Mood, Is.GreaterThan(0.58f));
        }

        [Test]
        public void Hobby_IsTheActivityThatActuallyRestoresEntertainment()
        {
            var wellbeing = new ResidentWellbeing(0.3f, 0.55f, 0.45f, 0.42f);

            wellbeing.Advance(
                10f,
                ResidentWellbeingActivity.Hobby,
                SafeDrivers,
                outcomeScale: 1f);

            Assert.That(wellbeing.Entertainment, Is.GreaterThan(0.3f));
            NeedEffect[] effects = ResidentWellbeing.CreateExpectedEffects(
                ResidentWellbeingActivity.Hobby,
                10f);
            Assert.That(
                effects,
                Has.Some.Matches<NeedEffect>(effect =>
                    effect.Need == ResidentNeed.Entertainment && effect.Restore > 0f));
        }

        [Test]
        public void Entertainment_ContinuouslyAffectsMoodAndFatigueGrowth()
        {
            var lowEntertainment = new ResidentWellbeing(0.12f, 0.65f, 0.25f, 0.2f);
            var highEntertainment = new ResidentWellbeing(0.9f, 0.65f, 0.25f, 0.2f);

            lowEntertainment.Advance(
                120f,
                ResidentWellbeingActivity.Routine,
                SafeDrivers);
            highEntertainment.Advance(
                120f,
                ResidentWellbeingActivity.Routine,
                SafeDrivers);

            Assert.That(
                lowEntertainment.Mood,
                Is.LessThan(highEntertainment.Mood),
                "低娱乐应缓慢拖累心情，而不是等某个离散阈值瞬间跳变。 ");
            Assert.That(
                highEntertainment.Fatigue,
                Is.LessThan(lowEntertainment.Fatigue),
                "高娱乐只提供有限的疲劳增长缓冲，不能直接消除疲劳。 ");
        }

        [Test]
        public void DecisionSnapshot_ConvertsOnlyEntertainmentToDeficitAtBoundary()
        {
            var wellbeing = new ResidentWellbeing(0.72f, 0.66f, 0.31f, 0.24f);

            ResidentNeedState[] needs = wellbeing.CreateDecisionNeedSnapshot();

            Assert.That(needs[0].Need, Is.EqualTo(ResidentNeed.Entertainment));
            Assert.That(needs[0].Deficit, Is.EqualTo(0.28f).Within(0.0001f));
            Assert.That(
                needs[0].CanPromoteToUrgent,
                Is.False,
                "娱乐可以强烈影响日常选择，但不能与即将失禁等生理紧急事项抢风险层。 ");
            Assert.That(needs[1].Need, Is.EqualTo(ResidentNeed.Fatigue));
            Assert.That(needs[1].Deficit, Is.EqualTo(0.31f).Within(0.0001f));
            Assert.That(needs[2].Need, Is.EqualTo(ResidentNeed.Stress));
            Assert.That(needs[2].Deficit, Is.EqualTo(0.24f).Within(0.0001f));
        }

        [Test]
        public void LeisureOutcomeScale_IsStableAndEventScoped()
        {
            float first = ResidentWellbeing.SampleLeisureOutcomeScale(1729, 0xF01UL, 7);
            float repeated = ResidentWellbeing.SampleLeisureOutcomeScale(1729, 0xF01UL, 7);
            float next = ResidentWellbeing.SampleLeisureOutcomeScale(1729, 0xF01UL, 8);

            Assert.That(first, Is.EqualTo(repeated));
            Assert.That(first, Is.InRange(0.85f, 1.15f));
            Assert.That(next, Is.InRange(0.85f, 1.15f));
            Assert.That(next, Is.Not.EqualTo(first));
        }
    }
}
