using NUnit.Framework;

namespace Game.NomadWorkshop.Simulation.Tests
{
    public sealed class PlacementRegionLedgerTests
    {
        private static readonly PlacementFootprint WaterCan = new(
            "water-can",
            "water-can",
            340,
            240,
            590,
            20,
            new[] { 0, 900 });

        [Test]
        public void StablePlacement_UsesAllowedRotationThatFitsRegion()
        {
            var ledger = new PlacementRegionLedger();
            PlacementRegionDefinition region = Region(
                "rack",
                new DeckPose(1000, 500, 450),
                width: 320,
                depth: 420,
                inset: 10);
            Assert.That(ledger.RegisterRegion(region), Is.EqualTo(PlacementRegionFailure.None));

            Assert.That(
                ledger.TryReserveStable(
                    "can-01",
                    WaterCan,
                    region.RegionId,
                    out PlacementRegionReservation reservation,
                    out PlacementRegionFailure failure),
                Is.True,
                failure.ToString());
            using (reservation)
            {
                Assert.That(reservation.Candidate.LocalPose.LocalYawDeciDegrees, Is.EqualTo(900));
                Assert.That(reservation.TryCommit(out PlacementRegionItem placed, out failure), Is.True);
                Assert.That(placed.WorldPose, Is.EqualTo(
                    region.Pose.TransformLocal(
                        placed.LocalPose.LocalXMillimeters,
                        placed.LocalPose.LocalZMillimeters,
                        placed.LocalPose.LocalYawDeciDegrees)));
            }
        }

        [Test]
        public void Reservation_BlocksSameSpaceUntilCancelled()
        {
            var ledger = new PlacementRegionLedger();
            PlacementRegionDefinition region = Region(
                "single",
                default,
                width: 400,
                depth: 300,
                inset: 10);
            ledger.RegisterRegion(region);

            Assert.That(
                ledger.TryReserveStable(
                    "can-01",
                    WaterCan,
                    region.RegionId,
                    out PlacementRegionReservation first,
                    out PlacementRegionFailure failure),
                Is.True,
                failure.ToString());
            Assert.That(
                ledger.TryReserveStable(
                    "can-02",
                    WaterCan,
                    region.RegionId,
                    out _,
                    out failure),
                Is.False);
            Assert.That(failure, Is.EqualTo(PlacementRegionFailure.RegionFull));

            first.Dispose();
            Assert.That(ledger.ReservationCount, Is.Zero);
            Assert.That(
                ledger.TryReserveStable(
                    "can-02",
                    WaterCan,
                    region.RegionId,
                    out PlacementRegionReservation second,
                    out failure),
                Is.True,
                failure.ToString());
            second.Dispose();
        }

        [Test]
        public void ExactPose_RejectsOrientationBoundaryAndCrossRegionWorldOverlap()
        {
            var ledger = new PlacementRegionLedger();
            PlacementRegionDefinition left = Region(
                "left",
                default,
                width: 500,
                depth: 400,
                inset: 10);
            PlacementRegionDefinition overlapping = Region(
                "overlapping",
                new DeckPose(100, 0, 0),
                width: 500,
                depth: 400,
                inset: 10);
            ledger.RegisterRegion(left);
            ledger.RegisterRegion(overlapping);

            Assert.That(
                ledger.TryRestorePlacement(
                    "can-01",
                    WaterCan,
                    left.RegionId,
                    PlacementRegionPose.Centered,
                    out _,
                    out PlacementRegionFailure failure),
                Is.True,
                failure.ToString());
            Assert.That(
                ledger.TryReserveExact(
                    "can-02",
                    WaterCan,
                    overlapping.RegionId,
                    PlacementRegionPose.Centered,
                    out _,
                    out failure),
                Is.False);
            Assert.That(failure, Is.EqualTo(PlacementRegionFailure.PoseOverlapsItem));

            Assert.That(
                ledger.TryReserveExact(
                    "can-03",
                    WaterCan,
                    left.RegionId,
                    new PlacementRegionPose(0, 0, 450),
                    out _,
                    out failure),
                Is.False);
            Assert.That(failure, Is.EqualTo(PlacementRegionFailure.OrientationRejected));

            Assert.That(
                ledger.TryReserveExact(
                    "can-04",
                    WaterCan,
                    left.RegionId,
                    new PlacementRegionPose(200, 0, 0),
                    out _,
                    out failure),
                Is.False);
            Assert.That(failure, Is.EqualTo(PlacementRegionFailure.PoseOutsideRegion));
        }

