using System;
using System.Collections;
using Game.NomadWorkshop.Foundation;
using Game.NomadWorkshop.Simulation.Persistence;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.NomadWorkshop.PlayMode.Tests
{
    public sealed partial class NomadFoundationVerticalSlicePlayModeTests
    {
        [UnityTest]
        public IEnumerator WaterCanHandling_BlockedLoadedDelivery_WaitsAndRetargetsWithoutOrphanedWater()
        {
            _worldView.enabled = false;
            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            yield return BuildFacility("drinking-station", 0, 0);
            AdvanceUntilJourneyPhase(FoundationResidentPhase.MovingToDrinkingStation);
            int amount = _model.WaterCanWaterMilliliters.Value;
            Assert.That(amount, Is.EqualTo(2000));
            // 玩家可以带通路警告建造；已取出的水必须等候或改道，不能被当作未开始的喝水动作取消。
            yield return BuildFacility("slot-blocker", 0, -800);
            StepJourney(100);
            Assert.That(_model.PrimaryResident.ResidentPhase.Value, Is.EqualTo(FoundationResidentPhase.WaitingForRoute));
            Assert.That(_model.WaterCanWaterMilliliters.Value, Is.EqualTo(amount));
            Assert.That(_model.WaterCanLocation.Value, Is.EqualTo(FoundationWaterCanLocation.Resident));
            yield return BuildFacility("drinking-station", 2400, 0);
            AdvanceUntilJourneyPhase(FoundationResidentPhase.ReleasingWaterCan);
            Assert.That(_model.WaterCanWaterMilliliters.Value, Is.Zero,
                "改道必须交付原来的这批水，不能重新搬一批后留下无人认领的余水。");
            Assert.That(_model.DrinkingStationWaterMilliliters.Value, Is.EqualTo(amount));
            Assert.That(_model.WaterCanPlacement.Value.OwnerEntityId, Is.EqualTo("facility-0003"));
            AdvanceUntilJourneyPhase(FoundationResidentPhase.Drinking);
        }

        [UnityTest]
        public IEnumerator WaterCanHandling_RealSidePickupAndParking_KeepOwnershipUntilContactCompletes()
        {
            _worldView.enabled = false;
            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            yield return BuildFacility("drinking-station", 0, 0);
            FoundationItemPlacementState source = _model.WaterCanPlacement.Value;
            AdvanceUntilJourneyPhase(FoundationResidentPhase.PickingUpWaterCan);
            Assert.That(_model.WaterCanLocation.Value, Is.EqualTo(FoundationWaterCanLocation.VehicleWaterTank));
            AssertWaterCanContact(source);
            Vector3 sidePickup = _model.PrimaryResident.ResidentLocalPosition.Value;
            AdvanceUntilJourneyPhase(FoundationResidentPhase.LiftingWaterCan);
            Assert.That(_model.WaterCanLocation.Value, Is.EqualTo(FoundationWaterCanLocation.Resident));
            Assert.That(_model.WaterCanPlacement.Value.Active, Is.False);
            AssertWaterCanContact(source);
            AdvanceUntilJourneyPhase(FoundationResidentPhase.MovingToWaterSource);
            Assert.That(_model.PrimaryResident.RemainingPathMeters.Value, Is.GreaterThan(.5f),
                "取到水罐之后必须从侧面走到前方装水，不能就地开始装水。");
            AdvanceUntilJourneyPhase(FoundationResidentPhase.PickingUpWater);
            Assert.That(Vector3.Distance(sidePickup, _model.PrimaryResident.ResidentLocalPosition.Value), Is.GreaterThan(.5f));
            AdvanceUntilJourneyPhase(FoundationResidentPhase.DeliveringWater);
            int stationBefore = _model.DrinkingStationWaterMilliliters.Value;
            int transfer = _model.WaterCanWaterMilliliters.Value;
            AdvanceUntilJourneyPhase(FoundationResidentPhase.MovingToWaterCanParking);
            Assert.That(_model.DrinkingStationWaterMilliliters.Value, Is.EqualTo(stationBefore + transfer));
            Assert.That(_model.WaterCanWaterMilliliters.Value, Is.Zero);
            Assert.That(_model.WaterCanLocation.Value, Is.EqualTo(FoundationWaterCanLocation.Resident));
            Assert.That(_model.WaterCanPlacement.Value.Active, Is.False);
            AdvanceUntilJourneyPhase(FoundationResidentPhase.PlacingWaterCan);
            FoundationItemPlacementState target = _model.PrimaryResident.FacilityWork.Value.ItemContactPlacement;
            AssertWaterCanContact(target);
            FoundationFacilityWorkState paused = _model.PrimaryResident.FacilityWork.Value;
            long tick = _model.SimulationTick.Value;
            for (int i = 0; i < 4; i++) yield return null;
            Assert.That(_model.SimulationTick.Value, Is.EqualTo(tick));
            Assert.That(_model.PrimaryResident.FacilityWork.Value, Is.EqualTo(paused));
            Assert.That(_model.WaterCanPlacement.Value.Active, Is.False, "暂停放罐不能提交落位。");
            AdvanceUntilJourneyPhase(FoundationResidentPhase.ReleasingWaterCan);
            Assert.That(_model.WaterCanLocation.Value, Is.EqualTo(FoundationWaterCanLocation.DrinkingStation));
            Assert.That(_model.WaterCanPlacement.Value, Is.EqualTo(target));
            AssertWaterCanContact(target);
            Assert.That(_model.DrinkingStationWaterMilliliters.Value, Is.EqualTo(stationBefore + transfer),
                "放罐和松手不能再次结算倾倒。");
            AdvanceUntilJourneyPhase(FoundationResidentPhase.Drinking);
            Assert.That(_model.PrimaryResident.FacilityWork.Value.GroupId, Is.EqualTo("drink-and-deliver"));
            Assert.That(_model.PrimaryResident.FacilityWork.Value.ItemContactPlacement.Active, Is.False);
        }

        [UnityTest]
        public IEnumerator WaterCanHandling_RestoreDuringLowering_ReleasesReservationAndPreservesDeliveredWater()
        {
            _worldView.enabled = false;
            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            yield return BuildFacility("drinking-station", 0, 0);
            AdvanceUntilJourneyPhase(FoundationResidentPhase.PlacingWaterCan);
            int delivered = _model.DrinkingStationWaterMilliliters.Value;
            FoundationItemPlacementState target = _model.PrimaryResident.FacilityWork.Value.ItemContactPlacement;
            NomadWorkshopSaveData checkpoint = _context.ExecuteCommand(new CaptureFoundationCheckpointCommand());
            AssertWaterCanContact(target);
            Assert.That(_model.WaterCanPlacement.Value.Active, Is.False, "捕获存档不应修改正在下放的现场。");
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(
                JsonUtility.FromJson<NomadWorkshopSaveData>(JsonUtility.ToJson(checkpoint))));
            Assert.That(_model.PrimaryResident.FacilityWork.Value.Active, Is.False);
            Assert.That(_model.WaterCanCarrierId.Value, Is.Empty);
            Assert.That(_model.WaterCanPlacement.Value.Active, Is.True);
            Assert.That(_model.DrinkingStationWaterMilliliters.Value, Is.EqualTo(delivered),
                "已送达的水不能随尚未完成的放罐动作回滚或重复入库。");
            // 正常补水应能再次完成：旧落点预留和工作位租约不能留在重建后的世界中。
            AdvanceUntilJourneyPhase(FoundationResidentPhase.PlacingWaterCan);
            AdvanceUntilJourneyPhase(FoundationResidentPhase.ReleasingWaterCan);
            Assert.That(_model.WaterCanPlacement.Value.OwnerEntityId, Is.EqualTo(target.OwnerEntityId));
        }

        [UnityTest]
        public IEnumerator WaterCanHandling_StopRecallDuringParking_DoesNotRestartOrDuplicateDeliveredWater()
        {
            yield return PrepareStopWaterScenario();
            int vehicleBefore = _model.VehicleWaterMilliliters.Value;
            _context.ExecuteCommand(new RequestFoundationStopWaterCommand(true));
            AdvanceUntilJourneyPhase(FoundationResidentPhase.PlacingWaterCan);
            Assert.That(_model.StopWaterActive.Value, Is.True, "放稳并松手前仍由原居民完成这次车外作业。");
            Assert.That(_model.VehicleWaterMilliliters.Value, Is.EqualTo(vehicleBefore + 2000));
            _context.ExecuteCommand(new RequestFoundationStopWaterCommand(false));
            AdvanceUntilJourneyPhase(FoundationResidentPhase.ReleasingWaterCan);
            Assert.That(_model.StopWaterActive.Value, Is.True);
            FinishStopWaterTrip();
            StepJourney(100);
            Assert.That(_model.StopWaterRequested.Value, Is.False);
            Assert.That(_model.StopWaterMilliliters.Value, Is.EqualTo(18000));
            Assert.That(_model.VehicleWaterMilliliters.Value, Is.EqualTo(vehicleBefore + 2000));
        }

        private void AssertWaterCanContact(FoundationItemPlacementState placement)
        {
            FoundationFacilityWorkState work = _model.PrimaryResident.FacilityWork.Value;
            Assert.That(placement.Active, Is.True);
            Assert.That(work.Active, Is.True);
            Assert.That(work.GroupId, Is.EqualTo("water-can-access"));
            Assert.That(work.FacilityInstanceId, Is.EqualTo(placement.OwnerEntityId));
            Assert.That(work.ItemContactPlacement, Is.EqualTo(placement));
            FoundationFacilityState facility = Array.Find(_context.ExecuteCommand(new GetFoundationFacilitiesCommand()),
                candidate => candidate.InstanceId == placement.OwnerEntityId);
            AssertResidentDockedToGroup(facility, FindDefinition(_definitions, facility.DefinitionId), "water-can-access",
                _context.ExecuteCommand(new GetFoundationReadModelCommand()));
        }
    }
}
