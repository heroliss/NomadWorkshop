#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.NomadWorkshop.Foundation;
using Game.NomadWorkshop.Navigation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.NomadWorkshop.PlayMode.Tests
{
    /// <summary>继承整路径手脚/罐体/楼板碰撞检查，额外验证真实中间平台转身及恢复。</summary>
    public sealed class NomadSwitchbackStairPlayModeTests : NomadStairTraversalPlayModeTests
    {
        protected override string ScenePath => "Assets/Game/NomadWorkshop/Scenes/SwitchbackCarrySpike.unity";

        [UnityTest]
        public IEnumerator TurningLanding_PauseCancelAndRestorePreserveSameCan_ThenReachOtherFlight()
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            var context = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<NomadFoundationContext>()).Single();
            var view = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<NomadStairTraversalView>()).Single();
            var read = context.ExecuteCommand(new GetStairTraversalStateCommand());
            var trail = new Trail { scene = ScenePath, startedUtc = DateTime.UtcNow.ToString("O") };
            bool firstFlight = false, secondFlight = false;
            int canId = view.WaterCan.GetInstanceID();
            Assert.That(context.ExecuteCommand(new MoveStairTraversalCommand(1)), Is.True);
            double deadline = Time.realtimeSinceStartupAsDouble + 42;
            try
            {
                while (Time.realtimeSinceStartupAsDouble < deadline)
                {
                    yield return new WaitForFixedUpdate();
                    StairTraversalState state = read.CurrentValue;
                    Sample(state);
                    Assert.That(state.Phase, Is.EqualTo(StairTraversalPhase.Moving), state.Status);
                    if (state.Position.y > .3f && state.Position.y < 1.4f && state.Position.x < 1.6f)
                        firstFlight = true;
                    if (state.Position.y >= 1.55f && state.Position.y <= 1.68f &&
                        state.Position.x >= 1.65f && state.Position.x <= 2.65f && state.Position.z < -.45f) break;
                }
                Assert.That(firstFlight, Is.True, "必须通过第一段实际踏板。");
                Vector3 atTurn = read.CurrentValue.Position;
                Assert.That(atTurn.y, Is.InRange(1.55f, 1.68f));
                Assert.That(atTurn.x, Is.InRange(1.65f, 2.65f), "必须到达两梯段之间，不能在第一段顶部提前通过。");
                context.ExecuteCommand(new PauseStairTraversalCommand(true));
                yield return null; yield return null;
                string saved = context.ExecuteCommand(new CaptureStairTraversalCommand());
                float yaw = read.CurrentValue.Yaw;
                Vector3 palm = view.Resident.Animator.GetComponent<FoundationResidentCarryIK>().RightPalmContactPosition;
                for (int i = 0; i < 6; i++) yield return null;
                Assert.That(read.CurrentValue.Position, Is.EqualTo(atTurn));
                Assert.That(read.CurrentValue.Yaw, Is.EqualTo(yaw));
                Assert.That(Vector3.Distance(palm, view.Resident.Animator.GetComponent<FoundationResidentCarryIK>().RightPalmContactPosition),
                    Is.LessThan(.00001f));
                context.ExecuteCommand(new CancelStairTraversalCommand());
                Assert.That(read.CurrentValue.Phase, Is.EqualTo(StairTraversalPhase.Cancelled));
                Assert.That(context.ExecuteCommand(new RestoreStairTraversalCommand(saved)), Is.True);
                yield return null; yield return null;
                Assert.That(read.CurrentValue.Paused, Is.True);
                Assert.That(Vector3.Distance(read.CurrentValue.Position, atTurn), Is.LessThan(.025f));
                Assert.That(view.WaterCan.GetInstanceID(), Is.EqualTo(canId));
                Assert.That(scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<CharacterController>()).Count(), Is.EqualTo(1));
                context.ExecuteCommand(new PauseStairTraversalCommand(false));
                deadline = Time.realtimeSinceStartupAsDouble + 42;
                while (read.CurrentValue.Phase == StairTraversalPhase.Moving && Time.realtimeSinceStartupAsDouble < deadline)
                {
                    yield return new WaitForFixedUpdate();
                    StairTraversalState state = read.CurrentValue;
                    Sample(state);
                    if (state.Position.y > 1.85f && state.Position.y < 3.05f && state.Position.x > 2.7f)
                        secondFlight = true;
                    Assert.That(state.WaterMilliliters, Is.EqualTo(8000));
                    Assert.That(view.WaterCan.GetInstanceID(), Is.EqualTo(canId));
                }
                Assert.That(secondFlight, Is.True, "必须折返进入另一梯段，不能依靠瞬移或旁路到达。");
                Assert.That(read.CurrentValue.Phase, Is.EqualTo(StairTraversalPhase.Arrived), read.CurrentValue.Status);
                Assert.That(read.CurrentValue.Position.y, Is.EqualTo(3.2f).Within(.05f));
                trail.status = "passed";
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Directory.CreateDirectory("Logs/AIValidation/nomad-warm-art");
                File.WriteAllText("Logs/AIValidation/nomad-warm-art/switchback-turn-trail.json", JsonUtility.ToJson(trail, true));
            }

            void Sample(StairTraversalState state)
            {
                if (trail.points.Count == 0 || Vector3.Distance(trail.points[trail.points.Count - 1].position, state.Position) > .04f)
                    trail.points.Add(new Point { position = state.Position, yaw = state.Yaw, phase = state.Phase.ToString() });
            }
        }

        [Serializable] private sealed class Trail
        {
            public string scene, startedUtc, status = "incomplete";
            public List<Point> points = new();
        }
        [Serializable] private sealed class Point { public Vector3 position; public float yaw; public string phase; }
    }
}
#endif
