#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Game.Framework.Common;
using Game.NomadWorkshop.Foundation;
using Game.NomadWorkshop.Navigation;
using NUnit.Framework;
using R3;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.NomadWorkshop.PlayMode.Tests
{
    /// <summary>在实际保存的踏面场景上检验身体、层高、携物和暂停恢复，动画不作为到达的 Oracle。</summary>
    public class NomadStairTraversalPlayModeTests
    {
        /// <summary>使用各候选的落盘场景，复用相同身体、层高与物品所有权断言。</summary>
        protected virtual string ScenePath => "Assets/Game/NomadWorkshop/Scenes/StairCarrySpike.unity";
        private Scene _previous, _scene;
        private bool _owns;
        private NomadFoundationContext _context;
        private NomadStairTraversalView _view;
        private ReadOnlyReactiveProperty<StairTraversalState> _read;
        private Bounds _canBounds;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _previous = SceneManager.GetActiveScene();
            _scene = SceneManager.GetSceneByPath(ScenePath);
            _owns = !_scene.IsValid() || !_scene.isLoaded;
            if (_owns)
            {
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath,
                    new LoadSceneParameters(LoadSceneMode.Additive));
                _scene = SceneManager.GetSceneByPath(ScenePath);
            }
            SceneManager.SetActiveScene(_scene);
            yield return null;
            _context = Find<NomadFoundationContext>();
            _view = Find<NomadStairTraversalView>();
            _read = _context.ExecuteCommand(new GetStairTraversalStateCommand());
            Assert.That(_read.CurrentValue.Phase, Is.Not.EqualTo(StairTraversalPhase.Booting));
            // 每个测试使用同一明确起点；场景已打开时也不继承上一用例的路线或暂停。
            string reset = "{\"version\":1,\"position\":{\"x\":-2,\"y\":0.02,\"z\":5.6},\"yaw\":180," +
                "\"targetFloor\":0,\"moving\":false,\"paused\":false,\"waterMilliliters\":8000}";
            Assert.That(_context.ExecuteCommand(new RestoreStairTraversalCommand(reset)), Is.True);
            yield return null;
            _canBounds = MeasureCanBounds(_view.WaterCan);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_previous.IsValid() && _previous.isLoaded) SceneManager.SetActiveScene(_previous);
            if (_owns && _scene.IsValid() && _scene.isLoaded) yield return SceneManager.UnloadSceneAsync(_scene);
        }

        [UnityTest]
        public IEnumerator SameXZDestinations_RequireActualAscentAndDescent_ThroughEighteenPhysicalTreads()
        {
            Collider[] steps = _scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Collider>())
                .Where(c => c.name.Contains("Actual Tread")).ToArray();
            Assert.That(steps.Length, Is.EqualTo(18));
            float envelopeRadius = 0f;
            foreach (Vector3 corner in Corners(_canBounds))
            {
                Vector3 local = _view.Resident.transform.InverseTransformPoint(_view.WaterCan.TransformPoint(corner));
                envelopeRadius = Mathf.Max(envelopeRadius, new Vector2(local.x, local.z).magnitude);
            }
            Assert.That(NavMesh.GetSettingsByID(Find<NavMeshAgent>().agentTypeID).agentRadius,
                Is.GreaterThanOrEqualTo(envelopeRadius + .01f), "导航净空要包含完整水罐，而非只覆盖身体胶囊。");
            Assert.That(_view.Resident.Animator.applyRootMotion, Is.False);
            Assert.That(_context.ExecuteCommand(new MoveStairTraversalCommand(1)), Is.True);
            yield return null;
            Assert.That(_read.CurrentValue.Phase, Is.EqualTo(StairTraversalPhase.Moving),
                "上下目标 XZ 相同，不能在一层误判二层到达。");
            yield return WaitForArrival(1);
            Assert.That(_read.CurrentValue.Position.y, Is.EqualTo(3.2f).Within(.05f));
            Assert.That(_context.ExecuteCommand(new MoveStairTraversalCommand(0)), Is.True);
            yield return WaitForArrival(0);
            Assert.That(_read.CurrentValue.Position.y, Is.EqualTo(0f).Within(.05f));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator PauseOnStairs_FreezesBodyBucketHandFeetAndAnimator_ThenContinuesSameBucket()
        {
            yield return BeginAndWaitForStairs();
            _context.ExecuteCommand(new PauseStairTraversalCommand(true));
            yield return null; yield return null;
            Transform can = _view.WaterCan;
            int identity = can.GetInstanceID();
            Animator animator = _view.Resident.Animator;
            Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            Transform left = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform right = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            AssertPalmContact();
            Assert.That(_view.FootIK.LeftContactWeight + _view.FootIK.RightContactWeight, Is.GreaterThan(0f));
            AssertPlantedFeetReachTargets();
            Vector3 bodyPosition = _read.CurrentValue.Position, canPosition = can.position,
                handPosition = hand.position, leftPosition = left.position, rightPosition = right.position;
            float time = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            for (int i = 0; i < 10; i++) yield return null;
            Assert.That(_read.CurrentValue.Position, Is.EqualTo(bodyPosition));
            Assert.That(Vector3.Distance(can.position, canPosition), Is.LessThan(.00001f));
            Assert.That(Vector3.Distance(hand.position, handPosition), Is.LessThan(.00001f));
            Assert.That(Vector3.Distance(left.position, leftPosition), Is.LessThan(.00001f));
            Assert.That(Vector3.Distance(right.position, rightPosition), Is.LessThan(.00001f));
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).normalizedTime, Is.EqualTo(time).Within(.00001f));
            _context.ExecuteCommand(new PauseStairTraversalCommand(false));
            double deadline = Time.realtimeSinceStartupAsDouble + 3;
            while (Vector3.Distance(_read.CurrentValue.Position, bodyPosition) < .1f && Time.realtimeSinceStartupAsDouble < deadline)
                yield return null;
            Assert.That(Vector3.Distance(_read.CurrentValue.Position, bodyPosition), Is.GreaterThan(.1f));
            Assert.That(_view.WaterCan.GetInstanceID(), Is.EqualTo(identity));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator CancelAndRestoreOnStairs_KeepHeightAndWater_ReplaceOldMotorWithoutDuplicatingProp()
        {
            yield return BeginAndWaitForStairs();
            _context.ExecuteCommand(new PauseStairTraversalCommand(true));
            string checkpoint = _context.ExecuteCommand(new CaptureStairTraversalCommand());
            Vector3 saved = _read.CurrentValue.Position;
            int canId = _view.WaterCan.GetInstanceID();
            _context.ExecuteCommand(new CancelStairTraversalCommand());
            for (int i = 0; i < 8; i++) yield return null;
            Assert.That(_read.CurrentValue.Position, Is.EqualTo(saved));
            Assert.That(_read.CurrentValue.Phase, Is.EqualTo(StairTraversalPhase.Cancelled));
            Assert.That(_context.ExecuteCommand(new MoveStairTraversalCommand(0)), Is.True);
            double deadline = Time.realtimeSinceStartupAsDouble + 4;
            while (Vector3.Distance(_read.CurrentValue.Position, saved) < .3f && Time.realtimeSinceStartupAsDouble < deadline)
                yield return null;
            Assert.That(Vector3.Distance(_read.CurrentValue.Position, saved), Is.GreaterThan(.3f));
            Assert.That(_context.ExecuteCommand(new RestoreStairTraversalCommand(checkpoint)), Is.True);
            yield return null; yield return null;
            Assert.That(Vector3.Distance(_read.CurrentValue.Position, saved), Is.LessThan(.025f));
            Assert.That(_read.CurrentValue.Paused, Is.True);
            Assert.That(_read.CurrentValue.TargetFloor, Is.EqualTo(1));
            Assert.That(_view.WaterCan.GetInstanceID(), Is.EqualTo(canId));
            Assert.That(_scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<CharacterController>()).Count(), Is.EqualTo(1));
            _context.ExecuteCommand(new PauseStairTraversalCommand(false));
            yield return WaitForArrival(1);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator InvalidCheckpoint_DoesNotCancelCurrentRouteOrChangeWater()
        {
            Assert.That(_context.ExecuteCommand(new MoveStairTraversalCommand(1)), Is.True);
            string valid = _context.ExecuteCommand(new CaptureStairTraversalCommand());
            foreach (string invalid in new[] { "{bad-json", valid.Replace("8000", "9000"),
                         valid.Replace("\"version\":1", "\"version\":2"),
                         "{\"version\":1,\"position\":{\"x\":-2,\"y\":20,\"z\":5.6}," +
                         "\"yaw\":180,\"targetFloor\":1,\"moving\":true,\"waterMilliliters\":8000}" })
                Assert.That(_context.ExecuteCommand(new RestoreStairTraversalCommand(invalid)), Is.False, invalid);
            Assert.That(_read.CurrentValue.Phase, Is.EqualTo(StairTraversalPhase.Moving));
            Assert.That(_read.CurrentValue.WaterMilliliters, Is.EqualTo(8000));
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }

        private IEnumerator BeginAndWaitForStairs()
        {
            Assert.That(_context.ExecuteCommand(new MoveStairTraversalCommand(1)), Is.True);
            double deadline = Time.realtimeSinceStartupAsDouble + 35;
            while (_read.CurrentValue.Position.y < 1.15f && Time.realtimeSinceStartupAsDouble < deadline)
            {
                Assert.That(_read.CurrentValue.Phase, Is.EqualTo(StairTraversalPhase.Moving), _read.CurrentValue.Status);
                yield return null;
            }
            Assert.That(_read.CurrentValue.Position.y, Is.InRange(1.15f,2.2f), "必须实际走到中间踏面。");
        }

        private IEnumerator WaitForArrival(int floor)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 46;
            Vector3 previous = _read.CurrentValue.Position;
            while (_read.CurrentValue.Phase == StairTraversalPhase.Moving && Time.realtimeSinceStartupAsDouble < deadline)
            {
                // 取上一完成帧的身体、物品和 Animator 输出，避免 Update 与 IK 之间的错帧比较。
                yield return new WaitForFixedUpdate();
                StairTraversalState state = _read.CurrentValue;
                Assert.That(state.WaterMilliliters, Is.EqualTo(8000));
                Assert.That(Vector3.Distance(state.Position, previous), Is.LessThan(.55f), "正常搬运不能跨层瞬移。");
                Assert.That(state.Position.y, Is.InRange(-.05f,3.3f));
                AssertPlantedFeetReachTargets();
                AssertPalmContact();
                Collider[] overlaps = Physics.OverlapBox(_view.WaterCan.TransformPoint(_canBounds.center),
                    _canBounds.extents, _view.WaterCan.rotation, ~0, QueryTriggerInteraction.Ignore);
                Assert.That(overlaps.Where(c => c.gameObject.scene == _scene &&
                    c.enabled).Select(c => c.name), Is.Empty, "水罐的完整携行体积不能穿过踏面、楼板或扶手。");
                previous = state.Position;
            }
            Assert.That(_read.CurrentValue.Phase, Is.EqualTo(StairTraversalPhase.Arrived),
                $"floor={floor} pos={_read.CurrentValue.Position} status={_read.CurrentValue.Status}");
            Assert.That(_read.CurrentValue.TargetFloor, Is.EqualTo(floor));
        }

        private T Find<T>() where T : Component =>
            _scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>()).Single();

        private void AssertPalmContact()
        {
            var contact = _view.Resident.Animator.GetComponent<FoundationResidentCarryIK>();
            Transform palm = _view.WaterCan.Find(FoundationWaterCanVisualFactory.PalmTargetName);
            Assert.That(Vector3.Distance(contact.RightPalmContactPosition, palm.position), Is.LessThan(.015f));
            Assert.That(Quaternion.Angle(contact.RightPalmRotation, palm.rotation), Is.LessThan(4f));
        }

        private static Bounds MeasureCanBounds(Transform can)
        {
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            foreach (MeshFilter mesh in can.GetComponentsInChildren<MeshFilter>(true))
            foreach (Vector3 corner in Corners(mesh.sharedMesh.bounds))
                bounds.Encapsulate(can.InverseTransformPoint(mesh.transform.TransformPoint(corner)));
            return bounds;
        }

        private static System.Collections.Generic.IEnumerable<Vector3> Corners(Bounds bounds)
        {
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
                yield return bounds.center + Vector3.Scale(bounds.extents, new Vector3(x,y,z));
        }

        private void AssertPlantedFeetReachTargets()
        {
            ResidentStairFootIK foot = _view.FootIK;
            if (foot.LeftContactWeight > .98f)
                Assert.That(foot.LeftTargetError, Is.LessThan(.045f), $"左支撑脚未够到目标，body={_read.CurrentValue.Position}");
            if (foot.RightContactWeight > .98f)
                Assert.That(foot.RightTargetError, Is.LessThan(.045f), $"右支撑脚未够到目标，body={_read.CurrentValue.Position}");
        }
    }
}
#endif
