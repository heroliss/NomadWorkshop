using System;
using Game.NomadWorkshop.Foundation;
using Game.NomadWorkshop.Navigation;
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
    /// NW15 的单人陡梯候选。它只生成独立实验场景，不改正式多层建造或存档。
    /// </summary>
    public static class NomadCompactSteepStairPipeline
    {
        public const string ScenePath = "Assets/Game/NomadWorkshop/Scenes/CompactSteepCarrySpike.unity";
        private const float Width = 1.48f;
        private const int StepCount = 16;
        private const float Rise = NomadStairTraversalSystem.UpperHeight / StepCount;
        private const float Tread = .25f;
        private const float StartZ = -2.6f;

        [MenuItem("Assets/SSFramework/游牧工坊/首版美术/创建或打开紧凑单人陡梯实验")]
        public static void CreateOrOpen()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("请在非 Play 且编译空闲时创建紧凑陡梯。");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("请先保存脏场景。");

            GameObject prefab = Load<GameObject>(NomadWarmWorkshopArtPipeline.Root + "/Prefabs/NW10_Mechanic.prefab");
            Mesh treadMesh = Load<Mesh>(NomadWarmWorkshopArtPipeline.Root + "/Models/NW12_FoldedTread.asset");
            RuntimeAnimatorController controller = Load<RuntimeAnimatorController>(NomadWarmWorkshopArtPipeline.ControllerPath);
            Material deck = Material("Deck"), teal = Material("Teal"), frame = Material("Frame"),
                orange = Material("Orange"), ivory = Material("Ivory"), wood = Material("Wood");

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject root = new("Nomad Workshop · Compact Steep Stair");
            root.transform.position = new Vector3(100f, 0f, 0f);
            root.AddComponent<NomadFoundationContext>();
            Child("Data", root.transform).AddComponent<NomadStairTraversalModel>();
            Transform nav = Child("Walkable Structure", root.transform).transform;

            NavMeshSurface surface = nav.gameObject.AddComponent<NavMeshSurface>();
            surface.agentTypeID = NomadStairTraversalPipeline.EnsureStairAgentType();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.ignoreNavMeshAgent = true;
            surface.ignoreNavMeshObstacle = true;
            surface.overrideVoxelSize = true;
            surface.voxelSize = .035f;
            surface.overrideTileSize = true;
            surface.tileSize = 128;
            surface.minRegionArea = .05f;

            DeckNavigationUtility navigation = nav.gameObject.AddComponent<DeckNavigationUtility>();
            navigation.ConfigureRuntime(surface, Array.Empty<NavigationAgentBinding>(), .3f, nav);
            Cube(nav, "Floor 1 · Ground Deck", new(0, -.12f, 0), new(9, .24f, 14), deck);

            float upperCenterZ = 4.3f;
            Cube(nav, "Floor 2 · Partial Deck", new(0, 3.08f, upperCenterZ), new(7, .24f, 5.8f), deck);
            foreach (float x in new[] { -3.2f, 3.2f })
            foreach (float z in new[] { 1.4f, 7.2f })
                Cube(nav, "Floor 2 Support", new(x, 1.5f, z), new(.18f, 3f, .18f), teal);

            const float stairX = 1.15f;
            for (int i = 0; i < StepCount; i++)
            {
                float top = (i + 1) * Rise;
                float z = StartZ + (i + .5f) * Tread;
                GameObject step = Child($"Step {i + 1:00} · Actual Tread", nav);
                step.transform.localPosition = new(stairX, top, StartZ + i * Tread);
                step.transform.localScale = new(Width / 1.65f, 1f, Tread / .30f);
                step.AddComponent<MeshFilter>().sharedMesh = treadMesh;
                step.AddComponent<MeshRenderer>().sharedMaterial = deck;
                step.AddComponent<MeshCollider>().sharedMesh = treadMesh;
                Cube(nav, $"Step {i + 1:00} · Visible Nosing", new(stairX, top + .002f, z - Tread * .5f + .025f),
                    new(Width, .008f, .05f), i % 3 == 0 ? orange : ivory, false);
            }

            float railX = Width * .5f + .075f;
            foreach (float side in new[] { -1f, 1f })
            {
                float x = stairX + side * railX;
                Bar(nav, "Compact Stair Stringer", new(x, -.04f, StartZ),
                    new(x, 3.16f, StartZ + StepCount * Tread), frame, .09f);
                for (int i = 0; i <= StepCount; i += 2)
                {
                    float y = i * Rise, z = StartZ + i * Tread;
                    Cube(nav, "Compact Stair Guard Post", new(x, y + .46f, z), new(.06f, .92f, .06f), teal);
                }
                Bar(nav, "Compact Stair Handrail", new(x, .92f, StartZ),
                    new(x, 4.05f, StartZ + StepCount * Tread), wood, .048f);
            }

            Cube(nav, "Lower Destination", NomadStairTraversalSystem.LowerGoal + Vector3.up * .006f,
                new(.85f, .012f, .85f), orange, false);
            Cube(nav, "Upper Destination · Same XZ", NomadStairTraversalSystem.UpperGoal + Vector3.up * .006f,
                new(.85f, .012f, .85f), orange, false);
            Bar(nav, "Upper Back Rail", new(-3.45f, 4.2f, 7.1f), new(3.45f, 4.2f, 7.1f), teal, .07f);
            Bar(nav, "Upper Left Rail", new(-3.45f, 4.2f, 1.4f), new(-3.45f, 4.2f, 7.1f), teal, .07f);
            Bar(nav, "Upper Right Rail", new(3.45f, 4.2f, 1.4f), new(3.45f, 4.2f, 7.1f), teal, .07f);
            foreach (float x in new[] { -3.45f, 3.45f })
            foreach (float z in new[] { 1.48f, 4.3f, 7.1f })
                Cube(nav, "Upper Rail Support", new(x, 3.7f, z), new(.07f, 1f, .07f), teal);

            Child("Traffic", root.transform).AddComponent<NomadStairTrafficGate>();
            Child("Logic", root.transform).AddComponent<NomadStairTraversalSystem>();
            NomadStairTraversalView view = Child("Presentation", root.transform).AddComponent<NomadStairTraversalView>();
            view.Configure(prefab, controller, nav, teal, frame, Material("Glass"));
            var serializedView = new SerializedObject(view);
            serializedView.FindProperty("containerPrefab").objectReferenceValue =
                Load<GameObject>(NomadWaterCanArtPipeline.PrefabPath);
            serializedView.ApplyModifiedPropertiesWithoutUndo();

            Camera camera = Child("Review Camera", root.transform).AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.localPosition = new Vector3(10.5f, 9.2f, -11.5f);
            camera.transform.LookAt(root.transform.TransformPoint(new Vector3(0f, 1.5f, .7f)));
            camera.orthographic = true;
            camera.orthographicSize = 7.4f;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 70f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.35f, .34f, .3f);
            camera.GetUniversalAdditionalCameraData().SetRenderer(NomadRenderingSpikePipeline.GetGame3DRendererIndexOrThrow());

            Light sun = Child("Warm Afternoon", root.transform).AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.18f;
            sun.color = new Color(1f, .95f, .87f);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = .7f;
            sun.transform.rotation = Quaternion.Euler(48f, -25f, 0f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.66f, .69f, .73f);
            RenderSettings.ambientEquatorColor = new Color(.65f, .60f, .52f);
            RenderSettings.ambientGroundColor = new Color(.50f, .46f, .38f);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException("紧凑陡梯保存失败。");
            Debug.Log($"[NomadCompactSteepStair] 已创建 {StepCount} 级、{Width:F2} m 净宽、约 39° 坡度的真实踏面实验：{ScenePath}");
        }

        private static GameObject Child(string name, Transform parent)
        {
            GameObject child = new(name);
            child.transform.SetParent(parent, false);
            return child;
        }

        private static GameObject Cube(Transform parent, string name, Vector3 position, Vector3 size,
            Material material, bool collider = true)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = position;
            cube.transform.localScale = size;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            if (!collider) UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
            return cube;
        }

        private static void Bar(Transform parent, string name, Vector3 start, Vector3 end,
            Material material, float width)
        {
            GameObject bar = Cube(parent, name, (start + end) * .5f,
                new(width, Vector3.Distance(start, end), width), material);
            bar.transform.localRotation = Quaternion.FromToRotation(Vector3.up, (end - start).normalized);
        }

        private static Material Material(string name) =>
            Load<Material>(NomadWarmWorkshopArtPipeline.Root + "/Materials/NW1_" + name + ".mat");

        private static T Load<T>(string path) where T : UnityEngine.Object =>
            AssetDatabase.LoadAssetAtPath<T>(path) != null
                ? AssetDatabase.LoadAssetAtPath<T>(path)
                : throw new InvalidOperationException("缺少紧凑楼梯依赖资产：" + path);
    }
}
