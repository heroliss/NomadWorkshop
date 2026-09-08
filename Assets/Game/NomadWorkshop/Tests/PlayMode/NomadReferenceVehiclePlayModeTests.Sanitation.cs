#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using Game.NomadWorkshop.Foundation;
using Game.NomadWorkshop.Simulation;
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
        public IEnumerator NewToilet_UsesConfiguredCamera_AndOneRealBucketAcrossPauseRecallRestoreAndDisposal()
        {
            var roots=SceneManager.GetSceneByPath(ScenePath).GetRootGameObjects();
            var context=roots.SelectMany(r=>r.GetComponentsInChildren<NomadFoundationContext>()).Single();
            var view=roots.SelectMany(r=>r.GetComponentsInChildren<NomadFoundationWorldView>()).Single();
            var camera=roots.SelectMany(r=>r.GetComponentsInChildren<Camera>()).Single(c=>c.CompareTag("MainCamera"));
            var deck=view.transform.Find("Vehicle Deck Root");
            Assert.That(Vector3.Distance(deck.InverseTransformPoint(camera.transform.position),new Vector3(9,15,-11)),Is.LessThan(.001f),
                "新机位必须来自落盘配置，不能在进入 Play 后被旧硬编码覆盖。");
            var read=context.ExecuteCommand(new GetFoundationReadModelCommand());
            var checkpoint=context.ExecuteCommand(new CaptureFoundationCheckpointCommand());
            // Start at a valid stop to exercise the actual local carrying path, not a second 2 km journey.
            checkpoint.Vehicle.Journey.PositionMicrometers=2_000_000_000L;
            checkpoint.Vehicle.Journey.Destination=NomadJourneyEndpoint.Destination;
            foreach (var resident in checkpoint.Residents) resident.ThirstPermille=200;
            foreach (var bladder in checkpoint.Inventories.Where(i=>i.InventoryId.EndsWith(":bladder",StringComparison.Ordinal)))
                bladder.Contents.Clear();
            var source=checkpoint.Inventories.Single(i=>i.InventoryId.EndsWith(":toilet-waste",StringComparison.Ordinal));
            source.Contents.Clear();
            source.Contents.Add(new NomadResourceStackSaveData{
                StackId=source.InventoryId+":"+NomadResourceIds.HumanWaste.Value,
                ResourceId=NomadResourceIds.HumanWaste.Value,Measure=NomadResourceIds.HumanWaste.Measure,AmountBaseUnits=900});
            context.ExecuteCommand(new RestoreFoundationCheckpointCommand(checkpoint));
            yield return null; yield return null;
            Assert.That(read.StopAccessOpen.CurrentValue,Is.True);
            Assert.That(view.GetComponentsInChildren<FoundationFacilitySpaceAuthoring>().Count(s=>s.name=="NW14_Toilet"),Is.EqualTo(1));
            Transform Bucket()=>view.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Detachable Waste Bucket");
            var bucket=Bucket(); var installedParent=bucket.parent;
            Vector3 installedPosition=bucket.localPosition;
            Assert.That(read.ToiletHoldingWasteMilliliters.CurrentValue,Is.EqualTo(900));
            Vector3 cameraBefore=camera.transform.position;
            Assert.That(context.ExecuteCommand(new RequestFoundationStopWasteCommand(true)),Is.True);
            context.ExecuteCommand(new SetFoundationSpeedCommand(1f));
            context.ExecuteCommand(new SetFoundationPausedCommand(false));
            float deadline=Time.realtimeSinceStartup+35f;
            while (string.IsNullOrEmpty(read.CarriedWasteBucketFacilityId.CurrentValue) && Time.realtimeSinceStartup<deadline) yield return null;
            Assert.That(read.CarriedWasteBucketFacilityId.CurrentValue,Is.EqualTo(source.OwnerEntityId));
            context.ExecuteCommand(new SetFoundationPausedCommand(true)); yield return null; yield return null;
            Assert.That(Bucket(),Is.SameAs(bucket));
            Assert.That(bucket.parent.GetComponent<ResidentHumanoidPresentation>(),Is.Not.Null);
            Assert.That(installedParent.Find("Detachable Waste Bucket"),Is.Null);
            Assert.That(read.StopWasteMilliliters.CurrentValue,Is.Zero);
            Assert.That(read.CarriedWasteMilliliters.CurrentValue,Is.EqualTo(900));
            Vector3 pausedPosition=bucket.position; Quaternion pausedRotation=bucket.rotation;
            for (int i=0;i<8;i++) yield return null;
            Assert.That(Vector3.Distance(bucket.position,pausedPosition),Is.LessThan(.001f));
            Assert.That(Quaternion.Angle(bucket.rotation,pausedRotation),Is.LessThan(.01f));
            var mid=context.ExecuteCommand(new CaptureFoundationCheckpointCommand());
            Assert.That(Bucket(),Is.SameAs(bucket),"生成检查点不能移动或复制正在携带的桶。");
            Assert.That(context.ExecuteCommand(new RequestFoundationStopWasteCommand(false)),Is.True);
            context.ExecuteCommand(new SetFoundationPausedCommand(false));
            deadline=Time.realtimeSinceStartup+35f;
            while (read.StopWasteActive.CurrentValue && Time.realtimeSinceStartup<deadline) yield return null;
            context.ExecuteCommand(new SetFoundationPausedCommand(true)); yield return null;
            Assert.That(read.StopWasteActive.CurrentValue,Is.False);
            Assert.That(Bucket(),Is.SameAs(bucket)); Assert.That(bucket.parent,Is.SameAs(installedParent));
            Assert.That(Vector3.Distance(bucket.localPosition,installedPosition),Is.LessThan(.001f));
            Assert.That(read.ToiletHoldingWasteMilliliters.CurrentValue,Is.EqualTo(900));
            Assert.That(read.StopWasteMilliliters.CurrentValue,Is.Zero);
            context.ExecuteCommand(new RestoreFoundationCheckpointCommand(mid));
            yield return null; yield return null;
            Assert.That(Vector3.Distance(camera.transform.position,cameraBefore),Is.LessThan(.001f));
            bucket=Bucket(); installedParent=bucket.parent;
            Assert.That(installedParent.GetComponent<ResidentHumanoidPresentation>(),Is.Null);
            Assert.That(read.ToiletHoldingWasteMilliliters.CurrentValue,Is.EqualTo(900));
            Assert.That(read.StopWasteMilliliters.CurrentValue,Is.Zero);
            Assert.That(context.ExecuteCommand(new RequestFoundationStopWasteCommand(true)),Is.True);
            context.ExecuteCommand(new SetFoundationPausedCommand(false));
            deadline=Time.realtimeSinceStartup+55f;
            while ((read.StopWasteMilliliters.CurrentValue==0 || read.StopWasteActive.CurrentValue) && Time.realtimeSinceStartup<deadline) yield return null;
            context.ExecuteCommand(new SetFoundationPausedCommand(true)); yield return null;
            Assert.That(read.StopWasteMilliliters.CurrentValue,Is.EqualTo(900));
            Assert.That(read.StopWasteActive.CurrentValue,Is.False);
            Assert.That(read.ToiletHoldingWasteMilliliters.CurrentValue,Is.Zero);
            Assert.That(Bucket(),Is.SameAs(bucket),"同一次完整出车清运必须归还原实体桶。");
            Assert.That(bucket.parent,Is.SameAs(installedParent));
            Assert.That(Vector3.Distance(bucket.localPosition,installedPosition),Is.LessThan(.001f));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
#endif
