using System;
using Game.NomadWorkshop.Simulation;
using NUnit.Framework;

namespace Game.NomadWorkshop.Simulation.Tests
{
    public sealed class ContinuousDeckReachabilityProbeTests
    {
        private static readonly DeckBounds Bounds = new(-2500, -2000, 2500, 2000);

        [Test]
        public void OpenDeck_ConnectsArbitraryContinuousPoints()
        {
            var probe = CreateProbe();

            Assert.That(
                probe.Rebuild(
                    Array.Empty<ContinuousPlacedFacility>(),
                    null,
                    new DeckPose(-1800, -900, 0)),
                Is.True);
            Assert.That(probe.IsReachable(new DeckPose(1700, 1100, 271)), Is.True);
            Assert.That(probe.VisitedCellCount, Is.GreaterThan(0));
        }

        [Test]
        public void CandidateWall_DisconnectsOppositeSidesWithoutBecomingPlacementTruth()
        {
            var ledger = new ContinuousFacilityPlacementLedger(Bounds);
            ContinuousFacilityPlacementRequest wall = Request(
                "preview-wall",
                new DeckPose(0, 0, 0),
                new DeckFootprintPart(0, 0, 180, 4000));
            var probe = CreateProbe();

            Assert.That(
                probe.Rebuild(
                    ledger.CreateStableSnapshot(),
                    wall,
                    new DeckPose(-1600, 0, 0)),
                Is.True);
            Assert.That(probe.IsReachable(new DeckPose(1600, 0, 0)), Is.False);
            Assert.That(ledger.Count, Is.Zero, "实时预检不得把候选写入正式摆放账本。");
        }

        [Test]
        public void CandidateCoveringOrigin_HasNoReachableSafetyAnchor()
        {
            ContinuousFacilityPlacementRequest candidate = Request(
                "cover-origin",
                new DeckPose(0, 0, 0),
                new DeckFootprintPart(0, 0, 1200, 1200));
            var probe = CreateProbe();

            Assert.That(
                probe.Rebuild(
                    Array.Empty<ContinuousPlacedFacility>(),
                    candidate,
                    new DeckPose(0, 0, 0)),
                Is.False);
        }

        [Test]
        public void DynamicResidentCoveringCandidate_CanResolveNearestEvacuationOrigin()
        {
            ContinuousFacilityPlacementRequest candidate = Request(
                "cover-resident",
                new DeckPose(0, 0, 0),
                new DeckFootprintPart(0, 0, 1200, 1200));
            var probe = CreateProbe();

            Assert.That(
                probe.RebuildAllowingOriginRelocation(
                    Array.Empty<ContinuousPlacedFacility>(),
                    candidate,
                    new DeckPose(0, 0, 0),
                    out DeckPose resolvedOrigin),
                Is.True);
            Assert.That(resolvedOrigin, Is.Not.EqualTo(new DeckPose(0, 0, 0)));
            Assert.That(
                candidate.Footprint.ContainsPoint(
                    candidate.Pose,
                    resolvedOrigin,
                    paddingMillimeters: 340),
                Is.False,
                "施工避让点必须位于居民净空扩张后的候选占地之外。");
            Assert.That(probe.IsReachable(resolvedOrigin), Is.True);
        }

        [Test]
        public void RotatedObstacle_UsesContinuousUnityYawInsteadOfQuarterTurns()
        {
            ContinuousFacilityPlacementRequest candidate = Request(
                "rotated",
                new DeckPose(0, 0, 370),
                new DeckFootprintPart(0, 0, 2200, 500));
            var probe = CreateProbe();

            Assert.That(
                probe.Rebuild(
                    Array.Empty<ContinuousPlacedFacility>(),
                    candidate,
                    new DeckPose(-1800, 1200, 0)),
                Is.True);
            Assert.That(probe.IsReachable(new DeckPose(1800, -1200, 0)), Is.True);
            Assert.That(probe.IsReachable(new DeckPose(0, 0, 0)), Is.False);
        }

        private static ContinuousDeckReachabilityProbe CreateProbe() =>
            new(Bounds, 250, 340, 360);

        private static ContinuousFacilityPlacementRequest Request(
            string id,
            DeckPose pose,
            params DeckFootprintPart[] parts) =>
            new(id, "test", pose, new ContinuousFacilityFootprint(parts));
    }
}
