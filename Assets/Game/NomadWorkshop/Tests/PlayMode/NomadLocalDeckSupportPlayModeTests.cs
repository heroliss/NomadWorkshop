#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Game.NomadWorkshop.Foundation;
using Game.NomadWorkshop.Navigation;
using Game.NomadWorkshop.Simulation.Persistence;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.NomadWorkshop.PlayMode.Tests
{
    /// <summary>在三居民实际车辆副本中验证局部楼板、建造事务、携物与原有生活/旅程边界。</summary>
    public sealed class NomadLocalDeckSupportPlayModeTests : NomadResidentCrewPlayModeTests
    {
        protected override string ScenePath => "Assets/Game/NomadWorkshop/Scenes/LocalDeckSupportSample.unity";
        protected override string WorkshirtPrefix => "NW10_Shirt_";
        private T Find<T>() where T : Component => SceneManager.GetSceneByPath(ScenePath).GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<T>(true)).Single();

        [UnityTest]
        public IEnumerator CheckpointAcceptsRealBodyNearFacilityCorner_ButRejectsUnsupportedHole()
        {
            var context = Find<NomadFoundationContext>();
            var save = context.ExecuteCommand(new CaptureFoundationCheckpointCommand());
            save.Facilities.Single(f=>f.DefinitionId=="drinking-station").Pose = new QuantizedDeckPose(-6200,1000,0);
            // 来自真实携物途中失败的脚底；位于圆胶囊可通过、工作位方形预留空间会拒绝的拐角。
            save.Residents[0].Pose = new QuantizedDeckPose(-5561,422,0);
            string id = save.Residents[0].ResidentId;
            context.ExecuteCommand(new RestoreFoundationCheckpointCommand(save));
            yield return null;
            var read = context.ExecuteCommand(new GetFoundationReadModelCommand());
            Vector3 position = read.Residents.Single(r=>r.StableId==id).ResidentLocalPosition.CurrentValue;
            Assert.That(Vector3.Distance(position,new Vector3(-5.561f,0,.422f)), Is.LessThan(.002f));
            var invalid = context.ExecuteCommand(new CaptureFoundationCheckpointCommand());
            invalid.Residents.Single(r=>r.ResidentId==id).Pose = new QuantizedDeckPose(-7600,0,0);
            Assert.That(()=>context.ExecuteCommand(new RestoreFoundationCheckpointCommand(invalid)),
                Throws.TypeOf<System.InvalidOperationException>());
            yield return null;
            Assert.That(Vector3.Distance(read.Residents.Single(r=>r.StableId==id).ResidentLocalPosition.CurrentValue,position),
                Is.LessThan(.002f), "错误存档回滚后原来的合法位置仍可重建。");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator TranslatedAndRotatedStationKeepsItsSemanticWorkingSlotsReachable()
        {
            var context = Find<NomadFoundationContext>();
            var poses = new[] {
                new QuantizedDeckPose(-6000,0,0), new QuantizedDeckPose(-6040,0,0),
                new QuantizedDeckPose(-6080,0,0), new QuantizedDeckPose(-6000,800,0),
                new QuantizedDeckPose(-6200,1000,900), new QuantizedDeckPose(-6200,1000,1800),
                new QuantizedDeckPose(-6200,0,2700) };
            foreach (var pose in poses)
            {
                var save = context.ExecuteCommand(new CaptureFoundationCheckpointCommand());
                var station = save.Facilities.Single(f=>f.DefinitionId=="drinking-station");
                station.Pose = pose;
                context.ExecuteCommand(new RestoreFoundationCheckpointCommand(save));
                yield return null;
                var access = context.ExecuteCommand(new GetFoundationFacilityAccessCommand()).Single(a=>a.InstanceId==station.InstanceId);
                Assert.That(access.CommittedAccess, Is.EqualTo(FoundationFacilityAccess.Reachable), pose.ToString());
                Assert.That(access.DisplayReachableSlotCount, Is.EqualTo(4), pose.ToString());
            }
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator StaticColliderOutsideNavMeshSourcesStillBlocksExactWorkingPose()
        {
            var context = Find<NomadFoundationContext>();
            var save = context.ExecuteCommand(new CaptureFoundationCheckpointCommand());
            var station = save.Facilities.Single(f=>f.DefinitionId=="drinking-station");
            station.Pose = new QuantizedDeckPose(-6000,0,0);
            context.ExecuteCommand(new RestoreFoundationCheckpointCommand(save));
            yield return null;
            var wall = new GameObject("Docking test · static obstruction outside navigation collection");
            wall.layer = 2; // Ignore Raycast 不等于不参与身体碰撞，不能用射线默认层掩码漏掉它。
            SceneManager.MoveGameObjectToScene(wall, SceneManager.GetSceneByPath(ScenePath));
            try
            {
                var deck = Find<FoundationDeckSupportSurface>().transform;
                wall.transform.SetPositionAndRotation(deck.TransformPoint(new Vector3(-6.48f,.9f,-.49f)),deck.rotation);
                wall.AddComponent<BoxCollider>().size = new Vector3(.65f,1.8f,.01f);
                Physics.SyncTransforms();
                context.ExecuteCommand(new RestoreFoundationCheckpointCommand(save));
                yield return null;
                var nav = Find<DeckNavigationUtility>();
                Assert.That(nav.TryCalculateCompleteLocalPath(Vector3.zero,new Vector3(-6.48f,0,-.64f),out _), Is.True,
                    "此例必须保留完整 NavMesh 路径，才能证明额外的物理检查。");
                var access = context.ExecuteCommand(new GetFoundationFacilityAccessCommand()).Single(a=>a.InstanceId==station.InstanceId);
                for (int i=0;i<3;i++) Assert.That(access.IsDisplaySlotReachable(i), Is.False, "静态墙挡住了正面身体位置。");
                Assert.That(access.IsDisplaySlotReachable(3), Is.True, "旁边的搬罐位仍可用。");
                wall.SetActive(false);
                context.ExecuteCommand(new RestoreFoundationCheckpointCommand(save));
                yield return null;
                Assert.That(context.ExecuteCommand(new GetFoundationFacilityAccessCommand()).Single(a=>a.InstanceId==station.InstanceId)
                    .DisplayReachableSlotCount, Is.EqualTo(4), "清除阻挡后不能遗留不可达缓存。");
            }
            finally { wall.SetActive(false); Object.Destroy(wall); }
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator NativeResidentDocksAtVoxelShiftedStation_ThenTransfersWater_AndPauseFreezesBoth()
        {
            var context = Find<NomadFoundationContext>();
            var read = context.ExecuteCommand(new GetFoundationReadModelCommand());
            var save = context.ExecuteCommand(new CaptureFoundationCheckpointCommand());
            save.Facilities.Single(f=>f.DefinitionId=="drinking-station").Pose = new QuantizedDeckPose(-6000,0,0);
            context.ExecuteCommand(new RestoreFoundationCheckpointCommand(save));
            int initialWater = read.DrinkingStationWaterMilliliters.CurrentValue;
            context.ExecuteCommand(new SetFoundationSpeedCommand(4f));
            context.ExecuteCommand(new SetFoundationPausedCommand(false));
            float deadline = Time.realtimeSinceStartup + 40f;
            FoundationResidentReadModel carrier = default;
            bool arrived = false;
            while (Time.realtimeSinceStartup < deadline)
            {
                foreach (var resident in read.Residents)
                    if (resident.ResidentPhase.CurrentValue == FoundationResidentPhase.DeliveringWater)
                    { carrier = resident; arrived = true; break; }
                if (arrived) break;
                Assert.That(read.DrinkingStationWaterMilliliters.CurrentValue, Is.EqualTo(initialWater), "真实到岗前不得交接水。");
                yield return null;
            }
            Assert.That(arrived, Is.True, string.Join(" | ",read.Residents.Select(r=>r.StableId+":"+r.ResidentPhase.CurrentValue+":"+r.LastBlocker.CurrentValue)));
            context.ExecuteCommand(new SetFoundationPausedCommand(true));
            Vector3 position = carrier.ResidentLocalPosition.CurrentValue;
            var deck = Find<FoundationDeckSupportSurface>().transform;
            var body = SceneManager.GetSceneByPath(ScenePath).GetRootGameObjects()
                .SelectMany(r=>r.GetComponentsInChildren<CharacterController>()).Single(b=>b.name=="Resident Motor · "+carrier.StableId);
            Vector3 physical = deck.InverseTransformPoint(body.transform.position);
            float distance = new[]{-6.56f,-6.48f,-6.4f}.Min(x=>Vector3.Distance(physical,new Vector3(x,0,-.64f)));
            Assert.That(distance, Is.LessThan(.025f), "不能用路径终点代替实际身体到岗。");
            int stationWater = read.DrinkingStationWaterMilliliters.CurrentValue;
            int canWater = read.WaterCanWaterMilliliters.CurrentValue;
            for (int i=0;i<5;i++) yield return null;
            Assert.That(carrier.ResidentLocalPosition.CurrentValue, Is.EqualTo(position));
            Assert.That(deck.InverseTransformPoint(body.transform.position), Is.EqualTo(physical));
            Assert.That(read.DrinkingStationWaterMilliliters.CurrentValue, Is.EqualTo(stationWater));
            Assert.That(read.WaterCanWaterMilliliters.CurrentValue, Is.EqualTo(canWater));
            context.ExecuteCommand(new SetFoundationPausedCommand(false));
            deadline = Time.realtimeSinceStartup + 8f;
            while (read.DrinkingStationWaterMilliliters.CurrentValue <= stationWater && Time.realtimeSinceStartup < deadline)
                yield return null;
            context.ExecuteCommand(new SetFoundationPausedCommand(true));
            Assert.That(read.DrinkingStationWaterMilliliters.CurrentValue, Is.GreaterThan(stationWater));
            Assert.That(read.WaterCanWaterMilliliters.CurrentValue, Is.LessThan(canWater));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator HoleHasNoFloor_ActualNavigationDetours_AndSeamHasContinuousHeight()
        {
            var surface = Find<FoundationDeckSupportSurface>();
            var collider = surface.GetComponent<MeshCollider>();
            var nav = Find<DeckNavigationUtility>();
            Assert.That(nav.TryCalculateCompleteLocalPath(new Vector3(-8.5f,0,0), new Vector3(-6.4f,0,0), out var path), Is.True);
            Assert.That(path.Corners.Count, Is.GreaterThan(2));
            Assert.That(path.Corners.Any(c => Mathf.Abs(c.z) > .6f), Is.True, "原生导航必须绕开实际开孔。");
            Assert.That(collider.Raycast(new Ray(surface.transform.TransformPoint(new Vector3(-7.6f,1,0)), Vector3.down), out _, 2f), Is.False,
                "孔洞下不能残留旧矩形碰撞。");
            foreach (float x in new[] { -5.401f, -5.4f, -5.399f })
            {
                Assert.That(collider.Raycast(new Ray(surface.transform.TransformPoint(new Vector3(x,1,1.4f)), Vector3.down), out var hit, 2f), Is.True);
                Assert.That(surface.transform.InverseTransformPoint(hit.point).y, Is.EqualTo(0f).Within(.0001f));
            }
            var visible = surface.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(visible.bounds.max.x, Is.EqualTo(-5.4f).Within(.001f), "新板面不能覆盖精制主甲板外观。");
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator BuildRejectsHole_CommitsOnExtension_AndRestoresFacilityPose()
        {
            var context = Find<NomadFoundationContext>();
            var view = Find<NomadFoundationWorldView>();
            var read = context.ExecuteCommand(new GetFoundationReadModelCommand());
            bool enabled = view.enabled;
            try
            {
                view.enabled = false;
                context.ExecuteCommand(new SetFoundationPositionSnapCommand(0));
                context.ExecuteCommand(new BeginFacilityPlacementCommand("drinking-station"));
                context.ExecuteCommand(new MoveFacilityPreviewCommand(-7600,0));
                Assert.That(read.PlacementPreview.CurrentValue.Failure, Is.EqualTo(FoundationPlacementFailure.FootprintOutOfBounds));
                Assert.That(read.PlacementPreview.CurrentValue.CanConfirm, Is.False);
                context.ExecuteCommand(new MoveFacilityPreviewCommand(-6200,1000));
                Assert.That(read.PlacementPreview.CurrentValue.CanConfirm, Is.True, read.PlacementPreview.CurrentValue.Failure.ToString());
                int count = context.ExecuteCommand(new GetFoundationFacilitiesCommand()).Length;
                yield return null; // 玩家开始预览的同帧确认被输入防抖契约拒绝。
                context.ExecuteCommand(new ConfirmFacilityPlacementCommand());
                yield return null;
                float deadline = Time.realtimeSinceStartup + 10f;
                while (read.BuildTransactionPhase.CurrentValue != FoundationBuildTransactionPhase.Idle && Time.realtimeSinceStartup < deadline)
                    yield return null;
                var facilities = context.ExecuteCommand(new GetFoundationFacilitiesCommand());
                Assert.That(facilities.Length, Is.EqualTo(count+1), read.BuildFeedback.CurrentValue);
                var added = facilities.Single(f => f.Pose.XMillimeters == -6200);
                var checkpoint = context.ExecuteCommand(new CaptureFoundationCheckpointCommand());
                context.ExecuteCommand(new RestoreFoundationCheckpointCommand(checkpoint));
                yield return null; yield return null;
                Assert.That(context.ExecuteCommand(new GetFoundationFacilitiesCommand()).Single(f => f.InstanceId == added.InstanceId).Pose, Is.EqualTo(added.Pose));
                Assert.That(read.BuildTransactionPhase.CurrentValue, Is.EqualTo(FoundationBuildTransactionPhase.Idle));
            }
            finally { view.enabled = enabled; }
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ResidentCarriesRealCanOntoExtension_PauseAndRestoreKeepBodyOnFloor()
        {
            var context = Find<NomadFoundationContext>();
            var read = context.ExecuteCommand(new GetFoundationReadModelCommand());
            var initial = context.ExecuteCommand(new CaptureFoundationCheckpointCommand());
            initial.Facilities.Single(f => f.DefinitionId == "drinking-station").Pose = new QuantizedDeckPose(-6200,1000,0);
            context.ExecuteCommand(new RestoreFoundationCheckpointCommand(initial));
            context.ExecuteCommand(new SetFoundationSpeedCommand(4f));
            context.ExecuteCommand(new SetFoundationPausedCommand(false));
            float deadline = Time.realtimeSinceStartup + 40f;
            FoundationResidentReadModel carrier = default;
            bool crossed = false;
            while (Time.realtimeSinceStartup < deadline)
            {
                foreach (var resident in read.Residents)
                    if (resident.StableId == read.WaterCanCarrierId.CurrentValue &&
                        read.WaterCanWaterMilliliters.CurrentValue > 0 &&
                        resident.ResidentLocalPosition.CurrentValue.x < -5.55f)
                    { carrier = resident; crossed = true; break; }
                if (crossed) break;
                yield return new WaitForFixedUpdate();
            }
            Assert.That(crossed, Is.True, string.Join(" | ",read.Residents.Select(r => r.StableId+":"+r.ResidentPhase.CurrentValue+":"+r.LastBlocker.CurrentValue)));
            context.ExecuteCommand(new SetFoundationPausedCommand(true));
            yield return null; yield return null;
            Vector3 before = carrier.ResidentLocalPosition.CurrentValue;
            int water = read.WaterCanWaterMilliliters.CurrentValue;
            int vehicleWater = read.VehicleWaterMilliliters.CurrentValue;
            var floor = Find<FoundationDeckSupportSurface>().GetComponent<MeshCollider>();
            Assert.That(floor.Raycast(new Ray(floor.transform.TransformPoint(before+Vector3.up),Vector3.down),out var hit,2f), Is.True);
            Assert.That(before.y, Is.EqualTo(floor.transform.InverseTransformPoint(hit.point).y).Within(.04f));
            for (int i=0;i<5;i++) yield return null;
            Assert.That(carrier.ResidentLocalPosition.CurrentValue, Is.EqualTo(before));
            var checkpoint = context.ExecuteCommand(new CaptureFoundationCheckpointCommand());
            string id = carrier.StableId;
            context.ExecuteCommand(new RestoreFoundationCheckpointCommand(checkpoint));
            yield return null; yield return null;
            var restored = read.Residents.Single(r=>r.StableId==id);
            Assert.That(Vector3.Distance(restored.ResidentLocalPosition.CurrentValue,before), Is.LessThan(.002f));
            // 正式检查点按既有协议中止携物事务，把水归还车载水箱；与楼梯实验的动作快照不同。
            Assert.That(read.WaterCanWaterMilliliters.CurrentValue, Is.Zero);
            Assert.That(read.VehicleWaterMilliliters.CurrentValue + read.WaterCanWaterMilliliters.CurrentValue,
                Is.EqualTo(vehicleWater + water));
            Assert.That(read.WaterCanLocation.CurrentValue, Is.EqualTo(FoundationWaterCanLocation.VehicleWaterTank));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
#endif
