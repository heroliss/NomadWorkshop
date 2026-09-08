#if UNITY_EDITOR
using System.Linq;
using Game.NomadWorkshop.Foundation;
using Game.NomadWorkshop.Navigation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using System.Collections;

namespace Game.NomadWorkshop.PlayMode.Tests
{
    /// <summary>NW15 紧凑单人陡梯：复用持罐行为断言，增加尺寸和单人占用验收。</summary>
    public sealed class NomadCompactSteepStairPlayModeTests : NomadStairTraversalPlayModeTests
    {
        protected override string ScenePath =>
            "Assets/Game/NomadWorkshop/Scenes/CompactSteepCarrySpike.unity";

        protected override int ExpectedTreadCount => 16;

        [UnityTest]
        public IEnumerator CompactGeometry_IsOnePersonSteepAndGuarded()
        {
            yield return null;
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(ScenePath);
            var treads = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Collider>())
                .Where(c => c.name.Contains("Actual Tread")).OrderBy(c => c.bounds.max.y).ToArray();
            Assert.That(treads.Length, Is.EqualTo(16));
            Assert.That(treads[0].bounds.size.x, Is.EqualTo(1.48f).Within(.02f));
            Assert.That(treads.All(c => c is MeshCollider), Is.True, "使用薄折边踏板的实际网格碰撞。");
            for (int i = 1; i < treads.Length; i++)
                Assert.That(treads[i].bounds.max.y - treads[i - 1].bounds.max.y,
                    Is.EqualTo(.2f).Within(.002f));
            Assert.That(treads[0].bounds.size.z, Is.EqualTo(.25f).Within(.02f));
            float pitch = Mathf.Rad2Deg * Mathf.Atan2(3.2f, 16f * .25f);
            Assert.That(pitch, Is.InRange(38f, 40f));
            Assert.That(scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<NomadStairTrafficGate>())
                .Single().IsOccupied, Is.False);
        }

        [UnityTest]
        public IEnumerator SingleLaneGate_RejectsSecondOwnerUntilRelease()
        {
            yield return null;
            var gate = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(ScenePath)
                .GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<NomadStairTrafficGate>())
                .Single();
            Assert.That(gate.TryAcquire(101UL), Is.True);
            Assert.That(gate.CurrentOwner, Is.EqualTo(101UL));
            Assert.That(gate.TryAcquire(101UL), Is.True, "原持有人可改选目的地。");
            Assert.That(gate.TryAcquire(0UL), Is.False);
            Assert.That(gate.TryAcquire(202UL), Is.False);
            Assert.That(gate.Release(202UL), Is.False);
            Assert.That(gate.CurrentOwner, Is.EqualTo(101UL));
            Assert.That(gate.Release(101UL), Is.True);
            Assert.That(gate.IsOccupied, Is.False);
            Assert.That(gate.TryAcquire(202UL), Is.True);
            Assert.That(gate.Release(202UL), Is.True);
        }

        [UnityTest]
        public IEnumerator CancelAndRestoreOnFlight_KeepExclusiveTrafficUntilSafeArrival()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(ScenePath);
            var context = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<NomadFoundationContext>()).Single();
            var gate = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<NomadStairTrafficGate>()).Single();
            var state = context.ExecuteCommand(new GetStairTraversalStateCommand());
            Assert.That(context.ExecuteCommand(new MoveStairTraversalCommand(1)), Is.True);
            double deadline = Time.realtimeSinceStartupAsDouble + 25;
            while (state.CurrentValue.Position.y < .8f && Time.realtimeSinceStartupAsDouble < deadline)
                yield return null;
            Assert.That(state.CurrentValue.Position.y, Is.InRange(.8f, 2.5f));
            context.ExecuteCommand(new PauseStairTraversalCommand(true));
            Assert.That(gate.TryAcquire(202UL), Is.False);
            context.ExecuteCommand(new CancelStairTraversalCommand());
            string cancelled = context.ExecuteCommand(new CaptureStairTraversalCommand());
            Assert.That(gate.CurrentOwner, Is.EqualTo(1UL));
            Assert.That(gate.TryAcquire(202UL), Is.False);
            Assert.That(context.ExecuteCommand(new RestoreStairTraversalCommand(cancelled)), Is.True);
            Assert.That(gate.CurrentOwner, Is.EqualTo(1UL), "非 moving 的楼梯中途检查点也必须恢复占用。");
            Assert.That(context.ExecuteCommand(new MoveStairTraversalCommand(0)), Is.True);
            deadline = Time.realtimeSinceStartupAsDouble + 30;
            while (state.CurrentValue.Phase == StairTraversalPhase.Moving && Time.realtimeSinceStartupAsDouble < deadline)
                yield return null;
            Assert.That(state.CurrentValue.Phase, Is.EqualTo(StairTraversalPhase.Arrived));
            Assert.That(gate.IsOccupied, Is.False);
            Assert.That(gate.TryAcquire(202UL), Is.True);
            Vector3 arrived = state.CurrentValue.Position;
            Assert.That(context.ExecuteCommand(new MoveStairTraversalCommand(1)), Is.False);
            Assert.That(context.ExecuteCommand(new RestoreStairTraversalCommand(cancelled)), Is.False);
            Assert.That(state.CurrentValue.Position, Is.EqualTo(arrived));
            Assert.That(gate.CurrentOwner, Is.EqualTo(202UL));
            gate.Release(202UL);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
#endif
