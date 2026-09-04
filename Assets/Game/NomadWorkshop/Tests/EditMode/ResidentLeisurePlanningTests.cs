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
        public void HobbyAtFacility_UsesTravelPreferenceAndRealEntertainmentBenefit()
        {
            ResidentActionPlanProposal proposal =
                ResidentLeisurePlanFactory.CreateHobbyAtFacility(
                    "facility-0007",
                    "观景画架",
                    pathDistanceMeters: 3f,
                    moveSpeedMetersPerSecond: 2f,
                    hobbySeconds: 5f,
                    personalAffinity: 0.72f);
            ResidentActionPlanEvaluation result = new ResidentActionPlanEvaluator().Evaluate(
                proposal,
                new ResidentDecisionCondition(0f));

            Assert.That(proposal.Id, Is.EqualTo("hobby:facility-0007"));
            Assert.That(proposal.IntentId, Is.EqualTo(ResidentLeisurePlanFactory.HobbyIntentId));
            Assert.That(result.Steps, Has.Count.EqualTo(2));
            Assert.That(result.Steps[0].Kind, Is.EqualTo(ResidentActionStepKind.Travel));
            Assert.That(result.Utility.TravelSeconds, Is.EqualTo(1.5f).Within(0.0001f));
            Assert.That(result.Candidate.PersonalAffinity, Is.EqualTo(0.72f));
            Assert.That(
                result.Candidate.NeedEffects,
                Has.Some.Matches<NeedEffect>(effect =>
                    effect.Need == ResidentNeed.Entertainment && effect.Restore > 0f));
            Assert.That(
                result.Candidate.ReservationKeys,
                Does.Contain("facility:facility-0007:hobby"));
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

        [Test]
        public void HobbyPreference_IsBoundedAndNeedSensitiveWithoutErasingBasicLeisure()
        {
            var evaluator = new ResidentActionPlanEvaluator();
            ResidentActionCandidate wander = evaluator.Evaluate(
                ResidentLeisurePlanFactory.CreateWander(2.8f, 2.8f, 3.5f, "test"),
                new ResidentDecisionCondition(0f)).Candidate;
            ResidentActionCandidate daydream = evaluator.Evaluate(
                ResidentLeisurePlanFactory.CreateDaydream(3.5f),
                new ResidentDecisionCondition(0f)).Candidate;
            ResidentActionCandidate hobby = evaluator.Evaluate(
                ResidentLeisurePlanFactory.CreateHobbyAtFacility(
                    "facility-0007",
                    "观景画架",
                    2f,
                    2.8f,
                    8f,
                    personalAffinity: 0.34f),
                new ResidentDecisionCondition(0f)).Candidate;
            var engine = new UtilityDecisionEngine();
            UtilityDecisionPolicy policy = ResidentLeisurePlanFactory.CreateSelectionPolicy();
            ResidentNeedState[] satisfiedEntertainment = LeisureNeeds(entertainmentDeficit: 0.05f);
            ResidentNeedState[] lowEntertainment = LeisureNeeds(entertainmentDeficit: 0.75f);
            var satisfiedCounts = new int[3];
            var lowCounts = new int[3];

            for (var sequence = 0; sequence < 1_200; sequence++)
            {
                CountSelection(
                    engine.Decide(
                        new ResidentDecisionContext(
                            20260905,
                            0x4E4F4D4144UL,
                            sequence,
                            satisfiedEntertainment,
                            new[] { daydream, wander, hobby }),
                        policy),
                    satisfiedCounts);
                CountSelection(
                    engine.Decide(
                        new ResidentDecisionContext(
                            20260905,
                            0x4E4F4D4144UL,
                            sequence,
                            lowEntertainment,
                            new[] { daydream, wander, hobby }),
                        policy),
                    lowCounts);
            }

            Assert.That(satisfiedCounts[0], Is.GreaterThan(satisfiedCounts[1]),
                "娱乐充足时仍保留约定的发呆高于散步倾向。 ");
            Assert.That(satisfiedCounts[1], Is.GreaterThan(0),
                "加入画架后，散步不能在短名单形成阶段永久消失。 ");
            Assert.That(satisfiedCounts[2], Is.GreaterThan(0),
                "个人爱好即使不是当前强需求，也应保留合理机会。 ");
            Assert.That(satisfiedCounts[2], Is.LessThan(satisfiedCounts[0] + satisfiedCounts[1]),
                "娱乐已满足时，单一爱好不应垄断所有基础休整。 ");
            Assert.That(lowCounts[2], Is.GreaterThan(satisfiedCounts[2]),
                "娱乐缺口升高必须通过真实需求收益提高爱好出现率。 ");

            ResidentDecisionResult traceSample = engine.Decide(
                new ResidentDecisionContext(
                    20260905,
                    0x4E4F4D4144UL,
                    0,
                    satisfiedEntertainment,
                    new[] { daydream, wander, hobby }),
                policy);
            CandidateDecisionTrace hobbyTrace = FindTrace(traceSample, hobby.Id);
            Assert.That(
                hobbyTrace.Score.PersonalBenefit,
                Is.EqualTo(0.34f * policy.PersonalAffinityUtilityScale).Within(0.0001f),
                "0–1 人物偏好应先映射到有边界的 Utility 加分，而不是直接压过整项方案。 ");
        }

        private static ResidentNeedState[] LeisureNeeds(float entertainmentDeficit) => new[]
        {
            new ResidentNeedState(
                ResidentNeed.Entertainment,
                entertainmentDeficit,
                ResidentWellbeing.EntertainmentDecayPerSecond,
                importance: 0.68f,
                canPromoteToUrgent: false),
            new ResidentNeedState(
                ResidentNeed.Fatigue,
                0.28f,
                ResidentWellbeing.BaseFatigueGrowthPerSecond,
                importance: 0.82f),
            new ResidentNeedState(
                ResidentNeed.Stress,
                0.22f,
                0f,
                importance: 0.74f),
        };

        private static void CountSelection(ResidentDecisionResult result, int[] counts)
        {
            Assert.That(result.Selected, Is.Not.Null);
            if (result.Selected.Id == ResidentLeisurePlanFactory.DaydreamCandidateId)
                counts[0]++;
            else if (result.Selected.Id == ResidentLeisurePlanFactory.WanderCandidateId)
                counts[1]++;
            else if (result.Selected.IntentId == ResidentLeisurePlanFactory.HobbyIntentId)
                counts[2]++;
            else
                Assert.Fail($"未知休闲候选：{result.Selected.Id}");
        }

        private static CandidateDecisionTrace FindTrace(
            ResidentDecisionResult result,
            string candidateId)
        {
            foreach (CandidateDecisionTrace trace in result.Traces)
            {
                if (trace.Candidate.Id == candidateId) return trace;
            }
            Assert.Fail($"没有找到决策轨迹：{candidateId}");
            return null;
        }
    }
}
