using System.Linq;
using Game.NomadWorkshop.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.NomadWorkshop.Editor.Tests
{
    /// <summary>固定纹理化 Blender 道具的 PBR 语义、几何合并和 URP 显示证据。</summary>
    public sealed class NomadTexturedPropAssetPipelineTests
    {
        private static readonly string[] MaterialSlugs =
        {
            "WornTeal",
            "DarkMetal",
            "SafetyOrange",
        };

        [Test]
        public void WaterRecyclerMatchesSourceTextureGeometryMaterialAndPrefabContract()
        {
            NomadTexturedPropAssetAudit audit = NomadTexturedPropAssetPipeline.Audit();

            Assert.IsTrue(audit.SourceHashesMatch, audit.ToMultilineString());
            Assert.IsTrue(audit.EvidenceImporterContractMatches, audit.ToMultilineString());
            Assert.IsTrue(audit.TextureImporterContractMatches, audit.ToMultilineString());
            Assert.IsTrue(audit.ModelImporterContractMatches, audit.ToMultilineString());
            Assert.IsTrue(audit.SourceHierarchyMatches, audit.ToMultilineString());
            Assert.IsTrue(audit.GeometryMatches, audit.ToMultilineString());
            Assert.IsTrue(audit.MaterialContractMatches, audit.ToMultilineString());
            Assert.IsTrue(audit.PrefabContractMatches, audit.ToMultilineString());
            Assert.IsTrue(audit.PreviewSceneContractMatches, audit.ToMultilineString());
            Assert.IsTrue(audit.Passed, audit.ToMultilineString());

            Assert.AreEqual(3, audit.MeshObjectCount);
            Assert.AreEqual(3, audit.MaterialSlotCount);
            Assert.AreEqual(12, audit.TextureCount);
            Assert.AreEqual(3928, audit.SourceVertexCount);
            Assert.AreEqual(8083, audit.VertexCount,
                "Unity 会按法线、UV 和硬边拆点；这里固定的是当前导入器的运行时顶点证据。");
            Assert.AreEqual(7772, audit.TriangleCount);
            Assert.That(audit.ActualBoundsSize.x, Is.EqualTo(1.46f).Within(0.008f));
            Assert.That(audit.ActualBoundsSize.y, Is.EqualTo(1.475f).Within(0.008f));
            Assert.That(audit.ActualBoundsSize.z, Is.EqualTo(0.895f).Within(0.008f));
            Assert.AreEqual(0, audit.NonManifoldEdgeCount);
            Assert.AreEqual(0, audit.DegenerateUvTriangleCount);
            Assert.That(
                audit.EffectiveTexelDensityPxPerMeter,
                Is.EqualTo(1001.091f).Within(0.01f),
                "这里固定的是允许重叠 UV 与材质平铺后的面积加权有效密度，不代表唯一贴图内存预算。");

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                NomadTexturedPropAssetPipeline.PrefabPath);
            Assert.IsNotNull(prefab);
            Assert.AreEqual(3, prefab.GetComponentsInChildren<MeshFilter>(true)
                .Count(filter => filter.sharedMesh != null));
            Assert.AreEqual(1, prefab.GetComponentsInChildren<BoxCollider>(true).Length);
            Assert.AreEqual(0, prefab.GetComponentsInChildren<MeshCollider>(true).Length);
        }

        [Test]
        public void ContactSheetPreservesSixViewManualReviewEvidence()
        {
            TextureImporter importer = Importer(NomadTexturedPropAssetPipeline.ContactSheetPath);
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                NomadTexturedPropAssetPipeline.ContactSheetPath);

            Assert.IsNotNull(texture);
            Assert.AreEqual(1536, texture.width);
            Assert.AreEqual(1024, texture.height);
            Assert.AreEqual(TextureImporterType.Default, importer.textureType);
            Assert.AreEqual(SpriteImportMode.None, importer.spriteImportMode);
            Assert.IsTrue(importer.sRGBTexture);
            Assert.IsFalse(importer.mipmapEnabled);
            Assert.AreEqual(TextureWrapMode.Clamp, importer.wrapMode);
            Assert.AreEqual(FilterMode.Bilinear, importer.filterMode);
            Assert.AreEqual(TextureImporterCompression.Uncompressed, importer.textureCompression);
            Assert.IsFalse(importer.isReadable);
        }

        [Test]
        public void EveryMaterialUsesTheSameFourMapUrpPackingContract()
        {
            foreach (string slug in MaterialSlugs)
            {
                string prefix = NomadTexturedPropAssetPipeline.TextureFolder +
                                "/T_NW_WaterRecycler_01_" + slug;
                TextureImporter baseColor = Importer(prefix + "_BaseColor.png");
                TextureImporter normal = Importer(prefix + "_Normal.png");
                TextureImporter mask = Importer(prefix + "_MetallicSmoothness.png");
                TextureImporter occlusion = Importer(prefix + "_Occlusion.png");

                Assert.AreEqual(TextureImporterType.Default, baseColor.textureType, slug);
                Assert.IsTrue(baseColor.sRGBTexture, slug);
                Assert.AreEqual(TextureImporterType.NormalMap, normal.textureType, slug);
                Assert.IsFalse(normal.sRGBTexture, slug);
                Assert.IsFalse(normal.flipGreenChannel, slug);
                Assert.AreEqual(TextureImporterType.Default, mask.textureType, slug);
                Assert.IsFalse(mask.sRGBTexture, slug);
                Assert.AreEqual(TextureImporterAlphaSource.FromInput, mask.alphaSource, slug);
                Assert.AreEqual(TextureImporterType.Default, occlusion.textureType, slug);
                Assert.IsFalse(occlusion.sRGBTexture, slug);
            }

            foreach (string materialName in new[]
                     {
                         "M_WornTeal",
                         "M_DarkMetal",
                         "M_SafetyOrange",
                     })
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(
                    NomadTexturedPropAssetPipeline.MaterialFolder + "/" + materialName + ".mat");
                Assert.IsNotNull(material, materialName);
                Assert.AreEqual("Universal Render Pipeline/Lit", material.shader.name, materialName);
                Assert.IsNotNull(material.GetTexture("_BaseMap"), materialName);
                Assert.IsNotNull(material.GetTexture("_BumpMap"), materialName);
                Assert.IsNotNull(material.GetTexture("_MetallicGlossMap"), materialName);
                Assert.IsNotNull(material.GetTexture("_OcclusionMap"), materialName);
                Assert.IsTrue(material.IsKeywordEnabled("_NORMALMAP"), materialName);
                Assert.IsTrue(material.IsKeywordEnabled("_METALLICSPECGLOSSMAP"), materialName);
                Assert.IsTrue(material.IsKeywordEnabled("_OCCLUSIONMAP"), materialName);
            }
        }

        private static TextureImporter Importer(string path)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsNotNull(importer, path);
            return importer;
        }
    }
}
