using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.NomadWorkshop.Editor
{
    /// <summary>创建或更新实体物流 Foundation 灰盒场景，并接入当前参数化厨房与居民表现资产。</summary>
    public static class NomadFoundationResourceFlowSpikePipeline
    {
        public const string ScenePath =
            "Assets/Game/NomadWorkshop/Scenes/FoundationResourceFlowSpike.unity";
        public const string RootName = "NomadWorkshop Foundation Resource Flow Spike";

        [MenuItem("Assets/SSFramework/游牧工坊/Foundation/创建或打开实体物流灰盒")]
        public static void CreateOrOpen()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出 Play Mode，再生成 Foundation 灰盒场景。 ");
            if (SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("当前场景有未保存修改；请先保存或放弃修改，再切换场景。 ");

            SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            Scene scene = sceneAsset == null
                ? EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single)
                : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            FoundationResourceFlowSpikeController controller = FindController(scene);
            if (controller == null)
            {
                var root = new GameObject(RootName);
                SceneManager.MoveGameObjectToScene(root, scene);
                controller = root.AddComponent<FoundationResourceFlowSpikeController>();
            }

            ConfigureAssetReferences(controller);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException($"保存 Foundation 灰盒场景失败：{ScenePath}");

            Selection.activeGameObject = controller.gameObject;
            EditorGUIUtility.PingObject(controller.gameObject);
            Debug.Log(
                $"[NomadWorkshop.Foundation] READY — 已创建实体物流灰盒：{ScenePath}\n" +
                "进入 Play Mode 可观察来源 → 随身库存 → 厨房，以及餐食 / 污水的明确输入输出。 ");
        }

        private static FoundationResourceFlowSpikeController FindController(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                FoundationResourceFlowSpikeController controller =
                    root.GetComponentInChildren<FoundationResourceFlowSpikeController>(true);
                if (controller != null) return controller;
            }
            return null;
        }

        private static void ConfigureAssetReferences(FoundationResourceFlowSpikeController controller)
        {
            GameObject kitchen = AssetDatabase.LoadAssetAtPath<GameObject>(
                NomadFieldKitchenPrototypePipeline.PrefabPath);
            if (kitchen == null)
                throw new InvalidOperationException(
                    "缺少参数化野战厨房 Prefab；请先执行“生成并审计野战厨房”。 ");

            GameObject humanoid = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/NomadWorkshop/ThirdParty/QuaterniusUniversalBaseCharacters/" +
                "Superhero_Male_FullBody.fbx");
            RuntimeAnimatorController animatorController =
                AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                    "Assets/Game/NomadWorkshop/Animation/NomadResident.controller");
            if (humanoid == null || animatorController == null)
                throw new InvalidOperationException("居民 Humanoid 或 Animator Controller 缺失。 ");

            var serialized = new SerializedObject(controller);
            serialized.FindProperty("fieldKitchenPrefab").objectReferenceValue = kitchen;
            serialized.FindProperty("humanoidPrefab").objectReferenceValue = humanoid;
            serialized.FindProperty("humanoidController").objectReferenceValue = animatorController;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
