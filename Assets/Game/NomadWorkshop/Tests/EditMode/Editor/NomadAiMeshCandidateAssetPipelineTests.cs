using Game.NomadWorkshop.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.NomadWorkshop.Editor.Tests
{
    /// <summary>固定首个外部 AI Mesh 候选的来源、PBR 导入与人工复核边界。</summary>
    public sealed class NomadAiMeshCandidateAssetPipelineTests
    {
        [Test]
        public void RodinFieldKitchenMatchesIntakeImportPrefabAndPreviewContract()
        {
            NomadAiMeshCandidateAudit audit = NomadAiMeshCandidateAssetPipeline.Audit();

            Assert.IsTrue(audit.SourceHashesMatch, audit.ToMultilineString());
            Assert.IsTrue(audit.TextureImporterContractMatches, audit.ToMultilineString());
            Assert.IsTrue(audit.ModelImporterContractMatches, audit.ToMultilineString());
            Assert.IsTrue(audit.SourceHierarchyMatches, audit.ToMultilineString());
            Assert.IsTrue(audit.GeometryMatches, audit.ToMultilineString());
            Assert.IsTrue(audit.MaterialContractMatches, audit.ToMultilineString());
            Assert.IsTrue(audit.PrefabContractMatches, audit.ToMultilineString());
            Assert.IsTrue(audit.PreviewSceneContractMatches, audit.ToMultilineString());
            Assert.IsTrue(audit.Passed, audit.ToMultilineString());

            Assert.AreEqual(1, audit.MeshObjectCount);
            Assert.AreEqual(1, audit.MaterialSlotCount);
            Assert.AreEqual(18924, audit.SourceVertexCount);
            Assert.That(audit.UnityVertexCount, Is.GreaterThanOrEqualTo(audit.SourceVertexCount));
            Assert.That(audit.UnityVertexCount, Is.LessThanOrEqualTo(audit.SourceVertexCount * 4));
            Assert.AreEqual(37903, audit.TriangleCount);
            Assert.That(audit.ActualBoundsSize.x, Is.EqualTo(1.85658f).Within(0.02f));
            Assert.That(audit.ActualBoundsSize.y, Is.EqualTo(1.585033f).Within(0.02f));
            Assert.That(audit.ActualBoundsSize.z, Is.EqualTo(0.745058f).Within(0.02f));

            Assert.AreEqual(5, audit.DuplicateFaceCountInSource,
                "这是 Intake 在源 .blend 中发现并仅对导出副本清理的已知证据。");
            Assert.AreEqual(6, audit.ConnectedComponentCount);
            Assert.AreEqual(2048, audit.DeliveredTextureResolution,
                "网页选择过 8K，但 Bridge 此次实际交付 2K；测试固定真实文件而不是 UI 设置。");
            Assert.IsTrue(audit.ManualArtReviewRequired);
            Assert.IsFalse(audit.ProductionApproved);
            Assert.That(audit.UpstreamWarnings, Is.Not.Empty);
        }

        [Test]
        public void RodinFieldKitchenUsesOpaqueUrpLitAndPackedMetallicSmoothness()
        {
            TextureImporter baseColor = Importer(NomadAiMeshCandidateAssetPipeline.BaseColorPath);
            TextureImporter normal = Importer(NomadAiMeshCandidateAssetPipeline.NormalPath);
            TextureImporter mask = Importer(
                NomadAiMeshCandidateAssetPipeline.MetallicSmoothnessPath);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(
                NomadAiMeshCandidateAssetPipeline.MaterialPath);

            Assert.IsTrue(baseColor.sRGBTexture);
            Assert.AreEqual(TextureImporterType.NormalMap, normal.textureType);
            Assert.IsFalse(normal.sRGBTexture);
            Assert.IsFalse(normal.flipGreenChannel,
                "当前 Rodin 法线按 +Y 导入；若视觉复核显示凹凸反转，应改契约并重新验证。");
            Assert.IsFalse(mask.sRGBTexture);
            Assert.AreEqual(TextureImporterAlphaSource.FromInput, mask.alphaSource);

            Assert.IsNotNull(material);
            Assert.AreEqual("Universal Render Pipeline/Lit", material.shader.name);
            Assert.AreEqual(0f, material.GetFloat("_Surface"));
            Assert.AreEqual(0f, material.GetFloat("_AlphaClip"));
            Assert.AreEqual(
                AssetDatabase.LoadAssetAtPath<Texture2D>(
                    NomadAiMeshCandidateAssetPipeline.BaseColorPath),
                material.GetTexture("_BaseMap"));
            Assert.AreEqual(
                AssetDatabase.LoadAssetAtPath<Texture2D>(
                    NomadAiMeshCandidateAssetPipeline.NormalPath),
                material.GetTexture("_BumpMap"));
            Assert.AreEqual(
                AssetDatabase.LoadAssetAtPath<Texture2D>(
                    NomadAiMeshCandidateAssetPipeline.MetallicSmoothnessPath),
                material.GetTexture("_MetallicGlossMap"));
            Assert.IsTrue(material.IsKeywordEnabled("_NORMALMAP"));
            Assert.IsTrue(material.IsKeywordEnabled("_METALLICSPECGLOSSMAP"));
        }

        private static TextureImporter Importer(string path)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsNotNull(importer, path);
            return importer;
        }
    }
}
