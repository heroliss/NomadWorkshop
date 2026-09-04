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
    /// 把确定性 Blender PBR 输出翻译成 Unity 贴图导入设置、URP/Lit 材质、Prefab 与代表性预览场景。
    /// 这是第二条可删除的资产管线证据，不是隐式修改所有模型的全局 AssetPostprocessor。
    /// </summary>
    public static class NomadTexturedPropAssetPipeline
    {
        public const string AssetId = "NW_WaterRecycler_01";
        public const string AssetRoot =
            "Assets/Game/NomadWorkshop/Spikes/BlenderImport/NW_WaterRecycler_01";
        public const string ModelPath = AssetRoot + "/" + AssetId + ".fbx";
        public const string ManifestPath = AssetRoot + "/" + AssetId + ".manifest.json";
        public const string TextureFolder = AssetRoot + "/Textures";
        public const string EvidenceFolder = AssetRoot + "/Evidence";
        /// <summary>供人工审查的 Blender 六视图证据；不是运行时贴图或自动美术批准。</summary>
        public const string ContactSheetPath =
            EvidenceFolder + "/" + AssetId + "_contact_sheet.png";
        public const string MaterialFolder = AssetRoot + "/Materials";
        public const string PrefabFolder = AssetRoot + "/Prefabs";
        public const string PrefabPath = PrefabFolder + "/" + AssetId + ".prefab";
        public const string PreviewFolder = AssetRoot + "/Preview";
        public const string PreviewScenePath = PreviewFolder + "/" + AssetId + "_Urp3DPreview.unity";

        private const string BlenderScriptPath =
            "Tools/ArtPipeline/Blender/blender_textured_prop.py";
        private const int SupportedManifestSchemaVersion = 3;
        private const string SupportedHarnessVersion = "0.4.0";
        private const float BoundsToleranceMeters = 0.008f;
        private const float MaxRuntimeVertexExpansion = 4f;

        /// <summary>
        /// 显式重放导入策略与生成步骤，然后写出结构化报告；不会修改项目中的其他 FBX 或贴图。
        /// </summary>
        [MenuItem("Assets/SSFramework/游牧工坊/Blender Import Spike/配置并审计纹理化水循环设施")]
        public static void ConfigureAndReport()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出 Play Mode，再配置纹理化 Blender 资产。");

            BlenderManifest manifest = LoadManifest();
            ValidateManifest(manifest);
            EnsureFolder(EvidenceFolder);
            EnsureFolder(MaterialFolder);
            EnsureFolder(PrefabFolder);
            EnsureFolder(PreviewFolder);

            ConfigureEvidenceImporter(manifest);
            ConfigureTextureImporters(manifest);
            IReadOnlyDictionary<string, Material> materials = CreateOrUpdateMaterials(manifest);
            ApplyModelImportPolicy(materials);
            CreateOrUpdatePrefab();
            EnsurePreviewScene();
            AssetDatabase.SaveAssets();

            NomadTexturedPropAssetAudit audit = Audit();
            WriteReport(audit);
            if (!audit.Passed) throw new InvalidOperationException(audit.ToMultilineString());
            Debug.Log(audit.ToMultilineString());
        }

        /// <summary>只读取落盘产物，验证从源文件哈希到代表性 URP 预览场景的证据链。</summary>
        public static NomadTexturedPropAssetAudit Audit()
        {
            BlenderManifest manifest = LoadManifest();
            ValidateManifest(manifest);
            var issues = new List<string>();

            bool sourceHashesMatch = AuditSourceHashes(manifest, issues);
            bool evidenceImporterMatches = AuditEvidenceImporter(manifest, issues);
            bool textureImportersMatch = AuditTextureImporters(manifest, issues);
            bool modelImporterMatches = AuditModelImporter(manifest, issues);
            ModelAudit modelAudit = AuditModel(manifest, issues);
            bool materialsMatch = AuditMaterials(manifest, modelAudit.MaterialAssetPaths, issues);
            bool prefabMatches = AuditPrefab(modelAudit.BoundsSize, issues);

            int rendererIndex = -1;
            try
            {
                rendererIndex = NomadRenderingSpikePipeline.GetGame3DRendererIndexOrThrow();
            }
            catch (Exception exception)
            {
                issues.Add($"共享 3D Renderer 不可用：{exception.Message}");
            }

            bool previewMatches = rendererIndex >= 0 &&
                                  AuditPreviewScene(rendererIndex, issues);
            return new NomadTexturedPropAssetAudit(
                sourceHashesMatch,
                evidenceImporterMatches,
                textureImportersMatch,
                modelImporterMatches,
                modelAudit.HierarchyMatches,
                modelAudit.GeometryMatches,
                materialsMatch,
                prefabMatches,
                previewMatches,
                modelAudit.MeshObjectCount,
                modelAudit.MaterialSlotCount,
                manifest.materials.Sum(_ => 4),
                modelAudit.SourceVertexCount,
                modelAudit.VertexCount,
                modelAudit.TriangleCount,
                modelAudit.ExpectedBoundsSize,
                modelAudit.BoundsSize,
                modelAudit.MaterialAssetPaths,
                manifest.geometry.quality.topology.nonManifoldEdgeCount,
                manifest.geometry.quality.uv.degenerateUvTriangleCount,
                manifest.geometry.quality.uv.areaWeightedTexelDensityPxPerMeter,
                "manual_review_required：导入、通道和场景契约可自动验证；构图、材质可信度与风格一致性仍需看图判断。",
                issues);
        }

        private static void ConfigureEvidenceImporter(BlenderManifest manifest)
        {
            if (AssetImporter.GetAtPath(ContactSheetPath) is not TextureImporter importer)
                throw new InvalidOperationException($"没有找到 Contact Sheet：{ContactSheetPath}");

            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.spriteImportMode = SpriteImportMode.None;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.alphaIsTransparency = false;
            importer.isReadable = false;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = 1;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = Mathf.NextPowerOfTwo(Mathf.Max(
                manifest.visualEvidence.contactSheet.width,
                manifest.visualEvidence.contactSheet.height));
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.crunchedCompression = false;
            importer.streamingMipmaps = false;
            importer.flipGreenChannel = false;
            importer.SaveAndReimport();
        }

        private static void ConfigureTextureImporters(BlenderManifest manifest)
        {
            foreach (TextureBinding binding in EnumerateTextureBindings(manifest))
            {
                string path = GetTexturePath(binding.Texture);
                if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
                    throw new InvalidOperationException($"没有找到可配置的 TextureImporter：{path}");

                bool isBaseColor = binding.Role == TextureRole.BaseColor;
                bool isNormal = binding.Role == TextureRole.Normal;
                bool keepsInputAlpha = binding.Role == TextureRole.MetallicSmoothness;
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
                importer.maxTextureSize = manifest.textureContract.width;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.crunchedCompression = false;
                importer.streamingMipmaps = false;
                importer.flipGreenChannel = false;
                importer.SaveAndReimport();
            }
        }

        private static IReadOnlyDictionary<string, Material> CreateOrUpdateMaterials(
            BlenderManifest manifest)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                throw new InvalidOperationException("当前项目找不到 Universal Render Pipeline/Lit Shader。");

            var result = new Dictionary<string, Material>(StringComparer.Ordinal);
            foreach (BlenderMaterial spec in manifest.materials)
            {
                string path = GetMaterialPath(spec.name);
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader) { name = spec.name };
                    AssetDatabase.CreateAsset(material, path);
                }
                else
                {
                    material.shader = shader;
                }

                Texture2D baseColor = LoadTexture(spec.textures.baseColor);
                Texture2D normal = LoadTexture(spec.textures.normal);
                Texture2D mask = LoadTexture(spec.textures.metallicSmoothness);
                Texture2D occlusion = LoadTexture(spec.textures.occlusion);

                material.SetFloat("_WorkflowMode", (float)LitGUI.WorkflowMode.Metallic);
                material.SetColor("_BaseColor", Color.white);
                material.SetTexture("_BaseMap", baseColor);
                material.SetTextureScale(
                    "_BaseMap", new Vector2(spec.textureTiling[0], spec.textureTiling[1]));
                material.SetTextureOffset("_BaseMap", Vector2.zero);
                material.SetTexture("_BumpMap", normal);
                material.SetFloat("_BumpScale", spec.normalScale);
                material.SetTexture("_MetallicGlossMap", mask);
                material.SetFloat("_Metallic", 1f);
                material.SetFloat("_Smoothness", 1f);
                material.SetFloat(
                    "_SmoothnessTextureChannel",
                    (float)LitGUI.SmoothnessMapChannel.SpecularMetallicAlpha);
                material.SetTexture("_OcclusionMap", occlusion);
                material.SetFloat("_OcclusionStrength", spec.occlusionStrength);
                material.SetFloat("_Surface", 0f);
                material.SetFloat("_AlphaClip", 0f);
                material.SetFloat("_Cull", (float)CullMode.Back);
                material.enableInstancing = true;
                material.doubleSidedGI = false;
                BaseShaderGUI.SetMaterialKeywords(material, LitGUI.SetMaterialKeywords);
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

            AssetImporter.SourceAssetIdentifier[] staleMaterialRemaps = importer
                .GetExternalObjectMap()
                .Where(pair => pair.Key.type == typeof(Material))
                .Select(pair => pair.Key)
                .ToArray();
            foreach (AssetImporter.SourceAssetIdentifier identifier in staleMaterialRemaps)
                importer.RemoveRemap(identifier);
            foreach ((string sourceName, Material material) in materials)
            {
                importer.AddRemap(
                    new AssetImporter.SourceAssetIdentifier(typeof(Material), sourceName),
                    material);
            }
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
            int rendererIndex = NomadRenderingSpikePipeline.GetGame3DRendererIndexOrThrow();
            var drift = new List<string>();
            if (AuditPreviewScene(rendererIndex, drift)) return;

            Scene loadedScene = SceneManager.GetSceneByPath(PreviewScenePath);
            if (loadedScene.IsValid() && loadedScene.isLoaded)
                throw new InvalidOperationException(
                    "水循环设施预览场景正在编辑器中打开；为避免覆盖当前 Scene 实例，本次停止。");
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
                    "缺少水循环设施 Prefab 或 3D Renderer Spike 材质；请先完成对应配置。");

            Scene previousActiveScene = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                GameObject instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
                if (instance == null) throw new InvalidOperationException("无法实例化水循环设施 Prefab。");
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
                RenderSettings.ambientIntensity = 1.05f;
                RenderSettings.reflectionIntensity = 1.00f;
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
            camera.fieldOfView = 36f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 100f;
            camera.allowHDR = true;
            cameraObject.transform.position = new Vector3(2.15f, 1.68f, -2.58f);
            cameraObject.transform.LookAt(new Vector3(0f, 0.72f, 0f));

            UniversalAdditionalCameraData cameraData =
                camera.GetUniversalAdditionalCameraData();
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
            key.intensity = 1.70f;
            key.shadows = LightShadows.Soft;
            key.shadowStrength = 0.82f;
            keyObject.transform.rotation = Quaternion.Euler(44f, -32f, 0f);

            var fillObject = new GameObject("Fill Light");
            SceneManager.MoveGameObjectToScene(fillObject, scene);
            Light fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.color = new Color(0.42f, 0.62f, 1f, 1f);
            fill.intensity = 4.2f;
            fill.range = 7f;
            fill.shadows = LightShadows.None;
            fillObject.transform.position = new Vector3(-2.2f, 1.9f, -1.7f);

            var rimObject = new GameObject("Rim Light");
            SceneManager.MoveGameObjectToScene(rimObject, scene);
            Light rim = rimObject.AddComponent<Light>();
            rim.type = LightType.Spot;
            rim.color = new Color(1f, 0.46f, 0.22f, 1f);
            rim.intensity = 5.0f;
            rim.range = 7f;
            rim.spotAngle = 58f;
            rim.innerSpotAngle = 34f;
            rim.shadows = LightShadows.None;
            rimObject.transform.position = new Vector3(1.9f, 2.35f, 2.25f);
            rimObject.transform.LookAt(new Vector3(0f, 0.75f, 0f));
            return key;
        }

        private static bool AuditSourceHashes(
            BlenderManifest manifest,
            ICollection<string> issues)
        {
            bool matches = true;
            string scriptPath = ToAbsoluteProjectPath(BlenderScriptPath);
            if (!File.Exists(scriptPath) || !HashMatches(
                    scriptPath, manifest.toolchain.sourceScriptSha256))
            {
                matches = false;
                issues.Add("Blender 生成脚本 SHA-256 与 manifest 不一致。");
            }

            BlenderFile modelFile = FindManifestFile(manifest, AssetId + ".fbx");
            string modelPath = ToAbsoluteProjectPath(ModelPath);
            if (modelFile == null || !File.Exists(modelPath) ||
                !HashAndLengthMatch(modelPath, modelFile.sha256, modelFile.bytes))
            {
                matches = false;
                issues.Add("Unity 内 FBX 与 manifest 的字节证据不一致。");
            }

            BlenderFile contactSheetFile = FindManifestFile(
                manifest, manifest.visualEvidence.contactSheet.file);
            string contactSheetPath = ToAbsoluteProjectPath(ContactSheetPath);
            if (contactSheetFile == null || !File.Exists(contactSheetPath) ||
                !HashAndLengthMatch(
                    contactSheetPath,
                    contactSheetFile.sha256,
                    contactSheetFile.bytes))
            {
                matches = false;
                issues.Add("Unity 内 Contact Sheet 与 manifest 的字节证据不一致。");
            }

            foreach (TextureBinding binding in EnumerateTextureBindings(manifest))
            {
                BlenderTexture texture = binding.Texture;
                BlenderFile file = FindManifestFile(manifest, texture.file);
                string path = ToAbsoluteProjectPath(GetTexturePath(texture));
                bool textureMatches = file != null &&
                                      string.Equals(file.sha256, texture.sha256,
                                          StringComparison.OrdinalIgnoreCase) &&
                                      file.bytes == texture.bytes &&
                                      File.Exists(path) &&
                                      HashAndLengthMatch(path, texture.sha256, texture.bytes);
                if (textureMatches) continue;
                matches = false;
                issues.Add($"贴图字节证据不一致：{texture.file}");
            }
            return matches;
        }

        private static bool AuditEvidenceImporter(
            BlenderManifest manifest,
            ICollection<string> issues)
        {
            TextureImporter importer = AssetImporter.GetAtPath(ContactSheetPath) as TextureImporter;
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(ContactSheetPath);
            BlenderContactSheet contactSheet = manifest.visualEvidence.contactSheet;
            int expectedMaxSize = Mathf.NextPowerOfTwo(Mathf.Max(
                contactSheet.width,
                contactSheet.height));
            bool matches = importer != null && texture != null &&
                           importer.textureType == TextureImporterType.Default &&
                           importer.textureShape == TextureImporterShape.Texture2D &&
                           importer.spriteImportMode == SpriteImportMode.None &&
                           importer.sRGBTexture &&
                           importer.alphaSource == TextureImporterAlphaSource.None &&
                           !importer.alphaIsTransparency &&
                           !importer.isReadable &&
                           !importer.mipmapEnabled &&
                           importer.wrapMode == TextureWrapMode.Clamp &&
                           importer.filterMode == FilterMode.Bilinear &&
                           importer.anisoLevel == 1 &&
                           importer.npotScale == TextureImporterNPOTScale.None &&
                           importer.maxTextureSize == expectedMaxSize &&
                           importer.textureCompression == TextureImporterCompression.Uncompressed &&
                           !importer.crunchedCompression &&
                           !importer.streamingMipmaps &&
                           !importer.flipGreenChannel &&
                           texture.width == contactSheet.width &&
                           texture.height == contactSheet.height;
            if (!matches)
                issues.Add($"Contact Sheet 的导入策略或尺寸不符合证据契约：{ContactSheetPath}");
            return matches;
        }

        private static bool AuditTextureImporters(
            BlenderManifest manifest,
            ICollection<string> issues)
        {
            bool matches = true;
            foreach (TextureBinding binding in EnumerateTextureBindings(manifest))
            {
                string path = GetTexturePath(binding.Texture);
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                bool isBaseColor = binding.Role == TextureRole.BaseColor;
                bool isNormal = binding.Role == TextureRole.Normal;
                bool keepsInputAlpha = binding.Role == TextureRole.MetallicSmoothness;
                bool itemMatches = importer != null && texture != null &&
                                   importer.textureType == (isNormal
                                       ? TextureImporterType.NormalMap
                                       : TextureImporterType.Default) &&
                                   importer.textureShape == TextureImporterShape.Texture2D &&
                                   importer.sRGBTexture == isBaseColor &&
                                   importer.alphaSource == (keepsInputAlpha
                                       ? TextureImporterAlphaSource.FromInput
                                       : TextureImporterAlphaSource.None) &&
                                   !importer.alphaIsTransparency &&
                                   importer.mipmapEnabled &&
                                   importer.wrapMode == TextureWrapMode.Repeat &&
                                   importer.filterMode == FilterMode.Trilinear &&
                                   importer.anisoLevel == 4 &&
                                   importer.npotScale == TextureImporterNPOTScale.None &&
                                   importer.maxTextureSize == manifest.textureContract.width &&
                                   importer.textureCompression == TextureImporterCompression.CompressedHQ &&
                                   !importer.crunchedCompression &&
                                   !importer.streamingMipmaps &&
                                   !importer.flipGreenChannel &&
                                   texture.width == manifest.textureContract.width &&
                                   texture.height == manifest.textureContract.height;
                if (itemMatches) continue;
                matches = false;
                issues.Add($"TextureImporter 或尺寸不符合 PBR 语义：{path}");
            }
            return matches;
        }

        private static bool AuditModelImporter(
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
                !importer.importBlendShapes &&
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

            KeyValuePair<AssetImporter.SourceAssetIdentifier, UnityEngine.Object>[] remaps =
                importer.GetExternalObjectMap()
                    .Where(pair => pair.Key.type == typeof(Material))
                    .ToArray();
            bool remapsMatch = remaps.Length == manifest.materials.Length &&
                               manifest.materials.All(spec => remaps.Any(pair =>
                                   pair.Key.name == spec.name &&
                                   pair.Value == AssetDatabase.LoadAssetAtPath<Material>(
                                       GetMaterialPath(spec.name))));
            if (!policyMatches) issues.Add("ModelImporter 不符合纹理化静态道具契约。");
            if (!remapsMatch) issues.Add("FBX 三个源材质没有精确映射到同名外部材质。");
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

            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject instance = PrefabUtility.InstantiatePrefab(model, scene) as GameObject;
                if (instance == null)
                {
                    issues.Add("无法实例化导入模型。");
                    return ModelAudit.Empty(manifest);
                }

                MeshFilter[] meshFilters = instance.GetComponentsInChildren<MeshFilter>(true)
                    .Where(filter => filter.sharedMesh != null)
                    .ToArray();
                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                int materialSlotCount = renderers.Sum(renderer => renderer.sharedMaterials.Length);
                int vertexCount = meshFilters.Sum(filter => filter.sharedMesh.vertexCount);
                int triangleCount = meshFilters.Sum(filter =>
                {
                    Mesh mesh = filter.sharedMesh;
                    long count = 0;
                    for (int i = 0; i < mesh.subMeshCount; i++)
                        count += (long)mesh.GetIndexCount(i) / 3;
                    return checked((int)count);
                });
                Bounds bounds = CalculateLocalRendererBounds(instance);
                Vector3 expectedBounds = ToExpectedUnitySize(manifest.geometry.boundsMeters.size);
                bool geometryMatches =
                    meshFilters.Length == manifest.geometry.meshObjectCount &&
                    materialSlotCount == manifest.geometry.materialSlotCount &&
                    renderers.All(renderer => renderer.sharedMaterials.Length == 1) &&
                    vertexCount >= manifest.geometry.vertexCount &&
                    vertexCount <= manifest.geometry.vertexCount * MaxRuntimeVertexExpansion &&
                    triangleCount == manifest.geometry.triangleCount &&
                    Approximately(bounds.size, expectedBounds, BoundsToleranceMeters);
                if (!geometryMatches)
                    issues.Add(
                        $"几何不符合 manifest：Mesh={meshFilters.Length}/{manifest.geometry.meshObjectCount}，" +
                        $"Slot={materialSlotCount}/{manifest.geometry.materialSlotCount}，" +
                        $"Runtime/Source Vertex={vertexCount}/{manifest.geometry.vertexCount}，" +
                        $"Triangle={triangleCount}/{manifest.geometry.triangleCount}，" +
                        $"Bounds={bounds.size}/{expectedBounds}");

                var actualNames = new HashSet<string>(
                    meshFilters.Select(filter => filter.transform.name), StringComparer.Ordinal);
                var expectedNames = new HashSet<string>(
                    manifest.geometry.objects, StringComparer.Ordinal);
                bool hierarchyMatches = actualNames.SetEquals(expectedNames) &&
                                        instance.GetComponentsInChildren<Camera>(true).Length == 0 &&
                                        instance.GetComponentsInChildren<Light>(true).Length == 0 &&
                                        IsIdentity(instance.transform);
                if (!hierarchyMatches)
                    issues.Add("FBX Mesh 名称、根 TRS 或 Camera/Light 过滤不符合契约。");

                string[] materialPaths = renderers
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
                    materialSlotCount,
                    manifest.geometry.vertexCount,
                    vertexCount,
                    triangleCount,
                    expectedBounds,
                    bounds.size,
                    materialPaths);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static bool AuditMaterials(
            BlenderManifest manifest,
            IReadOnlyCollection<string> actualPaths,
            ICollection<string> issues)
        {
            string[] expectedPaths = manifest.materials
                .Select(spec => GetMaterialPath(spec.name))
                .ToArray();
            bool matches = new HashSet<string>(expectedPaths, StringComparer.Ordinal)
                .SetEquals(actualPaths);
            foreach (BlenderMaterial spec in manifest.materials)
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(
                    GetMaterialPath(spec.name));
                bool itemMatches = material != null &&
                                   material.shader != null &&
                                   material.shader.name == "Universal Render Pipeline/Lit" &&
                                   material.GetTexture("_BaseMap") == LoadTexture(spec.textures.baseColor) &&
                                   material.GetTexture("_BumpMap") == LoadTexture(spec.textures.normal) &&
                                   material.GetTexture("_MetallicGlossMap") ==
                                   LoadTexture(spec.textures.metallicSmoothness) &&
                                   material.GetTexture("_OcclusionMap") ==
                                   LoadTexture(spec.textures.occlusion) &&
                                   Vector4.Distance(material.GetColor("_BaseColor"), Color.white) <= 0.001f &&
                                   Vector2.Distance(
                                       material.GetTextureScale("_BaseMap"),
                                       new Vector2(spec.textureTiling[0], spec.textureTiling[1])) <= 0.001f &&
                                   Mathf.Approximately(material.GetFloat("_BumpScale"), spec.normalScale) &&
                                   Mathf.Approximately(
                                       material.GetFloat("_OcclusionStrength"), spec.occlusionStrength) &&
                                   Mathf.Approximately(material.GetFloat("_Metallic"), 1f) &&
                                   Mathf.Approximately(material.GetFloat("_Smoothness"), 1f) &&
                                   material.IsKeywordEnabled("_NORMALMAP") &&
                                   material.IsKeywordEnabled("_METALLICSPECGLOSSMAP") &&
                                   material.IsKeywordEnabled("_OCCLUSIONMAP") &&
                                   material.enableInstancing;
                if (itemMatches) continue;
                matches = false;
                issues.Add($"URP/Lit 材质或 Shader Keyword 不符合 PBR 通道契约：{spec.name}");
            }
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
            bool structureMatches = IsIdentity(prefab.transform) &&
                                    visual != null && IsIdentity(visual) &&
                                    prefab.GetComponentsInChildren<BoxCollider>(true).Length == 1 &&
                                    prefab.GetComponentsInChildren<MeshCollider>(true).Length == 0 &&
                                    prefab.GetComponentsInChildren<MeshFilter>(true)
                                        .Count(filter => filter.sharedMesh != null) == 3;
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
                if (instance == null)
                {
                    issues.Add("无法实例化生成的 Prefab。");
                    return false;
                }
                Bounds bounds = CalculateLocalRendererBounds(instance);
                BoxCollider collider = instance.GetComponent<BoxCollider>();
                bool boundsMatch = collider != null &&
                                   Approximately(collider.center, bounds.center, BoundsToleranceMeters) &&
                                   Approximately(collider.size, bounds.size, BoundsToleranceMeters) &&
                                   Approximately(bounds.size, modelBoundsSize, BoundsToleranceMeters);
                if (structureMatches && boundsMatch) return true;
                issues.Add("Prefab 根/Visual TRS、三 Mesh、单一 BoxCollider 或覆盖范围不符合契约。");
                return false;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static bool AuditPreviewScene(int rendererIndex, ICollection<string> issues)
        {
            SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(PreviewScenePath);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Material groundMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                NomadRenderingSpikePipeline.GroundMaterialPath);
            Material skyboxMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                NomadRenderingSpikePipeline.SkyboxMaterialPath);
            if (sceneAsset == null || prefab == null || groundMaterial == null ||
                skyboxMaterial == null)
                return false;

            Scene previousActiveScene = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(PreviewScenePath);
            bool openedHere = !scene.IsValid() || !scene.isLoaded;
            try
            {
                if (openedHere)
                    scene = EditorSceneManager.OpenScene(PreviewScenePath, OpenSceneMode.Additive);
                SceneManager.SetActiveScene(scene);
                GameObject[] roots = scene.GetRootGameObjects();
                GameObject prop = roots.SingleOrDefault(root => root.name == AssetId);
                GameObject ground = roots.SingleOrDefault(root => root.name == "PreviewGround");
                Camera[] cameras = GetSceneComponents<Camera>(scene);
                Light[] lights = GetSceneComponents<Light>(scene);
                UniversalAdditionalCameraData data = cameras.Length == 1
                    ? cameras[0].GetComponent<UniversalAdditionalCameraData>()
                    : null;
                int actualRendererIndex = -1;
                if (data != null)
                {
                    SerializedProperty property = new SerializedObject(data)
                        .FindProperty("m_RendererIndex");
                    if (property != null) actualRendererIndex = property.intValue;
                }

                Light key = lights.SingleOrDefault(light => light.name == "Key Light");
                Light fill = lights.SingleOrDefault(light => light.name == "Fill Light");
                Light rim = lights.SingleOrDefault(light => light.name == "Rim Light");

                bool matches = prop != null &&
                               PrefabUtility.GetCorrespondingObjectFromSource(prop) == prefab &&
                               IsIdentity(prop.transform) &&
                               ground?.GetComponent<MeshRenderer>()?.sharedMaterial == groundMaterial &&
                               cameras.Length == 1 && cameras[0].CompareTag("MainCamera") &&
                               cameras[0].clearFlags == CameraClearFlags.Skybox &&
                               Mathf.Approximately(cameras[0].fieldOfView, 36f) &&
                               Approximately(
                                   cameras[0].transform.position,
                                   new Vector3(2.15f, 1.68f, -2.58f),
                                   0.001f) &&
                               data != null && actualRendererIndex == rendererIndex &&
                               data.antialiasing ==
                               AntialiasingMode.SubpixelMorphologicalAntiAliasing &&
                               lights.Length == 3 && key != null &&
                               key.type == LightType.Directional &&
                               key.shadows == LightShadows.Soft &&
                               Mathf.Approximately(key.intensity, 1.70f) &&
                               fill != null && fill.type == LightType.Point &&
                               Mathf.Approximately(fill.intensity, 4.2f) &&
                               rim != null && rim.type == LightType.Spot &&
                               Mathf.Approximately(rim.intensity, 5.0f) &&
                               RenderSettings.skybox == skyboxMaterial &&
                               RenderSettings.ambientMode == AmbientMode.Skybox &&
                               Mathf.Approximately(RenderSettings.ambientIntensity, 1.05f) &&
                               Mathf.Approximately(RenderSettings.reflectionIntensity, 1.00f);
                if (!matches)
                    issues.Add("代表性场景的 Prefab、共享 3D Renderer、灯光或 Skybox 契约不成立。");
                return matches;
            }
            catch (Exception exception)
            {
                issues.Add($"读取代表性预览场景失败：{exception.Message}");
                return false;
            }
            finally
            {
                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                    SceneManager.SetActiveScene(previousActiveScene);
                if (openedHere && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
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
                    Vector3 worldPoint = bounds.center +
                                         Vector3.Scale(bounds.extents, new Vector3(x, y, z));
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
            if (textAsset == null)
                throw new InvalidOperationException($"缺少 Blender manifest：{ManifestPath}");
            BlenderManifest manifest = JsonUtility.FromJson<BlenderManifest>(textAsset.text);
            return manifest ?? throw new InvalidOperationException("无法解析 Blender manifest。");
        }

        private static void ValidateManifest(BlenderManifest manifest)
        {
            if (manifest.schemaVersion != SupportedManifestSchemaVersion ||
                manifest.harnessVersion != SupportedHarnessVersion ||
                manifest.status != "passed" || manifest.asset?.id != AssetId)
                throw new InvalidOperationException("Blender manifest 的 schema、Harness、状态或资产 ID 不受支持。");
            if (manifest.toolchain == null ||
                string.IsNullOrWhiteSpace(manifest.toolchain.sourceScriptSha256))
                throw new InvalidOperationException("manifest 缺少 Blender 源脚本哈希。");
            if (manifest.coordinateContract == null ||
                manifest.coordinateContract.authoringUnits != "meters" ||
                manifest.coordinateContract.blenderUpAxis != "+Z" ||
                manifest.coordinateContract.fbxForwardAxis != "-Z" ||
                manifest.coordinateContract.fbxUpAxis != "+Y" ||
                !manifest.coordinateContract.fbxSpaceTransformBaked ||
                manifest.coordinateContract.rootTransform != "identity")
                throw new InvalidOperationException("manifest 坐标与 FBX 轴烘焙契约不成立。");
            if (manifest.textureContract == null ||
                manifest.textureContract.width != manifest.textureContract.height ||
                manifest.textureContract.width < 256 ||
                (manifest.textureContract.width & (manifest.textureContract.width - 1)) != 0 ||
                manifest.textureContract.normalConvention != "OpenGL tangent-space (+Y)" ||
                manifest.textureContract.metallicSmoothnessPacking != "R=metallic, A=smoothness" ||
                manifest.textureContract.occlusionPacking != "G=occlusion" ||
                !manifest.textureContract.tileable ||
                !manifest.textureContract.deterministicSeededPixels)
                throw new InvalidOperationException("manifest PBR 贴图契约不成立。");
            if (manifest.materials == null || manifest.materials.Length != 3 ||
                manifest.materials.Select(material => material.name)
                    .Distinct(StringComparer.Ordinal).Count() != 3)
                throw new InvalidOperationException("manifest 必须声明三个唯一材质。");

            foreach (BlenderMaterial material in manifest.materials)
            {
                if (material.shader != "Universal Render Pipeline/Lit" ||
                    material.textureTiling == null || material.textureTiling.Length != 2 ||
                    material.normalScale < 0f || material.occlusionStrength < 0f ||
                    material.textures == null)
                    throw new InvalidOperationException($"材质声明不完整：{material.name}");
                ValidateTexture(material.textures.baseColor, "base-color", "sRGB", "RGB");
                ValidateTexture(material.textures.normal, "tangent-normal-open-gl", "linear", "RGB");
                ValidateTexture(
                    material.textures.metallicSmoothness,
                    "urp-metallic-smoothness",
                    "linear",
                    "R=metallic,A=smoothness");
                ValidateTexture(material.textures.occlusion, "ambient-occlusion", "linear", "G=occlusion");
            }

            TextureBinding[] bindings = EnumerateTextureBindings(manifest).ToArray();
            if (bindings.Length != 12 || bindings.Select(binding => binding.Texture.file)
                    .Distinct(StringComparer.Ordinal).Count() != 12)
                throw new InvalidOperationException("manifest 必须声明十二张唯一 PBR 贴图。");
            if (manifest.geometry == null ||
                manifest.geometry.meshObjectCount != 3 ||
                manifest.geometry.materialSlotCount != 3 ||
                manifest.geometry.sourcePartCount <= manifest.geometry.meshObjectCount ||
                manifest.geometry.vertexCount <= 0 || manifest.geometry.triangleCount <= 0 ||
                manifest.geometry.objects == null || manifest.geometry.objects.Length != 3 ||
                manifest.geometry.boundsMeters?.size == null ||
                manifest.geometry.boundsMeters.size.Length != 3 ||
                manifest.geometry.fbxRoundTrip == null ||
                manifest.geometry.fbxRoundTrip.meshObjectCount !=
                manifest.geometry.meshObjectCount ||
                manifest.geometry.fbxRoundTrip.materialSlotCount !=
                manifest.geometry.materialSlotCount ||
                manifest.geometry.fbxRoundTrip.vertexCount != manifest.geometry.vertexCount ||
                manifest.geometry.fbxRoundTrip.triangleCount !=
                manifest.geometry.triangleCount ||
                manifest.geometry.fbxRoundTrip.degenerateTriangleCount != 0)
                throw new InvalidOperationException("manifest 几何与按材质合并契约不成立。");

            ValidateQuality(
                manifest.geometry.quality,
                manifest.geometry.meshObjectCount,
                manifest.textureContract.width,
                "Blender 源几何");
            ValidateQuality(
                manifest.geometry.fbxRoundTrip.quality,
                manifest.geometry.meshObjectCount,
                manifest.textureContract.width,
                "FBX 回读几何");
            if (Mathf.Abs(
                    manifest.geometry.quality.uv.areaWeightedTexelDensityPxPerMeter -
                    manifest.geometry.fbxRoundTrip.quality.uv
                        .areaWeightedTexelDensityPxPerMeter) > 0.01f)
                throw new InvalidOperationException("源几何与 FBX 回读的有效平铺纹素密度不一致。");

            string[] expectedPanels =
            {
                "hero", "front", "side", "top", "wireframe", "uv-checker",
            };
            BlenderContactSheet contactSheet = manifest.visualEvidence?.contactSheet;
            if (contactSheet == null ||
                contactSheet.file != AssetId + "_contact_sheet.png" ||
                contactSheet.width != 1536 || contactSheet.height != 1024 ||
                contactSheet.columns != 3 || contactSheet.rows != 2 ||
                contactSheet.panels == null ||
                !contactSheet.panels.SequenceEqual(expectedPanels, StringComparer.Ordinal) ||
                !contactSheet.manualReviewRequired)
                throw new InvalidOperationException("manifest Contact Sheet 的布局或人工复核边界不成立。");

            if (manifest.acceptance == null ||
                !manifest.acceptance.previewRendered ||
                !manifest.acceptance.contactSheetRendered ||
                !manifest.acceptance.fbxExported ||
                !manifest.acceptance.fbxRoundTripVerified ||
                !manifest.acceptance.blendSaved ||
                !manifest.acceptance.meshMergedByMaterial ||
                !manifest.acceptance.textureSetComplete ||
                !manifest.acceptance.sourceTopologyAndUvVerified ||
                !manifest.acceptance.fbxTopologyAndUvVerified ||
                !manifest.acceptance.manualReviewStillRequired)
                throw new InvalidOperationException("manifest 的 Blender Harness 验收证据不完整。");
            if (manifest.files == null || manifest.files.Length == 0)
                throw new InvalidOperationException("manifest 缺少文件哈希证据。");
            BlenderFile contactSheetFile = FindManifestFile(manifest, contactSheet.file);
            if (contactSheetFile == null || contactSheetFile.bytes <= 0 ||
                contactSheetFile.sha256?.Length != 64)
                throw new InvalidOperationException("manifest 缺少 Contact Sheet 文件哈希证据。");
        }

        private static void ValidateQuality(
            BlenderQuality quality,
            int expectedMeshCount,
            int expectedTextureResolution,
            string scope)
        {
            BlenderTopologyQuality topology = quality?.topology;
            BlenderUvQuality uv = quality?.uv;
            bool valid = topology != null &&
                         topology.looseVertexCount == 0 &&
                         topology.looseEdgeCount == 0 &&
                         topology.boundaryEdgeCount == 0 &&
                         topology.nonManifoldEdgeCount == 0 &&
                         uv != null &&
                         uv.policy == "overlap-and-repeat-allowed" &&
                         uv.textureResolution == expectedTextureResolution &&
                         uv.allMeshesHaveActiveUv &&
                         uv.degenerateUvTriangleCount == 0 &&
                         uv.outOfUnitRangeLoopCount == 0 &&
                         uv.surfaceAreaSquareMeters > 0f &&
                         uv.uvAreaSum > 0f &&
                         uv.effectiveUvAreaSum > 0f &&
                         uv.areaWeightedTexelDensityPxPerMeter > 0f &&
                         uv.method ==
                         "sqrt(sum(uvArea*tilingArea)*resolution^2/sum(surfaceArea))" &&
                         quality.meshes != null && quality.meshes.Length == expectedMeshCount &&
                         quality.meshes.All(mesh =>
                             !string.IsNullOrWhiteSpace(mesh.@object) &&
                             !string.IsNullOrWhiteSpace(mesh.material) &&
                             mesh.tiling != null && mesh.tiling.Length == 2 &&
                             mesh.looseVertexCount == 0 &&
                             mesh.looseEdgeCount == 0 &&
                             mesh.boundaryEdgeCount == 0 &&
                             mesh.nonManifoldEdgeCount == 0 &&
                             !string.IsNullOrWhiteSpace(mesh.activeUvLayer) &&
                             mesh.surfaceAreaSquareMeters > 0f &&
                             mesh.uvAreaSum > 0f &&
                             mesh.effectiveUvAreaSum > 0f &&
                             mesh.degenerateUvTriangleCount == 0 &&
                             mesh.outOfUnitRangeLoopCount == 0 &&
                             mesh.areaWeightedTexelDensityPxPerMeter > 0f);
            if (!valid)
                throw new InvalidOperationException($"{scope}的拓扑、UV 或有效平铺纹素密度证据不成立。");
        }

        private static void ValidateTexture(
            BlenderTexture texture,
            string semantic,
            string colorSpace,
            string channels)
        {
            if (texture == null || string.IsNullOrWhiteSpace(texture.file) ||
                Path.GetFileName(texture.file) != texture.file ||
                texture.semantic != semantic || texture.colorSpace != colorSpace ||
                texture.channels != channels || texture.bytes <= 0 ||
                texture.sha256?.Length != 64)
                throw new InvalidOperationException($"PBR 贴图声明不符合约定：{texture?.file}");
        }

        private static IEnumerable<TextureBinding> EnumerateTextureBindings(
            BlenderManifest manifest)
        {
            foreach (BlenderMaterial material in manifest.materials)
            {
                yield return new TextureBinding(material.textures.baseColor, TextureRole.BaseColor);
                yield return new TextureBinding(material.textures.normal, TextureRole.Normal);
                yield return new TextureBinding(
                    material.textures.metallicSmoothness, TextureRole.MetallicSmoothness);
                yield return new TextureBinding(material.textures.occlusion, TextureRole.Occlusion);
            }
        }

        private static Texture2D LoadTexture(BlenderTexture texture)
        {
            Texture2D result = AssetDatabase.LoadAssetAtPath<Texture2D>(GetTexturePath(texture));
            return result ?? throw new InvalidOperationException($"无法加载贴图：{texture.file}");
        }

        private static string GetTexturePath(BlenderTexture texture)
        {
            if (texture == null || Path.GetFileName(texture.file) != texture.file)
                throw new InvalidOperationException($"非法贴图文件名：{texture?.file}");
            return TextureFolder + "/" + texture.file;
        }

        private static string GetMaterialPath(string materialName)
        {
            if (string.IsNullOrWhiteSpace(materialName) ||
                materialName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                materialName.Contains('/'))
                throw new InvalidOperationException($"非法材质名：{materialName}");
            return MaterialFolder + "/" + materialName + ".mat";
        }

        private static BlenderFile FindManifestFile(BlenderManifest manifest, string name)
        {
            return manifest.files.FirstOrDefault(file => file.name == name);
        }

        private static bool HashMatches(string path, string expected)
        {
            return string.Equals(ComputeSha256(path), expected, StringComparison.OrdinalIgnoreCase);
        }

        private static bool HashAndLengthMatch(string path, string expectedHash, long expectedBytes)
        {
            return new FileInfo(path).Length == expectedBytes && HashMatches(path, expectedHash);
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

        private static T[] GetSceneComponents<T>(Scene scene) where T : Component
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<T>(true))
                .ToArray();
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

        private static void WriteReport(NomadTexturedPropAssetAudit audit)
        {
            string outputFolder = Path.Combine(
                Directory.GetParent(Application.dataPath)?.FullName ??
                throw new InvalidOperationException("无法定位项目根目录。"),
                "ArtPipelineOutput", "UnityImportSpike", AssetId);
            Directory.CreateDirectory(outputFolder);
            var report = new UnityImportReport
            {
                schemaVersion = 3,
                status = audit.Passed ? "passed" : "failed",
                assetId = AssetId,
                unityVersion = Application.unityVersion,
                generatedAtUtc = DateTime.UtcNow.ToString("O"),
                modelPath = ModelPath,
                manifestPath = ManifestPath,
                prefabPath = PrefabPath,
                previewScenePath = PreviewScenePath,
                contactSheetPath = ContactSheetPath,
                meshObjectCount = audit.MeshObjectCount,
                materialSlotCount = audit.MaterialSlotCount,
                textureCount = audit.TextureCount,
                sourceVertexCount = audit.SourceVertexCount,
                vertexCount = audit.VertexCount,
                triangleCount = audit.TriangleCount,
                expectedBoundsSize = audit.ExpectedBoundsSize,
                actualBoundsSize = audit.ActualBoundsSize,
                nonManifoldEdgeCount = audit.NonManifoldEdgeCount,
                degenerateUvTriangleCount = audit.DegenerateUvTriangleCount,
                effectiveTexelDensityPxPerMeter = audit.EffectiveTexelDensityPxPerMeter,
                materialAssetPaths = audit.MaterialAssetPaths.ToArray(),
                renderingVerdict = audit.RenderingVerdict,
                issues = audit.Issues.ToArray(),
                manualReviewStillRequired = true,
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
            Occlusion,
        }

        private readonly struct TextureBinding
        {
            public TextureBinding(BlenderTexture texture, TextureRole role)
            {
                Texture = texture;
                Role = role;
            }

            public BlenderTexture Texture { get; }
            public TextureRole Role { get; }
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
            public BlenderTextureContract textureContract = new();
            public BlenderMaterial[] materials = Array.Empty<BlenderMaterial>();
            public BlenderGeometry geometry = new();
            public BlenderVisualEvidence visualEvidence = new();
            public BlenderAcceptance acceptance = new();
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
        private sealed class BlenderTextureContract
        {
            public int width;
            public int height;
            public string normalConvention = string.Empty;
            public string metallicSmoothnessPacking = string.Empty;
            public string occlusionPacking = string.Empty;
            public bool tileable;
            public bool deterministicSeededPixels;
        }

        [Serializable]
        private sealed class BlenderMaterial
        {
            public string name = string.Empty;
            public string shader = string.Empty;
            public float[] textureTiling = Array.Empty<float>();
            public float normalScale;
            public float occlusionStrength;
            public BlenderTextureSet textures = new();
        }

        [Serializable]
        private sealed class BlenderTextureSet
        {
            public BlenderTexture baseColor = new();
            public BlenderTexture normal = new();
            public BlenderTexture metallicSmoothness = new();
            public BlenderTexture occlusion = new();
        }

        [Serializable]
        private sealed class BlenderTexture
        {
            public string file = string.Empty;
            public string semantic = string.Empty;
            public string colorSpace = string.Empty;
            public string channels = string.Empty;
            public long bytes;
            public string sha256 = string.Empty;
        }

        [Serializable]
        private sealed class BlenderGeometry
        {
            public int sourcePartCount;
            public int meshObjectCount;
            public int materialSlotCount;
            public int vertexCount;
            public int triangleCount;
            public BlenderBounds boundsMeters = new();
            public string[] objects = Array.Empty<string>();
            public BlenderQuality quality = new();
            public BlenderFbxRoundTrip fbxRoundTrip = new();
        }

        [Serializable]
        private sealed class BlenderFbxRoundTrip
        {
            public int meshObjectCount;
            public int materialSlotCount;
            public int vertexCount;
            public int triangleCount;
            public int degenerateTriangleCount;
            public BlenderQuality quality = new();
        }

        [Serializable]
        private sealed class BlenderQuality
        {
            public BlenderTopologyQuality topology = new();
            public BlenderUvQuality uv = new();
            public BlenderMeshQuality[] meshes = Array.Empty<BlenderMeshQuality>();
        }

        [Serializable]
        private sealed class BlenderTopologyQuality
        {
            public int looseVertexCount;
            public int looseEdgeCount;
            public int boundaryEdgeCount;
            public int nonManifoldEdgeCount;
        }

        [Serializable]
        private sealed class BlenderUvQuality
        {
            public string policy = string.Empty;
            public int textureResolution;
            public bool allMeshesHaveActiveUv;
            public int degenerateUvTriangleCount;
            public int outOfUnitRangeLoopCount;
            public float surfaceAreaSquareMeters;
            public float uvAreaSum;
            public float effectiveUvAreaSum;
            public float areaWeightedTexelDensityPxPerMeter;
            public string method = string.Empty;
        }

        [Serializable]
        private sealed class BlenderMeshQuality
        {
            public string @object = string.Empty;
            public string material = string.Empty;
            public float[] tiling = Array.Empty<float>();
            public int looseVertexCount;
            public int looseEdgeCount;
            public int boundaryEdgeCount;
            public int nonManifoldEdgeCount;
            public string activeUvLayer = string.Empty;
            public float surfaceAreaSquareMeters;
            public float uvAreaSum;
            public float effectiveUvAreaSum;
            public int degenerateUvTriangleCount;
            public int outOfUnitRangeLoopCount;
            public float areaWeightedTexelDensityPxPerMeter;
        }

        [Serializable]
        private sealed class BlenderVisualEvidence
        {
            public BlenderContactSheet contactSheet = new();
        }

        [Serializable]
        private sealed class BlenderContactSheet
        {
            public string file = string.Empty;
            public int width;
            public int height;
            public int columns;
            public int rows;
            public string[] panels = Array.Empty<string>();
            public bool manualReviewRequired;
        }

        [Serializable]
        private sealed class BlenderAcceptance
        {
            public bool previewRendered;
            public bool contactSheetRendered;
            public bool fbxExported;
            public bool fbxRoundTripVerified;
            public bool blendSaved;
            public bool meshMergedByMaterial;
            public bool textureSetComplete;
            public bool sourceTopologyAndUvVerified;
            public bool fbxTopologyAndUvVerified;
            public bool manualReviewStillRequired;
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
            public long bytes;
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
            public string contactSheetPath = string.Empty;
            public int meshObjectCount;
            public int materialSlotCount;
            public int textureCount;
            public int sourceVertexCount;
            public int vertexCount;
            public int triangleCount;
            public Vector3 expectedBoundsSize;
            public Vector3 actualBoundsSize;
            public int nonManifoldEdgeCount;
            public int degenerateUvTriangleCount;
            public float effectiveTexelDensityPxPerMeter;
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
                int materialSlotCount,
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
                MaterialSlotCount = materialSlotCount;
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
            public int MaterialSlotCount { get; }
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

    /// <summary>纹理化 Blender 静态道具从源字节到 Unity 显示场景的只读审计结果。</summary>
    public sealed class NomadTexturedPropAssetAudit
    {
        internal NomadTexturedPropAssetAudit(
            bool sourceHashesMatch,
            bool evidenceImporterContractMatches,
            bool textureImporterContractMatches,
            bool modelImporterContractMatches,
            bool sourceHierarchyMatches,
            bool geometryMatches,
            bool materialContractMatches,
            bool prefabContractMatches,
            bool previewSceneContractMatches,
            int meshObjectCount,
            int materialSlotCount,
            int textureCount,
            int sourceVertexCount,
            int vertexCount,
            int triangleCount,
            Vector3 expectedBoundsSize,
            Vector3 actualBoundsSize,
            IReadOnlyList<string> materialAssetPaths,
            int nonManifoldEdgeCount,
            int degenerateUvTriangleCount,
            float effectiveTexelDensityPxPerMeter,
            string renderingVerdict,
            IReadOnlyList<string> issues)
        {
            SourceHashesMatch = sourceHashesMatch;
            EvidenceImporterContractMatches = evidenceImporterContractMatches;
            TextureImporterContractMatches = textureImporterContractMatches;
            ModelImporterContractMatches = modelImporterContractMatches;
            SourceHierarchyMatches = sourceHierarchyMatches;
            GeometryMatches = geometryMatches;
            MaterialContractMatches = materialContractMatches;
            PrefabContractMatches = prefabContractMatches;
            PreviewSceneContractMatches = previewSceneContractMatches;
            MeshObjectCount = meshObjectCount;
            MaterialSlotCount = materialSlotCount;
            TextureCount = textureCount;
            SourceVertexCount = sourceVertexCount;
            VertexCount = vertexCount;
            TriangleCount = triangleCount;
            ExpectedBoundsSize = expectedBoundsSize;
            ActualBoundsSize = actualBoundsSize;
            MaterialAssetPaths = materialAssetPaths;
            NonManifoldEdgeCount = nonManifoldEdgeCount;
            DegenerateUvTriangleCount = degenerateUvTriangleCount;
            EffectiveTexelDensityPxPerMeter = effectiveTexelDensityPxPerMeter;
            RenderingVerdict = renderingVerdict;
            Issues = issues;
        }

        public bool SourceHashesMatch { get; }
        /// <summary>Contact Sheet 的字节、尺寸和非运行时导入策略是否满足约定。</summary>
        public bool EvidenceImporterContractMatches { get; }
        public bool TextureImporterContractMatches { get; }
        public bool ModelImporterContractMatches { get; }
        public bool SourceHierarchyMatches { get; }
        public bool GeometryMatches { get; }
        public bool MaterialContractMatches { get; }
        public bool PrefabContractMatches { get; }
        public bool PreviewSceneContractMatches { get; }
        public int MeshObjectCount { get; }
        public int MaterialSlotCount { get; }
        public int TextureCount { get; }
        public int SourceVertexCount { get; }
        public int VertexCount { get; }
        public int TriangleCount { get; }
        public Vector3 ExpectedBoundsSize { get; }
        public Vector3 ActualBoundsSize { get; }
        public IReadOnlyList<string> MaterialAssetPaths { get; }
        /// <summary>Blender 源 Mesh 的非流形边数；当前封闭静态道具契约要求为零。</summary>
        public int NonManifoldEdgeCount { get; }
        /// <summary>有表面积但 UV 面积退化为零的来源三角形数量。</summary>
        public int DegenerateUvTriangleCount { get; }
        /// <summary>包含共享 UV 和材质平铺的面积加权有效密度，不等于唯一纹理内存预算。</summary>
        public float EffectiveTexelDensityPxPerMeter { get; }
        public string RenderingVerdict { get; }
        public IReadOnlyList<string> Issues { get; }

        public bool Passed =>
            SourceHashesMatch && EvidenceImporterContractMatches &&
            TextureImporterContractMatches &&
            ModelImporterContractMatches && SourceHierarchyMatches && GeometryMatches &&
            MaterialContractMatches && PrefabContractMatches &&
            PreviewSceneContractMatches && Issues.Count == 0;

        public string ToMultilineString()
        {
            string issues = Issues.Count == 0 ? "（无）" : string.Join("\n  - ", Issues);
            return $"[Nomad Textured Blender Import Audit] {(Passed ? "PASS" : "FAIL")}\n" +
                   $"模型：{MeshObjectCount} Mesh / {MaterialSlotCount} Slot / " +
                   $"{VertexCount} Runtime Vertex ({SourceVertexCount} Source) / " +
                   $"{TriangleCount} Triangle\n" +
                   $"贴图：{TextureCount}，Bounds：实际 {ActualBoundsSize}，预期 {ExpectedBoundsSize}\n" +
                   $"源质量：{NonManifoldEdgeCount} Non-manifold Edge / " +
                   $"{DegenerateUvTriangleCount} Degenerate UV Triangle / " +
                   $"{EffectiveTexelDensityPxPerMeter:F3} px/m 有效平铺纹素密度\n" +
                   $"Contact Sheet：{NomadTexturedPropAssetPipeline.ContactSheetPath}（仍需人工看图）\n" +
                   $"材质：{string.Join(", ", MaterialAssetPaths)}\n" +
                   $"渲染结论：{RenderingVerdict}\n" +
                   $"问题：\n  - {issues}";
        }
    }
}
