#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using Game.NomadWorkshop.Foundation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.NomadWorkshop.PlayMode.Tests
{
    public sealed partial class NomadReferenceVehiclePlayModeTests
    {
        [UnityTest]
        public IEnumerator KitchenCup_UsesRealCountertop_AndPreservesPickupPauseAndCheckpointRollback()
        {
            var roots=SceneManager.GetSceneByPath(ScenePath).GetRootGameObjects();
            var context=roots.SelectMany(r=>r.GetComponentsInChildren<NomadFoundationContext>()).Single();
            var view=roots.SelectMany(r=>r.GetComponentsInChildren<NomadFoundationWorldView>()).Single();
            var read=context.ExecuteCommand(new GetFoundationReadModelCommand());
            var before=context.ExecuteCommand(new CaptureFoundationCheckpointCommand());
            var old=JsonUtility.FromJson<Game.NomadWorkshop.Simulation.Persistence.NomadWorkshopSaveData>(JsonUtility.ToJson(before));
            var oldKitchen=old.Facilities.Single(f=>f.DefinitionId=="field-kitchen");
            oldKitchen.SpaceSignature="";
            oldKitchen.Pose.YawDeciDegrees=0;
            // 模型保持原空间，未带空间签名的厨房检查点仍须接受。
            context.ExecuteCommand(new RestoreFoundationCheckpointCommand(old));
            yield return null; yield return null;
            AssertCupOnCountertop(view);
            var restoredKitchen=view.GetComponentsInChildren<FoundationFacilitySpaceAuthoring>().Single(s=>s.name=="NW9_Kitchen");
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(restoredKitchen.transform.localEulerAngles.y,0)),Is.LessThan(.01f),
                "旧检查点恢复自己的朝向，不能被新的起步朝向覆盖。");
            context.ExecuteCommand(new RestoreFoundationCheckpointCommand(before));
            yield return null; yield return null;
            AssertCupOnCountertop(view);
            var resident=read.Residents.Single(r=>r.StableId=="resident-02");
            int count=resident.CompletedWorldItemMoveCount.CurrentValue;
            Assert.That(context.ExecuteCommand(new TryStartFoundationCupMoveCommand(resident.StableId)),Is.True);
            context.ExecuteCommand(new SetFoundationSpeedCommand(1f));
            context.ExecuteCommand(new SetFoundationPausedCommand(false));
            float deadline=Time.realtimeSinceStartup+25f;
            while (!resident.ResidentCarriedWorldItem.CurrentValue.Active && Time.realtimeSinceStartup<deadline) yield return null;
            Assert.That(resident.ResidentCarriedWorldItem.CurrentValue.Active,Is.True,"须观察到真实拿起杯具。");
            context.ExecuteCommand(new SetFoundationPausedCommand(true)); yield return null; yield return null;
            var carried=view.GetComponentsInChildren<Transform>().Single(t=>t.name=="搪瓷杯 [cup-01] (carried)");
            Vector3 pause=carried.position;
            for (int i=0;i<6;i++) yield return null;
            Assert.That(Vector3.Distance(carried.position,pause),Is.LessThan(.001f));
            Assert.That(context.ExecuteCommand(new GetFoundationWorldItemPlacementsCommand()).Any(i=>i.ItemId=="cup-01"),Is.False);
            var middle=context.ExecuteCommand(new CaptureFoundationCheckpointCommand());
            Assert.That(middle.WorldItems.Single(i=>i.ItemId=="cup-01").OwnerEntityId,
                Is.EqualTo(before.WorldItems.Single(i=>i.ItemId=="cup-01").OwnerEntityId));
            context.ExecuteCommand(new RestoreFoundationCheckpointCommand(middle));
            yield return null; yield return null;
            AssertCupOnCountertop(view);
            Assert.That(view.GetComponentsInChildren<Transform>().Any(t=>t.name=="搪瓷杯 [cup-01] (carried)"),Is.False);
            Assert.That(context.ExecuteCommand(new TryStartFoundationCupMoveCommand("resident-02")),Is.True);
            context.ExecuteCommand(new SetFoundationPausedCommand(false));
            resident=read.Residents.Single(r=>r.StableId=="resident-02");
            deadline=Time.realtimeSinceStartup+25f;
            while (resident.CompletedWorldItemMoveCount.CurrentValue<=count && Time.realtimeSinceStartup<deadline) yield return null;
            Assert.That(resident.CompletedWorldItemMoveCount.CurrentValue,Is.EqualTo(count+1));
            context.ExecuteCommand(new SetFoundationPausedCommand(true)); yield return null;
            AssertCupOnCountertop(view);
            LogAssert.NoUnexpectedReceived();
        }

        private static void AssertCupOnCountertop(NomadFoundationWorldView view)
        {
            var kitchen=view.GetComponentsInChildren<FoundationFacilitySpaceAuthoring>().Single(s=>s.name=="NW9_Kitchen");
            var frame=kitchen.PlacementRegions.Single(r=>r.id=="countertop-center").frame;
            var cup=view.GetComponentsInChildren<Transform>().Single(t=>t.name=="搪瓷杯 [cup-01]");
            var local=frame.InverseTransformPoint(cup.position);
            Assert.That(Mathf.Abs(local.y),Is.LessThan(.0005f));
            Assert.That(Mathf.Abs(local.x),Is.LessThan(.21f));
            Assert.That(Mathf.Abs(local.z),Is.LessThan(.21f));
            Assert.That(cup.GetComponentsInChildren<Renderer>().Min(r=>r.bounds.min.y),Is.EqualTo(frame.position.y).Within(.0005f));
        }
    }
}
#endif
