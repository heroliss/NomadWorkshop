using System;
using System.Collections.Generic;
using System.Linq;
using Game.NomadWorkshop.Foundation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.NomadWorkshop.Editor
{
    /// <summary>用既有水设施制作真实几何/绑定变体，验证模型接线；不更改原站位、区域或源模型。</summary>
    public static class NomadFacilityBindingPipeline
    {
        public const string ScenePath = "Assets/Game/NomadWorkshop/Scenes/FacilityBindingSample.unity";
        private const string Root = NomadWarmWorkshopArtPipeline.Root;

        [MenuItem("Assets/SSFramework/游牧工坊/首版美术/生成并打开设施绑定变体")]
        public static void GenerateAndOpen()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("请在非 Play 且编译空闲时生成设施绑定变体。");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("存在未保存场景，请先保存。");
            var replacements = new Dictionary<string, string>();
            foreach (string kind in new[] { "WaterTank", "Dispenser" })
            {
                GameObject prefab = BuildVariant(kind);
                string suffix = kind == "WaterTank" ? "VehicleWaterTank" : "DrinkingStation";
                string original = Root + "/Definitions/NW_Facility_" + suffix + ".asset";
                string path = Root + "/Definitions/NW4_Facility_" + suffix + ".asset";
                if (AssetDatabase.LoadAssetAtPath<NomadFacilityDefinition>(path) == null && !AssetDatabase.CopyAsset(original, path))
                    throw new InvalidOperationException("设施变体定义创建失败：" + path);
                var definition = AssetDatabase.LoadAssetAtPath<NomadFacilityDefinition>(path);
                var data = new SerializedObject(definition);
                data.FindProperty("prefab").objectReferenceValue = prefab;
                data.ApplyModifiedPropertiesWithoutUndo();
                prefab.GetComponent<FoundationFacilityArtRig>().ValidateAgainst(definition);
                AssetDatabase.SaveAssetIfDirty(definition);
                replacements.Add(definition.Id, path);
            }
            // Single 打开场景可能卸载未被场景引用的资产。跨场景保留路径/id，接线时重新加载持久对象。
            Scene sourceScene = EditorSceneManager.OpenScene(NomadResidentCrewPipeline.ScenePath);
            var sourceIds = new Dictionary<Type, string[]>();
            foreach (Component owner in Owners(sourceScene))
            {
                var array = new SerializedObject(owner).FindProperty("facilityDefinitions");
                sourceIds.Add(owner.GetType(), Enumerable.Range(0, array.arraySize).Select(i =>
                    ((NomadFacilityDefinition)array.GetArrayElementAtIndex(i).objectReferenceValue).Id).ToArray());
            }
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                if (!EditorSceneManager.SaveScene(sourceScene, ScenePath, true)) throw new InvalidOperationException("设施变体场景副本保存失败。");
            }
            Scene sample = EditorSceneManager.OpenScene(ScenePath);
            foreach (Component owner in Owners(sample))
            {
                var data = new SerializedObject(owner);
                SerializedProperty definitions = data.FindProperty("facilityDefinitions");
                for (int i = 0; i < definitions.arraySize; i++)
                {
                    var item = definitions.GetArrayElementAtIndex(i);
                    var old = (NomadFacilityDefinition)item.objectReferenceValue;
                    string id = old != null ? old.Id : sourceIds[owner.GetType()].Length == definitions.arraySize
                        ? sourceIds[owner.GetType()][i] : throw new InvalidOperationException("变体定义缺失且与来源顺序不一致，无法恢复引用。");
                    if (replacements.TryGetValue(id, out string path))
                    {
                        var replacement = AssetDatabase.LoadAssetAtPath<NomadFacilityDefinition>(path);
                        if (replacement == null || !EditorUtility.IsPersistent(replacement))
                            throw new InvalidOperationException("设施变体不是有效持久资产：" + path);
                        item.objectReferenceValue = replacement;
                    }
                    else if (old == null) throw new InvalidOperationException("变体缺少未授权替换的设施：" + id);
                }
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorSceneManager.MarkSceneDirty(sample);
            if (!EditorSceneManager.SaveScene(sample)) throw new InvalidOperationException("设施变体场景保存失败。");
            sample = EditorSceneManager.OpenScene(ScenePath);
            foreach (Component owner in Owners(sample))
            {
                var array = new SerializedObject(owner).FindProperty("facilityDefinitions");
                for (int i = 0; i < array.arraySize; i++)
                {
                    var definition = array.GetArrayElementAtIndex(i).objectReferenceValue as NomadFacilityDefinition;
                    if (definition == null) throw new InvalidOperationException("设施变体落盘回读丢失定义：" + owner.GetType().Name + "/" + i);
                    if (definition.Prefab != null)
                        foreach (var rig in definition.Prefab.GetComponentsInChildren<FoundationFacilityArtRig>(true)) rig.ValidateAgainst(definition);
                }
            }
            Debug.Log("设施绑定变体已打开：接口位移、反向阀门、侧开盖及附加点；站位/区域沿用原定义，仍需真实工作验收。");
        }

        private static IEnumerable<Component> Owners(Scene scene) => scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Component>(true)).Where(c => c is NomadFoundationWorldView or NomadFoundationSystem);

        private static GameObject BuildVariant(string kind)
        {
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/NW1_" + kind + ".prefab");
                if (source == null) throw new InvalidOperationException("缺少基础水设施：" + kind);
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(source, preview);
                instance.name = "NW4_" + kind;
                var rig = instance.GetComponent<FoundationFacilityArtRig>();
                FoundationFacilityWorkBinding[] original = rig.Bindings.ToArray();
                void Move(Transform node, Vector3 offset) => node.position += instance.transform.TransformVector(offset);
                bool tank = kind == "WaterTank";
                if (tank)
                {
                    Vector3 outletShift = new(.16f, 0f, 0f);
                    Transform outlet = original.Single(b => b.WaterFlow == FoundationFacilityWaterFlow.FillCan).Contacts.Single().Target;
                    Vector3 sourceOutlet = instance.transform.InverseTransformPoint(outlet.position);
                    Move(outlet, outletShift);
                    AddConnector(instance.transform, sourceOutlet, sourceOutlet + outletShift, .054f);
                    Vector3 inletShift = new(-.22f, 0f, 0f);
                    Vector3 sourceInlet = instance.transform.InverseTransformPoint(rig.Inlet.position);
                    Move(rig.Inlet, inletShift); Move(rig.FillLid, inletShift);
                    AddConnector(instance.transform, sourceInlet, sourceInlet + inletShift, .22f);
                }
                else
                {
                    Vector3 shift = new(.11f, .04f, 0f);
                    Vector3 sourceInlet = instance.transform.InverseTransformPoint(rig.Inlet.position);
                    Move(rig.Inlet, shift);
                    AddConnector(instance.transform, sourceInlet, sourceInlet + shift, .22f);
                    // 换真实铰链位置，同时保持盖板静止几何不变。导入子树可能含非单位旋转/缩放。
                    var children = rig.FillLid.Cast<Transform>().ToArray();
                    Vector3[] positions = children.Select(c => c.position).ToArray();
                    rig.FillLid.position = instance.transform.TransformPoint(new Vector3(-.40f, .81f, 0f));
                    for (int i = 0; i < children.Length; i++) children[i].position = positions[i];
                }
                var optional = new GameObject("Optional effect marker").transform;
                optional.SetParent(instance.transform, false); optional.localPosition = new Vector3(.2f, 1.1f, .1f);
                var bindings = new List<FoundationFacilityWorkBinding>();
                foreach (var binding in original)
                {
                    var contacts = binding.Contacts.ToList();
                    contacts.Add(new FoundationFacilityContactBinding("optional-effect", FoundationFacilityContactRole.EffectOrigin, optional));
                    var motions = new List<FoundationFacilityMotionBinding>();
                    foreach (var motion in binding.Motions)
                    {
                        float amount = motion.Amount;
                        Vector3 axis = motion.LocalAxis;
                        if (motion.Id == "valve") amount = -95f;
                        if (motion.Id == "service-door") amount *= .58f / .52f;
                        if (!tank && motion.Id == "fill-lid") axis = motion.Target.InverseTransformDirection(instance.transform.forward);
                        motions.Add(new FoundationFacilityMotionBinding(motion.Id, motion.Target, motion.Kind, axis, amount));
                    }
                    bindings.Add(new FoundationFacilityWorkBinding(binding.Id, binding.GroupId, binding.SlotId, binding.Phase,
                        contacts.ToArray(), motions.ToArray(), binding.WaterFlow, binding.WaterEndpointId));
                }
                rig.ConfigureBindings(bindings.ToArray());
                GameObject result = PrefabUtility.SaveAsPrefabAsset(instance, Root + "/Prefabs/NW4_" + kind + ".prefab", out bool saved);
                if (!saved) throw new InvalidOperationException("设施变体 Prefab 保存失败。");
                return result;
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        private static void AddConnector(Transform root, Vector3 from, Vector3 to, float diameter)
        {
            GameObject pipe = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pipe.name = "Port extension connector";
            pipe.transform.SetParent(root, false);
            pipe.transform.localPosition = (from + to) * .5f;
            pipe.transform.localRotation = Quaternion.FromToRotation(Vector3.up, to - from);
            pipe.transform.localScale = new Vector3(diameter, Vector3.Distance(from, to) * .5f, diameter);
            pipe.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/NW1_Steel.mat");
            UnityEngine.Object.DestroyImmediate(pipe.GetComponent<Collider>());
        }
    }
}
