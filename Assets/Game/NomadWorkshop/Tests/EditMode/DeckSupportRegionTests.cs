using System;
using System.Collections.Generic;
using Game.NomadWorkshop.Simulation;
using NUnit.Framework;

namespace Game.NomadWorkshop.Simulation.Tests
{
    public sealed class DeckSupportRegionTests
    {
        private static readonly DeckBounds Outer = new(-2000, -2000, 2000, 2000);
        private static ContinuousFacilityFootprint Footprint(int width, int depth) =>
            new(new[] { new DeckFootprintPart(0, 0, width, depth) });
        private static ContinuousFacilityPlacementRequest Request(DeckPose pose, int width = 800, int depth = 800) =>
            new("candidate", "test", pose, Footprint(width, depth));

        [TestCase(0)]
        [TestCase(237)]
        [TestCase(450)]
        [TestCase(1379)]
        public void AdjacentPlates_SupportRotatedFootprintAcrossSeam(int yaw)
        {
            var support = new DeckSupportRegion(new[] {
                new DeckBounds(-2000, -2000, 0, 2000), new DeckBounds(0, -2000, 2000, 2000) });
            var ledger = new ContinuousFacilityPlacementLedger(new[] { support });
            Assert.That(ledger.TryPlace(Request(new DeckPose(0, 0, yaw), 1600, 900), out _, out var failure), Is.True);
            Assert.That(failure, Is.EqualTo(ContinuousPlacementFailure.None));
            Assert.That(support.Surfaces.Count, Is.EqualTo(1), "板缝应合并为连续物理面。");
        }

        [Test]
        public void HoleInsideFootprint_IsRejectedEvenWhenCenterAndAllCornersHaveSupport()
        {
            var support = new DeckSupportRegion(new[] { Outer }, new[] { new DeckBounds(201, 201, 202, 202) });
            Assert.That(support.Contains(new DeckPose(0, 0, 0)), Is.True);
            foreach (int x in new[] { -500, 500 })
                foreach (int z in new[] { -500, 500 })
                    Assert.That(support.Contains(new DeckPose(x, z, 0)), Is.True);
            var ledger = new ContinuousFacilityPlacementLedger(new[] { support });
            Assert.That(ledger.TryPlace(Request(new DeckPose(0, 0, 0), 1000, 1000), out _, out var failure), Is.False);
            Assert.That(failure, Is.EqualTo(ContinuousPlacementFailure.FootprintOutOfBounds));
            Assert.That(ledger.Count, Is.Zero);
        }

        [Test]
        public void UShape_GapBetweenSupportedArmsRejectsBridgeFootprint()
        {
            var support = new DeckSupportRegion(new[] { Outer }, new[] { new DeckBounds(-200, 0, 200, 2000) });
            Assert.That(support.Covers(new DeckPose(0, 800, 0), Footprint(1600, 300)), Is.False);
            Assert.That(support.Covers(new DeckPose(0, -800, 273), Footprint(1600, 300)), Is.True);
        }

        [Test]
        public void LShape_RotatedPartCrossingConcavityFails()
        {
            var support = new DeckSupportRegion(new[] {
                new DeckBounds(-2000, -2000, 2000, 0), new DeckBounds(-2000, 0, 0, 2000) });
            Assert.That(support.Contains(new DeckPose(-100, -100, 0)), Is.True);
            Assert.That(support.Covers(new DeckPose(-100, -100, 450), Footprint(1200, 300)), Is.False);
            Assert.That(support.Covers(new DeckPose(-1000, -1000, 450), Footprint(1200, 300)), Is.True);
        }

        [Test]
        public void FunctionalClearance_RequiresSupportIndependentlyOfBody()
        {
            var support = new DeckSupportRegion(new[] { Outer }, new[] { new DeckBounds(800, -100, 900, 100) });
            var clearance = new ContinuousFacilityFootprint(new[] { new DeckFootprintPart(850, 0, 300, 300) });
            var request = new ContinuousFacilityPlacementRequest("kitchen", "test", new DeckPose(0, 0, 0),
                Footprint(500, 500), clearance);
            var ledger = new ContinuousFacilityPlacementLedger(new[] { support });
            Assert.That(ledger.Evaluate(request), Is.EqualTo(ContinuousPlacementFailure.FunctionalClearanceOutOfBounds));
        }

        [Test]
        public void ContactAtHoleEdgeIsAllowed_OneMillimeterOverlapIsNot()
        {
            var support = new DeckSupportRegion(new[] { Outer }, new[] { new DeckBounds(0, -2000, 2000, 2000) });
            Assert.That(support.Covers(new DeckPose(-500, 0, 0), Footprint(1000, 1000)), Is.True);
            Assert.That(support.Covers(new DeckPose(-499, 0, 0), Footprint(1000, 1000)), Is.False);
        }

        [Test]
        public void InputsAreCopied_ReorderedOverlappingPlatesProduceSameSurfaces()
        {
            var plates = new[] { Outer, new DeckBounds(-1000, -1000, 3000, 1000), Outer };
            var holes = new[] { new DeckBounds(-100, -100, 100, 100) };
            var first = new DeckSupportRegion(plates, holes);
            Array.Reverse(plates);
            var second = new DeckSupportRegion(plates, holes);
            plates[0] = new DeckBounds(7000, 7000, 8000, 8000);
            holes[0] = Outer;
            Assert.That(first.Surfaces, Is.EqualTo(second.Surfaces));
            Assert.That(first.Contains(new DeckPose(2500, 0, 0)), Is.True);
            Assert.That(first.Contains(new DeckPose(0, 0, 0)), Is.False);
            Assert.That(() => ((IList<DeckBounds>)first.Surfaces).Clear(), Throws.TypeOf<NotSupportedException>());
        }

