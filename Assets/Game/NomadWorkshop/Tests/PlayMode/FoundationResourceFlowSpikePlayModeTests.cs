using System.Collections;
using Game.NomadWorkshop.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.NomadWorkshop.PlayMode.Tests
{
    /// <summary>证明资源流内核可以驱动可观察的居民搬运、厨房加工与程序门表现。</summary>
    public sealed class FoundationResourceFlowSpikePlayModeTests
    {
        private Scene _previousScene;
        private Scene _testScene;
        private GameObject _root;
        private FoundationResourceFlowSpikeController _controller;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _previousScene = SceneManager.GetActiveScene();
            _testScene = SceneManager.CreateScene("NomadWorkshopFoundationResourceFlowTest");
            Assert.IsTrue(SceneManager.SetActiveScene(_testScene));

            _root = new GameObject("Foundation Resource Flow Test");
            _controller = _root.AddComponent<FoundationResourceFlowSpikeController>();
            _controller.ConfigureTimingsForTests(80f, 80f);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_root != null) Object.Destroy(_root);
            yield return null;

            if (_previousScene.IsValid() && _previousScene.isLoaded)
                Assert.IsTrue(SceneManager.SetActiveScene(_previousScene));
            if (_testScene.IsValid() && _testScene.isLoaded)
            {
                AsyncOperation unload = SceneManager.UnloadSceneAsync(_testScene);
                while (unload != null && !unload.isDone) yield return null;
            }
        }

        [UnityTest]
        public IEnumerator OneBatch_VisiblyCarriesThenProducesMealAndWasteWater()
        {
            Assert.IsTrue(_controller.HasGeneratedWorld);
            Assert.IsTrue(_controller.HasGeneratedResident);
            Assert.IsTrue(_controller.HasKitchen);
            Assert.IsFalse(_controller.UsesParametricKitchen,
                "隔离测试未注入项目资产时应证明程序厨房回退仍然可用。 ");

            bool observedCargo = _controller.HasVisibleCargo;
            float maximumDoorOpen = _controller.CenterDoorOpenAmount;
            const int frameLimit = 90;
            for (int i = 0; i < frameLimit &&
                            _controller.Phase != FoundationResourceFlowSpikeController.SpikePhase.Completed &&
                            _controller.Phase != FoundationResourceFlowSpikeController.SpikePhase.Blocked;
                 i++)
            {
                yield return null;
                observedCargo |= _controller.HasVisibleCargo;
                maximumDoorOpen = Mathf.Max(maximumDoorOpen, _controller.CenterDoorOpenAmount);
            }

            Assert.AreEqual(FoundationResourceFlowSpikeController.SpikePhase.Completed, _controller.Phase);
            Assert.IsTrue(observedCargo, "至少一帧应能看见居民携带的真实资源表现。 ");
            Assert.AreEqual(1, _controller.CompletedBatchCount);
            Assert.AreEqual(2, _controller.PantryFood);
            Assert.AreEqual(2, _controller.WaterTankAmount);
            Assert.AreEqual(0, _controller.KitchenMealOutput);
            Assert.AreEqual(0, _controller.KitchenWasteOutput);
            Assert.AreEqual(1, _controller.PreparedMeals);
            Assert.AreEqual(1, _controller.WasteWater);
            Assert.IsFalse(_controller.HasVisibleCargo);
            Assert.Greater(maximumDoorOpen, 0.01f, "厨房门应由行动阶段驱动打开，而不是依赖动画结算。 ");
            Assert.IsFalse(_controller.LastBlocker.IsBlocked);
            Assert.AreEqual(1, CountCamerasInScene(_testScene));
        }

        [UnityTest]
        public IEnumerator ThirdBatch_LeavesWasteAtKitchenWhenFinalTankIsFull()
        {
            yield return RunUntilSettled();
            Assert.IsTrue(_controller.RequestNextBatch());
            yield return RunUntilSettled();
            Assert.IsTrue(_controller.RequestNextBatch());
            yield return RunUntilSettled();

            Assert.AreEqual(FoundationResourceFlowSpikeController.SpikePhase.Blocked, _controller.Phase);
            Assert.AreEqual(ResourceFlowBlockReason.DestinationFull, _controller.LastBlocker.Reason);
            Assert.AreEqual("vehicle-waste-tank", _controller.LastBlocker.InventoryId);
            Assert.AreEqual(3, _controller.PreparedMeals,
                "餐食已先经本地输出口完成实体清运，不应因随后污水阻塞而回滚。 ");
            Assert.AreEqual(2, _controller.WasteWater);
            Assert.AreEqual(1, _controller.KitchenWasteOutput,
                "无法清运的污水必须留在厨房输出口，不能消失或瞬移。 ");
            Assert.AreEqual(0, _controller.KitchenMealOutput);
            Assert.IsFalse(_controller.HasVisibleCargo,
                "目的容量在任务领取前失败，居民不应先把污水拿到手上。 ");
            Assert.IsFalse(_controller.RequestNextBatch(), "阻塞证据未处理前不能覆盖为下一批任务。 ");
        }

        [UnityTest]
        public IEnumerator WaterSanitationCycle_KeepsWaterAndHumanWasteObservableAcrossEveryStage()
        {
            yield return RunUntilSettled();
            Assert.IsTrue(_controller.RequestWaterSanitationCycle());

            bool observedBodyWater = false;
            bool observedBladderWaste = false;
            bool observedToiletWaste = false;
            bool observedCarriedWaste = false;
            float maximumPressure = 0f;
            const int frameLimit = 120;
            for (int i = 0; i < frameLimit &&
                            _controller.Phase != FoundationResourceFlowSpikeController.SpikePhase.Completed &&
                            _controller.Phase != FoundationResourceFlowSpikeController.SpikePhase.Blocked;
                 i++)
            {
                yield return null;
                observedBodyWater |= _controller.BodyWater > 0;
                observedBladderWaste |= _controller.BladderWaste > 0;
                observedToiletWaste |= _controller.ToiletHoldingWaste > 0;
                observedCarriedWaste |= _controller.ResidentCarriedHumanWaste > 0;
                maximumPressure = Mathf.Max(maximumPressure, _controller.ExcretionPressure);
            }

            Assert.AreEqual(FoundationResourceFlowSpikeController.SpikePhase.Completed, _controller.Phase);
            Assert.AreEqual(1, _controller.CompletedSanitationCycleCount);
            Assert.IsTrue(observedBodyWater, "喝下的水应先存在于居民体内，不能直接瞬移成废物。 ");
            Assert.IsTrue(observedBladderWaste, "代谢后应能观察到膀胱中的真实排泄物。 ");
            Assert.IsTrue(observedToiletWaste, "如厕后排泄物应先进入厕所暂存桶。 ");
            Assert.IsTrue(observedCarriedWaste, "厕所暂存桶必须由居民实体清运到车辆废物罐。 ");
            Assert.Greater(maximumPressure, 0.01f, "离散代谢提交前后都应提供连续排泄压力。 ");
            Assert.AreEqual(1, _controller.WaterTankAmount);
            Assert.AreEqual(1, _controller.WasteWater);
            Assert.AreEqual(1, _controller.HumanWaste);
            Assert.AreEqual(2, _controller.VehicleWasteTotal);
            Assert.AreEqual(0, _controller.DrinkingStationWater);
            Assert.AreEqual(0, _controller.BodyWater);
            Assert.AreEqual(0, _controller.BladderWaste);
            Assert.AreEqual(0, _controller.ToiletHoldingWaste);
            Assert.AreEqual(0, _controller.ResidentCarriedHumanWaste);
            Assert.IsFalse(_controller.LastBlocker.IsBlocked);
            Assert.IsFalse(_controller.RequestWaterSanitationCycle(), "当前 Foundation 只演示一次完整水循环。 ");
        }

        private IEnumerator RunUntilSettled()
        {
            const int frameLimit = 90;
            for (int i = 0; i < frameLimit &&
                            _controller.Phase != FoundationResourceFlowSpikeController.SpikePhase.Completed &&
                            _controller.Phase != FoundationResourceFlowSpikeController.SpikePhase.Blocked;
                 i++)
            {
                yield return null;
            }
        }

        private static int CountCamerasInScene(Scene scene)
        {
            int count = 0;
            Camera[] cameras = Object.FindObjectsByType<Camera>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i].gameObject.scene == scene) count++;
            }
            return count;
        }
    }
}
