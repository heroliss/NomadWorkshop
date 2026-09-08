using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Game.NomadWorkshop.Foundation;
using Game.NomadWorkshop.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Game.NomadWorkshop.Editor
{
    /// <summary>导入旱厕自有模型，保留原空间/桶归属；更新两个可玩美术入口及默认镜头。</summary>
    public static class NomadSanitationArtPipeline
    {
        public const string Root = "Assets/Game/NomadWorkshop/ArtFirstPass";
        public const string PrefabPath = Root + "/Prefabs/NW14_Toilet.prefab";
        public const string DefinitionPath = Root + "/Definitions/NW14_Facility_Toilet.asset";
        private const string Source = "ArtPipelineOutput/Sanitation/v04";
        private const string Stem = "NW14_Toilet";
        private static readonly string[] Scenes = {
            "Assets/Game/NomadWorkshop/Scenes/ReferenceVehicleSample.unity",
            "Assets/Game/NomadWorkshop/Scenes/LocalDeckSupportSample.unity" };

#pragma warning disable CS0649
        [Serializable] private sealed class Manifest
        { public string version, status; public int atlasSize; public Record[] sources, files; public Asset[] assets; }
        [Serializable] private sealed class Record { public string file, sha256; }
        [Serializable] private sealed class Asset { public string name; public Geometry source, roundTrip; }
        [Serializable] private sealed class Geometry
        {
            public int triangles, meshCount, degenerateTriangles;
            public bool uvComplete;
            public float[] minimumBlender, maximumBlender;
        }
#pragma warning restore CS0649

        [MenuItem("Assets/SSFramework/游牧工坊/首版美术/导入旱厕并更新生活区镜头")]
        public static void ImportAndOpen()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("请在非 Play 且编译空闲时导入旱厕。");
            for (int i=0;i<SceneManager.sceneCount;i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("存在未保存场景。");
            foreach (string path in Scenes)
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path)==null) throw new InvalidOperationException("缺少已验证的美术入口："+path);
            string json=File.ReadAllText(Source+"/manifest.json");
            var manifest=JsonUtility.FromJson<Manifest>(json);
            if (manifest==null || manifest.version!="0.1.0" || manifest.status!="passed-export" ||
                manifest.atlasSize!=2048 || manifest.assets?.Length!=1 || manifest.assets[0].name!=Stem)
                throw new InvalidOperationException("旱厕导出证据不完整。");
            CheckFiles(manifest.sources,"Tools/ArtPipeline/Blender",new[]{
                "blender_nomad_sanitation.py","blender_nomad_kitchen.py","blender_nomad_water_facilities.py",
                "blender_nomad_vehicle_forms.py","blender_nomad_form_study.py","blender_nomad_art_set.py",
                "blender_nomad_deck_cockpit.py","blender_nomad_canopy.py"});
            CheckFiles(manifest.files,Source,new[]{Stem+".fbx",Stem+"_Color.png",Stem+"_Normal.png",Stem+"_Surface.png"});
            foreach (var g in new[]{manifest.assets[0].source,manifest.assets[0].roundTrip})
                if (g==null || g.triangles<=0 || g.triangles>18000 || g.meshCount!=6 ||
                    g.degenerateTriangles!=0 || !g.uvComplete || g.minimumBlender?.Length!=3 || g.maximumBlender?.Length!=3 ||
                    g.minimumBlender.Concat(g.maximumBlender).Any(v=>!float.IsFinite(v)))
                    throw new InvalidOperationException("旱厕网格、UV 或分件证据无效。");
            if (manifest.assets[0].source.triangles!=manifest.assets[0].roundTrip.triangles)
                throw new InvalidOperationException("旱厕 FBX 往返面数不一致。");

            var atlas=NomadBakedPropTextureImporter.Import(Root,Source,Stem);
            string modelPath=Root+"/Models/"+Stem+".fbx";
            File.Copy(Source+"/"+Stem+".fbx",modelPath,true);
            AssetDatabase.ImportAsset(modelPath,ImportAssetOptions.ForceSynchronousImport);
            var importer=(ModelImporter)AssetImporter.GetAtPath(modelPath);
            importer.animationType=ModelImporterAnimationType.None; importer.importAnimation=false;
            importer.importCameras=false; importer.importLights=false; importer.addCollider=false;
            importer.globalScale=1f; importer.bakeAxisConversion=true; importer.isReadable=false;
            importer.meshCompression=ModelImporterMeshCompression.Off;
            importer.importNormals=ModelImporterNormals.Import; importer.importTangents=ModelImporterTangents.CalculateMikk;
            importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation=ModelImporterMaterialLocation.InPrefab;
            var lamp=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/NW1_Lamp.mat");
            if (lamp==null) throw new InvalidOperationException("缺少共用暖灯材质。");
            foreach (var m in new[]{atlas,lamp})
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),m.name),m);
            importer.SaveAndReimport();
            var original=AssetDatabase.LoadAssetAtPath<NomadFacilityDefinition>(Root+"/Definitions/NW_Facility_Toilet.asset");
            var baseline=original.CaptureSpaceSnapshot();
            Scene preview=EditorSceneManager.NewPreviewScene();
            GameObject prefab;
            try
            {
                var root=new GameObject(Stem); SceneManager.MoveGameObjectToScene(root,preview);
                var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath),preview);
                model.transform.SetParent(root.transform,false);
                Audit(root,manifest.assets[0].source);
                Transform Marker(string suffix)=>root.GetComponentsInChildren<Transform>(true).Single(t=>t.name==Stem+suffix);
                foreach (var axis in new[]{("_AxisRight",Vector3.right),("_AxisUp",Vector3.up),("_AxisForward",Vector3.forward)})
                    if (Vector3.Distance(root.transform.InverseTransformPoint(Marker(axis.Item1).position),axis.Item2)>.001f)
                        throw new InvalidOperationException("旱厕导入轴向错误："+axis.Item1);
                var light=Marker("_TaskLamp").gameObject.AddComponent<Light>();
                light.type=LightType.Point; light.color=new Color(1f,.79f,.53f);
                light.intensity=.45f; light.range=.9f; light.shadows=LightShadows.None;
                var space=root.AddComponent<FoundationFacilitySpaceAuthoring>();
                var footprints=baseline.Footprints.Select((p,i)=>new FoundationFacilitySpaceAuthoring.Footprint{
                    id="body-"+i,sizeMeters=p.SizeMeters,
                    frame=Node("占地-"+i,root.transform,new Vector3(p.LocalCenterMeters.x,0,p.LocalCenterMeters.y),p.LocalYawDegrees)}).ToArray();
                var groups=baseline.Groups.Select(g=>{
                    var owner=Node(g.GroupId,root.transform,Vector3.zero,0);
                    var group=owner.gameObject.AddComponent<FacilityInteractionGroup>();
                    var slots=g.AlternativeSlots.Select(s=>{
                        var point=Node(s.SlotId,owner,new Vector3(s.LocalPositionMeters.x,0,s.LocalPositionMeters.y),s.LocalYawDegrees);
                        var slot=point.gameObject.AddComponent<FacilityInteractionSlot>();
                        slot.ConfigureRuntime(s.SlotId,ResidentAnimationSemantic.Work,null); return slot;
                    }).ToArray();
                    group.ConfigureRuntime(g.GroupId,g.RequiredForOperation,slots); return group;
                }).ToArray();
                space.ConfigureForEditor(footprints,groups,Array.Empty<FoundationFacilitySpaceAuthoring.Region>());
                if (space.CreateSnapshot().Signature!=baseline.Signature)
                    throw new InvalidOperationException("旱厕占地或工作位偏离旧定义。");
                prefab=PrefabUtility.SaveAsPrefabAsset(root,PrefabPath,out bool saved);
                if (!saved) throw new InvalidOperationException("旱厕 Prefab 保存失败。");
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
            if (AssetDatabase.LoadAssetAtPath<NomadFacilityDefinition>(DefinitionPath)==null &&
                !AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(original),DefinitionPath))
                throw new InvalidOperationException("旱厕定义复制失败。");
            var candidate=AssetDatabase.LoadAssetAtPath<NomadFacilityDefinition>(DefinitionPath);
            NomadFacilitySpaceBaker.Bake(candidate,prefab);
            AssetDatabase.SaveAssetIfDirty(candidate);
            File.WriteAllText(Root+"/NW14-source-manifest.json",json);
            AssetDatabase.ImportAsset(Root+"/NW14-source-manifest.json");
            foreach (string path in Scenes) UpdateScene(path);
            EditorSceneManager.OpenScene(Scenes[0]);
            Debug.Log("旱厕已接入参考车辆与局部甲板：保留原工作位、污物桶和存档空间，主镜头可见设施操作面。");
        }

        private static void UpdateScene(string path)
        {
            Scene scene=EditorSceneManager.OpenScene(path);
            // OpenScene can unload assets that the previous scene did not reference yet.
            var candidate=AssetDatabase.LoadAssetAtPath<NomadFacilityDefinition>(DefinitionPath);
            if (candidate==null) throw new InvalidOperationException("旱厕定义无法重新加载。");
            var owners=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Component>(true))
                .Where(c=>c is NomadFoundationSystem or NomadFoundationWorldView).ToArray();
            if (owners.Length!=2) throw new InvalidOperationException("美术入口须含唯一 System 和 World View。");
            foreach (var owner in owners)
            {
                var data=new SerializedObject(owner); var definitions=data.FindProperty("facilityDefinitions"); int found=0;
                for (int i=0;i<definitions.arraySize;i++)
                    if (definitions.GetArrayElementAtIndex(i).objectReferenceValue is NomadFacilityDefinition d && d.Id=="toilet")
                    { definitions.GetArrayElementAtIndex(i).objectReferenceValue=candidate; found++; }
                if (found!=1) throw new InvalidOperationException("美术入口须含唯一旱厕定义。");
                if (owner is NomadFoundationWorldView)
                    data.FindProperty("artCameraInitialPosition").vector3Value=new Vector3(9,15,-11);
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            var camera=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Camera>(true)).Single(c=>c.CompareTag("MainCamera"));
            camera.transform.position=new Vector3(9,15,-11); camera.transform.LookAt(new Vector3(0,.3f,0));
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("场景保存失败："+path);
            scene=EditorSceneManager.OpenScene(path);
            foreach (var owner in scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Component>(true))
                .Where(c=>c is NomadFoundationSystem or NomadFoundationWorldView))
            {
                var definitions=new SerializedObject(owner).FindProperty("facilityDefinitions");
                var matched=Enumerable.Range(0,definitions.arraySize).Select(i=>definitions.GetArrayElementAtIndex(i).objectReferenceValue)
                    .OfType<NomadFacilityDefinition>().Single(d=>d.Id=="toilet");
                if (AssetDatabase.GetAssetPath(matched)!=DefinitionPath) throw new InvalidOperationException("旱厕引用未持久化："+path);
                matched.ValidateModelSpaceSnapshot();
            }
        }

        private static Transform Node(string name, Transform parent, Vector3 position, float yaw)
        { var t=new GameObject(name).transform; t.SetParent(parent,false); t.SetLocalPositionAndRotation(position,Quaternion.Euler(0,yaw,0)); return t; }

        private static void CheckFiles(Record[] records,string folder,string[] required)
        {
            foreach (string file in required)
            {
                var matches=records?.Where(r=>r.file==file).ToArray();
                if (matches?.Length!=1 || !File.Exists(folder+"/"+file)) throw new InvalidOperationException("旱厕来源缺失："+file);
                using var sha=SHA256.Create();
                string hash=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(folder+"/"+file))).Replace("-","").ToLowerInvariant();
                if (hash!=matches[0].sha256) throw new InvalidOperationException("旱厕来源哈希变化："+file);
            }
        }

        private static void Audit(GameObject root, Geometry g)
        {
            var filters=root.GetComponentsInChildren<MeshFilter>(true);
            if (root.GetComponentsInChildren<Collider>(true).Length!=0 || filters.Length!=g.meshCount ||
                filters.Sum(f=>Enumerable.Range(0,f.sharedMesh.subMeshCount).Sum(i=>(int)f.sharedMesh.GetIndexCount(i)/3))!=g.triangles ||
                filters.Any(f=>!f.sharedMesh.HasVertexAttribute(VertexAttribute.TexCoord0)))
                throw new InvalidOperationException("旱厕 Unity 网格、UV 或 Collider 不符合导出。");
            var renderers=root.GetComponentsInChildren<MeshRenderer>(true); Bounds bounds=renderers[0].bounds;
            foreach (var r in renderers)
            {
                bounds.Encapsulate(r.bounds);
                if (r.sharedMaterials.Any(m=>m==null || m.shader.name!="Universal Render Pipeline/Lit"))
                    throw new InvalidOperationException("旱厕存在错误材质。");
            }
            Vector3 Convert(float[] p)=>new(p[0],p[2],p[1]);
            if (Vector3.Distance(bounds.min,Convert(g.minimumBlender))>.003f ||
                Vector3.Distance(bounds.max,Convert(g.maximumBlender))>.003f ||
                bounds.min.x<-.4f || bounds.max.x>.4f || bounds.min.z<-.45f || bounds.max.z>.45f)
                throw new InvalidOperationException("旱厕尺度或可见占地错误。");
        }
    }
}
