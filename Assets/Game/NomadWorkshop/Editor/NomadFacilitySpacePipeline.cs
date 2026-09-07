using System;
using System.Collections.Generic;
using System.Linq;
using Game.NomadWorkshop.Foundation;
using Game.NomadWorkshop.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Game.NomadWorkshop.Editor
{
    /// <summary>在独立水设施样板验证模型空间变化。配方可按旧定义建立首批标记，后续编辑标记再生成即可。</summary>
    public static class NomadFacilitySpacePipeline
    {
        public const string ScenePath = "Assets/Game/NomadWorkshop/Scenes/FacilitySpaceSample.unity";
        private const string SourceScene = "Assets/Game/NomadWorkshop/Scenes/ReferenceVehicleSample.unity";
        private const string Root = NomadWarmWorkshopArtPipeline.Root;

        [MenuItem("Assets/SSFramework/游牧工坊/首版美术/生成并打开设施空间变体")]
        public static void GenerateAndOpen()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("请在非 Play 且编译空闲时生成设施空间变体。");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("存在未保存场景，请先保存。");
            var replacements = new Dictionary<string, string>();
            foreach (string kind in new[] { "WaterTank", "Dispenser" })
            {
                string suffix = kind == "WaterTank" ? "VehicleWaterTank" : "DrinkingStation";
                string originalPath = Root + "/Definitions/NW_Facility_" + suffix + ".asset";
                string path = Root + "/Definitions/NW7_Facility_" + suffix + ".asset";
                var original = AssetDatabase.LoadAssetAtPath<NomadFacilityDefinition>(originalPath);
                if (original == null) throw new InvalidOperationException("缺少原水设施定义：" + suffix);
                GameObject prefab = BuildVariant(kind, original);
                if (AssetDatabase.LoadAssetAtPath<NomadFacilityDefinition>(path) == null && !AssetDatabase.CopyAsset(originalPath, path))
                    throw new InvalidOperationException("设施空间变体定义创建失败：" + path);
                var definition = AssetDatabase.LoadAssetAtPath<NomadFacilityDefinition>(path);
                NomadFacilitySpaceBaker.Bake(definition, prefab);
                replacements.Add(definition.Id, path);
            }
            Scene source = EditorSceneManager.OpenScene(SourceScene);
            var sourceIds = Owners(source).ToDictionary(owner => owner.GetType(), owner =>
            {
                var array = new SerializedObject(owner).FindProperty("facilityDefinitions");
                return Enumerable.Range(0, array.arraySize).Select(i =>
                    ((NomadFacilityDefinition)array.GetArrayElementAtIndex(i).objectReferenceValue).Id).ToArray();
            });
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null && !EditorSceneManager.SaveScene(source, ScenePath, true))
                throw new InvalidOperationException("设施空间样板场景创建失败。");
            Scene sample = EditorSceneManager.OpenScene(ScenePath);
            foreach (Component owner in Owners(sample))
            {
                var data = new SerializedObject(owner);
                var array = data.FindProperty("facilityDefinitions");
                for (int i = 0; i < array.arraySize; i++)
                {
                    var item = array.GetArrayElementAtIndex(i);
                    var old = item.objectReferenceValue as NomadFacilityDefinition;
                    string id = old != null ? old.Id : sourceIds[owner.GetType()][i];
                    if (replacements.TryGetValue(id, out string path))
                        item.objectReferenceValue = AssetDatabase.LoadAssetAtPath<NomadFacilityDefinition>(path);
                    else if (old == null) throw new InvalidOperationException("缺少未授权替换的设施定义：" + id);
                }
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorSceneManager.MarkSceneDirty(sample);
            if (!EditorSceneManager.SaveScene(sample)) throw new InvalidOperationException("设施空间样板保存失败。");
            sample = EditorSceneManager.OpenScene(ScenePath);
            foreach (Component owner in Owners(sample))
            {
                var array = new SerializedObject(owner).FindProperty("facilityDefinitions");
                for (int i = 0; i < array.arraySize; i++)
                {
                    var definition = array.GetArrayElementAtIndex(i).objectReferenceValue as NomadFacilityDefinition;
                    if (definition == null || !EditorUtility.IsPersistent(definition))
                        throw new InvalidOperationException("场景回读丢失持久设施定义。");
                    definition.ValidateModelSpaceSnapshot();
                }
            }
            Debug.Log("设施空间变体已打开：站位由模型标记生成，维护托盘扩大并升高 0.10 米；待实际工作与读档验收。");
        }

        private static IEnumerable<Component> Owners(Scene scene) => scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Component>(true)).Where(c => c is NomadFoundationWorldView or NomadFoundationSystem);

        private static GameObject BuildVariant(string kind, NomadFacilityDefinition original)
        {
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/NW1_" + kind + ".prefab");
                if (source == null) throw new InvalidOperationException("缺少源模型：" + kind);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, preview);
                instance.name = "NW7_" + kind;
                var authoring = instance.AddComponent<FoundationFacilitySpaceAuthoring>();
                Transform markers = Node("静态空间标记", instance.transform, Vector3.zero, 0);
                FoundationFacilitySpaceSnapshot baseline = original.CaptureSpaceSnapshot();
                var parts = baseline.Footprints.Select((p, i) => new FoundationFacilitySpaceAuthoring.Footprint
                {
                    id = "body-" + i, sizeMeters = p.SizeMeters,
                    frame = Node("占地-" + i, markers, new Vector3(p.LocalCenterMeters.x, 0, p.LocalCenterMeters.y), p.LocalYawDegrees)
                }).ToArray();
                var groups = baseline.Groups.Select(g =>
                {
                    Transform groupRoot = Node(g.GroupId, markers, Vector3.zero, 0);
                    var group = groupRoot.gameObject.AddComponent<FacilityInteractionGroup>();
                    var slots = g.AlternativeSlots.Select(s =>
                    {
                        float shift = g.GroupId is "water-pickup" or "drink-and-deliver" ? -.06f : 0;
                        var node = Node(s.SlotId, groupRoot, new Vector3(s.LocalPositionMeters.x, 0, s.LocalPositionMeters.y + shift), s.LocalYawDegrees);
                        var slot = node.gameObject.AddComponent<FacilityInteractionSlot>();
                        slot.ConfigureRuntime(s.SlotId, ResidentAnimationSemantic.Pickup, null);
                        return slot;
                    }).ToArray();
                    group.ConfigureRuntime(g.GroupId, g.RequiredForOperation, slots);
                    return group;
                }).ToArray();
                var regions = baseline.Regions.Select(r =>
                {
                    bool tray = r.RegionId == "maintenance-tray";
                    float height = r.SupportHeightMeters + (tray ? .1f : 0);
                    Vector2 size = tray ? new Vector2(.6f, .4f) : r.SizeMeters;
                    var frame = Node(r.RegionId, markers, new Vector3(r.LocalCenterMeters.x, height, r.LocalCenterMeters.y), r.LocalYawDegrees);
                    if (tray) BuildTray(frame, size);
                    return new FoundationFacilitySpaceAuthoring.Region { id = r.RegionId, frame = frame, sizeMeters = size,
                        edgeInsetMeters = r.EdgeInsetMeters, acceptedCategories = r.AcceptedCategories.ToArray() };
                }).ToArray();
                authoring.ConfigureForEditor(parts, groups, regions);
                authoring.CreateSnapshot();
                GameObject result = PrefabUtility.SaveAsPrefabAsset(instance, Root + "/Prefabs/NW7_" + kind + ".prefab", out bool saved);
                if (!saved) throw new InvalidOperationException("空间变体 Prefab 保存失败。");
                return result;
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        private static Transform Node(string name, Transform parent, Vector3 position, float yaw)
        {
            var node = new GameObject(name).transform;
            node.SetParent(parent, false);
            node.SetLocalPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
            return node;
        }

        private static void BuildTray(Transform surface, Vector2 size)
        {
            // 这组可见托盘只用于空间接线实验；上表面和标记共用局部零高度。
            var material = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/NW1_Steel.mat");
            void Plate(string name, Vector3 p, Vector3 scale)
            {
                var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                plate.name = name; plate.transform.SetParent(surface, false);
                plate.transform.localPosition = p; plate.transform.localScale = scale;
                plate.GetComponent<Renderer>().sharedMaterial = material;
                Object.DestroyImmediate(plate.GetComponent<Collider>());
            }
            Plate("托盘底板", new Vector3(0, -.01f, 0), new Vector3(size.x, .02f, size.y));
            foreach (float sign in new[] { -1f, 1f })
            {
                Plate("托盘长边", new Vector3(0, .015f, sign * (size.y - .02f) * .5f), new Vector3(size.x, .03f, .02f));
                Plate("托盘短边", new Vector3(sign * (size.x - .02f) * .5f, .015f, 0), new Vector3(.02f, .03f, size.y));
                Plate("托盘支脚", new Vector3(sign * .22f, -.07f, 0), new Vector3(.025f, .1f, .24f));
            }
        }
    }
}
