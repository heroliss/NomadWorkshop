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
                recreationRestore: 0.3f,
                targetLabel: "(1.2, -0.5)");

            ResidentActionPlanEvaluation result = new ResidentActionPlanEvaluator().Evaluate(
                proposal,
                new ResidentDecisionCondition(0f));

            Assert.That(result.Steps.Count, Is.EqualTo(2));
            Assert.That(result.Steps[0].Kind, Is.EqualTo(ResidentActionStepKind.Travel));
            Assert.That(result.Steps[0].DistanceMeters, Is.EqualTo(2.8f));
            Assert.That(result.Utility.TravelSeconds, Is.EqualTo(1.4f).Within(0.0001f));
            Assert.That(result.Candidate.NeedEffects[0].Need, Is.EqualTo(ResidentNeed.Recreation));
        }

        [Test]
        public void WanderAndDaydream_AreDistinctWeightedChoices()
        {
            ResidentActionPlanProposal wander = ResidentLeisurePlanFactory.CreateWander(
                1.5f,
                2.5f,
                2f,
                0.3f,
                "test");
            ResidentActionPlanProposal daydream = ResidentLeisurePlanFactory.CreateDaydream(
                2f,
                0.22f);

            Assert.That(wander.IntentId, Is.Not.EqualTo(daydream.IntentId));
            Assert.That(wander.Id, Is.Not.EqualTo(daydream.Id));
            Assert.That(wander.NeedEffects[0].Restore, Is.GreaterThan(daydream.NeedEffects[0].Restore));
        }

        [Test]
        public void FoundationSteadyState_DaydreamAndWanderRemainWeightedRandomNearTwoToOne()
        {
            var evaluator = new ResidentActionPlanEvaluator();
            ResidentActionCandidate wander = evaluator.Evaluate(
                ResidentLeisurePlanFactory.CreateWander(2.8f, 2.8f, 1.2f, 0.32f, "test"),
                new ResidentDecisionCondition(0f)).Candidate;
            ResidentActionCandidate daydream = evaluator.Evaluate(
                ResidentLeisurePlanFactory.CreateDaydream(1.2f, 0.22f),
                new ResidentDecisionCondition(0f)).Candidate;
            var needs = new[]
            {
                // 实际长期运行时每次休闲都会把缺口拉回接近 0。旧测试只用 50% 缺口，
                // 没有发现散步会在这里被相对短名单阈值永久裁掉。
                new ResidentNeedState(ResidentNeed.Recreation, 0.01f, 0.01f),
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
