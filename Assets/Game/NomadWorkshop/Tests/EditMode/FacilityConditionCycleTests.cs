using NUnit.Framework;

namespace Game.NomadWorkshop.Simulation.Tests
{
    public sealed class FacilityConditionCycleTests
    {
        private static readonly FacilityConditionExposure AcceleratedExposure = new(
            wearUnitsPerMillisecond: 900,
            maintenanceDebtUnitsPerMillisecond: 1600,
            dustUnitsPerMillisecond: 1300,
            baseMicroHazardPerSecond: 600,
            wearMicroHazardPerPermilleSecond: 6,
            maintenanceMicroHazardPerPermilleSecond: 10,
            dustMicroHazardPerPermilleSecond: 8);

        [Test]
        public void CombinedAndSplitAdvance_ProduceIdenticalStateAndFailureTick()
        {
            FacilityConditionCycle combined = FacilityConditionCycle.Create(
                1729,
                "initial-vehicle-water-tank");
            FacilityConditionCycle split = FacilityConditionCycle.Create(
                1729,
                "initial-vehicle-water-tank");

            combined.AdvanceTo(200_000L, AcceleratedExposure);
            split.AdvanceTo(23_117L, AcceleratedExposure);
            split.AdvanceTo(81_003L, AcceleratedExposure);
            split.AdvanceTo(200_000L, AcceleratedExposure);

            AssertCheckpointEqual(combined.CaptureCheckpoint(), split.CaptureCheckpoint());
            Assert.That(combined.IsFaulted, Is.True, "加速场景应实际跨过一次风险阈值。");
            Assert.That(combined.FaultTriggeredSimulationTick, Is.GreaterThan(0L));
        }

        [Test]
        public void SameTickPause_DoesNotChangeConditionOrHazard()
        {
            FacilityConditionCycle cycle = FacilityConditionCycle.Create(8, "tank-pause", 500L);
            FacilityConditionCheckpoint before = cycle.CaptureCheckpoint();

            Assert.That(cycle.AdvanceTo(500L, AcceleratedExposure), Is.False);

            AssertCheckpointEqual(before, cycle.CaptureCheckpoint());
        }

        [Test]
        public void RiskRateUnits_IntegrateMicroHazardPerSecondAndPermilleExactly()
        {
            FacilityConditionCheckpoint initial = CreateHighThresholdCheckpoint("tank-units");
            var cycle = new FacilityConditionCycle(12, initial);
            cycle.ApplyConditionShock(
                wearPermille: 500,
                maintenanceDebtPermille: 0,
                dustPermille: 0);
            var exposure = new FacilityConditionExposure(
                wearUnitsPerMillisecond: 0,
                maintenanceDebtUnitsPerMillisecond: 0,
                dustUnitsPerMillisecond: 0,
                baseMicroHazardPerSecond: 1000,
                wearMicroHazardPerPermilleSecond: 2,
                maintenanceMicroHazardPerPermilleSecond: 0,
                dustMicroHazardPerPermilleSecond: 0);

            cycle.AdvanceTo(1000L, exposure);

            Assert.That(
                cycle.AccumulatedFailureMicroHazard,
                Is.EqualTo(2000L),
                "1 秒基础 1000 微风险，加上 500‰ × 2 微风险/‰/秒，应精确得到 2000。");
            Assert.That(cycle.CaptureCheckpoint().HazardSubMicroRemainder, Is.Zero);
        }

        [Test]
        public void Maintenance_ReducesFutureRiskButNotWearOrPastExposure()
        {
            FacilityConditionCheckpoint initial = CreateHighThresholdCheckpoint("tank-maintain");
            var maintained = new FacilityConditionCycle(99, initial);
            var ignored = new FacilityConditionCycle(99, initial);
            maintained.AdvanceTo(50_000L, AcceleratedExposure);
            ignored.AdvanceTo(50_000L, AcceleratedExposure);

            long accumulatedBefore = maintained.AccumulatedFailureMicroHazard;
            int wearBefore = maintained.WearPermille;
            Assert.That(maintained.PerformMaintenance(600, 600), Is.True);

            Assert.That(maintained.WearPermille, Is.EqualTo(wearBefore));
            Assert.That(
                maintained.AccumulatedFailureMicroHazard,
                Is.EqualTo(accumulatedBefore),
                "保养只能改变后续瞬时风险率，不能倒扣已经暴露的累计风险。");
            maintained.AdvanceTo(80_000L, AcceleratedExposure);
            ignored.AdvanceTo(80_000L, AcceleratedExposure);

            Assert.That(
                maintained.AccumulatedFailureMicroHazard,
                Is.LessThan(ignored.AccumulatedFailureMicroHazard));
            Assert.That(maintained.WearPermille, Is.EqualTo(ignored.WearPermille));
        }

