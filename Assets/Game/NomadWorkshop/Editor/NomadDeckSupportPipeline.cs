using System;
using System.Linq;
using Game.NomadWorkshop.Foundation;
using Game.NomadWorkshop.Navigation;
using Game.NomadWorkshop.Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.NomadWorkshop.Editor
{
    /// <summary>在独立参考车辆副本中验证局部楼板规则与真实地板；不更改基础场景和起步布局。</summary>
    public static class NomadDeckSupportPipeline
    {
        public const string ScenePath = "Assets/Game/NomadWorkshop/Scenes/LocalDeckSupportSample.unity";
        private const string Art = "Assets/Game/NomadWorkshop/ArtFirstPass";

        [MenuItem("Assets/SSFramework/游牧工坊/首版美术/创建局部甲板覆盖样板")]
        public static void Create()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("请在非 Play 且编译空闲时创建局部甲板样板。");
            for (var i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("请先保存脏场景。");
            Scene scene = EditorSceneManager.OpenScene("Assets/Game/NomadWorkshop/Scenes/ReferenceVehicleSample.unity");
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("无法建立独立样板场景。");
            var view = UnityEngine.Object.FindFirstObjectByType<NomadFoundationWorldView>();
            var viewData = new SerializedObject(view);
            var original = (DeckLayoutDefinition)viewData.FindProperty("deckLayout").objectReferenceValue;
            var layout = UnityEngine.Object.Instantiate(original);
            DeckBounds bounds = original.CreateBounds();
            layout.ConfigureSupportForTests(new[] {
                new RectInt(bounds.MinXMillimeters,bounds.MinZMillimeters,
                    bounds.MaxXMillimeters-bounds.MinXMillimeters,bounds.MaxZMillimeters-bounds.MinZMillimeters),
                new RectInt(bounds.MinXMillimeters-3600,-2400,3600,4800)
            }, new[] { new RectInt(bounds.MinXMillimeters-2600,-600,800,1200) });
            layout = SaveAsset(layout, Art + "/Definitions/NW13_DeckSupport.asset");
            viewData.FindProperty("deckLayout").objectReferenceValue = layout;
            string vehiclePath = AssetDatabase.GetAssetPath(viewData.FindProperty("vehicleVisualPrefab").objectReferenceValue);
            GameObject vehicle = PrefabUtility.LoadPrefabContents(vehiclePath);
            try
            {
                Transform panel = vehicle.GetComponentsInChildren<Transform>(true).Single(t => t.name == "NW5_ShellPanel_2");
                panel.gameObject.SetActive(false);
                var candidateVehicle = PrefabUtility.SaveAsPrefabAsset(vehicle, Art + "/Prefabs/NW13_Vehicle.prefab");
                if (candidateVehicle == null) throw new InvalidOperationException("局部甲板车体副本保存失败。");
                viewData.FindProperty("vehicleVisualPrefab").objectReferenceValue = candidateVehicle;
            }
            finally { PrefabUtility.UnloadPrefabContents(vehicle); }
            viewData.ApplyModifiedPropertiesWithoutUndo();
            var system = UnityEngine.Object.FindFirstObjectByType<NomadFoundationSystem>();
            var systemData = new SerializedObject(system);
            systemData.FindProperty("deckLayout").objectReferenceValue = layout;
            systemData.ApplyModifiedPropertiesWithoutUndo();

            var nav = UnityEngine.Object.FindFirstObjectByType<DeckNavigationUtility>();
            Transform floor = nav.transform.Find("Nav Source · Walkable Deck");
            if (floor == null) throw new InvalidOperationException("参考场景缺少明确的旧甲板碰撞源。");
            UnityEngine.Object.DestroyImmediate(floor.gameObject);
            var mesh = SaveAsset(FoundationDeckSupportSurface.CreateMesh(layout.CreateSupportRegion()), Art + "/Models/NW13_DeckSupport.asset");
            var surface = new GameObject("Nav Source · Local Deck Support");
            surface.transform.SetParent(nav.transform, false);
            surface.AddComponent<MeshFilter>().sharedMesh = SaveAsset(FoundationDeckSupportSurface.CreateExtensionMesh(layout),
                Art + "/Models/NW13_DeckExtension.asset");
            surface.AddComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Art + "/Materials/NW1_Deck.mat");
            surface.AddComponent<MeshCollider>().sharedMesh = mesh;
            var adapter = surface.AddComponent<FoundationDeckSupportSurface>();
            var adapterData = new SerializedObject(adapter);
            adapterData.FindProperty("layout").objectReferenceValue = layout;
            adapterData.ApplyModifiedPropertiesWithoutUndo();

            // 两根可见托梁连接到旧车架内部；几何在楼板下方，不添加新的导航支撑面。
            var beams = new DeckSupportRegion(new[] {
                new DeckBounds(bounds.MinXMillimeters-3600,-1890,bounds.MinXMillimeters+600,-1710),
                new DeckBounds(bounds.MinXMillimeters-3600,1710,bounds.MinXMillimeters+600,1890) });
            Mesh beamMesh = SaveAsset(FoundationDeckSupportSurface.CreateMesh(beams,.25f), Art + "/Models/NW13_CantileverBeams.asset");
            var beamObject = new GameObject("局部外挑托梁");
            beamObject.transform.SetParent(nav.transform,false);
            beamObject.transform.localPosition = Vector3.down * .3f;
            beamObject.AddComponent<MeshFilter>().sharedMesh = beamMesh;
            beamObject.AddComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Art + "/Materials/NW1_Frame.mat");
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("局部甲板样板保存失败。");
            Debug.Log("[Nomad.Art] 局部甲板覆盖样板已保存；这是静态结构样板，尚未包含材料施工和结构存档。");
        }

        private static T SaveAsset<T>(T value, string path) where T : UnityEngine.Object
        {
            T existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null) { AssetDatabase.CreateAsset(value,path); return value; }
            EditorUtility.CopySerialized(value,existing);
            UnityEngine.Object.DestroyImmediate(value);
            EditorUtility.SetDirty(existing);
            return existing;
        }
    }
}
