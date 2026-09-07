using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Game.NomadWorkshop.Foundation;
using Game.NomadWorkshop.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.NomadWorkshop.Editor
{
    /// <summary>固定三居民的离线外观配方接线。生成普通 Prefab，运行时不依赖 Blender 或人物生成器。</summary>
    public static class NomadResidentCrewPipeline
    {
        public const string ScenePath = "Assets/Game/NomadWorkshop/Scenes/ResidentCrewSample.unity";
        private const string Root = NomadWarmWorkshopArtPipeline.Root;
        private const string RecipePath = "Tools/ArtPipeline/Blender/nomad_resident_variants.json";
        private const string GeneratorPath = "Tools/ArtPipeline/Blender/blender_nomad_resident_variants.py";
        private const string BaseSource = "Assets/Game/NomadWorkshop/ThirdParty/QuaterniusUniversalBaseCharacters/";
        private const string OutfitSource = "Assets/Game/NomadWorkshop/ThirdParty/QuaterniusModularOutfits/";
        private const string ManifestPath = Root + "/Models/NW3_manifest.json";

#pragma warning disable CS0649 // JsonUtility 从固定配方与 Blender manifest 填充这些字段。
        [Serializable] private sealed class Recipe { public int recipeVersion; public Variant[] variants; }
        [Serializable] private sealed class Variant { public string id; public string body; public float[] shirtColor; public float[] hairColor; }
        [Serializable] private sealed class Manifest { public int recipeVersion; public string generatorSha256; public string recipeSha256; public Export[] variants; }
        [Serializable] private sealed class Export { public string id; public string fbx; public string sha256; }
#pragma warning restore CS0649

        [MenuItem("Assets/SSFramework/游牧工坊/首版美术/生成并打开三居民外观候选")]
        public static void GenerateAndOpen()
        {
            RequireIdle();
            Recipe recipe = JsonUtility.FromJson<Recipe>(File.ReadAllText(RecipePath));
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            if (recipe.recipeVersion != 1 || manifest.recipeVersion != recipe.recipeVersion ||
                manifest.recipeSha256 != Hash(RecipePath) || manifest.generatorSha256 != Hash(GeneratorPath) ||
                recipe.variants.Length != 3 || manifest.variants.Length != 3)
                throw new InvalidOperationException("三居民配方/生成器与导出证据不一致，请重新导出并核对。");
            for (int i = 0; i < recipe.variants.Length; i++)
                if (manifest.variants[i].id != recipe.variants[i].id ||
                    manifest.variants[i].fbx != "NW3_" + recipe.variants[i].id + ".fbx" ||
                    Hash(Root + "/Models/" + manifest.variants[i].fbx) != manifest.variants[i].sha256)
                    throw new InvalidOperationException("三居民 FBX 与 manifest 不一致。");

            var shared = new Dictionary<string, Material>
            {
                ["NW3_Outfit"] = Material("NW3_Outfit", Color.white, OutfitSource + "T_Peasant_BaseColor.png", OutfitSource + "T_Peasant_Normal.png"),
                ["NW3_Eyes"] = Material("NW3_Eyes", Color.white, BaseSource + "T_Eye_Brown.png"),
                ["NW3_Skin_Male"] = Material("NW3_Skin_Male", Color.white, BaseSource + "T_Superhero_Male_Ligh.png", BaseSource + "T_Superhero_Male_Normal.png"),
                ["NW3_Skin_Female"] = Material("NW3_Skin_Female", Color.white, BaseSource + "T_Superhero_Female_Light_BaseColor.png", BaseSource + "T_Superhero_Female_Normal.png")
            };
            foreach (Variant variant in recipe.variants)
            {
                var materials = new Dictionary<string, Material>(shared);
                string shirt = "NW3_Shirt_" + variant.id, hair = "NW3_Hair_" + variant.id;
                materials[shirt] = Material(shirt, ColorOf(variant.shirtColor), OutfitSource + "T_Peasant_BaseColor.png", OutfitSource + "T_Peasant_Normal.png");
                materials[hair] = Material(hair, ColorOf(variant.hairColor));
                NomadResidentSamplePipeline.GenerateModelPrefab(Root + "/Models/NW3_" + variant.id + ".fbx",
                    PrefabPath(variant.id), materials);
            }
            bool creating = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null;
            NomadResidentSamplePipeline.OpenCandidateScene<NomadFoundationWorldView>(
                NomadWarmWorkshopArtPipeline.ScenePath, ScenePath, "residentVisualPrefab", PrefabPath("Mechanic"));
            if (creating)
            {
                NomadFoundationWorldView view = null;
                foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (NomadFoundationWorldView found in root.GetComponentsInChildren<NomadFoundationWorldView>(true))
                {
                    if (view != null) throw new InvalidOperationException("三居民场景出现多个 WorldView。");
                    view = found;
                }
                if (view == null) throw new InvalidOperationException("三居民场景缺少 WorldView。");
                var serialized = new SerializedObject(view);
                SerializedProperty bindings = serialized.FindProperty("residentVisualBindings");
                bindings.arraySize = recipe.variants.Length;
                for (int i = 0; i < recipe.variants.Length; i++)
                {
                    SerializedProperty element = bindings.GetArrayElementAtIndex(i);
                    element.FindPropertyRelative("residentId").stringValue = "resident-" + (i + 1).ToString("00");
                    element.FindPropertyRelative("prefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(recipe.variants[i].id));
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene()))
                    throw new InvalidOperationException("三居民场景保存失败。");
            }
            Debug.Log("三居民外观候选已打开；身份绑定只负责外观，仍需实际动作与观感验收。");
        }

        [MenuItem("Assets/SSFramework/游牧工坊/首版美术/三居民楼梯对比/短发维修者")]
        public static void OpenMechanicStairs() => OpenStairs("Mechanic");
        [MenuItem("Assets/SSFramework/游牧工坊/首版美术/三居民楼梯对比/束发居民")]
        public static void OpenCaretakerStairs() => OpenStairs("Caretaker");
        [MenuItem("Assets/SSFramework/游牧工坊/首版美术/三居民楼梯对比/灰发驾驶者")]
        public static void OpenDriverStairs() => OpenStairs("Driver");

        private static void OpenStairs(string id) => NomadResidentSamplePipeline.OpenCandidateScene<NomadStairTraversalView>(
            NomadStairTraversalPipeline.ScenePath, "Assets/Game/NomadWorkshop/Scenes/ResidentCrew" + id + "Stairs.unity",
            "humanoidPrefab", PrefabPath(id));

        internal static bool IsStairScene(string path) =>
            path == "Assets/Game/NomadWorkshop/Scenes/ResidentCrewMechanicStairs.unity" ||
            path == "Assets/Game/NomadWorkshop/Scenes/ResidentCrewCaretakerStairs.unity" ||
            path == "Assets/Game/NomadWorkshop/Scenes/ResidentCrewDriverStairs.unity";

        private static string PrefabPath(string id) => Root + "/Prefabs/NW3_" + id + ".prefab";
        private static Color ColorOf(float[] rgb) => new(rgb[0], rgb[1], rgb[2]);

        private static void RequireIdle()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("请在非 Play、编译空闲且当前场景已保存时生成三居民。");
        }

        private static string Hash(string path)
        {
            using var algorithm = SHA256.Create();
            using FileStream stream = File.OpenRead(path);
            return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        private static Material Material(string name, Color color, string texturePath = null, string normalPath = null)
        {
            Texture2D texture = ImportTexture(texturePath, false), normal = ImportTexture(normalPath, true);
            string path = Root + "/Materials/" + name + ".mat";
            Material result = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (result == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) throw new InvalidOperationException("三居民候选需要 URP/Lit。");
                result = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(result, path);
            }
            result.SetColor("_BaseColor", color);
            result.SetTexture("_BaseMap", texture);
            result.SetTexture("_BumpMap", normal);
            result.SetFloat("_BumpScale", .6f);
            result.SetFloat("_Smoothness", .15f);
            if (normal != null) result.EnableKeyword("_NORMALMAP");
            else result.DisableKeyword("_NORMALMAP");
            EditorUtility.SetDirty(result);
            return result;
        }

        private static Texture2D ImportTexture(string path, bool normal)
        {
            if (path == null) return null;
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
                throw new InvalidOperationException("三居民配方缺少贴图：" + path);
            var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (importer.textureType != type || importer.sRGBTexture == normal || importer.maxTextureSize != 2048)
            {
                importer.textureType = type;
                importer.sRGBTexture = !normal;
                importer.maxTextureSize = 2048;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
