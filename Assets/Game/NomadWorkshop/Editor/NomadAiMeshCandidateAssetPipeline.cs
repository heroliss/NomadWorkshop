using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Game.NomadWorkshop.Editor
{
    /// <summary>
    /// 把经过 Blender Intake 审计的外部 AI Mesh 候选翻译为隔离的 Unity URP 资产。
    /// 该入口验证来源、导入语义和显示契约，但不会把候选自动升级为生产美术。
    /// </summary>
    public static class NomadAiMeshCandidateAssetPipeline
    {
        public const string AssetId = "NW_FieldKitchen_01_Rodin";
        public const string SourceAssetId = "NW_FieldKitchen_01";
        public const string AssetRoot =
            "Assets/Game/NomadWorkshop/Spikes/BlenderImport/NW_FieldKitchen_01_Rodin";
        public const string ModelPath = AssetRoot + "/" + AssetId + ".fbx";
        public const string IntakeReportPath = AssetRoot + "/" + AssetId + ".intake-report.json";
        public const string TextureFolder = AssetRoot + "/Textures";
        public const string BaseColorPath = TextureFolder + "/T_" + AssetId + "_BaseColor.png";
        public const string NormalPath = TextureFolder + "/T_" + AssetId + "_Normal.png";
        public const string MetallicSmoothnessPath =
            TextureFolder + "/T_" + AssetId + "_MetallicSmoothness.png";
        public const string MaterialFolder = AssetRoot + "/Materials";
        public const string MaterialPath = MaterialFolder + "/M_" + AssetId + ".mat";
        public const string PrefabFolder = AssetRoot + "/Prefabs";
        public const string PrefabPath = PrefabFolder + "/" + AssetId + ".prefab";
        public const string PreviewFolder = AssetRoot + "/Preview";
        public const string PreviewScenePath = PreviewFolder + "/" + AssetId + "_UrpPreview.unity";

        private const string IntakeScriptPath =
            "Tools/ArtPipeline/Blender/blender_ai_mesh_intake.py";
        private const int SupportedSchemaVersion = 1;
        private const string SupportedHarnessVersion = "0.1.0";
        private const string SourceMaterialName = "model";
        private const float BoundsToleranceMeters = 0.02f;
        private const float MaxRuntimeVertexExpansion = 4f;

        /// <summary>
        /// 幂等配置贴图、模型、外部材质、Prefab 和代表性预览场景，并输出结构化审计报告。
        /// </summary>
        [MenuItem("Assets/SSFramework/游牧工坊/AI Mesh Spike/配置并审计 Rodin 野战厨房")]
        public static void ConfigureAndReport()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出 Play Mode，再配置 AI Mesh 候选。");

            IntakeReport report = LoadAndValidateReport();
            EnsureFolder(MaterialFolder);
            EnsureFolder(PrefabFolder);
            EnsureFolder(PreviewFolder);

            ConfigureTextureImporter(BaseColorPath, TextureRole.BaseColor, report);
            ConfigureTextureImporter(NormalPath, TextureRole.Normal, report);
            ConfigureTextureImporter(
                MetallicSmoothnessPath, TextureRole.MetallicSmoothness, report);
            Material material = CreateOrUpdateMaterial();
            ApplyModelImportPolicy(material);
            CreateOrUpdatePrefab();
            EnsurePreviewScene();
            AssetDatabase.SaveAssets();

            NomadAiMeshCandidateAudit audit = Audit();
            WriteReport(audit, report);
            if (!audit.Passed) throw new InvalidOperationException(audit.ToMultilineString());
            Debug.Log(audit.ToMultilineString());
        }

        /// <summary>
        /// 只读复核从 Blender Intake 字节证据到 Unity URP 预览场景的完整候选链。
        /// </summary>
        public static NomadAiMeshCandidateAudit Audit()
        {
            IntakeReport report = LoadAndValidateReport();
            var issues = new List<string>();

            bool sourceHashesMatch = AuditSourceHashes(report, issues);
            bool textureImportersMatch = AuditTextureImporters(report, issues);
            bool modelImporterMatches = AuditModelImporter(issues);
            ModelAudit model = AuditModel(report, issues);
            bool materialMatches = AuditMaterial(model.MaterialPaths, issues);
            bool prefabMatches = AuditPrefab(model.BoundsCenter, model.BoundsSize, issues);

            int rendererIndex = -1;
            try
            {
                rendererIndex = NomadRenderingSpikePipeline.GetSecondaryRendererIndexOrThrow();
            }
            catch (Exception exception)
            {
                issues.Add($"次级 3D Renderer 不可用：{exception.Message}");
            }

            bool previewMatches = rendererIndex >= 0 &&
                                  AuditPreviewScene(rendererIndex, issues);
            GeometryObject sourceMesh = report.geometry.objects.Single();
            return new NomadAiMeshCandidateAudit(
                sourceHashesMatch,
                textureImportersMatch,
                modelImporterMatches,
                model.HierarchyMatches,
                model.GeometryMatches,
                materialMatches,
                prefabMatches,
                previewMatches,
                model.MeshObjectCount,
                model.MaterialSlotCount,
                sourceMesh.vertexCount,
                model.VertexCount,
                model.TriangleCount,
                model.ExpectedBoundsSize,
                model.BoundsSize,
                sourceMesh.duplicateFaceCount,
                sourceMesh.connectedComponentCount,
                GetTextureResolution(report),
                report.asset.manualArtReviewRequired,
                report.acceptance.productionApproved,
                "manual_review_required：字节、几何、PBR 通道与 URP 显示契约已自动验证；" +
                "轮廓、材质可信度、风格统一、碰撞和可拆分结构仍需人工看图与玩法审查。",
                report.warnings ?? Array.Empty<string>(),
                issues);
        }

        private static void ConfigureTextureImporter(
            string path,
            TextureRole role,
            IntakeReport report)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
                throw new InvalidOperationException($"没有找到可配置的 TextureImporter：{path}");

            bool isBaseColor = role == TextureRole.BaseColor;
            bool isNormal = role == TextureRole.Normal;
            bool keepsInputAlpha = role == TextureRole.MetallicSmoothness;
            importer.textureType = isNormal
                ? TextureImporterType.NormalMap
                : TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = isBaseColor;
            importer.alphaSource = keepsInputAlpha
                ? TextureImporterAlphaSource.FromInput
                : TextureImporterAlphaSource.None;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.mipmapFilter = TextureImporterMipFilter.BoxFilter;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = GetTextureResolution(report);
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.crunchedCompression = false;
            importer.streamingMipmaps = false;
            importer.flipGreenChannel = false;
            importer.isReadable = false;
            importer.SaveAndReimport();
        }

        private static Material CreateOrUpdateMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                throw new InvalidOperationException("当前项目找不到 Universal Render Pipeline/Lit Shader。");

            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "M_" + AssetId };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else
            {
                material.shader = shader;
            }

            Texture2D baseColor = LoadTexture(BaseColorPath);
            Texture2D normal = LoadTexture(NormalPath);
            Texture2D metallicSmoothness = LoadTexture(MetallicSmoothnessPath);
            material.SetFloat("_WorkflowMode", (float)LitGUI.WorkflowMode.Metallic);
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", baseColor);
            material.SetTextureScale("_BaseMap", Vector2.one);
            material.SetTextureOffset("_BaseMap", Vector2.zero);
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", 1f);
            material.SetTexture("_MetallicGlossMap", metallicSmoothness);
            material.SetFloat("_Metallic", 1f);
            material.SetFloat("_Smoothness", 1f);
            material.SetFloat(
                "_SmoothnessTextureChannel",
                (float)LitGUI.SmoothnessMapChannel.SpecularMetallicAlpha);
            material.SetTexture("_OcclusionMap", null);
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_AlphaClip", 0f);
            material.SetFloat("_Cull", (float)CullMode.Back);
            material.enableInstancing = true;
            material.doubleSidedGI = false;
            BaseShaderGUI.SetMaterialKeywords(material, LitGUI.SetMaterialKeywords);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }

        private static void ApplyModelImportPolicy(Material material)
        {
            if (AssetImporter.GetAtPath(ModelPath) is not ModelImporter importer)
                throw new InvalidOperationException($"没有找到可配置的 ModelImporter：{ModelPath}");

            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.importBlendShapes = false;
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

            AssetImporter.SourceAssetIdentifier[] staleRemaps = importer.GetExternalObjectMap()
                .Where(pair => pair.Key.type == typeof(Material))
                .Select(pair => pair.Key)
                .ToArray();
            foreach (AssetImporter.SourceAssetIdentifier identifier in staleRemaps)
                importer.RemoveRemap(identifier);
            importer.AddRemap(
                new AssetImporter.SourceAssetIdentifier(typeof(Material), SourceMaterialName),
                material);
            importer.SaveAndReimport();
        }

        private static void CreateOrUpdatePrefab()
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null) throw new InvalidOperationException($"无法加载模型：{ModelPath}");

            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject(AssetId);
                SceneManager.MoveGameObjectToScene(root, scene);
                GameObject visual = PrefabUtility.InstantiatePrefab(model, scene) as GameObject;
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
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static void EnsurePreviewScene()
        {
            int rendererIndex = NomadRenderingSpikePipeline.GetSecondaryRendererIndexOrThrow();
            var drift = new List<string>();
            if (AuditPreviewScene(rendererIndex, drift)) return;

            Scene loadedScene = SceneManager.GetSceneByPath(PreviewScenePath);
            if (loadedScene.IsValid() && loadedScene.isLoaded)
                throw new InvalidOperationException(
                    "AI Mesh 预览场景正在编辑器中打开；为避免覆盖当前 Scene 实例，本次停止。");
            CreateOrReplacePreviewScene(rendererIndex);
        }

        private static void CreateOrReplacePreviewScene(int rendererIndex)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Material groundMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                NomadRenderingSpikePipeline.GroundMaterialPath);
            Material skyboxMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                NomadRenderingSpikePipeline.SkyboxMaterialPath);
            if (prefab == null || groundMaterial == null || skyboxMaterial == null)
                throw new InvalidOperationException(
                    "缺少 AI Mesh Prefab 或 3D Renderer Spike 材质；请先完成对应配置。");

            Scene previousActiveScene = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                GameObject instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
                if (instance == null) throw new InvalidOperationException("无法实例化 AI Mesh Prefab。");
                instance.name = AssetId;
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ground.name = "PreviewGround";
                SceneManager.MoveGameObjectToScene(ground, scene);
                ground.transform.SetPositionAndRotation(
                    new Vector3(0f, -0.08f, 0.08f), Quaternion.identity);
                ground.transform.localScale = new Vector3(6.8f, 0.16f, 5.4f);
                ground.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;
                UnityEngine.Object.DestroyImmediate(ground.GetComponent<Collider>());

                CreatePreviewCamera(scene, rendererIndex);
                Light key = CreatePreviewLighting(scene);
                RenderSettings.ambientMode = AmbientMode.Skybox;
                RenderSettings.ambientIntensity = 1.02f;
                RenderSettings.reflectionIntensity = 1.0f;
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
                RenderSettings.skybox = skyboxMaterial;
                RenderSettings.sun = key;
                RenderSettings.fog = false;

                if (!EditorSceneManager.SaveScene(scene, PreviewScenePath))
                    throw new InvalidOperationException($"保存预览场景失败：{PreviewScenePath}");
            }
            finally
            {
                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                    SceneManager.SetActiveScene(previousActiveScene);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void CreatePreviewCamera(Scene scene, int rendererIndex)
        {
            var cameraObject = new GameObject("Main Camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.fieldOfView = 34f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 100f;
            camera.allowHDR = true;
            cameraObject.transform.position = new Vector3(2.75f, 1.92f, -3.15f);
            cameraObject.transform.LookAt(new Vector3(0f, 0.76f, 0f));

            UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.SetRenderer(rendererIndex);
            cameraData.renderPostProcessing = false;
            cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cameraData.antialiasingQuality = AntialiasingQuality.High;
            cameraData.stopNaN = true;
            cameraData.dithering = true;
            cameraData.allowXRRendering = false;
            cameraData.requiresDepthTexture = false;
            cameraData.requiresColorTexture = false;
        }

        private static Light CreatePreviewLighting(Scene scene)
        {
            var keyObject = new GameObject("Key Light");
            SceneManager.MoveGameObjectToScene(keyObject, scene);
            Light key = keyObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = new Color(1f, 0.84f, 0.70f, 1f);
            key.intensity = 1.55f;
            key.shadows = LightShadows.Soft;
            key.shadowStrength = 0.82f;
            keyObject.transform.rotation = Quaternion.Euler(44f, -32f, 0f);

            var fillObject = new GameObject("Fill Light");
            SceneManager.MoveGameObjectToScene(fillObject, scene);
            Light fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.color = new Color(0.42f, 0.62f, 1f, 1f);
            fill.intensity = 4.0f;
            fill.range = 7f;
            fill.shadows = LightShadows.None;
            fillObject.transform.position = new Vector3(-2.2f, 1.9f, -1.7f);

            var rimObject = new GameObject("Rim Light");
            SceneManager.MoveGameObjectToScene(rimObject, scene);
            Light rim = rimObject.AddComponent<Light>();
            rim.type = LightType.Spot;
            rim.color = new Color(1f, 0.46f, 0.22f, 1f);
            rim.intensity = 4.8f;
            rim.range = 7f;
            rim.spotAngle = 58f;
            rim.innerSpotAngle = 34f;
            rim.shadows = LightShadows.None;
            rimObject.transform.position = new Vector3(1.9f, 2.35f, 2.25f);
            rimObject.transform.LookAt(new Vector3(0f, 0.75f, 0f));
            return key;
        }

        private static bool AuditSourceHashes(IntakeReport report, ICollection<string> issues)
        {
            bool matches = true;
            matches &= AuditHash(
                ToAbsoluteProjectPath(IntakeScriptPath),
                report.toolchain.sourceScriptSha256,
                null,
                "Blender Intake 脚本",
                issues);

            IntakeFile modelFile = report.files.SingleOrDefault(file => file.path == report.exports.fbx);
            matches &= AuditHash(
                ToAbsoluteProjectPath(ModelPath),
                modelFile?.sha256,
                modelFile?.bytes,
                "Unity 内 FBX",
                issues);

            TextureEvidence baseColor = FindTexture(report, "Base Color");
            TextureEvidence normal = FindTexture(report, "Normal");
            matches &= AuditHash(
                ToAbsoluteProjectPath(BaseColorPath), baseColor.sha256, baseColor.bytes,
                "Base Color", issues);
            matches &= AuditHash(
                ToAbsoluteProjectPath(NormalPath), normal.sha256, normal.bytes,
                "Normal", issues);
            matches &= AuditHash(
                ToAbsoluteProjectPath(MetallicSmoothnessPath),
                report.derivedTextures.urpMetallicSmoothness.sha256,
                report.derivedTextures.urpMetallicSmoothness.bytes,
                "URP MetallicSmoothness", issues);
            return matches;
        }

        private static bool AuditHash(
            string path,
            string expectedHash,
            long? expectedBytes,
            string label,
            ICollection<string> issues)
        {
            bool matches = File.Exists(path) && expectedHash?.Length == 64 &&
                           (!expectedBytes.HasValue ||
                            new FileInfo(path).Length == expectedBytes.Value) &&
                           string.Equals(
                               ComputeSha256(path), expectedHash,
                               StringComparison.OrdinalIgnoreCase);
            if (!matches) issues.Add($"{label} 的字节或 SHA-256 与 Intake 报告不一致。");
            return matches;
        }

        private static bool AuditTextureImporters(
            IntakeReport report,
            ICollection<string> issues)
        {
            int resolution = GetTextureResolution(report);
            bool baseColor = AuditTextureImporter(
                BaseColorPath, TextureRole.BaseColor, resolution, issues);
            bool normal = AuditTextureImporter(
                NormalPath, TextureRole.Normal, resolution, issues);
            bool mask = AuditTextureImporter(
                MetallicSmoothnessPath, TextureRole.MetallicSmoothness, resolution, issues);
            return baseColor && normal && mask;
        }

        private static bool AuditTextureImporter(
            string path,
            TextureRole role,
            int resolution,
            ICollection<string> issues)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            bool isBaseColor = role == TextureRole.BaseColor;
            bool isNormal = role == TextureRole.Normal;
            bool keepsAlpha = role == TextureRole.MetallicSmoothness;
            bool matches = importer != null && texture != null &&
                           importer.textureType == (isNormal
                               ? TextureImporterType.NormalMap
                               : TextureImporterType.Default) &&
                           importer.sRGBTexture == isBaseColor &&
                           importer.alphaSource == (keepsAlpha
                               ? TextureImporterAlphaSource.FromInput
                               : TextureImporterAlphaSource.None) &&
                           !importer.alphaIsTransparency && importer.mipmapEnabled &&
                           importer.wrapMode == TextureWrapMode.Repeat &&
                           importer.filterMode == FilterMode.Trilinear &&
                           importer.anisoLevel == 4 && importer.maxTextureSize == resolution &&
                           importer.textureCompression == TextureImporterCompression.CompressedHQ &&
                           !importer.crunchedCompression && !importer.streamingMipmaps &&
                           !importer.flipGreenChannel && !importer.isReadable &&
                           texture.width == resolution && texture.height == resolution;
            if (!matches) issues.Add($"TextureImporter 或尺寸不符合 PBR 语义：{path}");
            return matches;
        }

        private static bool AuditModelImporter(ICollection<string> issues)
        {
            if (AssetImporter.GetAtPath(ModelPath) is not ModelImporter importer)
            {
                issues.Add($"缺少 ModelImporter：{ModelPath}");
                return false;
            }

            bool policyMatches = importer.animationType == ModelImporterAnimationType.None &&
                                 !importer.importAnimation && !importer.importBlendShapes &&
                                 !importer.importCameras && !importer.importLights &&
                                 !importer.importConstraints && !importer.isReadable &&
                                 Mathf.Approximately(importer.globalScale, 1f) &&
                                 importer.bakeAxisConversion &&
                                 importer.meshCompression == ModelImporterMeshCompression.Off &&
                                 !importer.addCollider && importer.weldVertices &&
                                 !importer.generateSecondaryUV &&
                                 importer.importNormals == ModelImporterNormals.Import &&
                                 importer.importTangents == ModelImporterTangents.CalculateMikk &&
                                 importer.materialImportMode == ModelImporterMaterialImportMode.ImportStandard &&
                                 importer.materialLocation == ModelImporterMaterialLocation.InPrefab;
            KeyValuePair<AssetImporter.SourceAssetIdentifier, UnityEngine.Object>[] remaps =
                importer.GetExternalObjectMap()
                    .Where(pair => pair.Key.type == typeof(Material))
                    .ToArray();
            Material expectedMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            bool remapMatches = remaps.Length == 1 &&
                                remaps[0].Key.name == SourceMaterialName &&
                                remaps[0].Value == expectedMaterial;
            if (!policyMatches) issues.Add("ModelImporter 不符合 AI 静态 Mesh 候选契约。");
            if (!remapMatches) issues.Add("FBX 源材质没有精确映射到外部 URP/Lit 材质。");
            return policyMatches && remapMatches;
        }

        private static ModelAudit AuditModel(IntakeReport report, ICollection<string> issues)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                issues.Add($"无法加载模型：{ModelPath}");
                return ModelAudit.Empty(ToExpectedUnitySize(report.geometry.worldBounds.size));
            }

            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject instance = PrefabUtility.InstantiatePrefab(model, scene) as GameObject;
                if (instance == null)
                {
                    issues.Add("无法实例化导入模型。");
                    return ModelAudit.Empty(ToExpectedUnitySize(report.geometry.worldBounds.size));
                }

                MeshFilter[] meshes = instance.GetComponentsInChildren<MeshFilter>(true)
                    .Where(filter => filter.sharedMesh != null)
                    .ToArray();
                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                int materialSlots = renderers.Sum(renderer => renderer.sharedMaterials.Length);
                int vertexCount = meshes.Sum(filter => filter.sharedMesh.vertexCount);
                int triangleCount = meshes.Sum(filter =>
                {
                    long count = 0;
                    for (int i = 0; i < filter.sharedMesh.subMeshCount; i++)
                        count += (long)filter.sharedMesh.GetIndexCount(i) / 3;
                    return checked((int)count);
                });
                Bounds bounds = CalculateLocalRendererBounds(instance);
                int sourceVertexCount = report.exports.roundTrips.fbx.vertexCount;
                int expectedTriangleCount = report.exports.roundTrips.fbx.triangleCount;
                Vector3 expectedBounds = ToExpectedUnitySize(report.geometry.worldBounds.size);
                bool geometryMatches = meshes.Length == report.exports.roundTrips.fbx.meshObjectCount &&
                                       materialSlots == 1 &&
                                       renderers.All(renderer => renderer.sharedMaterials.Length == 1) &&
                                       vertexCount >= sourceVertexCount &&
                                       vertexCount <= sourceVertexCount * MaxRuntimeVertexExpansion &&
                                       triangleCount == expectedTriangleCount &&
                                       Approximately(bounds.size, expectedBounds, BoundsToleranceMeters);
                if (!geometryMatches)
                    issues.Add(
                        $"Unity 几何不符合 Intake：Mesh={meshes.Length}/1，Slot={materialSlots}/1，" +
                        $"Runtime/FBX Vertex={vertexCount}/{sourceVertexCount}，" +
                        $"Triangle={triangleCount}/{expectedTriangleCount}，" +
                        $"Bounds={bounds.size}/{expectedBounds}。");

                bool hierarchyMatches =
                    instance.GetComponentsInChildren<Camera>(true).Length == 0 &&
                    instance.GetComponentsInChildren<Light>(true).Length == 0 &&
                    IsIdentity(instance.transform);
                if (!hierarchyMatches)
                    issues.Add("FBX 根 TRS 或 Camera/Light 过滤不符合候选契约。");

                string[] materialPaths = renderers
                    .SelectMany(renderer => renderer.sharedMaterials)
                    .Where(material => material != null)
                    .Select(AssetDatabase.GetAssetPath)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                return new ModelAudit(
                    hierarchyMatches, geometryMatches, meshes.Length, materialSlots,
                    vertexCount, triangleCount, expectedBounds, bounds.center, bounds.size,
                    materialPaths);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static bool AuditMaterial(
            IReadOnlyCollection<string> actualPaths,
            ICollection<string> issues)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            bool matches = actualPaths.Count == 1 && actualPaths.Contains(MaterialPath) &&
                           material != null && material.shader != null &&
                           material.shader.name == "Universal Render Pipeline/Lit" &&
                           material.GetTexture("_BaseMap") == LoadTexture(BaseColorPath) &&
                           material.GetTexture("_BumpMap") == LoadTexture(NormalPath) &&
                           material.GetTexture("_MetallicGlossMap") ==
                           LoadTexture(MetallicSmoothnessPath) &&
                           material.GetTexture("_OcclusionMap") == null &&
                           Vector4.Distance(material.GetColor("_BaseColor"), Color.white) <= 0.001f &&
                           Mathf.Approximately(material.GetFloat("_BumpScale"), 1f) &&
                           Mathf.Approximately(material.GetFloat("_Metallic"), 1f) &&
                           Mathf.Approximately(material.GetFloat("_Smoothness"), 1f) &&
                           material.IsKeywordEnabled("_NORMALMAP") &&
                           material.IsKeywordEnabled("_METALLICSPECGLOSSMAP") &&
                           material.enableInstancing;
            if (!matches)
                issues.Add("URP/Lit 材质或 PBR Shader Keyword 不符合候选通道契约。");
            return matches;
        }

        private static bool AuditPrefab(
            Vector3 modelBoundsCenter,
            Vector3 modelBoundsSize,
            ICollection<string> issues)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                issues.Add($"缺少 Prefab：{PrefabPath}");
                return false;
            }

            Transform visual = prefab.transform.Find("Visual");
            BoxCollider collider = prefab.GetComponent<BoxCollider>();
            bool matches = IsIdentity(prefab.transform) && visual != null &&
                           IsIdentity(visual) && collider != null &&
                           prefab.GetComponentsInChildren<BoxCollider>(true).Length == 1 &&
                           prefab.GetComponentsInChildren<MeshCollider>(true).Length == 0 &&
                           prefab.GetComponentsInChildren<MeshFilter>(true)
                               .Count(filter => filter.sharedMesh != null) == 1 &&
                           Approximately(
                               collider.center, modelBoundsCenter, BoundsToleranceMeters) &&
                           Approximately(collider.size, modelBoundsSize, BoundsToleranceMeters);
            if (!matches) issues.Add("Prefab 层级、BoxCollider 或模型 Bounds 不符合候选契约。");
            return matches;
        }

        private static bool AuditPreviewScene(int rendererIndex, ICollection<string> issues)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(PreviewScenePath) == null)
            {
                issues.Add($"缺少预览场景：{PreviewScenePath}");
                return false;
            }

            int issueCount = issues.Count;
            Scene previousActiveScene = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(PreviewScenePath);
            bool openedHere = !scene.IsValid() || !scene.isLoaded;
            try
            {
                if (openedHere)
                    scene = EditorSceneManager.OpenScene(PreviewScenePath, OpenSceneMode.Additive);
                SceneManager.SetActiveScene(scene);

                Camera[] cameras = GetSceneComponents<Camera>(scene);
                Light[] lights = GetSceneComponents<Light>(scene);
                GameObject instance = scene.GetRootGameObjects()
                    .SingleOrDefault(root => root.name == AssetId);
                GameObject ground = scene.GetRootGameObjects()
                    .SingleOrDefault(root => root.name == "PreviewGround");
                Camera camera = cameras.SingleOrDefault();
                UniversalAdditionalCameraData cameraData =
                    camera?.GetComponent<UniversalAdditionalCameraData>();
                int actualRendererIndex = -1;
                if (cameraData != null)
                {
                    SerializedProperty property = new SerializedObject(cameraData)
                        .FindProperty("m_RendererIndex");
                    if (property != null) actualRendererIndex = property.intValue;
                }

                bool matches = instance != null &&
                               PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance) ==
                               PrefabPath &&
                               ground?.GetComponent<MeshRenderer>()?.sharedMaterial ==
                               AssetDatabase.LoadAssetAtPath<Material>(
                                   NomadRenderingSpikePipeline.GroundMaterialPath) &&
                               cameras.Length == 1 && camera != null &&
                               camera.CompareTag("MainCamera") && camera.allowHDR &&
                               cameraData != null && actualRendererIndex == rendererIndex &&
                               cameraData.antialiasing ==
                               AntialiasingMode.SubpixelMorphologicalAntiAliasing &&
                               lights.Length == 3 &&
                               lights.Count(light => light.type == LightType.Directional) == 1 &&
                               lights.Count(light => light.type == LightType.Point) == 1 &&
                               lights.Count(light => light.type == LightType.Spot) == 1 &&
                               RenderSettings.skybox == AssetDatabase.LoadAssetAtPath<Material>(
                                   NomadRenderingSpikePipeline.SkyboxMaterialPath) &&
                               RenderSettings.ambientMode == AmbientMode.Skybox;
                if (!matches)
                    issues.Add("代表性场景的 Prefab、次级 Renderer、灯光或 Skybox 契约不成立。");
            }
            catch (Exception exception)
            {
                issues.Add($"读取 AI Mesh 预览场景失败：{exception.Message}");
            }
            finally
            {
                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                    SceneManager.SetActiveScene(previousActiveScene);
                if (openedHere && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
            return issues.Count == issueCount;
        }

        private static IntakeReport LoadAndValidateReport()
        {
            TextAsset textAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(IntakeReportPath);
            if (textAsset == null)
                throw new InvalidOperationException($"缺少 Blender Intake 报告：{IntakeReportPath}");
            IntakeReport report = JsonUtility.FromJson<IntakeReport>(textAsset.text) ??
                                  throw new InvalidOperationException("无法解析 Blender Intake 报告。");

            if (report.schemaVersion != SupportedSchemaVersion ||
                report.harnessVersion != SupportedHarnessVersion ||
                report.status != "inspected" || report.asset?.id != SourceAssetId ||
                report.asset.sourceObject != SourceMaterialName ||
                report.asset.sourceKind != "external-ai-mesh-candidate" ||
                report.asset.approval != "candidate-only" || report.asset.gameReady ||
                !report.asset.manualArtReviewRequired)
                throw new InvalidOperationException("Intake 报告的 schema、Harness、状态或候选边界不受支持。");
            if (report.toolchain?.sourceScriptSha256?.Length != 64 ||
                report.geometry?.meshObjectCount != 1 ||
                report.geometry.objects?.Length != 1 ||
                report.geometry.worldBounds?.size?.Length != 3 ||
                report.geometry.objects[0].materialSlots?.Length != 1 ||
                report.geometry.objects[0].materialSlots[0] != SourceMaterialName ||
                report.geometry.objects[0].topology == null ||
                report.geometry.objects[0].topology.looseVertexCount != 0 ||
                report.geometry.objects[0].topology.looseEdgeCount != 0 ||
                report.geometry.objects[0].topology.boundaryEdgeCount != 0 ||
                report.geometry.objects[0].topology.nonManifoldEdgeCount != 0 ||
                report.geometry.objects[0].uv == null ||
                report.geometry.objects[0].uv.degenerateTriangleCount != 0 ||
                report.geometry.objects[0].uv.outOfUnitRangeLoopCount != 0)
                throw new InvalidOperationException("Intake 报告的几何、拓扑或 UV 证据不完整。");
            if (report.exports?.roundTrips?.fbx == null ||
                !report.exports.roundTrips.fbx.succeeded ||
                report.exports.roundTrips.fbx.meshObjectCount != 1 ||
                report.exports.roundTrips.fbx.triangleCount !=
                report.geometry.objects[0].validatedTriangleCount ||
                report.derivedTextures?.urpMetallicSmoothness == null ||
                report.derivedTextures.urpMetallicSmoothness.packing !=
                "R=metallic, A=1-roughness (URP smoothness)" ||
                report.acceptance == null || !report.acceptance.sourcePreserved ||
                !report.acceptance.allReferencedTexturesArchived ||
                !report.acceptance.fbxExported || !report.acceptance.fbxRoundTripRead ||
                !report.acceptance.fbxTriangleCountMatchesValidatedSource ||
                !report.acceptance.allMeshesHaveActiveUv ||
                !report.acceptance.pbrCoreMapped ||
                !report.acceptance.urpMetallicSmoothnessGenerated ||
                !report.acceptance.manualArtReviewStillRequired ||
                report.acceptance.productionApproved)
                throw new InvalidOperationException("Intake 报告的 FBX、PBR 或人工复核证据不完整。");
            if (FindTexture(report, "Base Color").colorspace != "sRGB" ||
                FindTexture(report, "Normal").colorspace != "Non-Color" ||
                GetTextureResolution(report) < 256)
                throw new InvalidOperationException("Intake 报告的贴图色彩空间或分辨率不受支持。");
            if (report.files == null || report.files.All(file => file.path != report.exports.fbx))
                throw new InvalidOperationException("Intake 报告缺少 FBX 文件哈希证据。");
            return report;
        }

        private static TextureEvidence FindTexture(IntakeReport report, string usage)
        {
            TextureEvidence texture = report.textures?.SingleOrDefault(candidate =>
                candidate.usages?.Contains(usage) == true);
            return texture ?? throw new InvalidOperationException($"Intake 报告缺少 {usage} 贴图。");
        }

        private static int GetTextureResolution(IntakeReport report)
        {
            TextureEvidence baseColor = FindTexture(report, "Base Color");
            TextureEvidence normal = FindTexture(report, "Normal");
            DerivedTexture mask = report.derivedTextures.urpMetallicSmoothness;
            if (baseColor.width != baseColor.height || normal.width != normal.height ||
                mask.width != mask.height || baseColor.width != normal.width ||
                baseColor.width != mask.width)
                throw new InvalidOperationException("三张 Unity PBR 贴图的尺寸不一致。");
            return baseColor.width;
        }

        private static Texture2D LoadTexture(string path)
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path) ??
                   throw new InvalidOperationException($"无法加载贴图：{path}");
        }

        private static Bounds CalculateLocalRendererBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                throw new InvalidOperationException("模型没有可计算 Bounds 的 Renderer。");

            bool initialized = false;
            Vector3 minimum = default;
            Vector3 maximum = default;
            foreach (Renderer renderer in renderers)
            {
                Bounds bounds = renderer.bounds;
                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 point = root.transform.InverseTransformPoint(
                        bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z)));
                    if (!initialized)
                    {
                        minimum = point;
                        maximum = point;
                        initialized = true;
                    }
                    else
                    {
                        minimum = Vector3.Min(minimum, point);
                        maximum = Vector3.Max(maximum, point);
                    }
                }
            }
            return new Bounds((minimum + maximum) * 0.5f, maximum - minimum);
        }

        private static T[] GetSceneComponents<T>(Scene scene) where T : Component
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<T>(true))
                .ToArray();
        }

        private static string ComputeSha256(string path)
        {
            using SHA256 algorithm = SHA256.Create();
            using FileStream stream = File.OpenRead(path);
            return BitConverter.ToString(algorithm.ComputeHash(stream))
                .Replace("-", string.Empty)
                .ToLowerInvariant();
        }

        private static string ToAbsoluteProjectPath(string path)
        {
            string root = Directory.GetParent(Application.dataPath)?.FullName ??
                          throw new InvalidOperationException("无法定位项目根目录。");
            return Path.GetFullPath(Path.Combine(root, path));
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

        private static void EnsureFolder(string folderPath)
        {
            string[] segments = folderPath.Split('/');
            string current = segments[0];
            for (int i = 1; i < segments.Length; i++)
            {
                string next = current + "/" + segments[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, segments[i]);
                current = next;
            }
        }

        private static void WriteReport(NomadAiMeshCandidateAudit audit, IntakeReport source)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ??
                                 throw new InvalidOperationException("无法定位项目根目录。");
            string outputFolder = Path.Combine(
                projectRoot, "ArtPipelineOutput", "UnityImportSpike", AssetId);
            Directory.CreateDirectory(outputFolder);
            var report = new UnityImportReport
            {
                schemaVersion = 1,
                status = audit.Passed ? "passed" : "failed",
                unityVersion = Application.unityVersion,
                generatedAtUtc = DateTime.UtcNow.ToString("O"),
                assetId = AssetId,
                upstreamAssetId = source.asset.id,
                upstreamHarnessVersion = source.harnessVersion,
                modelPath = ModelPath,
                materialPath = MaterialPath,
                prefabPath = PrefabPath,
                previewScenePath = PreviewScenePath,
                meshObjectCount = audit.MeshObjectCount,
                materialSlotCount = audit.MaterialSlotCount,
                sourceVertexCount = audit.SourceVertexCount,
                unityVertexCount = audit.UnityVertexCount,
                triangleCount = audit.TriangleCount,
                expectedBoundsSize = audit.ExpectedBoundsSize,
                actualBoundsSize = audit.ActualBoundsSize,
                duplicateFaceCountInSource = audit.DuplicateFaceCountInSource,
                connectedComponentCount = audit.ConnectedComponentCount,
                deliveredTextureResolution = audit.DeliveredTextureResolution,
                upstreamWarnings = audit.UpstreamWarnings.ToArray(),
                renderingVerdict = audit.RenderingVerdict,
                issues = audit.Issues.ToArray(),
                manualArtReviewRequired = audit.ManualArtReviewRequired,
                productionApproved = audit.ProductionApproved,
            };
            File.WriteAllText(
                Path.Combine(outputFolder, "report.json"),
                JsonUtility.ToJson(report, true) + Environment.NewLine);
        }

        private enum TextureRole
        {
            BaseColor,
            Normal,
            MetallicSmoothness,
        }

        private readonly struct ModelAudit
        {
            public ModelAudit(
                bool hierarchyMatches,
                bool geometryMatches,
                int meshObjectCount,
                int materialSlotCount,
                int vertexCount,
                int triangleCount,
                Vector3 expectedBoundsSize,
                Vector3 boundsCenter,
                Vector3 boundsSize,
                IReadOnlyList<string> materialPaths)
            {
                HierarchyMatches = hierarchyMatches;
                GeometryMatches = geometryMatches;
                MeshObjectCount = meshObjectCount;
                MaterialSlotCount = materialSlotCount;
                VertexCount = vertexCount;
                TriangleCount = triangleCount;
                ExpectedBoundsSize = expectedBoundsSize;
                BoundsCenter = boundsCenter;
                BoundsSize = boundsSize;
                MaterialPaths = materialPaths;
            }

            public bool HierarchyMatches { get; }
            public bool GeometryMatches { get; }
            public int MeshObjectCount { get; }
            public int MaterialSlotCount { get; }
            public int VertexCount { get; }
            public int TriangleCount { get; }
            public Vector3 ExpectedBoundsSize { get; }
            public Vector3 BoundsCenter { get; }
            public Vector3 BoundsSize { get; }
            public IReadOnlyList<string> MaterialPaths { get; }

            public static ModelAudit Empty(Vector3 expectedBounds)
            {
                return new ModelAudit(
                    false, false, 0, 0, 0, 0, expectedBounds, Vector3.zero, Vector3.zero,
                    Array.Empty<string>());
            }
        }

        [Serializable]
        private sealed class IntakeReport
        {
            public int schemaVersion;
            public string harnessVersion = string.Empty;
            public string status = string.Empty;
            public IntakeAsset asset;
            public IntakeToolchain toolchain;
            public IntakeGeometry geometry;
            public TextureEvidence[] textures = Array.Empty<TextureEvidence>();
            public DerivedTextures derivedTextures;
            public IntakeExports exports;
            public IntakeAcceptance acceptance;
            public string[] warnings = Array.Empty<string>();
            public IntakeFile[] files = Array.Empty<IntakeFile>();
        }

        [Serializable]
        private sealed class IntakeAsset
        {
            public string id = string.Empty;
            public string sourceObject = string.Empty;
            public string sourceKind = string.Empty;
            public string approval = string.Empty;
            public bool gameReady;
            public bool manualArtReviewRequired;
        }

        [Serializable]
        private sealed class IntakeToolchain
        {
            public string sourceScriptSha256 = string.Empty;
        }

        [Serializable]
        private sealed class IntakeGeometry
        {
            public int meshObjectCount;
            public BoundsEvidence worldBounds;
            public GeometryObject[] objects = Array.Empty<GeometryObject>();
        }

        [Serializable]
        private sealed class BoundsEvidence
        {
            public float[] size = Array.Empty<float>();
        }

        [Serializable]
        private sealed class GeometryObject
        {
            public int vertexCount;
            public int duplicateFaceCount;
            public int validatedTriangleCount;
            public int connectedComponentCount;
            public TopologyEvidence topology;
            public UvEvidence uv;
            public string[] materialSlots = Array.Empty<string>();
        }

        [Serializable]
        private sealed class TopologyEvidence
        {
            public int looseVertexCount;
            public int looseEdgeCount;
            public int boundaryEdgeCount;
            public int nonManifoldEdgeCount;
        }

        [Serializable]
        private sealed class UvEvidence
        {
            public int degenerateTriangleCount;
            public int outOfUnitRangeLoopCount;
        }

        [Serializable]
        private sealed class TextureEvidence
        {
            public string[] usages = Array.Empty<string>();
            public int width;
            public int height;
            public string colorspace = string.Empty;
            public long bytes;
            public string sha256 = string.Empty;
        }

        [Serializable]
        private sealed class DerivedTextures
        {
            public DerivedTexture urpMetallicSmoothness;
        }

        [Serializable]
        private sealed class DerivedTexture
        {
            public int width;
            public int height;
            public string packing = string.Empty;
            public long bytes;
            public string sha256 = string.Empty;
        }

        [Serializable]
        private sealed class IntakeExports
        {
            public string fbx = string.Empty;
            public RoundTrips roundTrips;
        }

        [Serializable]
        private sealed class RoundTrips
        {
            public RoundTrip fbx;
        }

        [Serializable]
        private sealed class RoundTrip
        {
            public bool succeeded;
            public int meshObjectCount;
            public int vertexCount;
            public int triangleCount;
        }

        [Serializable]
        private sealed class IntakeAcceptance
        {
            public bool sourcePreserved;
            public bool allReferencedTexturesArchived;
            public bool fbxExported;
            public bool fbxRoundTripRead;
            public bool fbxTriangleCountMatchesValidatedSource;
            public bool allMeshesHaveActiveUv;
            public bool pbrCoreMapped;
            public bool urpMetallicSmoothnessGenerated;
            public bool manualArtReviewStillRequired;
            public bool productionApproved;
        }

        [Serializable]
        private sealed class IntakeFile
        {
            public string path = string.Empty;
            public long bytes;
            public string sha256 = string.Empty;
        }

        [Serializable]
        private sealed class UnityImportReport
        {
            public int schemaVersion;
            public string status = string.Empty;
            public string unityVersion = string.Empty;
            public string generatedAtUtc = string.Empty;
            public string assetId = string.Empty;
            public string upstreamAssetId = string.Empty;
            public string upstreamHarnessVersion = string.Empty;
            public string modelPath = string.Empty;
            public string materialPath = string.Empty;
            public string prefabPath = string.Empty;
            public string previewScenePath = string.Empty;
            public int meshObjectCount;
            public int materialSlotCount;
            public int sourceVertexCount;
            public int unityVertexCount;
            public int triangleCount;
            public Vector3 expectedBoundsSize;
            public Vector3 actualBoundsSize;
            public int duplicateFaceCountInSource;
            public int connectedComponentCount;
            public int deliveredTextureResolution;
            public string[] upstreamWarnings = Array.Empty<string>();
            public string renderingVerdict = string.Empty;
            public string[] issues = Array.Empty<string>();
            public bool manualArtReviewRequired;
            public bool productionApproved;
        }
    }

    /// <summary>外部 AI Mesh 候选从来源字节到 Unity URP 显示的结构化审计结果。</summary>
    public sealed class NomadAiMeshCandidateAudit
    {
        internal NomadAiMeshCandidateAudit(
            bool sourceHashesMatch,
            bool textureImporterContractMatches,
            bool modelImporterContractMatches,
            bool sourceHierarchyMatches,
            bool geometryMatches,
            bool materialContractMatches,
            bool prefabContractMatches,
            bool previewSceneContractMatches,
            int meshObjectCount,
            int materialSlotCount,
            int sourceVertexCount,
            int unityVertexCount,
            int triangleCount,
            Vector3 expectedBoundsSize,
            Vector3 actualBoundsSize,
            int duplicateFaceCountInSource,
            int connectedComponentCount,
            int deliveredTextureResolution,
            bool manualArtReviewRequired,
            bool productionApproved,
            string renderingVerdict,
            IReadOnlyList<string> upstreamWarnings,
            IReadOnlyList<string> issues)
        {
            SourceHashesMatch = sourceHashesMatch;
            TextureImporterContractMatches = textureImporterContractMatches;
            ModelImporterContractMatches = modelImporterContractMatches;
            SourceHierarchyMatches = sourceHierarchyMatches;
            GeometryMatches = geometryMatches;
            MaterialContractMatches = materialContractMatches;
            PrefabContractMatches = prefabContractMatches;
            PreviewSceneContractMatches = previewSceneContractMatches;
            MeshObjectCount = meshObjectCount;
            MaterialSlotCount = materialSlotCount;
            SourceVertexCount = sourceVertexCount;
            UnityVertexCount = unityVertexCount;
            TriangleCount = triangleCount;
            ExpectedBoundsSize = expectedBoundsSize;
            ActualBoundsSize = actualBoundsSize;
            DuplicateFaceCountInSource = duplicateFaceCountInSource;
            ConnectedComponentCount = connectedComponentCount;
            DeliveredTextureResolution = deliveredTextureResolution;
            ManualArtReviewRequired = manualArtReviewRequired;
            ProductionApproved = productionApproved;
            RenderingVerdict = renderingVerdict;
            UpstreamWarnings = upstreamWarnings;
            Issues = issues;
        }

        public bool SourceHashesMatch { get; }
        public bool TextureImporterContractMatches { get; }
        public bool ModelImporterContractMatches { get; }
        public bool SourceHierarchyMatches { get; }
        public bool GeometryMatches { get; }
        public bool MaterialContractMatches { get; }
        public bool PrefabContractMatches { get; }
        public bool PreviewSceneContractMatches { get; }
        public int MeshObjectCount { get; }
        public int MaterialSlotCount { get; }
        public int SourceVertexCount { get; }
        public int UnityVertexCount { get; }
        public int TriangleCount { get; }
        public Vector3 ExpectedBoundsSize { get; }
        public Vector3 ActualBoundsSize { get; }
        public int DuplicateFaceCountInSource { get; }
        public int ConnectedComponentCount { get; }
        public int DeliveredTextureResolution { get; }
        public bool ManualArtReviewRequired { get; }
        public bool ProductionApproved { get; }
        public string RenderingVerdict { get; }
        public IReadOnlyList<string> UpstreamWarnings { get; }
        public IReadOnlyList<string> Issues { get; }

        public bool Passed => SourceHashesMatch && TextureImporterContractMatches &&
                              ModelImporterContractMatches && SourceHierarchyMatches &&
                              GeometryMatches && MaterialContractMatches &&
                              PrefabContractMatches && PreviewSceneContractMatches &&
                              ManualArtReviewRequired && !ProductionApproved && Issues.Count == 0;

        public string ToMultilineString()
        {
            var lines = new List<string>
            {
                $"[NomadAiMeshCandidate] {(Passed ? "PASS" : "FAIL")}",
                $"- 来源字节证据：{SourceHashesMatch}",
                $"- TextureImporter PBR 语义：{TextureImporterContractMatches}",
                $"- ModelImporter 与材质重映射：{ModelImporterContractMatches}",
                $"- FBX 层级与过滤：{SourceHierarchyMatches}",
                $"- 几何与 Bounds：{GeometryMatches}",
                $"- URP/Lit 材质：{MaterialContractMatches}",
                $"- Prefab 与 BoxCollider：{PrefabContractMatches}",
                $"- 代表性预览场景：{PreviewSceneContractMatches}",
                $"- 几何：source/runtime vertex={SourceVertexCount}/{UnityVertexCount}, " +
                $"triangles={TriangleCount}, bounds={ActualBoundsSize}",
                $"- 已知上游风险：重复面 {DuplicateFaceCountInSource}，" +
                $"几何岛 {ConnectedComponentCount}，贴图 {DeliveredTextureResolution}px",
                $"- 视觉结论：{RenderingVerdict}",
            };
            foreach (string warning in UpstreamWarnings) lines.Add($"  ~ {warning}");
            foreach (string issue in Issues) lines.Add($"  ! {issue}");
            return string.Join(Environment.NewLine, lines);
        }
    }
}
