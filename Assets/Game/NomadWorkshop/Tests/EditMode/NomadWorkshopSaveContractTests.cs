using System;
using System.Collections.Generic;
using Game.NomadWorkshop.Simulation.Persistence;
using NUnit.Framework;

namespace Game.NomadWorkshop.Simulation.Tests
{
    /// <summary>锁定自由姿态量化、完整快照不变量和中途行动的幂等恢复决策。</summary>
    public sealed class NomadWorkshopSaveContractTests
    {
        [TestCase(null)]
        [TestCase("")]
        [TestCase("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
        public void FacilitySpaceSignature_AcceptsLegacyMissingAndCanonicalDigest(string signature)
        {
            var save = CreateValidSave();
            save.Facilities[0].SpaceSignature = signature;
            Assert.DoesNotThrow(() => NomadWorkshopSaveContract.ValidateForSave(save));
        }

        [TestCase("short")]
        [TestCase("0123456789ABCDEF0123456789abcdef0123456789abcdef0123456789abcdef")]
        [TestCase("g123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
        public void FacilitySpaceSignature_RejectsMalformedDigest(string signature)
        {
            var save = CreateValidSave();
            save.Facilities[0].SpaceSignature = signature;
            Assert.Throws<InvalidOperationException>(() => NomadWorkshopSaveContract.ValidateForSave(save));
        }

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
        public void JourneySave_PreservesSubMillimeterAndSubMilliliterTruthWithoutDriverOwnership()
        {
            var route = new NomadJourneyRoute("route", "a", "b", 1000, 137, 3);
            using var session = new NomadJourneySession(route, 99_999_999L);
            session.SetDestination(NomadJourneyEndpoint.Destination);
            Assert.That(session.TryAcquireDriver("driver", session.DestinationRevision, out _), Is.True);
            session.Advance(1);
            NomadWorkshopSaveData save = CreateValidSave();
            save.Vehicle.Journey = NomadJourneySaveData.FromSnapshot(session.Capture());
            NomadWorkshopSaveContract.ValidateForSave(save);
            Assert.That(save.Vehicle.Journey.CurrentSpeedNanometersPerMillisecond,
                Is.EqualTo(137_000L),
                "Journey 检查点应保存当前运动速度，避免平滑路线读取后出现速度跳变。");
            using var restored = new NomadJourneySession(route, 0);
            restored.Restore(save.Vehicle.Journey.ToValidatedSnapshot());
            Assert.That(restored.PositionMicrometers, Is.EqualTo(137L));
            Assert.That(restored.FuelPicoliters, Is.EqualTo(99_999_588L));
            Assert.That(restored.CurrentSpeedNanometersPerMillisecond, Is.EqualTo(137_000L));
            Assert.That(restored.Status, Is.EqualTo(NomadJourneyStatus.AwaitingDriver));
        }

        [Test]
        public void VersionFourJourneyMigration_PreservesExistingWorld_AndLeavesJourneyForAdapterInitialization()
        {
            NomadWorkshopSaveData save = CreateValidSave();
            save.Version = 4;
            int thirst = save.Residents[0].ThirstPermille;
            NomadWorkshopSaveContract.PrepareAfterLoad(save);
            Assert.That(save.Version, Is.EqualTo(NomadWorkshopSaveSchema.CurrentVersion));
            Assert.That(save.Vehicle.Journey, Is.Null);
            Assert.That(save.Residents[0].ThirstPermille, Is.EqualTo(thirst));
        }

        [Test]
        public void EmptyJourneyDto_IsLegacyMissingState_ButPartialDtoIsRejected()
        {
            NomadWorkshopSaveData save = CreateValidSave();
            save.Vehicle.Journey = new NomadJourneySaveData();
            Assert.DoesNotThrow(() => NomadWorkshopSaveContract.ValidateForSave(save));
            save.Vehicle.Journey.FuelPicoliters = 1;
            Assert.Throws<ArgumentException>(() => NomadWorkshopSaveContract.ValidateForSave(save));
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
        public void PrepareAfterLoad_MigratesVersionTwoWellbeingDefaults()
        {
            NomadWorkshopSaveData versionTwo = CreateValidSave();
            versionTwo.Version = 2;
            versionTwo.Residents[0].EntertainmentPermille = 0;
            versionTwo.Residents[0].MoodPermille = 0;

            NomadWorkshopSaveData migrated =
                NomadWorkshopSaveContract.PrepareAfterLoad(versionTwo);

            Assert.AreSame(versionTwo, migrated);
            Assert.AreEqual(NomadWorkshopSaveSchema.CurrentVersion, migrated.Version);
            Assert.AreEqual(
                NomadWorkshopSaveSchema.DefaultEntertainmentPermille,
                migrated.Residents[0].EntertainmentPermille,
                "v2 没有该字段，不能把反序列化器给出的 0 当成真实的极度无聊状态。");
            Assert.AreEqual(
                NomadWorkshopSaveSchema.DefaultMoodPermille,
                migrated.Residents[0].MoodPermille,
                "v2 没有该字段，迁移后应采用首版身心系统的中性初值。");
            Assert.AreEqual(
                NomadWorkshopSaveSchema.DefaultHealthPermille,
                migrated.Residents[0].HealthPermille,
                "v2 应逐级经过 v3→v4，并取得旧版本不会死亡的健康默认值。");
        }

        [Test]
        public void PrepareAfterLoad_MigratesVersionThreeHealthDefault()
        {
            NomadWorkshopSaveData versionThree = CreateValidSave();
            versionThree.Version = 3;
            versionThree.Residents[0].HealthPermille = 0;

            NomadWorkshopSaveData migrated =
                NomadWorkshopSaveContract.PrepareAfterLoad(versionThree);

            Assert.That(migrated.Version, Is.EqualTo(NomadWorkshopSaveSchema.CurrentVersion));
            Assert.That(
                migrated.Residents[0].HealthPermille,
                Is.EqualTo(NomadWorkshopSaveSchema.DefaultHealthPermille),
                "v3 的缺省 0 表示字段不存在，不能迁移成居民已经死亡。 ");
        }

        [Test]
        public void PrepareAfterLoad_NormalizesOnlySerializerGeneratedEmptyAction()
        {
            NomadWorkshopSaveData serializedEmpty = CreateValidSave();
            serializedEmpty.Residents[0].ActiveAction = new NomadResidentActionSaveData();

            NomadWorkshopSaveContract.PrepareAfterLoad(serializedEmpty);

            Assert.That(serializedEmpty.Residents[0].ActiveAction, Is.Null);

            NomadWorkshopSaveData contradictory = CreateValidSave();
            contradictory.Residents[0].ActiveAction = new NomadResidentActionSaveData
            {
                TaskId = "unexpected-payload",
                Stage = NomadResidentActionSaveStage.None,
            };
            Assert.Throws<InvalidOperationException>(
                () => NomadWorkshopSaveContract.PrepareAfterLoad(contradictory),
                "带有效载荷的 None 不是序列化器空壳，不能静默丢弃。 ");
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
        public void ValidateForSave_WorldItemRequiresExistingOwnerCanonicalRegionAndYaw()
        {
            NomadWorkshopSaveData coherent = CreateValidSave();
            coherent.WorldItems.Add(CreatePlacedCup());
            Assert.DoesNotThrow(() => NomadWorkshopSaveContract.ValidateForSave(coherent));

            NomadWorkshopSaveData missingOwner = CreateValidSave();
            NomadWorldItemSaveData orphan = CreatePlacedCup();
            orphan.OwnerEntityId = "facility-missing";
            orphan.PlacementRegionId = "facility-missing/placement/countertop-center";
            missingOwner.WorldItems.Add(orphan);
            Assert.Throws<InvalidOperationException>(
                () => NomadWorkshopSaveContract.ValidateForSave(missingOwner));

            NomadWorkshopSaveData foreignRegion = CreateValidSave();
            NomadWorldItemSaveData misplaced = CreatePlacedCup();
            misplaced.PlacementRegionId = "another-owner/placement/countertop-center";
            foreignRegion.WorldItems.Add(misplaced);
            Assert.Throws<InvalidOperationException>(
                () => NomadWorkshopSaveContract.ValidateForSave(foreignRegion));

            NomadWorkshopSaveData invalidYaw = CreateValidSave();
            NomadWorldItemSaveData unnormalized = CreatePlacedCup();
            unnormalized.PlacementLocalPose.LocalYawDeciDegrees = 3600;
            invalidYaw.WorldItems.Add(unnormalized);
            Assert.Throws<InvalidOperationException>(
                () => NomadWorkshopSaveContract.ValidateForSave(invalidYaw));
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

            NomadWorkshopSaveData entertainment = CreateValidSave();
            entertainment.Residents[0].EntertainmentPermille = 1001;
            Assert.Throws<InvalidOperationException>(
                () => NomadWorkshopSaveContract.ValidateForSave(entertainment));

            NomadWorkshopSaveData mood = CreateValidSave();
            mood.Residents[0].MoodPermille = -1;
            Assert.Throws<InvalidOperationException>(
                () => NomadWorkshopSaveContract.ValidateForSave(mood));

            NomadWorkshopSaveData health = CreateValidSave();
            health.Residents[0].HealthPermille = 1001;
            Assert.Throws<InvalidOperationException>(
                () => NomadWorkshopSaveContract.ValidateForSave(health));

            NomadWorkshopSaveData metabolismRemainder = CreateValidSave();
            metabolismRemainder.Residents[0].WaterMetabolismPendingNanoliters = -1L;
            Assert.Throws<InvalidOperationException>(
                () => NomadWorkshopSaveContract.ValidateForSave(metabolismRemainder));

            NomadWorkshopSaveData metabolismSequence = CreateValidSave();
            metabolismSequence.Residents[0].WaterMetabolismSequence = -1;
            Assert.Throws<InvalidOperationException>(
                () => NomadWorkshopSaveContract.ValidateForSave(metabolismSequence));
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
        public void ValidateForSave_AcceptsCompleteFacilityRiskCheckpointAndRejectsPartialOne()
        {
            NomadWorkshopSaveData complete = CreateValidSave();
            NomadFacilitySaveData facility = complete.Facilities[0];
            facility.WearConditionUnits = 190L *
                                          FacilityConditionCycle.ConditionUnitsPerPermille;
            facility.MaintenanceDebtConditionUnits = 340L *
                                                     FacilityConditionCycle.ConditionUnitsPerPermille;
            facility.DustConditionUnits = 230L *
                                          FacilityConditionCycle.ConditionUnitsPerPermille;
            facility.FailureThresholdMicroHazard = 1_500_000L;
            facility.AccumulatedFailureMicroHazard = 620_000L;
            facility.FailureHazardSubMicroRemainder = 42L;
            facility.FailureCycleSequence = 3L;
            facility.ConditionLastSettledSimulationTick = complete.SimulationTick;

            Assert.DoesNotThrow(() => NomadWorkshopSaveContract.ValidateForSave(complete));

            NomadWorkshopSaveData partial = CreateValidSave();
            partial.Facilities[0].WearConditionUnits = 1L;
            Assert.Throws<InvalidOperationException>(
                () => NomadWorkshopSaveContract.ValidateForSave(partial),
                "阈值为零而精确状态非零时，不能静默退化成旧版耐久投影。");

            NomadWorkshopSaveData incoherentFault = CreateValidSave();
            incoherentFault.Facilities[0].FailureThresholdMicroHazard = 1_000L;
            incoherentFault.Facilities[0].AccumulatedFailureMicroHazard = 999L;
            incoherentFault.Facilities[0].ActiveFault =
                FacilityFaultKind.OutletValveJammed;
            incoherentFault.Facilities[0].FaultSeverityPermille = 600;
            incoherentFault.Facilities[0].ConditionLastSettledSimulationTick =
                incoherentFault.SimulationTick;
            Assert.Throws<InvalidOperationException>(
                () => NomadWorkshopSaveContract.ValidateForSave(incoherentFault),
                "具体故障只能在累计风险达到本轮阈值时存在。");
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
                HealthPermille = 840,
                FatiguePermille = 280,
                StressPermille = 190,
                EntertainmentPermille = 640,
                MoodPermille = 710,
                BodyHygieneDeficitPermille = 240,
                HandContaminationPermille = 430,
                MotionSicknessPermille = 170,
                WaterMetabolismPendingNanoliters = 375_000L,
                WaterMetabolismSequence = 9,
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

        private static NomadWorldItemSaveData CreatePlacedCup() => new()
        {
            ItemId = "cup-01",
            DefinitionId = "drinking-cup",
            OwnerEntityId = "facility-cabinet-01",
            PlacementRegionId = "facility-cabinet-01/placement/countertop-center",
            PlacementLocalPose = new QuantizedPlacementPose(20, -10, 370),
        };
    }
}
