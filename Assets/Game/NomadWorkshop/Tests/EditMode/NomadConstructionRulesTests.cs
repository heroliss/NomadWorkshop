using System;
using NUnit.Framework;

namespace Game.NomadWorkshop.Simulation.Tests
{
    public sealed class NomadConstructionRulesTests
    {
        [TestCase(NomadConstructionSiteKind.PermanentNode)]
        [TestCase(NomadConstructionSiteKind.TemporaryRoadside)]
        [TestCase(NomadConstructionSiteKind.VehicleMounted)]
        public void Plan_ReservesFinalFootprint_AndMaterialHaulAdvancesOnlyAfterDelivery(
            NomadConstructionSiteKind siteKind)
        {
            var placements = new ContinuousFacilityPlacementLedger(
                new DeckBounds(-3000, -2000, 3000, 2000, 0));
            var flow = new ResourceFlowLedger();
            var ledger = new NomadConstructionBlueprintLedger(placements, flow);
            ContinuousFacilityPlacementRequest placement = Placement("kitchen-01", 0, 0);
            var requirements = new[]
            {
                new NomadConstructionMaterialRequirement(Material("steel"), 2),
                new NomadConstructionMaterialRequirement(Material("wood"), 1),
            };
            Assert.IsTrue(ledger.TryPlan(
                placement,
                new NomadConstructionSite("start", siteKind, "route-a"),
                requirements,
                3,
                out NomadConstructionBlueprint blueprint,
                out NomadConstructionPlanFailure failure,
                out _));
            Assert.AreEqual(NomadConstructionPlanFailure.None, failure);
            Assert.AreEqual(NomadConstructionStage.AwaitingMaterials, blueprint.Stage);
            Assert.AreEqual(1, placements.Count);
            Assert.That(placements.Evaluate(Placement("overlap", 0, 0)),
                Is.EqualTo(ContinuousPlacementFailure.FootprintOverlapsFacility));
            Assert.That(blueprint.TryStartBuilding(1, out _), Is.False);

            ResourceInventory source = new ResourceInventory(
                "roadside-scrap", ResourceMeasure.Item, 3,
                new ResourceQuantity(Material("steel"), 2),
                new ResourceQuantity(Material("wood"), 1));
            var carrier = new ResourceInventory("hands", ResourceMeasure.Item, 2);
            Assert.IsTrue(blueprint.TryReserveDelivery(
                "haul-steel", 1, source, carrier, Material("steel"), 2, out HaulTaskLease lease, out _));
            lease.PickUp();
            Assert.That(blueprint.GetStagedAmount(Material("steel")), Is.Zero);
            Assert.That(carrier.GetAmount(Material("steel")), Is.EqualTo(2));
            lease.Deliver();
            blueprint.RefreshStage();
            Assert.AreEqual(NomadConstructionStage.AwaitingMaterials, blueprint.Stage);
            Assert.AreEqual(2, blueprint.GetStagedAmount(Material("steel")));

            Assert.IsTrue(blueprint.TryReserveDelivery(
                    "haul-wood", 1, source, carrier, Material("wood"), 1,
                out HaulTaskLease woodLease,
                out _));
            woodLease.PickUp();
            woodLease.Deliver();
            blueprint.RefreshStage();
            Assert.AreEqual(NomadConstructionStage.ReadyToBuild, blueprint.Stage);
            Assert.IsTrue(blueprint.TryStartBuilding(1, out NomadConstructionActionFailure actionFailure));
            Assert.AreEqual(NomadConstructionActionFailure.None, actionFailure);
            Assert.That(blueprint.TryAdvanceWork(1, out _), Is.True);
            Assert.That(blueprint.TryCommission(out _), Is.False);
            Assert.IsTrue(blueprint.TryAdvanceWork(int.MaxValue, out _));
            Assert.AreEqual(NomadConstructionStage.Commissioning, blueprint.Stage);
            Assert.IsTrue(blueprint.TryCommission(out _));
            Assert.AreEqual(NomadConstructionStage.Complete, blueprint.Stage);
            Assert.That(blueprint.CompletedWorkUnits, Is.EqualTo(3));
            Assert.That(blueprint.StagedMaterials, Is.Empty);
            Assert.That(blueprint.InstalledMaterials.Count, Is.EqualTo(2));
            Assert.That(blueprint.TryCommission(out _), Is.False);
            Assert.IsTrue(ledger.TryDemolish("kitchen-01", out var recovered, out _));
            Assert.That(recovered.Materials[0].Amount + recovered.Materials[1].Amount, Is.EqualTo(3));
            Assert.That(blueprint.InstalledMaterials, Is.Empty);
            Assert.That(ledger.TryDemolish("kitchen-01", out _, out _), Is.False);
            Assert.That(placements.Count, Is.Zero);
        }

