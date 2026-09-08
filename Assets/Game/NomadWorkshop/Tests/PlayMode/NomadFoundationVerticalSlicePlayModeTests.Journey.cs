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
        public IEnumerator JourneyGoal_RequiresPhysicalDock_AndCancellationImmediatelyStopsTravel()
        {
            yield return PrepareDrivingScenario();
            long initialFuel = _model.JourneyFuelPicoliters.Value;
            _context.ExecuteCommand(new SetFoundationJourneyDestinationCommand(NomadJourneyEndpoint.Destination));
            Assert.That(_model.JourneyStatus.Value, Is.EqualTo(NomadJourneyStatus.AwaitingDriver));
            AdvanceUntilJourneyPhase(FoundationResidentPhase.MovingToDriver);
            Assert.That(_model.JourneyPositionMicrometers.Value, Is.Zero);
            Assert.That(_model.JourneyFuelPicoliters.Value, Is.EqualTo(initialFuel));
            Assert.That(_model.PrimaryResident.RemainingPathMeters.Value, Is.GreaterThan(0.1f));
            AdvanceUntilJourneyPhase(FoundationResidentPhase.Driving);
            FoundationFacilityState driver = Array.Find(
                _context.ExecuteCommand(new GetFoundationFacilitiesCommand()),
                facility => facility.DefinitionId == "driver-station");
            AssertResidentDockedToGroup(driver, FindDefinition(_definitions, "driver-station"), "drive",
                _context.ExecuteCommand(new GetFoundationReadModelCommand()));
            Assert.That(_model.JourneyPositionMicrometers.Value, Is.Zero,
                "到岗步骤不补算途中等待的行驶时间。");
            _context.ExecuteCommand(new SetFoundationJourneyDestinationCommand(NomadJourneyEndpoint.Destination));
            StepJourney(1000);
            Assert.That(_model.JourneyPositionMicrometers.Value, Is.EqualTo(800_000L),
                "1.6 m/s² 的平滑起步在第一秒应只行驶 0.8 米，不应瞬间达到巡航速度。");
            Assert.That(_model.JourneyFuelPicoliters.Value, Is.EqualTo(initialFuel - 2_000_000_000L));
            FoundationSoakRunResult drivingRun = _context.ExecuteCommand(
                new RunFoundationSoakHarnessCommand(35_000L, 1000, 30_000L));
            Assert.That(drivingRun.StopReason, Is.EqualTo(FoundationSoakStopReason.DurationReached),
                "居民持续留在驾驶岗位时，车辆行进不能被旧停滞检测误报。");
            Assert.That(_model.JourneyPositionMicrometers.Value, Is.EqualTo(268_000_000L),
                "达到巡航速度前的加速段应让长段旅途比旧恒速轨迹更平滑。");
            long stoppedFuel = _model.JourneyFuelPicoliters.Value;
            _context.ExecuteCommand(new SetFoundationJourneyDestinationCommand(NomadJourneyEndpoint.None));
            Assert.That(_model.JourneyStatus.Value, Is.EqualTo(NomadJourneyStatus.NoDestination));
            StepJourney(2000);
            Assert.That(_model.JourneyPositionMicrometers.Value, Is.EqualTo(268_000_000L));
            Assert.That(_model.JourneyFuelPicoliters.Value, Is.EqualTo(stoppedFuel));
        }

        [UnityTest]
        public IEnumerator JourneyAnchorDestination_StopsAtRouteInterestPoint_AndEndpointChangeClearsAnchor()
        {
            yield return PrepareDrivingScenario();
            _context.ExecuteCommand(new SetFoundationJourneyAnchorDestinationCommand("midway-shelter"));
            Assert.That(_model.JourneyDestination.Value, Is.EqualTo(NomadJourneyEndpoint.Destination));
            Assert.That(_model.JourneyDestinationAnchorId.Value, Is.EqualTo("midway-shelter"));
            Assert.That(_model.JourneyStatus.Value, Is.EqualTo(NomadJourneyStatus.AwaitingDriver));

            AdvanceUntilJourneyPhase(FoundationResidentPhase.Driving);
            for (var i = 0; i < 2000 && _model.JourneyStatus.Value != NomadJourneyStatus.Arrived; i++)
                StepJourney(1000);

            Assert.That(_model.JourneyStatus.Value, Is.EqualTo(NomadJourneyStatus.Arrived));
            Assert.That(_model.JourneyPositionMicrometers.Value, Is.EqualTo(1_000_000_000L));
            Assert.That(_model.JourneyDestinationAnchorId.Value, Is.EqualTo("midway-shelter"));
            Assert.That(_model.StopAccessOpen.Value, Is.False,
                "中途兴趣点到站不能误开干河驿站的取水、清运和备件权限。");

            _context.ExecuteCommand(new SetFoundationJourneyDestinationCommand(NomadJourneyEndpoint.Destination));
            Assert.That(_model.JourneyDestinationAnchorId.Value, Is.Empty);
            Assert.That(_model.JourneyStatus.Value, Is.EqualTo(NomadJourneyStatus.AwaitingDriver));
        }

        [UnityTest]
        public IEnumerator JourneyDriver_LeavesForThirst_StopsVehicle_AndReturnsAfterRealDrink()
        {
            yield return PrepareDrivingScenario();
            _context.ExecuteCommand(new SetFoundationJourneyDestinationCommand(NomadJourneyEndpoint.Destination));
            AdvanceUntilJourneyPhase(FoundationResidentPhase.Driving);
            for (var i = 0; i < 20_000 && _model.PrimaryResident.ResidentPhase.Value == FoundationResidentPhase.Driving; i++)
                StepJourney(10);
            Assert.That(_model.JourneyStatus.Value, Is.EqualTo(NomadJourneyStatus.AwaitingDriver));
            Assert.That(_model.PrimaryResident.ResidentThirst.Value, Is.GreaterThanOrEqualTo(0.75f));
            long stoppedPosition = _model.JourneyPositionMicrometers.Value;
            long stoppedFuel = _model.JourneyFuelPicoliters.Value;
            int previousDrinks = _model.PrimaryResident.CompletedDrinkCount.Value;
            bool drank = false;
            for (var i = 0; i < 30_000 && _model.PrimaryResident.ResidentPhase.Value != FoundationResidentPhase.Driving; i++)
            {
                StepJourney(10);
                drank |= _model.PrimaryResident.CompletedDrinkCount.Value > previousDrinks;
                Assert.That(_model.JourneyPositionMicrometers.Value, Is.EqualTo(stoppedPosition));
                Assert.That(_model.JourneyFuelPicoliters.Value, Is.EqualTo(stoppedFuel));
            }
            Assert.That(drank, Is.True, "应通过正式水库存与饮水行动降低口渴。");
            Assert.That(_model.PrimaryResident.ResidentPhase.Value, Is.EqualTo(FoundationResidentPhase.Driving));
            StepJourney(10);
            Assert.That(_model.JourneyPositionMicrometers.Value, Is.GreaterThan(stoppedPosition));
        }

        [UnityTest]
        public IEnumerator JourneyCheckpoint_JsonRoundTripPreservesExactWorld_RejectsForeignRouteAtomically()
        {
            yield return PrepareDrivingScenario();
            _context.ExecuteCommand(new SetFoundationJourneyDestinationCommand(NomadJourneyEndpoint.Destination));
            AdvanceUntilJourneyPhase(FoundationResidentPhase.Driving);
            StepJourney(1370);
            NomadWorkshopSaveData checkpoint = _context.ExecuteCommand(new CaptureFoundationCheckpointCommand());
            string json = JsonUtility.ToJson(checkpoint);
            StepJourney(1000);
            var restored = JsonUtility.FromJson<NomadWorkshopSaveData>(json);
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(restored));
            Assert.That(_model.JourneyPositionMicrometers.Value, Is.EqualTo(checkpoint.Vehicle.Journey.PositionMicrometers));
            Assert.That(_model.JourneyFuelPicoliters.Value, Is.EqualTo(checkpoint.Vehicle.Journey.FuelPicoliters));
            Assert.That(_model.JourneyStatus.Value, Is.EqualTo(NomadJourneyStatus.AwaitingDriver));
            Assert.That(_model.PrimaryResident.ResidentPhase.Value, Is.EqualTo(FoundationResidentPhase.Idle));
            var invalid = JsonUtility.FromJson<NomadWorkshopSaveData>(json);
            invalid.Vehicle.Journey.RouteId = "foreign-route";
            Assert.Throws<ArgumentException>(() =>
                _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(invalid)));
            Assert.That(_model.JourneyPositionMicrometers.Value, Is.EqualTo(checkpoint.Vehicle.Journey.PositionMicrometers));
            Assert.That(_model.JourneyFuelPicoliters.Value, Is.EqualTo(checkpoint.Vehicle.Journey.FuelPicoliters));
            _context.ExecuteCommand(new SetFoundationJourneyDestinationCommand(NomadJourneyEndpoint.None));
            StepJourney(1000);
            Assert.That(_model.JourneyPositionMicrometers.Value, Is.EqualTo(checkpoint.Vehicle.Journey.PositionMicrometers));

            var legacy = JsonUtility.FromJson<NomadWorkshopSaveData>(json);
            legacy.Vehicle.Journey.SpeedMillimetersPerSecond = 10_000;
            legacy.Vehicle.Journey.CurrentSpeedNanometersPerMillisecond = 0L;
            legacy.Vehicle.Journey.DistanceRemainderHalfNanometers = 0L;
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(legacy));
            Assert.That(_model.JourneyPositionMicrometers.Value,
                Is.EqualTo(checkpoint.Vehicle.Journey.PositionMicrometers),
                "旧 10 m/s 恒速检查点应迁移到当前 8 m/s 平滑路线，而不是拒绝或重置位置。");
            Assert.That(_model.JourneyFuelPicoliters.Value,
                Is.EqualTo(checkpoint.Vehicle.Journey.FuelPicoliters));
        }

        [UnityTest]
        public IEnumerator JourneyDriver_CompletesOutwardAndReturnLeg_WithConservedFuelAndLivingNeeds()
        {
            yield return PrepareDrivingScenario();
            _context.ExecuteCommand(new SetFoundationJourneyDestinationCommand(NomadJourneyEndpoint.Destination));
            AdvanceUntilArrival();
            Assert.That(_model.JourneyPositionMicrometers.Value, Is.EqualTo(2_000_000_000L));
            Assert.That(_model.JourneyFuelPicoliters.Value, Is.EqualTo(15_000_000_000_000L));
            Assert.That(_model.PrimaryResident.ResidentPhase.Value, Is.Not.EqualTo(FoundationResidentPhase.Driving));
            _context.ExecuteCommand(new SetFoundationJourneyDestinationCommand(NomadJourneyEndpoint.Origin));
            AdvanceUntilArrival();
            Assert.That(_model.JourneyPositionMicrometers.Value, Is.Zero);
            Assert.That(_model.JourneyFuelPicoliters.Value, Is.EqualTo(10_000_000_000_000L));
            Assert.That(_model.PrimaryResident.ResidentHealth.Value, Is.GreaterThan(0f));
            Assert.That(_model.PrimaryResident.CompletedDrinkCount.Value, Is.GreaterThan(0));
            Assert.That(_model.PrimaryResident.CompletedToiletUseCount.Value, Is.GreaterThan(0));
        }

        [UnityTest]
        public IEnumerator JourneyDriver_DisplacedByConstruction_StopsBeforeNextTravelStep()
        {
            yield return PrepareDrivingScenario();
            _context.ExecuteCommand(new SetFoundationJourneyDestinationCommand(NomadJourneyEndpoint.Destination));
            AdvanceUntilJourneyPhase(FoundationResidentPhase.Driving);
            StepJourney(1000);
            long position = _model.JourneyPositionMicrometers.Value;
            long fuel = _model.JourneyFuelPicoliters.Value;
            yield return BuildFacility("drinking-station", 3800, 2550);
            StepJourney(10);
            Assert.That(_model.JourneyPositionMicrometers.Value, Is.EqualTo(position));
            Assert.That(_model.JourneyFuelPicoliters.Value, Is.EqualTo(fuel));
            Assert.That(_model.JourneyStatus.Value, Is.EqualTo(NomadJourneyStatus.AwaitingDriver));
            Assert.That(_model.PrimaryResident.ResidentPhase.Value, Is.Not.EqualTo(FoundationResidentPhase.Driving));
        }

        private IEnumerator PrepareDrivingScenario()
        {
            yield return ResetAndBuildSoakScenario(1729);
            yield return BuildFacility("driver-station", 3800, 3400);
            NomadWorkshopSaveData checkpoint = _context.ExecuteCommand(new CaptureFoundationCheckpointCommand());
            checkpoint.Residents[0].ThirstPermille = 200;
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(checkpoint));
            Assert.That(_model.IsPaused.Value, Is.True);
        }

        private void AdvanceUntilJourneyPhase(FoundationResidentPhase phase)
        {
            for (var i = 0; i < 30_000 && _model.PrimaryResident.ResidentPhase.Value != phase; i++) StepJourney(10);
            Assert.That(_model.PrimaryResident.ResidentPhase.Value, Is.EqualTo(phase),
                $"实际任务：{_model.PrimaryResident.CurrentTask.Value}；诊断：{_model.PrimaryResident.LastBlocker.Value}");
        }

        private void AdvanceUntilArrival()
        {
            for (var i = 0; i < 2000 && _model.JourneyStatus.Value != NomadJourneyStatus.Arrived; i++)
                StepJourney(1000);
            Assert.That(_model.JourneyStatus.Value, Is.EqualTo(NomadJourneyStatus.Arrived),
                $"实际任务：{_model.PrimaryResident.CurrentTask.Value}；诊断：{_model.PrimaryResident.LastBlocker.Value}");
        }

        private void StepJourney(long milliseconds)
        {
            long previousTick = _model.SimulationTick.Value;
            FoundationResidentPhase previousPhase = _model.PrimaryResident.ResidentPhase.Value;
            string previousTask = _model.PrimaryResident.CurrentTask.Value;
            string previousBlocker = _model.PrimaryResident.LastBlocker.Value;
            FoundationSoakRunResult result = _context.ExecuteCommand(new RunFoundationSoakHarnessCommand(milliseconds, 10));
            if (result.StopReason == FoundationSoakStopReason.DurationReached) return;
            Assert.Fail($"旅途推进中断：{result.StopReason}；推进前 tick={previousTick}, phase={previousPhase}, " +
                $"task={previousTask}, blocker={previousBlocker}；can={_model.WaterCanLocation.Value}/{_model.WaterCanCarrierId.Value}, " +
                $"water={_model.VehicleWaterMilliliters.Value}/{_model.WaterCanWaterMilliliters.Value}/{_model.DrinkingStationWaterMilliliters.Value}, " +
                $"drinks={_model.PrimaryResident.CompletedDrinkCount.Value}；{_model.PrimaryResident.LastBlocker.Value}");
        }

        private static NomadFacilityDefinition CreateDriverDefinition()
        {
            var definition = ScriptableObject.CreateInstance<NomadFacilityDefinition>();
            definition.ConfigureForTests("driver-station", "驾驶台", NomadFacilityFunction.DriverStation,
                true, new[] { new NomadFacilityFootprintPartDefinition(Vector2.zero, new Vector2(1.1f, 0.7f)) },
                RequiredGroup("drive", new NomadFacilityInteractionSlotDefinition("seat", new Vector2(0f, -0.85f))),
                false, Vector2.zero, 0f, new Vector3(1f, 0.9f, 0.65f), Color.yellow);
            return definition;
        }
    }
}
