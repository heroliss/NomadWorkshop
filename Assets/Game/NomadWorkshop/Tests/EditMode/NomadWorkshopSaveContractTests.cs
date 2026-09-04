using System;
using System.Collections.Generic;
using Game.NomadWorkshop.Simulation.Persistence;
using NUnit.Framework;

namespace Game.NomadWorkshop.Simulation.Tests
{
    /// <summary>锁定自由姿态量化、完整快照不变量和中途行动的幂等恢复决策。</summary>
    public sealed class NomadWorkshopSaveContractTests
    {
        [Test]
        public void QuantizedDeckPose_RoundsMillimetersAndNormalizesYaw()
        {
            DeckPose runtimePose = DeckPose.FromMeters(1.2346d, -0.5006d, -15.04d, 2);
            QuantizedDeckPose pose = QuantizedDeckPose.FromDeckPose(runtimePose);

            Assert.AreEqual(1235, pose.XMillimeters);
            Assert.AreEqual(-501, pose.ZMillimeters);
            Assert.AreEqual(3450, pose.YawDeciDegrees);
            Assert.AreEqual(2, pose.DeckLevel);
            Assert.AreEqual(1.235f, pose.XMeters, 0.0001f);
            Assert.AreEqual(345f, pose.YawDegrees, 0.0001f);
            Assert.AreEqual(runtimePose, pose.ToDeckPose());
        }

        [Test]
        public void ValidateForSave_AcceptsCoherentMidInteractionSnapshot()
        {
            NomadWorkshopSaveData save = CreateValidSave();

            Assert.DoesNotThrow(() => NomadWorkshopSaveContract.ValidateForSave(save));
            Assert.AreEqual(
                NomadActionRestoreDisposition.ResumePresentationAfterCommittedOutcome,
                NomadWorkshopSaveContract.GetRestoreDisposition(save.Residents[0].ActiveAction),
                "交接已提交后加载只能恢复关门表现，不能再执行一次库存转移。");
        }

        [Test]
        public void PrepareAfterLoad_RejectsVersionOneWithoutMeasurementMigration()
        {
            NomadWorkshopSaveData versionOne = CreateValidSave();
            versionOne.Version = 1;

            Assert.Throws<NotSupportedException>(
                () => NomadWorkshopSaveContract.PrepareAfterLoad(versionOne),
                "v1 的 Amount / Capacity 没有量纲，不能把旧数字静默解释为 v2 的件或 mL。");
        }

        [Test]
        public void ValidateForSave_RejectsDuplicateEntityAndOverfilledInventory()
        {
            NomadWorkshopSaveData duplicate = CreateValidSave();
            duplicate.Blueprints.Add(new NomadBlueprintSaveData
            {
                InstanceId = duplicate.Facilities[0].InstanceId,
                DefinitionId = "water-recycler",
                Pose = new QuantizedDeckPose(0, 0, 0),
            });
            Assert.Throws<InvalidOperationException>(
                () => NomadWorkshopSaveContract.ValidateForSave(duplicate));

            NomadWorkshopSaveData overfilled = CreateValidSave();
            overfilled.Inventories[0].CapacityBaseUnits = 0;
            Assert.Throws<InvalidOperationException>(
                () => NomadWorkshopSaveContract.ValidateForSave(overfilled));

            NomadWorkshopSaveData mismatchedMeasure = CreateValidSave();
            mismatchedMeasure.Inventories[0].Contents[0].Measure = ResourceMeasure.Milliliter;
            Assert.Throws<InvalidOperationException>(
                () => NomadWorkshopSaveContract.ValidateForSave(mismatchedMeasure));
        }

        [Test]
        public void ValidateForSave_RejectsOutOfRangePersistentContaminationAndResidentConditions()
        {
            NomadWorkshopSaveData dirtyContainer = CreateValidSave();
            dirtyContainer.Inventories[0].ContaminationPermille = -1;
            Assert.Throws<InvalidOperationException>(
                () => NomadWorkshopSaveContract.ValidateForSave(dirtyContainer));

            NomadWorkshopSaveData dirtyHands = CreateValidSave();
            dirtyHands.Residents[0].HandContaminationPermille = 1001;
            Assert.Throws<InvalidOperationException>(
                () => NomadWorkshopSaveContract.ValidateForSave(dirtyHands));

            NomadWorkshopSaveData hygiene = CreateValidSave();
            hygiene.Residents[0].BodyHygieneDeficitPermille = -1;
            Assert.Throws<InvalidOperationException>(
                () => NomadWorkshopSaveContract.ValidateForSave(hygiene));

            NomadWorkshopSaveData motionSickness = CreateValidSave();
            motionSickness.Residents[0].MotionSicknessPermille = 1200;
            Assert.Throws<InvalidOperationException>(
                () => NomadWorkshopSaveContract.ValidateForSave(motionSickness));
        }

