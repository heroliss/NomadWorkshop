using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Game.NomadWorkshop.Foundation;
using Game.NomadWorkshop.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.NomadWorkshop.Editor
{
    /// <summary>
    /// 将经过 Blender 校核的固定工装配方导入普通 Humanoid 资产。
    /// 只替换参考车辆的外观绑定，并创建独立楼梯样本；不修改 NW3 基线或玩家存档。
    /// </summary>
    public static class NomadWorkwearPipeline
    {
        private const string Root = NomadWarmWorkshopArtPipeline.Root;
        private const string Source = "ArtPipelineOutput/Workwear/v10";
        private const string ReferenceScene = "Assets/Game/NomadWorkshop/Scenes/ReferenceVehicleSample.unity";
        private static readonly string[] Ids = { "Mechanic", "Caretaker", "Driver" };
#pragma warning disable CS0649 // 离线导出证据由 JsonUtility 填充。
        [Serializable] private sealed class Manifest { public string status, version; public Variant[] variants; public SourceFile[] sources; }
        [Serializable] private sealed class SourceFile { public string file, sha256; }
        [Serializable] private sealed class Variant
        {
            public string id, fbx, sha256, sourceSha256;
            public int boneCount;
            public float maxBoneRoundTripError;
            public SourceFile[] retainedTextures;
        }
#pragma warning restore CS0649

        [MenuItem("Assets/SSFramework/游牧工坊/首版美术/生成工装并接入参考车辆")]
        public static void GenerateAndApply()
        {
            RequireIdle();
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Source + "/manifest.json"));
            if (manifest.status != "passed-export" || manifest.version != "0.2.3" ||
                manifest.variants == null || manifest.variants.Length != Ids.Length ||
                manifest.sources == null || manifest.sources.Length != 5)
                throw new InvalidOperationException("工装导出证据缺失或版本不匹配。");
            foreach (SourceFile source in manifest.sources) CheckHash(source.file, source.sha256);
            for (int i = 0; i < Ids.Length; i++)
            {
                Variant variant = manifest.variants[i];
                if (variant.id != Ids[i] || variant.fbx != "NW10_" + Ids[i] + ".fbx" ||
                    variant.boneCount != 65 || variant.maxBoneRoundTripError > .0001f)
                    throw new InvalidOperationException("工装身份、骨架或 FBX 往返证据不一致。");
                CheckHash(Source + "/" + variant.fbx, variant.sha256);
                CheckHash(Root + "/Models/NW3_" + Ids[i] + ".fbx", variant.sourceSha256);
                foreach (SourceFile texture in variant.retainedTextures) CheckHash(texture.file, texture.sha256);
            }

            var shared = new Dictionary<string, Material>();
            foreach (string name in new[] { "NW3_Eyes", "NW3_Skin_Male", "NW3_Skin_Female",
                "NW3_Hair_Mechanic", "NW3_Hair_Caretaker", "NW3_Hair_Driver" })
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/" + name + ".mat");
                if (material == null) throw new InvalidOperationException("工装缺少现有人物材质：" + name);
                shared.Add(name, material);
            }
            shared.Add("NW10_Trousers", CreateMaterial("NW10_Trousers", new Color(.12f,.14f,.12f), true));
            shared.Add("NW10_Leather", CreateMaterial("NW10_Leather", new Color(.085f,.06f,.04f), false));
            shared.Add("NW10_Stitch", CreateMaterial("NW10_Stitch", new Color(.26f,.23f,.16f), true));
            Color[] colors = { new(.18f,.29f,.25f), new(.38f,.29f,.17f), new(.22f,.265f,.29f) };
            for (int i = 0; i < Ids.Length; i++)
            {
                string name = "NW10_Shirt_" + Ids[i];
                shared.Add(name, CreateMaterial(name, colors[i], true));
                string model = Root + "/Models/NW10_" + Ids[i] + ".fbx";
                File.Copy(Source + "/NW10_" + Ids[i] + ".fbx", model, true);
                AssetDatabase.ImportAsset(model, ImportAssetOptions.ForceSynchronousImport);
                NomadResidentSamplePipeline.GenerateModelPrefab(model, PrefabPath(Ids[i]), shared);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(Ids[i]));
                if (prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .SelectMany(r => r.sharedMaterials).Any(m => m == null || !shared.Values.Contains(m)))
                    throw new InvalidOperationException("工装存在未映射的材质。");
            }
            string evidence = Root + "/Models/NW10_manifest.json";
            File.Copy(Source + "/manifest.json", evidence, true);
            AssetDatabase.ImportAsset(evidence, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.SaveAssets();

            foreach (string id in Ids)
                NomadResidentSamplePipeline.OpenCandidateScene<NomadStairTraversalView>(
                    NomadStairTraversalPipeline.ScenePath, StairScenePath(id), "humanoidPrefab", PrefabPath(id));
            Scene scene = EditorSceneManager.OpenScene(ReferenceScene);
            var view = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<NomadFoundationWorldView>(true)).Single();
            var serialized = new SerializedObject(view);
            serialized.FindProperty("residentVisualPrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath("Mechanic"));
            SerializedProperty bindings = serialized.FindProperty("residentVisualBindings");
            if (bindings.arraySize != Ids.Length)
                throw new InvalidOperationException("参考车辆的三居民外观绑定数量已改变，请复核后再接入。");
            for (int i = 0; i < Ids.Length; i++)
            {
                var binding = bindings.GetArrayElementAtIndex(i);
                if (binding.FindPropertyRelative("residentId").stringValue != "resident-" + (i + 1).ToString("00"))
                    throw new InvalidOperationException("参考车辆的居民稳定身份顺序已改变。");
                binding.FindPropertyRelative("prefab").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(Ids[i]));
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("参考车辆工装绑定保存失败。");
            Debug.Log("[NomadWorkwear] 工装已接入参考车辆，三份独立楼梯场景已准备；需继续实机动作与观感验收。");
        }

        internal static string StairScenePath(string id) => "Assets/Game/NomadWorkshop/Scenes/Workwear" + id + "Stairs.unity";
        internal static bool IsStairScene(string path) => Ids.Any(id => path == StairScenePath(id));
        private static string PrefabPath(string id) => Root + "/Prefabs/NW10_" + id + ".prefab";

        private static Material CreateMaterial(string name, Color color, bool cloth)
        {
            string path = Root + "/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) throw new InvalidOperationException("工装需要 URP/Lit。");
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            Texture2D texture = null, normal = null;
            if (cloth)
            {
                texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/NW1_Fabric_Color.png");
                normal = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/NW1_Fabric_Normal.png");
                if (texture == null || normal == null) throw new InvalidOperationException("工装缺少共享布料贴图。");
            }
            // Blender 的节点配方使用线性 RGB，Unity 的 Color 材质属性按 sRGB 存储。
            // 先转换再赋值，避免将同一组数值重复压暗。
            material.SetColor("_BaseColor", color.gamma);
            material.SetTexture("_BaseMap", texture); material.SetTexture("_BumpMap", normal);
            material.SetTextureScale("_BaseMap", cloth ? Vector2.one * 12 : Vector2.one);
            material.SetFloat("_BumpScale", .15f); material.SetFloat("_Smoothness", .2f);
            material.SetFloat("_Metallic", 0);
            if (cloth) material.EnableKeyword("_NORMALMAP"); else material.DisableKeyword("_NORMALMAP");
            EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }

        private static void RequireIdle()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("请在非 Play 且编译空闲时生成工装。");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("请先保存所有已打开场景，再接入工装。");
        }

        private static void CheckHash(string path, string expected)
        {
            using var algorithm = SHA256.Create();
            using var stream = File.OpenRead(path);
            string actual = BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            if (actual != expected) throw new InvalidOperationException("工装源文件与导出证据不一致：" + path);
        }
    }
}
