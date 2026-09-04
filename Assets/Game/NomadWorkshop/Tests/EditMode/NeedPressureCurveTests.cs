using System;
using NUnit.Framework;

namespace Game.NomadWorkshop.Simulation.Tests
{
    public sealed class NeedPressureCurveTests
    {
        private static readonly NeedPressureCurve BladderCurve = new(
            onsetDeficit: 0.5f,
            urgentDeficit: 0.9f,
            responseExponent: 2f,
            maximumPressure: 3.5f);

        [Test]
        public void BladderCurve_IsZeroThroughHalfThenRisesSmoothlyToUrgent()
        {
            Assert.That(BladderCurve.EvaluatePressure(0f), Is.Zero);
            Assert.That(BladderCurve.EvaluatePressure(0.5f), Is.Zero);

            float sixtyPercent = BladderCurve.EvaluatePressure(0.6f);
            float seventyPercent = BladderCurve.EvaluatePressure(0.7f);
            float eightyPercent = BladderCurve.EvaluatePressure(0.8f);
            Assert.That(sixtyPercent, Is.GreaterThan(0f));
            Assert.That(sixtyPercent, Is.LessThan(0.02f), "60% 时只应有很小驱动力。 ");
            Assert.That(sixtyPercent, Is.LessThan(seventyPercent));
            Assert.That(seventyPercent, Is.EqualTo(0.25f).Within(0.000001f));
            Assert.That(seventyPercent, Is.LessThan(eightyPercent));
            Assert.That(eightyPercent, Is.GreaterThan(0.75f));
            Assert.That(eightyPercent, Is.LessThan(1f));
            Assert.That(BladderCurve.EvaluatePressure(0.9f), Is.EqualTo(1f).Within(0.000001f));
            Assert.That(BladderCurve.EvaluatePressure(1f), Is.EqualTo(3.5f).Within(0.000001f));
            Assert.That(
                BladderCurve.EvaluatePressure(0.5001f),
                Is.LessThan(0.000001f),
                "开始点附近不能突然跳变。 ");
            Assert.That(
                BladderCurve.EvaluatePressure(0.8999f),
                Is.EqualTo(1f).Within(0.000001f),
                "紧急点两侧应连续衔接。 ");
        }

        [Test]
        public void Opportunity_IsImpossibleBelowOnsetAndCertainAtUrgentPoint()
        {
            Assert.That(BladderCurve.ShouldOfferAction(0.5f, 0d), Is.False);
            Assert.That(BladderCurve.ShouldOfferAction(0.6f, 0d), Is.True);
            Assert.That(BladderCurve.ShouldOfferAction(0.6f, 0.999999d), Is.False);
            Assert.That(BladderCurve.ShouldOfferAction(0.9f, 0.999999d), Is.True);
        }

        [Test]
        public void Utility_UsesNeedSpecificCurveForBenefitAndEmergencyBoundary()
        {
            var toilet = new ResidentActionCandidate("toilet", "toilet", "使用厕所")
            {
                NeedEffects = new[] { new NeedEffect(ResidentNeed.Bladder, 1f) },
            };
            var engine = new UtilityDecisionEngine();

            ResidentDecisionResult belowOnset = engine.Decide(new ResidentDecisionContext(
                7,
                11UL,
                0,
                new[]
                {
                    new ResidentNeedState(
                        ResidentNeed.Bladder,
                        0.5f,
                        0f,
                        pressureCurve: BladderCurve),
                },
                new[] { toilet }));
            Assert.That(belowOnset.HasSelection, Is.False);
            Assert.That(belowOnset.Traces[0].Score.NeedBenefit, Is.Zero);

            ResidentDecisionResult urgent = engine.Decide(new ResidentDecisionContext(
                7,
                11UL,
                1,
                new[]
                {
                    new ResidentNeedState(
                        ResidentNeed.Bladder,
                        0.9f,
                        0f,
                        pressureCurve: BladderCurve),
                },
                new[] { toilet }));
            Assert.That(urgent.Selected, Is.SameAs(toilet));
            Assert.That(urgent.Traces[0].IsEmergency, Is.True);
            Assert.That(urgent.Traces[0].Score.NeedBenefit, Is.EqualTo(1f).Within(0.000001f));
        }

        [Test]
        public void InvalidOrDefaultCurve_FailsBeforeEnteringDecisionState()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _ = new NeedPressureCurve(0.5f, 0.5f));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _ = new NeedPressureCurve(0.5f, 0.9f, responseExponent: 0.5f));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _ = new NeedPressureCurve(0.5f, 0.9f, maximumPressure: 0.9f));
            Assert.Throws<InvalidOperationException>(() =>
                _ = default(NeedPressureCurve).EvaluatePressure(0.7f));
        }
    }
}