        [Test]
        public void ValidateForSave_RejectsInvalidOrDuplicateRandomStreamCursor()
        {
            NomadWorkshopSaveData negative = CreateValidSave();
            negative.RandomStreams[0].NextEventSequence = -1;
            Assert.Throws<InvalidOperationException>(
                () => NomadWorkshopSaveContract.ValidateForSave(negative));

            NomadWorkshopSaveData duplicate = CreateValidSave();
            duplicate.RandomStreams.Add(new NomadRandomStreamSaveData
            {
                OwnerEntityId = duplicate.RandomStreams[0].OwnerEntityId,
                StreamId = duplicate.RandomStreams[0].StreamId,
                NextEventSequence = 99,
            });
            Assert.Throws<InvalidOperationException>(
                () => NomadWorkshopSaveContract.ValidateForSave(duplicate));

            NomadWorkshopSaveData nonCanonical = CreateValidSave();
            nonCanonical.RandomStreams[0].StreamId = " resident-decision ";
            Assert.Throws<InvalidOperationException>(
                () => NomadWorkshopSaveContract.ValidateForSave(nonCanonical));
        }

        [Test]
        public void RestoreOrder_IsStableAndTransientPathIsNeverPartOfContract()
        {
            NomadWorkshopSaveData save = CreateValidSave();
            save.Inventories.Add(new NomadInventorySaveData
            {
                InventoryId = "resident-a-hands",
                OwnerEntityId = "resident-a",
                Measure = ResourceMeasure.Item,
                CapacityBaseUnits = 1,
            });
            save.Residents.Add(new NomadResidentSaveData
            {
                ResidentId = "resident-a",
                PersonalInventoryId = "resident-a-hands",
                Pose = new QuantizedDeckPose(-2000, 300, 0),
            });

            IReadOnlyList<NomadResidentSaveData> order =
                NomadWorkshopSaveContract.GetReservationRestoreOrder(save);

            Assert.AreEqual("resident-a", order[0].ResidentId);
            Assert.AreEqual("resident-b", order[1].ResidentId);
            Assert.AreEqual(
                NomadActionRestoreDisposition.ReplanPath,
                NomadWorkshopSaveContract.GetRestoreDisposition(new NomadResidentActionSaveData
                {
                    Stage = NomadResidentActionSaveStage.Moving,
                }));
        }

        internal static NomadWorkshopSaveData CreateValidSave()
        {
            var save = new NomadWorkshopSaveData
            {
                WorldSeed = 94217,
                SimulationTick = 123456,
                Vehicle = new NomadVehicleSaveData
                {
                    MapXCentimeters = 1200,
                    MapZCentimeters = -340,
                    CurrentRegionId = "salt-marsh",
                    DestinationId = "relay-07",
                    TravelProgressPermille = 420,
                    FuelMilliUnits = 18600,
                    IsTraveling = true,
                },
            };
            save.Facilities.Add(new NomadFacilitySaveData
            {
                InstanceId = "facility-cabinet-01",
                DefinitionId = "parts-cabinet",
                Pose = QuantizedDeckPose.FromMeters(1.25f, -0.75f, 27f),
                DurabilityPermille = 810,
                DirtPermille = 230,
            });
            save.Blueprints.Add(new NomadBlueprintSaveData
            {
                InstanceId = "blueprint-kitchen-02",
                DefinitionId = "field-kitchen",
                Pose = QuantizedDeckPose.FromMeters(-2.1f, 1.4f, 12.3f),
                Stage = NomadBlueprintSaveStage.AwaitingMaterials,
                ConstructionProgressPermille = 120,
            });
            save.Inventories.Add(new NomadInventorySaveData
            {
                InventoryId = "resident-b-hands",
                OwnerEntityId = "resident-b",
                Measure = ResourceMeasure.Item,
                CapacityBaseUnits = 2,
                ContaminationPermille = 110,
                Contents = new List<NomadResourceStackSaveData>
                {
                    new()
                    {
                        StackId = "parts-0004",
                        ResourceId = "repair-parts",
                        Measure = ResourceMeasure.Item,
                        AmountBaseUnits = 1,
                        ConditionPermille = 930,
                        ContaminationPermille = 80,
                    },
                },
            });
            save.Residents.Add(new NomadResidentSaveData
            {
                ResidentId = "resident-b",
                PersonalInventoryId = "resident-b-hands",
                Pose = QuantizedDeckPose.FromMeters(0.6f, -1.2f, 27f),
                HungerPermille = 350,
                ThirstPermille = 610,
                FatiguePermille = 280,
                StressPermille = 190,
                BodyHygieneDeficitPermille = 240,
                HandContaminationPermille = 430,
                MotionSicknessPermille = 170,
                ActiveAction = new NomadResidentActionSaveData
                {
                    TaskId = "pickup-parts-17",
                    ActionId = "pickup-from-cabinet",
                    TargetEntityId = "facility-cabinet-01",
                    InteractionGroupId = "cabinet-storage-door",
                    InteractionSlotId = "right",
                    DestinationPose = QuantizedDeckPose.FromMeters(0.82f, -1.47f, 27f),
                    Stage = NomadResidentActionSaveStage.Closing,
                    ProgressPermille = 400,
                    OutcomeCommitted = true,
                },
            });
            save.RandomStreams.Add(new NomadRandomStreamSaveData
            {
                OwnerEntityId = "resident-b",
                StreamId = "resident-decision",
                NextEventSequence = 18,
            });
            return save;
        }
    }
}