        [Test]
        public void DecompositionHasExactAreaAndNoOverlappingSurfaces()
        {
            var support = new DeckSupportRegion(new[] { Outer, Outer }, new[] { new DeckBounds(-1000, -1000, 1000, 1000) });
            long area = 0;
            for (var i = 0; i < support.Surfaces.Count; i++)
            {
                var a = support.Surfaces[i];
                area += (long)(a.MaxXMillimeters - a.MinXMillimeters) * (a.MaxZMillimeters - a.MinZMillimeters);
                for (var j = 0; j < i; j++)
                {
                    var b = support.Surfaces[j];
                    bool overlaps = a.MinXMillimeters < b.MaxXMillimeters && b.MinXMillimeters < a.MaxXMillimeters &&
                        a.MinZMillimeters < b.MaxZMillimeters && b.MinZMillimeters < a.MaxZMillimeters;
                    Assert.That(overlaps, Is.False);
                }
            }
            Assert.That(area, Is.EqualTo(12000000));
        }

        [Test]
        public void InvalidAndMixedLevelSourcesAreRejected()
        {
            Assert.That(() => new DeckSupportRegion(default(DeckBounds)), Throws.ArgumentException);
            Assert.That(() => new DeckSupportRegion(Array.Empty<DeckBounds>()), Throws.ArgumentException);
            Assert.That(() => new DeckSupportRegion(new[] { Outer }, new[] { Outer }), Throws.ArgumentException);
            Assert.That(() => new DeckSupportRegion(new[] { Outer, new DeckBounds(0, 0, 1, 1, 1) }), Throws.ArgumentException);
            var upper = new DeckSupportRegion(new DeckBounds(-2000, -2000, 2000, 2000, 1));
            Assert.That(upper.Contains(new DeckPose(0, 0, 0)), Is.False);
            Assert.That(upper.Contains(new DeckPose(0, 0, 0, 1)), Is.True);
            var ledger = new ContinuousFacilityPlacementLedger(new[] { upper });
            Assert.That(ledger.Evaluate(Request(new DeckPose(0, 0, 0))), Is.EqualTo(ContinuousPlacementFailure.DeckLevelUnavailable));
        }

        [Test]
        public void PreviewDoesNotJumpOneMillimeterGapBetweenSamplesOrSnapAcrossIt()
        {
            var support = new DeckSupportRegion(new[] { Outer }, new[] { new DeckBounds(51, -2000, 52, 2000) });
            var probe = new ContinuousDeckReachabilityProbe(support, 100, 0, 200);
            Assert.That(probe.Rebuild(Array.Empty<ContinuousPlacedFacility>(), null, new DeckPose(-500, 0, 0)), Is.True);
            Assert.That(probe.IsReachable(new DeckPose(55, 0, 0)), Is.False,
                "端点离左侧采样更近，也不能穿过缺口对齐。");
            Assert.That(probe.IsReachable(new DeckPose(1000, 0, 0)), Is.False);
            Assert.That(probe.IsReachable(new DeckPose(0, 0, 0)), Is.True);
        }

        [Test]
        public void PreviewCanGoAroundHole_AndRebuildRestoresSupportMask()
        {
            var support = new DeckSupportRegion(new[] { Outer }, new[] { new DeckBounds(-400, -400, 400, 400) });
            var probe = new ContinuousDeckReachabilityProbe(support, 200, 100, 150);
            var origin = new DeckPose(-1200, 0, 0);
            Assert.That(probe.Rebuild(Array.Empty<ContinuousPlacedFacility>(), Request(origin), origin), Is.False);
            Assert.That(probe.Rebuild(Array.Empty<ContinuousPlacedFacility>(), null, origin), Is.True);
            Assert.That(probe.IsReachable(new DeckPose(1200, 0, 0)), Is.True);
            Assert.That(probe.IsReachable(new DeckPose(0, 0, 0)), Is.False);
        }

        [Test]
        public void DiagonalTouchAndTooNarrowBridgeCannotCarryResidentClearance()
        {
            var diagonal = new DeckSupportRegion(new[] {
                new DeckBounds(-1000, -1000, 0, 0), new DeckBounds(0, 0, 1000, 1000) });
            var probe = new ContinuousDeckReachabilityProbe(diagonal, 100, 100, 100);
            Assert.That(probe.Rebuild(Array.Empty<ContinuousPlacedFacility>(), null, new DeckPose(-500, -500, 0)), Is.True);
            Assert.That(probe.IsReachable(new DeckPose(500, 500, 0)), Is.False);
            var bridge = new DeckSupportRegion(new[] {
                new DeckBounds(-2000, -1000, -500, 1000), new DeckBounds(500, -1000, 2000, 1000),
                new DeckBounds(-500, -99, 500, 99) });
            probe = new ContinuousDeckReachabilityProbe(bridge, 100, 100, 100);
            Assert.That(probe.Rebuild(Array.Empty<ContinuousPlacedFacility>(), null, new DeckPose(-1200, 0, 0)), Is.True);
            Assert.That(probe.IsReachable(new DeckPose(1200, 0, 0)), Is.False);
        }
    }
}
