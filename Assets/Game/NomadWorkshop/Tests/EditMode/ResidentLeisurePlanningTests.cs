using Game.NomadWorkshop.Simulation;
using NUnit.Framework;

namespace Game.NomadWorkshop.Tests
{
    public sealed class ResidentLeisurePlanningTests
    {
        [Test]
        public void Wander_UsesRealRouteAndRestAsOneCompletePlan()
        {
            ResidentActionPlanProposal proposal = ResidentLeisurePlanFactory.CreateWander(
                pathDistanceMeters: 2.8f,
                moveSpeedMetersPerSecond: 2f,
                restSeconds: 3f,
                targetLabel: "(1.2, -0.5)");

            ResidentActionPlanEvaluation result = new ResidentActionPlanEvaluator().Evaluate(
                proposal,
                new ResidentDecisionCondition(0f));

            Assert.That(result.Steps.Count, Is.EqualTo(2));
            Assert.That(result.Steps[0].Kind, Is.EqualTo(ResidentActionStepKind.Travel));
            Assert.That(result.Steps[0].DistanceMeters, Is.EqualTo(2.8f));
            Assert.That(result.Utility.TravelSeconds, Is.EqualTo(1.4f).Within(0.0001f));
            Assert.That(
                result.Candidate.NeedEffects,
                Has.None.Matches<NeedEffect>(effect =>
                    effect.Need == ResidentNeed.Entertainment),
                "普通散步只能休整身心，不能凭空满足兴趣娱乐。 ");
            Assert.That(
                result.Candidate.NeedEffects,
                Has.Some.Matches<NeedEffect>(effect => effect.Need == ResidentNeed.Stress));
        }

        [Test]
        public void WanderAndDaydream_AreDistinctWeightedChoices()
        {
            ResidentActionPlanProposal wander = ResidentLeisurePlanFactory.CreateWander(
                1.5f,
                2.5f,
                2f,
                "test");
            ResidentActionPlanProposal daydream = ResidentLeisurePlanFactory.CreateDaydream(2f);

            Assert.That(wander.IntentId, Is.Not.EqualTo(daydream.IntentId));
            Assert.That(wander.Id, Is.Not.EqualTo(daydream.Id));
            Assert.That(wander.NeedEffects.Length, Is.EqualTo(2));
            Assert.That(daydream.NeedEffects.Length, Is.EqualTo(2));
            Assert.That(
                daydream.NeedEffects[0].Restore,
                Is.GreaterThan(wander.NeedEffects[0].Restore),
                "发呆的疲劳恢复应高于闲逛，但两者都不恢复娱乐。 ");
        }

        [Test]
        public void FoundationSteadyState_DaydreamAndWanderRemainWeightedRandomNearTwoToOne()
        {
            var evaluator = new ResidentActionPlanEvaluator();
            ResidentActionCandidate wander = evaluator.Evaluate(
                ResidentLeisurePlanFactory.CreateWander(2.8f, 2.8f, 1.2f, "test"),
                new ResidentDecisionCondition(0f)).Candidate;
            ResidentActionCandidate daydream = evaluator.Evaluate(
                ResidentLeisurePlanFactory.CreateDaydream(1.2f),
                new ResidentDecisionCondition(0f)).Candidate;
            var needs = new[]
            {
                // 在休整缺口接近 0 时，基础偏好仍应让两个动作都留在短名单；
                // Entertainment 没有被它们恢复，因此不参与这两个候选的收益。
                new ResidentNeedState(ResidentNeed.Fatigue, 0.01f, 0.0008f),
                new ResidentNeedState(ResidentNeed.Stress, 0.01f, 0f),
            };
            var engine = new UtilityDecisionEngine();
            UtilityDecisionPolicy policy = ResidentLeisurePlanFactory.CreateSelectionPolicy();
            var daydreamCount = 0;
            var wanderCount = 0;

            for (var sequence = 0; sequence < 600; sequence++)
            {
                ResidentDecisionResult result = engine.Decide(new ResidentDecisionContext(
                    20260901,
                    0x4E4F4D4144UL,
                    sequence,
                    needs,
                    new[] { daydream, wander }),
                    policy);
                if (result.Selected.Id == ResidentLeisurePlanFactory.DaydreamCandidateId)
                    daydreamCount++;
                else if (result.Selected.Id == ResidentLeisurePlanFactory.WanderCandidateId)
                    wanderCount++;
            }

            Assert.That(wanderCount, Is.GreaterThan(0), "加权随机不能把散步退化成永远不会发生。 ");
            Assert.That(
                daydreamCount / (float)wanderCount,
                Is.InRange(1.7f, 2.3f),
                $"当前 Foundation 基线应接近 2:1，而不是固定轮换；实际 {daydreamCount}:{wanderCount}。 ");
        }
    }
}
