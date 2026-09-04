using System.Collections.Generic;
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

        [Test]
        public void MoveLease_HoldsSourceRecoveryAndDestinationUntilAtomicDelivery()
        {
            var ledger = new PlacementRegionLedger();
            PlacementRegionDefinition source = CupSurface("counter-a", -300);
            PlacementRegionDefinition destination = CupSurface("counter-b", 300);
            PlacementFootprint cup = CupFootprint();
            ledger.RegisterRegion(source);
            ledger.RegisterRegion(destination);
            Assert.That(
                ledger.TryRestorePlacement(
                    "cup-01",
                    cup,
                    source.RegionId,
                    new PlacementRegionPose(15, -20, 370),
                    out _,
                    out PlacementRegionFailure failure),
                Is.True,
                failure.ToString());

            var targetPose = new PlacementRegionPose(20, 10, 1230);
            Assert.That(
                ledger.TryReserveMoveExact(
                    "cup-01",
                    destination.RegionId,
                    targetPose,
                    out PlacementRegionMoveLease move,
                    out failure),
                Is.True,
                failure.ToString());
            Assert.That(move.State, Is.EqualTo(PlacementRegionMoveState.Reserved));
            Assert.That(ledger.ReservationCount, Is.EqualTo(1));
            Assert.That(ledger.PlacedItemCount, Is.EqualTo(1));

            Assert.That(move.TryPickUp(out failure), Is.True, failure.ToString());
            Assert.That(move.State, Is.EqualTo(PlacementRegionMoveState.Carrying));
            Assert.That(ledger.TryGetPlacement("cup-01", out _), Is.False);
            Assert.That(ledger.PlacedItemCount, Is.Zero);
            IReadOnlyList<PlacementRegionItem> checkpointWhileCarrying =
                ledger.CreateCheckpointSnapshot();
            Assert.That(checkpointWhileCarrying, Has.Count.EqualTo(1));
            Assert.That(
                checkpointWhileCarrying[0].Region.RegionId,
                Is.EqualTo(source.RegionId),
                "随时存档必须回到拿取前的守恒位置，而不是丢失半途携带物。 ");
            Assert.That(
                checkpointWhileCarrying[0].LocalPose,
                Is.EqualTo(new PlacementRegionPose(15, -20, 370)));

            Assert.That(
                ledger.TryRestorePlacement(
                    "cup-source-thief",
                    cup,
                    source.RegionId,
                    new PlacementRegionPose(15, -20, 0),
                    out _,
                    out failure),
                Is.False,
                "拿起后来源仍应为可中断恢复保留。 ");
            Assert.That(failure, Is.EqualTo(PlacementRegionFailure.PoseOverlapsItem));
            Assert.That(
                ledger.TryRestorePlacement(
                    "cup-target-thief",
                    cup,
                    destination.RegionId,
                    targetPose,
                    out _,
                    out failure),
                Is.False,
                "目标姿态必须从行动开始一直预留到放下。 ");
            Assert.That(failure, Is.EqualTo(PlacementRegionFailure.PoseOverlapsItem));

            Assert.That(
                move.TryDeliver(out PlacementRegionItem delivered, out failure),
                Is.True,
                failure.ToString());
            Assert.That(move.State, Is.EqualTo(PlacementRegionMoveState.Delivered));
            Assert.That(move.IsActive, Is.False);
            Assert.That(ledger.ReservationCount, Is.Zero);
            Assert.That(ledger.PlacedItemCount, Is.EqualTo(1));
            Assert.That(delivered.Region.RegionId, Is.EqualTo(destination.RegionId));
            Assert.That(delivered.LocalPose, Is.EqualTo(targetPose));
            Assert.That(
                ledger.CreateCheckpointSnapshot()[0].Region.RegionId,
                Is.EqualTo(destination.RegionId));
        }

        [Test]
        public void MoveLease_CancelAfterPickupRestoresExactSourceAndReleasesTarget()
        {
            var ledger = new PlacementRegionLedger();
            PlacementRegionDefinition source = CupSurface("counter-a", -300);
            PlacementRegionDefinition destination = CupSurface("counter-b", 300);
            PlacementFootprint cup = CupFootprint();
            ledger.RegisterRegion(source);
            ledger.RegisterRegion(destination);
            var sourcePose = new PlacementRegionPose(-25, 30, 2170);
            Assert.That(
                ledger.TryRestorePlacement(
                    "cup-01",
                    cup,
                    source.RegionId,
                    sourcePose,
                    out PlacementRegionItem original,
                    out PlacementRegionFailure failure),
                Is.True,
                failure.ToString());
            Assert.That(
                ledger.TryReserveMoveStable(
                    "cup-01",
                    destination.RegionId,
                    out PlacementRegionMoveLease move,
                    out failure),
                Is.True,
                failure.ToString());
            Assert.That(move.TryPickUp(out failure), Is.True, failure.ToString());

            move.Dispose();

            Assert.That(move.State, Is.EqualTo(PlacementRegionMoveState.Cancelled));
            Assert.That(ledger.ReservationCount, Is.Zero);
            Assert.That(
                ledger.TryGetPlacement("cup-01", out PlacementRegionItem restored),
                Is.True);
            Assert.That(restored.Region.RegionId, Is.EqualTo(source.RegionId));
            Assert.That(restored.LocalPose, Is.EqualTo(sourcePose));
            Assert.That(restored.WorldPose, Is.EqualTo(original.WorldPose));
            Assert.That(
                ledger.TryRestorePlacement(
                    "cup-02",
                    cup,
                    destination.RegionId,
                    PlacementRegionPose.Centered,
                    out _,
                    out failure),
                Is.True,
                "取消后目标空间应立即释放。 ");
        }

        [Test]
        public void UseLease_PickUpThenConsumeCommitsItemDisappearance()
        {
            var ledger = new PlacementRegionLedger();
            PlacementRegionDefinition source = CupSurface("counter-a", 0);
            PlacementFootprint cup = CupFootprint();
            ledger.RegisterRegion(source);
            Assert.That(
                ledger.TryRestorePlacement(
                    "cup-01",
                    cup,
                    source.RegionId,
                    new PlacementRegionPose(15, -20, 370),
                    out _,
                    out PlacementRegionFailure failure),
                Is.True,
                failure.ToString());

            Assert.That(
                ledger.TryReserveUse(
                    "cup-01",
                    out PlacementRegionUseLease use,
                    out failure),
                Is.True,
                failure.ToString());
            Assert.That(use.State, Is.EqualTo(PlacementRegionUseState.Reserved));
            Assert.That(ledger.ReservationCount, Is.EqualTo(1));
            Assert.That(use.TryPickUp(out failure), Is.True, failure.ToString());
            Assert.That(use.State, Is.EqualTo(PlacementRegionUseState.Carrying));
            Assert.That(ledger.TryGetPlacement("cup-01", out _), Is.False);

            Assert.That(use.TryConsume(out failure), Is.True, failure.ToString());
            Assert.That(use.State, Is.EqualTo(PlacementRegionUseState.Consumed));
            Assert.That(use.IsActive, Is.False);
            Assert.That(ledger.ReservationCount, Is.Zero);
            Assert.That(ledger.PlacedItemCount, Is.Zero);
            Assert.That(ledger.CreateCheckpointSnapshot(), Is.Empty);
        }

        [Test]
        public void UseLease_CancelAfterPickupRestoresExactSource()
        {
            var ledger = new PlacementRegionLedger();
            PlacementRegionDefinition source = CupSurface("counter-a", 0);
            PlacementFootprint cup = CupFootprint();
            var sourcePose = new PlacementRegionPose(-25, 30, 2170);
            ledger.RegisterRegion(source);
            Assert.That(
                ledger.TryRestorePlacement(
                    "cup-01",
                    cup,
                    source.RegionId,
                    sourcePose,
                    out PlacementRegionItem original,
                    out PlacementRegionFailure failure),
                Is.True,
                failure.ToString());
            Assert.That(
                ledger.TryReserveUse("cup-01", out PlacementRegionUseLease use, out failure),
                Is.True,
                failure.ToString());
            Assert.That(use.TryPickUp(out failure), Is.True, failure.ToString());

            use.Dispose();

            Assert.That(use.State, Is.EqualTo(PlacementRegionUseState.Cancelled));
            Assert.That(ledger.ReservationCount, Is.Zero);
            Assert.That(
                ledger.TryGetPlacement("cup-01", out PlacementRegionItem restored),
                Is.True);
            Assert.That(restored.Region.RegionId, Is.EqualTo(source.RegionId));
            Assert.That(restored.LocalPose, Is.EqualTo(sourcePose));
            Assert.That(restored.WorldPose, Is.EqualTo(original.WorldPose));
        }

        [Test]
        public void UseLease_CheckpointWhileCarryingReturnsConservedSource()
        {
            var ledger = new PlacementRegionLedger();
            PlacementRegionDefinition source = CupSurface("counter-a", 0);
            PlacementFootprint cup = CupFootprint();
            var sourcePose = new PlacementRegionPose(35, -15, 1230);
            ledger.RegisterRegion(source);
            Assert.That(
                ledger.TryRestorePlacement(
                    "cup-01",
                    cup,
                    source.RegionId,
                    sourcePose,
                    out _,
                    out PlacementRegionFailure failure),
                Is.True,
                failure.ToString());
            Assert.That(
                ledger.TryReserveUse("cup-01", out PlacementRegionUseLease use, out failure),
                Is.True,
                failure.ToString());
            Assert.That(use.TryPickUp(out failure), Is.True, failure.ToString());

            IReadOnlyList<PlacementRegionItem> checkpoint = ledger.CreateCheckpointSnapshot();

            Assert.That(checkpoint, Has.Count.EqualTo(1));
            Assert.That(checkpoint[0].ItemId, Is.EqualTo("cup-01"));
            Assert.That(checkpoint[0].Region.RegionId, Is.EqualTo(source.RegionId));
            Assert.That(checkpoint[0].LocalPose, Is.EqualTo(sourcePose));
            Assert.That(ledger.CreateStableSnapshot(), Is.Empty,
                "运行态稳定投影不能谎称居民手里的物品仍在台面上。 ");
            use.Dispose();
        }

        [Test]
        public void ConsumedUseLease_CannotAffectNewerTransactionWithSameItemId()
        {
            var ledger = new PlacementRegionLedger();
            PlacementRegionDefinition source = CupSurface("counter-a", 0);
            PlacementFootprint cup = CupFootprint();
            ledger.RegisterRegion(source);
            Assert.That(
                ledger.TryRestorePlacement(
                    "cup-01",
                    cup,
                    source.RegionId,
                    PlacementRegionPose.Centered,
                    out _,
                    out PlacementRegionFailure failure),
                Is.True,
                failure.ToString());
            Assert.That(
                ledger.TryReserveUse("cup-01", out PlacementRegionUseLease oldUse, out failure),
                Is.True,
                failure.ToString());
            Assert.That(oldUse.TryPickUp(out failure), Is.True, failure.ToString());
            Assert.That(oldUse.TryConsume(out failure), Is.True, failure.ToString());

            Assert.That(
                ledger.TryRestorePlacement(
                    "cup-01",
                    cup,
                    source.RegionId,
                    new PlacementRegionPose(20, 10, 0),
                    out _,
                    out failure),
                Is.True,
                failure.ToString());
            Assert.That(
                ledger.TryReserveUse("cup-01", out PlacementRegionUseLease currentUse, out failure),
                Is.True,
                failure.ToString());

            oldUse.Dispose();
            Assert.That(oldUse.TryPickUp(out failure), Is.False);
            Assert.That(failure, Is.EqualTo(PlacementRegionFailure.ReservationNotActive));
            Assert.That(oldUse.TryConsume(out failure), Is.False);
            Assert.That(failure, Is.EqualTo(PlacementRegionFailure.ReservationNotActive));
            Assert.That(currentUse.IsActive, Is.True);
            Assert.That(ledger.ReservationCount, Is.EqualTo(1));
            Assert.That(ledger.TryGetPlacement("cup-01", out _), Is.True);
            currentUse.Dispose();
        }

        private static PlacementRegionDefinition CupSurface(string owner, int worldX) =>
            new(
                PlacementRegionLedger.ComposeRegionId(owner, "countertop"),
                "countertop",
                owner,
                new DeckPose(worldX, 0, 0),
                260,
                220,
                970,
                10,
                new[] { "cup" });

        private static PlacementFootprint CupFootprint() =>
            new(
                "drinking-cup",
                "cup",
                80,
                70,
                120,
                5,
                new[] { 0 },
                allowsAnyYaw: true);

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
