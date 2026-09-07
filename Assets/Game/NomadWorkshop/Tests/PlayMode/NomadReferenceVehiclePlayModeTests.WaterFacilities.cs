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
    public sealed partial class NomadReferenceVehiclePlayModeTests
    {
        [UnityTest]
        public IEnumerator WaterPour_ReachesNearbyOpeningWithForwardFacingMouth()
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            var context = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<NomadFoundationContext>()).Single();
            var read = context.ExecuteCommand(new GetFoundationReadModelCommand());
            context.ExecuteCommand(new SetFoundationSpeedCommand(1f));
            context.ExecuteCommand(new SetFoundationPausedCommand(false));
            float deadline = Time.realtimeSinceStartup + 25f;
            bool reached = false;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForFixedUpdate();
                if (!read.Residents.Any(r => r.FacilityWork.CurrentValue.Active &&
                    r.FacilityWork.CurrentValue.Phase == FoundationResidentPhase.DeliveringWater &&
                    r.FacilityWork.CurrentValue.Progress is > .35f and < .65f)) continue;
                context.ExecuteCommand(new SetFoundationPausedCommand(true));
                yield return null; yield return null;
                var rig = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<FoundationFacilityArtRig>())
                    .Single(r => r.Water.enabled && !r.Hose.enabled);
                Vector3 from = rig.Water.GetPosition(0), to = rig.Water.GetPosition(rig.Water.positionCount - 1);
                Vector3 delta = to - from;
                Assert.That(new Vector2(delta.x, delta.z).magnitude, Is.LessThan(.5f),
                    "入口和站位必须让倒水发生在近处，不能用接近一米的斜线连接不可达入口。");
                Assert.That(-delta.y, Is.GreaterThan(.1f));
                Transform can = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>())
                    .Single(t => t.name == "Water Can 01 [physical carrier]");
                Assert.That(Vector3.Dot(can.up, delta.normalized), Is.GreaterThan(.25f),
                    "水流必须从倾斜罐口朝向的一侧流出，不能把罐口翻到身后。");
                reached = true; break;
            }
            Assert.That(reached, Is.True, "必须观察到真实倒水，不能只检查静态模型。");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator WaterFacilityReplacement_RestoresActualTrayContact_AndRejectsPreviousSpace()
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            var roots = scene.GetRootGameObjects();
            var context = roots.SelectMany(r => r.GetComponentsInChildren<NomadFoundationContext>()).Single();
            var view = roots.SelectMany(r => r.GetComponentsInChildren<NomadFoundationWorldView>()).Single();
            var saved = context.ExecuteCommand(new CaptureFoundationCheckpointCommand());
            Assert.That(saved.Facilities.Where(f => f.DefinitionId is "vehicle-water-tank" or "drinking-station")
                .All(f => f.SpaceSignature.Length == 64), Is.True);
            context.ExecuteCommand(new RestoreFoundationCheckpointCommand(saved));
            yield return null; yield return null;
            var spaces = view.GetComponentsInChildren<FoundationFacilitySpaceAuthoring>();
            Assert.That(spaces.Select(s => s.name).OrderBy(n => n),
                Is.EqualTo(new[] { "NW8_Dispenser", "NW8_WaterTank" }));
            var source = spaces.Single(s => s.PlacementRegions.Any(r => r.id == "maintenance-tray"));
            var tray = source.PlacementRegions.Single(r => r.id == "maintenance-tray");
            var kit = context.ExecuteCommand(new GetFoundationWorldItemPlacementsCommand())
                .Single(item => item.RegionId != null && item.RegionId.EndsWith("/placement/maintenance-tray", StringComparison.Ordinal));
            Assert.That(kit.SupportHeightMillimeters, Is.EqualTo(1280));
            Transform item = view.GetComponentsInChildren<Transform>()
                .Single(t => t.name.EndsWith("[" + kit.ItemId + "]", StringComparison.Ordinal));
            Assert.That(Vector3.Distance(item.position, tray.frame.position), Is.LessThan(.0005f));
            Assert.That(item.GetComponentsInChildren<Renderer>().Min(r => r.bounds.min.y),
                Is.EqualTo(tray.frame.position.y).Within(.0005f), "物品根与实际网格底部都须落在托盘上。");
            string before = JsonUtility.ToJson(context.ExecuteCommand(new CaptureFoundationCheckpointCommand()));
            foreach (string oldSignature in new[] { "", "7dbb89a08cb5dd194727141d420f642053bd1bf23b7214a825f6541cd24c15df" })
            {
                var old = JsonUtility.FromJson<NomadWorkshopSaveData>(before);
                old.Facilities.Single(f => f.DefinitionId == "vehicle-water-tank").SpaceSignature = oldSignature;
                Assert.Throws<NotSupportedException>(() => context.ExecuteCommand(new RestoreFoundationCheckpointCommand(old)));
                Assert.That(JsonUtility.ToJson(context.ExecuteCommand(new CaptureFoundationCheckpointCommand())), Is.EqualTo(before));
            }
            LogAssert.NoUnexpectedReceived();
        }
    }
}
#endif
