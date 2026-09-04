using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.NomadWorkshop.Editor.Tests
{
    /// <summary>锁定 Foundation 场景只经 Editor 生成并接到当前玩法 Prefab 与居民表现资产。</summary>
    public sealed class NomadFoundationResourceFlowSpikePipelineTests
    {
        [Test]
        public void GeneratedScene_WiresKitchenHumanoidAndAnimatorAssets()
        {
            SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(
                NomadFoundationResourceFlowSpikePipeline.ScenePath);
            Assert.IsNotNull(sceneAsset, "应先由项目菜单生成实体物流 Foundation 场景。 ");

            Scene scene = SceneManager.GetSceneByPath(NomadFoundationResourceFlowSpikePipeline.ScenePath);
            bool openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
            {
                scene = EditorSceneManager.OpenScene(
                    NomadFoundationResourceFlowSpikePipeline.ScenePath,
                    OpenSceneMode.Additive);
            }

            try
            {
                FoundationResourceFlowSpikeController controller = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    FoundationResourceFlowSpikeController candidate =
                        root.GetComponentInChildren<FoundationResourceFlowSpikeController>(true);
                    if (candidate == null) continue;
                    Assert.IsNull(controller, "Foundation 场景不应出现多个资源流入口。 ");
                    controller = candidate;
                }

                Assert.IsNotNull(controller);
                var serialized = new SerializedObject(controller);
                Assert.AreSame(
                    AssetDatabase.LoadAssetAtPath<GameObject>(NomadFieldKitchenPrototypePipeline.PrefabPath),
                    serialized.FindProperty("fieldKitchenPrefab").objectReferenceValue);
                Assert.AreSame(
                    AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/Game/NomadWorkshop/ThirdParty/QuaterniusUniversalBaseCharacters/" +
                        "Superhero_Male_FullBody.fbx"),
                    serialized.FindProperty("humanoidPrefab").objectReferenceValue);
                Assert.AreSame(
                    AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                        "Assets/Game/NomadWorkshop/Animation/NomadResident.controller"),
                    serialized.FindProperty("humanoidController").objectReferenceValue);
            }
            finally
            {
                if (openedForTest) EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
