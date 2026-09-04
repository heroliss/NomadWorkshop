using NUnit.Framework;

namespace Game.NomadWorkshop.Simulation.Tests
{
    /// <summary>锁定完整行动估算、容器约束、卫生风险、距离取舍与晕车修正的最小契约。</summary>
    public sealed class ResidentActionPlanningTests
    {
        private readonly ResidentActionPlanEvaluator _planEvaluator = new();
        private readonly UtilityDecisionEngine _decisionEngine = new();

        [Test]
        public void Water_CannotBeCarriedByHandOrOpenCrate_ButLiquidTightContainerWorks()
        {
            var requirement = new CargoTransportRequirement(
                NomadResourceIds.Water,
                amount: 2,
                CargoContainerCapability.LiquidTight,
                allowBareHands: false,
                bareHandsMaxAmount: 0,
                contaminationSensitivity: 0.4f);

            CargoTransportOptionEvaluation hands = CargoTransportPlanner.Evaluate(
                requirement,
                new CargoCarrierOption("hands", CargoCarrierKind.BareHands, 2, 0.9f, 0f));
            CargoTransportOptionEvaluation crate = CargoTransportPlanner.Evaluate(
                requirement,
                new CargoCarrierOption("crate", CargoCarrierKind.Container, 8, 0.8f, 4f));
            CargoTransportOptionEvaluation can = CargoTransportPlanner.Evaluate(
                requirement,
                new CargoCarrierOption(
                    "water-can",
                    CargoCarrierKind.Container,
                    4,
                    0.75f,
                    3f,
                    CargoContainerCapability.LiquidTight | CargoContainerCapability.Sealable));

            Assert.AreEqual(CargoTransportBlockReason.BareHandsForbidden, hands.BlockReason);
            Assert.AreEqual(CargoTransportBlockReason.MissingContainerCapability, crate.BlockReason);
            Assert.IsTrue(can.IsFeasible);
            Assert.AreEqual(0.1f, can.ContaminationTransferRisk, 0.0001f);
        }

        [Test]
        public void PreparedFood_BareHandsCarryOnlySmallAmount_AndDirtyHandsRaiseRisk()
        {
            var oneMeal = new CargoTransportRequirement(
                NomadResourceIds.PreparedMeal,
                amount: 1,
                CargoContainerCapability.FoodSafe,
                allowBareHands: true,
                bareHandsMaxAmount: 1,
                contaminationSensitivity: 0.8f);

            CargoTransportOptionEvaluation dirtyHands = CargoTransportPlanner.Evaluate(
                oneMeal,
                new CargoCarrierOption("dirty-hands", CargoCarrierKind.BareHands, 2, 0.1f, 0f));
            CargoTransportOptionEvaluation cleanHands = CargoTransportPlanner.Evaluate(
                oneMeal,
                new CargoCarrierOption("clean-hands", CargoCarrierKind.BareHands, 2, 0.95f, 0f));
            var twoMeals = new CargoTransportRequirement(
                NomadResourceIds.PreparedMeal,
                amount: 2,
                CargoContainerCapability.FoodSafe,
                allowBareHands: true,
                bareHandsMaxAmount: 1,
                contaminationSensitivity: 0.8f);
            CargoTransportOptionEvaluation overloadedHands = CargoTransportPlanner.Evaluate(
                twoMeals,
                new CargoCarrierOption("hands", CargoCarrierKind.BareHands, 3, 1f, 0f));

            Assert.IsTrue(dirtyHands.IsFeasible);
            Assert.IsTrue(cleanHands.IsFeasible);
            Assert.Greater(dirtyHands.ContaminationTransferRisk, cleanHands.ContaminationTransferRisk);
            Assert.AreEqual(CargoTransportBlockReason.CapacityInsufficient, overloadedHands.BlockReason);
        }

        [Test]
        public void HandOnlyCargo_RejectsContainersEvenWhenCapacityIsEnough()
        {
            var requirement = new CargoTransportRequirement(
                new ResourceId("awkward-fragile-part"),
                amount: 1,
                CargoContainerCapability.None,
                allowBareHands: true,
                bareHandsMaxAmount: 1,
                contaminationSensitivity: 0f,
                allowContainers: false);

            CargoTransportOptionEvaluation hands = CargoTransportPlanner.Evaluate(
                requirement,
                new CargoCarrierOption("hands", CargoCarrierKind.BareHands, 1, 1f, 0f));
            CargoTransportOptionEvaluation crate = CargoTransportPlanner.Evaluate(
                requirement,
                new CargoCarrierOption("large-crate", CargoCarrierKind.Container, 20, 1f, 2f));

            Assert.IsTrue(hands.IsFeasible);
            Assert.AreEqual(CargoTransportBlockReason.ContainerForbidden, crate.BlockReason);
        }

