using Game.NomadWorkshop.Foundation;
using Game.NomadWorkshop.Navigation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.NomadWorkshop.Editor.Tests
{
    /// <summary>锁定生成场景的 Framework 分层、稳定玩法根与三个互斥候选位。</summary>
    public sealed class NomadNavigationInteractionSpikePipelineTests
    {
        [Test]
        public void GeneratedScene_SeparatesUnscaledFacilityRootFromGeometryAndSlots()
        {
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SceneAsset>(
                NomadNavigationInteractionSpikePipeline.ScenePath));

            Scene scene = SceneManager.GetSceneByPath(NomadNavigationInteractionSpikePipeline.ScenePath);
            bool openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
            {
                scene = EditorSceneManager.OpenScene(
                    NomadNavigationInteractionSpikePipeline.ScenePath,
                    OpenSceneMode.Additive);
            }

            try
            {
                NomadFoundationContext context = FindOne<NomadFoundationContext>(scene);
                NomadNavigationSpikeModel model = FindOne<NomadNavigationSpikeModel>(scene);
                NomadNavigationSpikeSystem system = FindOne<NomadNavigationSpikeSystem>(scene);
                DeckNavigationUtility utility = FindOne<DeckNavigationUtility>(scene);
                NomadNavigationSpikeView view = FindOne<NomadNavigationSpikeView>(scene);
                FacilityInteractionGroup group = FindOne<FacilityInteractionGroup>(scene);

                Assert.IsNotNull(context);
                Assert.IsNotNull(model);
                Assert.IsNotNull(system);
                Assert.IsNotNull(utility);
                Assert.IsNotNull(view);
                Assert.IsNotNull(group);
                Assert.That(model.transform.IsChildOf(context.transform), Is.True);
                Assert.That(system.transform.IsChildOf(context.transform), Is.True);
                Assert.That(utility.transform.IsChildOf(context.transform), Is.True);
                Assert.That(view.transform.IsChildOf(context.transform), Is.True);

                Assert.That(group.transform.localScale, Is.EqualTo(Vector3.one));
                Transform body = group.transform.Find("Static Body + Navigation Collider");
                Assert.IsNotNull(body);
                Assert.That(body.localScale, Is.EqualTo(new Vector3(2.8f, 1.44f, 1.7f)));
                Assert.That(group.AlternativeSlots.Count, Is.EqualTo(3));
                Assert.That(group.ReservationKey, Is.EqualTo("facility-interaction:cabinet-storage-door"));
                Assert.That(group.AlternativeSlots[0].SlotId, Is.EqualTo("left"));
                Assert.That(group.AlternativeSlots[1].SlotId, Is.EqualTo("center"));
                Assert.That(group.AlternativeSlots[2].SlotId, Is.EqualTo("right"));
                for (var i = 0; i < group.AlternativeSlots.Count; i++)
                {
                    Assert.That(group.AlternativeSlots[i].transform.localScale, Is.EqualTo(Vector3.one));
                    Assert.That(group.AlternativeSlots[i].WorldPosition.y, Is.EqualTo(0f).Within(0.001f));
                }

                var utilitySerialized = new SerializedObject(utility);
                Assert.IsNotNull(utilitySerialized.FindProperty("surface").objectReferenceValue);
                Assert.That(utilitySerialized.FindProperty("agentBindings").arraySize, Is.EqualTo(2));
                Assert.That(
                    context.GetComponentsInChildren<NavigationSpikeTint>(true).Length,
                    Is.GreaterThan(10),
                    "颜色契约必须进入可序列化组件，不能只依赖不会保存的 MaterialPropertyBlock。");

                Camera camera = context.GetComponentInChildren<Camera>(true);
                Assert.IsNotNull(camera);
                Component cameraData = camera.GetComponent("UniversalAdditionalCameraData");
                Assert.IsNotNull(cameraData, "隔离场景必须显式选择 3D Universal Renderer。");
                SerializedProperty rendererIndex = new SerializedObject(cameraData)
                    .FindProperty("m_RendererIndex");
                Assert.IsNotNull(rendererIndex);
                Assert.That(rendererIndex.intValue, Is.GreaterThanOrEqualTo(1));
            }
            finally
            {
                if (openedForTest) EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static T FindOne<T>(Scene scene) where T : Component
        {
            T found = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T candidate = root.GetComponentInChildren<T>(true);
                if (candidate == null) continue;
                Assert.IsNull(found, $"场景中不应出现多个 {typeof(T).Name}。");
                found = candidate;
            }
            return found;
        }
    }
}
