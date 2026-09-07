using System;
using System.Collections;
using System.Linq;
using Game.NomadWorkshop.Foundation;
using Game.NomadWorkshop.Simulation;
using Game.NomadWorkshop.Simulation.Persistence;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.NomadWorkshop.PlayMode.Tests
{
    public sealed partial class NomadFoundationVerticalSlicePlayModeTests
    {
        private const string SpareSiteOwner = "site:dry-river";

        [UnityTest]
        public IEnumerator StopSpare_DestroyContextWhileCarrying_DoesNotNotifyViewsThroughDeadContext()
        {
            yield return PrepareStopSpareScenario();
            Assert.That(_context.ExecuteCommand(new RequestFoundationStopSpareCommand(true)), Is.True);
            AdvanceUntilJourneyPhase(FoundationResidentPhase.ReturningStopSpare);
            yield return null;
            Assert.That(_model.PrimaryResident.ResidentCarriedWorldItem.Value.Active, Is.True);
            UnityEngine.Object.Destroy(_root);
            yield return null;
            Assert.That(_context == null, Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator StopSpare_DeliversFiniteIdentities_AndRealRepairsConsumeEveryPackageWithoutRespawn()
        {
            yield return PrepareStopSpareScenario(emptyTray: false);
            Assert.That(_model.StopSpareCount.Value, Is.EqualTo(2));
            CompleteSpareTestRepair();
            Assert.That(KitItems().Length, Is.EqualTo(2), "首包必须通过实际维修消耗。");
            foreach (string id in new[] { "dry-river-valve-kit-01", "dry-river-valve-kit-02" })
            {
                Assert.That(_context.ExecuteCommand(new RequestFoundationStopSpareCommand(true)), Is.True);
                AdvanceUntilJourneyPhase(FoundationResidentPhase.PickingUpStopSpare);
                Assert.That(_model.PrimaryResident.ResidentLocalPosition.Value.x, Is.GreaterThan(_layout.CreateBounds().MaxXMillimeters / 1000f));
                Assert.That(_model.PrimaryResident.ResidentCarriedWorldItem.Value.Active, Is.False);
                AdvanceUntilJourneyPhase(FoundationResidentPhase.ReturningStopSpare);
                Assert.That(_model.PrimaryResident.ResidentCarriedWorldItem.Value.ItemId, Is.EqualTo(id));
                Assert.That(KitItems().Single(x => x.ItemId == id).OwnerEntityId, Is.EqualTo(SpareSiteOwner),
                    "携带中检查点仍恢复来源，但不改变现场携带状态。");
                FinishStopSpareTrip();
                Assert.That(KitItems().Single(x => x.ItemId == id).OwnerEntityId, Is.EqualTo("initial-vehicle-water-tank"));
                CompleteSpareTestRepair();
                Assert.That(KitItems().Any(x => x.ItemId == id), Is.False);
            }
            Assert.That(_model.StopSpareCount.Value, Is.Zero);
            Assert.That(_model.PrimaryResident.CompletedWaterTankRepairCount.Value, Is.EqualTo(3));
            var depleted = CaptureStopCheckpoint();
            Assert.That(depleted.Stop.SpareStockInitialized, Is.True);
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(JsonUtility.FromJson<NomadWorkshopSaveData>(JsonUtility.ToJson(depleted))));
            Assert.That(_model.StopSpareCount.Value, Is.Zero);
            Assert.That(KitItems(), Is.Empty);
            _context.ExecuteCommand(new RequestFoundationStopSpareCommand(true));
            for (var i = 0; i < 30000 && _model.StopSpareRequested.Value; i++) StepJourney(10);
            StringAssert.Contains("耗尽", _model.StopWorkFeedback.Value);
        }

        [UnityTest]
        public IEnumerator StopSpare_RecallBeforePickup_ReturnsWithoutTakingPackage_AndExcludesOtherVisits()
        {
            yield return PrepareStopSpareScenario();
            _context.ExecuteCommand(new RequestFoundationStopSpareCommand(true));
            AdvanceUntilJourneyPhase(FoundationResidentPhase.PickingUpStopSpare);
            Assert.That(_context.ExecuteCommand(new RequestFoundationStopWaterCommand(true)), Is.False);
            Assert.That(_context.ExecuteCommand(new RequestFoundationStopWasteCommand(true)), Is.False);
            Vector3 outside = _model.PrimaryResident.ResidentLocalPosition.Value;
            _context.ExecuteCommand(new RequestFoundationStopSpareCommand(false));
            Assert.That(_model.PrimaryResident.ResidentLocalPosition.Value, Is.EqualTo(outside));
            StepJourney(10);
            Assert.That(Vector3.Distance(_model.PrimaryResident.ResidentLocalPosition.Value, outside), Is.LessThanOrEqualTo(0.04f));
            Assert.That(_model.PrimaryResident.ResidentPhase.Value, Is.EqualTo(FoundationResidentPhase.ReturningStopSpare));
            FinishStopSpareTrip();
            Assert.That(_model.StopSpareCount.Value, Is.EqualTo(2));
            Assert.That(KitItems().All(x => x.OwnerEntityId == SpareSiteOwner), Is.True);
        }

        [UnityTest]
        public IEnumerator StopSpare_CarriedCheckpointRestoresSourceAndExactDestinationCapacity_ThenDeliversOnce()
        {
            yield return PrepareStopSpareScenario();
            _context.ExecuteCommand(new RequestFoundationStopSpareCommand(true));
            AdvanceUntilJourneyPhase(FoundationResidentPhase.ReturningStopSpare);
            Vector3 outside = _model.PrimaryResident.ResidentLocalPosition.Value;
            string id = _model.PrimaryResident.ResidentCarriedWorldItem.Value.ItemId;
            var saved = CaptureStopCheckpoint();
            Assert.That(_model.PrimaryResident.ResidentLocalPosition.Value, Is.EqualTo(outside));
            Assert.That(_model.StopSpareCount.Value, Is.EqualTo(1));
            Assert.That(_model.PrimaryResident.ResidentCarriedWorldItem.Value.ItemId, Is.EqualTo(id));
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(JsonUtility.FromJson<NomadWorkshopSaveData>(JsonUtility.ToJson(saved))));
            Assert.That(_model.StopSpareActive.Value, Is.False);
            Assert.That(_model.PrimaryResident.ResidentCarriedWorldItem.Value.Active, Is.False);
            Assert.That(_model.StopSpareCount.Value, Is.EqualTo(2));
            Assert.That(_model.StopSpareRequested.Value, Is.True);
            FinishStopSpareTrip();
            Assert.That(_model.StopSpareCount.Value, Is.EqualTo(1));
            Assert.That(KitItems().Count(x => x.ItemId == id && x.OwnerEntityId == "initial-vehicle-water-tank"), Is.EqualTo(1));
            Assert.That(KitItems().Length, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator StopSpare_DepartureWaitsForDelivery_AndRevisitKeepsFiniteSource()
        {
            yield return PrepareStopSpareScenario();
            _context.ExecuteCommand(new RequestFoundationStopSpareCommand(true));
            AdvanceUntilJourneyPhase(FoundationResidentPhase.ReturningStopSpare);
            long fuel = _model.JourneyFuelPicoliters.Value;
            _context.ExecuteCommand(new SetFoundationJourneyDestinationCommand(NomadJourneyEndpoint.Origin));
            for (var i = 0; i < 30000 && _model.PrimaryResident.ResidentPhase.Value != FoundationResidentPhase.Driving; i++)
            {
                StepJourney(10);
                Assert.That(_model.JourneyPositionMicrometers.Value, Is.EqualTo(2_000_000_000L));
                Assert.That(_model.JourneyFuelPicoliters.Value, Is.EqualTo(fuel));
            }
            Assert.That(_model.PrimaryResident.ResidentPhase.Value, Is.EqualTo(FoundationResidentPhase.Driving));
            Assert.That(_model.StopSpareActive.Value, Is.False);
            Assert.That(_model.PrimaryResident.ResidentCarriedWorldItem.Value.Active, Is.False);
            Assert.That(_model.StopAccessOpen.Value, Is.False);
            StepJourney(1000);
            Assert.That(_model.JourneyPositionMicrometers.Value, Is.LessThan(2_000_000_000L));
            _context.ExecuteCommand(new SetFoundationJourneyDestinationCommand(NomadJourneyEndpoint.Destination));
            AdvanceUntilArrival();
            Assert.That(_model.StopSpareCount.Value, Is.EqualTo(1));
            Assert.That(KitItems().Length, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator StopSpare_FullMaintenanceTray_PreservesBothSourcePackagesAndReportsSpaceBackpressure()
        {
            yield return PrepareStopSpareScenario(emptyTray: false);
            _context.ExecuteCommand(new RequestFoundationStopSpareCommand(true));
            for (var i = 0; i < 30000 && _model.StopSpareRequested.Value; i++) StepJourney(10);
            Assert.That(_model.StopSpareRequested.Value, Is.False);
            Assert.That(_model.StopSpareActive.Value, Is.False);
            Assert.That(_model.StopSpareCount.Value, Is.EqualTo(2));
            Assert.That(KitItems().Length, Is.EqualTo(3));
            StringAssert.Contains("维护托盘", _model.StopWorkFeedback.Value);
        }

        [UnityTest]
        public IEnumerator StopSpare_OldVersionEightInitializesOnce_AndInvalidSiteSupportCannotMutateWorld()
        {
            yield return PrepareStopSpareScenario();
            var legacy = CaptureStopCheckpoint();
            legacy.Version = 8;
            legacy.Stop.SpareStockInitialized = false;
            legacy.WorldItems.RemoveAll(x => x.OwnerEntityId == SpareSiteOwner);
            legacy.Stop.WaterMilliliters = 1234;
            legacy.Stop.WasteMilliliters = 345;
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(JsonUtility.FromJson<NomadWorkshopSaveData>(JsonUtility.ToJson(legacy))));
            Assert.That(_model.StopSpareCount.Value, Is.EqualTo(2));
            Assert.That(_model.StopWaterMilliliters.Value, Is.EqualTo(1234));
            Assert.That(_model.StopWasteMilliliters.Value, Is.EqualTo(345));
            var current = CaptureStopCheckpoint();
            Assert.That(current.Version, Is.EqualTo(NomadWorkshopSaveSchema.CurrentVersion));
            Assert.That(current.Stop.SpareStockInitialized, Is.True);
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(current));
            Assert.That(_model.StopSpareCount.Value, Is.EqualTo(2));
            string before = JsonUtility.ToJson(CaptureStopCheckpoint());
            var bad = CaptureStopCheckpoint();
            bad.WorldItems.First(x => x.OwnerEntityId == SpareSiteOwner).PlacementRegionId = "site:foreign/placement/spares";
            Assert.Throws<InvalidOperationException>(() => _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(bad)));
            Assert.That(JsonUtility.ToJson(CaptureStopCheckpoint()), Is.EqualTo(before));
        }

        [UnityTest]
        public IEnumerator StopSpare_HarnessDistinguishesEqualCountsWithDifferentItemIdentity()
        {
            yield return PrepareStopSpareScenario();
            var original = CaptureStopCheckpoint();
            var first = _context.ExecuteCommand(new RunFoundationSoakHarnessCommand(1000, 10));
            original.WorldItems.First(x => x.OwnerEntityId == SpareSiteOwner).ItemId = "different-valve-package";
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(original));
            var second = _context.ExecuteCommand(new RunFoundationSoakHarnessCommand(1000, 10));
            Assert.That(first.StopReason, Is.EqualTo(FoundationSoakStopReason.DurationReached));
            Assert.That(second.StopReason, Is.EqualTo(FoundationSoakStopReason.DurationReached));
            Assert.That(_model.StopSpareCount.Value, Is.EqualTo(2));
            Assert.That(first.TrajectoryChecksum, Is.Not.EqualTo(second.TrajectoryChecksum));
        }

        private IEnumerator PrepareStopSpareScenario(bool emptyTray = true)
        {
            yield return PrepareStopWaterScenario();
            if (emptyTray)
            {
                var saved = CaptureStopCheckpoint();
                saved.WorldItems.RemoveAll(x => x.ItemId == "water-valve-kit-01");
                _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(saved));
            }
            yield return null;
        }

        private NomadWorldItemSaveData[] KitItems() => CaptureStopCheckpoint().WorldItems
            .Where(x => x.DefinitionId == "water-valve-repair-kit").ToArray();

        private void FinishStopSpareTrip()
        {
            for (var i = 0; i < 30000 && (_model.StopSpareRequested.Value || _model.StopSpareActive.Value); i++) StepJourney(10);
            Assert.That(_model.StopSpareActive.Value, Is.False, _model.PrimaryResident.CurrentTask.Value);
            Assert.That(_model.StopSpareRequested.Value, Is.False, _model.StopWorkFeedback.Value);
        }

        private void CompleteSpareTestRepair()
        {
            int before = _model.PrimaryResident.CompletedWaterTankRepairCount.Value;
            Assert.That(_context.ExecuteCommand(new ForcePrimaryWaterTankFaultCommand()), Is.True);
            for (var i = 0; i < 30000 && _model.PrimaryResident.CompletedWaterTankRepairCount.Value == before; i++) StepJourney(10);
            Assert.That(_model.PrimaryResident.CompletedWaterTankRepairCount.Value, Is.EqualTo(before + 1), _model.PrimaryResident.CurrentTask.Value);
        }
    }
}
