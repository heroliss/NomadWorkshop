using System;
using Game.NomadWorkshop.Foundation;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace Game.NomadWorkshop.Navigation
{
    /// <summary>连续导航 Harness 的唯一组合工厂，供生成场景与 PlayMode 测试复用。</summary>
    public static class NomadNavigationSpikeRuntimeFactory
    {
        public static NavigationInteractionSpikeComposition Create(bool fastMode = false)
        {
            var root = new GameObject("Nomad Workshop · Navigation Interaction Spike");
            root.SetActive(false);
            root.AddComponent<NomadFoundationContext>();

            GameObject data = CreateChild(root.transform, "Data · Mono Model");
            NomadNavigationSpikeModel model = data.AddComponent<NomadNavigationSpikeModel>();

            GameObject navigation = CreateChild(root.transform, "Infrastructure · Deck Navigation Utility");
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

            GameObject deck = CreatePrimitive(
                navigation.transform,
                "Walkable Deck · 16 × 10 m",
                PrimitiveType.Cube,
                new Vector3(0f, -0.15f, 0f),
                new Vector3(16f, 0.3f, 10f),
                Quaternion.identity,
                new Color(0.13f, 0.16f, 0.17f));
            deck.isStatic = true;

            GameObject cabinet = CreateChild(
                navigation.transform,
                "Rotated Cabinet Obstacle · 27°",
                new Vector3(0f, 0f, 0f),
                Quaternion.Euler(0f, 27f, 0f));
            GameObject cabinetBody = CreatePrimitive(
                cabinet.transform,
                "Static Body + Navigation Collider",
                PrimitiveType.Cube,
                new Vector3(0f, 0.72f, 0f),
                new Vector3(2.8f, 1.44f, 1.7f),
                Quaternion.identity,
                new Color(0.13f, 0.48f, 0.45f),
                localSpace: true);
            cabinetBody.isStatic = true;

            Transform handle = CreateChild(cabinet.transform, "Shared Hand Target").transform;
            handle.localPosition = new Vector3(0f, 0.74f, -0.89f);

            Transform doorPivot = CreateChild(cabinet.transform, "Door Pivot").transform;
            doorPivot.localPosition = new Vector3(-1.36f, 0.72f, -0.87f);
            GameObject door = CreatePrimitive(
                doorPivot,
                "Door Visual",
                PrimitiveType.Cube,
                new Vector3(1.36f, 0f, -0.035f),
                new Vector3(2.68f, 1.24f, 0.07f),
                Quaternion.identity,
                new Color(0.9f, 0.48f, 0.12f),
                keepCollider: false,
                localSpace: true);
            door.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.On;

            FacilityInteractionGroup group = cabinet.AddComponent<FacilityInteractionGroup>();
            FacilityInteractionSlot[] slots =
            {
                CreateSlot(cabinet.transform, "left", new Vector3(-0.82f, 0f, -1.47f), handle),
                CreateSlot(cabinet.transform, "center", new Vector3(0f, 0f, -1.58f), handle),
                CreateSlot(cabinet.transform, "right", new Vector3(0.82f, 0f, -1.47f), handle),
            };
            group.ConfigureRuntime("cabinet-storage-door", true, slots);

            GameObject presentation = CreateChild(root.transform, "Presentation · Mono View");
            BuildDeckGrid(presentation.transform);
            CreateSlotMarkers(presentation.transform, slots);
            CreateProbeMarkers(presentation.transform);

            NavMeshAgent agentA = CreateAgent(
                presentation.transform,
                surface.agentTypeID,
                "Resident A · Orange",
                new Vector3(-6f, 0f, -1.9f),
                new Color(1f, 0.5f, 0.12f),
                fastMode ? 10f : 3.6f,
                40,
                out GameObject cargoA);
            NavMeshAgent agentB = CreateAgent(
                presentation.transform,
                surface.agentTypeID,
                "Resident B · Blue",
                new Vector3(6f, 0f, -2.5f),
                new Color(0.12f, 0.65f, 1f),
                fastMode ? 10f : 3.6f,
                60,
                out GameObject cargoB);

            DeckNavigationUtility utility = navigation.AddComponent<DeckNavigationUtility>();
            utility.ConfigureRuntime(
                surface,
                new[]
                {
                    new NavigationAgentBinding("resident-a", agentA),
                    new NavigationAgentBinding("resident-b", agentB),
                },
                1.5f);

            GameObject logic = CreateChild(root.transform, "Logic · Mono System");
            NomadNavigationSpikeSystem system = logic.AddComponent<NomadNavigationSpikeSystem>();
            system.ConfigureRuntime(group, fastMode);

            NomadNavigationSpikeView view = presentation.AddComponent<NomadNavigationSpikeView>();
            view.ConfigureRuntime(doorPivot, cargoA, cargoB);
            CreateCameraAndLight(presentation.transform);

            root.SetActive(true);
            return new NavigationInteractionSpikeComposition(root, model, system, utility, group, view);
        }

        private static FacilityInteractionSlot CreateSlot(
            Transform parent,
            string id,
            Vector3 localPosition,
            Transform handTarget)
        {
            GameObject slotObject = CreateChild(parent, $"Interaction Slot · {id}");
            slotObject.transform.localPosition = localPosition;
            slotObject.transform.localRotation = Quaternion.identity;
            FacilityInteractionSlot slot = slotObject.AddComponent<FacilityInteractionSlot>();
            slot.ConfigureRuntime(id, ResidentAnimationSemantic.Pickup, handTarget);
            return slot;
        }

        private static NavMeshAgent CreateAgent(
            Transform parent,
            int agentTypeId,
            string name,
            Vector3 position,
            Color color,
            float speed,
            int avoidancePriority,
            out GameObject cargo)
        {
            GameObject root = CreateChild(parent, name);
            root.transform.position = position;
            NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
            // 组合根启用时 NavMesh 尚未由 System 建立；由 DeckNavigationUtility.TryWarp
            // 在构建和目标采样成功后启用，避免产生“没有有效 NavMesh”的误导性警告。
            agent.enabled = false;
            // 新建 Agent 的默认类型受项目状态影响，必须与本次烘焙 Surface 显式一致。
            agent.agentTypeID = agentTypeId;
            agent.radius = 0.34f;
            agent.height = 1.7f;
            agent.baseOffset = 0f;
            agent.speed = speed;
            agent.angularSpeed = 720f;
            agent.acceleration = speed * 4f;
            agent.stoppingDistance = 0.07f;
            agent.autoBraking = true;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            agent.avoidancePriority = avoidancePriority;

            CreatePrimitive(
                root.transform,
                "Body Visual",
                PrimitiveType.Capsule,
                new Vector3(0f, 0.85f, 0f),
                new Vector3(0.64f, 0.85f, 0.64f),
                Quaternion.identity,
                color,
                keepCollider: false,
                localSpace: true);
            cargo = CreatePrimitive(
                root.transform,
                "Carried Item Visual",
                PrimitiveType.Cube,
                new Vector3(0.34f, 1.05f, 0.22f),
                Vector3.one * 0.26f,
                Quaternion.Euler(0f, 20f, 12f),
                new Color(0.94f, 0.82f, 0.28f),
                keepCollider: false,
                localSpace: true);
            cargo.SetActive(false);
            return agent;
        }

        private static void BuildDeckGrid(Transform parent)
        {
            Color gridColor = new(0.24f, 0.3f, 0.31f);
            for (var x = -8; x <= 8; x++)
            {
                CreatePrimitive(
                    parent,
                    $"Grid X {x}",
                    PrimitiveType.Cube,
                    new Vector3(x, 0.012f, 0f),
                    new Vector3(0.018f, 0.018f, 10f),
                    Quaternion.identity,
                    gridColor,
                    keepCollider: false);
            }
            for (var z = -5; z <= 5; z++)
            {
                CreatePrimitive(
                    parent,
                    $"Grid Z {z}",
                    PrimitiveType.Cube,
                    new Vector3(0f, 0.012f, z),
                    new Vector3(16f, 0.018f, 0.018f),
                    Quaternion.identity,
                    gridColor,
                    keepCollider: false);
            }
        }

        private static void CreateSlotMarkers(Transform parent, FacilityInteractionSlot[] slots)
        {
            Color[] colors =
            {
                new(0.35f, 0.95f, 0.5f),
                new(0.25f, 1f, 0.75f),
                new(0.35f, 0.95f, 0.5f),
            };
            for (var i = 0; i < slots.Length; i++)
            {
                FacilityInteractionSlot slot = slots[i];
                CreatePrimitive(
                    parent,
                    $"Slot Marker · {slot.SlotId}",
                    PrimitiveType.Cylinder,
                    slot.WorldPosition + Vector3.up * 0.025f,
                    new Vector3(0.34f, 0.025f, 0.34f),
                    slot.WorldRotation,
                    colors[i],
                    keepCollider: false);
            }
        }

        private static void CreateProbeMarkers(Transform parent)
        {
            CreateMarker(parent, "Open Path Start", new Vector3(-6f, 0.03f, 3.5f), Color.white);
            CreateMarker(parent, "Open Path End", new Vector3(6f, 0.03f, 3.5f), Color.white);
        }

        private static void CreateMarker(Transform parent, string name, Vector3 position, Color color)
        {
            CreatePrimitive(
                parent,
                name,
                PrimitiveType.Cylinder,
                position,
                new Vector3(0.18f, 0.025f, 0.18f),
                Quaternion.identity,
                color,
                keepCollider: false);
        }

        private static void CreateCameraAndLight(Transform parent)
        {
            GameObject cameraObject = CreateChild(parent, "Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(11.5f, 14f, -14.5f);
            cameraObject.transform.LookAt(new Vector3(0f, 0f, 0f));
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 7.4f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.055f, 0.065f, 0.07f);
            cameraObject.AddComponent<AudioListener>();

            GameObject lightObject = CreateChild(parent, "Key Light");
            lightObject.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.3f;
            light.shadows = LightShadows.Soft;
        }

        private static GameObject CreatePrimitive(
            Transform parent,
            string name,
            PrimitiveType primitiveType,
            Vector3 position,
            Vector3 scale,
            Quaternion rotation,
            Color color,
            bool keepCollider = true,
            bool localSpace = false)
        {
            GameObject instance = GameObject.CreatePrimitive(primitiveType);
            instance.name = name;
            instance.transform.SetParent(parent, false);
            if (localSpace)
            {
                instance.transform.localPosition = position;
                instance.transform.localRotation = rotation;
            }
            else
            {
                instance.transform.position = position;
                instance.transform.rotation = rotation;
            }
            instance.transform.localScale = scale;
            if (!keepCollider)
            {
                Collider collider = instance.GetComponent<Collider>();
                if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
            }
            instance.AddComponent<NavigationSpikeTint>().ConfigureRuntime(color);
            ApplyColor(instance.GetComponent<Renderer>(), color);
            return instance;
        }

        private static void ApplyColor(Renderer renderer, Color color)
        {
            if (renderer == null) return;
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            renderer.SetPropertyBlock(block);
        }

        private static GameObject CreateChild(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child;
        }

        private static GameObject CreateChild(
            Transform parent,
            string name,
            Vector3 localPosition,
            Quaternion localRotation)
        {
            GameObject child = CreateChild(parent, name);
            child.transform.localPosition = localPosition;
            child.transform.localRotation = localRotation;
            return child;
        }
    }

    public sealed class NavigationInteractionSpikeComposition
    {
        public NavigationInteractionSpikeComposition(
            GameObject root,
            NomadNavigationSpikeModel model,
            NomadNavigationSpikeSystem system,
            DeckNavigationUtility navigation,
            FacilityInteractionGroup interactionGroup,
            NomadNavigationSpikeView view)
        {
            Root = root;
            Model = model;
            System = system;
            Navigation = navigation;
            InteractionGroup = interactionGroup;
            View = view;
        }

        public GameObject Root { get; }
        public NomadNavigationSpikeModel Model { get; }
        public NomadNavigationSpikeSystem System { get; }
        public DeckNavigationUtility Navigation { get; }
        public FacilityInteractionGroup InteractionGroup { get; }
        public NomadNavigationSpikeView View { get; }
    }
}
