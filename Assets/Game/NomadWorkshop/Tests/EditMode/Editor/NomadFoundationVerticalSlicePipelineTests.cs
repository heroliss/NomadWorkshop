using Game.NomadWorkshop.Foundation;
using Game.NomadWorkshop.Navigation;
using Game.NomadWorkshop.Simulation;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Game.NomadWorkshop.Editor.Tests
{
    public sealed class NomadFoundationVerticalSlicePipelineTests
    {
        [Test]
        public void GeneratedScene_UsesFrameworkMonoLayersAndSharedDefinitions()
        {
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<DeckLayoutDefinition>(
                NomadFoundationVerticalSlicePipeline.DeckLayoutPath));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<NomadWorldItemDefinition>(
                NomadFoundationVerticalSlicePipeline.WaterCanItemPath));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<NomadWorldItemDefinition>(
                NomadFoundationVerticalSlicePipeline.DrinkingCupItemPath));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<NomadWorldItemDefinition>(
                NomadFoundationVerticalSlicePipeline.WaterValveKitItemPath));
            NomadConstructionBlueprintSampleDefinition blueprintSample =
                AssetDatabase.LoadAssetAtPath<NomadConstructionBlueprintSampleDefinition>(
                    NomadFoundationVerticalSlicePipeline.BlueprintSamplePath);
            Assert.IsNotNull(blueprintSample);
            Assert.That(blueprintSample.BlueprintId, Is.EqualTo("blueprint-kitchen-n2b"));
            Assert.That(blueprintSample.FacilityDefinition, Is.Not.Null);
            Assert.That(blueprintSample.FacilityDefinition.Id, Is.EqualTo("field-kitchen"));
            Assert.That(blueprintSample.Site.Kind, Is.EqualTo(NomadConstructionSiteKind.VehicleMounted));
            Assert.That(blueprintSample.Site.SiteId, Is.EqualTo("vehicle-01"));
            Assert.That(blueprintSample.RequiredWorkUnits, Is.EqualTo(12));
            Assert.That(blueprintSample.RequiredMaterials.Count, Is.EqualTo(1));
            Assert.That(blueprintSample.RequiredMaterials[0].ResourceId, Is.EqualTo("steel"));
            Assert.That(blueprintSample.RequiredMaterials[0].Amount, Is.EqualTo(2));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SceneAsset>(
                NomadFoundationVerticalSlicePipeline.ScenePath));

            Scene scene = SceneManager.GetSceneByPath(NomadFoundationVerticalSlicePipeline.ScenePath);
            bool openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
            {
                scene = EditorSceneManager.OpenScene(
                    NomadFoundationVerticalSlicePipeline.ScenePath,
                    OpenSceneMode.Additive);
            }
            Scene previousActiveScene = SceneManager.GetActiveScene();
            bool changedActiveScene = previousActiveScene != scene;
            if (changedActiveScene)
            {
                Assert.That(
                    SceneManager.SetActiveScene(scene),
                    Is.True,
                    "RenderSettings 属于活动场景；测试必须先切换上下文再读取。 ");
            }
            try
            {
                NomadFoundationContext context = FindOne<NomadFoundationContext>(scene);
                NomadFoundationModel model = FindOne<NomadFoundationModel>(scene);
                NomadFoundationSystem system = FindOne<NomadFoundationSystem>(scene);
                NomadFoundationWorldView worldView = FindOne<NomadFoundationWorldView>(scene);
                NomadFoundationDebugView debugView = FindOne<NomadFoundationDebugView>(scene);
                DeckNavigationUtility navigation = FindOne<DeckNavigationUtility>(scene);
                NavMeshSurface surface = FindOne<NavMeshSurface>(scene);
                Camera worldCamera = FindOne<Camera>(scene);
                Volume globalVolume = FindOne<Volume>(scene);
                ReflectionProbe reflectionProbe = FindOne<ReflectionProbe>(scene);

                Assert.IsNotNull(context);
                Assert.IsNotNull(model);
                Assert.IsNotNull(system);
                Assert.IsNotNull(worldView);
                Assert.IsNotNull(debugView);
                Assert.IsNotNull(navigation);
                Assert.IsNotNull(surface);
                Assert.IsNotNull(worldCamera);
                Assert.IsNotNull(globalVolume);
                Assert.IsNotNull(reflectionProbe);
                Assert.That(model.transform.IsChildOf(context.transform), Is.True);
                Assert.That(system.transform.IsChildOf(context.transform), Is.True);
                Assert.That(worldView.transform.IsChildOf(context.transform), Is.True);
                Assert.That(debugView.transform.IsChildOf(context.transform), Is.True);
                Assert.That(navigation.transform.IsChildOf(context.transform), Is.True);
                Assert.That(surface.transform, Is.SameAs(navigation.transform));
                Assert.IsNotNull(
                    navigation.transform.Find("Nav Source · Walkable Deck")
                        ?.GetComponent<BoxCollider>(),
                    "正式切片应有独立、不受表现 Mesh 影响的 NavMesh 物理构建输入。");
                Assert.IsNotNull(
                    navigation.transform.Find("Runtime Facility Navigation Obstacles"),
                    "设施候选障碍应有可整体停用的运行期所有权根。");

                var systemSerialized = new SerializedObject(system);
                var viewSerialized = new SerializedObject(worldView);
                Assert.AreSame(
                    systemSerialized.FindProperty("deckLayout").objectReferenceValue,
                    viewSerialized.FindProperty("deckLayout").objectReferenceValue,
                    "逻辑与表现必须引用同一份甲板布局，避免边界与可选网格漂移。");
                Assert.That(systemSerialized.FindProperty("facilityDefinitions").arraySize, Is.EqualTo(6));
                Assert.That(viewSerialized.FindProperty("facilityDefinitions").arraySize, Is.EqualTo(6));
                Assert.That(systemSerialized.FindProperty("worldItemDefinitions").arraySize, Is.EqualTo(3));
                Assert.That(viewSerialized.FindProperty("worldItemDefinitions").arraySize, Is.EqualTo(3));
                Assert.That(
                    systemSerialized.FindProperty("worldSeed").intValue,
                    Is.EqualTo(1729),
                    "可重建场景必须显式保存确定性世界 Seed，不能依赖新增字段的隐式反序列化默认值。 ");
                Assert.That(
                    systemSerialized.FindProperty("residentStartLocalPosition").vector3Value.y,
                    Is.EqualTo(0f).Within(0.0001f),
                    "居民根节点代表脚底，生成场景不得再把胶囊半高写入逻辑位置。");
                for (var i = 0; i < 5; i++)
                {
                    Assert.AreSame(
                        systemSerialized.FindProperty("facilityDefinitions")
                            .GetArrayElementAtIndex(i).objectReferenceValue,
                        viewSerialized.FindProperty("facilityDefinitions")
                            .GetArrayElementAtIndex(i).objectReferenceValue,
                        $"逻辑与表现的设施定义第 {i} 项必须是同一资产。 ");

                    var definition = (NomadFacilityDefinition)systemSerialized
                        .FindProperty("facilityDefinitions")
                        .GetArrayElementAtIndex(i)
                        .objectReferenceValue;
                    Assert.That(definition.CreateFootprint().Parts.Count, Is.GreaterThan(0));
                    Assert.That(definition.InteractionGroups.Count, Is.GreaterThan(0));
                }
                for (var i = 0; i < 3; i++)
                {
                    Object systemItem = systemSerialized.FindProperty("worldItemDefinitions")
                        .GetArrayElementAtIndex(i).objectReferenceValue;
                    Object viewItem = viewSerialized.FindProperty("worldItemDefinitions")
                        .GetArrayElementAtIndex(i).objectReferenceValue;
                    Assert.AreSame(
                        systemItem,
                        viewItem,
                        $"逻辑与表现的世界物品定义第 {i} 项必须是同一资产。 ");
                    var itemDefinition = (NomadWorldItemDefinition)systemItem;
                    Assert.DoesNotThrow(itemDefinition.ValidateOrThrow);
                    Assert.That(itemDefinition.CreateFootprint().DefinitionId, Is.EqualTo(itemDefinition.Id));
                }

                var fieldKitchen = AssetDatabase.LoadAssetAtPath<NomadFacilityDefinition>(
                    NomadFoundationVerticalSlicePipeline.FieldKitchenPath);
                Assert.That(fieldKitchen, Is.Not.Null);
                Assert.That(
                    fieldKitchen.TryGetPlacementRegion("countertop-center", out var countertop),
                    Is.True,
                    "野战厨房必须从定义资产声明台面，而不是由 WorldView 硬编码杯子位置。 ");
                Assert.That(countertop.SupportHeightMeters, Is.EqualTo(0.97f).Within(0.001f));
                Assert.That(countertop.AcceptedCategories, Does.Contain("cup"));

                var observationEasel = AssetDatabase.LoadAssetAtPath<NomadFacilityDefinition>(
                    NomadFoundationVerticalSlicePipeline.ObservationEaselPath);
                Assert.That(observationEasel, Is.Not.Null);
                Assert.That(observationEasel.Function, Is.EqualTo(NomadFacilityFunction.HobbyPoint));
                Assert.That(observationEasel.Buildable, Is.True);

                var waterTank = AssetDatabase.LoadAssetAtPath<NomadFacilityDefinition>(
                    NomadFoundationVerticalSlicePipeline.VehicleWaterTankPath);
                Assert.That(waterTank, Is.Not.Null);
                Assert.That(waterTank.InteractionGroups.Count, Is.EqualTo(4),
                    "拿取水罐需要独立的侧面工作组，不能复用正面的装水位。");
                Assert.That(
                    waterTank.TryGetPlacementRegion("maintenance-tray", out var maintenanceTray),
                    Is.True,
                    "实体维修包需要由水箱定义声明稳定托盘，而不是由维修逻辑猜一个位置。 ");
                Assert.That(maintenanceTray.AcceptedCategories, Does.Contain("spare-part"));

                Light keyLight = worldView.transform.Find("Key Light")?.GetComponent<Light>();
                Light fillLight = worldView.transform.Find("Fill Light")?.GetComponent<Light>();
                Assert.That(keyLight, Is.Not.Null);
                Assert.That(fillLight, Is.Not.Null, "深色灰盒需要独立补光，不能只靠纯色背景提供反射。 ");
                Assert.That(keyLight.type, Is.EqualTo(LightType.Directional));
                Assert.That(fillLight.type, Is.EqualTo(LightType.Directional));
                Assert.That(fillLight.shadows, Is.EqualTo(LightShadows.None));
                Assert.That(
                    viewSerialized.FindProperty("fillLight").objectReferenceValue,
                    Is.SameAs(fillLight));
                Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Skybox));
                Assert.That(RenderSettings.sun, Is.SameAs(keyLight));
                Assert.That(
                    RenderSettings.skybox,
                    Is.SameAs(AssetDatabase.LoadAssetAtPath<Material>(
                        NomadRenderingSpikePipeline.FoundationSkyboxMaterialPath)));
                Assert.That(RenderSettings.reflectionIntensity, Is.EqualTo(0.95f).Within(0.001f));

                Assert.That(globalVolume.isGlobal, Is.True);
                Assert.That(globalVolume.weight, Is.EqualTo(1f).Within(0.001f));
                Assert.That(
                    globalVolume.sharedProfile,
                    Is.SameAs(AssetDatabase.LoadAssetAtPath<VolumeProfile>(
                        NomadRenderingSpikePipeline.FoundationVolumeProfilePath)));
                Assert.That(reflectionProbe.mode, Is.EqualTo(ReflectionProbeMode.Realtime));
                Assert.That(
                    reflectionProbe.refreshMode,
                    Is.EqualTo(ReflectionProbeRefreshMode.ViaScripting));
                Assert.That(reflectionProbe.boxProjection, Is.True);
                Assert.That(reflectionProbe.resolution, Is.EqualTo(128));
                Assert.That(
                    viewSerialized.FindProperty("skyboxMaterial").objectReferenceValue,
                    Is.SameAs(RenderSettings.skybox));
                Assert.That(
                    viewSerialized.FindProperty("reflectionProbe").objectReferenceValue,
                    Is.SameAs(reflectionProbe));
                Assert.That(
                    viewSerialized.FindProperty("screenUi").objectReferenceValue,
                    Is.SameAs(debugView),
                    "世界输入必须查询实际 HUD 布局，不能再维护一份固定像素遮挡区。 ");

                Assert.That(
                    systemSerialized.FindProperty("pickupSeconds").floatValue,
                    Is.GreaterThanOrEqualTo(1f));
                Assert.That(
                    systemSerialized.FindProperty("drinkingSeconds").floatValue,
                    Is.GreaterThanOrEqualTo(3f));
                Assert.That(
                    systemSerialized.FindProperty("leisureSeconds").floatValue,
                    Is.GreaterThanOrEqualTo(3f),
                    "首版行动阶段要给玩家留下读清状态的观察窗口。 ");

                UniversalAdditionalCameraData cameraData =
                    worldCamera.GetComponent<UniversalAdditionalCameraData>();
                Assert.That(
                    cameraData,
                    Is.Not.Null,
                    "3D Foundation 相机必须显式持有 URP 相机数据。 ");
                SerializedProperty rendererIndex = new SerializedObject(cameraData)
                    .FindProperty("m_RendererIndex");
                Assert.That(rendererIndex, Is.Not.Null);
                Assert.That(
                    rendererIndex.intValue,
                    Is.EqualTo(NomadRenderingSpikePipeline.GetGame3DRendererIndexOrThrow()),
                    "URP/Lit 与 3D Directional Light 只有在 Universal 3D Renderer 下才会按预期响应。 ");
                Assert.That(worldCamera.clearFlags, Is.EqualTo(CameraClearFlags.Skybox));
                Assert.That(worldCamera.allowHDR, Is.True);
                Assert.That(cameraData.renderPostProcessing, Is.True);
                Assert.That(
                    cameraData.antialiasing,
                    Is.EqualTo(AntialiasingMode.SubpixelMorphologicalAntiAliasing));
                Assert.That(cameraData.antialiasingQuality, Is.EqualTo(AntialiasingQuality.High));
                Assert.That(cameraData.requiresDepthTexture, Is.True);
            }
            finally
            {
                if (changedActiveScene &&
                    previousActiveScene.IsValid() &&
                    previousActiveScene.isLoaded)
                    SceneManager.SetActiveScene(previousActiveScene);
                if (openedForTest) EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static T FindOne<T>(Scene scene) where T : Component
        {
            T found = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T candidate = root.GetComponentInChildren<T>(true);
                if (candidate == null) continue;
                Assert.IsNull(found, $"场景中不应出现多个 {typeof(T).Name}。 ");
                found = candidate;
            }
            return found;
        }
    }
}
