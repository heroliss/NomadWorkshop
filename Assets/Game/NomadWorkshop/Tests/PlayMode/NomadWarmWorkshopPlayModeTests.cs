#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using Game.NomadWorkshop.Foundation;
using Game.NomadWorkshop.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.NomadWorkshop.PlayMode.Tests
{
    /// <summary>在落盘美术场景上验证动作、实际携物和检查点接线，不把单独 Animator 绿灯当成整场景通过。</summary>
    public partial class NomadWarmWorkshopPlayModeTests
    {
        /// <summary>同一组行为断言可对实际落盘的人物候选场景执行；不在运行途中热换模型。</summary>
        protected virtual string ScenePath => "Assets/Game/NomadWorkshop/Scenes/NomadWarmWorkshop.unity";
        protected virtual string WorkshirtMaterialName => "NW1_Workshirt";
        /// <summary>多外观候选可按居民身份声明准确材质；仍逐人检查，不降低为只要任意人有衣服。</summary>
        protected virtual string ExpectedWorkshirtMaterial(ResidentHumanoidPresentation resident) => WorkshirtMaterialName;
        private Scene _previous;
        private Scene _scene;
        private bool _ownsScene;
        private NomadFoundationContext _context;
        private NomadFoundationWorldView _view;
        private FoundationReadModel _read;
        private NomadBackgroundFramePump _framePump;
        private Transform Deck => _view.transform.Find("Vehicle Deck Root");

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _framePump = new NomadBackgroundFramePump();
            _previous = SceneManager.GetActiveScene();
            _scene = SceneManager.GetSceneByPath(ScenePath);
            _ownsScene = !_scene.IsValid() || !_scene.isLoaded;
            if (_ownsScene)
            {
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath,
                    new LoadSceneParameters(LoadSceneMode.Additive));
                _scene = SceneManager.GetSceneByPath(ScenePath);
            }
            Assert.That(SceneManager.SetActiveScene(_scene), Is.True);
            yield return null;
            _context = FindOne<NomadFoundationContext>();
            _view = FindOne<NomadFoundationWorldView>();
            _read = _context.ExecuteCommand(new GetFoundationReadModelCommand());
            Assert.That(_read.IsReady.CurrentValue, Is.True, "实际样板的初始设施与出生点必须通过导航验收。");
            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            Assert.That(_context.ExecuteCommand(new ResetFoundationForSoakHarnessCommand(1729)), Is.True);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _framePump?.Dispose();
            _framePump = null;
            if (_previous.IsValid() && _previous.isLoaded) SceneManager.SetActiveScene(_previous);
            if (_ownsScene && _scene.IsValid() && _scene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(_scene);
        }

        [UnityTest]
        public IEnumerator PlayableScene_HasThreeClothedHumanoids_SixFacilities_AndIndependentWasteBucket()
        {
            Assert.That(_read.Residents.Count, Is.EqualTo(3));
            Assert.That(_context.ExecuteCommand(new GetFoundationFacilitiesCommand()).Length, Is.EqualTo(6));
            ResidentHumanoidPresentation[] residents = Deck.GetComponentsInChildren<ResidentHumanoidPresentation>();
            Assert.That(residents.Length, Is.EqualTo(3));
            foreach (ResidentHumanoidPresentation resident in residents)
            {
                Assert.That(resident.IsReady, Is.True);
                Assert.That(resident.Animator.applyRootMotion, Is.False);
                Assert.That(resident.Animator.GetComponent<FoundationResidentCarryIK>(), Is.Not.Null);
                SkinnedMeshRenderer eyes = resident.Animator.GetComponentsInChildren<SkinnedMeshRenderer>()
                    .Single(r => r.name == "Eyes");
                var eyeMesh = new Mesh();
                try
                {
                    // 包围盒保留导入/动画安全范围，不能用来判断动作后的脸朝向。
                    // 这两套 FBX 均保留嵌套单位缩放，和导入校核使用同一 BakeMesh 约定。
                    eyes.BakeMesh(eyeMesh, true);
                    Assert.That(eyeMesh.vertexCount, Is.GreaterThan(0));
                    Vector3 center = Vector3.zero;
                    foreach (Vector3 vertex in eyeMesh.vertices) center += vertex;
                    float eyeZ = resident.transform.InverseTransformPoint(
                        eyes.transform.TransformPoint(center / eyeMesh.vertexCount)).z;
                    float headZ = resident.transform.InverseTransformPoint(
                        resident.Animator.GetBoneTransform(HumanBodyBones.Head).position).z;
                    Assert.That(eyeZ-headZ, Is.GreaterThan(.015f), "居民面朝方向应与玩法根的 +Z 一致。");
                }
                finally { UnityEngine.Object.DestroyImmediate(eyeMesh); }
                Assert.That(resident.VisualRoot.GetComponentsInChildren<Renderer>()
                    .Any(r => r.sharedMaterials.Any(m => m != null && m.name == ExpectedWorkshirtMaterial(resident))), Is.True);
            }
            Transform bucket = Deck.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Detachable Waste Bucket");
            Assert.That(bucket.Find("Bucket Handle"), Is.Not.Null);
            Assert.That(bucket.parent.name, Does.Contain("旱厕"));
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator CarryingWater_HandTouchesHandle_AndPauseFreezesBodyPropAndAnimation()
        {
            yield return WaitForFilledCanInMotion();
            Transform can = FindCan();
            var resident = can.parent.GetComponent<ResidentHumanoidPresentation>();
            Assert.That(resident, Is.Not.Null);
            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            yield return null;
            yield return null;
            Transform hand = resident.Animator.GetBoneTransform(HumanBodyBones.RightHand);
            Assert.That(resident.Animator.GetComponent<FoundationResidentCarryIK>().RightHandContactWeight,
                Is.EqualTo(1f), "Animator 必须实际执行携物 IK 回调。");
            AssertHandOnCan(can);
            Vector3 bodyPosition = can.parent.position;
            Vector3 canPosition = can.position;
            Vector3 handPosition = hand.position;
            float normalizedTime = resident.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            long tick = _read.SimulationTick.CurrentValue;
            int itemId = can.GetInstanceID();
            for (int i = 0; i < 8; i++) yield return null;
            Assert.That(_read.SimulationTick.CurrentValue, Is.EqualTo(tick));
            Assert.That(resident.Animator.speed, Is.Zero);
            Assert.That(Vector3.Distance(can.parent.position, bodyPosition), Is.LessThan(.00001f));
            Assert.That(Vector3.Distance(can.position, canPosition), Is.LessThan(.00001f));
            Assert.That(Vector3.Distance(hand.position, handPosition), Is.LessThan(.00001f));
            Assert.That(resident.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime,
                Is.EqualTo(normalizedTime).Within(.00001f));
            _context.ExecuteCommand(new SetFoundationPausedCommand(false));
            for (int i = 0; i < 180 && Vector3.Distance(resident.transform.position, bodyPosition) < .03f; i++)
                yield return null;
            Assert.That(Vector3.Distance(resident.transform.position, bodyPosition), Is.GreaterThan(.03f));
            Assert.That(FindCan().GetInstanceID(), Is.EqualTo(itemId));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator RestoreWhileCarrying_ReusesPhysicalCan_AndClearsStaleHandOwnership()
        {
            yield return WaitForFilledCanInMotion();
            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            yield return null;
            Transform can = FindCan();
            int itemId = can.GetInstanceID();
            var checkpoint = _context.ExecuteCommand(new CaptureFoundationCheckpointCommand());
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(checkpoint));
            yield return null;
            yield return null;
            Assert.That(FindCan().GetInstanceID(), Is.EqualTo(itemId));
            Assert.That(Deck.GetComponentsInChildren<Transform>(true)
                .Count(t => t.name == "Water Can 01 [physical carrier]"), Is.EqualTo(1));
            Assert.That(_read.WaterCanLocation.CurrentValue, Is.Not.EqualTo(FoundationWaterCanLocation.Resident),
                "检查点恢复会规范化瞬时携物工作，物品投影应同步释放原来的手。");
            Assert.That(can.parent, Is.EqualTo(Deck));
            foreach (var resident in Deck.GetComponentsInChildren<ResidentHumanoidPresentation>())
                Assert.That(resident.Animator.GetComponent<FoundationResidentCarryIK>().RightHandContactWeight, Is.Zero);
            Assert.That(_read.WaterCanWaterMilliliters.CurrentValue,
                Is.EqualTo(checkpoint.Inventories.Single(x => x.InventoryId == "water-can-01").Contents.Sum(x => x.AmountBaseUnits)));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator JourneyDust_CommittedClockHandlesLongFrames_PauseDecayAndSpaceRebuild()
        {
            const string prefabs = "Assets/Game/NomadWorkshop/ArtFirstPass/Prefabs/";
            GameObject vehicle = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(prefabs + "NW1_Vehicle.prefab"));
            GameObject desert = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(prefabs + "NW1_Desert.prefab"));
            vehicle.transform.position = desert.transform.position = new Vector3(200f,0f,0f);
            try
            {
                using var presentation = new FoundationJourneyPresentation(vehicle.transform, desert.transform);
                presentation.Render(0L, 0L, false);
                presentation.Render(6000000L, 1000L, true);
                ParticleSystem[] dust = vehicle.GetComponentsInChildren<ParticleSystem>();
                Assert.That(dust.Length, Is.EqualTo(4));
                Assert.That(dust.All(p => p.particleCount > 0), Is.True, "一次长帧的真实进度必须产生扬尘。");
                float[] times = dust.Select(p => p.time).ToArray();
                for (int i = 0; i < 8; i++)
                {
                    presentation.Render(6000000L, 1000L, true);
                    yield return null;
                }
                Assert.That(dust.Select(p => p.time), Is.EqualTo(times), "未提交新模拟时间时，帧循环不能自行推进粒子。");
                presentation.Render(6000000L, 3000L, false);
                Assert.That(dust.Sum(p => p.particleCount), Is.Zero, "停车后已有扬尘按模拟时间消散。");
                presentation.Render(12000000L, 4000L, true);
                Assert.That(dust.Sum(p => p.particleCount), Is.GreaterThan(0));
                presentation.ClearTransientEffects();
                presentation.Render(24000000L, 10000L, true);
                Assert.That(dust.Sum(p => p.particleCount), Is.Zero, "空间重建不能补播旧/新检查点之间的扬尘。");
                presentation.Render(30000000L, 11000L, true);
                Assert.That(dust.Sum(p => p.particleCount), Is.GreaterThan(0));
                presentation.Render(0L, 0L, false);
                Assert.That(dust.Sum(p => p.particleCount), Is.Zero, "时间回退也必须清除旧扬尘。");
            }
            finally
            {
                UnityEngine.Object.Destroy(vehicle);
                UnityEngine.Object.Destroy(desert);
            }
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator RealJourney_MovesTrackAndScenery_FreezesOnPause_AndRestoresAbsolutePose()
        {
            Transform[] transforms = Deck.GetComponentsInChildren<Transform>(true);
            Transform wheel = transforms.First(t => t.name.Contains("_Wheel_") && t.GetComponent<Renderer>() == null);
            Transform shoe = transforms.First(t => t.name.EndsWith("_Shoe_00", StringComparison.Ordinal));
            FoundationEnvironmentVisual environment = Deck.GetComponentInChildren<FoundationEnvironmentVisual>();
            Transform rock = environment != null ? environment.SceneryGroups[0] : transforms.First(t => t.name == "Desert outcrop_0");
            Renderer[] scrolling = environment != null ? environment.ScrollingSurfaces.Select(s => s.Renderer).ToArray() : Array.Empty<Renderer>();
            Vector4[] initialUvs = scrolling.Select(EnvironmentUv).ToArray();
            Vector3 deckPosition = Deck.position;
            Quaternion initialWheel = wheel.rotation;
            Vector3 initialShoe = shoe.position;
            Vector3 initialRock = rock.position;
            _context.ExecuteCommand(new SetFoundationJourneyDestinationCommand(NomadJourneyEndpoint.Destination));
            _context.ExecuteCommand(new SetFoundationSpeedCommand(4f));
            _context.ExecuteCommand(new SetFoundationPausedCommand(false));
            yield return WaitForJourneyProgress(0, 3000000L);
            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            yield return null;
            Assert.That(Quaternion.Angle(wheel.rotation, initialWheel), Is.GreaterThan(1f));
            Assert.That(Vector3.Distance(shoe.position, initialShoe), Is.GreaterThan(.02f));
            Assert.That(Vector3.Distance(rock.position, initialRock), Is.GreaterThan(.1f));
            Vector4[] savedUvs = scrolling.Select(EnvironmentUv).ToArray();
            if (scrolling.Length > 0) Assert.That(savedUvs.SequenceEqual(initialUvs), Is.False, "真实旅程必须同时滚动地表/车辙。");
            Assert.That(Deck.position, Is.EqualTo(deckPosition), "表现不得移动导航所在的逻辑甲板。");
            var checkpoint = _context.ExecuteCommand(new CaptureFoundationCheckpointCommand());
            Quaternion savedWheel = wheel.rotation;
            Vector3 savedShoe = shoe.position;
            Vector3 savedRock = rock.position;
            ParticleSystem[] dust = Deck.GetComponentsInChildren<ParticleSystem>()
                .Where(p => p.name == "履带扬尘").ToArray();
            Assert.That(dust.Length, Is.EqualTo(4));
            Assert.That(dust.Sum(p => p.particleCount), Is.GreaterThan(0),
                "实际行驶需要产生扬尘。position=" + _read.JourneyPositionMicrometers.CurrentValue +
                " tick=" + _read.SimulationTick.CurrentValue + " particles=" +
                string.Join(" | ", dust.Select(p => $"time={p.time:F4} playing={p.isPlaying} paused={p.isPaused} " +
                    $"emitting={p.emission.enabled} visible={p.GetComponent<ParticleSystemRenderer>().isVisible} " +
                    $"culling={p.main.cullingMode} speed={p.main.simulationSpeed}")));
            float[] particleTimes = dust.Select(p => p.time).ToArray();
            for (int i = 0; i < 8; i++) yield return null;
            Assert.That(wheel.rotation, Is.EqualTo(savedWheel));
            Assert.That(shoe.position, Is.EqualTo(savedShoe));
            Assert.That(rock.position, Is.EqualTo(savedRock));
            Assert.That(scrolling.Select(EnvironmentUv), Is.EqualTo(savedUvs), "暂停不能继续滚动纹理。");
            Assert.That(dust.Select(p => p.time), Is.EqualTo(particleTimes));
            long savedPosition = _read.JourneyPositionMicrometers.CurrentValue;
            _context.ExecuteCommand(new SetFoundationPausedCommand(false));
            yield return WaitForJourneyProgress(savedPosition, 600000L);
            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(checkpoint));
            yield return null;
            Assert.That(_read.JourneyPositionMicrometers.CurrentValue, Is.EqualTo(savedPosition));
            Assert.That(Quaternion.Angle(wheel.rotation, savedWheel), Is.LessThan(.001f));
            Assert.That(Vector3.Distance(shoe.position, savedShoe), Is.LessThan(.0001f));
            Assert.That(Vector3.Distance(rock.position, savedRock), Is.LessThan(.0001f));
            Assert.That(scrolling.Select(EnvironmentUv), Is.EqualTo(savedUvs), "恢复必须还原同一地表与车辙相位。");
            Assert.That(dust.Sum(p => p.particleCount), Is.Zero, "恢复时不能把旧路段的扬尘带到新状态。");
            _context.ExecuteCommand(new SetFoundationJourneyDestinationCommand(NomadJourneyEndpoint.None));
            _context.ExecuteCommand(new SetFoundationPausedCommand(false));
            for (int i = 0; i < 8; i++) yield return null;
            Assert.That(_read.JourneyPositionMicrometers.CurrentValue, Is.EqualTo(savedPosition));
            Assert.That(wheel.rotation, Is.EqualTo(savedWheel));
            LogAssert.NoUnexpectedReceived();
        }

        private IEnumerator WaitForJourneyProgress(long from, long minimumDelta)
        {
            float deadline = Time.realtimeSinceStartup + 20f;
            while (_read.JourneyPositionMicrometers.CurrentValue - from < minimumDelta && Time.realtimeSinceStartup < deadline)
                // 与接触验收相同，先让上一帧的表现和粒子完成，再观察距离并提交暂停。
                // 在 Update 后立刻暂停可能截断首次发射，尤其是后台长帧一次跨过距离阈值时。
                yield return new WaitForFixedUpdate();
            Assert.That(_read.JourneyPositionMicrometers.CurrentValue - from, Is.GreaterThanOrEqualTo(minimumDelta),
                "真实居民必须实际到驾驶位后才出现行驶反馈：" + _read.DepartureFeedback.CurrentValue);
        }

        private static Vector4 EnvironmentUv(Renderer renderer)
        {
            var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block); return block.GetVector("_BaseMap_ST");
        }

        private IEnumerator WaitForFilledCanInMotion()
        {
            _context.ExecuteCommand(new SetFoundationSpeedCommand(4f));
            _context.ExecuteCommand(new SetFoundationPausedCommand(false));
            for (int i = 0; i < 1200; i++)
            {
                if (_read.WaterCanWaterMilliliters.CurrentValue > 0 &&
                    _read.WaterCanLocation.CurrentValue == FoundationWaterCanLocation.Resident &&
                    _read.Residents.Any(r => r.ResidentPhase.CurrentValue == FoundationResidentPhase.MovingToDrinkingStation))
                {
                    yield return null;
                    yield break;
                }
                yield return null;
            }
            Assert.Fail("未在有界帧数内观察到持满罐行走：" +
                string.Join(" | ", _read.Residents.Select(r => r.StableId + ":" + r.ResidentPhase.CurrentValue + ":" + r.LastBlocker.CurrentValue)));
        }

        private Transform FindCan() => Deck.GetComponentsInChildren<Transform>(true)
            .Single(t => t.name == "Water Can 01 [physical carrier]");

        private T FindOne<T>() where T : Component => _scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true)).Single();
    }
}
#endif
