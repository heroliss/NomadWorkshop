using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Game.NomadWorkshop.Foundation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Game.NomadWorkshop.Editor
{
    /// <summary>将参考造型及已烘焙材质导入独立可玩候选；先核对源文件，再经 Editor 保存并回读接线。</summary>
    public static class NomadReferenceVehiclePipeline
    {
        public const string ScenePath = "Assets/Game/NomadWorkshop/Scenes/ReferenceVehicleSample.unity";
        public const string PrefabPath = NomadWarmWorkshopArtPipeline.Root + "/Prefabs/NW5_Vehicle.prefab";
        public const string MaterialPath = NomadWarmWorkshopArtPipeline.Root + "/Materials/NW5_ShellAtlas.mat";
        private const string Root = NomadWarmWorkshopArtPipeline.Root;
        private const string SourceFolder = "ArtPipelineOutput/VehicleForms/current";
        private static readonly string[] AtlasStems = { "NW5_Shell", "NW5_Deck", "NW5_Cockpit" };

#pragma warning disable CS0649 // JsonUtility 读取明确版本的 Blender manifest。
        [Serializable] private sealed class Manifest
        {
            public string version, status;
            public FileRecord[] sources, files;
            public Geometry source;
            public int atlasSize;
        }
        [Serializable] private sealed class FileRecord { public string file, sha256; }
        [Serializable] private sealed class Geometry { public int triangles; public float[] minimumBlender, maximumBlender; }
#pragma warning restore CS0649

        [MenuItem("Assets/SSFramework/游牧工坊/首版美术/导入并打开参考车体候选")]
        public static void ImportAndOpen()
        {
            RequireCleanIdleScenes();
            string json = File.ReadAllText(SourceFolder + "/manifest.json");
            Manifest manifest = JsonUtility.FromJson<Manifest>(json);
            if (manifest == null || manifest.version != "0.3.0" || manifest.status != "passed-export" ||
                manifest.atlasSize != 2048 || manifest.source == null || manifest.source.triangles <= 0)
                throw new InvalidOperationException("参考车体没有完整的导出/烘焙证据。");
            ValidateFiles(manifest.sources, "Tools/ArtPipeline/Blender", new[]
            { "blender_nomad_vehicle_forms.py", "blender_nomad_form_study.py", "blender_nomad_art_set.py", "blender_nomad_deck_cockpit.py", "blender_nomad_canopy.py" });
            ValidateFiles(manifest.files, SourceFolder, new[] { "NW5_Vehicle.fbx" }.Concat(AtlasStems
                .SelectMany(stem => new[] { "Color", "Normal", "Surface" }.Select(channel => stem + "_" + channel + ".png"))).ToArray());
            Material[] atlases = AtlasStems.Select(ImportMaterial).ToArray();
            Material[] canopyMaterials =
            {
                ImportCanopyMaterial("NW5_CanopySheet", "NW1_Steel", new Color(.53f, .51f, .435f), .45f),
                ImportCanopyMaterial("NW5_CanopyFrame", "NW1_Frame", new Color(.32f, .345f, .305f), .38f)
            };
            string modelPath = Root + "/Models/NW5_Vehicle.fbx";
            File.Copy(SourceFolder + "/NW5_Vehicle.fbx", modelPath, true);
            AssetDatabase.ImportAsset(modelPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false; importer.importCameras = false; importer.importLights = false;
            importer.addCollider = false; importer.globalScale = 1f; importer.bakeAxisConversion = true;
            importer.isReadable = false; importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.importNormals = ModelImporterNormals.Import; importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            foreach (string guid in AssetDatabase.FindAssets("t:Material NW1_", new[] { Root + "/Materials" }))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), material.name), material);
            }
            foreach (Material atlas in atlases)
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), atlas.name), atlas);
            foreach (Material material in canopyMaterials)
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), material.name), material);
            importer.SaveAndReimport();
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var wrapper = new GameObject("NW5_Vehicle");
                SceneManager.MoveGameObjectToScene(wrapper, preview);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath), preview);
                model.transform.SetParent(wrapper.transform, true);
                AuditGeometry(wrapper, manifest.source);
                foreach (Renderer renderer in wrapper.GetComponentsInChildren<Renderer>(true))
                    if (renderer.sharedMaterials.Any(m => m != null && m.name == "NW1_Glass")) renderer.shadowCastingMode = ShadowCastingMode.Off;
                foreach (Transform lampPoint in wrapper.GetComponentsInChildren<Transform>(true)
                             .Where(t => t.name.StartsWith("WorkLamp_", StringComparison.Ordinal) && t.childCount > 0))
                {
                    Light lamp = lampPoint.gameObject.AddComponent<Light>(); lamp.type = LightType.Point;
                    lamp.color = new Color(1f, .67f, .30f); lamp.intensity = .55f; lamp.range = 2.3f;
                    lamp.shadows = LightShadows.None;
                }
                BindCanopy(wrapper);
                PrefabUtility.SaveAsPrefabAsset(wrapper, PrefabPath, out bool saved);
                if (!saved) throw new InvalidOperationException("参考车体 Prefab 保存失败。");
                ValidateCanopy(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
            File.WriteAllText(Root + "/NW5-source-manifest.json", json);
            AssetDatabase.ImportAsset(Root + "/NW5-source-manifest.json");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null &&
                !AssetDatabase.CopyAsset(NomadResidentCrewPipeline.ScenePath, ScenePath))
                throw new InvalidOperationException("参考车体场景副本创建失败。");
            Scene sample = EditorSceneManager.OpenScene(ScenePath);
            var view = FindView(sample);
            var data = new SerializedObject(view);
            // Single 切换后重新解析持久资产，不沿用切换前的 Unity 对象快照。
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null || !EditorUtility.IsPersistent(prefab)) throw new InvalidOperationException("参考车体持久资产丢失。");
            data.FindProperty("vehicleVisualPrefab").objectReferenceValue = prefab;
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(sample);
            if (!EditorSceneManager.SaveScene(sample)) throw new InvalidOperationException("参考车体场景保存失败。");
            sample = EditorSceneManager.OpenScene(ScenePath);
            data = new SerializedObject(FindView(sample));
            if (AssetDatabase.GetAssetPath(data.FindProperty("vehicleVisualPrefab").objectReferenceValue) != PrefabPath)
                throw new InvalidOperationException("参考车体场景回读接线不一致。");
            Directory.CreateDirectory("Logs/AIValidation/nomad-warm-art");
            File.WriteAllText("Logs/AIValidation/nomad-warm-art/reference-vehicle-import.json", JsonUtility.ToJson(new ImportReport
            { scene = ScenePath, triangles = manifest.source.triangles, capturedUtc = DateTime.UtcNow.ToString("O") }, true));
            Debug.Log("参考车体候选已保存并回读；结构边框、错缝甲板、折面车首与可剖开棚架接入同一三居民玩法，仍需实际运行和画面验收。");
        }

        [Serializable] private sealed class ImportReport
        {
            public string status = "passed-import", scene, capturedUtc;
            public int triangles;
            public bool runtimeValidated, visualReviewComplete;
        }

        private static void RequireCleanIdleScenes()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("请在非 Play、编译空闲时导入参考车体。");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("存在未保存场景，请先保存。");
        }

        private static NomadFoundationWorldView FindView(Scene scene) => scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<NomadFoundationWorldView>(true)).Single();

        private static void ValidateFiles(FileRecord[] records, string folder, string[] expected)
        {
            if (records == null || records.Length != expected.Length ||
                !records.Select(r => r.file).OrderBy(n => n, StringComparer.Ordinal).SequenceEqual(expected.OrderBy(n => n, StringComparer.Ordinal)))
                throw new InvalidOperationException("参考车体来源名单不完整或存在重复。");
            foreach (FileRecord record in records)
            {
                using var hash = SHA256.Create(); using var stream = File.OpenRead(folder + "/" + record.file);
                string actual = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                if (actual != record.sha256) throw new InvalidOperationException("参考车体文件摘要不匹配：" + record.file);
            }
        }

        private static Material ImportMaterial(string stem)
        {
            var textures = new Texture2D[3]; string[] channels = { "Color", "Normal", "Surface" };
            for (int i = 0; i < channels.Length; i++)
            {
                string path = Root + "/Textures/" + stem + "_" + channels[i] + ".png";
                File.Copy(SourceFolder + "/" + stem + "_" + channels[i] + ".png", path, true);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = i == 1 ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.spriteImportMode = SpriteImportMode.None;
                importer.isReadable = false; importer.alphaIsTransparency = false;
                importer.sRGBTexture = i == 0; importer.maxTextureSize = 2048; importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.alphaSource = i == 0 ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                ClearUnusedSpriteMetadata(importer);
                importer.SaveAndReimport(); textures[i] = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            string materialPath = Root + "/Materials/" + stem + "Atlas.mat";
            var result = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (result == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) throw new InvalidOperationException("缺少 URP/Lit。");
                result = new Material(shader); AssetDatabase.CreateAsset(result, materialPath);
            }
            result.name = stem + "Atlas";
            result.SetColor("_BaseColor", Color.white); result.SetTexture("_BaseMap", textures[0]);
            result.SetTexture("_BumpMap", textures[1]); result.SetFloat("_BumpScale", 1f);
            result.SetTexture("_MetallicGlossMap", textures[2]); result.SetFloat("_Smoothness", 1f);
            result.SetFloat("_Metallic", 1f); result.SetFloat("_SmoothnessTextureChannel", 0f);
            result.EnableKeyword("_NORMALMAP"); result.EnableKeyword("_METALLICSPECGLOSSMAP");
            EditorUtility.SetDirty(result); AssetDatabase.SaveAssetIfDirty(result); return result;
        }

        private static Material ImportCanopyMaterial(string name, string sourceName, Color color, float smoothness)
        {
            var source = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/" + sourceName + ".mat");
            if (source == null) throw new InvalidOperationException("屋顶需要已有金属材质，不能静默创建无贴图替代物。");
            string path = Root + "/Materials/" + name + ".mat";
            var result = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (result == null)
            {
                result = new Material(source);
                AssetDatabase.CreateAsset(result, path);
            }
            else result.CopyPropertiesFromMaterial(source);
            result.name = name;
            result.SetColor("_BaseColor", color);
            result.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(result);
            AssetDatabase.SaveAssetIfDirty(result);
            return result;
        }

        private static void BindCanopy(GameObject wrapper)
        {
            Transform[] nodes = wrapper.GetComponentsInChildren<Transform>(true);
            Transform canopy = nodes.Single(t => t.name == "NW5_Canopy");
            Transform roof = nodes.Single(t => t.name == "NW5_CanopyRoof");
            var binding = canopy.gameObject.AddComponent<FoundationRoofVisual>();
            var data = new SerializedObject(binding);
            SerializedProperty references = data.FindProperty("roofRenderers");
            Renderer[] renderers = roof.GetComponentsInChildren<Renderer>(true);
            references.arraySize = renderers.Length;
            for (int i = 0; i < renderers.Length; i++) references.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
            data.ApplyModifiedPropertiesWithoutUndo();
            foreach (string name in new[] { "CanopyWorkLamp_0", "CanopyWorkLamp_1" })
            {
                Transform lampPoint = nodes.Single(t => t.name == name);
                Light lamp = lampPoint.gameObject.AddComponent<Light>();
                lamp.type = LightType.Point; lamp.color = new Color(1f, .8f, .57f);
                lamp.intensity = 6f; lamp.range = 4f; lamp.shadows = LightShadows.None;
            }
            ValidateCanopy(wrapper);
        }

        // 导入、Prefab 保存回读都验证显式分件；不把局部隐藏误配到整车或工作灯。
        private static void ValidateCanopy(GameObject root)
        {
            FoundationRoofVisual binding = root.GetComponentsInChildren<FoundationRoofVisual>(true).Single();
            Renderer[] expected = root.GetComponentsInChildren<Renderer>(true)
                .Where(r => r.sharedMaterials.Any(m => m != null && m.name == "NW5_CanopySheet")).ToArray();
            if (expected.Length == 0 || !binding.RoofRenderers.OrderBy(r => r.GetInstanceID())
                    .SequenceEqual(expected.OrderBy(r => r.GetInstanceID())) ||
                binding.RoofRenderers.Any(r => !r.transform.IsChildOf(binding.transform)))
                throw new InvalidOperationException("屋顶分件引用缺失、越界或与材质目标不匹配。");
            if (binding.GetComponentsInChildren<Light>(true).Length != 2)
                throw new InvalidOperationException("棚下工作灯接线不完整。");
        }

        // 仅处理本生成器拥有的材质图。Default 类型不会主动移除旧 Sprite 切片/身份记录；
        // Unity 6 的序列化字段若升级变化则明确拒绝，避免默默保留无用元数据或修改其他资产。
        private static void ClearUnusedSpriteMetadata(TextureImporter importer)
        {
            var data = new SerializedObject(importer);
            foreach (string path in new[] { "m_SpriteSheet.m_Sprites", "m_SpriteSheet.m_NameFileIdTable", "m_InternalIDToNameTable" })
            {
                SerializedProperty property = data.FindProperty(path);
                if (property == null || !property.isArray) throw new InvalidOperationException("Unity 纹理导入序列化字段已变化：" + path);
                property.ClearArray();
            }
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AuditGeometry(GameObject root, Geometry expected)
        {
            if (root.GetComponentsInChildren<Collider>(true).Length != 0) throw new InvalidOperationException("车体外观不能隐式增加玩法 Collider。");
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = renderers.First().bounds;
            foreach (var renderer in renderers)
            {
                bounds.Encapsulate(renderer.bounds);
                if (renderer.sharedMaterials.Any(m => m == null || m.shader.name != "Universal Render Pipeline/Lit"))
                    throw new InvalidOperationException("车体存在缺失或非 URP 材质。");
            }
            int triangles = root.GetComponentsInChildren<MeshFilter>(true).Sum(f => Enumerable.Range(0, f.sharedMesh.subMeshCount)
                .Sum(i => (int)f.sharedMesh.GetIndexCount(i) / 3));
            Vector3 Convert(float[] p) => new(p[0], p[2], p[1]);
            if (triangles != expected.triangles || Vector3.Distance(bounds.min, Convert(expected.minimumBlender)) > .003f ||
                Vector3.Distance(bounds.max, Convert(expected.maximumBlender)) > .003f) throw new InvalidOperationException("车体往返几何不匹配。");
            foreach (var axis in new[] { ("Right", Vector3.right), ("Up", Vector3.up), ("Forward", Vector3.forward) })
                if (Vector3.Distance(root.GetComponentsInChildren<Transform>(true).Single(t => t.name == "NW5_Axis" + axis.Item1).position, axis.Item2) > .002f)
                    throw new InvalidOperationException("参考车体 FBX 轴向不匹配：" + axis.Item1);
            if (renderers.Count(r => r.sharedMaterials.Any(m => m.name == "NW5_ShellAtlas")) != 4)
                throw new InvalidOperationException("车体烘焙边框数量或材质接线不匹配。");
            foreach (string stem in AtlasStems.Skip(1))
                if (renderers.Count(r => r.sharedMaterials.Any(m => m.name == stem + "Atlas")) != 1)
                    throw new InvalidOperationException("甲板或驾驶舱的图集接线不匹配：" + stem);
        }
    }
}