        [Test]
        public void EatingPlace_ExtraComfortWinsNearby_ButLongWalkCanMakeStandingPreferable()
        {
            ResidentActionCandidate standing = Evaluate(Proposal(
                "stand", "eat", "站着吃", travelSeconds: 0f, activeSeconds: 20f, comfort: 0f));
            ResidentActionCandidate nearbyTable = Evaluate(Proposal(
                "near-table", "eat", "在近处餐桌吃", travelSeconds: 2f, activeSeconds: 20f, comfort: 0.1f));
            ResidentActionCandidate farTable = Evaluate(Proposal(
                "far-table", "eat", "在远处餐桌吃", travelSeconds: 20f, activeSeconds: 20f, comfort: 0.1f));

            ResidentDecisionResult nearbyResult = Decide(standing, nearbyTable);
            ResidentDecisionResult farResult = Decide(standing, farTable);

            Assert.AreSame(nearbyTable, nearbyResult.Selected);
            Assert.AreSame(standing, farResult.Selected);
            Assert.Greater(
                farTable.PlanEvaluation.Utility.TravelCost,
                nearbyTable.PlanEvaluation.Utility.TravelCost);
        }

        [Test]
        public void ToiletChoice_CleanerFacilityWinsNormally_ButUrgencyAmplifiesTravelDelay()
        {
            var policy = new ResidentActionPlanPolicy
            {
                TravelTimeCostPerSecond = 0.01f,
                UrgentDelayCostMultiplier = 4f,
            };

            ResidentActionPlanProposal nearDirty = Proposal(
                "near-dirty", "toilet", "近处较脏厕所", 2f, 8f, comfort: 0.01f);
            nearDirty.CleanlinessBenefit = 0.01f;
            ResidentActionPlanProposal farClean = Proposal(
                "far-clean", "toilet", "远处洁净厕所", 10f, 8f, comfort: 0.04f);
            farClean.CleanlinessBenefit = 0.14f;

            ResidentActionCandidate normalNear = Evaluate(nearDirty, policy);
            ResidentActionCandidate normalFar = Evaluate(farClean, policy);
            Assert.AreSame(normalFar, Decide(normalNear, normalFar).Selected);

            nearDirty.DelayUrgency = 1f;
            farClean.DelayUrgency = 1f;
            ResidentActionCandidate urgentNear = Evaluate(nearDirty, policy);
            ResidentActionCandidate urgentFar = Evaluate(farClean, policy);
            Assert.AreSame(urgentNear, Decide(urgentNear, urgentFar).Selected);
        }

        [Test]
        public void MotionSickness_LowersIntenseWorkAndRaisesRestUtility()
        {
            ResidentActionPlanProposal work = Proposal(
                "repair", "repair", "维修动力核心", 0f, 12f, comfort: 0f);
            work.BaseUtility = 0.42f;
            work.WorkIntensity = 1f;
            ResidentActionPlanProposal rest = Proposal(
                "rest", "rest", "躺下休息", 0f, 12f, comfort: 0f);
            rest.BaseUtility = 0.24f;
            rest.RestQuality = 1f;

            ResidentActionCandidate healthyWork = Evaluate(work, motionSickness: 0f);
            ResidentActionCandidate healthyRest = Evaluate(rest, motionSickness: 0f);
            Assert.AreSame(healthyWork, Decide(healthyWork, healthyRest).Selected);

            ResidentActionCandidate sickWork = Evaluate(work, motionSickness: 0.9f);
            ResidentActionCandidate sickRest = Evaluate(rest, motionSickness: 0.9f);
            Assert.AreSame(sickRest, Decide(sickWork, sickRest).Selected);
            Assert.Greater(sickWork.PlanEvaluation.Utility.MotionWorkCost, 0f);
            Assert.Greater(sickRest.PlanEvaluation.Utility.MotionRecoveryBenefit, 0f);
        }

