using System;
using System.Linq;
using Game.NomadWorkshop.Foundation;
using NUnit.Framework;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.NomadWorkshop.Editor.Tests
{
    /// <summary>容器绑定必须对应真实握持面、开孔和分件，不能仅靠坐标配置自证正确。</summary>
    public sealed class NomadCarriedContainerArtTests
    {
        private GameObject _root;
        private FoundationCarriedContainerRig _rig;
        [SetUp] public void SetUp()
        {
            _root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(NomadWaterCanArtPipeline.PrefabPath));
            _rig = _root.GetComponent<FoundationCarriedContainerRig>();
        }
        [TearDown] public void TearDown() { if (_root != null) Object.DestroyImmediate(_root); }

        [Test]
        public void ImportedContainerFitsSavedItemSpace_AndUsesThreeBoundAtlasChannels()
        {
            Assert.DoesNotThrow(_rig.ValidateBindings);
            Assert.That(_root.GetComponentsInChildren<Collider>(true), Is.Empty);
            var definition = AssetDatabase.LoadAssetAtPath<NomadWorldItemDefinition>(
                "Assets/Game/NomadWorkshop/Foundation/Definitions/NW_Item_WaterCan.asset");
            var filters = _root.GetComponentsInChildren<MeshFilter>(true);
            Assert.That(filters.Length, Is.EqualTo(3), "静态罐体、独立封盖和液位显示。");
            foreach (var renderer in _root.GetComponentsInChildren<MeshRenderer>(true))
            {
                Assert.That(renderer.bounds.min.y, Is.GreaterThanOrEqualTo(-.001f));
                Assert.That(renderer.bounds.max.y, Is.LessThanOrEqualTo(definition.HeightMeters));
                Assert.That(Mathf.Max(Mathf.Abs(renderer.bounds.min.x), Mathf.Abs(renderer.bounds.max.x)),
                    Is.LessThanOrEqualTo(definition.FootprintSizeMeters.x/2));
                Assert.That(Mathf.Max(Mathf.Abs(renderer.bounds.min.z), Mathf.Abs(renderer.bounds.max.z)),
                    Is.LessThanOrEqualTo(definition.FootprintSizeMeters.y/2));
                Assert.That(renderer.sharedMaterial.shader.name, Is.EqualTo("Universal Render Pipeline/Lit"));
                foreach (var channel in new[] { ("_BaseMap","Color"), ("_BumpMap","Normal"), ("_MetallicGlossMap","Surface") })
                {
                    var texture = renderer.sharedMaterial.GetTexture(channel.Item1);
                    Assert.That(texture.name, Is.EqualTo("NW11_WaterCan_" + channel.Item2));
                    var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture));
                    Assert.That(importer.sRGBTexture, Is.EqualTo(channel.Item2 == "Color"));
                    Assert.That(importer.textureType, Is.EqualTo(channel.Item2 == "Normal" ? TextureImporterType.NormalMap : TextureImporterType.Default));
                }
            }
        }

        [Test]
        public void PalmTouchesActualHandle_AndRemovingCapExposesARealRecessedOpening()
        {
            _root.transform.SetPositionAndRotation(new Vector3(4,2,-7), Quaternion.Euler(0,43,0));
            Vector3 up = _root.transform.up;
            Assert.That(RayDistance(_rig.RightPalm.position + up*.08f, -up), Is.EqualTo(.08f).Within(.002f),
                "掌心应落在可见把手上表面。");
            Vector3 mouth = _rig.Opening.position;
            float closed = RayDistance(mouth + up*.1f, -up);
            var stationary = _root.GetComponentsInChildren<MeshRenderer>().Where(r => !r.transform.IsChildOf(_rig.Closure)).ToArray();
            Vector3[] centers = stationary.Select(r => r.bounds.center).ToArray();
            _rig.Closure.gameObject.SetActive(false);
            float opened = RayDistance(mouth + up*.1f, -up);
            Assert.That(closed, Is.LessThan(.1f), "封盖应实际挡住罐口。");
            Assert.That(opened, Is.GreaterThan(.13f).And.LessThan(.20f), "开口应进入罐内，不能仅隐藏贴在实心顶板上的瓶盖。");
            for (int i=0;i<stationary.Length;i++)
            {
                Assert.That(stationary[i].gameObject.activeInHierarchy, Is.True);
                Assert.That(Vector3.Distance(stationary[i].bounds.center, centers[i]), Is.LessThan(.0001f));
            }
        }

        [Test]
        public void BindingSurvivesHierarchyRenaming_AndDiffersFromLegacyGripMouthAndVolumeCount()
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            Transform old = null;
            try
            {
                old = FoundationWaterCanVisualFactory.Create(null,mat,mat,mat,out _);
                var legacy = old.GetComponent<FoundationCarriedContainerRig>();
                Assert.That(Vector3.Distance(_rig.CarryPivotLocalPosition, legacy.CarryPivotLocalPosition), Is.GreaterThan(.05f));
                Assert.That(Vector3.Distance(_rig.OpeningLocalPosition, legacy.OpeningLocalPosition), Is.GreaterThan(.05f));
                Assert.That(_rig.ClearanceVolumes.Count, Is.EqualTo(2));
                Assert.That(legacy.ClearanceVolumes.Count, Is.EqualTo(1));
                Vector3 palm = _rig.RightPalm.position, mouth = _rig.Opening.position;
                int index=0;
                foreach (var node in _root.GetComponentsInChildren<Transform>(true)) node.name="模型部件-"+index++;
                Assert.DoesNotThrow(_rig.ValidateBindings);
                Assert.That(_rig.RightPalm.position, Is.EqualTo(palm));
                Assert.That(_rig.Opening.position, Is.EqualTo(mouth));
            }
            finally { if(old!=null) Object.DestroyImmediate(old.gameObject); Object.DestroyImmediate(mat); }
        }

        [Test]
        public void InvalidVolumeOrExternalContact_IsRejectedBeforeRuntimeUse()
        {
            var external = new GameObject("外部握点");
            try
            {
                Assert.Throws<InvalidOperationException>(() => _rig.Configure(_rig.CarryPivot,external.transform,
                    _rig.Opening,_rig.Closure,_rig.FillIndicator,_rig.ClearanceVolumes.ToArray(),_rig.GroundReachOffset));
                Assert.Throws<InvalidOperationException>(() => _rig.Configure(_rig.CarryPivot,_root.transform,
                    _rig.Opening,_rig.Closure,_rig.FillIndicator,
                    new[] {new FoundationCarriedContainerRig.ClearanceVolume(_root.transform,new Bounds(Vector3.zero,Vector3.zero))},
                    new Vector3(0,-.6f,.2f)));
            }
            finally { Object.DestroyImmediate(external); }
        }

        private float RayDistance(Vector3 origin, Vector3 direction)
        {
            float nearest = float.PositiveInfinity;
            foreach (var filter in _root.GetComponentsInChildren<MeshFilter>())
            {
                using var meshData=MeshUtility.AcquireReadOnlyMeshData(filter.sharedMesh);
                var data=meshData[0];
                using var vertices=new NativeArray<Vector3>(data.vertexCount,Allocator.Temp); data.GetVertices(vertices);
                for(int sub=0;sub<filter.sharedMesh.subMeshCount;sub++)
                {
                    using var indices=new NativeArray<int>((int)filter.sharedMesh.GetIndexCount(sub),Allocator.Temp);
                    data.GetIndices(indices,sub);
                    for(int i=0;i<indices.Length;i+=3)
                    {
                        Vector3 a=filter.transform.TransformPoint(vertices[indices[i]]);
                        Vector3 b=filter.transform.TransformPoint(vertices[indices[i+1]]);
                        Vector3 c=filter.transform.TransformPoint(vertices[indices[i+2]]);
                        Vector3 ab=b-a, ac=c-a, p=Vector3.Cross(direction,ac);
                        float determinant=Vector3.Dot(ab,p);
                        if(Mathf.Abs(determinant)<1e-9f) continue;
                        float inverse=1f/determinant;
                        Vector3 delta=origin-a;
                        float u=Vector3.Dot(delta,p)*inverse;
                        Vector3 q=Vector3.Cross(delta,ab);
                        float v=Vector3.Dot(direction,q)*inverse, t=Vector3.Dot(ac,q)*inverse;
                        if(u>=0f && v>=0f && u+v<=1f && t>=0f) nearest=Mathf.Min(nearest,t);
                    }
                }
            }
            return nearest;
        }
    }
}
