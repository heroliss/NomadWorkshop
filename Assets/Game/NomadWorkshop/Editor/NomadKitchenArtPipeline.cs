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
    /// <summary>导入自有厨房配方与显式台面空间，保留原厨房和现有玩法；只在非 Play 的干净场景中执行。</summary>
    public static class NomadKitchenArtPipeline
    {
        public const string Root = "Assets/Game/NomadWorkshop/ArtFirstPass";
        public const string PrefabPath = Root + "/Prefabs/NW9_Kitchen.prefab";
        public const string DefinitionPath = Root + "/Definitions/NW9_Facility_FieldKitchen.asset";
        private const string Source = "ArtPipelineOutput/Kitchen/v02";
        private const string Stem = "NW9_Kitchen";

#pragma warning disable CS0649
        [Serializable] private sealed class Manifest
        {
            public string version, status;
            public int atlasSize;
            public Record[] sources, files;
            public Asset[] assets;
        }
        [Serializable] private sealed class Record { public string file, sha256; }
        [Serializable] private sealed class Asset { public string name; public Geometry source, roundTrip; }
        [Serializable] private sealed class Geometry
        {
            public int triangles, meshCount, degenerateTriangles;
            public bool uvComplete;
            public float[] minimumBlender, maximumBlender;
        }
#pragma warning restore CS0649

        [MenuItem("Assets/SSFramework/游牧工坊/首版美术/导入小厨房并打开参考车辆")]
        public static void ImportAndOpen()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("请在非 Play 且编译空闲时导入厨房。");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("存在未保存场景。");
            string json = File.ReadAllText(Source + "/manifest.json");
            var manifest = JsonUtility.FromJson<Manifest>(json);
            if (manifest == null || manifest.version != "0.1.1" || manifest.status != "passed-export" ||
                manifest.atlasSize != 2048 || manifest.assets?.Length != 1 || manifest.assets[0].name != Stem)
                throw new InvalidOperationException("厨房导出证据不完整。");
            CheckFiles(manifest.sources, "Tools/ArtPipeline/Blender", new[]
            {
                "blender_nomad_kitchen.py", "blender_nomad_water_facilities.py", "blender_nomad_vehicle_forms.py",
                "blender_nomad_form_study.py", "blender_nomad_art_set.py", "blender_nomad_deck_cockpit.py", "blender_nomad_canopy.py"
            });
            CheckFiles(manifest.files, Source, new[] { Stem + ".fbx", Stem + "_Color.png", Stem + "_Normal.png", Stem + "_Surface.png" });
            foreach (var geometry in new[] { manifest.assets[0].source, manifest.assets[0].roundTrip })
                if (geometry == null || geometry.triangles <= 0 || geometry.meshCount <= 0 || !geometry.uvComplete ||
                    geometry.degenerateTriangles != 0 || geometry.minimumBlender?.Length != 3 || geometry.maximumBlender?.Length != 3 ||
                    geometry.minimumBlender.Concat(geometry.maximumBlender).Any(v => !float.IsFinite(v)))
                    throw new InvalidOperationException("厨房网格证据无效。");
            if (manifest.assets[0].source.triangles != manifest.assets[0].roundTrip.triangles)
                throw new InvalidOperationException("厨房 FBX 往返面数不一致。");

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
            Material lamp = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/NW1_Lamp.mat");
            if (lamp == null) throw new InvalidOperationException("缺少暖灯材质。");
            foreach (Material material in new[] { atlas, lamp })
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), material.name), material);
            importer.SaveAndReimport();

            var original = AssetDatabase.LoadAssetAtPath<NomadFacilityDefinition>(Root + "/Definitions/NW_Facility_FieldKitchen.asset");
            if (original == null) throw new InvalidOperationException("缺少原厨房定义。");
            Scene preview = EditorSceneManager.NewPreviewScene();
            GameObject prefab;
            try
            {
                var root = new GameObject(Stem); SceneManager.MoveGameObjectToScene(root, preview);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath), preview);
                model.transform.SetParent(root.transform, false);
                AuditModel(root, manifest.assets[0].source);
                Transform Marker(string suffix) => root.GetComponentsInChildren<Transform>(true).Single(t => t.name == Stem + suffix);
                foreach (var axis in new[] { ("_AxisRight", Vector3.right), ("_AxisUp", Vector3.up), ("_AxisForward", Vector3.forward) })
                    if (Vector3.Distance(root.transform.InverseTransformPoint(Marker(axis.Item1).position), axis.Item2) > .001f)
                        throw new InvalidOperationException("厨房导入轴向不一致：" + axis.Item1);
                var baseline = original.CaptureSpaceSnapshot();
                var space = root.AddComponent<FoundationFacilitySpaceAuthoring>();
                var frame = Node("可用备餐面", Marker("_Countertop"), Vector3.zero, 0);
                frame.rotation = root.transform.rotation;
                var footprint = baseline.Footprints.Select((p, i) => new FoundationFacilitySpaceAuthoring.Footprint
                {
                    id = "body-" + i, sizeMeters = p.SizeMeters,
                    frame = Node("占地-" + i, root.transform, new Vector3(p.LocalCenterMeters.x, 0, p.LocalCenterMeters.y), p.LocalYawDegrees)
                }).ToArray();
                var groups = baseline.Groups.Select(g =>
                {
                    var owner = Node(g.GroupId, root.transform, Vector3.zero, 0);
                    var group = owner.gameObject.AddComponent<FacilityInteractionGroup>();
                    var slots = g.AlternativeSlots.Select(s =>
                    {
                        var t = Node(s.SlotId, owner, new Vector3(s.LocalPositionMeters.x, 0, s.LocalPositionMeters.y), s.LocalYawDegrees);
                        var slot = t.gameObject.AddComponent<FacilityInteractionSlot>();
                        slot.ConfigureRuntime(s.SlotId, ResidentAnimationSemantic.Pickup, null);
                        return slot;
                    }).ToArray();
                    group.ConfigureRuntime(g.GroupId, g.RequiredForOperation, slots); return group;
                }).ToArray();
                space.ConfigureForEditor(footprint, groups, baseline.Regions.Select(r => new FoundationFacilitySpaceAuthoring.Region
                {
                    id = r.RegionId, frame = frame, sizeMeters = r.SizeMeters, edgeInsetMeters = r.EdgeInsetMeters,
                    acceptedCategories = r.AcceptedCategories.ToArray()
                }).ToArray());
                // 这次替换保持原空间语义；未来若台面变化，应明确接受迁移边界，不能顺手改动尺寸。
                if (space.CreateSnapshot().Signature != baseline.Signature)
                    throw new InvalidOperationException("厨房台面或站位已偏离原空间，请先明确新的布局。");
                prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool saved);
                if (!saved) throw new InvalidOperationException("厨房 Prefab 保存失败。");
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
            if (AssetDatabase.LoadAssetAtPath<NomadFacilityDefinition>(DefinitionPath) == null &&
                !AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(original), DefinitionPath))
                throw new InvalidOperationException("厨房定义创建失败。");
            var candidate = AssetDatabase.LoadAssetAtPath<NomadFacilityDefinition>(DefinitionPath);
            NomadFacilitySpaceBaker.Bake(candidate, prefab);
            // 操作面朝向甲板中央，让默认俯瞰镜头看见工作区；存档中的实例姿态仍由存档恢复。
            var candidateData = new SerializedObject(candidate);
            candidateData.FindProperty("startYawDegrees").floatValue = 270f;
            candidateData.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(candidate);
            File.WriteAllText(Root + "/NW9-source-manifest.json", json);
            AssetDatabase.ImportAsset(Root + "/NW9-source-manifest.json");
            Scene scene = EditorSceneManager.OpenScene(NomadReferenceVehiclePipeline.ScenePath);
            foreach (var owner in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Component>(true))
                .Where(c => c is NomadFoundationSystem or NomadFoundationWorldView))
            {
                var data = new SerializedObject(owner); var definitions = data.FindProperty("facilityDefinitions"); int assigned = 0;
                for (int i = 0; i < definitions.arraySize; i++)
                {
                    var slot = definitions.GetArrayElementAtIndex(i);
                    if (slot.objectReferenceValue is NomadFacilityDefinition definition && definition.Id == "field-kitchen")
                    { slot.objectReferenceValue = AssetDatabase.LoadAssetAtPath<NomadFacilityDefinition>(DefinitionPath); assigned++; }
                }
                if (assigned != 1) throw new InvalidOperationException("参考车辆须含唯一厨房定义。");
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("参考车辆保存失败。");
            scene = EditorSceneManager.OpenScene(NomadReferenceVehiclePipeline.ScenePath);
            foreach (var owner in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Component>(true))
                .Where(c => c is NomadFoundationSystem or NomadFoundationWorldView))
            {
                var definitions = new SerializedObject(owner).FindProperty("facilityDefinitions"); int found = 0;
                for (int i = 0; i < definitions.arraySize; i++)
                    if (definitions.GetArrayElementAtIndex(i).objectReferenceValue is NomadFacilityDefinition definition &&
                        definition.Id == "field-kitchen")
                    {
                        definition.ValidateModelSpaceSnapshot();
                        if (AssetDatabase.GetAssetPath(definition) != DefinitionPath) throw new InvalidOperationException("厨房引用未持久化。");
                        found++;
                    }
                if (found != 1) throw new InvalidOperationException("保存回读厨房定义丢失。");
            }
            Debug.Log("小厨房已接入参考车辆：独立柜门、真实水槽、灶具与木质备餐台；待杯具和画面验收。");
        }

        private static Transform Node(string name, Transform parent, Vector3 position, float yaw)
        {
            var t = new GameObject(name).transform; t.SetParent(parent, false);
            t.SetLocalPositionAndRotation(position, Quaternion.Euler(0, yaw, 0)); return t;
        }

        private static void CheckFiles(Record[] records, string folder, string[] required)
        {
            foreach (string file in required)
            {
                var match = records?.Where(r => r.file == file).ToArray();
                if (match?.Length != 1 || !File.Exists(folder + "/" + file)) throw new InvalidOperationException("厨房来源缺失：" + file);
                using var sha = SHA256.Create();
                string hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(folder + "/" + file))).Replace("-", "").ToLowerInvariant();
                if (hash != match[0].sha256) throw new InvalidOperationException("厨房来源哈希变化：" + file);
            }
        }

        private static Material ImportMaterial() => NomadBakedPropTextureImporter.Import(Root, Source, Stem);

        private static void AuditModel(GameObject root, Geometry geometry)
        {
            var filters = root.GetComponentsInChildren<MeshFilter>(true);
            if (root.GetComponentsInChildren<Collider>(true).Length != 0 || filters.Length != geometry.meshCount ||
                filters.Sum(f => Enumerable.Range(0, f.sharedMesh.subMeshCount).Sum(i => (int)f.sharedMesh.GetIndexCount(i)/3)) != geometry.triangles ||
                filters.Any(f => !f.sharedMesh.HasVertexAttribute(VertexAttribute.TexCoord0)))
                throw new InvalidOperationException("厨房 Unity 网格、UV 或 Collider 不符合交付。");
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true); Bounds bounds = renderers[0].bounds;
            foreach (var r in renderers)
            {
                bounds.Encapsulate(r.bounds);
                if (r.sharedMaterials.Any(m => m == null || m.shader.name != "Universal Render Pipeline/Lit"))
                    throw new InvalidOperationException("厨房存在缺失或错误材质。");
            }
            Vector3 Convert(float[] p) => new(p[0], p[2], p[1]);
            if (Vector3.Distance(bounds.min, Convert(geometry.minimumBlender)) > .003f ||
                Vector3.Distance(bounds.max, Convert(geometry.maximumBlender)) > .003f)
                throw new InvalidOperationException("厨房 Unity 尺度不符合 Blender。");
        }
    }
}
