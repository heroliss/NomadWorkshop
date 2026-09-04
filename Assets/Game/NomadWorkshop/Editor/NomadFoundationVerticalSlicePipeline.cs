using System;
using Game.NomadWorkshop.Foundation;
using Game.NomadWorkshop.Navigation;
using Game.NomadWorkshop.Simulation;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Game.NomadWorkshop.Editor
{
    /// <summary>
    /// 生成 Foundation 最小垂直切片所需的定义资产与场景接线。场景内容只经 Unity Editor API 写入，
    /// 重跑时只替换这个生成场景，不修改玩家手工场景。
    /// </summary>
    public static class NomadFoundationVerticalSlicePipeline
    {
        public const string ScenePath =
            "Assets/Game/NomadWorkshop/Scenes/NomadFoundationVerticalSlice.unity";
        public const string DefinitionRoot =
            "Assets/Game/NomadWorkshop/Foundation/Definitions";
        public const string DeckLayoutPath = DefinitionRoot + "/NW_DeckLayout.asset";
        public const string VehicleWaterTankPath = DefinitionRoot + "/NW_Facility_VehicleWaterTank.asset";
        public const string DrinkingStationPath = DefinitionRoot + "/NW_Facility_DrinkingStation.asset";
        public const string FieldKitchenPath = DefinitionRoot + "/NW_Facility_FieldKitchen.asset";
        public const string ToiletPath = DefinitionRoot + "/NW_Facility_Toilet.asset";
        public const string ObservationEaselPath = DefinitionRoot + "/NW_Facility_ObservationEasel.asset";
        public const string WaterCanItemPath = DefinitionRoot + "/NW_Item_WaterCan.asset";
        public const string DrinkingCupItemPath = DefinitionRoot + "/NW_Item_DrinkingCup.asset";

        [MenuItem("Assets/SSFramework/游牧工坊/Foundation/创建或打开最小垂直切片")]
        public static void CreateOrOpen()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出 Play Mode 再生成 Foundation 场景。 ");

            Scene current = SceneManager.GetActiveScene();
            if (current.IsValid() && current.isDirty && current.path != ScenePath)
                throw new InvalidOperationException(
                    $"当前场景 '{current.name}' 有未保存修改；为避免覆盖，请先保存或撤销后重试。 ");

            EnsureAssets();
            Scene scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null
                ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single)
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            foreach (GameObject root in scene.GetRootGameObjects())
                UnityEngine.Object.DestroyImmediate(root);

            DeckLayoutDefinition layout = RequireAsset<DeckLayoutDefinition>(DeckLayoutPath);
            NomadFacilityDefinition[] definitions =
            {
                RequireAsset<NomadFacilityDefinition>(VehicleWaterTankPath),
                RequireAsset<NomadFacilityDefinition>(DrinkingStationPath),
                RequireAsset<NomadFacilityDefinition>(FieldKitchenPath),
                RequireAsset<NomadFacilityDefinition>(ToiletPath),
                RequireAsset<NomadFacilityDefinition>(ObservationEaselPath),
            };
            NomadWorldItemDefinition[] itemDefinitions =
            {
                RequireAsset<NomadWorldItemDefinition>(WaterCanItemPath),
                RequireAsset<NomadWorldItemDefinition>(DrinkingCupItemPath),
            };
            Material skyboxMaterial = RequireAsset<Material>(
                NomadRenderingSpikePipeline.FoundationSkyboxMaterialPath);
            VolumeProfile volumeProfile = RequireAsset<VolumeProfile>(
                NomadRenderingSpikePipeline.FoundationVolumeProfilePath);

            var rootObject = new GameObject("Nomad Workshop · Foundation Slice");
            rootObject.AddComponent<NomadFoundationContext>();

            GameObject data = CreateChild(rootObject.transform, "Data · Mono Model");
            data.AddComponent<NomadFoundationModel>();

            CreateNavigationInfrastructure(rootObject.transform, layout);

            GameObject logic = CreateChild(rootObject.transform, "Logic · Mono System");
            NomadFoundationSystem system = logic.AddComponent<NomadFoundationSystem>();
            WireSystem(system, layout, definitions, itemDefinitions);

            GameObject presentation = CreateChild(rootObject.transform, "Presentation · Mono Views");
            GameObject deck = CreateChild(presentation.transform, "Vehicle Deck Root");
            Camera camera = CreateCamera(
                presentation.transform,
                NomadRenderingSpikePipeline.GetGame3DRendererIndexOrThrow());
            Light keyLight = CreateLight(presentation.transform, "Key Light");
            Light fillLight = CreateLight(presentation.transform, "Fill Light");
            ConfigureLightRig(keyLight, fillLight);
            CreateGlobalVolume(presentation.transform, volumeProfile);
            ReflectionProbe reflectionProbe =
                CreateReflectionProbe(presentation.transform, layout);
            NomadFoundationWorldView worldView = presentation.AddComponent<NomadFoundationWorldView>();

            GameObject debug = CreateChild(rootObject.transform, "Debug · Command View");
            NomadFoundationDebugView debugView = debug.AddComponent<NomadFoundationDebugView>();
            WireWorldView(
                worldView,
                layout,
                definitions,
                deck.transform,
                camera,
                keyLight,
                fillLight,
                skyboxMaterial,
                reflectionProbe,
                debugView,
                itemDefinitions);
            ConfigureEnvironmentLighting(keyLight, skyboxMaterial);

            Selection.activeGameObject = rootObject;
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException($"保存场景失败：{ScenePath}");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[NomadWorkshop] Foundation 最小垂直切片已生成：{ScenePath}");
        }

        public static void EnsureAssets()
        {
            EnsureFolder("Assets/Game/NomadWorkshop/Foundation");
            EnsureFolder(DefinitionRoot);
            NomadRenderingSpikePipeline.EnsureGameGraphicsAssets();

            DeckLayoutDefinition layout = LoadOrCreate<DeckLayoutDefinition>(DeckLayoutPath);
            var layoutSerialized = new SerializedObject(layout);
            layoutSerialized.FindProperty("minX").intValue = -4;
            layoutSerialized.FindProperty("minZ").intValue = -3;
            layoutSerialized.FindProperty("width").intValue = 9;
            layoutSerialized.FindProperty("depth").intValue = 7;
            layoutSerialized.FindProperty("cellSize").floatValue = 1.2f;
            layoutSerialized.FindProperty("positionSnapMillimeters").intValue = 200;
            layoutSerialized.FindProperty("rotationSnapDeciDegrees").intValue = 450;
            layoutSerialized.ApplyModifiedPropertiesWithoutUndo();

            ConfigureDefinition(
                VehicleWaterTankPath,
                "vehicle-water-tank",
                "车辆水箱",
                NomadFacilityFunction.VehicleWaterTank,
                buildable: false,
                new[]
                {
                    new NomadFacilityFootprintPartDefinition(
                        Vector2.zero,
                        new Vector2(1.8f, 1f)),
                },
                RequiredGroup(
                    "water-pickup",
                    new NomadFacilityInteractionSlotDefinition(
                        "left",
                        new Vector2(-0.48f, -0.9f)),
                    new NomadFacilityInteractionSlotDefinition(
                        "center",
                        new Vector2(0f, -0.94f)),
                    new NomadFacilityInteractionSlotDefinition(
                        "right",
                        new Vector2(0.48f, -0.9f))),
                placeAtStart: true,
                new Vector2(-3.4f, 2.25f),
                0f,
                new Vector3(1.65f, 1.4f, 0.86f),
                new Color(0.08f, 0.48f, 0.68f));
            ConfigureDefinition(
                DrinkingStationPath,
                "drinking-station",
                "饮水站",
                NomadFacilityFunction.DrinkingStation,
                buildable: true,
                new[]
                {
                    new NomadFacilityFootprintPartDefinition(
                        Vector2.zero,
                        new Vector2(0.85f, 0.72f)),
                },
                RequiredGroup(
                    "drink-and-deliver",
                    new NomadFacilityInteractionSlotDefinition(
                        "left",
                        new Vector2(-0.24f, -0.78f)),
                    new NomadFacilityInteractionSlotDefinition(
                        "center",
                        new Vector2(0f, -0.82f)),
                    new NomadFacilityInteractionSlotDefinition(
                        "right",
                        new Vector2(0.24f, -0.78f))),
                placeAtStart: false,
                Vector2.zero,
                0f,
                new Vector3(0.78f, 0.9f, 0.65f),
                new Color(0.12f, 0.72f, 0.86f));
            ConfigureDefinition(
                FieldKitchenPath,
                "field-kitchen",
                "野战厨房",
                NomadFacilityFunction.FieldKitchen,
                buildable: true,
                new[]
                {
                    new NomadFacilityFootprintPartDefinition(
                        Vector2.zero,
                        new Vector2(2.1f, 0.95f)),
                },
                RequiredGroup(
                    "cook",
                    new NomadFacilityInteractionSlotDefinition(
                        "left",
                        new Vector2(-0.62f, -0.9f)),
                    new NomadFacilityInteractionSlotDefinition(
                        "center",
                        new Vector2(0f, -0.94f)),
                    new NomadFacilityInteractionSlotDefinition(
                        "right",
                        new Vector2(0.62f, -0.9f))),
                placeAtStart: false,
                Vector2.zero,
                0f,
                new Vector3(2.05f, 0.9f, 0.88f),
                new Color(0.12f, 0.46f, 0.44f));
            ConfigureDefinition(
                ToiletPath,
                "toilet",
                "旱厕",
                NomadFacilityFunction.Toilet,
                buildable: true,
                new[]
                {
                    new NomadFacilityFootprintPartDefinition(
                        Vector2.zero,
                        new Vector2(0.8f, 0.9f)),
                },
                RequiredGroup(
                    "use-toilet",
                    new NomadFacilityInteractionSlotDefinition(
                        "left",
                        new Vector2(-0.2f, -0.88f)),
                    new NomadFacilityInteractionSlotDefinition(
                        "center",
                        new Vector2(0f, -0.92f)),
                    new NomadFacilityInteractionSlotDefinition(
                        "right",
                        new Vector2(0.2f, -0.88f))),
                placeAtStart: false,
                Vector2.zero,
                0f,
                new Vector3(0.72f, 0.75f, 0.82f),
                new Color(0.68f, 0.62f, 0.42f));
            ConfigureDefinition(
                ObservationEaselPath,
                "observation-easel",
                "观景画架",
                NomadFacilityFunction.HobbyPoint,
                buildable: true,
                new[]
                {
                    new NomadFacilityFootprintPartDefinition(
                        Vector2.zero,
                        new Vector2(0.9f, 0.68f)),
                },
                RequiredGroup(
                    "paint-and-observe",
                    new NomadFacilityInteractionSlotDefinition(
                        "left",
                        new Vector2(-0.24f, -0.82f)),
                    new NomadFacilityInteractionSlotDefinition(
                        "center",
                        new Vector2(0f, -0.86f)),
                    new NomadFacilityInteractionSlotDefinition(
                        "right",
                        new Vector2(0.24f, -0.82f))),
                placeAtStart: false,
                Vector2.zero,
                0f,
                new Vector3(0.86f, 1.38f, 0.62f),
                new Color(0.58f, 0.28f, 0.13f));

            ConfigureWorldItemDefinition(
                WaterCanItemPath,
                "water-can",
                "防漏水罐",
                "water-can",
                new Vector2(0.34f, 0.24f),
                0.59f,
                0.02f,
                allowAnyYaw: false,
                new[] { 0f, 90f },
                NomadWorldItemPrototypeStyle.WaterCan,
                new Color(0.1f, 0.54f, 0.62f));
            ConfigureWorldItemDefinition(
                DrinkingCupItemPath,
                "drinking-cup",
                "搪瓷杯",
                "cup",
                new Vector2(0.09f, 0.09f),
                0.12f,
                0.01f,
                allowAnyYaw: true,
                new[] { 0f },
                NomadWorldItemPrototypeStyle.Cup,
                new Color(0.83f, 0.48f, 0.16f));

            EditorUtility.SetDirty(layout);
            AssetDatabase.SaveAssets();
        }

        private static void ConfigureDefinition(
            string path,
            string id,
            string displayName,
            NomadFacilityFunction function,
            bool buildable,
            NomadFacilityFootprintPartDefinition[] footprintParts,
            NomadFacilityInteractionGroupDefinition[] interactionGroups,
            bool placeAtStart,
            Vector2 startPositionMeters,
            float startYawDegrees,
            Vector3 prototypeSize,
            Color prototypeColor)
        {
            NomadFacilityDefinition definition = LoadOrCreate<NomadFacilityDefinition>(path);
            definition.ConfigureForEditor(
                id,
                displayName,
                function,
                buildable,
                footprintParts,
                interactionGroups,
                CreatePlacementRegions(function),
                placeAtStart,
                startPositionMeters,
                startYawDegrees,
                prototypeSize,
                prototypeColor);
            EditorUtility.SetDirty(definition);
        }

        private static NomadPlacementRegionDefinition[] CreatePlacementRegions(
            NomadFacilityFunction function) => function switch
            {
                NomadFacilityFunction.VehicleWaterTank =>
                new[]
                {
                    new NomadPlacementRegionDefinition(
                        "water-can-parking",
                        new Vector2(1.2f, 0f),
                        new Vector2(0.42f, 0.32f),
                        0f,
                        0f,
                        0.02f,
                        "water-can"),
                },
                NomadFacilityFunction.DrinkingStation =>
                new[]
                {
                    new NomadPlacementRegionDefinition(
                        "water-can-parking",
                        new Vector2(0.72f, 0f),
                        new Vector2(0.42f, 0.32f),
                        0f,
                        0f,
                        0.02f,
                        "water-can"),
                },
                NomadFacilityFunction.FieldKitchen =>
                new[]
                {
                    new NomadPlacementRegionDefinition(
                        "countertop-center",
                        new Vector2(0f, -0.08f),
                        new Vector2(0.42f, 0.42f),
                        0f,
                        0.97f,
                        0.03f,
                        "cup",
                        "plate",
                        "food-serving",
                        "tool-small"),
                },
                _ => Array.Empty<NomadPlacementRegionDefinition>(),
            };

        private static void ConfigureWorldItemDefinition(
            string path,
            string id,
            string displayName,
            string categoryId,
            Vector2 footprintSizeMeters,
            float heightMeters,
            float safetyMarginMeters,
            bool allowAnyYaw,
            float[] stableYawDegrees,
            NomadWorldItemPrototypeStyle prototypeStyle,
            Color prototypeColor)
        {
            NomadWorldItemDefinition definition =
                LoadOrCreate<NomadWorldItemDefinition>(path);
            definition.ConfigureForEditor(
                id,
                displayName,
                categoryId,
                footprintSizeMeters,
                heightMeters,
                safetyMarginMeters,
                allowAnyYaw,
                stableYawDegrees,
                prototypeStyle,
                prototypeColor);
            EditorUtility.SetDirty(definition);
        }

        private static NomadFacilityInteractionGroupDefinition[] RequiredGroup(
            string groupId,
            params NomadFacilityInteractionSlotDefinition[] slots) =>
            new[]
            {
                new NomadFacilityInteractionGroupDefinition(groupId, true, slots),
            };

        private static DeckNavigationUtility CreateNavigationInfrastructure(
            Transform parent,
            DeckLayoutDefinition layout)
        {
            GameObject navigation = CreateChild(
                parent,
                "Infrastructure · Continuous Deck Navigation Utility");
            NavMeshSurface surface = navigation.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = ~0;
            surface.ignoreNavMeshAgent = true;
            surface.ignoreNavMeshObstacle = true;
            surface.overrideVoxelSize = true;
            surface.voxelSize = 0.09f;
            surface.overrideTileSize = true;
            surface.tileSize = 128;
            surface.minRegionArea = 0.05f;

            GameObject walkableDeck = GameObject.CreatePrimitive(PrimitiveType.Cube);
            walkableDeck.name = "Nav Source · Walkable Deck";
            walkableDeck.transform.SetParent(navigation.transform, false);
            walkableDeck.transform.localPosition =
                layout.DeckCenterLocal + new Vector3(0f, -0.15f, 0f);
            walkableDeck.transform.localScale = new Vector3(
                layout.DeckSize.x,
                0.3f,
                layout.DeckSize.z);
            walkableDeck.isStatic = true;
            Renderer renderer = walkableDeck.GetComponent<Renderer>();
            if (renderer != null) UnityEngine.Object.DestroyImmediate(renderer);

            GameObject obstacleRoot = CreateChild(
                navigation.transform,
                "Runtime Facility Navigation Obstacles");
            DeckNavigationUtility utility = navigation.AddComponent<DeckNavigationUtility>();
            utility.ConfigureRuntime(
                surface,
                Array.Empty<NavigationAgentBinding>(),
                0.42f,
                navigation.transform,
                obstacleRoot.transform,
                2.4f);
            return utility;
        }

        private static void WireSystem(
            NomadFoundationSystem system,
            DeckLayoutDefinition layout,
            NomadFacilityDefinition[] definitions,
            NomadWorldItemDefinition[] itemDefinitions)
        {
            var serialized = new SerializedObject(system);
            serialized.FindProperty("deckLayout").objectReferenceValue = layout;
            SetObjectArray(serialized.FindProperty("facilityDefinitions"), definitions);
            SetObjectArray(serialized.FindProperty("worldItemDefinitions"), itemDefinitions);
            serialized.FindProperty("worldSeed").intValue = 1729;
            serialized.FindProperty("residentStartLocalPosition").vector3Value =
                new Vector3(0f, 0f, 2.4f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void WireWorldView(
            NomadFoundationWorldView view,
            DeckLayoutDefinition layout,
            NomadFacilityDefinition[] definitions,
            Transform deck,
            Camera camera,
            Light keyLight,
            Light fillLight,
            Material skyboxMaterial,
            ReflectionProbe reflectionProbe,
            NomadFoundationDebugView debugView,
            NomadWorldItemDefinition[] itemDefinitions)
        {
            var serialized = new SerializedObject(view);
            serialized.FindProperty("deckLayout").objectReferenceValue = layout;
            SetObjectArray(serialized.FindProperty("facilityDefinitions"), definitions);
            SetObjectArray(serialized.FindProperty("worldItemDefinitions"), itemDefinitions);
            serialized.FindProperty("deckRoot").objectReferenceValue = deck;
            serialized.FindProperty("worldCamera").objectReferenceValue = camera;
            serialized.FindProperty("keyLight").objectReferenceValue = keyLight;
            serialized.FindProperty("fillLight").objectReferenceValue = fillLight;
            serialized.FindProperty("skyboxMaterial").objectReferenceValue = skyboxMaterial;
            serialized.FindProperty("reflectionProbe").objectReferenceValue = reflectionProbe;
            serialized.FindProperty("screenUi").objectReferenceValue = debugView;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Camera CreateCamera(Transform parent, int rendererIndex)
        {
            GameObject cameraObject = CreateChild(parent, "Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.backgroundColor = new Color(0.055f, 0.075f, 0.085f);
            camera.fieldOfView = 42f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 120f;
            camera.allowHDR = true;
            UniversalAdditionalCameraData cameraData =
                camera.GetUniversalAdditionalCameraData();
            cameraData.SetRenderer(rendererIndex);
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing =
                AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cameraData.antialiasingQuality = AntialiasingQuality.High;
            cameraData.stopNaN = true;
            cameraData.dithering = true;
            cameraData.allowXRRendering = false;
            cameraData.requiresDepthTexture = true;
            cameraData.requiresColorTexture = false;
            cameraObject.AddComponent<AudioListener>();
            return camera;
        }

        private static void CreateGlobalVolume(Transform parent, VolumeProfile profile)
        {
            GameObject volumeObject = CreateChild(parent, "Graphics · Global Volume");
            Volume volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0f;
            volume.weight = 1f;
            volume.sharedProfile = profile;
        }

        private static ReflectionProbe CreateReflectionProbe(
            Transform parent,
            DeckLayoutDefinition layout)
        {
            GameObject probeObject =
                CreateChild(parent, "Graphics · Vehicle Reflection Probe");
            ReflectionProbe probe = probeObject.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
            probe.clearFlags = ReflectionProbeClearFlags.Skybox;
            probe.hdr = true;
            probe.resolution = 128;
            probe.boxProjection = true;
            probe.blendDistance = 1.5f;
            probe.intensity = 0.9f;
            probe.importance = 1;
            probe.nearClipPlane = 0.1f;
            probe.farClipPlane = 80f;
            probe.center = layout.DeckCenterLocal + Vector3.up * 1.4f;
            probe.size = new Vector3(
                layout.DeckSize.x + 4f,
                6f,
                layout.DeckSize.z + 4f);
            return probe;
        }

        private static Light CreateLight(Transform parent, string name)
        {
            GameObject lightObject = CreateChild(parent, name);
            return lightObject.AddComponent<Light>();
        }

        private static void ConfigureEnvironmentLighting(
            Light keyLight,
            Material skyboxMaterial)
        {
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1.02f;
            RenderSettings.skybox = skyboxMaterial;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 0.95f;
            RenderSettings.sun = keyLight;
            RenderSettings.fog = false;
        }

        private static void ConfigureLightRig(Light keyLight, Light fillLight)
        {
            keyLight.type = LightType.Directional;
            keyLight.color = new Color(1f, 0.89f, 0.72f);
            keyLight.intensity = 1.6f;
            keyLight.shadows = LightShadows.Soft;
            keyLight.transform.rotation = Quaternion.Euler(48f, -35f, 0f);

            fillLight.type = LightType.Directional;
            fillLight.color = new Color(0.52f, 0.68f, 1f);
            fillLight.intensity = 0.52f;
            fillLight.shadows = LightShadows.None;
            fillLight.transform.rotation = Quaternion.Euler(42f, 145f, 0f);
        }

        private static GameObject CreateChild(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child;
        }

        private static void SetObjectArray<T>(SerializedProperty property, T[] values)
            where T : UnityEngine.Object
        {
            property.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static T RequireAsset<T>(string path) where T : UnityEngine.Object =>
            AssetDatabase.LoadAssetAtPath<T>(path) ??
            throw new InvalidOperationException($"缺少生成资产：{path}");

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = System.IO.Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
                throw new InvalidOperationException($"无效的 Unity 目录：{path}");
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
