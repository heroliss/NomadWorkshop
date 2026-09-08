using System;
using System.Linq;
using Game.NomadWorkshop.Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.NomadWorkshop.Editor
{
    /// <summary>通过 Unity Editor API 生成 N1-B 起步场景；不手写 Unity 场景 YAML。</summary>
    public static class NomadStarterJourneyPipeline
    {
        public const string ScenePath =
            "Assets/Game/NomadWorkshop/Scenes/StarterJourneySample.unity";

        [MenuItem("Assets/SSFramework/游牧工坊/N1-B/创建或打开 StarterJourneySample")]
        public static void CreateOrOpen()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("请在非 Play、编译空闲时生成 StarterJourneySample。");

            Scene current = SceneManager.GetActiveScene();
            if (current.IsValid() && current.isDirty && current.path != ScenePath)
                throw new InvalidOperationException(
                    $"当前场景 '{current.name}' 有未保存修改；请先保存或撤销后重试。");

            Scene scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null
                ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single)
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            foreach (GameObject existingRoot in scene.GetRootGameObjects())
                UnityEngine.Object.DestroyImmediate(existingRoot);

            Material groundMaterial = CreateMaterial(new Color(.16f, .17f, .14f));
            Material bodyMaterial = CreateMaterial(new Color(.24f, .27f, .25f));
            Material trimMaterial = CreateMaterial(new Color(.63f, .44f, .22f));
            Material scrapMaterial = CreateMaterial(new Color(.42f, .34f, .27f));

            var root = new GameObject("Nomad Workshop · Starter Journey");
            NomadStarterJourneyController controller = root.AddComponent<NomadStarterJourneyController>();

            GameObject ground = CreateCube(
                "Route · Desert Ground",
                root.transform,
                new Vector3(0f, -.2f, 3f),
                new Vector3(12f, .4f, 18f),
                groundMaterial);
            ground.isStatic = true;
            CreateRouteMarkings(root.transform, trimMaterial);
            CreateMicroCar(root.transform, bodyMaterial, trimMaterial);

            var scrapRoot = new GameObject("Scavenge Cache · Roadside Scrap");
            scrapRoot.transform.SetParent(root.transform, false);
            scrapRoot.transform.localPosition = new Vector3(0f, 0f, 5.2f);
            var scrapObjects = new GameObject[5];
            for (var i = 0; i < scrapObjects.Length; i++)
            {
                float x = (i - 2) * .42f;
                float y = .22f + (i % 2) * .13f;
                scrapObjects[i] = CreateCube(
                    $"Scrap · {i + 1:00}",
                    scrapRoot.transform,
                    new Vector3(x, y, (i % 3) * .22f),
                    new Vector3(.32f, .22f + (i % 2) * .12f, .42f),
                    scrapMaterial);
                scrapObjects[i].transform.localRotation = Quaternion.Euler(0f, i * 19f, i * 7f);
            }

            CreateCamera(root.transform);
            CreateLight(root.transform);
            WireController(controller, scrapObjects);
            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException($"保存场景失败：{ScenePath}");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            ValidateScene();
            Debug.Log($"[NomadWorkshop] StarterJourneySample 已生成：{ScenePath}");
        }

        private static void WireController(
            NomadStarterJourneyController controller,
            GameObject[] scrapObjects)
        {
            var serialized = new SerializedObject(controller);
            SerializedProperty visuals = serialized.FindProperty("scrapVisuals");
            visuals.arraySize = scrapObjects.Length;
            for (var i = 0; i < scrapObjects.Length; i++)
                visuals.GetArrayElementAtIndex(i).objectReferenceValue = scrapObjects[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void CreateMicroCar(
            Transform parent,
            Material bodyMaterial,
            Material trimMaterial)
        {
            var car = new GameObject("Starter MicroCar · Seat Only");
            car.transform.SetParent(parent, false);
            CreateCube("MicroCar · Long Body", car.transform,
                new Vector3(0f, .64f, 0f), new Vector3(1.7f, .9f, 3.2f), bodyMaterial);
            CreateCube("MicroCar · Driver Seat", car.transform,
                new Vector3(0f, 1.16f, -.75f), new Vector3(.62f, .62f, .62f), trimMaterial);
            CreateCube("MicroCar · Windshield", car.transform,
                new Vector3(0f, 1.28f, .65f), new Vector3(1.28f, .5f, .08f), trimMaterial);
            CreateCube("MicroCar · Forward Marker", car.transform,
                new Vector3(0f, 1.1f, 1.7f), new Vector3(.16f, .1f, .3f), trimMaterial);
        }

        private static void CreateRouteMarkings(Transform parent, Material material)
        {
            for (var i = 0; i < 5; i++)
                CreateCube($"Route Marker · {i + 1:00}", parent,
                    new Vector3(0f, .015f, -2f + i * 3f), new Vector3(.08f, .025f, 1.2f), material);
        }

        private static Camera CreateCamera(Transform parent)
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.transform.SetParent(parent, false);
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 5.2f, -8.8f);
            cameraObject.transform.LookAt(new Vector3(0f, .7f, 2.1f));
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 42f;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 100f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.09f, .12f, .14f);
            cameraObject.AddComponent<AudioListener>();
            return camera;
        }

        private static void CreateLight(Transform parent)
        {
            var lightObject = new GameObject("Warm Sun");
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.rotation = Quaternion.Euler(38f, -28f, 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.color = new Color(1f, .76f, .56f);
        }

        private static GameObject CreateCube(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 scale,
            Material material)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = position;
            cube.transform.localScale = scale;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            return cube;
        }

        private static Material CreateMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("找不到可用的 Lit 材质 Shader。");
            var material = new Material(shader) { color = color };
            return material;
        }

        private static void ValidateScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            NomadStarterJourneyController controller =
                scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<NomadStarterJourneyController>(true)).Single();
            if (controller == null || GameObject.FindGameObjectsWithTag("MainCamera").Length != 1)
                throw new InvalidOperationException("StarterJourneySample 场景接线不完整。");
        }
    }
}
