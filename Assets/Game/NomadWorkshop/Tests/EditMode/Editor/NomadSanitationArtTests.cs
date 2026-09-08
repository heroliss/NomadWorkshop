using System;
using System.Linq;
using Game.NomadWorkshop.Foundation;
using NUnit.Framework;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Game.NomadWorkshop.Editor.Tests
{
    /// <summary>检查落盘旱厕的空间兼容、真实桶净空、独立座盖和共享图集导入。</summary>
    public sealed class NomadSanitationArtTests
    {
        private const string Root = NomadSanitationArtPipeline.Root;

        [Test]
        public void ToiletRetainsOriginalSpace_AndOnlyTheLidMoves()
        {
            var definition = AssetDatabase.LoadAssetAtPath<NomadFacilityDefinition>(NomadSanitationArtPipeline.DefinitionPath);
            var baseline = AssetDatabase.LoadAssetAtPath<NomadFacilityDefinition>(Root + "/Definitions/NW_Facility_Toilet.asset");
            Assert.DoesNotThrow(definition.ValidateModelSpaceSnapshot);
            Assert.That(definition.CaptureSpaceSnapshot().Signature, Is.EqualTo(baseline.CaptureSpaceSnapshot().Signature));
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(definition.Prefab, scene);
                Assert.That(root.GetComponentsInChildren<Collider>(true), Is.Empty);
                var renderers = root.GetComponentsInChildren<MeshRenderer>();
                foreach (var renderer in renderers)
                {
                    Assert.That(renderer.bounds.min.x, Is.GreaterThanOrEqualTo(-.4f));
                    Assert.That(renderer.bounds.max.x, Is.LessThanOrEqualTo(.4f));
                    Assert.That(renderer.bounds.min.z, Is.GreaterThanOrEqualTo(-.45f));
                    Assert.That(renderer.bounds.max.z, Is.LessThanOrEqualTo(.45f));
                }
                var lid = root.GetComponentsInChildren<Transform>().Single(t => t.name == "NW14_Toilet_SeatLid");
                var moving = lid.GetComponentsInChildren<MeshRenderer>().Single();
                var stationary = renderers.Where(r => !r.transform.IsChildOf(lid)).ToArray();
                var centers = stationary.Select(r => r.bounds.center).ToArray();
                Vector3 before = moving.bounds.center;
                lid.localRotation = Quaternion.identity;
                Assert.That(Vector3.Distance(before, moving.bounds.center), Is.GreaterThan(.2f));
                for (int i=0;i<stationary.Length;i++)
                    Assert.That(Vector3.Distance(centers[i],stationary[i].bounds.center), Is.LessThan(.0001f));
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        [Test]
        public void ActualDetachableBucketFitsTheImportedCavity_WhileSolidWallRejectsProbe()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            using var factory = new FoundationFacilityGrayboxFactory();
            try
            {
                var root = new GameObject("真实桶与导入外壳净空检查");
                SceneManager.MoveGameObjectToScene(root, scene);
                factory.Build(root.transform, AssetDatabase.LoadAssetAtPath<NomadFacilityDefinition>(NomadSanitationArtPipeline.DefinitionPath));
                var bucket = root.transform.Find("Detachable Waste Bucket");
                Assert.That(bucket, Is.Not.Null);
                var filters = root.GetComponentsInChildren<MeshFilter>().Where(f => !f.transform.IsChildOf(bucket)).ToArray();
                var triangles = new System.Collections.Generic.List<(Vector3 a, Vector3 b, Vector3 c)>();
                foreach (var filter in filters)
                {
                    using var data = MeshUtility.AcquireReadOnlyMeshData(filter.sharedMesh);
                    using var vertices = new NativeArray<Vector3>(data[0].vertexCount, Allocator.Temp);
                    data[0].GetVertices(vertices);
                    Vector3 At(int index) => filter.transform.TransformPoint(vertices[index]);
                    for (int i=0;i<filter.sharedMesh.subMeshCount;i++)
                    {
                        using var indices = new NativeArray<int>((int)filter.sharedMesh.GetIndexCount(i), Allocator.Temp);
                        data[0].GetIndices(indices,i);
                        for (int j=0;j<indices.Length;j+=3)
                            triangles.Add((At(indices[j]),At(indices[j+1]),At(indices[j+2])));
                    }
                }
                foreach (var part in bucket.GetComponentsInChildren<Renderer>())
                    Assert.That(triangles.Any(t=>Intersects(part.bounds,t)), Is.False, part.name+" 穿入了外壳、座体或底盘。");
                // Positive control: the same oracle must detect real solid side-wall geometry.
                var solidWall = new Bounds(new Vector3(-.36f,.27f,.1f), new Vector3(.09f,.2f,.2f));
                Assert.That(triangles.Any(t=>Intersects(solidWall,t)), Is.True, "净空探针必须能识别实体侧壁。");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        // Triangle/AABB separating axes inspect actual exported surfaces without relying on
        // PhysX cooking in an Editor preview scene. This is surface clearance, not navigation.
        private static bool Intersects(Bounds box, (Vector3 a, Vector3 b, Vector3 c) triangle)
        {
            Vector3 a=triangle.a-box.center,b=triangle.b-box.center,c=triangle.c-box.center;
            bool Separated(Vector3 axis)
            {
                float radius=Vector3.Dot(box.extents,new Vector3(Mathf.Abs(axis.x),Mathf.Abs(axis.y),Mathf.Abs(axis.z)));
                float x=Vector3.Dot(a,axis),y=Vector3.Dot(b,axis),z=Vector3.Dot(c,axis);
                return Mathf.Min(x,Mathf.Min(y,z))>radius+.000001f || Mathf.Max(x,Mathf.Max(y,z)) < -radius-.000001f;
            }
            var basis=new[]{Vector3.right,Vector3.up,Vector3.forward};
            if (basis.Any(Separated) || Separated(Vector3.Cross(b-a,c-a))) return false;
            foreach(var edge in new[]{b-a,c-b,a-c})
                foreach(var axis in basis) if(Separated(Vector3.Cross(edge,axis))) return false;
            return true;
        }

        [TestCase("NW9_Kitchen")]
        [TestCase("NW11_WaterCan")]
        [TestCase("NW14_Toilet")]
        public void SharedPropAtlasRetainsColourNormalAndSurfaceSemantics(string stem)
        {
            var atlas=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/"+stem+"Atlas.mat");
            Assert.That(atlas, Is.Not.Null); Assert.That(atlas.shader.name, Is.EqualTo("Universal Render Pipeline/Lit"));
            foreach (var channel in new[]{("_BaseMap","Color"),("_BumpMap","Normal"),("_MetallicGlossMap","Surface")})
            {
                var texture=atlas.GetTexture(channel.Item1);
                Assert.That(texture.name, Is.EqualTo(stem+"_"+channel.Item2));
                var importer=(TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture));
                Assert.That(importer.sRGBTexture, Is.EqualTo(channel.Item2=="Color"));
                Assert.That(importer.textureType, Is.EqualTo(channel.Item2=="Normal" ? TextureImporterType.NormalMap : TextureImporterType.Default));
                Assert.That(importer.alphaSource, Is.EqualTo(channel.Item2=="Color" ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput));
                Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.None));
                Assert.That(importer.isReadable, Is.False);
            }
            Assert.That(atlas.IsKeywordEnabled("_NORMALMAP"), Is.True);
            Assert.That(atlas.IsKeywordEnabled("_METALLICSPECGLOSSMAP"), Is.True);
        }
    }
}