        [Test]
        public void StableSnapshot_RestoresExactRegionLocalPose()
        {
            var source = new PlacementRegionLedger();
            PlacementRegionDefinition sourceRegion = Region(
                "table",
                new DeckPose(1200, -700, 900),
                width: 1000,
                depth: 500,
                inset: 25);
            source.RegisterRegion(sourceRegion);
            Assert.That(
                source.TryReserveStable(
                    "can-01",
                    WaterCan,
                    sourceRegion.RegionId,
                    out PlacementRegionReservation reservation,
                    out PlacementRegionFailure failure),
                Is.True,
                failure.ToString());
            Assert.That(reservation.TryCommit(out PlacementRegionItem original, out failure), Is.True);

            var restored = new PlacementRegionLedger();
            restored.RegisterRegion(Region(
                "table",
                new DeckPose(1200, -700, 900),
                width: 1000,
                depth: 500,
                inset: 25));
            Assert.That(
                restored.TryRestorePlacement(
                    original.ItemId,
                    WaterCan,
                    original.Region.RegionId,
                    original.LocalPose,
                    out PlacementRegionItem roundTripped,
                    out failure),
                Is.True,
                failure.ToString());
            Assert.That(roundTripped.LocalPose, Is.EqualTo(original.LocalPose));
            Assert.That(roundTripped.WorldPose, Is.EqualTo(original.WorldPose));
        }

        [Test]
        public void ExactPose_AllowsArbitraryYawWhenFootprintOptsIn()
        {
            var ledger = new PlacementRegionLedger();
            var region = new PlacementRegionDefinition(
                PlacementRegionLedger.ComposeRegionId("counter", "surface"),
                "surface",
                "counter",
                default,
                300,
                300,
                970,
                10,
                new[] { "cup" });
            var cup = new PlacementFootprint(
                "drinking-cup",
                "cup",
                90,
                70,
                120,
                10,
                new[] { 0 },
                allowsAnyYaw: true);
            ledger.RegisterRegion(region);

            var requestedPose = new PlacementRegionPose(20, -10, 370);
            Assert.That(
                ledger.TryRestorePlacement(
                    "cup-01",
                    cup,
                    region.RegionId,
                    requestedPose,
                    out PlacementRegionItem placed,
                    out PlacementRegionFailure failure),
                Is.True,
                failure.ToString());
            Assert.That(placed.LocalPose, Is.EqualTo(requestedPose));
            Assert.That(placed.WorldPose.YawDeciDegrees, Is.EqualTo(370));
        }

        [Test]
        public void OneRegion_PacksDifferentFootprintsAndRejectsOnlyTrueOverlap()
        {
            var ledger = new PlacementRegionLedger();
            var region = new PlacementRegionDefinition(
                PlacementRegionLedger.ComposeRegionId("counter", "surface"),
                "surface",
                "counter",
                default,
                500,
                240,
                970,
                10,
                new[] { "cup", "plate" });
            var cup = new PlacementFootprint(
                "cup",
                "cup",
                90,
                90,
                120,
                5,
                new[] { 0 });
            var plate = new PlacementFootprint(
                "plate",
                "plate",
                160,
                100,
                30,
                5,
                new[] { 0 });
            ledger.RegisterRegion(region);

            Assert.That(
                ledger.TryRestorePlacement(
                    "cup-01",
                    cup,
                    region.RegionId,
                    new PlacementRegionPose(-150, 0, 0),
                    out _,
                    out PlacementRegionFailure failure),
                Is.True,
                failure.ToString());
            Assert.That(
                ledger.TryRestorePlacement(
                    "plate-01",
                    plate,
                    region.RegionId,
                    new PlacementRegionPose(80, 0, 0),
                    out _,
                    out failure),
                Is.True,
                failure.ToString());
            Assert.That(ledger.PlacedItemCount, Is.EqualTo(2));

            Assert.That(
                ledger.TryReserveExact(
                    "cup-02",
                    cup,
                    region.RegionId,
                    PlacementRegionPose.Centered,
                    out _,
                    out failure),
                Is.False);
            Assert.That(failure, Is.EqualTo(PlacementRegionFailure.PoseOverlapsItem));
            Assert.That(ledger.PlacedItemCount, Is.EqualTo(2));
        }

        private static PlacementRegionDefinition Region(
            string owner,
            DeckPose pose,
            int width,
            int depth,
            int inset) =>
            new(
                PlacementRegionLedger.ComposeRegionId(owner, "water-can-parking"),
                "water-can-parking",
                owner,
                pose,
                width,
                depth,
                0,
                inset,
                new[] { "water-can" });
    }
}
