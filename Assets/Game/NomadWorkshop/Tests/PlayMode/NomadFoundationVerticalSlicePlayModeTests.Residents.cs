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
        private FoundationReadModel ReadCohort() => _context.ExecuteCommand(new GetFoundationReadModelCommand());

        [UnityTest]
        public IEnumerator ThreeResidents_CupCommandUsesSelectedOwner_AndSharedItemCannotBeClaimedTwice()
        {
            _system.ConfigureResidentCountForTests(3);
            _worldView.enabled = false;
            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            Assert.That(_context.ExecuteCommand(new ResetFoundationForSoakHarnessCommand(1729)), Is.True);
            yield return BuildFacility("field-kitchen", 0, 0);
            yield return BuildFacility("field-kitchen", 3000, 0);
            var read = ReadCohort();
            var source = FindWorldItem(_context.ExecuteCommand(new GetFoundationWorldItemPlacementsCommand()), "cup-01");
            string before = JsonUtility.ToJson(CaptureStopCheckpoint());
            Assert.That(_context.ExecuteCommand(new TryStartFoundationCupMoveCommand("resident-unknown")), Is.False);
            Assert.That(JsonUtility.ToJson(CaptureStopCheckpoint()), Is.EqualTo(before));
            Assert.That(_context.ExecuteCommand(new TryStartFoundationCupMoveCommand("resident-02")), Is.True);
            Assert.That(_context.ExecuteCommand(new TryStartFoundationCupMoveCommand("resident-03")), Is.False,
                "另一人不能在首位执行者已经预留来源与目的地后抢走同一只杯子。");

            bool sawCarry = false;
            for (var frame = 0; frame < 120 && read.Residents[1].CompletedWorldItemMoveCount.CurrentValue == 0; frame++)
            {
                StepCohort(10);
                yield return null;
                if (!read.Residents[1].ResidentCarriedWorldItem.CurrentValue.Active) continue;
                sawCarry = true;
                Assert.That(_worldView.transform.Find(
                    "Vehicle Deck Root/Resident 02/Right Hand Carry Anchor/搪瓷杯 [cup-01] (carried)"), Is.Not.Null);
                var checkpointCup = CaptureStopCheckpoint().WorldItems.Single(x => x.ItemId == "cup-01");
                Assert.That(checkpointCup.OwnerEntityId, Is.EqualTo(source.OwnerEntityId),
                    "非首位居民携带中的 checkpoint 也必须保留完整的来源恢复位。");
            }
            Assert.That(sawCarry, Is.True);
            Assert.That(read.Residents.Select(x => x.CompletedWorldItemMoveCount.CurrentValue), Is.EqualTo(new[] { 0, 1, 0 }));
            Assert.That(read.Residents.All(x => !x.ResidentCarriedWorldItem.CurrentValue.Active), Is.True);
            Assert.That(FindWorldItem(_context.ExecuteCommand(new GetFoundationWorldItemPlacementsCommand()), "cup-01").OwnerEntityId,
                Is.Not.EqualTo(source.OwnerEntityId));
        }

        private IEnumerator PrepareThreeResidents(bool atStop = false, bool stockedStation = false)
        {
            _system.ConfigureResidentCountForTests(3);
            yield return PrepareDrivingScenario();
            var saved = CaptureStopCheckpoint();
            foreach (var resident in saved.Residents) resident.ThirstPermille = 200;
            if (atStop)
            {
                saved.Vehicle.Journey.PositionMicrometers = 2_000_000_000L;
                saved.Vehicle.Journey.Destination = NomadJourneyEndpoint.Destination;
            }
            if (stockedStation)
            {
                SetTestInventoryAmount(saved.Inventories.Single(x => x.InventoryId.EndsWith(":drinking-water")),
                    NomadResourceIds.Water, 4000);
                SetTestInventoryAmount(FindInventory(saved, "vehicle-water-tank"), NomadResourceIds.Water, 56000);
            }
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(saved));
            Assert.That(ReadCohort().Residents.Count, Is.EqualTo(3));
            _worldView.enabled = false;
        }

        private FoundationSoakRunResult StepCohort(long milliseconds)
        {
            var result = _context.ExecuteCommand(new RunFoundationSoakHarnessCommand(milliseconds, 100));
            Assert.That(result.StopReason, Is.EqualTo(FoundationSoakStopReason.DurationReached), CohortDiagnostic());
            Assert.That(result.MaximumAbsoluteWaterDeviationMilliliters, Is.Zero, "世界、地点和三人体内水必须共同守恒。");
            return result;
        }

        private string CohortDiagnostic() => string.Join(" | ", ReadCohort().Residents.Select(x =>
            $"{x.StableId}:{x.ResidentPhase.CurrentValue}:{x.CurrentTask.CurrentValue}:{x.LastBlocker.CurrentValue}" +
            $"[thirst={x.ResidentThirst.CurrentValue:F2}, fatigue={x.ResidentFatigue.CurrentValue:F2}, health={x.ResidentHealth.CurrentValue:F2}]")) +
            $" | can={_model.WaterCanLocation.Value}/{_model.WaterCanCarrierId.Value}/{_model.WaterCanWaterMilliliters.Value}mL" +
            $" vehicle={_model.VehicleWaterMilliliters.Value} station={_model.DrinkingStationWaterMilliliters.Value}";

        private void AdvanceCohortUntil(Func<bool> condition, int steps = 6000)
        {
            for (var i = 0; i < steps && !condition(); i++) StepCohort(100);
            Assert.That(condition(), Is.True, CohortDiagnostic());
        }

        [UnityTest]
        public IEnumerator ThreeResidents_ShareOneClockAndCan_AllMeetNeeds_WithWorldConservation()
        {
            yield return PrepareThreeResidents();
            var read = ReadCohort();
            float[] initialThirst = read.Residents.Select(x => x.ResidentThirst.CurrentValue).ToArray();
            StepCohort(1000);
            Assert.That(read.SimulationTick.CurrentValue, Is.EqualTo(1000L));
            for (var i = 0; i < 3; i++)
                Assert.That(read.Residents[i].ResidentThirst.CurrentValue,
                    Is.EqualTo(initialThirst[i] + 0.004f).Within(0.00002f), "每人的生理仅消费同一个世界秒。");
            bool observedCarrier = false;
            for (var i = 0; i < 2000 && read.Residents.Any(x => x.CompletedDrinkCount.CurrentValue == 0); i++)
            {
                StepCohort(100);
                Assert.That(read.Residents.Count(x => x.ResidentCarryingWater.CurrentValue), Is.LessThanOrEqualTo(1));
                if (string.IsNullOrEmpty(read.WaterCanCarrierId.CurrentValue)) continue;
                observedCarrier = true;
                Transform can = _worldView.transform.Find("Vehicle Deck Root").GetComponentsInChildren<Transform>(true)
                    .Single(x => x.name == "Water Can 01 [physical carrier]");
                Assert.That(can.parent.name, Is.EqualTo("Resident " + read.WaterCanCarrierId.CurrentValue.Substring(9)));
            }
            Assert.That(observedCarrier, Is.True);
            Assert.That(read.Residents.All(x => x.CompletedDrinkCount.CurrentValue > 0), Is.True, CohortDiagnostic());
            Assert.That(StepCohort(100).FinalWaterTotalMilliliters, Is.EqualTo(80_000L));
            var saved = CaptureStopCheckpoint();
            Assert.That(saved.Residents.Select(x => x.ResidentId).Distinct().Count(), Is.EqualTo(3));
            Assert.That(saved.Inventories.Count(x => x.InventoryId == "water-can-01"), Is.EqualTo(1));
            Assert.That(saved.Inventories.Count(x => x.InventoryId.EndsWith(":body-water")), Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator ThreeResidents_CheckpointRestoresEveryOwnerAndRandomStream_RejectsCrossOwnedBodyAtomically()
        {
            yield return PrepareThreeResidents(stockedStation: true);
            StepCohort(45000);
            var saved = CaptureStopCheckpoint();
            var before = ReadCohort().Residents.ToArray();
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(JsonUtility.FromJson<NomadWorkshopSaveData>(JsonUtility.ToJson(saved))));
            var first = StepCohort(15000);
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(saved));
            var second = StepCohort(15000);
            Assert.That(second.TrajectoryChecksum, Is.EqualTo(first.TrajectoryChecksum));
            for (var i = 0; i < 3; i++)
                Assert.That(ReadCohort().Residents[i].ResidentThirst, Is.SameAs(before[i].ResidentThirst));
            Assert.That(saved.RandomStreams.Select(x => x.OwnerEntityId).Distinct().Count(), Is.EqualTo(3));
            var invalid = CaptureStopCheckpoint();
            invalid.Inventories.Single(x => x.InventoryId == "resident-02:body-water").OwnerEntityId = "resident-03";
            string actualBefore = JsonUtility.ToJson(CaptureStopCheckpoint());
            Assert.Throws<InvalidOperationException>(() => _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(invalid)));
            Assert.That(JsonUtility.ToJson(CaptureStopCheckpoint()), Is.EqualTo(actualBefore));
        }

        [UnityTest]
        public IEnumerator ThreeResidents_LoadingSingleResidentCheckpoint_DetachesWorldCanBeforeRemovingItsCarrierVisual()
        {
            yield return PrepareThreeResidents();
            var initial = CaptureStopCheckpoint();
            initial.Residents[1].ThirstPermille = 800;
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(initial));
            AdvanceCohortUntil(() => _model.WaterCanCarrierId.Value == "resident-02" && _model.WaterCanWaterMilliliters.Value > 0);
            Transform deck = _worldView.transform.Find("Vehicle Deck Root");
            Transform can = deck.GetComponentsInChildren<Transform>(true).Single(x => x.name == "Water Can 01 [physical carrier]");
            int canId = can.GetInstanceID();
            var single = CaptureStopCheckpoint();
            foreach (string id in new[] { "resident-02", "resident-03" })
            {
                Assert.That(single.Inventories.Where(x => x.OwnerEntityId == id).Sum(x => x.Contents.Sum(y => y.AmountBaseUnits)), Is.Zero,
                    "此回归在被移除居民饮水前捕获，删掉空身体不会改变测试世界的物质量。");
                single.Residents.RemoveAll(x => x.ResidentId == id);
                single.Inventories.RemoveAll(x => x.OwnerEntityId == id);
                single.RandomStreams.RemoveAll(x => x.OwnerEntityId == id);
            }
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(single));
            yield return null;
            Assert.That(ReadCohort().Residents.Count, Is.EqualTo(1));
            Assert.That(can != null, Is.True, "共享水罐不能随被移除的居民行销毁。");
            Assert.That(can.GetInstanceID(), Is.EqualTo(canId));
            Assert.That(can.parent, Is.EqualTo(deck));
            Assert.That(deck.Find("Resident 02"), Is.Null);
            StepCohort(1000);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ThreeResidents_DepartureWaitsForNonPrimaryCarrier_WhileOtherResidentsCanDrive()
        {
            yield return PrepareThreeResidents(atStop: true, stockedStation: true);
            var saved = CaptureStopCheckpoint();
            saved.Residents[0].EntertainmentPermille = 0;
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(saved));
            AdvanceCohortUntil(() => _model.PrimaryResident.ResidentPhase.Value == FoundationResidentPhase.EnjoyingHobby);
            Assert.That(_context.ExecuteCommand(new RequestFoundationStopWaterCommand(true)), Is.True);
            AdvanceCohortUntil(() => _model.WaterCanWaterMilliliters.Value > 0 && _model.StopWaterActive.Value);
            Assert.That(_model.WaterCanCarrierId.Value, Is.Not.EqualTo("resident-01"), "当前场景应由仍在作画者之外的居民执行外勤。");
            _context.ExecuteCommand(new BeginFacilityPlacementCommand("drinking-station"));
            Assert.That(_model.InteractionMode.Value, Is.Not.EqualTo(FoundationInteractionMode.Build),
                "非首位居民仍在车外时也不能进入重建导航的建造事务。");
            Assert.That(_system.ConfirmPlacement(), Is.False);
            Assert.That(ReadCohort().Residents.Any(x => x.StableId != _model.WaterCanCarrierId.Value &&
                x.ResidentThirst.CurrentValue < 0.55f && x.ResidentFatigue.CurrentValue < 0.6f &&
                x.ResidentHealth.CurrentValue > 0.3f), Is.True, "必须存在身体状态满足驾驶条件的非外勤者。");
            long position = _model.JourneyPositionMicrometers.Value;
            _context.ExecuteCommand(new SetFoundationJourneyDestinationCommand(NomadJourneyEndpoint.Origin));
            StringAssert.Contains("等待", _model.DepartureFeedback.Value);
            for (var i = 0; i < 3000 && _model.StopWaterActive.Value; i++)
            {
                StepCohort(10);
                Assert.That(_model.JourneyPositionMicrometers.Value, Is.EqualTo(position), "外勤回装前，即便其他人能够驾驶也不得移动。");
            }
            Assert.That(_model.StopWaterActive.Value, Is.False, CohortDiagnostic());
            Assert.That(_model.StopWaterMilliliters.Value, Is.EqualTo(18000));
            AdvanceCohortUntil(() => _model.JourneyStatus.Value == NomadJourneyStatus.Moving);
            Assert.That(_model.DepartureFeedback.Value, Is.Empty);
        }

        [UnityTest]
        public IEnumerator ThreeResidents_DetachedToiletBucketIsUnavailableToEveryone_UntilReinstalled()
        {
            yield return PrepareThreeResidents(atStop: true, stockedStation: true);
            var saved = CaptureStopCheckpoint();
            var toilet = saved.Inventories.Single(x => x.InventoryId.EndsWith(":toilet-waste"));
            SetTestInventoryAmount(toilet, NomadResourceIds.HumanWaste, toilet.CapacityBaseUnits);
            foreach (var resident in saved.Residents)
                SetTestInventoryAmount(FindInventory(saved, resident.ResidentId + ":bladder"), NomadResourceIds.HumanWaste, 500);
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(saved));
            Assert.That(_context.ExecuteCommand(new RequestFoundationStopWasteCommand(true)), Is.True);
            AdvanceCohortUntil(() => !string.IsNullOrEmpty(_model.WasteBucketCarrierId.Value));
            Assert.That(ReadCohort().Residents.Count(x => x.ResidentPhase.CurrentValue == FoundationResidentPhase.UsingToilet), Is.Zero);
            int toiletUses = ReadCohort().Residents.Sum(x => x.CompletedToiletUseCount.CurrentValue);
            for (var i = 0; i < 3000 && _model.StopWasteActive.Value; i++)
            {
                StepCohort(10);
                Assert.That(ReadCohort().Residents.Sum(x => x.CompletedToiletUseCount.CurrentValue), Is.EqualTo(toiletUses));
            }
            Assert.That(_model.StopWasteActive.Value, Is.False, CohortDiagnostic());
            Assert.That(_model.StopWasteMilliliters.Value, Is.EqualTo(toilet.CapacityBaseUnits));
            AdvanceCohortUntil(() => ReadCohort().Residents.All(x => x.CompletedToiletUseCount.CurrentValue > 0));
            Assert.That(_model.WasteBucketCarrierId.Value, Is.Empty);
        }

        [UnityTest]
        public IEnumerator ThreeResidents_CompleteDriving_StopWaterAndWaste_AndReturnWithExactFuel()
        {
            yield return PrepareThreeResidents();
            _context.ExecuteCommand(new SetFoundationJourneyDestinationCommand(NomadJourneyEndpoint.Destination));
            AdvanceCohortUntil(() => _model.JourneyStatus.Value == NomadJourneyStatus.Arrived, 12000);
            Assert.That(_model.JourneyFuelPicoliters.Value, Is.EqualTo(15_000_000_000_000L));
            Assert.That(_context.ExecuteCommand(new RequestFoundationStopWaterCommand(true)), Is.True);
            AdvanceCohortUntil(() => !_model.StopWaterRequested.Value && !_model.StopWaterActive.Value);
            Assert.That(_model.StopWaterMilliliters.Value, Is.EqualTo(18000));
            AdvanceCohortUntil(() => _model.ToiletHoldingWasteMilliliters.Value > 0);
            Assert.That(_context.ExecuteCommand(new RequestFoundationStopWasteCommand(true)), Is.True);
            AdvanceCohortUntil(() => !_model.StopWasteRequested.Value && !_model.StopWasteActive.Value);
            Assert.That(_model.StopWasteMilliliters.Value, Is.GreaterThan(0));
            _context.ExecuteCommand(new SetFoundationJourneyDestinationCommand(NomadJourneyEndpoint.Origin));
            AdvanceCohortUntil(() => _model.JourneyStatus.Value == NomadJourneyStatus.Arrived, 12000);
            Assert.That(_model.JourneyPositionMicrometers.Value, Is.Zero);
            Assert.That(_model.JourneyFuelPicoliters.Value, Is.EqualTo(10_000_000_000_000L));
            Assert.That(ReadCohort().Residents.All(x => x.ResidentHealth.CurrentValue > 0f &&
                x.CompletedDrinkCount.CurrentValue > 0 && x.CompletedToiletUseCount.CurrentValue > 0), Is.True, CohortDiagnostic());
            Assert.That(StepCohort(100).FinalWaterTotalMilliliters, Is.EqualTo(80000L));
        }
    }
}
