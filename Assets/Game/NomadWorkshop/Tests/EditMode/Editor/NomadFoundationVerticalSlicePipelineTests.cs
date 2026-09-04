using Game.NomadWorkshop.Foundation;
using Game.NomadWorkshop.Navigation;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.NomadWorkshop.Editor.Tests
{
    public sealed class NomadFoundationVerticalSlicePipelineTests
    {
        [Test]
        public void GeneratedScene_UsesFrameworkMonoLayersAndSharedDefinitions()
        {
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<DeckLayoutDefinition>(
                NomadFoundationVerticalSlicePipeline.DeckLayoutPath));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SceneAsset>(
                NomadFoundationVerticalSlicePipeline.ScenePath));

            Scene scene = SceneManager.GetSceneByPath(NomadFoundationVerticalSlicePipeline.ScenePath);
            bool openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
            {
                scene = EditorSceneManager.OpenScene(
                    NomadFoundationVerticalSlicePipeline.ScenePath,
                    OpenSceneMode.Additive);
            }
            try
            {
                NomadFoundationContext context = FindOne<NomadFoundationContext>(scene);
                NomadFoundationModel model = FindOne<NomadFoundationModel>(scene);
                NomadFoundationSystem system = FindOne<NomadFoundationSystem>(scene);
                NomadFoundationWorldView worldView = FindOne<NomadFoundationWorldView>(scene);
                NomadFoundationDebugView debugView = FindOne<NomadFoundationDebugView>(scene);
                DeckNavigationUtility navigation = FindOne<DeckNavigationUtility>(scene);
                NavMeshSurface surface = FindOne<NavMeshSurface>(scene);

                Assert.IsNotNull(context);
                Assert.IsNotNull(model);
                Assert.IsNotNull(system);
                Assert.IsNotNull(worldView);
                Assert.IsNotNull(debugView);
                Assert.IsNotNull(navigation);
                Assert.IsNotNull(surface);
                Assert.That(model.transform.IsChildOf(context.transform), Is.True);
                Assert.That(system.transform.IsChildOf(context.transform), Is.True);
                Assert.That(worldView.transform.IsChildOf(context.transform), Is.True);
                Assert.That(debugView.transform.IsChildOf(context.transform), Is.True);
                Assert.That(navigation.transform.IsChildOf(context.transform), Is.True);
                Assert.That(surface.transform, Is.SameAs(navigation.transform));
                Assert.IsNotNull(
                    navigation.transform.Find("Nav Source · Walkable Deck")
                        ?.GetComponent<BoxCollider>(),
                    "正式切片应有独立、不受表现 Mesh 影响的 NavMesh 物理构建输入。");
                Assert.IsNotNull(
                    navigation.transform.Find("Runtime Facility Navigation Obstacles"),
                    "设施候选障碍应有可整体停用的运行期所有权根。");

                var systemSerialized = new SerializedObject(system);
                var viewSerialized = new SerializedObject(worldView);
                Assert.AreSame(
                    systemSerialized.FindProperty("deckLayout").objectReferenceValue,
                    viewSerialized.FindProperty("deckLayout").objectReferenceValue,
                    "逻辑与表现必须引用同一份甲板布局，避免边界与可选网格漂移。");
                Assert.That(systemSerialized.FindProperty("facilityDefinitions").arraySize, Is.EqualTo(4));
                Assert.That(viewSerialized.FindProperty("facilityDefinitions").arraySize, Is.EqualTo(4));
                for (var i = 0; i < 4; i++)
                {
                    Assert.AreSame(
                        systemSerialized.FindProperty("facilityDefinitions")
                            .GetArrayElementAtIndex(i).objectReferenceValue,
                        viewSerialized.FindProperty("facilityDefinitions")
                            .GetArrayElementAtIndex(i).objectReferenceValue,
                        $"逻辑与表现的设施定义第 {i} 项必须是同一资产。 ");

                    var definition = (NomadFacilityDefinition)systemSerialized
                        .FindProperty("facilityDefinitions")
                        .GetArrayElementAtIndex(i)
                        .objectReferenceValue;
                    Assert.That(definition.CreateFootprint().Parts.Count, Is.GreaterThan(0));
                    Assert.That(definition.InteractionGroups.Count, Is.GreaterThan(0));
                }
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
                Assert.IsNull(found, $"场景中不应出现多个 {typeof(T).Name}。 ");
                found = candidate;
            }
            return found;
        }
    }
}
