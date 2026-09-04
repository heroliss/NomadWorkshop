using NUnit.Framework;

namespace Game.NomadWorkshop.Simulation.Tests
{
    /// <summary>锁定自由姿态、独立吸附、复合占地和连续摆放的原子语义。</summary>
    public sealed class ContinuousFacilityPlacementTests
    {
        private static readonly DeckBounds MainDeck = new(-5000, -4000, 5000, 4000);

        private static readonly ContinuousFacilityFootprint TwoByOne = new(
            new[] { new DeckFootprintPart(0, 0, 2000, 1000) });

        [Test]
        public void DeckPose_QuantizesMetersAndNormalizesArbitraryYaw()
        {
            DeckPose pose = DeckPose.FromMeters(1.2346d, -0.5006d, -15.04d, 2);

            Assert.That(pose.XMillimeters, Is.EqualTo(1235));
            Assert.That(pose.ZMillimeters, Is.EqualTo(-501));
            Assert.That(pose.YawDeciDegrees, Is.EqualTo(3450));
            Assert.That(pose.DeckLevel, Is.EqualTo(2));
            Assert.That(pose.XMeters, Is.EqualTo(1.235d).Within(0.000001d));
            Assert.That(pose.YawDegrees, Is.EqualTo(345d).Within(0.000001d));
        }

        [Test]
        public void DeckPose_TransformsLocalPoseWithUnityYawConvention()
        {
            var facility = new DeckPose(250, 500, 900, 2);

            DeckPose slot = facility.TransformLocal(1000, 300, -150);

            Assert.That(slot, Is.EqualTo(new DeckPose(550, -500, 750, 2)));
        }

        [Test]
        public void SnapSettings_ControlPositionAndRotationIndependently()
        {
            var raw = new DeckPose(1137, -361, 143);

            DeckPose positionOnly = new DeckPlacementSnapSettings(250, 0).Apply(raw);
            DeckPose rotationOnly = new DeckPlacementSnapSettings(0, 150).Apply(raw);
            DeckPose disabled = DeckPlacementSnapSettings.Disabled.Apply(raw);
            DeckPose recommended = DeckPlacementSnapSettings.RecommendedDefault.Apply(raw);

            Assert.That(positionOnly, Is.EqualTo(new DeckPose(1250, -250, 143)));
            Assert.That(rotationOnly, Is.EqualTo(new DeckPose(1137, -361, 150)));
            Assert.That(disabled, Is.EqualTo(raw));
            Assert.That(recommended, Is.EqualTo(new DeckPose(1200, -400, 0)));
        }

        [Test]
        public void TryPlace_AcceptsArbitraryAngleInsideDeck()
        {
            var ledger = new ContinuousFacilityPlacementLedger(MainDeck);
            ContinuousFacilityPlacementRequest request = Request(
                "kitchen-27",
                new DeckPose(500, -300, 270),
                new ContinuousFacilityFootprint(
                    new[] { new DeckFootprintPart(0, 0, 3000, 1000) }));

            bool succeeded = ledger.TryPlace(
                request,
                out ContinuousPlacedFacility placement,
                out ContinuousPlacementFailure failure);

            Assert.That(succeeded, Is.True);
            Assert.That(failure, Is.EqualTo(ContinuousPlacementFailure.None));
            Assert.That(placement.Pose, Is.EqualTo(request.Pose));
            Assert.That(ledger.Count, Is.EqualTo(1));
        }

        [Test]
        public void TryPlace_RejectsRotatedCornerOutsideDeckWithoutMutation()
        {
            var ledger = new ContinuousFacilityPlacementLedger(
                new DeckBounds(-1000, -1000, 1000, 1000));
            ContinuousFacilityPlacementRequest request = Request(
                "edge",
                new DeckPose(500, 0, 450),
                new ContinuousFacilityFootprint(
                    new[] { new DeckFootprintPart(0, 0, 1600, 800) }));

            bool succeeded = ledger.TryPlace(request, out var placement, out var failure);

            Assert.That(succeeded, Is.False);
            Assert.That(placement, Is.Null);
            Assert.That(failure, Is.EqualTo(ContinuousPlacementFailure.FootprintOutOfBounds));
            Assert.That(ledger.Count, Is.Zero);
        }

        [Test]
        public void TryPlace_RotatesOffsetPartClockwiseLikeUnityPositiveYaw()
        {
            var ledger = new ContinuousFacilityPlacementLedger(
                new DeckBounds(-300, -1300, 300, -700));
            var offsetPart = new ContinuousFacilityFootprint(
                new[] { new DeckFootprintPart(1000, 0, 400, 400) });

            bool succeeded = ledger.TryPlace(
                Request("rotated-offset", new DeckPose(0, 0, 900), offsetPart),
                out _,
                out ContinuousPlacementFailure failure);

            Assert.That(succeeded, Is.True);
            Assert.That(failure, Is.EqualTo(ContinuousPlacementFailure.None));
        }

        [Test]
        public void Evaluate_AllowsTouchingEdgesButRejectsRealOverlap()
        {
            var ledger = new ContinuousFacilityPlacementLedger(MainDeck);
            Assert.That(
                ledger.TryPlace(Request("first", new DeckPose(0, 0, 0)), out _, out _),
                Is.True);

            ContinuousPlacementFailure touching = ledger.Evaluate(
                Request("touching", new DeckPose(2000, 0, 0)));
            ContinuousPlacementFailure overlapping = ledger.Evaluate(
                Request("overlapping", new DeckPose(1999, 0, 0)));

            Assert.That(touching, Is.EqualTo(ContinuousPlacementFailure.None));
            Assert.That(
                overlapping,
                Is.EqualTo(ContinuousPlacementFailure.FootprintOverlapsFacility));
        }

