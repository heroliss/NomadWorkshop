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
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Game.NomadWorkshop.Editor
{
    /// <summary>
    /// 将已烘焙的水设施接入参考车辆。只负责这组资产的制作配方与显式接线；
    /// 空间生成、存档兼容和运行时动作仍复用既有机制。
    /// </summary>
    public static class NomadWaterFacilityArtPipeline
    {
        private const string Root = NomadWarmWorkshopArtPipeline.Root;
        private const string SourceFolder = "ArtPipelineOutput/WaterFacilities/v02";
        private const string Atlas = "NW8_WaterFacilities";
        private static readonly string[] Kinds = { "WaterTank", "Dispenser" };

#pragma warning disable CS0649 // JsonUtility 读取 Blender 导出契约。
        [Serializable] private sealed class Manifest
        {
            public string version, status;
            public int atlasSize;
            public FileRecord[] sources, files;
            public AssetRecord[] assets;
        }
        [Serializable] private sealed class FileRecord { public string file, sha256; }
        [Serializable] private sealed class AssetRecord { public string name; public Geometry source, roundTrip; }
        [Serializable] private sealed class Geometry
        {
            public int triangles, meshCount, degenerateTriangles;
            public bool uvComplete;
            public float[] minimumBlender, maximumBlender;
        }
#pragma warning restore CS0649

        [MenuItem("Assets/SSFramework/游牧工坊/首版美术/导入水设施并打开参考车辆")]
        public static void ImportAndOpen()
        {
            RequireIdle();
            string json = File.ReadAllText(SourceFolder + "/manifest.json");
            var manifest = JsonUtility.FromJson<Manifest>(json);
            if (manifest == null || manifest.version != "0.1.1" || manifest.status != "passed-export" ||
                manifest.atlasSize != 2048 || manifest.assets == null || manifest.assets.Length != 2 ||
                !manifest.assets.Select(a => a.name).OrderBy(n => n).SequenceEqual(Kinds.Select(k => "NW8_" + k).OrderBy(n => n)))
                throw new InvalidOperationException("水设施缺少完整导出证据。");
            ValidateFiles(manifest.sources, "Tools/ArtPipeline/Blender", new[]
            {
                "blender_nomad_water_facilities.py", "blender_nomad_vehicle_forms.py", "blender_nomad_form_study.py",
                "blender_nomad_art_set.py", "blender_nomad_deck_cockpit.py", "blender_nomad_canopy.py"
            });
            ValidateFiles(manifest.files, SourceFolder, Kinds.Select(k => "NW8_" + k + ".fbx").Concat(
                new[] { "Color", "Normal", "Surface" }.Select(c => Atlas + "_" + c + ".png")).ToArray());
            Material atlas = ImportMaterial();
            var replacements = new Dictionary<string, string>();
            foreach (string kind in Kinds)
            {
                var asset = manifest.assets.Single(a => a.name == "NW8_" + kind);
                ValidateGeometryRecord(asset);
                string modelPath = ImportModel(kind, atlas);
                string suffix = kind == "WaterTank" ? "VehicleWaterTank" : "DrinkingStation";
                string originalPath = Root + "/Definitions/NW_Facility_" + suffix + ".asset";
                var original = AssetDatabase.LoadAssetAtPath<NomadFacilityDefinition>(originalPath);
                if (original == null) throw new InvalidOperationException("缺少原水设施定义：" + suffix);
                GameObject prefab = BuildPrefab(kind, modelPath, asset.source, original);
                string definitionPath = Root + "/Definitions/NW8_Facility_" + suffix + ".asset";
                if (AssetDatabase.LoadAssetAtPath<NomadFacilityDefinition>(definitionPath) == null &&
                    !AssetDatabase.CopyAsset(originalPath, definitionPath))
                    throw new InvalidOperationException("水设施定义创建失败：" + definitionPath);
                var definition = AssetDatabase.LoadAssetAtPath<NomadFacilityDefinition>(definitionPath);
                NomadFacilitySpaceBaker.Bake(definition, prefab);
                replacements.Add(definition.Id, definitionPath);
            }
            File.WriteAllText(Root + "/NW8-source-manifest.json", json);
            AssetDatabase.ImportAsset(Root + "/NW8-source-manifest.json");
            Scene scene = EditorSceneManager.OpenScene(NomadReferenceVehiclePipeline.ScenePath);
            foreach (Component owner in Owners(scene))
            {
                var data = new SerializedObject(owner);
                var definitions = data.FindProperty("facilityDefinitions");
                var assigned = new HashSet<string>();
                for (int i = 0; i < definitions.arraySize; i++)
                {
                    var item = definitions.GetArrayElementAtIndex(i);
                    var old = item.objectReferenceValue as NomadFacilityDefinition;
                    if (old == null) throw new InvalidOperationException("参考场景存在丢失的设施定义。");
                    if (!replacements.TryGetValue(old.Id, out string path)) continue;
                    item.objectReferenceValue = AssetDatabase.LoadAssetAtPath<NomadFacilityDefinition>(path);
                    assigned.Add(old.Id);
                }
                if (assigned.Count != 2) throw new InvalidOperationException("参考场景没有完整水设施接线。");
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("参考车辆场景保存失败。");
            scene = EditorSceneManager.OpenScene(NomadReferenceVehiclePipeline.ScenePath);
            foreach (Component owner in Owners(scene))
            {
                var array = new SerializedObject(owner).FindProperty("facilityDefinitions");
                for (int i = 0; i < array.arraySize; i++)
                {
                    var definition = array.GetArrayElementAtIndex(i).objectReferenceValue as NomadFacilityDefinition;
                    if (definition == null) throw new InvalidOperationException("水设施保存回读丢失定义。");
                    definition.ValidateModelSpaceSnapshot();
                    if (replacements.TryGetValue(definition.Id, out string path) && AssetDatabase.GetAssetPath(definition) != path)
                        throw new InvalidOperationException("水设施保存回读仍连接旧资产。");
                }
            }
            Debug.Log("水设施已导入参考车辆：曲面储罐、独立检修舱与内凹饮水站；待实机动作和画面验收。");
        }

        private static IEnumerable<Component> Owners(Scene scene) => scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Component>(true))
            .Where(c => c is NomadFoundationWorldView or NomadFoundationSystem);

        private static void RequireIdle()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("请在非 Play 且编译空闲时导入水设施。");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("存在未保存场景，请先保存。");
        }

        private static void ValidateFiles(FileRecord[] records, string folder, string[] required)
        {
            foreach (string file in required)
            {
                var matches = records?.Where(r => r.file == file).ToArray();
                if (matches == null || matches.Length != 1 || !File.Exists(folder + "/" + file))
                    throw new InvalidOperationException("水设施来源文件缺失或重复：" + file);
                using var sha = SHA256.Create();
                string hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(folder + "/" + file)))
                    .Replace("-", "").ToLowerInvariant();
                if (hash != matches[0].sha256) throw new InvalidOperationException("水设施来源哈希不一致：" + file);
            }
        }

        private static Material ImportMaterial()
        {
            var textures = new Texture2D[3];
            string[] channels = { "Color", "Normal", "Surface" };
            for (int i = 0; i < channels.Length; i++)
            {
                string name = Atlas + "_" + channels[i] + ".png", path = Root + "/Textures/" + name;
                File.Copy(SourceFolder + "/" + name, path, true);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = i == 1 ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.spriteImportMode = SpriteImportMode.None;
                importer.sRGBTexture = i == 0; importer.isReadable = false;
                importer.alphaIsTransparency = false; importer.maxTextureSize = 2048;
                importer.mipmapEnabled = true; importer.wrapMode = TextureWrapMode.Clamp;
                importer.alphaSource = i == 0 ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                // 项目的默认纹理预设会生成 Sprite 身份；改回 Default 不会移除这些记录。
                // 仅清理由本配方拥有的图集，沿用已验证的 Unity 6 序列化边界。
                var data = new SerializedObject(importer);
                foreach (string propertyPath in new[] { "m_SpriteSheet.m_Sprites", "m_SpriteSheet.m_NameFileIdTable", "m_InternalIDToNameTable" })
                {
                    var property = data.FindProperty(propertyPath);
                    if (property == null || !property.isArray)
                        throw new InvalidOperationException("Unity 纹理导入序列化字段已变化：" + propertyPath);
                    property.ClearArray();
                }
                data.ApplyModifiedPropertiesWithoutUndo();
                importer.SaveAndReimport();
                textures[i] = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            string materialPath = Root + "/Materials/" + Atlas + "Atlas.mat";
            var result = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (result == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) throw new InvalidOperationException("缺少 URP Lit。");
                result = new Material(shader); AssetDatabase.CreateAsset(result, materialPath);
            }
            result.name = Atlas + "Atlas";
            result.SetColor("_BaseColor", Color.white); result.SetTexture("_BaseMap", textures[0]);
            result.SetTexture("_BumpMap", textures[1]); result.SetFloat("_BumpScale", 1f);
            result.SetTexture("_MetallicGlossMap", textures[2]);
            result.SetFloat("_Metallic", 1f); result.SetFloat("_Smoothness", 1f);
            result.SetFloat("_SmoothnessTextureChannel", 0f);
            result.EnableKeyword("_NORMALMAP"); result.EnableKeyword("_METALLICSPECGLOSSMAP");
            EditorUtility.SetDirty(result); AssetDatabase.SaveAssetIfDirty(result);
            return result;
        }

        private static string ImportModel(string kind, Material atlas)
        {
            string name = "NW8_" + kind + ".fbx", path = Root + "/Models/" + name;
            File.Copy(SourceFolder + "/" + name, path, true);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false; importer.importCameras = false; importer.importLights = false;
            importer.addCollider = false; importer.globalScale = 1f; importer.bakeAxisConversion = true;
            importer.isReadable = false; importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.importNormals = ModelImporterNormals.Import; importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            foreach (Material material in new[] { atlas, LoadMaterial("NW1_Lamp") })
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), material.name), material);
            importer.SaveAndReimport();
            return path;
        }

        private static Material LoadMaterial(string name) => AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/" + name + ".mat")
            ?? throw new InvalidOperationException("缺少设施材质：" + name);

        private static GameObject BuildPrefab(string kind, string modelPath, Geometry geometry, NomadFacilityDefinition original)
        {
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var wrapper = new GameObject("NW8_" + kind);
                SceneManager.MoveGameObjectToScene(wrapper, preview);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath), preview);
                model.transform.SetParent(wrapper.transform, true);
                AuditGeometry(wrapper, geometry);
                Transform Part(string suffix) => wrapper.GetComponentsInChildren<Transform>(true).Single(t => t.name == "NW8_" + kind + "_" + suffix);
                foreach (var axis in new[] { ("Right", Vector3.right), ("Up", Vector3.up), ("Forward", Vector3.forward) })
                    if (Vector3.Distance(Part("Axis" + axis.Item1).position, axis.Item2) > .002f)
                        throw new InvalidOperationException("水设施 FBX 轴向不匹配：" + kind + "/" + axis.Item1);
                var rig = wrapper.AddComponent<FoundationFacilityArtRig>();
                if (kind == "WaterTank")
                    rig.ConfigureTank(Part("Work_Valve"), Part("Work_ServiceDoor"), Part("Work_FillLid"), Part("Work_Outlet"),
                        Part("Work_Inlet"), Part("Work_ConditionIndicator").GetComponentsInChildren<Renderer>().Single(),
                        LoadMaterial("NW1_Rubber"), LoadMaterial("NW1_Water"));
                else rig.ConfigureDispenser(Part("Work_Valve"), Part("Work_FillLid"), Part("Work_Inlet"),
                    LoadMaterial("NW1_Rubber"), LoadMaterial("NW1_Water"));
                BuildSpace(wrapper, original, kind == "WaterTank" ? Part("Placement_MaintenanceTray") : null);
                string path = Root + "/Prefabs/NW8_" + kind + ".prefab";
                var result = PrefabUtility.SaveAsPrefabAsset(wrapper, path, out bool saved);
                if (!saved) throw new InvalidOperationException("水设施 Prefab 保存失败。");
                return result;
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        private static void BuildSpace(GameObject root, NomadFacilityDefinition original, Transform tray)
        {
            var authoring = root.AddComponent<FoundationFacilitySpaceAuthoring>();
            var baseline = original.CaptureSpaceSnapshot();
            Transform markers = Node("静态空间标记", root.transform, Vector3.zero, 0);
            Transform trayFrame = null;
            if (tray != null)
            {
                // FBX 空节点保留 Blender 的轴基；模型轴向已由三个独立标记校核。
                // 在它下面建立游戏的水平坐标系，位置继续跟随真实托盘，不旋转可见网格。
                trayFrame = Node("支撑平面", tray, Vector3.zero, 0);
                trayFrame.rotation = root.transform.rotation;
            }
            var parts = baseline.Footprints.Select((p, i) => new FoundationFacilitySpaceAuthoring.Footprint
            {
                id = "body-" + i, sizeMeters = tray != null ? new Vector2(1.8f, 1.2f) : p.SizeMeters,
                frame = Node("占地-" + i, markers, new Vector3(p.LocalCenterMeters.x, 0, p.LocalCenterMeters.y), p.LocalYawDegrees)
            }).ToArray();
            var groups = baseline.Groups.Select(g =>
            {
                Transform parent = Node(g.GroupId, markers, Vector3.zero, 0);
                var group = parent.gameObject.AddComponent<FacilityInteractionGroup>();
                var slots = g.AlternativeSlots.Select((s, index) =>
                {
                    var position = new Vector3(s.LocalPositionMeters.x, 0, s.LocalPositionMeters.y);
                    // 单手持罐从操作口左侧接近；检修位对准独立机柜，不再沿用旧罐体中心。
                    if (g.GroupId == "water-pickup") position = new Vector3(-.78f + index * .08f, 0, -.94f);
                    else if (g.GroupId == "drink-and-deliver") position = new Vector3(-.56f + index * .08f, 0, -.64f);
                    else if (g.GroupId == "service-valve") position = new Vector3(.4f + index * .3f, 0, .9f);
                    var node = Node(s.SlotId, parent, position, s.LocalYawDegrees);
                    var slot = node.gameObject.AddComponent<FacilityInteractionSlot>();
                    slot.ConfigureRuntime(s.SlotId, ResidentAnimationSemantic.Pickup, null);
                    return slot;
                }).ToArray();
                group.ConfigureRuntime(g.GroupId, g.RequiredForOperation, slots);
                return group;
            }).ToArray();
            var regions = baseline.Regions.Select(r => new FoundationFacilitySpaceAuthoring.Region
            {
                id = r.RegionId,
                frame = r.RegionId == "maintenance-tray" ? trayFrame : Node(r.RegionId, markers,
                    new Vector3(r.LocalCenterMeters.x, r.SupportHeightMeters, r.LocalCenterMeters.y), r.LocalYawDegrees),
                sizeMeters = r.RegionId == "maintenance-tray" ? new Vector2(.5f, .38f) : r.SizeMeters,
                edgeInsetMeters = r.EdgeInsetMeters, acceptedCategories = r.AcceptedCategories.ToArray()
            }).ToArray();
            authoring.ConfigureForEditor(parts, groups, regions);
            authoring.CreateSnapshot();
        }

        private static Transform Node(string name, Transform parent, Vector3 position, float yaw)
        {
            Transform node = new GameObject(name).transform;
            node.SetParent(parent, false); node.SetLocalPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
            return node;
        }

        private static void ValidateGeometryRecord(AssetRecord asset)
        {
            foreach (Geometry geometry in new[] { asset.source, asset.roundTrip })
                if (geometry == null || geometry.triangles <= 0 || geometry.meshCount <= 0 ||
                    geometry.degenerateTriangles != 0 || !geometry.uvComplete ||
                    geometry.minimumBlender?.Length != 3 || geometry.maximumBlender?.Length != 3 ||
                    geometry.minimumBlender.Concat(geometry.maximumBlender).Any(v => !float.IsFinite(v)))
                    throw new InvalidOperationException("水设施几何证据不完整：" + asset.name);
            if (asset.source.triangles != asset.roundTrip.triangles)
                throw new InvalidOperationException("水设施往返三角形数量不一致：" + asset.name);
        }

        private static void AuditGeometry(GameObject root, Geometry expected)
        {
            if (root.GetComponentsInChildren<Collider>(true).Length != 0)
                throw new InvalidOperationException("水设施美术不能隐式增加玩法 Collider。");
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            var meshes = root.GetComponentsInChildren<MeshFilter>(true);
            Bounds bounds = renderers.First().bounds;
            foreach (var renderer in renderers)
            {
                bounds.Encapsulate(renderer.bounds);
                if (renderer.sharedMaterials.Any(m => m == null || m.shader.name != "Universal Render Pipeline/Lit"))
                    throw new InvalidOperationException("水设施存在缺失或非 URP 材质。");
            }
            int triangles = meshes.Sum(f => Enumerable.Range(0, f.sharedMesh.subMeshCount).Sum(i => (int)f.sharedMesh.GetIndexCount(i) / 3));
            Vector3 Convert(float[] p) => new(p[0], p[2], p[1]);
            if (meshes.Length != expected.meshCount || triangles != expected.triangles ||
                meshes.Any(f => !f.sharedMesh.HasVertexAttribute(VertexAttribute.TexCoord0)) ||
                Vector3.Distance(bounds.min, Convert(expected.minimumBlender)) > .003f ||
                Vector3.Distance(bounds.max, Convert(expected.maximumBlender)) > .003f)
                throw new InvalidOperationException("水设施 Unity 网格、UV 或包围范围与 Blender 不一致。");
        }
    }
}
