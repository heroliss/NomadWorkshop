using System;
using Game.NomadWorkshop.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Game.NomadWorkshop.Editor
{
    /// <summary>生成可删除的连续导航与候选交互位对照场景；不会改写正式 Foundation 场景。</summary>
    public static class NomadNavigationInteractionSpikePipeline
    {
        public const string ScenePath =
            "Assets/Game/NomadWorkshop/Scenes/NavigationInteractionSpike.unity";

        [MenuItem("Assets/SSFramework/游牧工坊/Foundation/创建或打开连续导航与交互 Spike")]
        public static void CreateOrOpen()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出 Play Mode 再生成连续导航场景。");

            Scene current = SceneManager.GetActiveScene();
            if (current.IsValid() && current.isDirty && current.path != ScenePath)
                throw new InvalidOperationException(
                    $"当前场景 '{current.name}' 有未保存修改；为避免覆盖，请先保存或撤销后重试。");

            Scene scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null
                ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single)
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            foreach (GameObject root in scene.GetRootGameObjects())
                UnityEngine.Object.DestroyImmediate(root);

            NavigationInteractionSpikeComposition composition =
                NomadNavigationSpikeRuntimeFactory.Create();
            Camera camera = composition.Root.GetComponentInChildren<Camera>(true);
            if (camera == null)
                throw new InvalidOperationException("连续导航场景缺少 Camera。");
            camera.GetUniversalAdditionalCameraData().SetRenderer(
                NomadRenderingSpikePipeline.GetSecondaryRendererIndexOrThrow());
            Selection.activeGameObject = composition.Root;
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException($"保存连续导航场景失败：{ScenePath}");
            AssetDatabase.SaveAssets();
            Debug.Log($"[NomadWorkshop] 连续导航与交互位 Spike 已生成：{ScenePath}");
        }
    }
}
