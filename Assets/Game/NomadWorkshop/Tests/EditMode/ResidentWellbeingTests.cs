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
            Assert.That(needs[2].Need, Is.EqualTo(ResidentNeed.Health));
            Assert.That(needs[2].Deficit, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(needs[2].CanPromoteToUrgent, Is.False,
                "普通休息不能因为能少量恢复结果健康值就被误判为急救；真正病因自行声明风险层。 ");
            Assert.That(needs[3].Need, Is.EqualTo(ResidentNeed.Stress));
            Assert.That(needs[3].Deficit, Is.EqualTo(0.24f).Within(0.0001f));
        }

        [Test]
        public void SevereDehydration_DamagesHealthAndZeroHealthCannotRecover()
        {
            var dehydrated = new ResidentWellbeing(
                entertainment: 0.7f,
                mood: 0.7f,
                fatigue: 0.2f,
                stress: 0.2f,
                health: 0.25f);
            var severeDrivers = new ResidentWellbeingDrivers(
                thirstDeficit: 1f,
                bladderPressure: 0f,
                isBlocked: false);

            dehydrated.Advance(
                120f,
                ResidentWellbeingActivity.Routine,
                severeDrivers);

            Assert.That(dehydrated.Health, Is.EqualTo(0f));
            Assert.That(dehydrated.IsAlive, Is.False);

            dehydrated.Advance(
                120f,
                ResidentWellbeingActivity.GroundRest,
                SafeDrivers);
            Assert.That(dehydrated.Health, Is.EqualTo(0f),
                "死亡是业务终态，普通休息不能把居民复活。 ");
        }

        [Test]
        public void GroundRest_IsLowQualityHealthRecoveryWithMoodCost()
        {
            var wellbeing = new ResidentWellbeing(
                entertainment: 0.9f,
                mood: 0.8f,
                fatigue: 0.3f,
                stress: 0.2f,
                health: 0.55f);

            wellbeing.Advance(
                30f,
                ResidentWellbeingActivity.GroundRest,
                SafeDrivers);

            Assert.That(wellbeing.Health, Is.GreaterThan(0.55f));
            Assert.That(wellbeing.Fatigue, Is.LessThan(0.3f));
            Assert.That(wellbeing.Mood, Is.LessThan(0.8f),
                "地面坐卧是无床椅时的兜底，不应与舒适床铺等价。 ");
        }

        [Test]
        public void WorkEfficiency_UsesHealthFatigueStressAndStableActionVariation()
        {
            float baseline = ResidentPerformance.CalculateExpectedWorkEfficiency(
                baseEfficiency: 1f,
                health: 1f,
                fatigue: 0f,
                stress: 0.1f);
            float pressured = ResidentPerformance.CalculateExpectedWorkEfficiency(
                baseEfficiency: 1f,
                health: 1f,
                fatigue: 0f,
                stress: 0.7f);
            float overloaded = ResidentPerformance.CalculateExpectedWorkEfficiency(
                baseEfficiency: 1f,
                health: 1f,
                fatigue: 0f,
                stress: 1f);
            float unwell = ResidentPerformance.CalculateExpectedWorkEfficiency(
                baseEfficiency: 1f,
                health: 0.3f,
                fatigue: 0.8f,
                stress: 0.8f);

            Assert.That(pressured, Is.GreaterThan(baseline));
            Assert.That(overloaded, Is.GreaterThan(baseline));
            Assert.That(overloaded, Is.LessThan(pressured),
                "接近崩溃的压力仍有应激加速，但不应比可控压力更高效。 ");
            Assert.That(unwell, Is.LessThan(baseline));

            float first = ResidentPerformance.SampleWorkEfficiency(
                1729, 0xF01UL, 4, pressured);
            float repeated = ResidentPerformance.SampleWorkEfficiency(
                1729, 0xF01UL, 4, pressured);
            float next = ResidentPerformance.SampleWorkEfficiency(
                1729, 0xF01UL, 5, pressured);
            Assert.That(first, Is.EqualTo(repeated));
            Assert.That(next, Is.Not.EqualTo(first));
            Assert.That(first, Is.InRange(pressured * 0.92f, pressured * 1.08f));
        }

        [Test]
        public void PoorHealthAndHighFatigue_MakeGroundRestOutweighDaydream()
        {
            float healthyPreference = CalculateGroundRestPreference(
                health: 1f,
                fatigue: 0.1f,
                stress: 0.1f);
            float unwellPreference = CalculateGroundRestPreference(
                health: 0.2f,
                fatigue: 0.85f,
                stress: 0.5f);

            Assert.That(healthyPreference, Is.LessThan(0f),
                "健康且精力充足时，不应无故躺在地面。 ");
            Assert.That(unwellPreference, Is.GreaterThan(0f),
                "低健康与高疲劳应通过统一需求收益让地面休息自然反超，而非写死优先级。 ");
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

        private static float CalculateGroundRestPreference(
            float health,
            float fatigue,
            float stress)
        {
            var wellbeing = new ResidentWellbeing(
                entertainment: 0.7f,
                mood: 0.7f,
                fatigue: fatigue,
                stress: stress,
                health: health);
            var evaluator = new ResidentActionPlanEvaluator();
            var condition = new ResidentDecisionCondition(
                motionSickness: 0f,
                effortAversion: ResidentPerformance.CalculateEffortAversion(
                    health,
                    fatigue));
            ResidentActionCandidate daydream = evaluator.Evaluate(
                ResidentLeisurePlanFactory.CreateDaydream(restSeconds: 3.5f),
                condition).Candidate;
            ResidentActionCandidate groundRest = evaluator.Evaluate(
                ResidentLeisurePlanFactory.CreateGroundRest(restSeconds: 20f),
                condition).Candidate;
            ResidentDecisionResult decision = new UtilityDecisionEngine().Decide(
                new ResidentDecisionContext(
                    worldSeed: 1729,
                    residentId: 0xF01UL,
                    decisionSequence: 1,
                    needs: wellbeing.CreateDecisionNeedSnapshot(),
                    candidates: new[] { daydream, groundRest }),
                ResidentLeisurePlanFactory.CreateSelectionPolicy());

            float daydreamScore = float.NaN;
            float groundRestScore = float.NaN;
            for (var i = 0; i < decision.Traces.Count; i++)
            {
                CandidateDecisionTrace trace = decision.Traces[i];
                if (trace.Candidate.Id == ResidentLeisurePlanFactory.DaydreamCandidateId)
                    daydreamScore = trace.Score.Total;
                else if (trace.Candidate.Id == ResidentLeisurePlanFactory.GroundRestCandidateId)
                    groundRestScore = trace.Score.Total;
            }

            Assert.That(float.IsNaN(daydreamScore), Is.False);
            Assert.That(float.IsNaN(groundRestScore), Is.False);
            return groundRestScore - daydreamScore;
        }
    }
}