        [Test]
        public void Cancel_ReturnsStagedMaterialsWithoutTeleportingThemBackToSource()
        {
            var placements = new ContinuousFacilityPlacementLedger(
                new DeckBounds(-2000, -2000, 2000, 2000, 0));
            var flow = new ResourceFlowLedger();
            var ledger = new NomadConstructionBlueprintLedger(placements, flow);
            Assert.IsTrue(ledger.TryPlan(
                Placement("bed-01", 0, 0),
                new NomadConstructionSite("roadside-01", NomadConstructionSiteKind.TemporaryRoadside,
                    "route-a", 1000, 5000),
                new[] { new NomadConstructionMaterialRequirement(Material("cloth"), 2) },
                2,
                out NomadConstructionBlueprint blueprint,
                out _,
                out _));
            ResourceInventory source = new ResourceInventory(
                "source", ResourceMeasure.Item, 2, new ResourceQuantity(Material("cloth"), 2));
            var carrier = new ResourceInventory("hands", ResourceMeasure.Item, 2);
            Assert.IsTrue(blueprint.TryReserveDelivery(
                    "haul-cloth", 2, source, carrier, Material("cloth"), 2,
                out HaulTaskLease lease,
                out _));
            lease.PickUp();
            lease.Deliver();
            Assert.That(blueprint.TryStartBuilding(2, out _), Is.True);
            blueprint.TryAdvanceWork(1, out _);

            Assert.IsTrue(ledger.TryCancel("bed-01", out NomadConstructionRecovery recovery, out _));
            Assert.AreEqual(0, placements.Count);
            Assert.AreEqual(1, recovery.Materials.Count);
            Assert.AreEqual(2, recovery.Materials[0].Amount);
            Assert.AreEqual(0, source.GetAmount(Material("cloth")));
            Assert.That(blueprint.StagedMaterials, Is.Empty);
            Assert.That(blueprint.Stage, Is.EqualTo(NomadConstructionStage.Cancelled));
            Assert.That(ledger.TryCancel("bed-01", out _, out _), Is.False);
            Assert.That(blueprint.TryAdvanceWork(1, out _), Is.False);
            Assert.That(blueprint.TryCommission(out _), Is.False);
        }