        [Test]
        public void Evaluate_TreatsCompoundFootprintAsPartsInsteadOfOneLargeBox()
        {
            var ledger = new ContinuousFacilityPlacementLedger(MainDeck);
            var splitFootprint = new ContinuousFacilityFootprint(new[]
            {
                new DeckFootprintPart(-1000, 0, 1000, 1000),
                new DeckFootprintPart(1000, 0, 1000, 1000),
            });
            Assert.That(
                ledger.TryPlace(
                    Request("split", new DeckPose(0, 0, 0), splitFootprint),
                    out _,
                    out _),
                Is.True);

            ContinuousPlacementFailure inGap = ledger.Evaluate(
                Request(
                    "gap",
                    new DeckPose(0, 0, 0),
                    new ContinuousFacilityFootprint(
                        new[] { new DeckFootprintPart(0, 0, 800, 800) })));
            ContinuousPlacementFailure onRightPart = ledger.Evaluate(
                Request(
                    "right-hit",
                    new DeckPose(1000, 0, 0),
                    new ContinuousFacilityFootprint(
                        new[] { new DeckFootprintPart(0, 0, 800, 800) })));

            Assert.That(inGap, Is.EqualTo(ContinuousPlacementFailure.None));
            Assert.That(
                onRightPart,
                Is.EqualTo(ContinuousPlacementFailure.FootprintOverlapsFacility));
        }

        [Test]
        public void ContainsPoint_UsesCompoundPartsAndArbitraryFacilityRotation()
        {
            var footprint = new ContinuousFacilityFootprint(new[]
            {
                new DeckFootprintPart(1000, 0, 800, 400),
                new DeckFootprintPart(-1000, 0, 400, 800, 450),
            });
            var facility = new DeckPose(500, -250, 900, 1);

            Assert.That(
                footprint.ContainsPoint(
                    facility,
                    new DeckPose(500, -1250, 0, 1)),
                Is.True,
                "设施旋转后，偏移部件应使用与 Unity 一致的顺时针 Yaw。");
            Assert.That(
                footprint.ContainsPoint(
                    facility,
                    new DeckPose(500, 750, 0, 1)),
                Is.True,
                "复合占地中任一部件命中即算悬停命中。");
            Assert.That(
                footprint.ContainsPoint(
                    facility,
                    new DeckPose(500, -250, 0, 1)),
                Is.False,
                "两部件之间的空隙不应被整体 Bounds 误判为命中。");
            Assert.That(
                footprint.ContainsPoint(
                    facility,
                    new DeckPose(500, -1250, 0, 0)),
                Is.False,
                "其他甲板层不应命中。");
        }

        [Test]
        public void TryPlace_SeparatesIndependentDeckLevels()
        {
            var ledger = new ContinuousFacilityPlacementLedger(new[]
            {
                MainDeck,
                new DeckBounds(-5000, -4000, 5000, 4000, 1),
            });

            Assert.That(
                ledger.TryPlace(Request("lower", new DeckPose(0, 0, 0, 0)), out _, out _),
                Is.True);
            Assert.That(
                ledger.TryPlace(Request("upper", new DeckPose(0, 0, 0, 1)), out _, out _),
                Is.True);
            Assert.That(ledger.Count, Is.EqualTo(2));
        }

        [Test]
        public void FailedPlacementAndRemoval_KeepLedgerAtomic()
        {
            var ledger = new ContinuousFacilityPlacementLedger(MainDeck);
            Assert.That(
                ledger.TryPlace(Request("first", new DeckPose(0, 0, 0)), out _, out _),
                Is.True);

            Assert.That(
                ledger.TryPlace(
                    Request("first", new DeckPose(3500, 0, 0)),
                    out _,
                    out ContinuousPlacementFailure duplicate),
                Is.False);
            Assert.That(duplicate, Is.EqualTo(ContinuousPlacementFailure.DuplicateInstanceId));

            ContinuousFacilityPlacementRequest replacement =
                Request("replacement", new DeckPose(100, 0, 0));
            Assert.That(ledger.TryPlace(replacement, out _, out var overlap), Is.False);
            Assert.That(overlap, Is.EqualTo(ContinuousPlacementFailure.FootprintOverlapsFacility));
            Assert.That(ledger.Count, Is.EqualTo(1));

            Assert.That(ledger.Remove("first"), Is.True);
            Assert.That(ledger.Remove("first"), Is.False);
            Assert.That(ledger.TryPlace(replacement, out var placed, out _), Is.True);
            Assert.That(ledger.TryGetPlacement("replacement", out var found), Is.True);
            Assert.That(found, Is.SameAs(placed));
        }

        [Test]
        public void StableSnapshot_SortsIdentityIndependentlyOfInsertionOrder()
        {
            var ledger = new ContinuousFacilityPlacementLedger(MainDeck);
            Assert.That(
                ledger.TryPlace(Request("facility-b", new DeckPose(-2500, 0, 270)), out _, out _),
                Is.True);
            Assert.That(
                ledger.TryPlace(Request("facility-a", new DeckPose(2500, 0, 123)), out _, out _),
                Is.True);

            var snapshot = ledger.CreateStableSnapshot();

            Assert.That(snapshot.Count, Is.EqualTo(2));
            Assert.That(snapshot[0].InstanceId, Is.EqualTo("facility-a"));
            Assert.That(snapshot[1].InstanceId, Is.EqualTo("facility-b"));
        }

        private static ContinuousFacilityPlacementRequest Request(
            string instanceId,
            DeckPose pose,
            ContinuousFacilityFootprint footprint = null) =>
            new(instanceId, "field-kitchen", pose, footprint ?? TwoByOne);
    }
}
