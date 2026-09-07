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
    /// <summary>检查真正导入的分件和表面；不从源脚本参数推断 FBX 或支撑面已经正确。</summary>
    public sealed class NomadWaterFacilityArtTests
    {
        private const string Root = "Assets/Game/NomadWorkshop/ArtFirstPass/";

        [TestCase("WaterTank", "VehicleWaterTank", 3)]
        [TestCase("Dispenser", "DrinkingStation", 2)]
        public void BakedMeshesRetainIndependentMovingPartsAndSharedAtlas(string kind, string suffix, int movingParts)
        {
            var definition = AssetDatabase.LoadAssetAtPath<NomadFacilityDefinition>(Root + "Definitions/NW8_Facility_" + suffix + ".asset");
            Assert.That(definition, Is.Not.Null);
            Assert.DoesNotThrow(definition.ValidateModelSpaceSnapshot);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/NW8_" + kind + ".prefab");
            var rig = prefab.GetComponent<FoundationFacilityArtRig>();
            Assert.DoesNotThrow(() => rig.ValidateAgainst(definition));
            var motions = rig.Bindings.SelectMany(b => b.Motions).Select(m => m.Target).Distinct().ToArray();
            Assert.That(motions.Length, Is.EqualTo(movingParts));
            var meshes = prefab.GetComponentsInChildren<MeshFilter>(true);
            foreach (Transform motion in motions)
            {
                Assert.That(motion.GetComponentsInChildren<MeshFilter>(true).Length, Is.EqualTo(1),
                    "活动件烘焙后必须仍拥有自己的网格：" + motion.name);
                Assert.That(motions.Where(t => t != motion).Any(t => t.IsChildOf(motion)), Is.False);
                Assert.That(meshes.Any(m => !m.transform.IsChildOf(motion)), Is.True, "活动件不能包住整台设施。");
            }
            var atlas = AssetDatabase.LoadAssetAtPath<Material>(Root + "Materials/NW8_WaterFacilitiesAtlas.mat");
            foreach (var mesh in meshes)
            {
                var material = mesh.GetComponent<Renderer>().sharedMaterial;
                Assert.That(material == atlas || material.name == "NW1_Lamp", Is.True);
            }
            foreach (var channel in new[] { ("_BaseMap", "Color"), ("_BumpMap", "Normal"), ("_MetallicGlossMap", "Surface") })
            {
                Assert.That(atlas.GetTexture(channel.Item1).name, Is.EqualTo("NW8_WaterFacilities_" + channel.Item2));
                var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(atlas.GetTexture(channel.Item1)));
                Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.None));
                Assert.That(new SerializedObject(importer).FindProperty("m_InternalIDToNameTable").arraySize, Is.Zero);
                Assert.That(importer.sRGBTexture, Is.EqualTo(channel.Item2 == "Color"));
            }
            Assert.That(prefab.GetComponentsInChildren<Collider>().Length, Is.Zero);
        }

        [Test]
        public void TrayFrameMatchesBakedTopFaces_AfterWorldTranslationAndRotation()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/NW8_WaterTank.prefab");
            var instance = Object.Instantiate(prefab);
            try
            {
                var authoring = instance.GetComponent<FoundationFacilitySpaceAuthoring>();
                var initial = authoring.CreateSnapshot();
                instance.transform.SetPositionAndRotation(new Vector3(11, 3, -8), Quaternion.Euler(0, 37, 0));
                var moved = authoring.CreateSnapshot();
                Assert.That(moved.Signature, Is.EqualTo(initial.Signature));
                var tray = authoring.PlacementRegions.Single(r => r.id == "maintenance-tray");
                Vector3 frame = instance.transform.InverseTransformPoint(tray.frame.position);
                Assert.That(Vector3.Distance(frame, new Vector3(.54f, 1.28f, .035f)), Is.LessThan(.0005f));
                Assert.That(Vector3.Dot(tray.frame.up, Vector3.up), Is.GreaterThan(.99999f));
                var filter = tray.frame.parent.GetComponentsInChildren<MeshFilter>().Single();
                using var meshData = MeshUtility.AcquireReadOnlyMeshData(filter.sharedMesh);
                var data = meshData[0];
                using var vertices = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp);
                data.GetVertices(vertices);
                int indexCount = (int)filter.sharedMesh.GetIndexCount(0);
                using var indices = new NativeArray<int>(indexCount, Allocator.Temp);
                data.GetIndices(indices, 0);
                Matrix4x4 toRoot = instance.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                Vector3 At(int index) => toRoot.MultiplyPoint3x4(vertices[indices[index]]);
                // 四个内部采样点必须实际落在顶面三角形里，不能只凭 Bounds 顶部/边框高度。
                foreach (Vector2 offset in new[] { new Vector2(-.15f, -.1f), new Vector2(.15f, -.1f),
                    new Vector2(-.15f, .1f), new Vector2(.15f, .1f) })
                {
                    Vector2 point = new Vector2(frame.x, frame.z) + offset;
                    bool supported = false;
                    for (int i = 0; i < indices.Length; i += 3)
                    {
                        Vector3 a = At(i), b = At(i + 1), c = At(i + 2);
                        if (Mathf.Abs(a.y - frame.y) > .0005f || Mathf.Abs(b.y - frame.y) > .0005f ||
                            Mathf.Abs(c.y - frame.y) > .0005f || Vector3.Cross(b-a, c-a).y <= 0) continue;
                        if (Contains(point, new Vector2(a.x,a.z), new Vector2(b.x,b.z), new Vector2(c.x,c.z)))
                        { supported = true; break; }
                    }
                    Assert.That(supported, Is.True, "托盘标记下面没有实际朝上的支撑三角形：" + offset);
                }
            }
            finally { Object.DestroyImmediate(instance); }
        }

        private static bool Contains(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float Cross(Vector2 v, Vector2 w) => v.x*w.y-v.y*w.x;
            float ab = Cross(b-a,p-a), bc = Cross(c-b,p-b), ca = Cross(a-c,p-c);
            return (ab >= -.000001f && bc >= -.000001f && ca >= -.000001f) ||
                   (ab <= .000001f && bc <= .000001f && ca <= .000001f);
        }
    }
}
