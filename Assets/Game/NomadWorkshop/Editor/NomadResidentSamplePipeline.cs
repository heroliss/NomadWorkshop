using System;
using System.Collections.Generic;
using Game.NomadWorkshop.Foundation;
using Game.NomadWorkshop.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.NomadWorkshop.Editor
{
    /// <summary>
    /// 现成服装人物的最小导入实验。保留源服装的蒙皮骨架；模型只提供外观，复用已有 Humanoid 动作。
    /// 先生成独立候选 Prefab，不修改正式场景，也不把该资产配方当作参数化人物系统。
    /// </summary>
    public static class NomadResidentSamplePipeline
    {
        public const string ModelPath = NomadWarmWorkshopArtPipeline.Root + "/Models/NW2_PeasantResident.fbx";
        public const string PrefabPath = NomadWarmWorkshopArtPipeline.Root + "/Prefabs/NW2_ResidentSample.prefab";
        public const string ScenePath = "Assets/Game/NomadWorkshop/Scenes/ResidentAppearanceSample.unity";
        public const string StairScenePath = "Assets/Game/NomadWorkshop/Scenes/ResidentStairSample.unity";
        private const string SourceRoot = "Assets/Game/NomadWorkshop/ThirdParty/QuaterniusModularOutfits";

        /// <summary>首次从温暖工坊保存场景副本并替换人物引用；以后直接打开，不覆盖试验场景中的调整。</summary>
        [MenuItem("Assets/SSFramework/游牧工坊/首版美术/创建或打开现成人物对比")]
        public static void OpenSample() => OpenCandidateScene<NomadFoundationWorldView>(
            NomadWarmWorkshopArtPipeline.ScenePath, ScenePath, "residentVisualPrefab");

        /// <summary>为候选保留同一套真实楼梯及操作入口；只替换独立副本中的外观引用。</summary>
        [MenuItem("Assets/SSFramework/游牧工坊/首版美术/创建或打开现成人物楼梯对比")]
        public static void OpenStairSample() => OpenCandidateScene<NomadStairTraversalView>(
            NomadStairTraversalPipeline.ScenePath, StairScenePath, "humanoidPrefab");

        internal static void OpenCandidateScene<TView>(string sourcePath, string targetPath, string prefabField,
            string candidatePrefabPath = PrefabPath)
            where TView : Component
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("请在非 Play 且编译空闲时打开人物对比。");
            if (SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("当前场景未保存，请先保存后再打开人物对比。");
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(candidatePrefabPath);
            if (prefab == null) throw new InvalidOperationException("请先生成现成人物候选。");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(targetPath) == null)
            {
                Scene source = EditorSceneManager.OpenScene(sourcePath);
                if (!EditorSceneManager.SaveScene(source, targetPath, true))
                    throw new InvalidOperationException("人物对比场景副本保存失败。");
                Scene sample = EditorSceneManager.OpenScene(targetPath);
                TView view = null;
                foreach (GameObject root in sample.GetRootGameObjects())
                foreach (TView candidate in root.GetComponentsInChildren<TView>(true))
                {
                    if (view != null) throw new InvalidOperationException("人物对比存在多个 " + typeof(TView).Name);
                    view = candidate;
                }
                if (view == null) throw new InvalidOperationException("人物对比没有 " + typeof(TView).Name);
                var serialized = new SerializedObject(view);
                SerializedProperty field = serialized.FindProperty(prefabField);
                if (field == null) throw new InvalidOperationException("人物对比缺少外观引用：" + prefabField);
                field.objectReferenceValue = prefab;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(sample);
                if (!EditorSceneManager.SaveScene(sample)) throw new InvalidOperationException("人物对比引用保存失败。");
            }
            else EditorSceneManager.OpenScene(targetPath);
        }

        /// <summary>在非 Play 状态配置导入器、材质与候选包装 Prefab；重复生成保留资产 GUID。</summary>
        [MenuItem("Assets/SSFramework/游牧工坊/首版美术/生成现成人物候选")]
        public static void Generate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("请在非 Play 且编译空闲时生成人物候选。");
            Material garment = Garment("NW2_Workwear", "Peasant");
            Material hood = Garment("NW2_Hood", "Ranger");
            GenerateModelPrefab(ModelPath, PrefabPath, new Dictionary<string, Material>
            {
                ["MI_Peasant"] = garment,
                ["MI_Ranger"] = hood,
                ["MI_Superhero_Male"] = AssetDatabase.LoadAssetAtPath<Material>(NomadHumanoidAssetPipeline.BodyMaterialPath),
                ["MI_Regular_Male"] = AssetDatabase.LoadAssetAtPath<Material>(NomadHumanoidAssetPipeline.BodyMaterialPath),
                ["MI_Eyes"] = AssetDatabase.LoadAssetAtPath<Material>(NomadHumanoidAssetPipeline.EyeMaterialPath),
                ["MI_Hair_1"] = AssetDatabase.LoadAssetAtPath<Material>(NomadHumanoidAssetPipeline.HairMaterialPath)
            });
        }

        /// <summary>
        /// 当前同源 FBX 候选共用的导入、Avatar/实际脸朝向检查与 Prefab 保存入口。
        /// 不自动适配任意 FBX 朝向或骨架；未通过检查时不覆盖既有 Prefab。
        /// </summary>
        internal static void GenerateModelPrefab(string modelPath, string prefabPath,
            IReadOnlyDictionary<string, Material> materials)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("请在非 Play 且编译空闲时生成人物候选。");
            if (AssetImporter.GetAtPath(modelPath) is not ModelImporter importer)
                throw new InvalidOperationException("人物候选 FBX 尚未导入：" + modelPath);
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importConstraints = false;
            importer.optimizeGameObjects = false;
            importer.isReadable = false;
            importer.globalScale = 1f;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            foreach (KeyValuePair<string, Material> entry in materials)
                Remap(importer, entry.Key, entry.Value);
            importer.SaveAndReimport();

            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject(System.IO.Path.GetFileNameWithoutExtension(prefabPath));
                SceneManager.MoveGameObjectToScene(root, preview);
                GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(source, preview);
                model.transform.SetParent(root.transform, false);
                // 实际 FBX 中性姿态面朝 -Z，外层包装对齐玩法 +Z。
                model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                Animator animator = model.GetComponentInChildren<Animator>(true);
                if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
                    throw new InvalidOperationException("候选人物未生成有效 Humanoid Avatar。");
                // 柔性衣物需要关节权重混合，不能跟随 Very Low 档退化为单骨刚性段。
                // 这是资产自己的变形要求；衣物层间穿插仍需遮罩及实际姿态检查。
                foreach (SkinnedMeshRenderer renderer in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    renderer.quality = SkinQuality.Bone4;
                foreach (HumanBodyBones required in new[] { HumanBodyBones.Head, HumanBodyBones.RightHand,
                    HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightIndexProximal,
                    HumanBodyBones.RightLittleProximal, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
                    if (animator.GetBoneTransform(required) == null)
                        throw new InvalidOperationException("候选人物缺少接触所需骨骼：" + required);
                SkinnedMeshRenderer eyes = Array.Find(animator.GetComponentsInChildren<SkinnedMeshRenderer>(),
                    renderer => renderer.name == "Eyes");
                if (eyes == null) throw new InvalidOperationException("候选人物缺少眼部几何，无法核对正面。");
                var eyeMesh = new Mesh();
                try
                {
                    // FBX 的嵌套单位缩放保留在 Renderer 上；包围盒不是动画后真实眼部位置。
                    eyes.BakeMesh(eyeMesh, true);
                    if (eyeMesh.vertexCount == 0)
                        throw new InvalidOperationException("候选人物眼部几何为空，无法核对正面。");
                    Vector3 center = Vector3.zero;
                    foreach (Vector3 vertex in eyeMesh.vertices) center += vertex;
                    center = eyes.transform.TransformPoint(center / eyeMesh.vertexCount);
                    if (root.transform.InverseTransformPoint(center).z -
                        root.transform.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.Head).position).z < .01f)
                        throw new InvalidOperationException("候选人物正面没有对齐玩法 +Z，请核对 FBX 导出朝向。");
                }
                finally { UnityEngine.Object.DestroyImmediate(eyeMesh); }
                animator.applyRootMotion = false;
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool saved);
                if (!saved) throw new InvalidOperationException("人物候选 Prefab 保存失败。");
                AssetDatabase.SaveAssets();
                Debug.Log("现成人物候选已生成，Humanoid 与必要手指/脚骨有效；仍需实机外观及动作验收。\n" + prefabPath);
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        private static Material Garment(string name, string sourceName)
        {
            string colorPath = SourceRoot + "/T_" + sourceName + "_BaseColor.png";
            string normalPath = SourceRoot + "/T_" + sourceName + "_Normal.png";
            foreach (string path in new[] { colorPath, normalPath })
            {
                var texture = AssetImporter.GetAtPath(path) as TextureImporter;
                if (texture == null) throw new InvalidOperationException("候选材质缺少贴图：" + path);
                texture.textureType = path == normalPath ? TextureImporterType.NormalMap : TextureImporterType.Default;
                texture.sRGBTexture = path != normalPath;
                texture.maxTextureSize = 2048;
                texture.SaveAndReimport();
            }
            string materialPath = NomadWarmWorkshopArtPipeline.Root + "/Materials/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(colorPath));
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
            material.SetFloat("_BumpScale", .6f);
            material.SetFloat("_Smoothness", .15f);
            material.EnableKeyword("_NORMALMAP");
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void Remap(ModelImporter importer, string name, Material material)
        {
            if (material == null) throw new InvalidOperationException("人物候选缺少材质：" + name);
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), material);
        }
    }
}