        [Test]
        public void Retention_KeepsBoundaryAndRetiresOnlyTemporaryRoadsideSites()
        {
            var permanent = new NomadConstructionSite("node", NomadConstructionSiteKind.PermanentNode);
            var vehicle = new NomadConstructionSite("vehicle", NomadConstructionSiteKind.VehicleMounted);
            var roadside = new NomadConstructionSite(
                "roadside", NomadConstructionSiteKind.TemporaryRoadside, "route-a", 1000, 5000);

            Assert.AreEqual(NomadSiteRetentionDecision.Keep,
                NomadConstructionLifecycle.Evaluate(permanent, "route-a", 999999));
            Assert.AreEqual(NomadSiteRetentionDecision.Keep,
                NomadConstructionLifecycle.Evaluate(vehicle, "route-a", 999999));
            Assert.AreEqual(NomadSiteRetentionDecision.Keep,
                NomadConstructionLifecycle.Evaluate(roadside, "route-a", 6000));
            Assert.AreEqual(NomadSiteRetentionDecision.Retire,
                NomadConstructionLifecycle.Evaluate(roadside, "route-a", 6001));
            Assert.Throws<ArgumentException>(() =>
                NomadConstructionLifecycle.Evaluate(roadside, "another-route", 6001));
            Assert.Throws<ArgumentException>(() =>
                NomadConstructionLifecycle.Evaluate(default, "route-a", 100));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Cancel_WithActiveHaulIsBlocked_ReleasedCargoKeepsItsOwner(bool pickedUp)
        {
            var flow = new ResourceFlowLedger();
            var placements = new ContinuousFacilityPlacementLedger(new DeckBounds(-2000, -2000, 2000, 2000));
            var ledger = new NomadConstructionBlueprintLedger(placements, flow);
            Assert.That(ledger.TryPlan(Placement("bed", 0, 0),
                new NomadConstructionSite("start", NomadConstructionSiteKind.PermanentNode),
                new[] { new NomadConstructionMaterialRequirement(Material("wood"), 2) }, 10,
                out var blueprint, out _, out _), Is.True);
            var source = new ResourceInventory("source", ResourceMeasure.Item, 2,
                new ResourceQuantity(Material("wood"), 2));
            var carrier = new ResourceInventory("hands", ResourceMeasure.Item, 2);
            Assert.That(blueprint.TryReserveDelivery("wood", 1, source, carrier, Material("wood"), 2,
                out var haul, out _), Is.True);
            if (pickedUp) haul.PickUp();

            Assert.That(ledger.TryCancel("bed", out _, out var failure), Is.False);
            Assert.That(failure, Is.EqualTo(NomadConstructionActionFailure.MaterialsBusy));
            Assert.That(placements.Count, Is.EqualTo(1));
            haul.Dispose();
            Assert.That(ledger.TryCancel("bed", out var recovered, out _), Is.True);
            Assert.That(recovered.Materials, Is.Empty);
            Assert.That(source.TotalAmount, Is.EqualTo(pickedUp ? 0 : 2));
            Assert.That(carrier.TotalAmount, Is.EqualTo(pickedUp ? 2 : 0));
            Assert.That(flow.HasReservations(source), Is.False);
            Assert.That(flow.HasReservations(carrier), Is.False);
        }

        [Test]
        public void InvalidMaterialsAndOverflow_LeaveNoGhostPlacement()
        {
            var placements = new ContinuousFacilityPlacementLedger(new DeckBounds(-2000, -2000, 2000, 2000));
            var ledger = new NomadConstructionBlueprintLedger(placements, new ResourceFlowLedger());
            var site = new NomadConstructionSite("start", NomadConstructionSiteKind.PermanentNode);
            var wood = new NomadConstructionMaterialRequirement(Material("wood"), int.MaxValue);
            var steel = new NomadConstructionMaterialRequirement(Material("steel"), 1);
            foreach (var requirements in new[]
            {
                new[] { default(NomadConstructionMaterialRequirement) },
                new[] { wood, wood },
                new[] { wood, steel },
            })
            {
                Assert.That(ledger.TryPlan(Placement("invalid", 0, 0), site, requirements, 10,
                    out _, out var failure, out _), Is.False);
                Assert.That(failure, Is.EqualTo(NomadConstructionPlanFailure.InvalidMaterials));
                Assert.That(placements.Count, Is.Zero);
                Assert.That(ledger.Count, Is.Zero);
            }
        }

        [Test]
        public void Delivery_RejectsWrongAndExcessMaterials_AndDoesNotOverbook()
        {
            var flow = new ResourceFlowLedger();
            var placements = new ContinuousFacilityPlacementLedger(new DeckBounds(-2000, -2000, 2000, 2000));
            var ledger = new NomadConstructionBlueprintLedger(placements, flow);
            Assert.That(ledger.TryPlan(Placement("bed", 0, 0),
                new NomadConstructionSite("start", NomadConstructionSiteKind.VehicleMounted),
                new[] { new NomadConstructionMaterialRequirement(Material("wood"), 2) }, 10,
                out var blueprint, out _, out _), Is.True);
            var source = new ResourceInventory("source", ResourceMeasure.Item, 5,
                new ResourceQuantity(Material("wood"), 4), new ResourceQuantity(Material("steel"), 1));
            var first = new ResourceInventory("first", ResourceMeasure.Item, 3);
            var second = new ResourceInventory("second", ResourceMeasure.Item, 3);
            Assert.Throws<ArgumentException>(() => blueprint.TryReserveDelivery(
                "wrong", 1, source, first, Material("steel"), 1, out _, out _));
            Assert.Throws<ArgumentException>(() => blueprint.TryReserveDelivery(
                "excess", 1, source, first, Material("wood"), 3, out _, out _));
            Assert.That(blueprint.TryReserveDelivery("first", 1, source, first, Material("wood"), 2,
                out var haul, out _), Is.True);
            Assert.That(blueprint.TryReserveDelivery("second", 2, source, second, Material("wood"), 2,
                out _, out _), Is.False);
            Assert.That(source.TotalAmount, Is.EqualTo(5));
            haul.Dispose();
        }

        private static ResourceId Material(string value) => new(value, ResourceMeasure.Item);

        private static ContinuousFacilityPlacementRequest Placement(
            string id,
            int x,
            int z)
        {
            return new ContinuousFacilityPlacementRequest(
                id,
                "facility",
                new DeckPose(x, z, 0, 0),
                new ContinuousFacilityFootprint(new[]
                {
                    new DeckFootprintPart(0, 0, 1000, 1000, 0),
                }));
        }
    }
}