        [Test]
        public void CheckpointRestore_ContinuesTheSameRiskTrajectory()
        {
            FacilityConditionCycle original = FacilityConditionCycle.Create(1729, "tank-restore");
            original.AdvanceTo(37_321L, AcceleratedExposure);
            FacilityConditionCheckpoint checkpoint = original.CaptureCheckpoint();
            var restored = new FacilityConditionCycle(1729, checkpoint);

            original.AdvanceTo(200_000L, AcceleratedExposure);
            restored.AdvanceTo(200_000L, AcceleratedExposure);

            AssertCheckpointEqual(original.CaptureCheckpoint(), restored.CaptureCheckpoint());
        }

        [Test]
        public void Repair_StartsNewCycleWithoutMakingOldEquipmentNew()
        {
            FacilityConditionCycle cycle = FacilityConditionCycle.Create(42, "tank-repair");
            cycle.ApplyConditionShock(240, 680, 530);
            long firstSequence = cycle.FailureCycleSequence;
            Assert.That(
                cycle.ApplyHazardShock(cycle.FailureThresholdMicroHazard),
                Is.True);
            Assert.That(cycle.ActiveFault, Is.EqualTo(FacilityFaultKind.OutletValveJammed));
            int wear = cycle.WearPermille;
            int maintenance = cycle.MaintenanceDebtPermille;
            int dust = cycle.DustPermille;

            Assert.That(cycle.Repair(), Is.True);

            Assert.That(cycle.ActiveFault, Is.EqualTo(FacilityFaultKind.None));
            Assert.That(cycle.AccumulatedFailureMicroHazard, Is.Zero);
            Assert.That(cycle.FailureCycleSequence, Is.EqualTo(firstSequence + 1L));
            Assert.That(cycle.WearPermille, Is.EqualTo(wear));
            Assert.That(cycle.MaintenanceDebtPermille, Is.EqualTo(maintenance));
            Assert.That(cycle.DustPermille, Is.EqualTo(dust));
            Assert.That(cycle.Repair(), Is.False);
        }

        [Test]
        public void Warning_ExposesServiceAndFaultWithoutFlatteningSources()
        {
            FacilityConditionCycle cycle = FacilityConditionCycle.Create(3, "tank-warning");
            cycle.ApplyConditionShock(120, 560, 510);

            Assert.That(cycle.Warning, Is.EqualTo(FacilityConditionWarning.ServiceDue));
            Assert.That(cycle.WearPermille, Is.EqualTo(120));
            Assert.That(cycle.MaintenanceDebtPermille, Is.EqualTo(560));
            Assert.That(cycle.DustPermille, Is.EqualTo(510));

            cycle.ApplyHazardShock(cycle.FailureThresholdMicroHazard);
            Assert.That(cycle.Warning, Is.EqualTo(FacilityConditionWarning.Faulted));
        }

        private static FacilityConditionCheckpoint CreateHighThresholdCheckpoint(string id) => new(
            id,
            0L,
            0L,
            0L,
            0L,
            failureThresholdMicroHazard: 10_000_000_000L,
            accumulatedFailureMicroHazard: 0L,
            hazardSubMicroRemainder: 0L,
            failureCycleSequence: 0L,
            FacilityFaultKind.None,
            faultSeverityPermille: 0,
            faultTriggeredSimulationTick: 0L);

        private static void AssertCheckpointEqual(
            in FacilityConditionCheckpoint expected,
            in FacilityConditionCheckpoint actual)
        {
            Assert.That(actual.FacilityId, Is.EqualTo(expected.FacilityId));
            Assert.That(
                actual.LastSettledSimulationTick,
                Is.EqualTo(expected.LastSettledSimulationTick));
            Assert.That(actual.WearUnits, Is.EqualTo(expected.WearUnits));
            Assert.That(actual.MaintenanceDebtUnits, Is.EqualTo(expected.MaintenanceDebtUnits));
            Assert.That(actual.DustUnits, Is.EqualTo(expected.DustUnits));
            Assert.That(
                actual.FailureThresholdMicroHazard,
                Is.EqualTo(expected.FailureThresholdMicroHazard));
            Assert.That(
                actual.AccumulatedFailureMicroHazard,
                Is.EqualTo(expected.AccumulatedFailureMicroHazard));
            Assert.That(
                actual.HazardSubMicroRemainder,
                Is.EqualTo(expected.HazardSubMicroRemainder));
            Assert.That(actual.FailureCycleSequence, Is.EqualTo(expected.FailureCycleSequence));
            Assert.That(actual.ActiveFault, Is.EqualTo(expected.ActiveFault));
            Assert.That(actual.FaultSeverityPermille, Is.EqualTo(expected.FaultSeverityPermille));
            Assert.That(
                actual.FaultTriggeredSimulationTick,
                Is.EqualTo(expected.FaultTriggeredSimulationTick));
        }
    }
}
