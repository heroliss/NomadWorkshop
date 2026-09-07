using System;
using System.Collections;
using System.Collections.Generic;
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
        public IEnumerator Toilets_FullNearestInstance_SelectsOtherInstance_AndRestoresSeparateContents()
        {
            yield return PrepareTwoToilets();
            NomadWorkshopSaveData checkpoint = _context.ExecuteCommand(new CaptureFoundationCheckpointCommand());
            var toilets = ToiletInventories(checkpoint);
            SetTestInventoryAmount(toilets[0], NomadResourceIds.HumanWaste, toilets[0].CapacityBaseUnits);
            toilets[1].CapacityBaseUnits = 1700;
            int waste = FillBladderForToiletTest(checkpoint);
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(checkpoint));
            long totalBefore = CalculateProjectedWaterTotal();
            AdvanceUntilJourneyPhase(FoundationResidentPhase.UsingToilet);
            var target = Array.Find(_context.ExecuteCommand(new GetFoundationFacilitiesCommand()),
                facility => facility.InstanceId == toilets[1].OwnerEntityId);
            AssertResidentDockedToGroup(target, FindDefinition(_definitions, "toilet"), "use-toilet",
                _context.ExecuteCommand(new GetFoundationReadModelCommand()));
            CompleteOneToiletUse();
            var saved = _context.ExecuteCommand(new CaptureFoundationCheckpointCommand());
            var after = ToiletInventories(saved);
            Assert.That(GetInventoryAmount(after[0], NomadResourceIds.HumanWaste.Value),
                Is.EqualTo(toilets[0].CapacityBaseUnits));
            Assert.That(GetInventoryAmount(after[1], NomadResourceIds.HumanWaste.Value), Is.EqualTo(waste));
            Assert.That(CalculateProjectedWaterTotal(), Is.EqualTo(totalBefore));
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(
                JsonUtility.FromJson<NomadWorkshopSaveData>(JsonUtility.ToJson(saved))));
            var restored = ToiletInventories(_context.ExecuteCommand(new CaptureFoundationCheckpointCommand()));
            Assert.That(restored[1].CapacityBaseUnits, Is.EqualTo(1700));
            Assert.That(GetInventoryAmount(restored[1], NomadResourceIds.HumanWaste.Value), Is.EqualTo(waste));
            Assert.That(restored[0].OwnerEntityId, Is.Not.EqualTo(restored[1].OwnerEntityId));
        }

        [UnityTest]
        public IEnumerator Toilets_ConstructionBlocksApproach_RetargetsBothPathAndWasteReservation()
        {
            yield return PrepareTwoToilets();
            NomadWorkshopSaveData checkpoint = _context.ExecuteCommand(new CaptureFoundationCheckpointCommand());
            var toilets = ToiletInventories(checkpoint);
            int waste = FillBladderForToiletTest(checkpoint);
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(checkpoint));
            AdvanceUntilJourneyPhase(FoundationResidentPhase.MovingToToilet);
            StringAssert.Contains(toilets[0].OwnerEntityId, _model.PrimaryResident.CurrentTask.Value);
            yield return BuildFacility("slot-blocker", 0, -1000);
            AdvanceUntilJourneyPhase(FoundationResidentPhase.UsingToilet);
            var target = Array.Find(_context.ExecuteCommand(new GetFoundationFacilitiesCommand()),
                facility => facility.InstanceId == toilets[1].OwnerEntityId);
            AssertResidentDockedToGroup(target, FindDefinition(_definitions, "toilet"), "use-toilet",
                _context.ExecuteCommand(new GetFoundationReadModelCommand()));
            CompleteOneToiletUse();
            var after = ToiletInventories(_context.ExecuteCommand(new CaptureFoundationCheckpointCommand()));
            Assert.That(GetInventoryAmount(after[0], NomadResourceIds.HumanWaste.Value), Is.Zero);
            Assert.That(GetInventoryAmount(after[1], NomadResourceIds.HumanWaste.Value), Is.EqualTo(waste));
        }

        [UnityTest]
        public IEnumerator Toilets_LegacySharedBucket_MigratesOnceByStableOwner_WithoutDuplicatingWaste()
        {
            yield return PrepareTwoToilets();
            var old = _context.ExecuteCommand(new CaptureFoundationCheckpointCommand());
            var toilets = ToiletInventories(old);
            old.Inventories.RemoveAll(inventory => inventory.InventoryId.EndsWith(":toilet-waste", StringComparison.Ordinal));
            old.Inventories.Add(LegacyToiletForTest(725));
            old.Version = 5;
            old.WorldItems.RemoveAll(item => item.OwnerEntityId == "site:dry-river");
            old.Facilities.Reverse();
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(old));
            var migrated = _context.ExecuteCommand(new CaptureFoundationCheckpointCommand());
            var after = ToiletInventories(migrated);
            Assert.That(migrated.Version, Is.EqualTo(NomadWorkshopSaveSchema.CurrentVersion));
            Assert.That(after.Count, Is.EqualTo(2));
            Assert.That(after[0].OwnerEntityId, Is.EqualTo(toilets[0].OwnerEntityId));
            Assert.That(GetInventoryAmount(after[0], NomadResourceIds.HumanWaste.Value), Is.EqualTo(725));
            Assert.That(GetInventoryAmount(after[1], NomadResourceIds.HumanWaste.Value), Is.Zero);
            Assert.That(_model.ToiletHoldingCapacityMilliliters.Value, Is.EqualTo(4400));
            Assert.That(migrated.Inventories.Exists(inventory => inventory.InventoryId == "toilet-holding"), Is.False);
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(migrated));
            Assert.That(_model.ToiletHoldingWasteMilliliters.Value, Is.EqualTo(725));
        }

        [UnityTest]
        public IEnumerator Toilets_MixedLegacyAndInstanceOrWrongOwner_IsRejectedBeforeWorldMutation()
        {
            yield return PrepareTwoToilets();
            string before = JsonUtility.ToJson(_context.ExecuteCommand(new CaptureFoundationCheckpointCommand()));
            var mixed = JsonUtility.FromJson<NomadWorkshopSaveData>(before);
            mixed.Inventories.Add(LegacyToiletForTest(100));
            Assert.Throws<InvalidOperationException>(() =>
                _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(mixed)));
            var wrongOwner = JsonUtility.FromJson<NomadWorkshopSaveData>(before);
            var toilets = ToiletInventories(wrongOwner);
            toilets[0].OwnerEntityId = toilets[1].OwnerEntityId;
            Assert.Throws<InvalidOperationException>(() =>
                _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(wrongOwner)));
            Assert.That(JsonUtility.ToJson(_context.ExecuteCommand(new CaptureFoundationCheckpointCommand())),
                Is.EqualTo(before));
        }

        [UnityTest]
        public IEnumerator Toilets_LegacyEmptyBucketWithoutToilet_IsSafe_ButOrphanWasteIsRejected()
        {
            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            var empty = _context.ExecuteCommand(new CaptureFoundationCheckpointCommand());
            empty.Inventories.Add(LegacyToiletForTest(0));
            empty.Version = 5;
            empty.WorldItems.RemoveAll(item => item.OwnerEntityId == "site:dry-river");
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(empty));
            Assert.That(_model.ToiletHoldingCapacityMilliliters.Value, Is.Zero);
            var orphan = _context.ExecuteCommand(new CaptureFoundationCheckpointCommand());
            orphan.Inventories.Add(LegacyToiletForTest(100));
            orphan.Version = 5;
            orphan.WorldItems.RemoveAll(item => item.OwnerEntityId == "site:dry-river");
            Assert.Throws<NotSupportedException>(() =>
                _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(orphan)));
            Assert.That(_model.ToiletHoldingWasteMilliliters.Value, Is.Zero);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Toilets_SameTotalDifferentOwners_ProducesDistinctHarnessTrajectory()
        {
            yield return PrepareTwoToilets();
            string json = JsonUtility.ToJson(_context.ExecuteCommand(new CaptureFoundationCheckpointCommand()));
            var first = JsonUtility.FromJson<NomadWorkshopSaveData>(json);
            var firstToilets = ToiletInventories(first);
            SetTestInventoryAmount(firstToilets[0], NomadResourceIds.HumanWaste, 100);
            SetTestInventoryAmount(firstToilets[1], NomadResourceIds.HumanWaste, 200);
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(first));
            var firstTrace = _context.ExecuteCommand(new RunFoundationSoakHarnessCommand(10));
            var second = JsonUtility.FromJson<NomadWorkshopSaveData>(json);
            var secondToilets = ToiletInventories(second);
            SetTestInventoryAmount(secondToilets[0], NomadResourceIds.HumanWaste, 200);
            SetTestInventoryAmount(secondToilets[1], NomadResourceIds.HumanWaste, 100);
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(second));
            var secondTrace = _context.ExecuteCommand(new RunFoundationSoakHarnessCommand(10));
            Assert.That(firstTrace.StopReason, Is.EqualTo(FoundationSoakStopReason.DurationReached));
            Assert.That(secondTrace.StopReason, Is.EqualTo(FoundationSoakStopReason.DurationReached));
            Assert.That(_model.ToiletHoldingWasteMilliliters.Value, Is.EqualTo(300));
            Assert.That(firstTrace.TrajectoryChecksum, Is.Not.EqualTo(secondTrace.TrajectoryChecksum));
        }

        private IEnumerator PrepareTwoToilets()
        {
            _system.ConfigureTimingsForTests(1f, 2.8f);
            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            _context.ExecuteCommand(new ResetFoundationForSoakHarnessCommand(1729));
            yield return BuildFacility("toilet", 0, 0);
            yield return BuildFacility("toilet", 3000, 0);
        }

        private static List<NomadInventorySaveData> ToiletInventories(NomadWorkshopSaveData save)
        {
            var result = save.Inventories.FindAll(inventory =>
                inventory.InventoryId.EndsWith(":toilet-waste", StringComparison.Ordinal));
            result.Sort((a, b) => string.CompareOrdinal(a.OwnerEntityId, b.OwnerEntityId));
            return result;
        }

        private static int FillBladderForToiletTest(NomadWorkshopSaveData checkpoint)
        {
            checkpoint.Residents[0].Pose = QuantizedDeckPose.FromMeters(0, -2.4f, 0);
            checkpoint.Residents[0].ThirstPermille = 200;
            var bladder = FindInventory(checkpoint, "resident-01:bladder");
            SetTestInventoryAmount(bladder, NomadResourceIds.HumanWaste, bladder.CapacityBaseUnits);
            return bladder.CapacityBaseUnits;
        }

        private static void SetTestInventoryAmount(NomadInventorySaveData inventory, ResourceId resource, int amount)
        {
            inventory.Contents.Clear();
            if (amount > 0) inventory.Contents.Add(new NomadResourceStackSaveData
            {
                StackId = inventory.InventoryId + ":" + resource.Value,
                ResourceId = resource.Value, Measure = resource.Measure, AmountBaseUnits = amount,
            });
        }

        private static NomadInventorySaveData LegacyToiletForTest(int amount)
        {
            var inventory = new NomadInventorySaveData
            {
                InventoryId = "toilet-holding", OwnerEntityId = "vehicle-01",
                Measure = ResourceMeasure.Milliliter, CapacityBaseUnits = 2200,
            };
            SetTestInventoryAmount(inventory, NomadResourceIds.HumanWaste, amount);
            return inventory;
        }

        private void CompleteOneToiletUse()
        {
            int before = _model.PrimaryResident.CompletedToiletUseCount.Value;
            for (var i = 0; i < 2000 && _model.PrimaryResident.CompletedToiletUseCount.Value == before; i++) StepJourney(10);
            Assert.That(_model.PrimaryResident.CompletedToiletUseCount.Value, Is.EqualTo(before + 1));
        }
    }
}
