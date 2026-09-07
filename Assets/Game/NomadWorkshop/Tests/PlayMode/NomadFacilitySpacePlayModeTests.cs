#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using Game.NomadWorkshop.Foundation;
using Game.NomadWorkshop.Simulation.Persistence;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.NomadWorkshop.PlayMode.Tests
{
    /// <summary>在真实空间变体中复用居民工作、机械反馈与旅程检查，并验证变化后的区域和存档身份。</summary>
    public sealed class NomadFacilitySpacePlayModeTests : NomadResidentCrewPlayModeTests
    {
        protected override string ScenePath => "Assets/Game/NomadWorkshop/Scenes/FacilitySpaceSample.unity";

        [UnityTest]
        public IEnumerator GeneratedSpace_RestoresRaisedItemAndSignatures_RejectsForeignAndLegacySavesAtomically()
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            var context = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<NomadFoundationContext>()).Single();
            var snapshot = context.ExecuteCommand(new CaptureFoundationCheckpointCommand());
            var water = snapshot.Facilities.Where(f => f.DefinitionId is "vehicle-water-tank" or "drinking-station").ToArray();
            Assert.That(water.Length, Is.EqualTo(2));
            Assert.That(water.All(f => f.SpaceSignature.Length == 64), Is.True);
            string json = JsonUtility.ToJson(snapshot);
            var loaded = JsonUtility.FromJson<NomadWorkshopSaveData>(json);
            context.ExecuteCommand(new RestoreFoundationCheckpointCommand(loaded));
            yield return null; yield return null;
            var kit = context.ExecuteCommand(new GetFoundationWorldItemPlacementsCommand())
                .Single(item => item.RegionId != null && item.RegionId.EndsWith("/placement/maintenance-tray", StringComparison.Ordinal));
            Assert.That(kit.SupportHeightMillimeters, Is.EqualTo(1520));
            var authoring = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<FoundationFacilitySpaceAuthoring>())
                .Single(a => a.PlacementRegions.Any(r => r.id == "maintenance-tray"));
            Transform surface = authoring.PlacementRegions.Single(r => r.id == "maintenance-tray").frame;
            var view = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<NomadFoundationWorldView>()).Single();
            Transform deck = view.transform.Find("Vehicle Deck Root");
            Vector3 expectedSupport = deck.TransformPoint(new Vector3((float)kit.WorldPose.XMeters,
                kit.SupportHeightMillimeters / 1000f, (float)kit.WorldPose.ZMeters));
            Assert.That(Vector3.Distance(surface.position, expectedSupport), Is.LessThan(.0005f),
                "实际模型的支撑标记必须与账本落点相同，不能把整个设施额外抬高。");
            var plate = surface.GetComponentsInChildren<Renderer>().Single(r => r.name == "托盘底板");
            Assert.That(plate.bounds.max.y, Is.EqualTo(expectedSupport.y).Within(.0005f));
            string before = JsonUtility.ToJson(context.ExecuteCommand(new CaptureFoundationCheckpointCommand()));
            foreach (string badSignature in new[] { new string('a', 64), string.Empty })
            {
                var invalid = JsonUtility.FromJson<NomadWorkshopSaveData>(before);
                invalid.Facilities.Single(f => f.DefinitionId == "vehicle-water-tank").SpaceSignature = badSignature;
                Assert.Throws<NotSupportedException>(() => context.ExecuteCommand(new RestoreFoundationCheckpointCommand(invalid)));
                Assert.That(JsonUtility.ToJson(context.ExecuteCommand(new CaptureFoundationCheckpointCommand())), Is.EqualTo(before));
            }
            LogAssert.NoUnexpectedReceived();
        }
    }
}
#endif