        [Test]
        public void MissingPrerequisite_IsHardBlockedBeforeProbabilitySelection()
        {
            ResidentActionPlanProposal washThenEat = Proposal(
                "wash-eat", "eat", "洗手后进食", 2f, 24f, comfort: 0.1f);
            washThenEat.Feasibility = ResidentActionPlanFeasibility.Blocked(
                ResidentActionPlanBlockReason.PrerequisiteUnavailable,
                "洗手池缺水");
            ResidentActionCandidate blocked = Evaluate(washThenEat);
            ResidentActionCandidate standing = Evaluate(Proposal(
                "stand", "eat", "站着吃", 0f, 20f, comfort: 0f));

            ResidentDecisionResult result = Decide(blocked, standing);

            Assert.AreSame(standing, result.Selected);
            Assert.AreEqual("洗手池缺水", FindTrace(result, "wash-eat").Reason);
            Assert.AreEqual(CandidateDecisionState.Ineligible, FindTrace(result, "wash-eat").State);
        }

        [TestCase(ResidentActionPlanBlockReason.UnsafeEnvironment, "厕所舱室正在燃烧")]
        [TestCase(ResidentActionPlanBlockReason.CapabilityUnavailable, "居民当前无法站立")]
        public void SafetyAndCapability_AreHardConstraintsRatherThanLargeUtilityPenalties(
            ResidentActionPlanBlockReason reason,
            string detail)
        {
            ResidentActionPlanProposal unsafePlan = Proposal(
                "unsafe", "hazard-response", "危险行动", 1f, 2f, comfort: 1f);
            unsafePlan.RiskTier = ResidentDecisionRiskTier.Critical;
            unsafePlan.RiskPriority = 1f;
            unsafePlan.Feasibility = ResidentActionPlanFeasibility.Blocked(reason, detail);
            ResidentActionCandidate blocked = Evaluate(unsafePlan);

            ResidentActionPlanProposal fallbackPlan = Proposal(
                "fallback", "fallback", "可执行退路", 1f, 2f, comfort: 0f);
            fallbackPlan.RiskTier = ResidentDecisionRiskTier.Severe;
            fallbackPlan.RiskPriority = 0.8f;
            ResidentActionCandidate fallback = Evaluate(fallbackPlan);

            ResidentDecisionResult result = Decide(blocked, fallback);

            Assert.AreSame(fallback, result.Selected);
            Assert.AreEqual(CandidateDecisionState.Ineligible, FindTrace(result, "unsafe").State);
            Assert.AreEqual(detail, FindTrace(result, "unsafe").Reason);
        }

        private ResidentActionCandidate Evaluate(
            ResidentActionPlanProposal proposal,
            ResidentActionPlanPolicy policy = null,
            float motionSickness = 0f)
            => _planEvaluator.Evaluate(
                proposal,
                new ResidentDecisionCondition(motionSickness),
                policy).Candidate;

        private static ResidentActionPlanProposal Proposal(
            string id,
            string intent,
            string displayName,
            float travelSeconds,
            float activeSeconds,
            float comfort)
        {
            return new ResidentActionPlanProposal(id, intent, displayName)
            {
                BaseUtility = 0.18f,
                ComfortBenefit = comfort,
                Steps = new[]
                {
                    new ResidentActionStepEstimate(
                        ResidentActionStepKind.Travel,
                        travelSeconds,
                        travelSeconds,
                        "前往目标"),
                    new ResidentActionStepEstimate(
                        ResidentActionStepKind.UseFacility,
                        activeSeconds,
                        label: "执行行动"),
                },
            };
        }

        private ResidentDecisionResult Decide(params ResidentActionCandidate[] candidates)
            => _decisionEngine.Decide(new ResidentDecisionContext(
                worldSeed: 41,
                residentId: 0xCAFEUL,
                decisionSequence: 7,
                needs: new ResidentNeedState[0],
                candidates: candidates));

        private static CandidateDecisionTrace FindTrace(ResidentDecisionResult result, string id)
        {
            for (int i = 0; i < result.Traces.Count; i++)
            {
                if (result.Traces[i].Candidate.Id == id) return result.Traces[i];
            }
            Assert.Fail($"未找到候选轨迹：{id}");
            return null;
        }
    }
}
