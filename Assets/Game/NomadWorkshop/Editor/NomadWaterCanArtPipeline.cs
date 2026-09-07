using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Game.NomadWorkshop.Foundation;
using Game.NomadWorkshop.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Game.NomadWorkshop.Editor
{
    /// <summary>导入已往返核验的容器模型及其绑定，在参考车辆与三款工装楼梯中替换视觉资产。</summary>
    public static class NomadWaterCanArtPipeline
    {
        public const string Root = "Assets/Game/NomadWorkshop/ArtFirstPass";
        public const string PrefabPath = Root + "/Prefabs/NW11_WaterCan.prefab";
        private const string Source = "ArtPipelineOutput/WaterCan/v03";
        private const string Stem = "NW11_WaterCan";

#pragma warning disable CS0649
        [Serializable] private sealed class Manifest
        {
            public string version, status;
            public int atlasSize;
            public Record[] sources, files;
            public Asset[] assets;
            public Binding binding;
        }
        [Serializable] private sealed class Record { public string file, sha256; }
        [Serializable] private sealed class Asset { public string name; public Geometry source, roundTrip; }
        [Serializable] private sealed class Geometry
        {
            public int triangles, meshCount, degenerateTriangles;
            public bool uvComplete;
            public float[] minimumBlender, maximumBlender;
        }
        [Serializable] private sealed class Binding
        {
            public string carryPivot, rightPalm, opening, closure, fillIndicator;
            public float[] groundReachOffset;
            public Volume[] clearances;
        }
        [Serializable] private sealed class Volume { public string marker; public float[] size; }
#pragma warning restore CS0649

        [MenuItem("Assets/SSFramework/游牧工坊/首版美术/导入手提水罐并打开参考车辆")]
        public static void ImportAndOpen()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("请在非 Play 且编译空闲时导入水罐。");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("存在未保存场景。");
            string json = File.ReadAllText(Source + "/manifest.json");
            var manifest = JsonUtility.FromJson<Manifest>(json);
            if (manifest == null || manifest.version != "0.1.1" || manifest.status != "passed-export" ||
                manifest.atlasSize != 2048 || manifest.assets?.Length != 1 || manifest.assets[0].name != Stem)
                throw new InvalidOperationException("水罐导出证据不完整。");
            CheckFiles(manifest.sources, "Tools/ArtPipeline/Blender", new[]
            {
                "blender_nomad_watercan.py", "blender_nomad_water_facilities.py", "blender_nomad_vehicle_forms.py",
                "blender_nomad_form_study.py", "blender_nomad_art_set.py", "blender_nomad_deck_cockpit.py", "blender_nomad_canopy.py"
            });
            CheckFiles(manifest.files, Source, new[] { Stem + ".fbx", Stem + "_Color.png", Stem + "_Normal.png", Stem + "_Surface.png" });
            foreach (var geometry in new[] { manifest.assets[0].source, manifest.assets[0].roundTrip })
                if (geometry == null || geometry.triangles <= 0 || geometry.meshCount <= 0 || !geometry.uvComplete ||
                    geometry.degenerateTriangles != 0 || geometry.minimumBlender?.Length != 3 || geometry.maximumBlender?.Length != 3 ||
                    geometry.minimumBlender.Concat(geometry.maximumBlender).Any(v => !float.IsFinite(v)))
                    throw new InvalidOperationException("水罐网格证据无效。");
            if (manifest.assets[0].source.triangles != manifest.assets[0].roundTrip.triangles ||
                manifest.binding?.clearances == null || manifest.binding.clearances.Length == 0)
                throw new InvalidOperationException("水罐 FBX 往返面数或绑定不完整。");

            Material atlas = ImportMaterial();
            string modelPath = Root + "/Models/" + Stem + ".fbx";
            File.Copy(Source + "/" + Stem + ".fbx", modelPath, true);
            AssetDatabase.ImportAsset(modelPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
            importer.animationType = ModelImporterAnimationType.None; importer.importAnimation = false;
            importer.importCameras = false; importer.importLights = false; importer.addCollider = false;
            importer.globalScale = 1f; importer.bakeAxisConversion = true; importer.isReadable = false;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.importNormals = ModelImporterNormals.Import; importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard; importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), atlas.name), atlas);
            importer.SaveAndReimport();

            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject(Stem); SceneManager.MoveGameObjectToScene(root, preview);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath), preview);
                model.transform.SetParent(root.transform, false);
                AuditModel(root, manifest.assets[0].source);
                Transform Marker(string name) => root.GetComponentsInChildren<Transform>(true).Single(t => t.name == name);
                foreach (var axis in new[] { ("_AxisRight", Vector3.right), ("_AxisUp", Vector3.up), ("_AxisForward", Vector3.forward) })
                    if (Vector3.Distance(root.transform.InverseTransformPoint(Marker(Stem + axis.Item1).position), axis.Item2) > .001f)
                        throw new InvalidOperationException("水罐导入轴向不一致：" + axis.Item1);
                Transform Frame(string name, Transform source)
                {
                    var frame = new GameObject(name).transform; frame.SetParent(root.transform, false);
                    frame.position = source.position; frame.rotation = root.transform.rotation;
                    return frame;
                }
                var binding = manifest.binding;
                var rig = root.AddComponent<FoundationCarriedContainerRig>();
                rig.Configure(Frame("携行枢轴", Marker(binding.carryPivot)), Frame("右掌接触", Marker(binding.rightPalm)),
                    Frame("液体开口", Marker(binding.opening)), Marker(binding.closure), Marker(binding.fillIndicator),
                    binding.clearances.Select((v, i) => new FoundationCarriedContainerRig.ClearanceVolume(
                        Frame("罐体避让-" + i, Marker(v.marker)), new Bounds(Vector3.zero, Vector(v.size)))).ToArray(),
                    Vector(binding.groundReachOffset));
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
            File.Copy(Source + "/manifest.json", Root + "/Models/NW11_manifest.json", true);
            AssetDatabase.ImportAsset(Root + "/Models/NW11_manifest.json", ImportAssetOptions.ForceSynchronousImport);
            // 场景切换后重新读取持久资产，避免保留被卸载的临时 Object 引用。
            foreach (string scenePath in new[]
            {
                "Assets/Game/NomadWorkshop/Scenes/ReferenceVehicleSample.unity",
                "Assets/Game/NomadWorkshop/Scenes/WorkwearMechanicStairs.unity",
                "Assets/Game/NomadWorkshop/Scenes/WorkwearCaretakerStairs.unity",
                "Assets/Game/NomadWorkshop/Scenes/WorkwearDriverStairs.unity"
            })
            {
                Scene scene = EditorSceneManager.OpenScene(scenePath);
                bool workshop = scenePath.EndsWith("ReferenceVehicleSample.unity", StringComparison.Ordinal);
                Component view = workshop ? UnityEngine.Object.FindFirstObjectByType<NomadFoundationWorldView>() :
                    UnityEngine.Object.FindFirstObjectByType<NomadStairTraversalView>();
                var serialized = new SerializedObject(view);
                serialized.FindProperty(workshop ? "waterCanVisualPrefab" : "containerPrefab").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            }
            EditorSceneManager.OpenScene("Assets/Game/NomadWorkshop/Scenes/ReferenceVehicleSample.unity");
            Debug.Log("[NomadWaterCan] 已导入可分件水罐与模型绑定，参考车辆和三款工装楼梯待实机验收。");
        }

        private static Vector3 Vector(float[] value)
        {
            if (value?.Length != 3 || value.Any(v => !float.IsFinite(v))) throw new InvalidOperationException("容器绑定向量无效。");
            return new Vector3(value[0], value[1], value[2]);
        }

        private static void CheckFiles(Record[] records, string folder, string[] required)
        {
            if (records == null) throw new InvalidOperationException("缺少水罐文件记录。");
            foreach (string name in required)
            {
                var matches = records.Where(r => r.file == name).ToArray();
                if (matches.Length != 1) throw new InvalidOperationException("水罐文件缺失或重复：" + name);
                string path = Path.Combine(folder, name);
                using var hash = SHA256.Create();
                string actual = BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
                if (actual != matches[0].sha256) throw new InvalidOperationException("水罐文件已变化，请重新导出：" + name);
            }
        }

        private static Material ImportMaterial()
        {
            var textures = new Texture2D[3]; string[] channels = { "Color", "Normal", "Surface" };
            for (int i = 0; i < channels.Length; i++)
            {
                string file = Stem + "_" + channels[i] + ".png", path = Root + "/Textures/" + file;
                File.Copy(Source + "/" + file, path, true); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = i == 1 ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.spriteImportMode = SpriteImportMode.None; importer.sRGBTexture = i == 0;
                importer.isReadable = false; importer.alphaIsTransparency = false; importer.maxTextureSize = 2048;
                importer.alphaSource = i == 0 ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput;
                importer.mipmapEnabled = true; importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                var data = new SerializedObject(importer);
                foreach (string field in new[] { "m_SpriteSheet.m_Sprites", "m_SpriteSheet.m_NameFileIdTable", "m_InternalIDToNameTable" })
                {
                    var property = data.FindProperty(field);
                    if (property == null || !property.isArray) throw new InvalidOperationException("Unity 纹理字段已改变：" + field);
                    property.ClearArray();
                }
                data.ApplyModifiedPropertiesWithoutUndo(); importer.SaveAndReimport();
                textures[i] = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            string materialPath = Root + "/Materials/" + Stem + "Atlas.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) throw new InvalidOperationException("缺少 URP Lit。");
                material = new Material(shader); AssetDatabase.CreateAsset(material, materialPath);
            }
            material.name = Stem + "Atlas"; material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", textures[0]); material.SetTexture("_BumpMap", textures[1]);
            material.SetTexture("_MetallicGlossMap", textures[2]); material.SetFloat("_BumpScale", 1);
            material.SetFloat("_Metallic", 1); material.SetFloat("_Smoothness", 1); material.SetFloat("_SmoothnessTextureChannel", 0);
            material.EnableKeyword("_NORMALMAP"); material.EnableKeyword("_METALLICSPECGLOSSMAP");
            EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material); return material;
        }

        private static void AuditModel(GameObject root, Geometry geometry)
        {
            var filters = root.GetComponentsInChildren<MeshFilter>(true);
            if (root.GetComponentsInChildren<Collider>(true).Length != 0 || filters.Length != geometry.meshCount ||
                filters.Sum(f => Enumerable.Range(0, f.sharedMesh.subMeshCount).Sum(i => (int)f.sharedMesh.GetIndexCount(i)/3)) != geometry.triangles ||
                filters.Any(f => !f.sharedMesh.HasVertexAttribute(VertexAttribute.TexCoord0)))
                throw new InvalidOperationException("水罐 Unity 网格、UV 或 Collider 不符合交付。");
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true); Bounds bounds = renderers[0].bounds;
            foreach (var r in renderers)
            {
                bounds.Encapsulate(r.bounds);
                if (r.sharedMaterials.Any(m => m == null || m.shader.name != "Universal Render Pipeline/Lit"))
                    throw new InvalidOperationException("水罐存在缺失或错误材质。");
            }
            Vector3 Convert(float[] p) => new(p[0], p[2], p[1]);
            if (Vector3.Distance(bounds.min, Convert(geometry.minimumBlender)) > .003f ||
                Vector3.Distance(bounds.max, Convert(geometry.maximumBlender)) > .003f)
                throw new InvalidOperationException("水罐 Unity 尺度不符合 Blender。");
            var definition = AssetDatabase.LoadAssetAtPath<NomadWorldItemDefinition>(
                "Assets/Game/NomadWorkshop/Foundation/Definitions/NW_Item_WaterCan.asset");
            var footprint = definition.FootprintSizeMeters;
            if (bounds.min.y < -.001f || bounds.max.y > definition.HeightMeters ||
                bounds.min.x < -footprint.x/2 || bounds.max.x > footprint.x/2 ||
                bounds.min.z < -footprint.y/2 || bounds.max.z > footprint.y/2)
                throw new InvalidOperationException("新水罐超出现有物品空间定义，需要先处理布局兼容性。");
        }
    }
}
