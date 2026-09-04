using System;
using System.Collections;
using System.IO;
using Cysharp.Threading.Tasks;
using Game.Framework;
using Game.Framework.Context;
using Game.Framework.Storage;
using Game.Framework.Systems;
using Game.NomadWorkshop.Persistence;
using Game.NomadWorkshop.Simulation;
using Game.NomadWorkshop.Simulation.Persistence;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.NomadWorkshop.PlayMode.Tests
{
    /// <summary>证明游戏存档契约经 SSFramework Command + IStorageUtility 真正原子落盘并可往返。</summary>
    public sealed class NomadWorkshopSaveStoragePlayModeTests
    {
        private string _storageRoot;

        [SetUp]
        public void SetUp()
        {
            _storageRoot = Path.Combine(
                Application.temporaryCachePath,
                "NomadWorkshopSaveTests",
                Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_storageRoot)) Directory.Delete(_storageRoot, recursive: true);
        }

        [UnityTest]
        public IEnumerator SaveAndLoad_UsesFrameworkContextAndPreservesCommittedCheckpoint() =>
            UniTask.ToCoroutine(async () =>
            {
                using var builder = new ContainerBuilder();
                builder.RegisterValue(new LoggingCommandSystem(), typeof(ICommandSystem));
                builder.RegisterOwnedUtility(
                    new StorageUtility(new FileStorageProvider(_storageRoot)));
                using var context = new GameContext(builder.Build(), inheritFromGlobal: false);
                NomadWorkshopSaveData expected = CreateSnapshot();

                await context.ExecuteCommandAsync(
                    new SaveNomadWorkshopProgressCommand("roundtrip", expected));
                NomadWorkshopSaveData actual = await context.ExecuteCommandAsync<
                    LoadNomadWorkshopProgressCommand,
                    NomadWorkshopSaveData>(new LoadNomadWorkshopProgressCommand("roundtrip"));

                Assert.IsNotNull(actual);
                Assert.AreEqual(NomadWorkshopSaveSchema.CurrentVersion, actual.Version);
                Assert.AreEqual(expected.WorldSeed, actual.WorldSeed);
                Assert.AreEqual(expected.SimulationTick, actual.SimulationTick);
                Assert.AreEqual(1, actual.Facilities.Count);
                Assert.AreEqual(expected.Facilities[0].Pose, actual.Facilities[0].Pose);
                Assert.AreEqual(expected.Inventories[0].ContaminationPermille,
                    actual.Inventories[0].ContaminationPermille);
                Assert.AreEqual(ResourceMeasure.Item, actual.Inventories[0].Measure);
                Assert.AreEqual(1, actual.Inventories[0].Contents[0].AmountBaseUnits);
                Assert.AreEqual("right", actual.Residents[0].ActiveAction.InteractionSlotId);
                Assert.IsTrue(actual.Residents[0].ActiveAction.OutcomeCommitted);
                Assert.AreEqual(expected.Residents[0].BodyHygieneDeficitPermille,
                    actual.Residents[0].BodyHygieneDeficitPermille);
                Assert.AreEqual(expected.Residents[0].HandContaminationPermille,
                    actual.Residents[0].HandContaminationPermille);
                Assert.AreEqual(expected.Residents[0].MotionSicknessPermille,
                    actual.Residents[0].MotionSicknessPermille);
                Assert.AreEqual(expected.Residents[0].EntertainmentPermille,
                    actual.Residents[0].EntertainmentPermille);
                Assert.AreEqual(expected.Residents[0].MoodPermille,
                    actual.Residents[0].MoodPermille);
                Assert.AreEqual(expected.Residents[0].WaterMetabolismPendingNanoliters,
                    actual.Residents[0].WaterMetabolismPendingNanoliters);
                Assert.AreEqual(expected.Residents[0].WaterMetabolismSequence,
                    actual.Residents[0].WaterMetabolismSequence);
                Assert.AreEqual(1, actual.RandomStreams.Count);
                Assert.AreEqual("resident-decision", actual.RandomStreams[0].StreamId);
                Assert.AreEqual(18, actual.RandomStreams[0].NextEventSequence);
                Assert.AreEqual(
                    NomadActionRestoreDisposition.ResumePresentationAfterCommittedOutcome,
                    NomadWorkshopSaveContract.GetRestoreDisposition(actual.Residents[0].ActiveAction));
                Assert.IsTrue(
                    context.GetUtility<IStorageUtility>().Exists(
                        NomadWorkshopStorageKeys.ProgressSlot("roundtrip")));
            });

        private static NomadWorkshopSaveData CreateSnapshot()
        {
            var data = new NomadWorkshopSaveData
            {
                WorldSeed = 94217,
                SimulationTick = 123456,
                Vehicle = new NomadVehicleSaveData
                {
                    CurrentRegionId = "salt-marsh",
                    TravelProgressPermille = 420,
                    FuelMilliUnits = 18600,
                    IsTraveling = true,
                },
            };
            data.Facilities.Add(new NomadFacilitySaveData
            {
                InstanceId = "facility-cabinet-01",
                DefinitionId = "parts-cabinet",
                Pose = QuantizedDeckPose.FromMeters(1.25f, -0.75f, 27f),
                DurabilityPermille = 810,
                DirtPermille = 230,
            });
            data.Inventories.Add(new NomadInventorySaveData
            {
                InventoryId = "resident-b-hands",
                OwnerEntityId = "resident-b",
                Measure = ResourceMeasure.Item,
                CapacityBaseUnits = 2,
                ContaminationPermille = 110,
                Contents =
                {
                    new NomadResourceStackSaveData
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
            data.Residents.Add(new NomadResidentSaveData
            {
                ResidentId = "resident-b",
                PersonalInventoryId = "resident-b-hands",
                Pose = QuantizedDeckPose.FromMeters(0.6f, -1.2f, 27f),
                HungerPermille = 350,
                ThirstPermille = 610,
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
            data.RandomStreams.Add(new NomadRandomStreamSaveData
            {
                OwnerEntityId = "resident-b",
                StreamId = "resident-decision",
                NextEventSequence = 18,
            });
            return data;
        }
    }
}
