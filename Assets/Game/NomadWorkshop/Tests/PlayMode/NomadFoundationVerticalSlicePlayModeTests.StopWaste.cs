using System;
using System.Collections;
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
        [UnityTest]
        public IEnumerator StopWaste_MovesOneActualBucket_CommitsOnlyAtReceiver_AndReinstallsSameVisual()
        {
            yield return PrepareStopWasteScenario(700);
            var source = ToiletInventories(CaptureStopCheckpoint())[0];
            Transform installed = FindWasteBucketVisual(source.OwnerEntityId);
            Assert.That(installed, Is.Not.Null);
            Transform installedParent = installed.parent;
            long total = TotalWithReceiver();
            _context.ExecuteCommand(new RequestFoundationStopWasteCommand(true));
            AdvanceUntilJourneyPhase(FoundationResidentPhase.MovingToWasteReceiver);
            Assert.That(_model.CarriedWasteBucketFacilityId.Value, Is.EqualTo(source.OwnerEntityId));
            Assert.That(_model.CarriedWasteMilliliters.Value, Is.EqualTo(700));
            Assert.That(installed.parent.name, Is.EqualTo("Resident 01"));
            Assert.That(installedParent.Find("Detachable Waste Bucket"), Is.Null, "同一个桶移动，不能在厕所留副本。");
            Assert.That(GetInventoryAmount(FindInventory(CaptureStopCheckpoint(), source.InventoryId),
                NomadResourceIds.HumanWaste.Value), Is.EqualTo(700), "搬桶不复制或提前倾倒桶内库存。");
            AdvanceUntilJourneyPhase(FoundationResidentPhase.EmptyingWasteBucket);
            Assert.That(_model.PrimaryResident.ResidentLocalPosition.Value.x, Is.GreaterThan(_layout.CreateBounds().MaxXMillimeters / 1000f));
            Assert.That(_model.PrimaryResident.ResidentLocalYawDegrees.Value, Is.EqualTo(180f).Within(1f));
            Assert.That(_model.StopWasteMilliliters.Value, Is.Zero);
            AdvanceUntilJourneyPhase(FoundationResidentPhase.ReturningWasteBucket);
            Assert.That(_model.StopWasteMilliliters.Value, Is.EqualTo(700));
            Assert.That(_model.CarriedWasteMilliliters.Value, Is.Zero);
            Assert.That(_model.CarriedWasteBucketFacilityId.Value, Is.EqualTo(source.OwnerEntityId), "倒空后仍须带回容器。");
            Assert.That(_model.StopWasteActive.Value, Is.True);
            Assert.That(TotalWithReceiver(), Is.EqualTo(total));
            FinishStopWasteTrip();
            Assert.That(installed.parent, Is.SameAs(installedParent));
            Assert.That(_model.CarriedWasteBucketFacilityId.Value, Is.Empty);
            Assert.That(_model.ToiletHoldingWasteMilliliters.Value, Is.Zero);
            Assert.That(TotalWithReceiver(), Is.EqualTo(total));
        }

        [UnityTest]
        public IEnumerator StopWaste_RecallBeforePour_ReturnsOriginalContents_WithoutTeleportOrDisposal()
        {
            yield return PrepareStopWasteScenario(900);
            string owner = ToiletInventories(CaptureStopCheckpoint())[0].OwnerEntityId;
            _context.ExecuteCommand(new RequestFoundationStopWasteCommand(true));
            AdvanceUntilJourneyPhase(FoundationResidentPhase.EmptyingWasteBucket);
            Vector3 outside = _model.PrimaryResident.ResidentLocalPosition.Value;
            _context.ExecuteCommand(new RequestFoundationStopWasteCommand(false));
            Assert.That(_model.PrimaryResident.ResidentLocalPosition.Value, Is.EqualTo(outside));
            StepJourney(10);
            Assert.That(Vector3.Distance(_model.PrimaryResident.ResidentLocalPosition.Value, outside), Is.LessThanOrEqualTo(0.04f));
            Assert.That(_model.PrimaryResident.ResidentPhase.Value, Is.EqualTo(FoundationResidentPhase.ReturningWasteBucket));
            Assert.That(_model.CarriedWasteBucketFacilityId.Value, Is.EqualTo(owner));
            Assert.That(_model.CarriedWasteMilliliters.Value, Is.EqualTo(900));
            Assert.That(_model.StopWasteMilliliters.Value, Is.Zero);
            FinishStopWasteTrip();
            Assert.That(_model.ToiletHoldingWasteMilliliters.Value, Is.EqualTo(900));
            Assert.That(_model.StopWasteMilliliters.Value, Is.Zero);
            Assert.That(_model.CarriedWasteBucketFacilityId.Value, Is.Empty);
        }

        [UnityTest]
        public IEnumerator StopWaste_CommittedCheckpointKeepsWasteAtSite_AndDepartureWaitsForEmptyBucket()
        {
            yield return PrepareStopWasteScenario(600);
            _context.ExecuteCommand(new RequestFoundationStopWasteCommand(true));
            AdvanceUntilJourneyPhase(FoundationResidentPhase.ReturningWasteBucket);
            var committed = CaptureStopCheckpoint();
            Assert.That(committed.Stop.WasteMilliliters, Is.EqualTo(600));
            Assert.That(committed.Stop.WasteDisposalRequested, Is.False, "已完成倾倒不能在读档后重复请求。");
            long fuel = _model.JourneyFuelPicoliters.Value;
            _context.ExecuteCommand(new SetFoundationJourneyDestinationCommand(NomadJourneyEndpoint.Origin));
            for (var i = 0; i < 30_000 && _model.PrimaryResident.ResidentPhase.Value != FoundationResidentPhase.Driving; i++)
            {
                StepJourney(10);
                Assert.That(_model.JourneyPositionMicrometers.Value, Is.EqualTo(2_000_000_000L));
                Assert.That(_model.JourneyFuelPicoliters.Value, Is.EqualTo(fuel));
            }
            Assert.That(_model.PrimaryResident.ResidentPhase.Value, Is.EqualTo(FoundationResidentPhase.Driving));
            Assert.That(_model.CarriedWasteBucketFacilityId.Value, Is.Empty);
            Assert.That(_model.StopWasteActive.Value, Is.False);
            Assert.That(_model.StopAccessOpen.Value, Is.False);
            StepJourney(10);
            Assert.That(_model.JourneyPositionMicrometers.Value, Is.LessThan(2_000_000_000L));
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(JsonUtility.FromJson<NomadWorkshopSaveData>(JsonUtility.ToJson(committed))));
            Assert.That(_model.StopWasteMilliliters.Value, Is.EqualTo(600));
            Assert.That(_model.ToiletHoldingWasteMilliliters.Value, Is.Zero);
            Assert.That(_model.CarriedWasteBucketFacilityId.Value, Is.Empty);
            Assert.That(_model.StopWasteRequested.Value, Is.False);
        }

        [UnityTest]
        public IEnumerator StopWaste_UncommittedCheckpointKeepsExactToiletContents_AndDoesNotMutateLiveContainer()
        {
            yield return PrepareStopWasteScenario(750);
            _context.ExecuteCommand(new RequestFoundationStopWasteCommand(true));
            AdvanceUntilJourneyPhase(FoundationResidentPhase.EmptyingWasteBucket);
            Vector3 livePosition = _model.PrimaryResident.ResidentLocalPosition.Value;
            string owner = _model.CarriedWasteBucketFacilityId.Value;
            var save = CaptureStopCheckpoint();
            Assert.That(save.Stop.WasteMilliliters, Is.Zero);
            Assert.That(_model.PrimaryResident.ResidentLocalPosition.Value, Is.EqualTo(livePosition));
            Assert.That(_model.CarriedWasteBucketFacilityId.Value, Is.EqualTo(owner));
            Assert.That(_model.CarriedWasteMilliliters.Value, Is.EqualTo(750));
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(JsonUtility.FromJson<NomadWorkshopSaveData>(JsonUtility.ToJson(save))));
            Assert.That(_model.ToiletHoldingWasteMilliliters.Value, Is.EqualTo(750));
            Assert.That(_model.StopWasteMilliliters.Value, Is.Zero);
            Assert.That(_model.CarriedWasteBucketFacilityId.Value, Is.Empty);
            Assert.That(_model.StopWasteRequested.Value, Is.True);
            AdvanceUntilJourneyPhase(FoundationResidentPhase.EmptyingWasteBucket);
            FinishStopWasteTrip();
            Assert.That(_model.StopWasteMilliliters.Value, Is.EqualTo(750), "重规划只能提交一次原有内容。");
        }

        [UnityTest]
        public IEnumerator StopWaste_ReceiverFitsOnlySmallerBucket_SelectsIt_AndPreservesBackpressureAfterReload()
        {
            yield return PrepareStopWasteScenario(1000);
            yield return BuildFacility("toilet", -3000, 0);
            var saved = CaptureStopCheckpoint();
            var toilets = ToiletInventories(saved);
            SetTestInventoryAmount(toilets[1], NomadResourceIds.HumanWaste, 700);
            saved.Stop.WasteCapacityMilliliters = 800;
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(saved));
            _context.ExecuteCommand(new RequestFoundationStopWasteCommand(true));
            AdvanceUntilJourneyPhase(FoundationResidentPhase.EmptyingWasteBucket);
            Assert.That(_model.CarriedWasteBucketFacilityId.Value, Is.EqualTo(toilets[1].OwnerEntityId));
            FinishStopWasteTrip();
            Assert.That(_model.StopWasteMilliliters.Value, Is.EqualTo(700));
            Assert.That(_model.ToiletHoldingWasteMilliliters.Value, Is.EqualTo(1000));
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(JsonUtility.FromJson<NomadWorkshopSaveData>(JsonUtility.ToJson(CaptureStopCheckpoint()))));
            Assert.That(_model.StopWasteCapacityMilliliters.Value, Is.EqualTo(800));
            _context.ExecuteCommand(new RequestFoundationStopWasteCommand(true));
            StepJourney(10);
            Assert.That(_model.StopWasteRequested.Value, Is.False);
            Assert.That(_model.StopWasteActive.Value, Is.False);
            StringAssert.Contains("接收余量不足", _model.StopWorkFeedback.Value);
            Assert.That(_model.StopWasteMilliliters.Value, Is.EqualTo(700));
            Assert.That(_model.ToiletHoldingWasteMilliliters.Value, Is.EqualTo(1000));
        }

        [UnityTest]
        public IEnumerator StopWaste_FullBladderAndOnlyToiletFull_ClearsDependencyThenResumesRealToiletUse()
        {
            yield return PrepareStopWasteScenario();
            var saved = CaptureStopCheckpoint();
            var toilet = ToiletInventories(saved)[0];
            SetTestInventoryAmount(toilet, NomadResourceIds.HumanWaste, toilet.CapacityBaseUnits);
            FillBladderForToiletTest(saved);
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(saved));
            _context.ExecuteCommand(new RequestFoundationStopWasteCommand(true));
            AdvanceUntilJourneyPhase(FoundationResidentPhase.EmptyingWasteBucket);
            FinishStopWasteTrip();
            Assert.That(_model.StopWasteMilliliters.Value, Is.EqualTo(toilet.CapacityBaseUnits));
            AdvanceUntilJourneyPhase(FoundationResidentPhase.UsingToilet);
            CompleteOneToiletUse();
            Assert.That(_model.ToiletHoldingWasteMilliliters.Value, Is.GreaterThan(0));
            Assert.That(_model.PrimaryResident.CompletedToiletUseCount.Value, Is.GreaterThan(0));
        }

        [UnityTest]
        public IEnumerator StopWaste_VersionSevenPreservesWaterAndAddsEmptyReceiver_InvalidReceiverIsAtomic()
        {
            yield return PrepareStopWasteScenario();
            var old = CaptureStopCheckpoint();
            old.Version = 7;
            old.Stop.WaterMilliliters = 1200;
            old.Stop.WasteCapacityMilliliters = 0;
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(JsonUtility.FromJson<NomadWorkshopSaveData>(JsonUtility.ToJson(old))));
            Assert.That(_model.StopWaterMilliliters.Value, Is.EqualTo(1200));
            Assert.That(_model.StopWasteCapacityMilliliters.Value, Is.EqualTo(24_000));
            Assert.That(_model.StopWasteMilliliters.Value, Is.Zero);
            string before = JsonUtility.ToJson(CaptureStopCheckpoint());
            var invalid = CaptureStopCheckpoint();
            invalid.Stop.WasteMilliliters = invalid.Stop.WasteCapacityMilliliters + 1;
            Assert.Throws<InvalidOperationException>(() => _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(invalid)));
            Assert.That(JsonUtility.ToJson(CaptureStopCheckpoint()), Is.EqualTo(before));
        }

        private IEnumerator PrepareStopWasteScenario(int waste = 500)
        {
            yield return PrepareStopWaterScenario();
            var saved = CaptureStopCheckpoint();
            SetTestInventoryAmount(ToiletInventories(saved)[0], NomadResourceIds.HumanWaste, waste);
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(saved));
            // Restore 重建设施表现；等 Unity 帧末释放旧树后，再获取要跟踪的实体桶。
            yield return null;
        }

        private Transform FindWasteBucketVisual(string facilityId)
        {
            var facility = Array.Find(_context.ExecuteCommand(new GetFoundationFacilitiesCommand()), f => f.InstanceId == facilityId);
            string display = FindDefinition(_definitions, facility.DefinitionId).DisplayName;
            return _worldView.transform.Find($"Vehicle Deck Root/Facilities/{display} [{facilityId}]/Detachable Waste Bucket");
        }

        private int TotalWithReceiver() => CalculateProjectedWaterTotal() + _model.StopWaterMilliliters.Value + _model.StopWasteMilliliters.Value;

        private void FinishStopWasteTrip()
        {
            for (var i = 0; i < 30_000 && (_model.StopWasteRequested.Value || _model.StopWasteActive.Value); i++) StepJourney(10);
            Assert.That(_model.StopWasteActive.Value, Is.False, _model.PrimaryResident.CurrentTask.Value);
            Assert.That(_model.StopWasteRequested.Value, Is.False);
        }
    }
}
