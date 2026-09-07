using System;
using System.Collections;
using System.Linq;
using Game.NomadWorkshop.Foundation;
using Game.NomadWorkshop.Navigation;
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
        public IEnumerator StopWater_NeedRecallBeforePickup_KeepsPlayerOrderForARecoveredResident()
        {
            _system.ConfigurePhysiologyForTests(8f, .2f, .012f);
            yield return PrepareStopWaterScenario();
            Assert.That(_context.ExecuteCommand(new RequestFoundationStopWaterCommand(true)), Is.True);
            AdvanceUntilJourneyPhase(FoundationResidentPhase.MovingToStopWater);
            // 通过正常生理推进和慢速出车产生自动返程，不发玩家取消命令，也不改私有执行状态。
            _system.ConfigureTimingsForTests(1f, .1f);
            AdvanceUntilJourneyPhase(FoundationResidentPhase.ReturningFromStopWater);
            Assert.That(_model.WaterCanWaterMilliliters.Value, Is.Zero, "必须是在取到水之前因需求返程。");
            Assert.That(_model.StopWaterRequested.Value, Is.True);
            _system.ConfigureTimingsForTests(1f, 100f);
            for (int i = 0; i < 30000 && _model.StopWaterActive.Value; i++) StepJourney(10);
            Assert.That(_model.StopWaterActive.Value, Is.False);
            Assert.That(_model.StopWaterRequested.Value, Is.True,
                "居民身体需要导致归车，不代表玩家撤销尚未完成的取水请求。" + _model.StopWorkFeedback.Value);
            Assert.That(_model.StopWaterMilliliters.Value, Is.EqualTo(20000));
            FinishStopWaterTrip();
            Assert.That(_model.StopWaterMilliliters.Value, Is.EqualTo(18000));
        }

        [UnityTest]
        public IEnumerator StopWater_TemporarilyReservedEmptyCan_KeepsOrderUntilCarrierIsAvailable()
        {
            yield return PrepareThreeResidents(atStop: true);
            var saved = CaptureStopCheckpoint();
            SetTestInventoryAmount(FindInventory(saved, "vehicle-water-tank"), NomadResourceIds.Water, 56000);
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(saved));
            AdvanceCohortUntil(() => ReadCohort().Residents.Any(r =>
                r.ResidentPhase.CurrentValue == FoundationResidentPhase.MovingToWaterCan));
            Assert.That(_model.WaterCanWaterMilliliters.Value, Is.Zero);
            Assert.That(_model.WaterCanLocation.Value, Is.Not.EqualTo(FoundationWaterCanLocation.Resident));
            Assert.That(_context.ExecuteCommand(new RequestFoundationStopWaterCommand(true)), Is.True);
            StepCohort(100);
            Assert.That(_model.StopWaterRequested.Value, Is.True,
                "另一项车内搬水已预留空水罐，应等待共享工具，不能把玩家请求当失败清除。" +
                _model.StopWorkFeedback.Value + " | " + CohortDiagnostic());
            AdvanceCohortUntil(() => !_model.StopWaterRequested.Value && !_model.StopWaterActive.Value);
            Assert.That(_model.StopWaterMilliliters.Value, Is.EqualTo(18000), _model.StopWorkFeedback.Value);
        }

        [UnityTest]
        public IEnumerator StopWater_PhysicalRoundTrip_ConservesFiniteSource_AndExhaustionSurvivesJson()
        {
            yield return PrepareStopWaterScenario(3500);
            long total = TotalIncludingStopWater();
            int vehicle = _model.VehicleWaterMilliliters.Value;
            Assert.That(_context.ExecuteCommand(new RequestFoundationStopWaterCommand(true)), Is.True);
            AdvanceUntilJourneyPhase(FoundationResidentPhase.FillingAtStop);
            Assert.That(_model.PrimaryResident.ResidentLocalPosition.Value.x, Is.GreaterThan(_layout.CreateBounds().MaxXMillimeters / 1000f));
            Assert.That(_model.StopWaterMilliliters.Value, Is.EqualTo(3500));
            Assert.That(_model.WaterCanWaterMilliliters.Value, Is.Zero, "装水计时完成前不转移源库存。");
            AdvanceUntilJourneyPhase(FoundationResidentPhase.ReturningFromStopWater);
            Assert.That(_model.StopWaterMilliliters.Value, Is.EqualTo(1500));
            Assert.That(_model.WaterCanWaterMilliliters.Value, Is.EqualTo(2000));
            Assert.That(_model.VehicleWaterMilliliters.Value, Is.EqualTo(vehicle));
            Assert.That(TotalIncludingStopWater(), Is.EqualTo(total));
            FinishStopWaterTrip();
            Assert.That(_model.VehicleWaterMilliliters.Value, Is.EqualTo(vehicle + 2000));
            Assert.That(_model.WaterCanLocation.Value, Is.EqualTo(FoundationWaterCanLocation.VehicleWaterTank));
            _context.ExecuteCommand(new RequestFoundationStopWaterCommand(true));
            AdvanceUntilJourneyPhase(FoundationResidentPhase.FillingAtStop);
            FinishStopWaterTrip();
            Assert.That(_model.StopWaterMilliliters.Value, Is.Zero);
            Assert.That(TotalIncludingStopWater(), Is.EqualTo(total));
            var saved = CaptureStopCheckpoint();
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(JsonUtility.FromJson<NomadWorkshopSaveData>(JsonUtility.ToJson(saved))));
            Assert.That(_model.StopWaterMilliliters.Value, Is.Zero, "已空地点不能因读档再次补满。");
            _context.ExecuteCommand(new RequestFoundationStopWaterCommand(true));
            StepJourney(10);
            Assert.That(_model.StopWaterRequested.Value, Is.False);
            Assert.That(_model.StopWaterActive.Value, Is.False);
        }

        [UnityTest]
        public IEnumerator StopWater_RecallBeforePickup_ReturnsEmptyCan_WithoutSourceOrVehicleTransfer()
        {
            yield return PrepareStopWaterScenario();
            int vehicle = _model.VehicleWaterMilliliters.Value;
            _context.ExecuteCommand(new RequestFoundationStopWaterCommand(true));
            AdvanceUntilJourneyPhase(FoundationResidentPhase.FillingAtStop);
            _context.ExecuteCommand(new RequestFoundationStopWaterCommand(false));
            StepJourney(10);
            Assert.That(_model.PrimaryResident.ResidentPhase.Value, Is.EqualTo(FoundationResidentPhase.ReturningFromStopWater));
            Assert.That(_model.WaterCanWaterMilliliters.Value, Is.Zero);
            FinishStopWaterTrip();
            Assert.That(_model.StopWaterMilliliters.Value, Is.EqualTo(20_000));
            Assert.That(_model.VehicleWaterMilliliters.Value, Is.EqualTo(vehicle));
            Assert.That(_model.WaterCanLocation.Value, Is.EqualTo(FoundationWaterCanLocation.VehicleWaterTank));
        }

        [UnityTest]
        public IEnumerator StopWater_DepartureRecallsCarrier_AndMovesOnlyAfterReturnAndDriverDock()
        {
            yield return PrepareStopWaterScenario();
            _context.ExecuteCommand(new RequestFoundationStopWaterCommand(true));
            AdvanceUntilJourneyPhase(FoundationResidentPhase.ReturningFromStopWater);
            Assert.That(_model.WaterCanWaterMilliliters.Value, Is.EqualTo(2000));
            long fuel = _model.JourneyFuelPicoliters.Value;
            _context.ExecuteCommand(new SetFoundationJourneyDestinationCommand(NomadJourneyEndpoint.Origin));
            for (var i = 0; i < 30_000 && _model.PrimaryResident.ResidentPhase.Value != FoundationResidentPhase.Driving; i++)
            {
                StepJourney(10);
                Assert.That(_model.JourneyPositionMicrometers.Value, Is.EqualTo(2_000_000_000L));
                Assert.That(_model.JourneyFuelPicoliters.Value, Is.EqualTo(fuel));
            }
            Assert.That(_model.PrimaryResident.ResidentPhase.Value, Is.EqualTo(FoundationResidentPhase.Driving));
            Assert.That(_model.StopWaterActive.Value, Is.False);
            Assert.That(_model.StopAccessOpen.Value, Is.False);
            Assert.That(_model.WaterCanWaterMilliliters.Value, Is.Zero);
            var navigation = _context.GetUtility<DeckNavigationUtility>();
            Vector3 outside = _layout.DeckCenterLocal + new Vector3(_layout.DeckSize.x * 0.5f + 3, 0, 0);
            Assert.That(navigation.TryCalculateCompleteLocalPath(_model.PrimaryResident.ResidentLocalPosition.Value, outside, out _), Is.False);
            StepJourney(10);
            Assert.That(_model.JourneyPositionMicrometers.Value, Is.LessThan(2_000_000_000L));
            // 尚未远离地点时改变目标返回，以真实到达再次打开通路，水源不重生。
            _context.ExecuteCommand(new SetFoundationJourneyDestinationCommand(NomadJourneyEndpoint.Destination));
            AdvanceUntilArrival();
            Assert.That(_model.StopWaterMilliliters.Value, Is.EqualTo(18_000));
            Assert.That(_model.StopAccessOpen.Value, Is.True);
        }

        [UnityTest]
        public IEnumerator StopWater_CarryingCheckpoint_RewindsToSite_NotVehicle_WithoutMutatingLiveTrip()
        {
            yield return PrepareStopWaterScenario();
            int vehicle = _model.VehicleWaterMilliliters.Value;
            long total = TotalIncludingStopWater();
            _context.ExecuteCommand(new RequestFoundationStopWaterCommand(true));
            AdvanceUntilJourneyPhase(FoundationResidentPhase.ReturningFromStopWater);
            Vector3 livePosition = _model.PrimaryResident.ResidentLocalPosition.Value;
            var saved = CaptureStopCheckpoint();
            Assert.That(saved.Stop.WaterMilliliters, Is.EqualTo(20_000));
            Assert.That(GetInventoryAmount(FindInventory(saved, "vehicle-water-tank"), NomadResourceIds.Water.Value), Is.EqualTo(vehicle));
            Assert.That(_model.StopWaterMilliliters.Value, Is.EqualTo(18_000));
            Assert.That(_model.WaterCanWaterMilliliters.Value, Is.EqualTo(2000));
            Assert.That(_model.PrimaryResident.ResidentLocalPosition.Value, Is.EqualTo(livePosition));
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(JsonUtility.FromJson<NomadWorkshopSaveData>(JsonUtility.ToJson(saved))));
            Assert.That(_model.StopWaterMilliliters.Value, Is.EqualTo(20_000));
            Assert.That(_model.WaterCanWaterMilliliters.Value, Is.Zero);
            Assert.That(TotalIncludingStopWater(), Is.EqualTo(total));
            AdvanceUntilJourneyPhase(FoundationResidentPhase.FillingAtStop);
            FinishStopWaterTrip();
            Assert.That(_model.StopWaterMilliliters.Value, Is.EqualTo(18_000));
            Assert.That(_model.VehicleWaterMilliliters.Value, Is.EqualTo(vehicle + 2000));
        }

        [UnityTest]
        public IEnumerator StopWater_InvalidSiteOrCapacity_IsRejectedBeforeWorldMutation_AndLegacyInitializesOnce()
        {
            yield return PrepareStopWaterScenario(1200);
            string before = JsonUtility.ToJson(CaptureStopCheckpoint());
            var invalid = CaptureStopCheckpoint();
            invalid.Stop.SiteId = "unrelated-site";
            invalid.WorldItems.RemoveAll(item => item.OwnerEntityId == "site:dry-river");
            Assert.Throws<NotSupportedException>(() => _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(invalid)));
            Assert.That(JsonUtility.ToJson(CaptureStopCheckpoint()), Is.EqualTo(before));
            invalid = CaptureStopCheckpoint();
            invalid.Stop.WaterMilliliters = invalid.Stop.WaterCapacityMilliliters + 1;
            Assert.Throws<InvalidOperationException>(() => _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(invalid)));
            Assert.That(JsonUtility.ToJson(CaptureStopCheckpoint()), Is.EqualTo(before));
            var legacy = CaptureStopCheckpoint();
            legacy.Version = 6;
            legacy.WorldItems.RemoveAll(item => item.OwnerEntityId == "site:dry-river");
            legacy.Stop = null;
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(JsonUtility.FromJson<NomadWorkshopSaveData>(JsonUtility.ToJson(legacy))));
            Assert.That(_model.StopWaterMilliliters.Value, Is.EqualTo(20_000));
            Assert.That(CaptureStopCheckpoint().Version, Is.EqualTo(NomadWorkshopSaveSchema.CurrentVersion));
        }

        private IEnumerator PrepareStopWaterScenario(int sourceWater = 20_000)
        {
            yield return PrepareDrivingScenario();
            var saved = CaptureStopCheckpoint();
            saved.Vehicle.Journey.PositionMicrometers = 2_000_000_000L;
            saved.Vehicle.Journey.Destination = NomadJourneyEndpoint.Destination;
            saved.Stop.WaterMilliliters = sourceWater;
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(saved));
            Assert.That(_model.StopAccessOpen.Value, Is.True);
        }

        [UnityTest]
        public IEnumerator StopWater_IncompatibleCanRejectsOrder_AndTripProtectsReturnLayoutUntilRecall()
        {
            yield return PrepareStopWaterScenario();
            _system.ConfigureWaterCanForTests(CargoContainerCapability.Sealable, 1f);
            _context.ExecuteCommand(new RequestFoundationStopWaterCommand(true));
            StepJourney(10);
            Assert.That(_model.StopWaterRequested.Value, Is.False);
            Assert.That(_model.StopWaterActive.Value, Is.False);
            Assert.That(_model.StopWaterMilliliters.Value, Is.EqualTo(20_000));
            _system.ConfigureWaterCanForTests(CargoContainerCapability.LiquidTight, 1f);
            _context.ExecuteCommand(new RequestFoundationStopWaterCommand(true));
            AdvanceUntilJourneyPhase(FoundationResidentPhase.FillingAtStop);
            int facilities = _context.ExecuteCommand(new GetFoundationFacilitiesCommand()).Length;
            _context.ExecuteCommand(new BeginFacilityPlacementCommand("field-kitchen"));
            Assert.That(_model.PlacementPreview.Value.Active, Is.False);
            _context.ExecuteCommand(new ConfirmFacilityPlacementCommand());
            Assert.That(_context.ExecuteCommand(new GetFoundationFacilitiesCommand()).Length, Is.EqualTo(facilities));
            _context.ExecuteCommand(new RequestFoundationStopWaterCommand(false));
            FinishStopWaterTrip();
            _context.ExecuteCommand(new BeginFacilityPlacementCommand("field-kitchen"));
            Assert.That(_model.PlacementPreview.Value.Active, Is.True, "归车后重新允许布局调整。");
        }

        private NomadWorkshopSaveData CaptureStopCheckpoint() =>
            _context.ExecuteCommand(new CaptureFoundationCheckpointCommand());

        private long TotalIncludingStopWater() => CalculateProjectedWaterTotal() + _model.StopWaterMilliliters.Value;

        private void FinishStopWaterTrip()
        {
            for (var i = 0; i < 30_000 && (_model.StopWaterRequested.Value || _model.StopWaterActive.Value); i++)
                StepJourney(10);
            Assert.That(_model.StopWaterActive.Value, Is.False, _model.PrimaryResident.CurrentTask.Value);
            Assert.That(_model.StopWaterRequested.Value, Is.False);
        }
    }
}
