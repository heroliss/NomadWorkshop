using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Game.NomadWorkshop.Foundation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.NomadWorkshop.Editor
{
    /// <summary>
    /// 暖色工坊样板的显式导入/接线入口。只写游戏自有 ArtFirstPass 资产和独立场景；
    /// 先验证 Blender manifest 与文件哈希，再建立材质、可见 Prefab 和布局定义副本。
    /// </summary>
    public static class NomadWarmWorkshopArtPipeline
    {
        public const string Root = "Assets/Game/NomadWorkshop/ArtFirstPass";
        public const string ScenePath = "Assets/Game/NomadWorkshop/Scenes/NomadWarmWorkshop.unity";
        public const string SourceFolder = "ArtPipelineOutput/NomadWarmPass/current";
        public const string ControllerPath = Root + "/ResidentWorkshop.controller";

        [Serializable] private sealed class Manifest
        {
            public string status;
            public string version;
            public string generatorSha256;
            public AssetRecord[] assets;
            public MaterialRecord[] materials;
        }
        [Serializable] private sealed class AssetRecord
        {
            public string name;
            public string file;
            public string sha256;
            public Geometry source;
            public AxisRecord[] anchors;
            public AxisRecord[] workAnchors;
        }
        [Serializable] private sealed class AxisRecord { public string name; public float[] position; }
        [Serializable] private sealed class Geometry
        {
            public int triangles;
            public float[] boundsBlender;
            public float[] minimumBlender;
            public float[] maximumBlender;
        }
        [Serializable] private sealed class MaterialRecord
        {
            public string name;
            public float[] color;
            public float metallic;
            public float roughness;
        }
        [Serializable] private sealed class AuditRecord
        {
            public string name;
            public Vector3 expectedSize;
            public Vector3 importedSize;
            public Vector3 expectedMinimum;
            public Vector3 importedMinimum;
            public Vector3 expectedMaximum;
            public Vector3 importedMaximum;
            public int triangles;
            public int expectedTriangles;
            public int colliderCount;
            public string[] pivotPositions;
            public string groundNormal;
            public bool passed;
        }
        [Serializable] private sealed class Report
        {
            public string status;
            public string scene;
            public AuditRecord[] assets;
            public bool manualVisualReviewRequired = true;
        }

        [MenuItem("Assets/SSFramework/游牧工坊/首版美术/导入并打开温暖工坊样板")]
        public static void ImportAndOpen()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("请在非 Play 且编译空闲时导入美术样板。");
            Scene current = SceneManager.GetActiveScene();
            if (current.isDirty) throw new InvalidOperationException("当前场景未保存，请先保存后再导入样板。");
            string json = File.ReadAllText(Path.Combine(SourceFolder, "manifest.json"));
            Manifest manifest = JsonUtility.FromJson<Manifest>(json);
            if (manifest == null || manifest.status != "passed" || manifest.assets == null || manifest.assets.Length != 9)
                throw new InvalidOperationException("Blender manifest 不是完整的九资产通过结果。");
            if (HashFile("Tools/ArtPipeline/Blender/blender_nomad_art_set.py") != manifest.generatorSha256)
                throw new InvalidOperationException("生成器已变化，请重新生成资产后再导入。");
            foreach (AssetRecord asset in manifest.assets)
            {
                if (Path.GetFileName(asset.file) != asset.file ||
                    HashFile(Path.Combine(SourceFolder, asset.file)) != asset.sha256)
                    throw new InvalidOperationException("Blender 文件名或哈希不匹配：" + asset.file);
            }
            EnsureFolder(Root + "/Models");
            EnsureFolder(Root + "/Materials");
            EnsureFolder(Root + "/Prefabs");
            EnsureFolder(Root + "/Definitions");
            NomadWorkshopSurfacePipeline.GenerateTextures();
            var materials = CreateMaterials(manifest.materials);
            var prefabs = new Dictionary<string, GameObject>(StringComparer.Ordinal);
            var audits = new List<AuditRecord>();
            foreach (AssetRecord asset in manifest.assets)
            {
                string modelPath = Root + "/Models/" + asset.file;
                File.Copy(Path.Combine(SourceFolder, asset.file), modelPath, true);
                AssetDatabase.ImportAsset(modelPath, ImportAssetOptions.ForceSynchronousImport);
                var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
                importer.animationType = ModelImporterAnimationType.None;
                importer.importAnimation = false;
                importer.importCameras = false;
                importer.importLights = false;
                importer.addCollider = false;
                importer.globalScale = 1f;
                importer.bakeAxisConversion = true;
                importer.isReadable = false;
                importer.meshCompression = ModelImporterMeshCompression.Off;
                importer.importNormals = ModelImporterNormals.Import;
                importer.importTangents = ModelImporterTangents.CalculateMikk;
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
                foreach (var entry in materials)
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), entry.Key), entry.Value);
                importer.SaveAndReimport();
                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                Scene preview = EditorSceneManager.NewPreviewScene();
                try
                {
                    GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model, preview);
                    // FBX 的轴转换可能保留在导入根。外层提供玩法要求的单位根，内部保留导入变换。
                    var prefabRoot = new GameObject("NW1_" + asset.name);
                    SceneManager.MoveGameObjectToScene(prefabRoot, preview);
                    instance.transform.SetParent(prefabRoot.transform, true);
                    if (asset.anchors == null || asset.anchors.Length != 3)
                        throw new InvalidOperationException("模型缺少三轴验收锚点：" + asset.name);
                    foreach (AxisRecord anchor in asset.anchors)
                    {
                        Transform marker = instance.GetComponentsInChildren<Transform>(true).Single(t => t.name == anchor.name);
                        Vector3 expectedPoint = new(anchor.position[0], anchor.position[1], anchor.position[2]);
                        if ((marker.position - expectedPoint).sqrMagnitude > .000004f)
                            throw new InvalidOperationException("模型朝向错误：" + anchor.name + " " + marker.position);
                    }
                    Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                    if (renderers.Length == 0) throw new InvalidOperationException("模型没有 Renderer：" + asset.name);
                    Bounds bounds = renderers[0].bounds;
                    foreach (Renderer renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                    foreach (Renderer renderer in renderers)
                        if (renderer.sharedMaterials.Any(m => m != null && m.name == "NW1_Glass"))
                            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    foreach (Transform socket in instance.GetComponentsInChildren<Transform>(true)
                                 .Where(t => t.name.StartsWith("WorkLamp_", StringComparison.Ordinal) && t.childCount > 0))
                    {
                        Light lamp = socket.gameObject.AddComponent<Light>();
                        lamp.type = LightType.Point;
                        lamp.color = new Color(1f, .59f, .29f);
                        lamp.intensity = .22f;
                        lamp.range = 1.8f;
                        lamp.shadows = LightShadows.None;
                    }
                    Vector3 expected = new(asset.source.boundsBlender[0], asset.source.boundsBlender[2], asset.source.boundsBlender[1]);
                    Vector3 minimum = new(asset.source.minimumBlender[0], asset.source.minimumBlender[2], asset.source.minimumBlender[1]);
                    Vector3 maximum = new(asset.source.maximumBlender[0], asset.source.maximumBlender[2], asset.source.maximumBlender[1]);
                    int triangles = instance.GetComponentsInChildren<MeshFilter>(true).Sum(filter =>
                    {
                        Mesh mesh = filter.sharedMesh;
                        long indices = 0;
                        for (int i = 0; i < mesh.subMeshCount; i++) indices += mesh.GetIndexCount(i);
                        return checked((int)(indices / 3));
                    });
                    bool passed = (bounds.size - expected).sqrMagnitude < 0.0004f &&
                        (bounds.min - minimum).sqrMagnitude < 0.0004f && (bounds.max - maximum).sqrMagnitude < 0.0004f &&
                        triangles == asset.source.triangles &&
                        instance.GetComponentsInChildren<Collider>(true).Length == 0 &&
                        instance.GetComponentsInChildren<MeshFilter>(true).Where(f => f.name.Contains("NW1_Ground"))
                            .All(f => f.sharedMesh.normals.All(n => f.transform.TransformDirection(n).y > .8f));
                    audits.Add(new AuditRecord { name = asset.name, expectedSize = expected,
                        importedSize = bounds.size, triangles = triangles, expectedTriangles = asset.source.triangles,
                        expectedMinimum = minimum, importedMinimum = bounds.min,
                        expectedMaximum = maximum, importedMaximum = bounds.max,
                        colliderCount = instance.GetComponentsInChildren<Collider>(true).Length,
                        groundNormal = string.Join(";", instance.GetComponentsInChildren<MeshFilter>(true)
                            .Where(f => f.name.Contains("NW1_Ground"))
                            .Select(f => f.transform.TransformDirection(f.sharedMesh.normals[0]).ToString("F3"))),
                        pivotPositions = instance.GetComponentsInChildren<Transform>(true)
                            .Where(t => t.name.Contains("_Wheel_", StringComparison.Ordinal) && t.GetComponent<Renderer>() == null)
                            .Select(t => t.name + "=" + t.position.ToString("F3")).ToArray(), passed = passed });
                    WriteReport(passed ? "import-in-progress" : "failed-import", audits);
                    if (!passed) throw new InvalidOperationException("Unity 回读尺寸/三角形/Collider 不匹配：" +
                        asset.name + " expected=" + expected + " actual=" + bounds.size +
                        " triangles=" + triangles + "/" + asset.source.triangles);
                    string prefabPath = Root + "/Prefabs/NW1_" + asset.name + ".prefab";
                    ConfigureFacilityWork(prefabRoot, asset, materials);
                    GameObject prefab = PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath, out bool success);
                    if (!success) throw new InvalidOperationException("Prefab 保存失败：" + prefabPath);
                    prefabs.Add(asset.name, prefab);
                }
                finally { EditorSceneManager.ClosePreviewScene(preview); }
            }
            File.WriteAllText(Root + "/source-manifest.json", json);
            AssetDatabase.ImportAsset(Root + "/source-manifest.json");
            AnimatorController controller = CreateController();
            GameObject resident = NomadWorkshopResidentArtPipeline.Generate();
            GameObject kitchen = CreateKitchen(materials);
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null &&
                !AssetDatabase.CopyAsset(NomadFoundationVerticalSlicePipeline.ScenePath, ScenePath))
                throw new InvalidOperationException("无法从 Foundation 创建独立样板场景。");
            Scene sample = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            NomadFoundationWorldView view = FindOne<NomadFoundationWorldView>(sample);
            NomadFoundationSystem system = FindOne<NomadFoundationSystem>(sample);
            var viewData = new SerializedObject(view);
            SerializedProperty sourceDefinitions = viewData.FindProperty("facilityDefinitions");
            var definitions = new List<NomadFacilityDefinition>();
            for (int i = 0; i < sourceDefinitions.arraySize; i++)
            {
                var source = (NomadFacilityDefinition)sourceDefinitions.GetArrayElementAtIndex(i).objectReferenceValue;
                string sourcePath = AssetDatabase.GetAssetPath(source);
                string destination = Root + "/Definitions/" + Path.GetFileName(sourcePath);
                if (sourcePath != destination)
                {
                    if (AssetDatabase.LoadAssetAtPath<NomadFacilityDefinition>(destination) == null)
                        AssetDatabase.CopyAsset(sourcePath, destination);
                    source = AssetDatabase.LoadAssetAtPath<NomadFacilityDefinition>(destination);
                }
                NomadWaterCanHandlingPipeline.EnsureAccessGroup(source);
                var definitionData = new SerializedObject(source);
                GameObject art = source.Function switch
                {
                    NomadFacilityFunction.VehicleWaterTank => prefabs["WaterTank"],
                    NomadFacilityFunction.DrinkingStation => prefabs["Dispenser"],
                    NomadFacilityFunction.Toilet => prefabs["Toilet"],
                    NomadFacilityFunction.HobbyPoint => prefabs["Easel"],
                    NomadFacilityFunction.DriverStation => prefabs["Driver"],
                    NomadFacilityFunction.FieldKitchen => kitchen,
                    _ => null
                };
                if (art != null) definitionData.FindProperty("prefab").objectReferenceValue = art;
                Vector2? initialPosition = source.Function switch
                {
                    NomadFacilityFunction.DrinkingStation => new Vector2(-1.7f, 1.0f),
                    NomadFacilityFunction.FieldKitchen => new Vector2(-3.15f, -.75f),
                    NomadFacilityFunction.Toilet => new Vector2(4.65f, -1.3f),
                    NomadFacilityFunction.HobbyPoint => new Vector2(.8f, -2f),
                    _ => null
                };
                if (initialPosition.HasValue)
                {
                    definitionData.FindProperty("placeAtStart").boolValue = true;
                    definitionData.FindProperty("startPositionMeters").vector2Value = initialPosition.Value;
                }
                definitionData.ApplyModifiedPropertiesWithoutUndo();
                definitions.Add(source);
            }
            SetDefinitions(viewData, definitions);
            viewData.FindProperty("vehicleVisualPrefab").objectReferenceValue = prefabs["Vehicle"];
            viewData.FindProperty("environmentVisualPrefab").objectReferenceValue = prefabs["Desert"];
            viewData.FindProperty("waystationVisualPrefab").objectReferenceValue = prefabs["Waystation"];
            viewData.FindProperty("residentVisualPrefab").objectReferenceValue = resident;
            viewData.FindProperty("residentAnimationController").objectReferenceValue = controller;
            viewData.ApplyModifiedPropertiesWithoutUndo();
            var systemData = new SerializedObject(system);
            SetDefinitions(systemData, definitions);
            systemData.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(sample);
            EditorSceneManager.SaveScene(sample);
            AssetDatabase.SaveAssets();
            WriteReport("passed-import-only", audits);
            Debug.Log("[NomadWarmArt] READY：九类模型尺寸/面数/三轴锚点回读通过，独立场景已保存；等待实际画面与动作检查。");
        }

        private static void WriteReport(string status, List<AuditRecord> audits)
        {
            Directory.CreateDirectory("Logs/AIValidation/nomad-warm-art");
            File.WriteAllText("Logs/AIValidation/nomad-warm-art/import-report.json",
                JsonUtility.ToJson(new Report { status = status, scene = ScenePath, assets = audits.ToArray() }, true));
        }

        private static void ConfigureFacilityWork(GameObject root, AssetRecord asset,
            Dictionary<string, Material> materials)
        {
            bool isTank = asset.name == "WaterTank";
            if (!isTank && asset.name != "Dispenser") return;
            string[] required = isTank ? new[] { "Valve", "ServiceDoor", "FillLid", "Outlet", "Inlet", "ConditionIndicator" }
                : new[] { "Valve", "FillLid", "Inlet" };
            if (asset.workAnchors == null || required.Any(label => asset.workAnchors.Count(a =>
                    a.name == "NW1_" + asset.name + "_Work_" + label) != 1) ||
                asset.workAnchors.Select(a => a.name).Distinct(StringComparer.Ordinal).Count() != asset.workAnchors.Length)
                throw new InvalidOperationException($"{asset.name} 的必需工作锚点缺失或重复；附加点允许存在。");
            Transform[] parts = root.GetComponentsInChildren<Transform>(true);
            foreach (AxisRecord anchor in asset.workAnchors)
            {
                Transform part = parts.Single(t => t.name == anchor.name);
                Vector3 wanted = new(anchor.position[0], anchor.position[1], anchor.position[2]);
                if (Vector3.Distance(root.transform.InverseTransformPoint(part.position), wanted) > .002f)
                    throw new InvalidOperationException("设施工作锚点位置不一致：" + anchor.name);
            }
            Transform Part(string label) => parts.Single(t => t.name == "NW1_" + asset.name + "_Work_" + label);
            var rig = root.AddComponent<FoundationFacilityArtRig>();
            if (isTank)
                rig.ConfigureTank(Part("Valve"), Part("ServiceDoor"), Part("FillLid"), Part("Outlet"), Part("Inlet"),
                    Part("ConditionIndicator").GetComponentsInChildren<Renderer>().Single(),
                    materials["NW1_Rubber"], materials["NW1_Water"]);
            else
                rig.ConfigureDispenser(Part("Valve"), Part("FillLid"), Part("Inlet"),
                    materials["NW1_Rubber"], materials["NW1_Water"]);
        }

        private static Dictionary<string, Material> CreateMaterials(MaterialRecord[] records)
        {
            var result = new Dictionary<string, Material>(StringComparer.Ordinal);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("缺少 URP/Lit。");
            foreach (MaterialRecord record in records)
            {
                string path = Root + "/Materials/" + record.name + ".mat";
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
                material.SetColor("_BaseColor", new Color(record.color[0], record.color[1], record.color[2]));
                material.SetFloat("_Metallic", record.metallic);
                material.SetFloat("_Smoothness", 1f - record.roughness);
                NomadWorkshopSurfacePipeline.Apply(material, record.name);
                if (record.name.EndsWith("_Foliage", StringComparison.Ordinal) || record.name.EndsWith("_Canvas", StringComparison.Ordinal))
                {
                    material.SetFloat("_Cull", 0f);
                    material.doubleSidedGI = true;
                }
                if (record.name.EndsWith("_Lamp", StringComparison.Ordinal))
                {
                    material.EnableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor", new Color(1f, 0.42f, 0.12f) * 1.5f);
                }
                if (record.name == "NW1_Glass")
                {
                    Color glass = material.GetColor("_BaseColor");
                    glass.a = .26f;
                    material.SetColor("_BaseColor", glass);
                    material.SetFloat("_Surface", 1f);
                    material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    material.SetFloat("_ZWrite", 0f);
                    material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                }
                EditorUtility.SetDirty(material);
                result.Add(record.name, material);
            }
            return result;
        }

        private static AnimatorController CreateController()
        {
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) == null &&
                !AssetDatabase.CopyAsset(NomadHumanoidAssetPipeline.ControllerPath, ControllerPath))
                throw new InvalidOperationException("无法建立项目动作 Controller 副本。");
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            AnimatorControllerLayer[] layers = controller.layers;
            layers[0].iKPass = true;
            controller.layers = layers;
            ConfigureGripLayer(controller);
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static GameObject CreateKitchen(Dictionary<string, Material> materials)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(NomadFieldKitchenPrototypePipeline.PrefabPath);
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(source, preview);
                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] slots = renderer.sharedMaterials;
                    for (int i = 0; i < slots.Length; i++)
                    {
                        string name = slots[i].name;
                        string key = name.Contains("StainlessSteel") ? "Ivory" : name.Contains("WornTeal") ? "Teal" :
                            name.Contains("SafetyOrange") ? "Orange" : name.Contains("Rubber") ? "Rubber" : "Frame";
                        slots[i] = materials["NW1_" + key];
                    }
                    renderer.sharedMaterials = slots;
                }
                GameObject kitchen = PrefabUtility.SaveAsPrefabAsset(instance, Root + "/Prefabs/NW1_Kitchen.prefab", out bool saved);
                if (!saved) throw new InvalidOperationException("样板厨房 Prefab 保存失败。");
                return kitchen;
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        private static void ConfigureGripLayer(AnimatorController controller)
        {
            string clipPath = Root + "/CarryGrip.anim";
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null) { clip = new AnimationClip { name = "CarryGrip", frameRate = 30 }; AssetDatabase.CreateAsset(clip, clipPath); }
            foreach (string finger in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
            {
                for (int joint = 1; joint <= 3; joint++)
                    AnimationUtility.SetEditorCurve(clip,
                        EditorCurveBinding.FloatCurve("", typeof(Animator), $"RightHand.{finger}.{joint} Stretched"),
                        AnimationCurve.Constant(0f,1f,finger == "Thumb" ? -.30f : -.60f));
                AnimationUtility.SetEditorCurve(clip,
                    EditorCurveBinding.FloatCurve("", typeof(Animator), $"RightHand.{finger}.Spread"),
                    AnimationCurve.Constant(0f,1f,0f));
            }
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip,settings);
            string maskPath = Root + "/CarryFingers.mask";
            AvatarMask mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(maskPath);
            if (mask == null) { mask = new AvatarMask { name = "CarryFingers" }; AssetDatabase.CreateAsset(mask,maskPath); }
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
                mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, i == (int)AvatarMaskBodyPart.RightFingers);
            AnimatorControllerLayer[] layers = controller.layers;
            int index = Array.FindIndex(layers,l => l.name == FoundationResidentCarryIK.GripLayerName);
            if (index < 0)
            {
                var machine = new AnimatorStateMachine { name = FoundationResidentCarryIK.GripLayerName };
                AssetDatabase.AddObjectToAsset(machine,controller);
                controller.AddLayer(new AnimatorControllerLayer { name = FoundationResidentCarryIK.GripLayerName,
                    stateMachine = machine, blendingMode = AnimatorLayerBlendingMode.Override });
                layers = controller.layers;
                index = layers.Length-1;
            }
            layers[index].avatarMask = mask;
            layers[index].defaultWeight = 0f;
            layers[index].iKPass = false;
            AnimatorState state = layers[index].stateMachine.states.Select(s => s.state).SingleOrDefault(s => s.name == "Grip");
            if (state == null) state = layers[index].stateMachine.AddState("Grip");
            state.motion = clip;
            state.writeDefaultValues = false;
            layers[index].stateMachine.defaultState = state;
            controller.layers = layers;
            EditorUtility.SetDirty(clip);
            EditorUtility.SetDirty(mask);
        }

        private static void SetDefinitions(SerializedObject target, List<NomadFacilityDefinition> definitions)
        {
            SerializedProperty array = target.FindProperty("facilityDefinitions");
            array.arraySize = definitions.Count;
            for (int i = 0; i < definitions.Count; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = definitions[i];
        }

        private static T FindOne<T>(Scene scene) where T : Component =>
            scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).Single();

        private static string HashFile(string path)
        {
            using SHA256 sha = SHA256.Create();
            using FileStream stream = File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        private static void EnsureFolder(string path)
        {
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
