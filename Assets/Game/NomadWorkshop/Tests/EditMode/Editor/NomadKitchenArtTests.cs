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
    /// <summary>检查落盘厨房的真实支撑三角形、可动分件及原空间兼容。</summary>
    public sealed class NomadKitchenArtTests
    {
        [Test]
        public void KitchenRetainsLegacySpace_AndAllClosedGeometryFitsFootprint()
        {
            var definition = AssetDatabase.LoadAssetAtPath<NomadFacilityDefinition>(NomadKitchenArtPipeline.DefinitionPath);
            var old = AssetDatabase.LoadAssetAtPath<NomadFacilityDefinition>(NomadKitchenArtPipeline.Root + "/Definitions/NW_Facility_FieldKitchen.asset");
            Assert.DoesNotThrow(definition.ValidateModelSpaceSnapshot);
            Assert.That(definition.CaptureSpaceSnapshot().Signature, Is.EqualTo(old.CaptureSpaceSnapshot().Signature));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NomadKitchenArtPipeline.PrefabPath);
            Assert.That(prefab.GetComponentsInChildren<Collider>(true), Is.Empty);
            foreach (var renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
            {
                Assert.That(renderer.bounds.min.x, Is.GreaterThanOrEqualTo(-1.05f - .001f), renderer.name);
                Assert.That(renderer.bounds.max.x, Is.LessThanOrEqualTo(1.05f + .001f), renderer.name);
                Assert.That(renderer.bounds.min.z, Is.GreaterThanOrEqualTo(-.475f - .001f), renderer.name);
                Assert.That(renderer.bounds.max.z, Is.LessThanOrEqualTo(.475f + .001f), renderer.name);
                Assert.That(renderer.sharedMaterial.shader.name, Is.EqualTo("Universal Render Pipeline/Lit"));
                Assert.That(renderer.sharedMaterial.name, Is.EqualTo("NW9_KitchenAtlas").Or.EqualTo("NW1_Lamp"));
            }
            var atlas = AssetDatabase.LoadAssetAtPath<Material>(NomadKitchenArtPipeline.Root + "/Materials/NW9_KitchenAtlas.mat");
            foreach (var channel in new[] { ("_BaseMap","Color"), ("_BumpMap","Normal"), ("_MetallicGlossMap","Surface") })
            {
                var texture = atlas.GetTexture(channel.Item1);
                Assert.That(texture.name, Is.EqualTo("NW9_Kitchen_" + channel.Item2));
                var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture));
                Assert.That(importer.sRGBTexture, Is.EqualTo(channel.Item2 == "Color"));
                Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.None));
                Assert.That(new SerializedObject(importer).FindProperty("m_InternalIDToNameTable").arraySize, Is.Zero);
            }
        }

        [Test]
        public void RealCountertopSupportsRegion_AfterMovingRoot_AndDoorsRotateIndependently()
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(NomadKitchenArtPipeline.PrefabPath));
            try
            {
                root.transform.SetPositionAndRotation(new Vector3(7,3,-5), Quaternion.Euler(0,37,0));
                var space = root.GetComponent<FoundationFacilitySpaceAuthoring>();
                var frame = space.PlacementRegions.Single().frame;
                Assert.That(Vector3.Distance(root.transform.InverseTransformPoint(frame.position), new Vector3(0,.97f,-.08f)), Is.LessThan(.0005f));
                Assert.That(Vector3.Dot(frame.up, Vector3.up), Is.GreaterThan(.99999f));
                var filter = frame.parent.GetComponentsInChildren<MeshFilter>().Single();
                using var meshData = MeshUtility.AcquireReadOnlyMeshData(filter.sharedMesh);
                var data = meshData[0];
                using var vertices = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp); data.GetVertices(vertices);
                using var indices = new NativeArray<int>((int)filter.sharedMesh.GetIndexCount(0), Allocator.Temp); data.GetIndices(indices,0);
                var toFrame = frame.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                Vector3 At(int index) => toFrame.MultiplyPoint3x4(vertices[indices[index]]);
                foreach (var point in new[] {new Vector2(-.205f,-.205f),new Vector2(-.205f,.205f),new Vector2(.205f,-.205f),new Vector2(.205f,.205f),Vector2.zero})
                {
                    bool supported = false;
                    for (int i=0;i<indices.Length;i+=3)
                    {
                        var a=At(i); var b=At(i+1); var c=At(i+2);
                        if (Mathf.Abs(a.y)>.0005f || Mathf.Abs(b.y)>.0005f || Mathf.Abs(c.y)>.0005f ||
                            Vector3.Cross(b-a,c-a).y<=0) continue;
                        float Cross(Vector2 v,Vector2 w)=>v.x*w.y-v.y*w.x;
                        var aa=new Vector2(a.x,a.z); var bb=new Vector2(b.x,b.z); var cc=new Vector2(c.x,c.z);
                        float ab=Cross(bb-aa,point-aa), bc=Cross(cc-bb,point-bb), ca=Cross(aa-cc,point-cc);
                        if ((ab>=-.000001f && bc>=-.000001f && ca>=-.000001f) || (ab<=.000001f && bc<=.000001f && ca<=.000001f))
                        { supported=true; break; }
                    }
                    Assert.That(supported,Is.True,"可用台面必须有真正的平面三角形：" + point);
                }
                var doors = root.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("NW9_Kitchen_Door_",StringComparison.Ordinal) && !t.name.EndsWith("_Mesh",StringComparison.Ordinal)).ToArray();
                Assert.That(doors.Length,Is.EqualTo(3));
                var all = root.GetComponentsInChildren<MeshRenderer>();
                foreach (var door in doors)
                {
                    var moving=door.GetComponentsInChildren<MeshRenderer>();
                    Assert.That(moving.Length,Is.EqualTo(1));
                    Assert.That(doors.Any(d=>d!=door && d.IsChildOf(door)),Is.False);
                    var rest=door.rotation;
                    var stationary=all.Where(r=>!r.transform.IsChildOf(door)).ToArray();
                    var centers=stationary.Select(r=>r.bounds.center).ToArray();
                    var movingCenter=moving[0].bounds.center;
                    door.rotation=Quaternion.AngleAxis(70,root.transform.up)*rest;
                    Assert.That(Vector3.Distance(moving[0].bounds.center,movingCenter),Is.GreaterThan(.1f));
                    for (int i=0;i<stationary.Length;i++) Assert.That(Vector3.Distance(centers[i],stationary[i].bounds.center),Is.LessThan(.0001f));
                    door.rotation=rest;
                }
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
