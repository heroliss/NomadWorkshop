using System.Collections;
using Game.NomadWorkshop.Foundation;
using Game.NomadWorkshop.Simulation;
using Game.NomadWorkshop.Simulation.Persistence;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Game.NomadWorkshop.PlayMode.Tests
{
    public sealed partial class NomadFoundationVerticalSlicePlayModeTests
    {
        [UnityTest]
        public IEnumerator ConstructionBlueprintCheckpoint_RoundTripsThroughFoundationReadModel()
        {
            yield return null;

            NomadWorkshopSaveData checkpoint = _context.ExecuteCommand(
                new CaptureFoundationCheckpointCommand());
            checkpoint.Blueprints.Add(new NomadBlueprintSaveData
            {
                InstanceId = "blueprint-kitchen-n2b",
                DefinitionId = "field-kitchen",
                Pose = QuantizedDeckPose.FromMeters(0f, -2f, 0f),
                Stage = NomadBlueprintSaveStage.AwaitingMaterials,
                SiteKind = NomadConstructionSiteKind.VehicleMounted,
                SiteId = "vehicle-01",
                RequiredWorkUnits = 12,
                CompletedWorkUnits = 0,
                RequiredMaterials =
                {
                    new NomadResourceStackSaveData
                    {
                        StackId = "blueprint-kitchen-n2b/required/00",
                        ResourceId = "steel",
                        Measure = ResourceMeasure.Item,
                        AmountBaseUnits = 2,
                        ConditionPermille = 1000,
                    },
                },
                StagedMaterials =
                {
                    new NomadResourceStackSaveData
                    {
                        StackId = "blueprint-kitchen-n2b/staged/00",
                        ResourceId = "steel",
                        Measure = ResourceMeasure.Item,
                        AmountBaseUnits = 1,
                        ConditionPermille = 1000,
                    },
                },
            });

            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(checkpoint));
            yield return null;

            FoundationReadModel read = _context.ExecuteCommand(
                new GetFoundationReadModelCommand());
            Assert.That(read.ConstructionBlueprints.Count, Is.EqualTo(1));
            FoundationConstructionBlueprintState blueprint = read.ConstructionBlueprints[0];
            Assert.That(blueprint.InstanceId, Is.EqualTo("blueprint-kitchen-n2b"));
            Assert.That(blueprint.Stage, Is.EqualTo(NomadConstructionStage.AwaitingMaterials));
            Assert.That(blueprint.StagedMaterialItems, Is.EqualTo(1));
            Assert.That(blueprint.RequiredMaterialItems, Is.EqualTo(2));

            NomadWorkshopSaveData roundTrip = _context.ExecuteCommand(
                new CaptureFoundationCheckpointCommand());
            Assert.That(roundTrip.Blueprints.Count, Is.EqualTo(1));
            Assert.That(roundTrip.Blueprints[0].SiteId, Is.EqualTo("vehicle-01"));
            Assert.That(roundTrip.Blueprints[0].StagedMaterials[0].AmountBaseUnits, Is.EqualTo(1));
            Assert.That(roundTrip.Blueprints[0].RequiredWorkUnits, Is.EqualTo(12));
        }
    }
}
