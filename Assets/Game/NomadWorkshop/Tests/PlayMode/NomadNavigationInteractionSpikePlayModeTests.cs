using System.Collections;
using Game.NomadWorkshop.Navigation;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.NomadWorkshop.PlayMode.Tests
{
    /// <summary>验证连续路径、局部避让、候选停靠位互斥和真实物品交接穿过同一框架组合根。</summary>
    public sealed class NomadNavigationInteractionSpikePlayModeTests
    {
        private Scene _previousScene;
        private Scene _testScene;
        private NavigationInteractionSpikeComposition _composition;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _previousScene = SceneManager.GetActiveScene();
            _testScene = SceneManager.CreateScene("NomadNavigationInteractionSpikePlayModeTest");
            Assert.That(SceneManager.SetActiveScene(_testScene), Is.True);
            _composition = NomadNavigationSpikeRuntimeFactory.Create(fastMode: true);

            NavMeshSurface surface = _composition.Root.GetComponentInChildren<NavMeshSurface>();
            foreach (NavMeshAgent agent in _composition.Root.GetComponentsInChildren<NavMeshAgent>(true))
                Assert.That(agent.agentTypeID, Is.EqualTo(surface.agentTypeID),
                    $"{agent.name} 必须使用当前 Surface 的 Agent 类型，不能依赖编辑器上次选择的类型。");

            const int readyFrameLimit = 180;
            for (var i = 0;
                 i < readyFrameLimit &&
                 !_composition.Model.IsReady.Value &&
                 _composition.Model.Phase.Value != NavigationInteractionSpikePhase.Blocked;
                 i++)
                yield return null;

            Assert.That(
                _composition.Model.Phase.Value,
                Is.Not.EqualTo(NavigationInteractionSpikePhase.Blocked),
                _composition.Model.LastBlocker.Value);
            Assert.That(_composition.Model.IsReady.Value, Is.True, "Harness 应在限定帧内完成 NavMesh 构建。");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_composition?.Root != null) Object.Destroy(_composition.Root);
            yield return null;

            if (_previousScene.IsValid() && _previousScene.isLoaded)
                SceneManager.SetActiveScene(_previousScene);
            if (_testScene.IsValid() && _testScene.isLoaded)
            {
                AsyncOperation unload = SceneManager.UnloadSceneAsync(_testScene);
                while (unload != null && !unload.isDone) yield return null;
            }
        }

        [UnityTest]
        public IEnumerator Paths_OpenSpaceIsStraight_RotatedCabinetRequiresDetour()
        {
            Assert.That(_composition.Model.OpenPathLengthRatio.Value, Is.InRange(0.999f, 1.03f));
            Assert.That(_composition.Model.OpenPathCorners.Value, Is.GreaterThanOrEqualTo(2));
            Assert.That(_composition.Model.ObstaclePathLengthRatio.Value, Is.GreaterThan(1.005f));
            Assert.That(_composition.Model.ObstaclePathCorners.Value, Is.GreaterThanOrEqualTo(3));
            yield return null;
        }

        [UnityTest]
        public IEnumerator TwoResidents_CrossAndSerializeOneCabinetGroup_ThenCarryRealItems()
        {
            const int completionFrameLimit = 900;
            for (var i = 0;
                 i < completionFrameLimit &&
                 _composition.Model.Phase.Value is not
                     (NavigationInteractionSpikePhase.Completed or NavigationInteractionSpikePhase.Blocked);
                 i++)
                yield return null;

            Assert.That(
                _composition.Model.Phase.Value,
                Is.EqualTo(NavigationInteractionSpikePhase.Completed),
                _composition.Model.LastBlocker.Value);
            Assert.That(_composition.Model.CrossingCompleted.Value, Is.True);
            Assert.That(_composition.Model.MinimumAgentSeparation.Value, Is.GreaterThan(0.1f));
            Assert.That(
                _composition.Model.ReservationContentionCount.Value,
                Is.EqualTo(2),
                "居民 B 应在 A 接近柜门时，以及物品交接后但关门前各被互斥一次。");
            Assert.That(_composition.Model.CompletedInteractionCount.Value, Is.EqualTo(2));
            Assert.That(_composition.Model.ResidentASelectedSlot.Value, Is.Not.Empty);
            Assert.That(_composition.Model.ResidentBSelectedSlot.Value, Is.Not.Empty);
            Assert.That(_composition.Model.CabinetItemCount.Value, Is.Zero);
            Assert.That(_composition.Model.ResidentAItemCount.Value, Is.EqualTo(1));
            Assert.That(_composition.Model.ResidentBItemCount.Value, Is.EqualTo(1));
            Assert.That(_composition.Model.DoorOpenProgress.Value, Is.Zero.Within(0.001f));
        }
    }
}
