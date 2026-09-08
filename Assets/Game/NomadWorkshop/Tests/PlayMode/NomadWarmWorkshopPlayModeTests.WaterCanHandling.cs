#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Game.NomadWorkshop.Foundation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.NomadWorkshop.PlayMode.Tests
{
    public partial class NomadWarmWorkshopPlayModeTests
    {
        [UnityTest]
        public IEnumerator WaterCanGroundContact_RestoreDuringLowering_ClearsOldBodyFootAndHandConstraints()
        {
            yield return WaitForFacilityWork(FoundationResidentPhase.PlacingWaterCan);
            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            yield return null;
            yield return null;
            int identity = FindCan().GetInstanceID();
            int delivered = _read.DrinkingStationWaterMilliliters.CurrentValue;
            Assert.That(Deck.GetComponentsInChildren<FoundationResidentCarryIK>().Any(ik => ik.GroundFootContactWeight > .9f),
                Is.True, "恢复前必须实际处于支撑脚已约束的下放动作。");
            var checkpoint = _context.ExecuteCommand(new CaptureFoundationCheckpointCommand());
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(checkpoint));
            for (int i = 0; i < 3; i++) yield return null;
            Assert.That(FindCan().GetInstanceID(), Is.EqualTo(identity));
            Assert.That(FindCan().parent, Is.EqualTo(Deck));
            Assert.That(_read.Residents.All(r => !r.FacilityWork.CurrentValue.Active), Is.True);
            Assert.That(_read.DrinkingStationWaterMilliliters.CurrentValue, Is.EqualTo(delivered));
            foreach (var ik in Deck.GetComponentsInChildren<FoundationResidentCarryIK>())
            {
                Assert.That(ik.GroundFootContactWeight, Is.Zero, "旧落点不能继续约束恢复后的双脚。");
                Assert.That(ik.RightHandContactWeight, Is.Zero, "旧居民不能继续抓住已经规范化到地面的水罐。");
            }
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator WaterCanGroundContact_RealTimePickupLiftLowerRelease_KeepPalmFeetAndSingleItem()
        {
            _context.ExecuteCommand(new SetFoundationSpeedCommand(1f));
            _context.ExecuteCommand(new SetFoundationPausedCommand(false));
            int[] samples = new int[4];
            int identity = FindCan().GetInstanceID();
            float deadline = Time.realtimeSinceStartup + 40f;
            bool pausedChecked = false;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForFixedUpdate();
                FoundationResidentReadModel worker = _read.Residents.FirstOrDefault(r =>
                    r.FacilityWork.CurrentValue.Active && r.FacilityWork.CurrentValue.ItemContactPlacement.Active);
                if (string.IsNullOrEmpty(worker.StableId))
                {
                    if (samples[3] > 1) break;
                    continue;
                }
                FoundationFacilityWorkState work = worker.FacilityWork.CurrentValue;
                int stage = work.Phase switch
                {
                    FoundationResidentPhase.PickingUpWaterCan => 0,
                    FoundationResidentPhase.LiftingWaterCan => 1,
                    FoundationResidentPhase.PlacingWaterCan => 2,
                    FoundationResidentPhase.ReleasingWaterCan => 3,
                    _ => -1,
                };
                Assert.That(stage, Is.GreaterThanOrEqualTo(0));
                samples[stage]++;
                Transform can = FindCan();
                Assert.That(can.GetInstanceID(), Is.EqualTo(identity));
                var human = Deck.Find("Resident " + worker.StableId.Substring(worker.StableId.LastIndexOf('-') + 1))
                    .GetComponent<ResidentHumanoidPresentation>();
                var ik = human.Animator.GetComponent<FoundationResidentCarryIK>();
                Transform target = can.GetComponent<FoundationCarriedContainerRig>().RightPalm;
                string info = $"{work.Phase} p={work.Progress:F4}, reach={ik.RightArmReachRatio:F4}, " +
                    $"shoulder={human.transform.InverseTransformPoint(human.Animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position):F4}, " +
                    $"solveShoulder={human.transform.InverseTransformPoint(ik.RightShoulderAtSolve):F4}";
                if (ik.RightHandContactWeight > .999f)
                {
                    Assert.That(Vector3.Distance(ik.RightPalmContactPosition, target.position), Is.LessThan(.015f), info);
                    Assert.That(Quaternion.Angle(ik.RightPalmRotation, target.rotation), Is.LessThan(4f), info);
                    var body = can.GetComponent<FoundationCarriedContainerRig>();
                    Vector3 shoulder = human.Animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position;
                    Vector3 elbow = human.Animator.GetBoneTransform(HumanBodyBones.RightLowerArm).position;
                    Vector3 wrist = human.Animator.GetBoneTransform(HumanBodyBones.RightHand).position;
                    AssertBoneSegmentOutsideCan(shoulder, elbow, body, info);
                    AssertBoneSegmentOutsideCan(elbow, wrist, body, info);
                }
                if (ik.GroundFootContactWeight > .999f)
                {
                    if (ik.RightHandContactWeight > .999f)
                        Assert.That(ik.RightArmReachRatio, Is.LessThan(1f), "固定双脚时应靠蹲姿够取，不能拉伸手臂：" + info);
                    Assert.That(Vector3.Distance(human.Animator.GetBoneTransform(HumanBodyBones.LeftFoot).position,
                        ik.LeftGroundFootTarget), Is.LessThan(.045f), "左脚支撑失效：" + info);
                    Assert.That(Vector3.Distance(human.Animator.GetBoneTransform(HumanBodyBones.RightFoot).position,
                        ik.RightGroundFootTarget), Is.LessThan(.045f), "右脚支撑失效：" + info);
                    var body = can.GetComponent<FoundationCarriedContainerRig>();
                    foreach (bool left in new[] { true, false })
                    {
                        Vector3 hip = human.Animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg).position;
                        Vector3 knee = human.Animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg).position;
                        Vector3 foot = human.Animator.GetBoneTransform(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot).position;
                        AssertBoneSegmentOutsideCan(hip, knee, body, (left ? "左腿 " : "右腿 ") + info, .06f);
                        AssertBoneSegmentOutsideCan(knee, foot, body, (left ? "左腿 " : "右腿 ") + info, .06f);
                    }
                }
                Assert.That(_read.WaterCanLocation.CurrentValue == FoundationWaterCanLocation.Resident,
                    Is.EqualTo(stage is 1 or 2), "表现接触阶段与物品所有权必须一致。" + info);
                if (!pausedChecked && stage == 0 && work.Progress > .8f)
                {
                    _context.ExecuteCommand(new SetFoundationPausedCommand(true));
                    yield return null;
                    yield return null;
                    Vector3 palm = ik.RightPalmContactPosition;
                    Vector3 hips = human.Animator.GetBoneTransform(HumanBodyBones.Hips).position;
                    Vector3 canPosition = can.position;
                    long tick = _read.SimulationTick.CurrentValue;
                    for (int i = 0; i < 6; i++) yield return null;
                    Assert.That(_read.SimulationTick.CurrentValue, Is.EqualTo(tick));
                    Assert.That(Vector3.Distance(ik.RightPalmContactPosition, palm), Is.LessThan(.00001f));
                    Assert.That(Vector3.Distance(human.Animator.GetBoneTransform(HumanBodyBones.Hips).position, hips), Is.LessThan(.00001f));
                    Assert.That(can.position, Is.EqualTo(canPosition));
                    pausedChecked = true;
                    _context.ExecuteCommand(new SetFoundationPausedCommand(false));
                }
            }
            Assert.That(pausedChecked, Is.True, "需要实际采到蹲身握罐后的暂停。");
            Assert.That(samples.All(count => count > 1), Is.True,
                "需要连续采到接触、抬起、下放和松手四阶段：" + string.Join(",", samples));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
#endif
