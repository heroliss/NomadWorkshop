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
    /// <summary>在实际变体场景复用同一组动作/库存/恢复断言，并核对新接口与机构方向确实生效。</summary>
    public sealed class NomadFacilityBindingPlayModeTests : NomadResidentCrewPlayModeTests
    {
        protected override string ScenePath => "Assets/Game/NomadWorkshop/Scenes/FacilityBindingSample.unity";

        [UnityTest]
        public IEnumerator RealWork_UsesMovedPortsAndReverseValve_WithoutChangingCarrierOwnership()
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            var context = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<NomadFoundationContext>()).Single();
            var read = context.ExecuteCommand(new GetFoundationReadModelCommand());
            var rigs = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<FoundationFacilityArtRig>()).ToArray();
            var tank = rigs.Single(r => r.ServiceDoor != null);
            var fill = tank.Bindings.Single(b => b.WaterFlow == FoundationFacilityWaterFlow.FillCan);
            var motion = fill.Motions.Single();
            var closed = motion.Target.localRotation;
            Assert.That(fill.Contacts.Count, Is.EqualTo(2), "变体包含可选点，不能仍按固定总数要求旧模型。");
            Assert.That(motion.Amount, Is.EqualTo(-95f));
            context.ExecuteCommand(new SetFoundationSpeedCommand(1f));
            context.ExecuteCommand(new SetFoundationPausedCommand(false));
            yield return WaitFor(FoundationResidentPhase.PickingUpWater);
            yield return null;
            Assert.That(Quaternion.Angle(motion.Target.localRotation,
                closed * Quaternion.AngleAxis(-95f, motion.LocalAxis)), Is.LessThan(.02f));
            Vector3 source = fill.Contacts.Single(p => p.Id == fill.WaterEndpointId).Target.position;
            Assert.That(Vector3.Distance(tank.Hose.GetPosition(0), source), Is.LessThan(.001f));
            Assert.That(Vector3.Distance(tank.transform.InverseTransformPoint(source), new Vector3(-.34f, 1.17f, -.58f)), Is.LessThan(.002f));
            context.ExecuteCommand(new SetFoundationPausedCommand(false));
            yield return WaitFor(FoundationResidentPhase.DeliveringWater);
            yield return null;
            FoundationResidentReadModel worker = read.Residents.Single(r => r.FacilityWork.CurrentValue.Active &&
                r.FacilityWork.CurrentValue.Phase == FoundationResidentPhase.DeliveringWater);
            var receiver = rigs.Single(r => r.IsWorking && r.ServiceDoor == null);
            Assert.That(receiver.TryGetWaterInlet(worker.FacilityWork.CurrentValue, out Transform inlet), Is.True);
            Assert.That(Vector3.Distance(receiver.Water.GetPosition(receiver.Water.positionCount - 1), inlet.position), Is.LessThan(.001f));
            Assert.That(Vector3.Distance(receiver.transform.InverseTransformPoint(inlet.position), new Vector3(.11f, .87f, .04f)), Is.LessThan(.002f));
            Assert.That(read.WaterCanCarrierId.CurrentValue, Is.EqualTo(worker.StableId));
            Assert.That(read.WaterCanWaterMilliliters.CurrentValue, Is.GreaterThan(0), "表现中段不提前结算资源。");
            LogAssert.NoUnexpectedReceived();

            IEnumerator WaitFor(FoundationResidentPhase phase)
            {
                float deadline = Time.realtimeSinceStartup + 25f;
                while (Time.realtimeSinceStartup < deadline)
                {
                    yield return new WaitForFixedUpdate();
                    if (!read.Residents.Any(r => r.FacilityWork.CurrentValue.Active && r.FacilityWork.CurrentValue.Phase == phase &&
                        r.FacilityWork.CurrentValue.Progress is > .4f and < .7f)) continue;
                    context.ExecuteCommand(new SetFoundationPausedCommand(true));
                    yield break;
                }
                Assert.Fail("真实变体设施工作未到达：" + phase);
            }
        }
    }
}
#endif
