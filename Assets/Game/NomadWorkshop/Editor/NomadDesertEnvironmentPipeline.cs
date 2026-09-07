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
    /// <summary>校核 Blender 环境来源、导入米制资产并保存显式行驶绑定，只更新现有参考车体场景。</summary>
    public static class NomadDesertEnvironmentPipeline
    {
        private const string Root = NomadWarmWorkshopArtPipeline.Root;
        private const string SourceFolder = "ArtPipelineOutput/DesertEnvironment/current";
        public const string PrefabPath = Root + "/Prefabs/NW6_Desert.prefab";
        private static readonly string[] Stems = { "NW6_Ground", "NW6_Sandstone" };

#pragma warning disable CS0649 // JsonUtility 读取 Blender 导出报告。
        [Serializable] private sealed class Manifest
        {
            public string version, status;
            public FileRecord[] sources, files;
            public Geometry source;
            public int textureSize, sceneryGroupCount;
            public float groundRepeatMeters, trackRepeatMeters, sceneryLoopMeters;
        }
        [Serializable] private sealed class FileRecord { public string file, sha256; }
        [Serializable] private sealed class Geometry { public int triangles, meshCount; public float[] minimumBlender, maximumBlender; }
#pragma warning restore CS0649

        [MenuItem("Assets/SSFramework/游牧工坊/首版美术/导入沙漠并打开参考车体")]
        public static void ImportAndOpen()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("请在非 Play、编译空闲时导入沙漠。");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("存在未保存场景，请先保存。");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(NomadReferenceVehiclePipeline.ScenePath) == null)
                throw new InvalidOperationException("请先创建参考车体候选场景，再接入沙漠。");
            string json = File.ReadAllText(SourceFolder + "/manifest.json");
            Manifest manifest = JsonUtility.FromJson<Manifest>(json);
            if (manifest == null || manifest.version != "0.1.0" || manifest.status != "passed-export" ||
                manifest.source == null || manifest.source.triangles <= 0 || manifest.source.triangles > 160000 ||
                manifest.textureSize != 2048 || manifest.sceneryGroupCount != 16 ||
                manifest.groundRepeatMeters != 6 || manifest.trackRepeatMeters != 1.4f || manifest.sceneryLoopMeters != 60)
                throw new InvalidOperationException("沙漠导出证据或米制配方不完整。");
            ValidateFiles(manifest.sources, "Tools/ArtPipeline/Blender", new[] { "blender_nomad_desert.py", "blender_nomad_art_set.py" });
            ValidateFiles(manifest.files, SourceFolder, new[] { "NW6_Desert.fbx", "NW6_Track_Color.png" }.Concat(Stems
                .SelectMany(stem => new[] { "Color", "Normal", "Surface" }.Select(c => stem+"_"+c+".png"))).ToArray());
            var materials = Stems.Select(ImportMaterial).ToList();
            Material track = GetMaterial("NW6_Track");
            track.SetTexture("_BaseMap", ImportTexture("NW6_Track", "Color", true));
            track.SetColor("_BaseColor", new Color(.35f, .35f, .35f, 1));
            track.SetFloat("_SpecularHighlights", 0); track.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            track.SetFloat("_EnvironmentReflections", 0); track.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            track.SetFloat("_Surface", 1); track.SetFloat("_Blend", 0);
            track.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); track.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            track.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); track.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            track.SetFloat("_ZWrite", 0); track.SetFloat("_Smoothness", .04f);
            track.SetOverrideTag("RenderType", "Transparent"); track.renderQueue = (int)RenderQueue.Transparent;
            track.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); track.SetShaderPassEnabled("ShadowCaster", false);
            Save(track); materials.Add(track);
            Material scrub = GetMaterial("NW6_Scrub");
            scrub.SetColor("_BaseColor", new Color(.40f, .365f, .27f)); scrub.SetFloat("_Smoothness", .06f);
            scrub.SetFloat("_Cull", (float)CullMode.Off); Save(scrub); materials.Add(scrub);
            string modelPath = Root + "/Models/NW6_Desert.fbx";
            File.Copy(SourceFolder + "/NW6_Desert.fbx", modelPath, true);
            AssetDatabase.ImportAsset(modelPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
            importer.animationType = ModelImporterAnimationType.None; importer.importAnimation = false;
            importer.importCameras = false; importer.importLights = false; importer.addCollider = false;
            importer.globalScale = 1; importer.bakeAxisConversion = true; importer.isReadable = false;
            importer.meshCompression = ModelImporterMeshCompression.Off; importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            foreach (Material material in materials)
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), material.name), material);
            importer.SaveAndReimport();
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var wrapper = new GameObject("NW6_Desert"); SceneManager.MoveGameObjectToScene(wrapper, preview);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath), preview);
                model.transform.SetParent(wrapper.transform, true);
                Audit(wrapper, manifest.source);
                var binding = wrapper.AddComponent<FoundationEnvironmentVisual>();
                var data = new SerializedObject(binding);
                Renderer[] surfaces = wrapper.GetComponentsInChildren<Renderer>(true)
                    .Where(r => r.sharedMaterial.name == "NW6_Ground" || r.sharedMaterial.name == "NW6_Track").OrderBy(r => r.name).ToArray();
                if (surfaces.Length != 3) throw new InvalidOperationException("沙漠需要砂地及两条压痕表面。");
                SerializedProperty references = data.FindProperty("scrollingSurfaces"); references.arraySize = surfaces.Length;
                for (int i = 0; i < surfaces.Length; i++)
                {
                    var item = references.GetArrayElementAtIndex(i);
                    item.FindPropertyRelative("renderer").objectReferenceValue = surfaces[i];
                    item.FindPropertyRelative("metersPerUvUnit").floatValue = surfaces[i].sharedMaterial == track ? manifest.trackRepeatMeters : manifest.groundRepeatMeters;
                    surfaces[i].shadowCastingMode = ShadowCastingMode.Off;
                }
                Transform[] groups = wrapper.GetComponentsInChildren<Transform>(true)
                    .Where(t => t.name.StartsWith("NW6_Scenery_", StringComparison.Ordinal)).OrderBy(t => t.name).ToArray();
                if (groups.Length != manifest.sceneryGroupCount) throw new InvalidOperationException("地景组数与 Blender 不匹配。");
                references = data.FindProperty("sceneryGroups"); references.arraySize = groups.Length;
                for (int i = 0; i < groups.Length; i++) references.GetArrayElementAtIndex(i).objectReferenceValue = groups[i];
                data.FindProperty("sceneryLoopMeters").floatValue = manifest.sceneryLoopMeters;
                data.ApplyModifiedPropertiesWithoutUndo();
                using (var presentation = new FoundationEnvironmentPresentation(wrapper.transform)) { }
                PrefabUtility.SaveAsPrefabAsset(wrapper, PrefabPath, out bool saved);
                if (!saved) throw new InvalidOperationException("沙漠 Prefab 保存失败。");
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
            File.WriteAllText(Root + "/NW6-source-manifest.json", json); AssetDatabase.ImportAsset(Root + "/NW6-source-manifest.json");
            Scene sample = EditorSceneManager.OpenScene(NomadReferenceVehiclePipeline.ScenePath);
            var viewData = new SerializedObject(FindView(sample));
            viewData.FindProperty("environmentVisualPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            viewData.ApplyModifiedPropertiesWithoutUndo(); EditorSceneManager.MarkSceneDirty(sample);
            if (!EditorSceneManager.SaveScene(sample)) throw new InvalidOperationException("环境场景保存失败。");
            sample = EditorSceneManager.OpenScene(NomadReferenceVehiclePipeline.ScenePath);
            viewData = new SerializedObject(FindView(sample));
            if (AssetDatabase.GetAssetPath(viewData.FindProperty("environmentVisualPrefab").objectReferenceValue) != PrefabPath)
                throw new InvalidOperationException("场景环境引用回读不匹配。");
            Directory.CreateDirectory("Logs/AIValidation/nomad-warm-art");
            File.WriteAllText("Logs/AIValidation/nomad-warm-art/desert-import.json", JsonUtility.ToJson(new ImportReport
            { triangles = manifest.source.triangles, meshes = manifest.source.meshCount, capturedUtc = DateTime.UtcNow.ToString("O") }, true));
            Debug.Log("沙漠已导入并保存：米制砂砾纹理、层状石群和柔边压痕已接线；仍需实际行驶和画面验收。");
        }

        [Serializable] private sealed class ImportReport
        {
            public string status = "passed-import", scene = NomadReferenceVehiclePipeline.ScenePath, capturedUtc;
            public int triangles, meshes;
            public bool runtimeValidated, visualReviewComplete;
        }

        private static NomadFoundationWorldView FindView(Scene scene) => scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<NomadFoundationWorldView>(true)).Single();

        private static void ValidateFiles(FileRecord[] records, string folder, string[] expected)
        {
            if (records == null || records.Length != expected.Length ||
                !records.Select(r => r.file).OrderBy(n => n, StringComparer.Ordinal).SequenceEqual(expected.OrderBy(n => n, StringComparer.Ordinal)))
                throw new InvalidOperationException("沙漠文件名单不完整或重复。");
            foreach (FileRecord record in records)
            {
                using var hash = SHA256.Create(); using var stream = File.OpenRead(folder + "/" + record.file);
                if (BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() != record.sha256)
                    throw new InvalidOperationException("沙漠文件摘要不匹配：" + record.file);
            }
        }

        private static Texture2D ImportTexture(string stem, string channel, bool transparent = false)
        {
            string file = stem+"_"+channel+".png", path = Root+"/Textures/"+file;
            File.Copy(SourceFolder+"/"+file, path, true); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = channel == "Normal" ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.spriteImportMode = SpriteImportMode.None; importer.isReadable = false;
            importer.sRGBTexture = channel == "Color"; importer.alphaIsTransparency = transparent;
            importer.alphaSource = transparent || channel == "Surface" ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            importer.maxTextureSize = 2048; importer.mipmapEnabled = true; importer.wrapMode = TextureWrapMode.Repeat;
            importer.anisoLevel = 4; importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport(); return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Material GetMaterial(string name)
        {
            string path = Root+"/Materials/"+name+".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
            mat.name = name; mat.SetColor("_BaseColor", Color.white); mat.SetFloat("_Metallic", 0); return mat;
        }

        private static Material ImportMaterial(string stem)
        {
            Material mat = GetMaterial(stem);
            mat.SetTexture("_BaseMap", ImportTexture(stem, "Color")); mat.SetTexture("_BumpMap", ImportTexture(stem, "Normal"));
            mat.SetTexture("_MetallicGlossMap", ImportTexture(stem, "Surface"));
            mat.SetFloat("_BumpScale", 1); mat.SetFloat("_Smoothness", 1); mat.SetFloat("_SmoothnessTextureChannel", 0);
            mat.EnableKeyword("_NORMALMAP"); mat.EnableKeyword("_METALLICSPECGLOSSMAP"); Save(mat); return mat;
        }

        private static void Save(Material mat) { EditorUtility.SetDirty(mat); AssetDatabase.SaveAssetIfDirty(mat); }

        private static void Audit(GameObject root, Geometry expected)
        {
            if (root.GetComponentsInChildren<Collider>(true).Length != 0) throw new InvalidOperationException("沙漠美术不能加入玩法碰撞。");
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Any(r => r.sharedMaterials.Length != 1 || r.sharedMaterial == null || r.sharedMaterial.shader.name != "Universal Render Pipeline/Lit" ||
                !new[] { "NW6_Ground", "NW6_Sandstone", "NW6_Track", "NW6_Scrub" }.Contains(r.sharedMaterial.name)))
                throw new InvalidOperationException("沙漠材质回读不匹配。");
            Bounds bounds = renderers[0].bounds; foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);
            MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
            int triangles = filters.Sum(f => Enumerable.Range(0, f.sharedMesh.subMeshCount).Sum(i => (int)f.sharedMesh.GetIndexCount(i)/3));
            Vector3 Convert(float[] p) => new(p[0], p[2], p[1]);
            if (triangles != expected.triangles || filters.Length != expected.meshCount ||
                Vector3.Distance(bounds.min, Convert(expected.minimumBlender)) > .003f || Vector3.Distance(bounds.max, Convert(expected.maximumBlender)) > .003f)
                throw new InvalidOperationException("沙漠往返几何不匹配。");
            foreach (var axis in new[] { ("Right", Vector3.right), ("Up", Vector3.up), ("Forward", Vector3.forward) })
                if (Vector3.Distance(root.GetComponentsInChildren<Transform>(true).Single(t => t.name == "NW6_Axis"+axis.Item1).position, axis.Item2) > .002f)
                    throw new InvalidOperationException("沙漠 FBX 轴向不匹配："+axis.Item1);
        }
    }
}
