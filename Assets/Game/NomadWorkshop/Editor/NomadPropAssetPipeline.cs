using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Game.NomadWorkshop.Editor
{
    /// <summary>
    /// 将 Blender Smoke 的 FBX 与 manifest 翻译成 Unity 可审计的材质、Prefab 和预览场景。
    /// 这是游戏本地、可删除的 Import Spike，不是全局 AssetPostprocessor 或正式道具生产规范。
    /// </summary>
    public static class NomadPropAssetPipeline
    {
        public const string AssetId = "NW_StorageCrate_01";
        public const string AssetRoot =
            "Assets/Game/NomadWorkshop/Spikes/BlenderImport/NW_StorageCrate_01";
        public const string ModelPath = AssetRoot + "/" + AssetId + ".fbx";
        public const string ManifestPath = AssetRoot + "/" + AssetId + ".manifest.json";
        public const string MaterialFolder = AssetRoot + "/Materials";
        public const string PrefabFolder = AssetRoot + "/Prefabs";
        public const string PrefabPath = PrefabFolder + "/" + AssetId + ".prefab";
        public const string PreviewFolder = AssetRoot + "/Preview";
        public const string PreviewScenePath = PreviewFolder + "/" + AssetId + "_ImportPreview.unity";

        private const string BlenderScriptPath = "Tools/ArtPipeline/Blender/blender_smoke.py";
        private const int SupportedManifestSchemaVersion = 1;
        private const string SupportedHarnessVersion = "0.2.0";
        private const string PreviewGroundMaterialPath = PreviewFolder + "/M_ImportPreviewGround.mat";
        private const float BoundsToleranceMeters = 0.005f;
        private const float MaxRuntimeVertexExpansion = 4f;

        /// <summary>
        /// 显式重放静态道具的 Importer、URP 材质映射、根级 Collider、Prefab 与预览场景生成，
        /// 并把本轮审计证据写到已忽略的 ArtPipelineOutput。
        /// </summary>
        [MenuItem("Assets/SSFramework/游牧工坊/Blender Import Spike/配置并审计储物箱")]
        public static void ConfigureAndReport()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出 Play Mode，再配置 Blender Import Spike。");

            BlenderManifest manifest = LoadManifest();
            ValidateManifest(manifest);
            EnsureFolder(MaterialFolder);
            EnsureFolder(PrefabFolder);
            EnsureFolder(PreviewFolder);

            IReadOnlyDictionary<string, Material> materials = CreateOrUpdateMaterials(manifest);
            ApplyModelImportPolicy(materials);
            CreateOrUpdatePrefab();
            CreateOrUpdatePreviewScene();
            AssetDatabase.SaveAssets();

            NomadPropAssetAudit audit = Audit();
            WriteReport(audit);
            if (!audit.Passed) throw new InvalidOperationException(audit.ToMultilineString());
            Debug.Log(audit.ToMultilineString());
        }

        /// <summary>只读取已经落盘的导入产物并返回结构化证据，不修改 Importer 或资产。</summary>
        public static NomadPropAssetAudit Audit()
        {
            BlenderManifest manifest = LoadManifest();
            ValidateManifest(manifest);
            var issues = new List<string>();

            bool sourceHashMatches = AuditSourceHash(manifest, issues);
            bool importerPolicyMatches = AuditImporter(manifest, issues);
            ModelAudit modelAudit = AuditModel(manifest, issues);
            bool materialContractMatches = AuditMaterials(manifest, modelAudit.MaterialAssetPaths, issues);
            bool prefabContractMatches = AuditPrefab(modelAudit.BoundsSize, issues);
            bool previewSceneExists = AuditPreviewScene(issues);

            return new NomadPropAssetAudit(
                sourceHashMatches,
                importerPolicyMatches,
                modelAudit.HierarchyMatches,
                modelAudit.GeometryMatches,
                materialContractMatches,
                prefabContractMatches,
                previewSceneExists,
                modelAudit.MeshObjectCount,
                modelAudit.SourceVertexCount,
                modelAudit.VertexCount,
                modelAudit.TriangleCount,
                modelAudit.ExpectedBoundsSize,
                modelAudit.BoundsSize,
                modelAudit.MaterialAssetPaths,
                GetRenderingVerdict(),
                issues);
        }

        private static IReadOnlyDictionary<string, Material> CreateOrUpdateMaterials(
            BlenderManifest manifest)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                throw new InvalidOperationException("当前项目找不到 Universal Render Pipeline/Lit Shader。");

            var result = new Dictionary<string, Material>(StringComparer.Ordinal);
            for (int i = 0; i < manifest.materials.Length; i++)
            {
                BlenderMaterial spec = manifest.materials[i];
                Color color = ToColor(spec.baseColorLinear, spec.name);
                string assetPath = GetMaterialPath(spec.name);
                Material material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
                if (material == null)
                {
                    material = new Material(shader) { name = spec.name };
                    AssetDatabase.CreateAsset(material, assetPath);
                }
                else
                {
                    material.shader = shader;
                }

                material.SetColor("_BaseColor", color);
                material.SetFloat("_Metallic", Mathf.Clamp01(spec.metallic));
                material.SetFloat("_Smoothness", 1f - Mathf.Clamp01(spec.roughness));
                material.enableInstancing = true;
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material);
                result.Add(spec.name, material);
            }

            return result;
        }

        private static void ApplyModelImportPolicy(
            IReadOnlyDictionary<string, Material> materials)
        {
            if (AssetImporter.GetAtPath(ModelPath) is not ModelImporter importer)
                throw new InvalidOperationException($"没有找到可配置的模型导入器：{ModelPath}");

            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importConstraints = false;
            importer.isReadable = false;
            importer.globalScale = 1f;
            importer.bakeAxisConversion = true;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.addCollider = false;
            importer.weldVertices = true;
            importer.generateSecondaryUV = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;

            foreach ((string sourceName, Material material) in materials)
            {
                var identifier =
                    new AssetImporter.SourceAssetIdentifier(typeof(Material), sourceName);
                importer.RemoveRemap(identifier);
                importer.AddRemap(identifier, material);
            }

            importer.SaveAndReimport();
        }

        private static void CreateOrUpdatePrefab()
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null) throw new InvalidOperationException($"无法加载模型主资产：{ModelPath}");

            Scene previewScene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject(AssetId);
                SceneManager.MoveGameObjectToScene(root, previewScene);
                GameObject visual = PrefabUtility.InstantiatePrefab(model, previewScene) as GameObject;
                if (visual == null) throw new InvalidOperationException("无法实例化 FBX 模型。");

                visual.name = "Visual";
                visual.transform.SetParent(root.transform, false);
                ResetLocalTransform(root.transform);
                ResetLocalTransform(visual.transform);

                Bounds bounds = CalculateLocalRendererBounds(root);
                BoxCollider collider = root.AddComponent<BoxCollider>();
                collider.center = bounds.center;
                collider.size = bounds.size;

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool success);
                if (!success) throw new InvalidOperationException($"保存 Prefab 失败：{PrefabPath}");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        private static void CreateOrUpdatePreviewScene()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) throw new InvalidOperationException($"无法加载生成的 Prefab：{PrefabPath}");

            Material groundMaterial = CreateOrUpdatePreviewGroundMaterial();
            Scene previousActiveScene = SceneManager.GetActiveScene();
            Scene previewScene =
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(previewScene);
                GameObject instance =
                    PrefabUtility.InstantiatePrefab(prefab, previewScene) as GameObject;
                if (instance == null) throw new InvalidOperationException("无法实例化生成的 Prefab。");
                instance.name = AssetId;
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ground.name = "PreviewGround";
                SceneManager.MoveGameObjectToScene(ground, previewScene);
                ground.transform.SetPositionAndRotation(new Vector3(0f, -0.05f, 0f), Quaternion.identity);
                ground.transform.localScale = new Vector3(4.2f, 0.1f, 4.2f);
                ground.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;
                UnityEngine.Object.DestroyImmediate(ground.GetComponent<Collider>());

                GameObject cameraObject = new("Main Camera");
                SceneManager.MoveGameObjectToScene(cameraObject, previewScene);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.tag = "MainCamera";
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.018f, 0.024f, 0.03f, 1f);
                camera.fieldOfView = 39f;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 50f;
                camera.allowHDR = true;
                cameraObject.transform.position = new Vector3(2.6f, 1.9f, -3.35f);
                cameraObject.transform.LookAt(new Vector3(0f, 0.48f, 0f));

                GameObject keyObject = new("Key Light");
                SceneManager.MoveGameObjectToScene(keyObject, previewScene);
                Light key = keyObject.AddComponent<Light>();
                key.type = LightType.Directional;
                key.color = new Color(1f, 0.84f, 0.70f, 1f);
                key.intensity = 1.35f;
                key.shadows = LightShadows.Soft;
                keyObject.transform.rotation = Quaternion.Euler(42f, -32f, 0f);

                GameObject fillObject = new("Fill Light");
                SceneManager.MoveGameObjectToScene(fillObject, previewScene);
                Light fill = fillObject.AddComponent<Light>();
                fill.type = LightType.Point;
                fill.color = new Color(0.42f, 0.66f, 1f, 1f);
                fill.intensity = 2.1f;
                fill.range = 7f;
                fillObject.transform.position = new Vector3(-2.2f, 1.8f, -1.8f);

                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.24f, 0.26f, 0.29f, 1f);
                RenderSettings.skybox = null;

                if (!EditorSceneManager.SaveScene(previewScene, PreviewScenePath))
                    throw new InvalidOperationException($"保存预览场景失败：{PreviewScenePath}");
            }
            finally
            {
                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                    SceneManager.SetActiveScene(previousActiveScene);
                EditorSceneManager.CloseScene(previewScene, true);
            }
        }

        private static Material CreateOrUpdatePreviewGroundMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                throw new InvalidOperationException("当前项目找不到 Universal Render Pipeline/Lit Shader。");

            Material material =
                AssetDatabase.LoadAssetAtPath<Material>(PreviewGroundMaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "M_ImportPreviewGround" };
                AssetDatabase.CreateAsset(material, PreviewGroundMaterialPath);
            }
            else
            {
                material.shader = shader;
            }

            material.SetColor("_BaseColor", new Color(0.12f, 0.095f, 0.07f, 1f));
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.18f);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }

        private static bool AuditSourceHash(BlenderManifest manifest, ICollection<string> issues)
        {
            BlenderFile file = manifest.files.FirstOrDefault(candidate =>
                string.Equals(candidate.name, AssetId + ".fbx", StringComparison.Ordinal));
            if (file == null)
            {
                issues.Add("manifest 没有声明 FBX 文件。");
                return false;
            }

            string modelAbsolutePath = ToAbsoluteProjectPath(ModelPath);
            if (!File.Exists(modelAbsolutePath))
            {
                issues.Add($"缺少 FBX 文件：{ModelPath}");
                return false;
            }

            string actualModelHash = ComputeSha256(modelAbsolutePath);
            bool modelMatches = string.Equals(
                actualModelHash,
                file.sha256,
                StringComparison.OrdinalIgnoreCase);
            if (!modelMatches)
                issues.Add($"FBX SHA-256 与 manifest 不一致：{actualModelHash}");

            string scriptAbsolutePath = ToAbsoluteProjectPath(BlenderScriptPath);
            if (!File.Exists(scriptAbsolutePath))
            {
                issues.Add($"缺少 Blender 生成脚本：{BlenderScriptPath}");
                return false;
            }

            string actualScriptHash = ComputeSha256(scriptAbsolutePath);
            bool scriptMatches = string.Equals(
                actualScriptHash,
                manifest.toolchain.sourceScriptSha256,
                StringComparison.OrdinalIgnoreCase);
            if (!scriptMatches)
                issues.Add($"Blender 源脚本 SHA-256 与 manifest 不一致：{actualScriptHash}");

            return modelMatches && scriptMatches;
        }

        private static bool AuditImporter(
            BlenderManifest manifest,
            ICollection<string> issues)
        {
            if (AssetImporter.GetAtPath(ModelPath) is not ModelImporter importer)
            {
                issues.Add($"缺少 ModelImporter：{ModelPath}");
                return false;
            }

            bool policyMatches =
                importer.animationType == ModelImporterAnimationType.None &&
                !importer.importAnimation &&
                !importer.importCameras &&
                !importer.importLights &&
                !importer.importConstraints &&
                !importer.isReadable &&
                Mathf.Approximately(importer.globalScale, 1f) &&
                importer.bakeAxisConversion &&
                importer.meshCompression == ModelImporterMeshCompression.Off &&
                !importer.addCollider &&
                importer.weldVertices &&
                !importer.generateSecondaryUV &&
                importer.importNormals == ModelImporterNormals.Import &&
                importer.importTangents == ModelImporterTangents.CalculateMikk &&
                importer.materialImportMode == ModelImporterMaterialImportMode.ImportStandard &&
                importer.materialLocation == ModelImporterMaterialLocation.InPrefab;

            IReadOnlyDictionary<AssetImporter.SourceAssetIdentifier, UnityEngine.Object> externalMap =
                importer.GetExternalObjectMap();
            KeyValuePair<AssetImporter.SourceAssetIdentifier, UnityEngine.Object>[] materialRemaps =
                externalMap
                    .Where(pair => pair.Key.type == typeof(Material))
                    .ToArray();
            bool remapsMatch = materialRemaps.Length == manifest.materials.Length;
            for (int i = 0; i < manifest.materials.Length; i++)
            {
                BlenderMaterial spec = manifest.materials[i];
                Material expected = AssetDatabase.LoadAssetAtPath<Material>(GetMaterialPath(spec.name));
                remapsMatch &= materialRemaps.Any(pair =>
                    string.Equals(pair.Key.name, spec.name, StringComparison.Ordinal) &&
                    pair.Value == expected);
            }

            if (!policyMatches) issues.Add("ModelImporter 设置不符合静态道具 Spike 契约。");
            if (!remapsMatch)
                issues.Add("ModelImporter 的源材质名没有精确映射到同名外部材质，或存在多余 Remap。");
            return policyMatches && remapsMatch;
        }

        private static ModelAudit AuditModel(
            BlenderManifest manifest,
            ICollection<string> issues)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                issues.Add($"无法加载模型：{ModelPath}");
                return ModelAudit.Empty(manifest);
            }

            Scene previewScene = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject instance =
                    PrefabUtility.InstantiatePrefab(model, previewScene) as GameObject;
                if (instance == null)
                {
                    issues.Add("无法实例化导入模型。");
                    return ModelAudit.Empty(manifest);
                }

                MeshFilter[] meshFilters = instance.GetComponentsInChildren<MeshFilter>(true)
                    .Where(filter => filter.sharedMesh != null)
                    .ToArray();
                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                int vertexCount = meshFilters.Sum(filter => filter.sharedMesh.vertexCount);
                int triangleCount = meshFilters.Sum(filter =>
                {
                    Mesh mesh = filter.sharedMesh;
                    long count = 0;
                    for (int i = 0; i < mesh.subMeshCount; i++) count += (long)mesh.GetIndexCount(i) / 3;
                    return checked((int)count);
                });
                Bounds bounds = CalculateLocalRendererBounds(instance);
                Vector3 expectedBoundsSize = ToExpectedUnitySize(manifest.geometry.boundsMeters.size);

                bool geometryMatches =
                    meshFilters.Length == manifest.geometry.meshObjectCount &&
                    vertexCount >= manifest.geometry.vertexCount &&
                    vertexCount <= manifest.geometry.vertexCount * MaxRuntimeVertexExpansion &&
                    triangleCount == manifest.geometry.triangleCount &&
                    Approximately(bounds.size, expectedBoundsSize, BoundsToleranceMeters);
                if (!geometryMatches)
                    issues.Add(
                        $"几何不符合 manifest：Mesh={meshFilters.Length}/{manifest.geometry.meshObjectCount}，" +
                        $"Runtime/Source Vertex={vertexCount}/{manifest.geometry.vertexCount}，" +
                        $"Triangle={triangleCount}/{manifest.geometry.triangleCount}，" +
                        $"Bounds={bounds.size}/{expectedBoundsSize}");

                var meshObjectNames = new HashSet<string>(
                    meshFilters.Select(filter => filter.transform.name),
                    StringComparer.Ordinal);
                var expectedObjectNames = new HashSet<string>(
                    manifest.geometry.objects,
                    StringComparer.Ordinal);
                string[] missingObjects = manifest.geometry.objects
                    .Where(name => !meshObjectNames.Contains(name))
                    .ToArray();
                string[] unexpectedObjects = meshObjectNames
                    .Where(name => !expectedObjectNames.Contains(name))
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();
                bool hierarchyMatches =
                    missingObjects.Length == 0 &&
                    unexpectedObjects.Length == 0 &&
                    meshObjectNames.SetEquals(expectedObjectNames) &&
                    instance.GetComponentsInChildren<Camera>(true).Length == 0 &&
                    instance.GetComponentsInChildren<Light>(true).Length == 0 &&
                    IsIdentity(instance.transform);
                if (!hierarchyMatches)
                    issues.Add(
                        "FBX 层级、根 TRS 或 Camera/Light 过滤不符合契约。" +
                        (missingObjects.Length == 0
                            ? string.Empty
                            : " 缺少对象：" + string.Join(", ", missingObjects)) +
                        (unexpectedObjects.Length == 0
                            ? string.Empty
                            : " 多余对象：" + string.Join(", ", unexpectedObjects)));

                string[] materialAssetPaths = renderers
                    .SelectMany(renderer => renderer.sharedMaterials)
                    .Where(material => material != null)
                    .Select(AssetDatabase.GetAssetPath)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .ToArray();

                return new ModelAudit(
                    hierarchyMatches,
                    geometryMatches,
                    meshFilters.Length,
                    manifest.geometry.vertexCount,
                    vertexCount,
                    triangleCount,
                    expectedBoundsSize,
                    bounds.size,
                    materialAssetPaths);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        private static bool AuditMaterials(
            BlenderManifest manifest,
            IReadOnlyCollection<string> actualMaterialPaths,
            ICollection<string> issues)
        {
            string[] expectedPaths = manifest.materials
                .Select(spec => GetMaterialPath(spec.name))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            bool matches = new HashSet<string>(expectedPaths, StringComparer.Ordinal)
                .SetEquals(actualMaterialPaths);

            for (int i = 0; i < manifest.materials.Length; i++)
            {
                BlenderMaterial spec = manifest.materials[i];
                Material material =
                    AssetDatabase.LoadAssetAtPath<Material>(GetMaterialPath(spec.name));
                Color expectedColor = ToColor(spec.baseColorLinear, spec.name);
                if (material == null ||
                    material.shader == null ||
                    material.shader.name != "Universal Render Pipeline/Lit" ||
                    Vector4.Distance(material.GetColor("_BaseColor"), expectedColor) > 0.001f ||
                    !Mathf.Approximately(material.GetFloat("_Metallic"), spec.metallic) ||
                    !Mathf.Approximately(
                        material.GetFloat("_Smoothness"),
                        1f - spec.roughness))
                {
                    matches = false;
                }
            }

            if (!matches)
                issues.Add(
                    "Renderer 没有只引用 manifest 对应的三个外部 URP/Lit 材质，或 PBR 参数不一致。");
            return matches;
        }

        private static bool AuditPrefab(Vector3 modelBoundsSize, ICollection<string> issues)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                issues.Add($"缺少 Prefab：{PrefabPath}");
                return false;
            }

            Transform visual = prefab.transform.Find("Visual");
            bool staticContractMatches =
                IsIdentity(prefab.transform) &&
                visual != null &&
                IsIdentity(visual) &&
                prefab.GetComponentsInChildren<BoxCollider>(true).Length == 1 &&
                prefab.GetComponentsInChildren<MeshCollider>(true).Length == 0;

            Scene previewScene = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject instance =
                    PrefabUtility.InstantiatePrefab(prefab, previewScene) as GameObject;
                if (instance == null)
                {
                    issues.Add("无法实例化生成的 Prefab。");
                    return false;
                }

                Bounds bounds = CalculateLocalRendererBounds(instance);
                BoxCollider collider = instance.GetComponent<BoxCollider>();
                bool boundsMatch =
                    collider != null &&
                    Approximately(collider.center, bounds.center, BoundsToleranceMeters) &&
                    Approximately(collider.size, bounds.size, BoundsToleranceMeters) &&
                    Approximately(bounds.size, modelBoundsSize, BoundsToleranceMeters);
                bool matches = staticContractMatches && boundsMatch;
                if (!matches)
                    issues.Add("Prefab 根/Visual TRS、单一 BoxCollider 或 Collider 覆盖范围不符合契约。");
                return matches;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        private static bool AuditPreviewScene(ICollection<string> issues)
        {
            SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(PreviewScenePath);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Material groundMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(PreviewGroundMaterialPath);
            if (sceneAsset == null || prefab == null || groundMaterial == null)
            {
                issues.Add($"预览场景或其固定依赖缺失：{PreviewScenePath}");
                return false;
            }

            Scene previousActiveScene = SceneManager.GetActiveScene();
            Scene previewScene = SceneManager.GetSceneByPath(PreviewScenePath);
            bool openedByAudit = !previewScene.IsValid() || !previewScene.isLoaded;
            if (openedByAudit)
                previewScene = EditorSceneManager.OpenScene(PreviewScenePath, OpenSceneMode.Additive);

            try
            {
                GameObject[] roots = previewScene.GetRootGameObjects();
                GameObject[] propRoots = roots.Where(item => item.name == AssetId).ToArray();
                GameObject[] groundRoots = roots.Where(item => item.name == "PreviewGround").ToArray();
                Camera[] cameras = roots
                    .SelectMany(item => item.GetComponentsInChildren<Camera>(true))
                    .ToArray();
                Light[] lights = roots
                    .SelectMany(item => item.GetComponentsInChildren<Light>(true))
                    .ToArray();

                GameObject propRoot = propRoots.Length == 1 ? propRoots[0] : null;
                GameObject source = propRoot == null
                    ? null
                    : PrefabUtility.GetCorrespondingObjectFromSource(propRoot);
                MeshRenderer groundRenderer = groundRoots.Length == 1
                    ? groundRoots[0].GetComponent<MeshRenderer>()
                    : null;
                bool matches =
                    propRoots.Length == 1 &&
                    source == prefab &&
                    IsIdentity(propRoot.transform) &&
                    groundRoots.Length == 1 &&
                    groundRenderer != null &&
                    groundRenderer.sharedMaterial == groundMaterial &&
                    cameras.Length == 1 &&
                    cameras[0].CompareTag("MainCamera") &&
                    lights.Length == 2;
                if (!matches)
                    issues.Add(
                        "预览场景没有精确引用生成 Prefab、Ground 材质、单一 MainCamera 和两盏固定灯。");
                return matches;
            }
            finally
            {
                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                    SceneManager.SetActiveScene(previousActiveScene);
                if (openedByAudit) EditorSceneManager.CloseScene(previewScene, true);
            }
        }

        private static Bounds CalculateLocalRendererBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) throw new InvalidOperationException("模型没有可计算 Bounds 的 Renderer。");

            bool initialized = false;
            Vector3 minimum = default;
            Vector3 maximum = default;
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Bounds worldBounds = renderers[rendererIndex].bounds;
                Vector3 center = worldBounds.center;
                Vector3 extents = worldBounds.extents;
                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 worldPoint =
                        center + Vector3.Scale(extents, new Vector3(x, y, z));
                    Vector3 localPoint = root.transform.InverseTransformPoint(worldPoint);
                    if (!initialized)
                    {
                        minimum = localPoint;
                        maximum = localPoint;
                        initialized = true;
                    }
                    else
                    {
                        minimum = Vector3.Min(minimum, localPoint);
                        maximum = Vector3.Max(maximum, localPoint);
                    }
                }
            }

            return new Bounds((minimum + maximum) * 0.5f, maximum - minimum);
        }

        private static BlenderManifest LoadManifest()
        {
            TextAsset textAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            if (textAsset == null) throw new InvalidOperationException($"缺少 Blender manifest：{ManifestPath}");
            BlenderManifest manifest = JsonUtility.FromJson<BlenderManifest>(textAsset.text);
            if (manifest == null) throw new InvalidOperationException("无法解析 Blender manifest。");
            return manifest;
        }

        private static void ValidateManifest(BlenderManifest manifest)
        {
            if (manifest.schemaVersion != SupportedManifestSchemaVersion)
                throw new InvalidOperationException(
                    $"Blender manifest schemaVersion 不是 {SupportedManifestSchemaVersion}。");
            if (manifest.harnessVersion != SupportedHarnessVersion)
                throw new InvalidOperationException(
                    $"Blender manifest harnessVersion 不是 {SupportedHarnessVersion}。");
            if (manifest.status != "passed")
                throw new InvalidOperationException($"Blender manifest 状态不是 passed：{manifest.status}");
            if (manifest.asset == null || manifest.asset.id != AssetId)
                throw new InvalidOperationException($"Blender manifest 资产 ID 不是 {AssetId}。");
            if (manifest.toolchain == null ||
                string.IsNullOrWhiteSpace(manifest.toolchain.sourceScriptSha256))
                throw new InvalidOperationException("Blender manifest 缺少源脚本 SHA-256。");
            if (manifest.coordinateContract == null ||
                manifest.coordinateContract.authoringUnits != "meters" ||
                manifest.coordinateContract.blenderUpAxis != "+Z" ||
                manifest.coordinateContract.fbxForwardAxis != "-Z" ||
                manifest.coordinateContract.fbxUpAxis != "+Y" ||
                !manifest.coordinateContract.fbxSpaceTransformBaked ||
                manifest.coordinateContract.rootTransform != "identity")
                throw new InvalidOperationException("Blender manifest 坐标或静态 FBX 轴烘焙契约不符合预期。");
            if (manifest.materials == null || manifest.materials.Length != 3)
                throw new InvalidOperationException("Blender manifest 必须声明三个资产材质。");
            if (manifest.geometry?.boundsMeters?.size == null ||
                manifest.geometry.boundsMeters.size.Length != 3)
                throw new InvalidOperationException("Blender manifest 缺少三维 Bounds。");
            if (manifest.geometry.objects == null || manifest.geometry.objects.Length == 0)
                throw new InvalidOperationException("Blender manifest 缺少对象清单。");
            if (manifest.geometry.objects.Distinct(StringComparer.Ordinal).Count() !=
                manifest.geometry.objects.Length)
                throw new InvalidOperationException("Blender manifest 对象清单包含重复名称。");
            if (manifest.files == null || manifest.files.Length == 0)
                throw new InvalidOperationException("Blender manifest 缺少文件证据。");
        }

        private static string GetMaterialPath(string materialName)
        {
            if (string.IsNullOrWhiteSpace(materialName) ||
                materialName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                materialName.Contains('/'))
                throw new InvalidOperationException($"非法材质名：{materialName}");
            return MaterialFolder + "/" + materialName + ".mat";
        }

        private static Color ToColor(float[] values, string materialName)
        {
            if (values == null || values.Length != 4)
                throw new InvalidOperationException($"材质 {materialName} 的 Base Color 不是 RGBA 四元组。");
            return new Color(values[0], values[1], values[2], values[3]);
        }

        private static Vector3 ToExpectedUnitySize(float[] blenderSize)
        {
            return new Vector3(blenderSize[0], blenderSize[2], blenderSize[1]);
        }

        private static void ResetLocalTransform(Transform transform)
        {
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
        }

        private static bool IsIdentity(Transform transform)
        {
            return Approximately(transform.localPosition, Vector3.zero, 0.0001f) &&
                   Quaternion.Angle(transform.localRotation, Quaternion.identity) <= 0.01f &&
                   Approximately(transform.localScale, Vector3.one, 0.0001f);
        }

        private static bool Approximately(Vector3 left, Vector3 right, float tolerance)
        {
            return Mathf.Abs(left.x - right.x) <= tolerance &&
                   Mathf.Abs(left.y - right.y) <= tolerance &&
                   Mathf.Abs(left.z - right.z) <= tolerance;
        }

        private static string GetRenderingVerdict()
        {
            RenderPipelineAsset pipeline = QualitySettings.renderPipeline;
            if (pipeline == null) return "inconclusive：当前 Quality 没有设置 SRP Asset。";

            var serialized = new SerializedObject(pipeline);
            SerializedProperty renderers = serialized.FindProperty("m_RendererDataList");
            SerializedProperty defaultIndex = serialized.FindProperty("m_DefaultRendererIndex");
            if (renderers == null || defaultIndex == null ||
                defaultIndex.intValue < 0 || defaultIndex.intValue >= renderers.arraySize)
                return $"inconclusive：无法读取 {pipeline.name} 的默认 Renderer。";

            UnityEngine.Object renderer =
                renderers.GetArrayElementAtIndex(defaultIndex.intValue).objectReferenceValue;
            string rendererName = renderer == null ? "（空）" : renderer.GetType().Name;
            return rendererName.Contains("Renderer2D", StringComparison.Ordinal)
                ? $"inconclusive：当前默认 Renderer 是 {rendererName}；本轮不能证明正式 3D 光照、阴影与法线响应。"
                : $"manual_review_required：当前默认 Renderer 是 {rendererName}；仍需在代表性游戏镜头人工验收。";
        }

        private static void WriteReport(NomadPropAssetAudit audit)
        {
            string outputFolder = Path.Combine(
                Directory.GetParent(Application.dataPath)?.FullName ??
                throw new InvalidOperationException("无法定位项目根目录。"),
                "ArtPipelineOutput",
                "UnityImportSpike",
                AssetId);
            Directory.CreateDirectory(outputFolder);
            var report = new UnityImportReport
            {
                schemaVersion = 1,
                status = audit.Passed ? "passed" : "failed",
                assetId = AssetId,
                unityVersion = Application.unityVersion,
                generatedAtUtc = DateTime.UtcNow.ToString("O"),
                modelPath = ModelPath,
                manifestPath = ManifestPath,
                prefabPath = PrefabPath,
                previewScenePath = PreviewScenePath,
                meshObjectCount = audit.MeshObjectCount,
                sourceVertexCount = audit.SourceVertexCount,
                vertexCount = audit.VertexCount,
                triangleCount = audit.TriangleCount,
                expectedBoundsSize = audit.ExpectedBoundsSize,
                actualBoundsSize = audit.ActualBoundsSize,
                materialAssetPaths = audit.MaterialAssetPaths.ToArray(),
                renderingVerdict = audit.RenderingVerdict,
                issues = audit.Issues.ToArray(),
                manualReviewStillRequired = true,
            };
            File.WriteAllText(
                Path.Combine(outputFolder, "report.json"),
                JsonUtility.ToJson(report, true) + Environment.NewLine);
        }

        private static string ComputeSha256(string path)
        {
            using SHA256 algorithm = SHA256.Create();
            using FileStream stream = File.OpenRead(path);
            return BitConverter.ToString(algorithm.ComputeHash(stream))
                .Replace("-", string.Empty)
                .ToLowerInvariant();
        }

        private static string ToAbsoluteProjectPath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ??
                                 throw new InvalidOperationException("无法定位项目根目录。");
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }

        private static void EnsureFolder(string folderPath)
        {
            string[] segments = folderPath.Split('/');
            string current = segments[0];
            for (int i = 1; i < segments.Length; i++)
            {
                string next = current + "/" + segments[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, segments[i]);
                current = next;
            }
        }

        [Serializable]
        private sealed class BlenderManifest
        {
            public int schemaVersion;
            public string harnessVersion = string.Empty;
            public string status = string.Empty;
            public BlenderAsset asset = new();
            public BlenderToolchain toolchain = new();
            public BlenderCoordinateContract coordinateContract = new();
            public BlenderMaterial[] materials = Array.Empty<BlenderMaterial>();
            public BlenderGeometry geometry = new();
            public BlenderFile[] files = Array.Empty<BlenderFile>();
        }

        [Serializable]
        private sealed class BlenderAsset
        {
            public string id = string.Empty;
        }

        [Serializable]
        private sealed class BlenderToolchain
        {
            public string blenderVersion = string.Empty;
            public string sourceScriptSha256 = string.Empty;
        }

        [Serializable]
        private sealed class BlenderCoordinateContract
        {
            public string authoringUnits = string.Empty;
            public string blenderUpAxis = string.Empty;
            public string fbxForwardAxis = string.Empty;
            public string fbxUpAxis = string.Empty;
            public bool fbxSpaceTransformBaked;
            public string rootTransform = string.Empty;
        }

        [Serializable]
        private sealed class BlenderMaterial
        {
            public string name = string.Empty;
            public float[] baseColorLinear = Array.Empty<float>();
            public float metallic;
            public float roughness;
        }

        [Serializable]
        private sealed class BlenderGeometry
        {
            public int meshObjectCount;
            public int vertexCount;
            public int triangleCount;
            public BlenderBounds boundsMeters = new();
            public string[] objects = Array.Empty<string>();
        }

        [Serializable]
        private sealed class BlenderBounds
        {
            public float[] size = Array.Empty<float>();
        }

        [Serializable]
        private sealed class BlenderFile
        {
            public string name = string.Empty;
            public string sha256 = string.Empty;
        }

        [Serializable]
        private sealed class UnityImportReport
        {
            public int schemaVersion;
            public string status = string.Empty;
            public string assetId = string.Empty;
            public string unityVersion = string.Empty;
            public string generatedAtUtc = string.Empty;
            public string modelPath = string.Empty;
            public string manifestPath = string.Empty;
            public string prefabPath = string.Empty;
            public string previewScenePath = string.Empty;
            public int meshObjectCount;
            public int sourceVertexCount;
            public int vertexCount;
            public int triangleCount;
            public Vector3 expectedBoundsSize;
            public Vector3 actualBoundsSize;
            public string[] materialAssetPaths = Array.Empty<string>();
            public string renderingVerdict = string.Empty;
            public string[] issues = Array.Empty<string>();
            public bool manualReviewStillRequired;
        }

        private sealed class ModelAudit
        {
            public ModelAudit(
                bool hierarchyMatches,
                bool geometryMatches,
                int meshObjectCount,
                int sourceVertexCount,
                int vertexCount,
                int triangleCount,
                Vector3 expectedBoundsSize,
                Vector3 boundsSize,
                IReadOnlyList<string> materialAssetPaths)
            {
                HierarchyMatches = hierarchyMatches;
                GeometryMatches = geometryMatches;
                MeshObjectCount = meshObjectCount;
                SourceVertexCount = sourceVertexCount;
                VertexCount = vertexCount;
                TriangleCount = triangleCount;
                ExpectedBoundsSize = expectedBoundsSize;
                BoundsSize = boundsSize;
                MaterialAssetPaths = materialAssetPaths;
            }

            public bool HierarchyMatches { get; }
            public bool GeometryMatches { get; }
            public int MeshObjectCount { get; }
            public int SourceVertexCount { get; }
            public int VertexCount { get; }
            public int TriangleCount { get; }
            public Vector3 ExpectedBoundsSize { get; }
            public Vector3 BoundsSize { get; }
            public IReadOnlyList<string> MaterialAssetPaths { get; }

            public static ModelAudit Empty(BlenderManifest manifest)
            {
                return new ModelAudit(
                    false,
                    false,
                    0,
                    manifest.geometry.vertexCount,
                    0,
                    0,
                    ToExpectedUnitySize(manifest.geometry.boundsMeters.size),
                    Vector3.zero,
                    Array.Empty<string>());
            }
        }
    }

    /// <summary>供菜单、EditMode 测试和后续批处理共同消费的只读静态道具导入证据。</summary>
    public sealed class NomadPropAssetAudit
    {
        public NomadPropAssetAudit(
            bool sourceHashMatches,
            bool importerPolicyMatches,
            bool sourceHierarchyMatches,
            bool geometryMatches,
            bool materialContractMatches,
            bool prefabContractMatches,
            bool previewSceneExists,
            int meshObjectCount,
            int sourceVertexCount,
            int vertexCount,
            int triangleCount,
            Vector3 expectedBoundsSize,
            Vector3 actualBoundsSize,
            IReadOnlyList<string> materialAssetPaths,
            string renderingVerdict,
            IReadOnlyList<string> issues)
        {
            SourceHashMatches = sourceHashMatches;
            ImporterPolicyMatches = importerPolicyMatches;
            SourceHierarchyMatches = sourceHierarchyMatches;
            GeometryMatches = geometryMatches;
            MaterialContractMatches = materialContractMatches;
            PrefabContractMatches = prefabContractMatches;
            PreviewSceneExists = previewSceneExists;
            MeshObjectCount = meshObjectCount;
            SourceVertexCount = sourceVertexCount;
            VertexCount = vertexCount;
            TriangleCount = triangleCount;
            ExpectedBoundsSize = expectedBoundsSize;
            ActualBoundsSize = actualBoundsSize;
            MaterialAssetPaths = materialAssetPaths;
            RenderingVerdict = renderingVerdict;
            Issues = issues;
        }

        public bool SourceHashMatches { get; }
        public bool ImporterPolicyMatches { get; }
        public bool SourceHierarchyMatches { get; }
        public bool GeometryMatches { get; }
        public bool MaterialContractMatches { get; }
        public bool PrefabContractMatches { get; }
        public bool PreviewSceneExists { get; }
        public int MeshObjectCount { get; }
        public int SourceVertexCount { get; }
        public int VertexCount { get; }
        public int TriangleCount { get; }
        public Vector3 ExpectedBoundsSize { get; }
        public Vector3 ActualBoundsSize { get; }
        public IReadOnlyList<string> MaterialAssetPaths { get; }
        public string RenderingVerdict { get; }
        public IReadOnlyList<string> Issues { get; }

        public bool Passed =>
            SourceHashMatches &&
            ImporterPolicyMatches &&
            SourceHierarchyMatches &&
            GeometryMatches &&
            MaterialContractMatches &&
            PrefabContractMatches &&
            PreviewSceneExists &&
            Issues.Count == 0;

        public string ToMultilineString()
        {
            string issues = Issues.Count == 0 ? "（无）" : string.Join("\n  - ", Issues);
            return $"[Nomad Blender Import Audit] {(Passed ? "PASS" : "FAIL")}\n" +
                   $"模型：{MeshObjectCount} Mesh / {VertexCount} Runtime Vertex " +
                   $"({SourceVertexCount} Source) / {TriangleCount} Triangle\n" +
                   $"Bounds：实际 {ActualBoundsSize}，预期 {ExpectedBoundsSize}\n" +
                   $"材质：{string.Join(", ", MaterialAssetPaths)}\n" +
                   $"渲染结论：{RenderingVerdict}\n" +
                   $"问题：\n  - {issues}";
        }
    }
}
