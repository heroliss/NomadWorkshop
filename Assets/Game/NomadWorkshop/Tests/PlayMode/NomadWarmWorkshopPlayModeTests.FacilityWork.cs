#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using Game.NomadWorkshop.Foundation;
using Game.NomadWorkshop.Simulation;
using Game.NomadWorkshop.Simulation.Persistence;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.NomadWorkshop.PlayMode.Tests
{
    public partial class NomadWarmWorkshopPlayModeTests
    {
        [UnityTest]
        public IEnumerator DeliverySweep_PalmAndArmsStayClear_DuringRaisePourAndLower()
        {
            _context.ExecuteCommand(new SetFoundationSpeedCommand(1f));
            _context.ExecuteCommand(new SetFoundationPausedCommand(false));
            float deadline = Time.realtimeSinceStartup + 25f;
            int raising = 0, pouring = 0, lowering = 0;
            string worker = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                // Foundation/View 与 Normal Animator 都在帧 Update 中推进；下一次 FixedUpdate
                // 前一帧的物品和骨骼已经一起完成，避免 yield null 在 Animator 前读到新罐体/旧手臂。
                yield return new WaitForFixedUpdate();
                FoundationResidentReadModel resident = _read.Residents.FirstOrDefault(r =>
                    r.FacilityWork.CurrentValue.Active &&
                    r.FacilityWork.CurrentValue.Phase == FoundationResidentPhase.DeliveringWater);
                if (string.IsNullOrEmpty(resident.StableId))
                {
                    if (worker != null) break;
                    continue;
                }
                worker ??= resident.StableId;
                Assert.That(resident.StableId, Is.EqualTo(worker));
                Assert.That(_read.WaterCanCarrierId.CurrentValue, Is.EqualTo(worker));
                Transform can = FindCan();
                AssertHandOnCan(can, "progress=" + resident.FacilityWork.CurrentValue.Progress.ToString("F4"));
                Transform body = can.Find("Can Body");
                Physics.SyncTransforms();
                Assert.That(Physics.OverlapBox(body.position, body.lossyScale * .5f, body.rotation,
                    ~0, QueryTriggerInteraction.Ignore).OfType<CharacterController>().Select(c => c.name), Is.Empty,
                    "抬起/倾倒/放回携行姿态时，水罐壳体不能穿进居民身体。");
                float progress = resident.FacilityWork.CurrentValue.Progress;
                if (progress < .16f) raising++;
                else if (progress > .84f) lowering++;
                else pouring++;
            }
            Assert.That(raising, Is.GreaterThan(1), "需要采到实际抬起阶段，不能仅检查中段静止姿态。");
            Assert.That(pouring, Is.GreaterThan(1));
            Assert.That(lowering, Is.GreaterThan(1), "需要采到实际收回携行姿态的阶段。");
            Assert.That(_read.WaterCanLocation.CurrentValue, Is.EqualTo(FoundationWaterCanLocation.Resident),
                "倾倒结束后仍需持罐走到侧面，不能直接出现在地面停放区。");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator FacilityWork_FillingUsesRealValveHoseAndCan_AndPauseFreezesContact()
        {
            FoundationFacilityArtRig tank = FindTankRig();
            Quaternion closed = tank.Valve.localRotation;
            yield return WaitForFacilityWork(FoundationResidentPhase.PickingUpWater);
            FoundationResidentReadModel resident = WorkingResident(FoundationResidentPhase.PickingUpWater);
            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            yield return null;
            yield return null;
            FoundationFacilityWorkState work = resident.FacilityWork.CurrentValue;
            Assert.That(work.GroupId, Is.EqualTo("water-pickup"));
            Assert.That(RigFor(work.FacilityInstanceId), Is.SameAs(tank));
            Assert.That(Quaternion.Angle(tank.Valve.localRotation, closed), Is.GreaterThan(60f));
            Assert.That(tank.Hose.enabled && tank.Water.enabled, Is.True,
                "真实装水阶段必须出现连接当前水罐的管线和水流。");
            Transform can = FindCan();
            Assert.That(can.Find("Sealed Cap").gameObject.activeSelf, Is.False);
            Assert.That(Vector3.Distance(tank.Water.GetPosition(tank.Water.positionCount - 1),
                can.TransformPoint(FoundationWaterCanVisualFactory.OpeningPosition)), Is.LessThan(.001f));
            AssertHandOnCan(can);
            AssertResidentClearance(tank.Hose, .026f);
            AssertResidentClearance(tank.Water, .011f);
            Vector3 waterFrom = tank.Water.GetPosition(0);
            Vector3 waterTo = tank.Water.GetPosition(tank.Water.positionCount - 1);
            foreach (Transform handle in can.Cast<Transform>().Where(t => t.name.StartsWith("Handle ", StringComparison.Ordinal)))
            {
                Bounds bounds = handle.GetComponent<Renderer>().bounds;
                bounds.Expand(.022f);
                bool intersects = bounds.IntersectRay(new Ray(waterFrom, waterTo - waterFrom), out float distance);
                Assert.That(intersects && distance <= Vector3.Distance(waterFrom, waterTo), Is.False,
                    "装水口与水流不能穿过实际把手部件：" + handle.name);
            }
            Assert.That(_read.WaterCanWaterMilliliters.CurrentValue, Is.Zero,
                "动作中的水流不应提前提交整批库存。");
            int identity = can.GetInstanceID();
            Vector3 position = can.position;
            Quaternion valve = tank.Valve.localRotation;
            Vector3[] hose = new Vector3[tank.Hose.positionCount];
            tank.Hose.GetPositions(hose);
            for (int i = 0; i < 8; i++) yield return null;
            Assert.That(resident.FacilityWork.CurrentValue, Is.EqualTo(work));
            Assert.That(can.position, Is.EqualTo(position));
            Assert.That(tank.Valve.localRotation, Is.EqualTo(valve));
            for (int i = 0; i < hose.Length; i++) Assert.That(tank.Hose.GetPosition(i), Is.EqualTo(hose[i]));
            _context.ExecuteCommand(new SetFoundationPausedCommand(false));
            yield return WaitForFilledCanInMotion();
            Assert.That(FindCan().GetInstanceID(), Is.EqualTo(identity));
            Assert.That(tank.Hose.enabled || tank.Water.enabled, Is.False);
            Assert.That(FindCan().Find("Sealed Cap").gameObject.activeSelf, Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator FacilityWork_DeliveryOnlyOpensTargetLid_AndRestoreClearsOldWork()
        {
            // 第二座同类设施经正式建造 Command 进入同一布局，避免只测孤立 Prefab 的角度。
            bool enabled = _view.enabled;
            _view.enabled = false;
            try
            {
                _context.ExecuteCommand(new BeginFacilityPlacementCommand("drinking-station"));
                _context.ExecuteCommand(new MoveFacilityPreviewCommand(1800, 1200));
                DeckPose intendedPose = _read.PlacementPreview.CurrentValue.Pose;
                yield return null;
                Assert.That(_read.PlacementPreview.CurrentValue.Pose, Is.EqualTo(intendedPose));
                Assert.That(_read.PlacementPreview.CurrentValue.CanConfirm, Is.True,
                    _read.PlacementPreview.CurrentValue.Failure.ToString());
                _context.ExecuteCommand(new ConfirmFacilityPlacementCommand());
                yield return null;
                float deadline = Time.realtimeSinceStartup + 10f;
                while (_read.BuildTransactionPhase.CurrentValue != FoundationBuildTransactionPhase.Idle &&
                       Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(_read.BuildTransactionPhase.CurrentValue, Is.EqualTo(FoundationBuildTransactionPhase.Idle));
                Assert.That(_context.ExecuteCommand(new GetFoundationFacilitiesCommand()).Length, Is.EqualTo(7),
                    "正式事务必须实际提交第二座饮水站：" + _read.BuildFeedback.CurrentValue);
                _context.ExecuteCommand(new ExitFoundationBuildModeCommand());
            }
            finally { _view.enabled = enabled; }
            yield return null;
            FoundationFacilityArtRig[] dispensers = Deck.GetComponentsInChildren<FoundationFacilityArtRig>()
                .Where(r => r.ServiceDoor == null).ToArray();
            Assert.That(dispensers.Length, Is.EqualTo(2));
            Quaternion[] closed = dispensers.Select(r => r.FillLid.localRotation).ToArray();
            yield return WaitForFacilityWork(FoundationResidentPhase.DeliveringWater);
            FoundationResidentReadModel resident = WorkingResident(FoundationResidentPhase.DeliveringWater);
            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            yield return null;
            yield return null;
            FoundationFacilityArtRig target = RigFor(resident.FacilityWork.CurrentValue.FacilityInstanceId);
            for (int i = 0; i < dispensers.Length; i++)
            {
                float angle = Quaternion.Angle(dispensers[i].FillLid.localRotation, closed[i]);
                if (dispensers[i] == target) Assert.That(angle, Is.GreaterThan(80f));
                else Assert.That(angle, Is.LessThan(.001f), "未接收本批水的另一座饮水站不应开盖。");
            }
            Transform can = FindCan();
            AssertHandOnCan(can);
            Assert.That(target.Water.enabled, Is.True);
            AssertResidentClearance(target.Water, .011f);
            Assert.That(target.Water.GetPosition(0).y,
                Is.GreaterThan(target.Water.GetPosition(target.Water.positionCount - 1).y + .025f),
                "倒水口必须高于实际入水口，不能用向上流的水线掩盖站位或姿态错误。");
            int identity = can.GetInstanceID();
            var checkpoint = _context.ExecuteCommand(new CaptureFoundationCheckpointCommand());
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(checkpoint));
            yield return null;
            yield return null;
            Assert.That(FindCan().GetInstanceID(), Is.EqualTo(identity));
            Assert.That(_read.Residents.All(r => !r.FacilityWork.CurrentValue.Active), Is.True);
            Assert.That(Deck.GetComponentsInChildren<FoundationFacilityArtRig>().All(r =>
                !r.IsWorking && !r.Water.enabled && !r.Hose.enabled), Is.True,
                "恢复后的规范化事务不应留下旧设施动作或旧水流。");
            Assert.That(FindCan().Find("Sealed Cap").gameObject.activeSelf, Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator FacilityWork_RepairSlidesServiceCover_ThenClosesAfterRealRepair()
        {
            FoundationFacilityArtRig tank = FindTankRig();
            Vector3 closed = tank.ServiceDoor.localPosition;
            int repairs = _read.Residents.Sum(r => r.CompletedWaterTankRepairCount.CurrentValue);
            Assert.That(_context.ExecuteCommand(new ForcePrimaryWaterTankFaultCommand()), Is.True);
            yield return WaitForFacilityWork(FoundationResidentPhase.RepairingFacility);
            FoundationResidentReadModel resident = WorkingResident(FoundationResidentPhase.RepairingFacility);
            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            yield return null;
            Assert.That(resident.FacilityWork.CurrentValue.GroupId, Is.EqualTo("service-valve"));
            Assert.That(RigFor(resident.FacilityWork.CurrentValue.FacilityInstanceId), Is.SameAs(tank));
            Assert.That(Vector3.Distance(tank.ServiceDoor.localPosition, closed), Is.GreaterThan(.4f));
            Assert.That(tank.ConditionIndicator, Is.Not.Null);
            var conditionProperties = new MaterialPropertyBlock();
            tank.ConditionIndicator.GetPropertyBlock(conditionProperties);
            Assert.That(conditionProperties.GetColor("_EmissionColor").r, Is.GreaterThan(1f));
            Renderer paint = tank.GetComponentsInChildren<MeshRenderer>().First(r => r.sharedMaterial.name == "NW1_Teal");
            var paintProperties = new MaterialPropertyBlock();
            paint.GetPropertyBlock(paintProperties);
            Color paintColor = paintProperties.HasColor("_BaseColor")
                ? paintProperties.GetColor("_BaseColor") : paint.sharedMaterial.GetColor("_BaseColor");
            Assert.That(Vector4.Distance(paintColor, paint.sharedMaterial.GetColor("_BaseColor")), Is.LessThan(.25f),
                "故障应点亮局部警示灯，不能再把整台水箱染成红色。");
            Assert.That(_read.Residents.Sum(r => r.CompletedWaterTankRepairCount.CurrentValue), Is.EqualTo(repairs));
            Vector3 paused = tank.ServiceDoor.position;
            for (int i = 0; i < 8; i++) yield return null;
            Assert.That(tank.ServiceDoor.position, Is.EqualTo(paused));
            _context.ExecuteCommand(new SetFoundationPausedCommand(false));
            float deadline = Time.realtimeSinceStartup + 30f;
            while (_read.Residents.Sum(r => r.CompletedWaterTankRepairCount.CurrentValue) == repairs &&
                   Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(_read.Residents.Sum(r => r.CompletedWaterTankRepairCount.CurrentValue), Is.EqualTo(repairs + 1));
            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            yield return null;
            Assert.That(tank.ServiceDoor.localPosition, Is.EqualTo(closed));
            tank.ConditionIndicator.GetPropertyBlock(conditionProperties);
            Assert.That(conditionProperties.GetColor("_EmissionColor").maxColorComponent, Is.Zero);
            Assert.That(_read.Residents.Any(r => r.FacilityWork.CurrentValue.Active &&
                r.FacilityWork.CurrentValue.Phase == FoundationResidentPhase.RepairingFacility), Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator FacilityWork_StopWaterReturn_ReachesHighInlet_AndCancelFinishesOneRealBatch()
        {
            // 以合法停靠检查点开始；此用例验证车外实际取水往返和高位接触，不重测整段驾驶。
            var checkpoint = _context.ExecuteCommand(new CaptureFoundationCheckpointCommand());
            checkpoint.Vehicle.Journey.PositionMicrometers = checkpoint.Vehicle.Journey.LengthMillimeters * 1000L;
            checkpoint.Vehicle.Journey.Destination = NomadJourneyEndpoint.Destination;
            NomadInventorySaveData tankInventory = checkpoint.Inventories.Single(i => i.InventoryId == "vehicle-water-tank");
            tankInventory.Contents.Single(s => s.ResourceId == NomadResourceIds.Water.Value).AmountBaseUnits -= 4000;
            NomadInventorySaveData stationInventory = checkpoint.Inventories.Single(i => i.InventoryId.EndsWith(":drinking-water", StringComparison.Ordinal));
            stationInventory.Contents.Clear();
            stationInventory.Contents.Add(new NomadResourceStackSaveData
            {
                StackId = stationInventory.InventoryId + ":water", ResourceId = NomadResourceIds.Water.Value,
                Measure = NomadResourceIds.Water.Measure, AmountBaseUnits = 4000
            });
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(checkpoint));
            yield return null;
            Assert.That(_context.ExecuteCommand(new RequestFoundationStopWaterCommand(true)), Is.True);
            yield return WaitForFacilityWork(FoundationResidentPhase.DeliveringStopWater);
            FoundationResidentReadModel resident = WorkingResident(FoundationResidentPhase.DeliveringStopWater);
            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            yield return null;
            yield return null;
            FoundationFacilityArtRig tank = RigFor(resident.FacilityWork.CurrentValue.FacilityInstanceId);
            Assert.That(tank, Is.SameAs(FindTankRig()));
            Assert.That(resident.FacilityWork.CurrentValue.GroupId, Is.EqualTo("water-pickup"));
            Transform can = FindCan();
            AssertHandOnCan(can);
            AssertResidentClearance(tank.Water, .011f);
            Assert.That(tank.Water.GetPosition(0).y, Is.GreaterThan(tank.Inlet.position.y + .025f));
            int identity = can.GetInstanceID();
            int carried = _read.WaterCanWaterMilliliters.CurrentValue;
            int vehicleBefore = _read.VehicleWaterMilliliters.CurrentValue;
            Assert.That(carried, Is.GreaterThan(0));
            _context.ExecuteCommand(new RequestFoundationStopWaterCommand(false));
            _context.ExecuteCommand(new SetFoundationPausedCommand(false));
            float deadline = Time.realtimeSinceStartup + 20f;
            while (_read.StopWaterActive.CurrentValue && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(_read.StopWaterActive.CurrentValue, Is.False);
            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            yield return null;
            Assert.That(_read.VehicleWaterMilliliters.CurrentValue, Is.EqualTo(vehicleBefore + carried));
            Assert.That(_read.WaterCanWaterMilliliters.CurrentValue, Is.Zero);
            Assert.That(FindCan().GetInstanceID(), Is.EqualTo(identity));
            Assert.That(tank.Water.enabled, Is.False);
            Assert.That(FindCan().Find("Sealed Cap").gameObject.activeSelf, Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        private IEnumerator WaitForFacilityWork(FoundationResidentPhase phase)
        {
            _context.ExecuteCommand(new SetFoundationSpeedCommand(4f));
            _context.ExecuteCommand(new SetFoundationPausedCommand(false));
            float deadline = Time.realtimeSinceStartup + 45f;
            while (Time.realtimeSinceStartup < deadline)
            {
                foreach (FoundationResidentReadModel resident in _read.Residents)
                {
                    FoundationFacilityWorkState work = resident.FacilityWork.CurrentValue;
                    if (work.Active) Assert.That(work.Phase, Is.EqualTo(resident.ResidentPhase.CurrentValue));
                    if (work.Active && work.Phase == phase && work.Progress is > .3f and < .7f) yield break;
                }
                yield return null;
            }
            Assert.Fail("未观察到实际设施操作：" + phase + " · " +
                string.Join(" | ", _read.Residents.Select(r => r.StableId + ":" + r.ResidentPhase.CurrentValue + ":" + r.LastBlocker.CurrentValue)));
        }

        private FoundationResidentReadModel WorkingResident(FoundationResidentPhase phase) =>
            _read.Residents.First(r => r.FacilityWork.CurrentValue.Active && r.FacilityWork.CurrentValue.Phase == phase);

        private FoundationFacilityArtRig FindTankRig() =>
            Deck.GetComponentsInChildren<FoundationFacilityArtRig>().Single(r => r.ServiceDoor != null);

        private FoundationFacilityArtRig RigFor(string facilityId) =>
            Deck.GetComponentsInChildren<FoundationFacilityArtRig>().Single(r => r.GetComponentsInParent<Transform>()
                .Any(t => t.name.EndsWith("[" + facilityId + "]", StringComparison.Ordinal)));

        private static void AssertHandOnCan(Transform can, string phaseInfo = "")
        {
            var humanoid = can.parent.GetComponent<ResidentHumanoidPresentation>();
            Assert.That(humanoid, Is.Not.Null);
            var contact = humanoid.Animator.GetComponent<FoundationResidentCarryIK>();
            Transform palmTarget = can.Find(FoundationWaterCanVisualFactory.PalmTargetName);
            Assert.That(Vector3.Distance(contact.RightPalmContactPosition, palmTarget.position), Is.LessThan(.015f),
                "抬桶/倾倒时掌心表面仍需接触同一把手；target=" +
                humanoid.transform.InverseTransformPoint(palmTarget.position).ToString("F4") + "; shoulder=" +
                humanoid.transform.InverseTransformPoint(humanoid.Animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position).ToString("F4"));
            Assert.That(Quaternion.Angle(contact.RightPalmRotation, palmTarget.rotation), Is.LessThan(4f),
                "掌心朝向必须对齐把手，不能只把腕骨拉到一个点。");
            Vector3 elbow = humanoid.transform.InverseTransformPoint(humanoid.Animator.GetBoneTransform(HumanBodyBones.RightLowerArm).position);
            Vector3 shoulder = humanoid.transform.InverseTransformPoint(humanoid.Animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position);
            Assert.That(elbow.x * Mathf.Sign(shoulder.x), Is.GreaterThan(.16f), "肘部不能折向胸腔内部。");
            Transform body = can.Find("Can Body");
            Vector3 upperArm = humanoid.Animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position;
            Vector3 lowerArm = humanoid.Animator.GetBoneTransform(HumanBodyBones.RightLowerArm).position;
            Vector3 hand = humanoid.Animator.GetBoneTransform(HumanBodyBones.RightHand).position;
            phaseInfo += $"; reach={contact.RightArmReachRatio:F4}; solved={contact.RightElbowClearanceResolved}" +
                $"; lengths={contact.RightArmLength:F4}/{Vector3.Distance(upperArm, lowerArm) + Vector3.Distance(lowerArm, hand):F4}" +
                $"; shoulderBefore={humanoid.transform.InverseTransformPoint(contact.RightShoulderAtSolve):F4}" +
                $"; shoulderAfter={shoulder:F4}; wrist={humanoid.transform.InverseTransformPoint(hand):F4}";
            AssertBoneSegmentOutsideCan(upperArm, lowerArm, body, phaseInfo);
            AssertBoneSegmentOutsideCan(lowerArm, hand, body, phaseInfo);
        }

        private static void AssertBoneSegmentOutsideCan(Vector3 start, Vector3 end, Transform body, string phaseInfo,
            float radius = .03f)
        {
            // 将骨段变到罐体单位盒，手臂默认留 3 cm、穿工装的腿显式留 6 cm；不以导航胶囊代替肢体。
            Vector3 scale = body.lossyScale;
            var bounds = new Bounds(Vector3.zero, Vector3.one + new Vector3(
                2f * radius / scale.x, 2f * radius / scale.y, 2f * radius / scale.z));
            Vector3 from = body.InverseTransformPoint(start);
            Vector3 to = body.InverseTransformPoint(end);
            bool hit = bounds.Contains(from) || (bounds.IntersectRay(new Ray(from, to - from), out float distance) &&
                distance <= Vector3.Distance(from, to));
            Assert.That(hit, Is.False, $"水罐壳体不能穿入肢体，半径 {radius:F3} m；罐体单位盒骨段 {from:F4} → {to:F4}。{phaseInfo}");
        }

        private static void AssertResidentClearance(LineRenderer line, float radius)
        {
            Assert.That(line.enabled, Is.True);
            Physics.SyncTransforms();
            for (int i = 1; i < line.positionCount; i++)
            {
                Collider[] contacts = Physics.OverlapCapsule(line.GetPosition(i - 1), line.GetPosition(i),
                    radius, ~0, QueryTriggerInteraction.Ignore);
                Assert.That(contacts.OfType<CharacterController>().Select(c => c.name), Is.Empty,
                    $"{line.name} 第 {i} 段穿过真实居民碰撞体。");
            }
        }
    }
}
#endif
