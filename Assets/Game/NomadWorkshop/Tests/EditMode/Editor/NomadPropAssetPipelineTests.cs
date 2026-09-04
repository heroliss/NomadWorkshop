using System.Linq;
using Game.NomadWorkshop.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.NomadWorkshop.Editor.Tests
{
    /// <summary>固定 Blender 静态道具从源哈希到 Unity Prefab 的首条导入证据链。</summary>
    public sealed class NomadPropAssetPipelineTests
    {
        [Test]
        public void BlenderSmokePropMatchesImporterGeometryMaterialAndPrefabContract()
        {
            NomadPropAssetAudit audit = NomadPropAssetPipeline.Audit();

            Assert.IsTrue(audit.SourceHashMatches, audit.ToMultilineString());
            Assert.IsTrue(audit.ImporterPolicyMatches, audit.ToMultilineString());
            Assert.IsTrue(audit.SourceHierarchyMatches, audit.ToMultilineString());
            Assert.IsTrue(audit.GeometryMatches, audit.ToMultilineString());
            Assert.IsTrue(audit.MaterialContractMatches, audit.ToMultilineString());
            Assert.IsTrue(audit.PrefabContractMatches, audit.ToMultilineString());
            Assert.IsTrue(audit.PreviewSceneExists, audit.ToMultilineString());
            Assert.IsTrue(audit.Passed, audit.ToMultilineString());

            Assert.AreEqual(14, audit.MeshObjectCount);
            Assert.AreEqual(784, audit.SourceVertexCount);
            Assert.AreEqual(3024, audit.VertexCount,
                "Unity 会按硬边、法线和材质边界拆点；这是运行时顶点，不等于 Blender 拓扑顶点。");
            Assert.AreEqual(1512, audit.TriangleCount);
            Assert.That(audit.ActualBoundsSize.x, Is.EqualTo(1.235f).Within(0.005f));
            Assert.That(audit.ActualBoundsSize.y, Is.EqualTo(0.945f).Within(0.005f));
            Assert.That(audit.ActualBoundsSize.z, Is.EqualTo(0.958f).Within(0.005f));

            string[] expectedMaterials =
            {
                NomadPropAssetPipeline.MaterialFolder + "/M_DarkMetal.mat",
                NomadPropAssetPipeline.MaterialFolder + "/M_SafetyOrange.mat",
                NomadPropAssetPipeline.MaterialFolder + "/M_WornTeal.mat",
            };
            CollectionAssert.AreEquivalent(expectedMaterials, audit.MaterialAssetPaths);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                NomadPropAssetPipeline.PrefabPath);
            Assert.IsNotNull(prefab);
            Assert.AreEqual(1, prefab.GetComponentsInChildren<BoxCollider>(true).Length);
            Assert.AreEqual(0, prefab.GetComponentsInChildren<MeshCollider>(true).Length);
            Assert.AreEqual(14, prefab.GetComponentsInChildren<MeshFilter>(true)
                .Count(filter => filter.sharedMesh != null));
        }

        [Test]
        public void CurrentRendererRequiresManualReviewAfterThreeDimensionalBaseline()
        {
            NomadPropAssetAudit audit = NomadPropAssetPipeline.Audit();

            Assert.That(audit.RenderingVerdict, Does.StartWith("manual_review_required"));
            Assert.That(audit.RenderingVerdict, Does.Contain("UniversalRendererData"));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SceneAsset>(
                NomadPropAssetPipeline.PreviewScenePath));
        }
    }
}
