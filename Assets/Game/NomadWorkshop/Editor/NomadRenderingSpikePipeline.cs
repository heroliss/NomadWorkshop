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
    /// 为游牧工坊建立可删除的 URP 3D Renderer 显示实验。
    /// 它只给隔离预览相机选择次级 Renderer，不改变项目既有 Renderer2D 默认值。
    /// </summary>
    public static class NomadRenderingSpikePipeline
    {
        public const string PipelineAssetPath = "Assets/Settings/UniversalRP.asset";
        public const string DefaultRendererPath = "Assets/Settings/Renderer2D.asset";
        public const string SpikeRoot =
            "Assets/Game/NomadWorkshop/Spikes/Rendering/Urp3D";
        public const string RendererDataPath = SpikeRoot + "/NW_UniversalRenderer3D.asset";
        public const string MaterialFolder = SpikeRoot + "/Materials";
        public const string GroundMaterialPath = MaterialFolder + "/M_NW_Urp3DGround.mat";
        public const string SkyboxMaterialPath = MaterialFolder + "/M_NW_Urp3DSkybox.mat";
        public const string PreviewFolder = SpikeRoot + "/Preview";
        public const string PreviewScenePath = PreviewFolder + "/NW_Urp3DVisualPreview.unity";

        private const string UniversalRendererTemplatePath =
            "Packages/com.unity.render-pipelines.universal/Runtime/Data/UniversalRendererData.asset";

        /// <summary>
        /// 幂等创建或更新次级 Universal Renderer、材质与隔离预览场景，并输出结构化审计证据。
        /// </summary>
        [MenuItem("Assets/SSFramework/游牧工坊/Rendering Spike/配置并审计 3D Renderer")]
        public static void ConfigureAndReport()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出 Play Mode，再配置 3D Renderer Spike。");

            UniversalRenderPipelineAsset pipeline = ValidateConfigurationPreconditions();
            EnsureFolder(MaterialFolder);
            EnsureFolder(PreviewFolder);

            UniversalRendererData rendererData = CreateOrUpdateRendererData();
            int rendererIndex =
                AttachSecondaryRendererAndPreserveDefault(pipeline, rendererData);
            Material groundMaterial = CreateOrUpdateGroundMaterial();
            Material skyboxMaterial = CreateOrUpdateSkyboxMaterial();
            EnsurePreviewScene(rendererIndex, groundMaterial, skyboxMaterial);
            AssetDatabase.SaveAssets();

            NomadRenderingSpikeAudit audit = Audit();
            WriteReport(audit);
            if (!audit.Passed) throw new InvalidOperationException(audit.ToMultilineString());
            Debug.Log(audit.ToMultilineString());
        }

        /// <summary>读取落盘配置与场景，验证次级 3D Renderer 没有取代全局默认 Renderer2D。</summary>
        public static NomadRenderingSpikeAudit Audit()
        {
            var issues = new List<string>();
            UniversalRenderPipelineAsset pipeline =
                AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelineAssetPath);
            UniversalRendererData rendererData =
                AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererDataPath);

            bool pipelineAssetMatches = AuditPipelineAsset(pipeline, issues);
            RendererListAudit rendererList = AuditRendererList(pipeline, rendererData, issues);
            bool rendererContractMatches = AuditRendererData(rendererData, issues);
            bool previewSceneContractMatches =
                AuditPreviewScene(rendererList.SecondaryRendererIndex, issues);

            return new NomadRenderingSpikeAudit(
                pipelineAssetMatches,
                rendererList.DefaultRendererPreserved,
                rendererList.SecondaryRendererRegistered,
                rendererContractMatches,
                previewSceneContractMatches,
                rendererList.DefaultRendererIndex,
                rendererList.SecondaryRendererIndex,
                "manual_review_required：配置与场景契约已自动验证；代表性 Game View 的构图、阴影和材质可读性仍需人工检查。",
                issues);
        }

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
            rendererData.rendererFeatures.Clear();
            rendererData.SetDirty();
            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssetIfDirty(rendererData);
            return rendererData;
        }

        private static UniversalRenderPipelineAsset ValidateConfigurationPreconditions()
        {
            Scene loadedPreview = SceneManager.GetSceneByPath(PreviewScenePath);
            if (loadedPreview.IsValid() && loadedPreview.isLoaded)
                throw new InvalidOperationException(
                    "3D 预览场景当前已加载；请先切换到其他场景，避免覆盖正在查看的 Scene 实例。");

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
            if (AssetDatabase.GetAssetPath(defaultRenderer) != DefaultRendererPath)
                throw new InvalidOperationException(
                    $"默认 Renderer 不是预期的 {DefaultRendererPath}；为避免留下部分产物，本次在写入前停止。");
            return pipeline;
        }

        private static int AttachSecondaryRendererAndPreserveDefault(
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

            UnityEngine.Object originalDefault =
                renderers.GetArrayElementAtIndex(defaultIndex.intValue).objectReferenceValue;
            if (AssetDatabase.GetAssetPath(originalDefault) != DefaultRendererPath)
                throw new InvalidOperationException(
                    $"默认 Renderer 不是预期的 {DefaultRendererPath}；为避免覆盖未知配置，本次停止。");

            int secondaryRendererIndex = -1;
            int occurrenceCount = 0;
            for (int i = 0; i < renderers.arraySize; i++)
            {
                UnityEngine.Object candidate =
                    renderers.GetArrayElementAtIndex(i).objectReferenceValue;
                if (candidate != rendererData) continue;
                occurrenceCount++;
                secondaryRendererIndex = i;
            }

            if (occurrenceCount > 1)
                throw new InvalidOperationException(
                    "次级 3D Renderer 在共享列表中重复出现；为避免改变其他相机使用的整数索引，本次停止。");
            if (occurrenceCount == 1) return secondaryRendererIndex;

            Undo.RecordObject(pipeline, "配置游牧工坊次级 3D Renderer");
            secondaryRendererIndex = renderers.arraySize;
            renderers.arraySize++;
            renderers.GetArrayElementAtIndex(secondaryRendererIndex).objectReferenceValue =
                rendererData;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(pipeline);
            AssetDatabase.SaveAssetIfDirty(pipeline);
            return secondaryRendererIndex;
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

        private static RendererListAudit AuditRendererList(
            UniversalRenderPipelineAsset pipeline,
            UniversalRendererData rendererData,
            ICollection<string> issues)
        {
            if (pipeline == null)
                return new RendererListAudit(false, false, -1, -1);

            var serialized = new SerializedObject(pipeline);
            serialized.Update();
            SerializedProperty renderers = serialized.FindProperty("m_RendererDataList");
            SerializedProperty defaultIndex = serialized.FindProperty("m_DefaultRendererIndex");
            if (renderers == null || defaultIndex == null)
            {
                issues.Add("无法读取 URP Renderer 列表与默认索引。");
                return new RendererListAudit(false, false, -1, -1);
            }

            int defaultRendererIndex = defaultIndex.intValue;
            bool validDefaultIndex =
                defaultRendererIndex >= 0 && defaultRendererIndex < renderers.arraySize;
            UnityEngine.Object defaultRenderer = validDefaultIndex
                ? renderers.GetArrayElementAtIndex(defaultRendererIndex).objectReferenceValue
                : null;
            bool defaultPreserved = validDefaultIndex &&
                                    AssetDatabase.GetAssetPath(defaultRenderer) ==
                                    DefaultRendererPath;
            if (!defaultPreserved)
                issues.Add($"默认 Renderer 未保持为 {DefaultRendererPath}。");

            int secondaryIndex = -1;
            int secondaryCount = 0;
            for (int i = 0; i < renderers.arraySize; i++)
            {
                if (renderers.GetArrayElementAtIndex(i).objectReferenceValue != rendererData)
                    continue;
                secondaryCount++;
                secondaryIndex = i;
            }

            bool registered = rendererData != null && secondaryCount == 1 &&
                              secondaryIndex != defaultRendererIndex;
            if (!registered)
                issues.Add(
                    $"次级 3D Renderer 应注册且仅注册一次，并且不能成为默认值；实际数量 {secondaryCount}。");

            return new RendererListAudit(
                defaultPreserved, registered, defaultRendererIndex, secondaryIndex);
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
            if (rendererData.rendererFeatures.Count != 0)
                mismatches.Add("实验 Renderer 不应预装 Renderer Feature");

            foreach (string mismatch in mismatches) issues.Add(mismatch + "。");
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
                issues.Add("没有有效的次级 Renderer 索引，无法审计预览相机。");
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
                    $"预览 Camera 未选择次级 Renderer 索引 {expectedRendererIndex}。");
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

        private static void WriteReport(NomadRenderingSpikeAudit audit)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ??
                                 throw new InvalidOperationException("无法定位项目根目录。");
            string outputFolder = Path.Combine(
                projectRoot, "ArtPipelineOutput", "UnityRenderingSpike", "Urp3D");
            Directory.CreateDirectory(outputFolder);
            var report = new RenderingSpikeReport
            {
                schemaVersion = 1,
                status = audit.Passed ? "passed" : "failed",
                unityVersion = Application.unityVersion,
                generatedAtUtc = DateTime.UtcNow.ToString("O"),
                pipelineAssetPath = PipelineAssetPath,
                defaultRendererPath = DefaultRendererPath,
                secondaryRendererPath = RendererDataPath,
                previewScenePath = PreviewScenePath,
                defaultRendererIndex = audit.DefaultRendererIndex,
                secondaryRendererIndex = audit.SecondaryRendererIndex,
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
                bool defaultRendererPreserved,
                bool secondaryRendererRegistered,
                int defaultRendererIndex,
                int secondaryRendererIndex)
            {
                DefaultRendererPreserved = defaultRendererPreserved;
                SecondaryRendererRegistered = secondaryRendererRegistered;
                DefaultRendererIndex = defaultRendererIndex;
                SecondaryRendererIndex = secondaryRendererIndex;
            }

            public bool DefaultRendererPreserved { get; }
            public bool SecondaryRendererRegistered { get; }
            public int DefaultRendererIndex { get; }
            public int SecondaryRendererIndex { get; }
        }

        [Serializable]
        private sealed class RenderingSpikeReport
        {
            public int schemaVersion;
            public string status = string.Empty;
            public string unityVersion = string.Empty;
            public string generatedAtUtc = string.Empty;
            public string pipelineAssetPath = string.Empty;
            public string defaultRendererPath = string.Empty;
            public string secondaryRendererPath = string.Empty;
            public string previewScenePath = string.Empty;
            public int defaultRendererIndex;
            public int secondaryRendererIndex;
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
            bool defaultRendererPreserved,
            bool secondaryRendererRegistered,
            bool rendererContractMatches,
            bool previewSceneContractMatches,
            int defaultRendererIndex,
            int secondaryRendererIndex,
            string renderingVerdict,
            IReadOnlyList<string> issues)
        {
            PipelineAssetMatches = pipelineAssetMatches;
            DefaultRendererPreserved = defaultRendererPreserved;
            SecondaryRendererRegistered = secondaryRendererRegistered;
            RendererContractMatches = rendererContractMatches;
            PreviewSceneContractMatches = previewSceneContractMatches;
            DefaultRendererIndex = defaultRendererIndex;
            SecondaryRendererIndex = secondaryRendererIndex;
            RenderingVerdict = renderingVerdict;
            Issues = issues;
        }

        public bool PipelineAssetMatches { get; }
        public bool DefaultRendererPreserved { get; }
        public bool SecondaryRendererRegistered { get; }
        public bool RendererContractMatches { get; }
        public bool PreviewSceneContractMatches { get; }
        public int DefaultRendererIndex { get; }
        public int SecondaryRendererIndex { get; }
        public string RenderingVerdict { get; }
        public IReadOnlyList<string> Issues { get; }

        public bool Passed =>
            PipelineAssetMatches && DefaultRendererPreserved && SecondaryRendererRegistered &&
            RendererContractMatches && PreviewSceneContractMatches && Issues.Count == 0;

        public string ToMultilineString()
        {
            var lines = new List<string>
            {
                $"[NomadRenderingSpike] {(Passed ? "PASS" : "FAIL")}",
                $"- 当前 Quality 使用目标 URP Asset：{PipelineAssetMatches}",
                $"- 默认 Renderer2D 保持不变（index {DefaultRendererIndex}）：{DefaultRendererPreserved}",
                $"- 次级 3D Renderer 唯一注册（index {SecondaryRendererIndex}）：{SecondaryRendererRegistered}",
                $"- Universal Renderer 配置契约：{RendererContractMatches}",
                $"- 隔离预览场景契约：{PreviewSceneContractMatches}",
                $"- 视觉结论：{RenderingVerdict}",
            };
            foreach (string issue in Issues) lines.Add($"  ! {issue}");
            return string.Join(Environment.NewLine, lines);
        }
    }
}
