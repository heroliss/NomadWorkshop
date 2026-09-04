using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Game.NomadWorkshop.Editor
{
    /// <summary>
    /// 管理游牧工坊共享的 URP 3D 图形基线，并生成可删除的显示实验。
    /// 新建相机默认使用 3D Renderer；既有 2D 场景显式固定到 Renderer2D，避免默认值迁移后静默改画面。
    /// </summary>
    public static class NomadRenderingSpikePipeline
    {
        public const string PipelineAssetPath = "Assets/Settings/UniversalRP.asset";
        public const string Legacy2DRendererPath = "Assets/Settings/Renderer2D.asset";
        public const string RenderingRoot = "Assets/Game/NomadWorkshop/Rendering";
        public const string RendererDataPath = RenderingRoot + "/NW_UniversalRenderer3D.asset";
        public const string BaselineMaterialFolder = RenderingRoot + "/Materials";
        public const string FoundationSkyboxMaterialPath =
            BaselineMaterialFolder + "/M_NW_FoundationWastelandSkybox.mat";
        public const string FoundationVolumeProfilePath =
            RenderingRoot + "/NW_FoundationGraphicsBaseline.asset";
        public const string SpikeRoot =
            "Assets/Game/NomadWorkshop/Spikes/Rendering/Urp3D";
        public const string MaterialFolder = SpikeRoot + "/Materials";
        public const string GroundMaterialPath = MaterialFolder + "/M_NW_Urp3DGround.mat";
        public const string SkyboxMaterialPath = MaterialFolder + "/M_NW_Urp3DSkybox.mat";
        public const string PreviewFolder = SpikeRoot + "/Preview";
        public const string PreviewScenePath = PreviewFolder + "/NW_Urp3DVisualPreview.unity";

        private const string UniversalRendererTemplatePath =
            "Packages/com.unity.render-pipelines.universal/Runtime/Data/UniversalRendererData.asset";

        private static readonly string[] Legacy2DScenePathsInternal =
        {
            "Assets/Settings/Scenes/URP2DSceneTemplate.unity",
            "Assets/Game/Framework/Demo/Scenes/DemoScene.unity",
            "Assets/Game/Outpost/Scenes/OutpostBattle.unity",
        };

        /// <summary>必须显式使用 Renderer2D 的已知场景；新增 2D 场景时同步扩展此清单和测试。</summary>
        public static IReadOnlyList<string> Legacy2DScenePaths => Legacy2DScenePathsInternal;

        /// <summary>
        /// 幂等创建或更新默认 Universal 3D Renderer、兼容 2D 场景、图形资产与隔离预览，
        /// 并输出结构化审计证据。
        /// </summary>
        [MenuItem("Assets/SSFramework/游牧工坊/Rendering Spike/配置并审计 3D Renderer")]
        public static void ConfigureAndReport()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出 Play Mode，再配置 3D Renderer Spike。");

            UniversalRenderPipelineAsset pipeline = ValidateConfigurationPreconditions();
            EnsureFolder(RenderingRoot);
            EnsureFolder(BaselineMaterialFolder);
            EnsureFolder(MaterialFolder);
            EnsureFolder(PreviewFolder);

            UniversalRendererData rendererData = CreateOrUpdateRendererData();
            RendererRegistration registration = EnsureRendererRegistration(pipeline, rendererData);
            PinLegacy2DScenes(registration.Legacy2DRendererIndex);
            SetGame3DRendererAsDefault(pipeline, registration.Game3DRendererIndex);
            ConfigurePipelineGraphicsBaseline(pipeline);
            EnsureGameGraphicsAssets();
            Material groundMaterial = CreateOrUpdateGroundMaterial();
            Material skyboxMaterial = CreateOrUpdateSkyboxMaterial();
            EnsurePreviewScene(registration.Game3DRendererIndex, groundMaterial, skyboxMaterial);
            AssetDatabase.SaveAssets();

            NomadRenderingSpikeAudit audit = Audit();
            WriteReport(audit);
            if (!audit.Passed) throw new InvalidOperationException(audit.ToMultilineString());
            Debug.Log(audit.ToMultilineString());
        }

        /// <summary>读取落盘配置与场景，验证默认 3D、显式 2D 兼容和保守图形基线。</summary>
        public static NomadRenderingSpikeAudit Audit()
        {
            var issues = new List<string>();
            UniversalRenderPipelineAsset pipeline =
                AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelineAssetPath);
            UniversalRendererData rendererData =
                AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererDataPath);

            bool pipelineAssetMatches = AuditPipelineAsset(pipeline, issues);
            bool pipelineGraphicsBaselineMatches =
                AuditPipelineGraphicsBaseline(pipeline, issues);
            RendererListAudit rendererList = AuditRendererList(pipeline, rendererData, issues);
            bool legacy2DScenesPinned =
                AuditLegacy2DScenes(rendererList.Legacy2DRendererIndex, issues);
            bool rendererContractMatches = AuditRendererData(rendererData, issues);
            bool gameGraphicsAssetsMatch = AuditGameGraphicsAssets(issues);
            bool previewSceneContractMatches =
                AuditPreviewScene(rendererList.Game3DRendererIndex, issues);

            return new NomadRenderingSpikeAudit(
                pipelineAssetMatches,
                pipelineGraphicsBaselineMatches,
                rendererList.Legacy2DRendererRegistered,
                rendererList.Game3DRendererRegistered,
                rendererList.Game3DRendererIsDefault,
                legacy2DScenesPinned,
                rendererContractMatches,
                gameGraphicsAssetsMatch,
                previewSceneContractMatches,
                rendererList.Legacy2DRendererIndex,
                rendererList.Game3DRendererIndex,
                "manual_review_required：配置与场景契约已自动验证；代表性 Game View 的构图、阴影和材质可读性仍需人工检查。",
                issues);
        }

        /// <summary>
        /// 返回已经验证为唯一且为项目默认值的 3D Renderer 索引。正式 3D 场景仍显式选择它，
        /// 从而让场景资产不依赖未来可能变化的项目默认值。
        /// </summary>
        public static int GetGame3DRendererIndexOrThrow()
        {
            UniversalRenderPipelineAsset pipeline =
                AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelineAssetPath);
            UniversalRendererData rendererData =
                AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererDataPath);
            var issues = new List<string>();
            if (!AuditPipelineAsset(pipeline, issues))
                throw new InvalidOperationException(string.Join(Environment.NewLine, issues));

            RendererListAudit rendererList = AuditRendererList(pipeline, rendererData, issues);
            if (!rendererList.Legacy2DRendererRegistered ||
                !rendererList.Game3DRendererRegistered ||
                !rendererList.Game3DRendererIsDefault ||
                rendererList.Game3DRendererIndex < 0)
                throw new InvalidOperationException(string.Join(Environment.NewLine, issues));
            return rendererList.Game3DRendererIndex;
        }

        /// <summary>旧调用名的兼容入口；新代码应使用 <see cref="GetGame3DRendererIndexOrThrow"/>。</summary>
        public static int GetSecondaryRendererIndexOrThrow() => GetGame3DRendererIndexOrThrow();

        private static UniversalRendererData CreateOrUpdateRendererData()
        {
            UniversalRendererData rendererData =
                AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererDataPath);
            if (rendererData == null)
            {
                if (AssetDatabase.LoadAssetAtPath<UniversalRendererData>(
                        UniversalRendererTemplatePath) == null)
                    throw new InvalidOperationException(
                        $"找不到 URP 官方 Universal Renderer 模板：{UniversalRendererTemplatePath}");

                if (!AssetDatabase.CopyAsset(UniversalRendererTemplatePath, RendererDataPath))
                    throw new InvalidOperationException(
                        $"无法从 URP 官方模板创建 Renderer：{RendererDataPath}");

                AssetDatabase.ImportAsset(RendererDataPath, ImportAssetOptions.ForceSynchronousImport);
                rendererData =
                    AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererDataPath);
            }

            if (rendererData == null)
                throw new InvalidOperationException($"无法加载 Universal Renderer：{RendererDataPath}");

            rendererData.name = "NW_UniversalRenderer3D";
            rendererData.renderingMode = RenderingMode.Forward;
            rendererData.depthPrimingMode = DepthPrimingMode.Disabled;
            rendererData.prepassLayerMask = ~0;
            rendererData.opaqueLayerMask = ~0;
            rendererData.transparentLayerMask = ~0;
            rendererData.shadowTransparentReceive = true;
            rendererData.useNativeRenderPass = false;
            EnsureSsaoFeature(rendererData);
            rendererData.SetDirty();
            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssetIfDirty(rendererData);
            return rendererData;
        }

        private static void EnsureSsaoFeature(UniversalRendererData rendererData)
        {
            ScreenSpaceAmbientOcclusion[] features = rendererData.rendererFeatures
                .OfType<ScreenSpaceAmbientOcclusion>()
                .ToArray();
            ScreenSpaceAmbientOcclusion ssao;
            if (features.Length == 0)
            {
                ssao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
                ssao.name = "NW_SSAO_Conservative";
                AssetDatabase.AddObjectToAsset(ssao, rendererData);
                rendererData.rendererFeatures.Add(ssao);
            }
            else
            {
                ssao = features[0];
                for (var i = 1; i < features.Length; i++)
                {
                    rendererData.rendererFeatures.Remove(features[i]);
                    UnityEngine.Object.DestroyImmediate(features[i], true);
                }
            }

            ssao.name = "NW_SSAO_Conservative";
            ssao.SetActive(true);
            var serialized = new SerializedObject(ssao);
            serialized.Update();
            SerializedProperty settings = serialized.FindProperty("m_Settings") ??
                                          throw new InvalidOperationException(
                                              "当前 URP 版本无法读取 SSAO 设置。 ");
            SetRequiredBool(settings, "Downsample", true);
            SetRequiredBool(settings, "AfterOpaque", false);
            SetRequiredInt(settings, "AOMethod", 0);
            SetRequiredInt(settings, "Source", 1);
            SetRequiredInt(settings, "NormalSamples", 1);
            SetRequiredFloat(settings, "Intensity", 1.25f);
            SetRequiredFloat(settings, "DirectLightingStrength", 0.2f);
            SetRequiredFloat(settings, "Radius", 0.04f);
            SetRequiredInt(settings, "Samples", 1);
            SetRequiredInt(settings, "BlurQuality", 1);
            SetRequiredFloat(settings, "Falloff", 80f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(ssao);

            // Renderer Feature 既是子资产引用，也是 local fileID 映射。通过 Editor API 同步两份数据，
            // 避免只改 List 后重开项目出现 Missing Renderer Feature。
            AssetDatabase.SaveAssets();
            var rendererSerialized = new SerializedObject(rendererData);
            rendererSerialized.Update();
            SerializedProperty featureMap =
                rendererSerialized.FindProperty("m_RendererFeatureMap") ??
                throw new InvalidOperationException("当前 URP 版本无法读取 Renderer Feature Map。 ");
            featureMap.arraySize = rendererData.rendererFeatures.Count;
            for (var i = 0; i < rendererData.rendererFeatures.Count; i++)
            {
                ScriptableRendererFeature feature = rendererData.rendererFeatures[i];
                if (feature == null ||
                    !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                        feature,
                        out string _,
                        out long localId))
                    throw new InvalidOperationException(
                        $"Renderer Feature 第 {i} 项没有稳定的子资产 local fileID。 ");
                featureMap.GetArrayElementAtIndex(i).longValue = localId;
            }
            rendererSerialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static UniversalRenderPipelineAsset ValidateConfigurationPreconditions()
        {
            Scene loadedPreview = SceneManager.GetSceneByPath(PreviewScenePath);
            if (loadedPreview.IsValid() && loadedPreview.isLoaded)
                throw new InvalidOperationException(
                    "3D 预览场景当前已加载；请先切换到其他场景，避免覆盖正在查看的 Scene 实例。");

            foreach (string scenePath in Legacy2DScenePathsInternal)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
                    throw new InvalidOperationException($"缺少受管 2D 场景：{scenePath}");
                Scene loadedScene = SceneManager.GetSceneByPath(scenePath);
                if (loadedScene.IsValid() && loadedScene.isLoaded && loadedScene.isDirty)
                    throw new InvalidOperationException(
                        $"受管 2D 场景 '{scenePath}' 当前有未保存修改；" +
                        "配置工具不会代替使用者决定是否保存，请先处理后重试。 ");
            }

            UniversalRenderPipelineAsset pipeline =
                AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelineAssetPath);
            if (pipeline == null)
                throw new InvalidOperationException($"无法加载 URP Asset：{PipelineAssetPath}");

            var serialized = new SerializedObject(pipeline);
            serialized.Update();
            SerializedProperty renderers = serialized.FindProperty("m_RendererDataList");
            SerializedProperty defaultIndex = serialized.FindProperty("m_DefaultRendererIndex");
            if (renderers == null || defaultIndex == null ||
                defaultIndex.intValue < 0 || defaultIndex.intValue >= renderers.arraySize)
                throw new InvalidOperationException("当前 URP Asset 的 Renderer 列表或默认索引无效。");

            UnityEngine.Object defaultRenderer =
                renderers.GetArrayElementAtIndex(defaultIndex.intValue).objectReferenceValue;
            string defaultRendererPath = AssetDatabase.GetAssetPath(defaultRenderer);
            if (defaultRendererPath != Legacy2DRendererPath &&
                defaultRendererPath != RendererDataPath)
                throw new InvalidOperationException(
                    $"当前默认 Renderer '{defaultRendererPath}' 既不是受管 2D 也不是受管 3D Renderer；" +
                    "为避免覆盖未知配置，本次在写入前停止。 ");
            return pipeline;
        }

        private static RendererRegistration EnsureRendererRegistration(
            UniversalRenderPipelineAsset pipeline,
            UniversalRendererData rendererData)
        {
            var serialized = new SerializedObject(pipeline);
            serialized.Update();
            SerializedProperty renderers = serialized.FindProperty("m_RendererDataList");
            SerializedProperty defaultIndex = serialized.FindProperty("m_DefaultRendererIndex");
            if (renderers == null || defaultIndex == null)
                throw new InvalidOperationException("当前 URP 版本无法读取 Renderer 列表序列化字段。");
            if (defaultIndex.intValue < 0 || defaultIndex.intValue >= renderers.arraySize)
                throw new InvalidOperationException("当前 URP Asset 的默认 Renderer 索引无效。");

            int legacy2DRendererIndex = -1;
            int legacy2DRendererCount = 0;
            int game3DRendererIndex = -1;
            int game3DRendererCount = 0;
            for (var i = 0; i < renderers.arraySize; i++)
            {
                UnityEngine.Object candidate =
                    renderers.GetArrayElementAtIndex(i).objectReferenceValue;
                if (AssetDatabase.GetAssetPath(candidate) == Legacy2DRendererPath)
                {
                    legacy2DRendererCount++;
                    legacy2DRendererIndex = i;
                }
                if (candidate != rendererData) continue;
                game3DRendererCount++;
                game3DRendererIndex = i;
            }

            if (legacy2DRendererCount != 1 || legacy2DRendererIndex != 0)
                throw new InvalidOperationException(
                    $"Renderer2D 必须且只能注册一次并保持 index 0；实际数量 {legacy2DRendererCount}，" +
                    $"index {legacy2DRendererIndex}。 ");
            if (game3DRendererCount > 1)
                throw new InvalidOperationException(
                    "游牧工坊 3D Renderer 在共享列表中重复出现；为避免改变相机整数索引，本次停止。 ");

            if (game3DRendererCount == 0)
            {
                Undo.RecordObject(pipeline, "注册游牧工坊 3D Renderer");
                game3DRendererIndex = renderers.arraySize;
                renderers.arraySize++;
                renderers.GetArrayElementAtIndex(game3DRendererIndex).objectReferenceValue =
                    rendererData;
                serialized.ApplyModifiedProperties();
            }
            EditorUtility.SetDirty(pipeline);
            AssetDatabase.SaveAssetIfDirty(pipeline);
            return new RendererRegistration(legacy2DRendererIndex, game3DRendererIndex);
        }

        private static void SetGame3DRendererAsDefault(
            UniversalRenderPipelineAsset pipeline,
            int game3DRendererIndex)
        {
            var serialized = new SerializedObject(pipeline);
            serialized.Update();
            SerializedProperty defaultIndex = serialized.FindProperty("m_DefaultRendererIndex") ??
                                              throw new InvalidOperationException(
                                                  "当前 URP 版本无法读取默认 Renderer 索引。 ");
            if (defaultIndex.intValue == game3DRendererIndex) return;
            Undo.RecordObject(pipeline, "将 Universal 3D Renderer 设为项目默认");
            defaultIndex.intValue = game3DRendererIndex;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(pipeline);
            AssetDatabase.SaveAssetIfDirty(pipeline);
        }

        private static void ConfigurePipelineGraphicsBaseline(UniversalRenderPipelineAsset pipeline)
        {
            Undo.RecordObject(pipeline, "配置游牧工坊 URP 图形基线");
            var serialized = new SerializedObject(pipeline);
            serialized.Update();
            SetRequiredBool(serialized, "m_SupportsHDR", true);
            SetRequiredInt(serialized, "m_HDRColorBufferPrecision", 1);
            SetRequiredInt(serialized, "m_MSAA", 4);
            SetRequiredBool(serialized, "m_ReflectionProbeBlending", true);
            SetRequiredBool(serialized, "m_ReflectionProbeBoxProjection", true);
            SetRequiredBool(serialized, "m_ReflectionProbeAtlas", true);
            SetRequiredInt(serialized, "m_ShadowCascadeCount", 2);
            SetRequiredFloat(serialized, "m_Cascade2Split", 0.35f);
            SetRequiredBool(serialized, "m_SoftShadowsSupported", true);
            SetRequiredInt(serialized, "m_SoftShadowQuality", 1);
            SetRequiredInt(serialized, "m_ColorGradingMode", 1);
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(pipeline);
            AssetDatabase.SaveAssetIfDirty(pipeline);
        }

        /// <summary>
        /// 创建正式 Foundation 会复用的天空盒与后处理 Profile；不会创建或覆盖任何场景。
        /// </summary>
        public static void EnsureGameGraphicsAssets()
        {
            EnsureFolder(RenderingRoot);
            EnsureFolder(BaselineMaterialFolder);
            CreateOrUpdateFoundationSkyboxMaterial();
            CreateOrUpdateFoundationVolumeProfile();
            AssetDatabase.SaveAssets();
        }

        private static Material CreateOrUpdateFoundationSkyboxMaterial()
        {
            Shader shader = Shader.Find("Skybox/Procedural") ??
                            throw new InvalidOperationException(
                                "当前项目找不到 Skybox/Procedural Shader。 ");
            Material material =
                AssetDatabase.LoadAssetAtPath<Material>(FoundationSkyboxMaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "M_NW_FoundationWastelandSkybox" };
                AssetDatabase.CreateAsset(material, FoundationSkyboxMaterialPath);
            }
            else
            {
                material.shader = shader;
            }

            material.SetFloat("_SunDisk", 2f);
            material.SetFloat("_SunSize", 0.028f);
            material.SetFloat("_SunSizeConvergence", 5f);
            material.SetFloat("_AtmosphereThickness", 0.68f);
            material.SetColor("_SkyTint", new Color(0.24f, 0.34f, 0.45f, 1f));
            material.SetColor("_GroundColor", new Color(0.22f, 0.12f, 0.065f, 1f));
            material.SetFloat("_Exposure", 0.86f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static VolumeProfile CreateOrUpdateFoundationVolumeProfile()
        {
            VolumeProfile profile =
                AssetDatabase.LoadAssetAtPath<VolumeProfile>(FoundationVolumeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.name = "NW_FoundationGraphicsBaseline";
                AssetDatabase.CreateAsset(profile, FoundationVolumeProfilePath);
            }

            Tonemapping tonemapping = GetOrAddVolumeComponent<Tonemapping>(profile);
            tonemapping.SetAllOverridesTo(false);
            tonemapping.active = true;
            tonemapping.mode.Override(TonemappingMode.ACES);

            Bloom bloom = GetOrAddVolumeComponent<Bloom>(profile);
            bloom.SetAllOverridesTo(false);
            bloom.active = true;
            bloom.threshold.Override(1.05f);
            bloom.intensity.Override(0.12f);
            bloom.scatter.Override(0.62f);
            bloom.highQualityFiltering.Override(false);

            ColorAdjustments color = GetOrAddVolumeComponent<ColorAdjustments>(profile);
            color.SetAllOverridesTo(false);
            color.active = true;
            color.postExposure.Override(0.5f);
            color.contrast.Override(3f);
            color.saturation.Override(-3f);

            EditorUtility.SetDirty(tonemapping);
            EditorUtility.SetDirty(bloom);
            EditorUtility.SetDirty(color);
            profile.Reset();
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static T GetOrAddVolumeComponent<T>(VolumeProfile profile)
            where T : VolumeComponent
        {
            if (profile.TryGet(out T component)) return component;
            component = profile.Add<T>(false);
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        private static void PinLegacy2DScenes(int legacy2DRendererIndex)
        {
            if (legacy2DRendererIndex < 0)
                throw new InvalidOperationException("没有有效的 Renderer2D 索引，不能迁移 2D 场景。 ");

            Scene previousActiveScene = SceneManager.GetActiveScene();
            try
            {
                foreach (string scenePath in Legacy2DScenePathsInternal)
                    PinLegacy2DScene(scenePath, legacy2DRendererIndex);
            }
            finally
            {
                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                    SceneManager.SetActiveScene(previousActiveScene);
            }
        }

        private static void PinLegacy2DScene(string scenePath, int rendererIndex)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
                throw new InvalidOperationException($"缺少受管 2D 场景：{scenePath}");

            Scene scene = SceneManager.GetSceneByPath(scenePath);
            bool openedHere = !scene.IsValid() || !scene.isLoaded;
            try
            {
                if (openedHere)
                    scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

                Camera[] cameras = GetSceneComponents<Camera>(scene);
                if (cameras.Length == 0)
                    throw new InvalidOperationException($"受管 2D 场景没有 Camera：{scenePath}");

                bool changed = false;
                foreach (Camera camera in cameras)
                {
                    UniversalAdditionalCameraData cameraData =
                        camera.GetUniversalAdditionalCameraData();
                    var serialized = new SerializedObject(cameraData);
                    SerializedProperty currentIndex = serialized.FindProperty("m_RendererIndex") ??
                                                      throw new InvalidOperationException(
                                                          $"无法读取 2D Camera Renderer 索引：{scenePath}");
                    if (currentIndex.intValue == rendererIndex) continue;
                    Undo.RecordObject(cameraData, "显式固定 2D Camera Renderer");
                    cameraData.SetRenderer(rendererIndex);
                    EditorUtility.SetDirty(cameraData);
                    changed = true;
                }

                if (!changed) return;
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene, scenePath))
                    throw new InvalidOperationException($"保存受管 2D 场景失败：{scenePath}");
            }
            finally
            {
                if (openedHere && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static Material CreateOrUpdateGroundMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                throw new InvalidOperationException("当前项目找不到 Universal Render Pipeline/Lit Shader。");

            Material material = AssetDatabase.LoadAssetAtPath<Material>(GroundMaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "M_NW_Urp3DGround" };
                AssetDatabase.CreateAsset(material, GroundMaterialPath);
            }
            else
            {
                material.shader = shader;
            }

            material.SetColor("_BaseColor", new Color(0.105f, 0.075f, 0.045f, 1f));
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.16f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }

        private static Material CreateOrUpdateSkyboxMaterial()
        {
            Shader shader = Shader.Find("Skybox/Procedural");
            if (shader == null)
                throw new InvalidOperationException("当前项目找不到 Skybox/Procedural Shader。");

            Material material = AssetDatabase.LoadAssetAtPath<Material>(SkyboxMaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "M_NW_Urp3DSkybox" };
                AssetDatabase.CreateAsset(material, SkyboxMaterialPath);
            }
            else
            {
                material.shader = shader;
            }

            material.SetFloat("_SunDisk", 2f);
            material.SetFloat("_SunSize", 0.035f);
            material.SetFloat("_SunSizeConvergence", 5f);
            material.SetFloat("_AtmosphereThickness", 0.72f);
            material.SetColor("_SkyTint", new Color(0.22f, 0.34f, 0.52f, 1f));
            material.SetColor("_GroundColor", new Color(0.24f, 0.13f, 0.065f, 1f));
            material.SetFloat("_Exposure", 0.78f);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }

        private static void EnsurePreviewScene(
            int rendererIndex,
            Material groundMaterial,
            Material skyboxMaterial)
        {
            var drift = new List<string>();
            if (AuditPreviewScene(rendererIndex, drift)) return;
            CreateOrReplacePreviewScene(rendererIndex, groundMaterial, skyboxMaterial);
        }

        private static void CreateOrReplacePreviewScene(
            int rendererIndex,
            Material groundMaterial,
            Material skyboxMaterial)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                NomadPropAssetPipeline.PrefabPath);
            if (prefab == null)
                throw new InvalidOperationException(
                    $"请先生成 Blender Import Spike Prefab：{NomadPropAssetPipeline.PrefabPath}");

            Scene previousActiveScene = SceneManager.GetActiveScene();
            Scene previewScene =
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(previewScene);

                GameObject instance = PrefabUtility.InstantiatePrefab(prefab, previewScene) as GameObject;
                if (instance == null) throw new InvalidOperationException("无法实例化储物箱 Prefab。");
                instance.name = NomadPropAssetPipeline.AssetId;
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ground.name = "PreviewGround";
                SceneManager.MoveGameObjectToScene(ground, previewScene);
                ground.transform.SetPositionAndRotation(
                    new Vector3(0f, -0.08f, 0.15f), Quaternion.identity);
                ground.transform.localScale = new Vector3(6.6f, 0.16f, 5.2f);
                ground.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;
                UnityEngine.Object.DestroyImmediate(ground.GetComponent<Collider>());

                CreateCamera(previewScene, rendererIndex);
                Light key = CreateLighting(previewScene);

                RenderSettings.ambientMode = AmbientMode.Skybox;
                RenderSettings.ambientIntensity = 0.82f;
                RenderSettings.reflectionIntensity = 0.92f;
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
                RenderSettings.skybox = skyboxMaterial;
                RenderSettings.sun = key;
                RenderSettings.fog = false;

                if (!EditorSceneManager.SaveScene(previewScene, PreviewScenePath))
                    throw new InvalidOperationException($"保存 3D 预览场景失败：{PreviewScenePath}");
            }
            finally
            {
                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                    SceneManager.SetActiveScene(previousActiveScene);
                EditorSceneManager.CloseScene(previewScene, true);
            }
        }

        private static void CreateCamera(Scene scene, int rendererIndex)
        {
            var cameraObject = new GameObject("Main Camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.fieldOfView = 38f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 100f;
            camera.allowHDR = true;
            cameraObject.transform.position = new Vector3(2.75f, 1.72f, -3.2f);
            cameraObject.transform.LookAt(new Vector3(0f, 0.43f, 0f));

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

        private static Light CreateLighting(Scene scene)
        {
            var keyObject = new GameObject("Key Light");
            SceneManager.MoveGameObjectToScene(keyObject, scene);
            Light key = keyObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = new Color(1f, 0.82f, 0.62f, 1f);
            key.intensity = 1.28f;
            key.shadows = LightShadows.Hard;
            key.shadowStrength = 0.82f;
            keyObject.transform.rotation = Quaternion.Euler(44f, -32f, 0f);

            var fillObject = new GameObject("Fill Light");
            SceneManager.MoveGameObjectToScene(fillObject, scene);
            Light fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.color = new Color(0.36f, 0.58f, 1f, 1f);
            fill.intensity = 2.4f;
            fill.range = 6.5f;
            fill.shadows = LightShadows.None;
            fillObject.transform.position = new Vector3(-2.1f, 1.55f, -1.7f);

            var rimObject = new GameObject("Rim Light");
            SceneManager.MoveGameObjectToScene(rimObject, scene);
            Light rim = rimObject.AddComponent<Light>();
            rim.type = LightType.Spot;
            rim.color = new Color(1f, 0.38f, 0.16f, 1f);
            rim.intensity = 4.2f;
            rim.range = 7f;
            rim.spotAngle = 58f;
            rim.innerSpotAngle = 34f;
            rim.shadows = LightShadows.None;
            rimObject.transform.position = new Vector3(1.85f, 2.1f, 2.15f);
            rimObject.transform.LookAt(new Vector3(0f, 0.45f, 0f));
            return key;
        }

        private static bool AuditPipelineAsset(
            UniversalRenderPipelineAsset pipeline,
            ICollection<string> issues)
        {
            if (pipeline == null)
            {
                issues.Add($"缺少 URP Asset：{PipelineAssetPath}");
                return false;
            }

            string currentPath = AssetDatabase.GetAssetPath(QualitySettings.renderPipeline);
            if (currentPath == PipelineAssetPath) return true;
            issues.Add($"当前 Quality 使用的 SRP Asset 是 {currentPath}，预期 {PipelineAssetPath}。");
            return false;
        }

        private static bool AuditPipelineGraphicsBaseline(
            UniversalRenderPipelineAsset pipeline,
            ICollection<string> issues)
        {
            if (pipeline == null) return false;
            var mismatches = new List<string>();
            var serialized = new SerializedObject(pipeline);
            serialized.Update();
            CheckRequiredBool(serialized, "m_SupportsHDR", true, "内部 HDR 未开启", mismatches);
            CheckRequiredInt(
                serialized,
                "m_HDRColorBufferPrecision",
                1,
                "HDR Color Buffer 不是 64-bit",
                mismatches);
            CheckRequiredInt(serialized, "m_MSAA", 4, "MSAA 不是 4x", mismatches);
            CheckRequiredBool(
                serialized,
                "m_ReflectionProbeBlending",
                true,
                "Reflection Probe Blending 未开启",
                mismatches);
            CheckRequiredBool(
                serialized,
                "m_ReflectionProbeBoxProjection",
                true,
                "Reflection Probe Box Projection 未开启",
                mismatches);
            CheckRequiredBool(
                serialized,
                "m_ReflectionProbeAtlas",
                true,
                "Reflection Probe Atlas 未开启",
                mismatches);
            CheckRequiredInt(
                serialized,
                "m_ShadowCascadeCount",
                2,
                "主光阴影不是 2 Cascades",
                mismatches);
            CheckRequiredFloat(
                serialized,
                "m_Cascade2Split",
                0.35f,
                "2 Cascades 分界不是 0.35",
                mismatches);
            CheckRequiredBool(
                serialized,
                "m_SoftShadowsSupported",
                true,
                "Soft Shadows 未开启",
                mismatches);
            CheckRequiredInt(
                serialized,
                "m_SoftShadowQuality",
                1,
                "Soft Shadow Quality 不是 Medium",
                mismatches);
            CheckRequiredInt(
                serialized,
                "m_ColorGradingMode",
                1,
                "Color Grading 不是 HDR 模式",
                mismatches);

            foreach (string mismatch in mismatches) issues.Add(mismatch + "。 ");
            return mismatches.Count == 0;
        }

        private static RendererListAudit AuditRendererList(
            UniversalRenderPipelineAsset pipeline,
            UniversalRendererData rendererData,
            ICollection<string> issues)
        {
            if (pipeline == null)
                return new RendererListAudit(false, false, false, -1, -1);

            var serialized = new SerializedObject(pipeline);
            serialized.Update();
            SerializedProperty renderers = serialized.FindProperty("m_RendererDataList");
            SerializedProperty defaultIndex = serialized.FindProperty("m_DefaultRendererIndex");
            if (renderers == null || defaultIndex == null)
            {
                issues.Add("无法读取 URP Renderer 列表与默认索引。");
                return new RendererListAudit(false, false, false, -1, -1);
            }

            int defaultRendererIndex = defaultIndex.intValue;
            bool validDefaultIndex =
                defaultRendererIndex >= 0 && defaultRendererIndex < renderers.arraySize;
            int legacy2DIndex = -1;
            int legacy2DCount = 0;
            int game3DIndex = -1;
            int game3DCount = 0;
            for (int i = 0; i < renderers.arraySize; i++)
            {
                UnityEngine.Object candidate =
                    renderers.GetArrayElementAtIndex(i).objectReferenceValue;
                if (AssetDatabase.GetAssetPath(candidate) == Legacy2DRendererPath)
                {
                    legacy2DCount++;
                    legacy2DIndex = i;
                }
                if (candidate != rendererData) continue;
                game3DCount++;
                game3DIndex = i;
            }

            bool legacy2DRegistered = legacy2DCount == 1 && legacy2DIndex == 0;
            if (!legacy2DRegistered)
                issues.Add(
                    $"Renderer2D 应唯一注册在 index 0；实际数量 {legacy2DCount}，index {legacy2DIndex}。 ");
            bool game3DRegistered = rendererData != null && game3DCount == 1 &&
                                    game3DIndex != legacy2DIndex;
            if (!game3DRegistered)
                issues.Add($"3D Renderer 应唯一注册；实际数量 {game3DCount}。 ");
            bool game3DIsDefault = validDefaultIndex && game3DRegistered &&
                                   defaultRendererIndex == game3DIndex;
            if (!game3DIsDefault)
                issues.Add(
                    $"3D Renderer 应为项目默认值；当前 default index {defaultRendererIndex}，" +
                    $"3D index {game3DIndex}。 ");

            return new RendererListAudit(
                legacy2DRegistered,
                game3DRegistered,
                game3DIsDefault,
                legacy2DIndex,
                game3DIndex);
        }

        private static bool AuditLegacy2DScenes(
            int expectedRendererIndex,
            ICollection<string> issues)
        {
            if (expectedRendererIndex < 0)
            {
                issues.Add("没有有效的 Renderer2D 索引，无法审计 2D 场景。 ");
                return false;
            }

            int issueCountBeforeAudit = issues.Count;
            Scene previousActiveScene = SceneManager.GetActiveScene();
            foreach (string scenePath in Legacy2DScenePathsInternal)
            {
                Scene scene = default;
                bool openedHere = false;
                try
                {
                    if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
                    {
                        issues.Add($"缺少受管 2D 场景：{scenePath}");
                        continue;
                    }

                    scene = SceneManager.GetSceneByPath(scenePath);
                    openedHere = !scene.IsValid() || !scene.isLoaded;
                    if (openedHere)
                        scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

                    Camera[] cameras = GetSceneComponents<Camera>(scene);
                    if (cameras.Length == 0)
                    {
                        issues.Add($"受管 2D 场景没有 Camera：{scenePath}");
                        continue;
                    }

                    foreach (Camera camera in cameras)
                    {
                        UniversalAdditionalCameraData cameraData =
                            camera.GetComponent<UniversalAdditionalCameraData>();
                        SerializedProperty rendererIndex = cameraData == null
                            ? null
                            : new SerializedObject(cameraData).FindProperty("m_RendererIndex");
                        if (rendererIndex == null ||
                            rendererIndex.intValue != expectedRendererIndex)
                            issues.Add(
                                $"2D Camera '{scenePath}:{camera.name}' 未显式选择 Renderer2D " +
                                $"index {expectedRendererIndex}。 ");
                    }
                }
                catch (Exception exception)
                {
                    issues.Add($"读取受管 2D 场景失败 '{scenePath}'：{exception.Message}");
                }
                finally
                {
                    if (openedHere && scene.IsValid() && scene.isLoaded)
                        EditorSceneManager.CloseScene(scene, true);
                }
            }

            if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                SceneManager.SetActiveScene(previousActiveScene);
            return issues.Count == issueCountBeforeAudit;
        }

        private static bool AuditRendererData(
            UniversalRendererData rendererData,
            ICollection<string> issues)
        {
            if (rendererData == null)
            {
                issues.Add($"缺少 Universal Renderer：{RendererDataPath}");
                return false;
            }

            var mismatches = new List<string>();
            if (rendererData.renderingMode != RenderingMode.Forward)
                mismatches.Add("Rendering Mode 不是 Forward");
            if (rendererData.depthPrimingMode != DepthPrimingMode.Disabled)
                mismatches.Add("Depth Priming 未保持 Disabled");
            if (rendererData.prepassLayerMask.value != ~0 ||
                rendererData.opaqueLayerMask.value != ~0 ||
                rendererData.transparentLayerMask.value != ~0)
                mismatches.Add("Renderer Layer Mask 未覆盖全部层");
            if (!rendererData.shadowTransparentReceive)
                mismatches.Add("透明物体阴影接收被关闭");
            if (rendererData.useNativeRenderPass)
                mismatches.Add("实验期 Native Render Pass 应保持关闭");
            if (rendererData.rendererFeatures.Any(feature => feature == null))
                mismatches.Add("Renderer Feature 列表包含 Missing 引用");

            ScreenSpaceAmbientOcclusion[] ssaoFeatures = rendererData.rendererFeatures
                .OfType<ScreenSpaceAmbientOcclusion>()
                .ToArray();
            if (ssaoFeatures.Length != 1)
            {
                mismatches.Add($"保守 SSAO 应唯一存在，实际 {ssaoFeatures.Length}");
            }
            else
            {
                ScreenSpaceAmbientOcclusion ssao = ssaoFeatures[0];
                if (!ssao.isActive) mismatches.Add("SSAO Renderer Feature 未激活");
                var serialized = new SerializedObject(ssao);
                SerializedProperty settings = serialized.FindProperty("m_Settings");
                if (settings == null)
                {
                    mismatches.Add("无法读取 SSAO 设置");
                }
                else
                {
                    CheckRequiredBool(settings, "Downsample", true, "SSAO 未降采样", mismatches);
                    CheckRequiredBool(settings, "AfterOpaque", false, "SSAO 在 Opaque 后执行", mismatches);
                    CheckRequiredInt(settings, "Source", 1, "SSAO 未使用 Depth Normals", mismatches);
                    CheckRequiredFloat(settings, "Intensity", 1.25f, "SSAO Intensity 漂移", mismatches);
                    CheckRequiredFloat(settings, "Radius", 0.04f, "SSAO Radius 漂移", mismatches);
                    CheckRequiredInt(settings, "Samples", 1, "SSAO Samples 不是 Medium", mismatches);
                    CheckRequiredInt(settings, "BlurQuality", 1, "SSAO Blur 不是 Medium", mismatches);
                }
            }

            var rendererSerialized = new SerializedObject(rendererData);
            SerializedProperty featureMap = rendererSerialized.FindProperty("m_RendererFeatureMap");
            if (featureMap == null ||
                featureMap.arraySize != rendererData.rendererFeatures.Count)
            {
                mismatches.Add("Renderer Feature local fileID Map 与列表长度不一致");
            }
            else
            {
                for (var i = 0; i < rendererData.rendererFeatures.Count; i++)
                {
                    ScriptableRendererFeature feature = rendererData.rendererFeatures[i];
                    if (feature == null ||
                        !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                            feature,
                            out string _,
                            out long localId) ||
                        featureMap.GetArrayElementAtIndex(i).longValue != localId)
                    {
                        mismatches.Add($"Renderer Feature 第 {i} 项 local fileID Map 漂移");
                        break;
                    }
                }
            }

            foreach (string mismatch in mismatches) issues.Add(mismatch + "。");
            return mismatches.Count == 0;
        }

        private static bool AuditGameGraphicsAssets(ICollection<string> issues)
        {
            var mismatches = new List<string>();
            Material skybox =
                AssetDatabase.LoadAssetAtPath<Material>(FoundationSkyboxMaterialPath);
            if (skybox == null || skybox.shader == null ||
                skybox.shader.name != "Skybox/Procedural")
                mismatches.Add("Foundation 程序化天空盒资产缺失或 Shader 不匹配");

            VolumeProfile profile =
                AssetDatabase.LoadAssetAtPath<VolumeProfile>(FoundationVolumeProfilePath);
            if (profile == null)
            {
                mismatches.Add("Foundation Global Volume Profile 缺失");
            }
            else
            {
                if (!profile.TryGet(out Tonemapping tone) || !tone.active ||
                    !tone.mode.overrideState || tone.mode.value != TonemappingMode.ACES)
                    mismatches.Add("Foundation Tonemapping 不是启用的 ACES");
                if (!profile.TryGet(out Bloom bloom) || !bloom.active ||
                    !bloom.intensity.overrideState ||
                    !Mathf.Approximately(bloom.intensity.value, 0.12f))
                    mismatches.Add("Foundation Bloom 基线缺失或漂移");
                if (!profile.TryGet(out ColorAdjustments color) || !color.active ||
                    !color.contrast.overrideState ||
                    !Mathf.Approximately(color.contrast.value, 3f) ||
                    !color.postExposure.overrideState ||
                    !Mathf.Approximately(color.postExposure.value, 0.5f))
                    mismatches.Add("Foundation Color Adjustments 基线缺失或漂移");
            }

            foreach (string mismatch in mismatches) issues.Add(mismatch + "。 ");
            return mismatches.Count == 0;
        }

        private static bool AuditPreviewScene(int expectedRendererIndex, ICollection<string> issues)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(PreviewScenePath) == null)
            {
                issues.Add($"缺少 3D 预览场景：{PreviewScenePath}");
                return false;
            }
            if (expectedRendererIndex < 0)
            {
                issues.Add("没有有效的 3D Renderer 索引，无法审计预览相机。");
                return false;
            }

            int issueCountBeforeSceneAudit = issues.Count;
            Scene previousActiveScene = SceneManager.GetActiveScene();
            Scene previewScene = SceneManager.GetSceneByPath(PreviewScenePath);
            bool openedHere = !previewScene.IsValid() || !previewScene.isLoaded;
            try
            {
                if (openedHere)
                    previewScene = EditorSceneManager.OpenScene(
                        PreviewScenePath, OpenSceneMode.Additive);
                SceneManager.SetActiveScene(previewScene);

                Camera[] cameras = GetSceneComponents<Camera>(previewScene);
                if (cameras.Length != 1)
                {
                    issues.Add($"3D 预览场景应有且仅有一个 Camera，实际 {cameras.Length}。");
                }
                else
                {
                    AuditCamera(cameras[0], expectedRendererIndex, issues);
                }

                GameObject crate = previewScene.GetRootGameObjects()
                    .SingleOrDefault(root => root.name == NomadPropAssetPipeline.AssetId);
                if (crate == null ||
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(crate) !=
                    NomadPropAssetPipeline.PrefabPath)
                    issues.Add("预览场景没有引用预期的 Blender 储物箱 Prefab。");

                GameObject ground = previewScene.GetRootGameObjects()
                    .SingleOrDefault(root => root.name == "PreviewGround");
                if (ground == null || ground.GetComponent<MeshRenderer>()?.sharedMaterial !=
                    AssetDatabase.LoadAssetAtPath<Material>(GroundMaterialPath))
                    issues.Add("预览地面或其材质不符合约定。");

                AuditLights(previewScene, issues);
                if (RenderSettings.skybox !=
                    AssetDatabase.LoadAssetAtPath<Material>(SkyboxMaterialPath))
                    issues.Add("预览场景没有使用约定的程序化 Skybox。");
                if (RenderSettings.ambientMode != AmbientMode.Skybox)
                    issues.Add("预览场景的环境光模式不是 Skybox。");
            }
            catch (Exception exception)
            {
                issues.Add($"读取 3D 预览场景失败：{exception.Message}");
            }
            finally
            {
                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                    SceneManager.SetActiveScene(previousActiveScene);
                if (openedHere && previewScene.IsValid() && previewScene.isLoaded)
                    EditorSceneManager.CloseScene(previewScene, true);
            }

            return issues.Count == issueCountBeforeSceneAudit;
        }

        private static void AuditCamera(
            Camera camera,
            int expectedRendererIndex,
            ICollection<string> issues)
        {
            UniversalAdditionalCameraData cameraData =
                camera.GetComponent<UniversalAdditionalCameraData>();
            if (!camera.CompareTag("MainCamera") || camera.clearFlags != CameraClearFlags.Skybox ||
                !camera.allowHDR)
                issues.Add("预览 Camera 的 MainCamera、Skybox 或 HDR 契约不成立。");
            if (cameraData == null)
            {
                issues.Add("预览 Camera 缺少 UniversalAdditionalCameraData。");
                return;
            }

            var serialized = new SerializedObject(cameraData);
            SerializedProperty rendererIndex = serialized.FindProperty("m_RendererIndex");
            if (rendererIndex == null || rendererIndex.intValue != expectedRendererIndex)
                issues.Add(
                    $"预览 Camera 未显式选择 3D Renderer 索引 {expectedRendererIndex}。");
            if (cameraData.renderPostProcessing ||
                cameraData.antialiasing !=
                AntialiasingMode.SubpixelMorphologicalAntiAliasing ||
                cameraData.antialiasingQuality != AntialiasingQuality.High)
                issues.Add("预览 Camera 的后处理或 SMAA 契约不成立。");
            if (!cameraData.stopNaN || !cameraData.dithering || cameraData.allowXRRendering)
                issues.Add("预览 Camera 的稳定性或 XR 选项不符合实验约定。");
        }

        private static void AuditLights(Scene scene, ICollection<string> issues)
        {
            Light[] lights = GetSceneComponents<Light>(scene);
            Light key = lights.SingleOrDefault(light => light.name == "Key Light");
            Light fill = lights.SingleOrDefault(light => light.name == "Fill Light");
            Light rim = lights.SingleOrDefault(light => light.name == "Rim Light");
            if (lights.Length != 3 || key == null || key.type != LightType.Directional ||
                key.shadows == LightShadows.None)
                issues.Add("预览场景缺少唯一主方向光或主光阴影。");
            if (fill == null || fill.type != LightType.Point || fill.shadows != LightShadows.None)
                issues.Add("预览场景的冷色补光契约不成立。");
            if (rim == null || rim.type != LightType.Spot || rim.shadows != LightShadows.None)
                issues.Add("预览场景的暖色轮廓光契约不成立。");
            if (RenderSettings.sun != key)
                issues.Add("程序化天空没有绑定主方向光作为 Sun。");
        }

        private static T[] GetSceneComponents<T>(Scene scene) where T : Component
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<T>(true))
                .ToArray();
        }

        private static void SetRequiredBool(
            SerializedObject serialized,
            string propertyName,
            bool value) =>
            RequireProperty(serialized, propertyName).boolValue = value;

        private static void SetRequiredInt(
            SerializedObject serialized,
            string propertyName,
            int value) =>
            RequireProperty(serialized, propertyName).intValue = value;

        private static void SetRequiredFloat(
            SerializedObject serialized,
            string propertyName,
            float value) =>
            RequireProperty(serialized, propertyName).floatValue = value;

        private static void SetRequiredBool(
            SerializedProperty parent,
            string propertyName,
            bool value) =>
            RequireProperty(parent, propertyName).boolValue = value;

        private static void SetRequiredInt(
            SerializedProperty parent,
            string propertyName,
            int value) =>
            RequireProperty(parent, propertyName).intValue = value;

        private static void SetRequiredFloat(
            SerializedProperty parent,
            string propertyName,
            float value) =>
            RequireProperty(parent, propertyName).floatValue = value;

        private static SerializedProperty RequireProperty(
            SerializedObject serialized,
            string propertyName) =>
            serialized.FindProperty(propertyName) ??
            throw new InvalidOperationException($"缺少序列化字段：{propertyName}");

        private static SerializedProperty RequireProperty(
            SerializedProperty parent,
            string propertyName) =>
            parent.FindPropertyRelative(propertyName) ??
            throw new InvalidOperationException(
                $"缺少序列化字段：{parent.propertyPath}.{propertyName}");

        private static void CheckRequiredBool(
            SerializedObject serialized,
            string propertyName,
            bool expected,
            string mismatch,
            ICollection<string> mismatches)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null || property.boolValue != expected) mismatches.Add(mismatch);
        }

        private static void CheckRequiredInt(
            SerializedObject serialized,
            string propertyName,
            int expected,
            string mismatch,
            ICollection<string> mismatches)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null || property.intValue != expected) mismatches.Add(mismatch);
        }

        private static void CheckRequiredFloat(
            SerializedObject serialized,
            string propertyName,
            float expected,
            string mismatch,
            ICollection<string> mismatches)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null || !Mathf.Approximately(property.floatValue, expected))
                mismatches.Add(mismatch);
        }

        private static void CheckRequiredBool(
            SerializedProperty parent,
            string propertyName,
            bool expected,
            string mismatch,
            ICollection<string> mismatches)
        {
            SerializedProperty property = parent.FindPropertyRelative(propertyName);
            if (property == null || property.boolValue != expected) mismatches.Add(mismatch);
        }

        private static void CheckRequiredInt(
            SerializedProperty parent,
            string propertyName,
            int expected,
            string mismatch,
            ICollection<string> mismatches)
        {
            SerializedProperty property = parent.FindPropertyRelative(propertyName);
            if (property == null || property.intValue != expected) mismatches.Add(mismatch);
        }

        private static void CheckRequiredFloat(
            SerializedProperty parent,
            string propertyName,
            float expected,
            string mismatch,
            ICollection<string> mismatches)
        {
            SerializedProperty property = parent.FindPropertyRelative(propertyName);
            if (property == null || !Mathf.Approximately(property.floatValue, expected))
                mismatches.Add(mismatch);
        }

        private static void WriteReport(NomadRenderingSpikeAudit audit)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ??
                                 throw new InvalidOperationException("无法定位项目根目录。");
            string outputFolder = Path.Combine(
                projectRoot, "ArtPipelineOutput", "UnityRenderingSpike", "Urp3D");
            Directory.CreateDirectory(outputFolder);
            var report = new RenderingSpikeReport
            {
                schemaVersion = 2,
                status = audit.Passed ? "passed" : "failed",
                unityVersion = Application.unityVersion,
                generatedAtUtc = DateTime.UtcNow.ToString("O"),
                pipelineAssetPath = PipelineAssetPath,
                legacy2DRendererPath = Legacy2DRendererPath,
                game3DRendererPath = RendererDataPath,
                previewScenePath = PreviewScenePath,
                legacy2DRendererIndex = audit.Legacy2DRendererIndex,
                game3DRendererIndex = audit.Game3DRendererIndex,
                renderingVerdict = audit.RenderingVerdict,
                issues = audit.Issues.ToArray(),
                manualReviewStillRequired = true,
            };
            File.WriteAllText(
                Path.Combine(outputFolder, "report.json"),
                JsonUtility.ToJson(report, true) + Environment.NewLine);
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

        private readonly struct RendererListAudit
        {
            public RendererListAudit(
                bool legacy2DRendererRegistered,
                bool game3DRendererRegistered,
                bool game3DRendererIsDefault,
                int legacy2DRendererIndex,
                int game3DRendererIndex)
            {
                Legacy2DRendererRegistered = legacy2DRendererRegistered;
                Game3DRendererRegistered = game3DRendererRegistered;
                Game3DRendererIsDefault = game3DRendererIsDefault;
                Legacy2DRendererIndex = legacy2DRendererIndex;
                Game3DRendererIndex = game3DRendererIndex;
            }

            public bool Legacy2DRendererRegistered { get; }
            public bool Game3DRendererRegistered { get; }
            public bool Game3DRendererIsDefault { get; }
            public int Legacy2DRendererIndex { get; }
            public int Game3DRendererIndex { get; }
        }

        private readonly struct RendererRegistration
        {
            public RendererRegistration(int legacy2DRendererIndex, int game3DRendererIndex)
            {
                Legacy2DRendererIndex = legacy2DRendererIndex;
                Game3DRendererIndex = game3DRendererIndex;
            }

            public int Legacy2DRendererIndex { get; }
            public int Game3DRendererIndex { get; }
        }

        [Serializable]
        private sealed class RenderingSpikeReport
        {
            public int schemaVersion;
            public string status = string.Empty;
            public string unityVersion = string.Empty;
            public string generatedAtUtc = string.Empty;
            public string pipelineAssetPath = string.Empty;
            public string legacy2DRendererPath = string.Empty;
            public string game3DRendererPath = string.Empty;
            public string previewScenePath = string.Empty;
            public int legacy2DRendererIndex;
            public int game3DRendererIndex;
            public string renderingVerdict = string.Empty;
            public string[] issues = Array.Empty<string>();
            public bool manualReviewStillRequired;
        }
    }

    /// <summary>URP 3D Renderer Spike 的可测试结构化审计结果。</summary>
    public sealed class NomadRenderingSpikeAudit
    {
        internal NomadRenderingSpikeAudit(
            bool pipelineAssetMatches,
            bool pipelineGraphicsBaselineMatches,
            bool legacy2DRendererRegistered,
            bool game3DRendererRegistered,
            bool game3DRendererIsDefault,
            bool legacy2DScenesPinned,
            bool rendererContractMatches,
            bool gameGraphicsAssetsMatch,
            bool previewSceneContractMatches,
            int legacy2DRendererIndex,
            int game3DRendererIndex,
            string renderingVerdict,
            IReadOnlyList<string> issues)
        {
            PipelineAssetMatches = pipelineAssetMatches;
            PipelineGraphicsBaselineMatches = pipelineGraphicsBaselineMatches;
            Legacy2DRendererRegistered = legacy2DRendererRegistered;
            Game3DRendererRegistered = game3DRendererRegistered;
            Game3DRendererIsDefault = game3DRendererIsDefault;
            Legacy2DScenesPinned = legacy2DScenesPinned;
            RendererContractMatches = rendererContractMatches;
            GameGraphicsAssetsMatch = gameGraphicsAssetsMatch;
            PreviewSceneContractMatches = previewSceneContractMatches;
            Legacy2DRendererIndex = legacy2DRendererIndex;
            Game3DRendererIndex = game3DRendererIndex;
            RenderingVerdict = renderingVerdict;
            Issues = issues;
        }

        public bool PipelineAssetMatches { get; }
        public bool PipelineGraphicsBaselineMatches { get; }
        public bool Legacy2DRendererRegistered { get; }
        public bool Game3DRendererRegistered { get; }
        public bool Game3DRendererIsDefault { get; }
        public bool Legacy2DScenesPinned { get; }
        public bool RendererContractMatches { get; }
        public bool GameGraphicsAssetsMatch { get; }
        public bool PreviewSceneContractMatches { get; }
        public int Legacy2DRendererIndex { get; }
        public int Game3DRendererIndex { get; }
        public string RenderingVerdict { get; }
        public IReadOnlyList<string> Issues { get; }

        public bool Passed =>
            PipelineAssetMatches && PipelineGraphicsBaselineMatches &&
            Legacy2DRendererRegistered && Game3DRendererRegistered &&
            Game3DRendererIsDefault && Legacy2DScenesPinned &&
            RendererContractMatches && GameGraphicsAssetsMatch &&
            PreviewSceneContractMatches && Issues.Count == 0;

        public string ToMultilineString()
        {
            var lines = new List<string>
            {
                $"[NomadRenderingSpike] {(Passed ? "PASS" : "FAIL")}",
                $"- 当前 Quality 使用目标 URP Asset：{PipelineAssetMatches}",
                $"- URP HDR / 阴影 / 反射基线：{PipelineGraphicsBaselineMatches}",
                $"- Renderer2D 唯一保留（index {Legacy2DRendererIndex}）：{Legacy2DRendererRegistered}",
                $"- 3D Renderer 唯一注册（index {Game3DRendererIndex}）：{Game3DRendererRegistered}",
                $"- 3D Renderer 是项目默认值：{Game3DRendererIsDefault}",
                $"- 既有 2D 场景显式固定 Renderer2D：{Legacy2DScenesPinned}",
                $"- Universal Renderer 配置契约：{RendererContractMatches}",
                $"- 天空盒与后处理资产契约：{GameGraphicsAssetsMatch}",
                $"- 隔离预览场景契约：{PreviewSceneContractMatches}",
                $"- 视觉结论：{RenderingVerdict}",
            };
            foreach (string issue in Issues) lines.Add($"  ! {issue}");
            return string.Join(Environment.NewLine, lines);
        }
    }
}
