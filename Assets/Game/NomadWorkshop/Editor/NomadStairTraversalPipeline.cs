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
    /// <summary>显式创建独立跨层实验；场景只经编辑器落盘，复用已导入的首版美术资产。</summary>
    public static class NomadStairTraversalPipeline
    {
        public const string ScenePath = "Assets/Game/NomadWorkshop/Scenes/StairCarrySpike.unity";
        private const float CarrierRadius = .67f;

        [MenuItem("Assets/SSFramework/游牧工坊/首版美术/创建或打开持桶楼梯实验")]
        public static void CreateOrOpen()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出 Play 再创建持桶楼梯实验。");
            Scene current = SceneManager.GetActiveScene();
            if (current.IsValid() && current.isDirty)
                throw new InvalidOperationException("当前场景有未保存修改，请先保存再创建实验。");
            GameObject prefab = Load<GameObject>(NomadWorkshopResidentArtPipeline.PrefabPath);
            RuntimeAnimatorController controller = Load<RuntimeAnimatorController>(NomadWarmWorkshopArtPipeline.ControllerPath);
            Material deck = Material("Deck"), teal = Material("Teal"), frame = Material("Frame"),
                orange = Material("Orange"), ivory = Material("Ivory"), wood = Material("Wood");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Nomad Workshop · Stair Carry Experiment");
            // 与正式甲板保持物理隔离，允许测试以 Additive 方式载入，不共享 NavMesh 岛和 Collider。
            root.transform.position = new Vector3(100f, 0f, 0f);
            root.AddComponent<NomadFoundationContext>();
            Child("Data", root.transform).AddComponent<NomadStairTraversalModel>();
            Transform nav = Child("Walkable Structure", root.transform).transform;
            NavMeshSurface surface = nav.gameObject.AddComponent<NavMeshSurface>();
            surface.agentTypeID = EnsureStairAgentType();
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
            Cube(nav, "Floor 1 · Ground Deck", new(0,-.12f,0), new(9,.24f,14), deck);
            Cube(nav, "Floor 2 · Partial Deck", new(0,3.08f,4.8f), new(7,.24f,4.8f), deck);
            foreach (float x in new[] { -3.2f, 3.2f })
            foreach (float z in new[] { 2.65f, 6.85f })
                Cube(nav, "Floor 2 Support", new(x,1.5f,z), new(.18f,3f,.18f), teal);

            const float stairX = 1.2f, startZ = -3f, tread = .3f;
            float rise = NomadStairTraversalSystem.UpperHeight / 18f;
            for (int i = 0; i < 18; i++)
            {
                float top = (i+1)*rise, z = startZ + (i+.5f)*tread;
                Cube(nav, $"Step {i+1:00} · Actual Tread", new(stairX,top*.5f,z), new(1.65f,top,tread), deck);
                Cube(nav, $"Step {i+1:00} · Visible Nosing", new(stairX,top+.002f,z-tread*.5f+.025f),
                    new(1.65f,.008f,.05f), i % 3 == 0 ? orange : ivory, false);
            }
            foreach (float side in new[] { -.9f, .9f })
            {
                float x = stairX + side;
                for (int i = 0; i <= 18; i += 3)
                {
                    float y = i*rise, z = startZ+i*tread;
                    Cube(nav, "Stair Guard Post", new(x,y+.51f,z), new(.06f,1.02f,.06f), teal);
                }
                Bar(nav, "Warm Wood Handrail", new(x,1.05f,startZ), new(x,4.25f,2.4f), wood);
            }
            Bar(nav, "Upper Back Rail", new(-3.45f,4.2f,7.1f), new(3.45f,4.2f,7.1f), teal);
            Bar(nav, "Upper Left Rail", new(-3.45f,4.2f,2.5f), new(-3.45f,4.2f,7.1f), teal);
            Bar(nav, "Upper Right Rail", new(3.45f,4.2f,2.5f), new(3.45f,4.2f,7.1f), teal);
            Cube(nav, "Lower Destination", NomadStairTraversalSystem.LowerGoal + Vector3.up*.006f,
                new(.85f,.012f,.85f), orange, false);
            Cube(nav, "Upper Destination · Same XZ", NomadStairTraversalSystem.UpperGoal + Vector3.up*.006f,
                new(.85f,.012f,.85f), orange, false);
            Child("Logic", root.transform).AddComponent<NomadStairTraversalSystem>();
            NomadStairTraversalView view = Child("Presentation", root.transform).AddComponent<NomadStairTraversalView>();
            view.Configure(prefab, controller, nav, teal, frame, Material("Glass"));
            Camera camera = Child("Review Camera", root.transform).AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.localPosition = new Vector3(11f,10f,-11f);
            camera.transform.LookAt(root.transform.TransformPoint(new Vector3(0f,1.5f,1.4f)));
            camera.orthographic = true; camera.orthographicSize = 8.2f;
            camera.nearClipPlane = .1f; camera.farClipPlane = 70f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.35f,.34f,.3f);
            camera.GetUniversalAdditionalCameraData().SetRenderer(NomadRenderingSpikePipeline.GetGame3DRendererIndexOrThrow());
            Light sun = Child("Warm Afternoon", root.transform).AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.25f;
            sun.color = new Color(1f,.95f,.87f); sun.shadows = LightShadows.Soft; sun.shadowStrength = .7f;
            sun.transform.rotation = Quaternion.Euler(48f,-25f,0f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.66f,.69f,.73f);
            RenderSettings.ambientEquatorColor = new Color(.65f,.60f,.52f);
            RenderSettings.ambientGroundColor = new Color(.50f,.46f,.38f);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("楼梯实验保存失败。");
            Debug.Log($"[NomadStairTraversal] 两层持桶实验已保存：{ScenePath}；18 级真实踏面，上下目标 XZ 相同。");
        }

        private static GameObject Child(string name, Transform parent)
        {
            var child = new GameObject(name); child.transform.SetParent(parent, false); return child;
        }

        private static GameObject Cube(Transform parent, string name, Vector3 position, Vector3 size,
            Material material, bool collider = true)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name; cube.transform.SetParent(parent, false);
            cube.transform.localPosition = position; cube.transform.localScale = size;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            if (!collider) UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
            return cube;
        }

        private static void Bar(Transform parent, string name, Vector3 start, Vector3 end, Material material)
        {
            GameObject bar = Cube(parent, name, (start+end)*.5f, new(.07f,Vector3.Distance(start,end),.07f), material);
            bar.transform.localRotation = Quaternion.FromToRotation(Vector3.up, (end-start).normalized);
        }

        private static Material Material(string name) => Load<Material>(NomadWarmWorkshopArtPipeline.Root + "/Materials/NW1_" + name + ".mat");

        /// <summary>
        /// 只更新本实验的通行能力，保持其他 Agent 类型不变。采用当前 Unity NavigationWindow
        /// 的 SerializedObject 接口，不手改 ProjectSettings YAML；属性缺失时明确失败。
        /// </summary>
        private static int EnsureStairAgentType()
        {
            const string name = "Nomad Stair Carrier";
            for (int i = 0; i < NavMesh.GetSettingsCount(); i++)
            {
                NavMeshBuildSettings existing = NavMesh.GetSettingsByIndex(i);
                if (NavMesh.GetSettingsNameFromID(existing.agentTypeID) != name) continue;
                bool legacyRadius = Mathf.Abs(existing.agentRadius - .2f) < .001f ||
                    Mathf.Abs(existing.agentRadius - .4f) < .001f ||
                    Mathf.Abs(existing.agentRadius - .65f) < .001f;
                if (Mathf.Abs(existing.agentClimb - .22f) > .001f ||
                    (!legacyRadius && Mathf.Abs(existing.agentRadius - CarrierRadius) > .001f) ||
                    Mathf.Abs(existing.agentHeight - 1.8f) > .001f || Mathf.Abs(existing.agentSlope - 40f) > .001f)
                    throw new InvalidOperationException("Nomad Stair Carrier 配置与实验身体不一致，请检查导航设置。");
                // 握点移出大腿后，按整只水罐的外缘预留转身空间；仅迁移明确识别的自有旧配置。
                if (legacyRadius)
                {
                    using var migration = new SerializedObject(Unsupported.GetSerializedAssetInterfaceSingleton("NavMeshProjectSettings"));
                    migration.FindProperty("m_Settings").GetArrayElementAtIndex(i)
                        .FindPropertyRelative("agentRadius").floatValue = CarrierRadius;
                    migration.ApplyModifiedProperties();
                    AssetDatabase.SaveAssets();
                }
                return existing.agentTypeID;
            }
            UnityEngine.Object project = Unsupported.GetSerializedAssetInterfaceSingleton("NavMeshProjectSettings");
            using var serialized = new SerializedObject(project);
            if (serialized.FindProperty("m_Settings") == null || serialized.FindProperty("m_SettingNames") == null)
                throw new InvalidOperationException("当前 Unity 未暴露预期的导航设置接口。");
            int id = NavMesh.CreateSettings().agentTypeID;
            serialized.Update();
            SerializedProperty settings = serialized.FindProperty("m_Settings");
            SerializedProperty names = serialized.FindProperty("m_SettingNames");
            for (int i = 0; i < settings.arraySize; i++)
            {
                SerializedProperty entry = settings.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("agentTypeID").intValue != id) continue;
                names.GetArrayElementAtIndex(i).stringValue = name;
                entry.FindPropertyRelative("agentRadius").floatValue = CarrierRadius;
                entry.FindPropertyRelative("agentHeight").floatValue = 1.8f;
                entry.FindPropertyRelative("agentClimb").floatValue = .22f;
                entry.FindPropertyRelative("agentSlope").floatValue = 40f;
                serialized.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                return id;
            }
            throw new InvalidOperationException("创建楼梯 Agent 类型后无法找到其序列化配置。");
        }

        private static T Load<T>(string path) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(path) != null
            ? AssetDatabase.LoadAssetAtPath<T>(path) : throw new InvalidOperationException("请先导入温暖工坊美术资产：" + path);
    }
}
