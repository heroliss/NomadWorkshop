using Game.NomadWorkshop.Editor;
using NUnit.Framework;
using UnityEditor;

namespace Game.NomadWorkshop.Editor.Tests
{
    /// <summary>固定默认 3D、显式 2D 兼容和保守 URP 图形基线。</summary>
    public sealed class NomadRenderingSpikePipelineTests
    {
        [Test]
        public void Game3DRendererIsDefaultAndLegacy2DScenesStayExplicit()
        {
            NomadRenderingSpikeAudit audit = NomadRenderingSpikePipeline.Audit();

            Assert.IsTrue(audit.PipelineAssetMatches, audit.ToMultilineString());
            Assert.IsTrue(audit.PipelineGraphicsBaselineMatches, audit.ToMultilineString());
            Assert.IsTrue(audit.Legacy2DRendererRegistered, audit.ToMultilineString());
            Assert.IsTrue(audit.Game3DRendererRegistered, audit.ToMultilineString());
            Assert.IsTrue(audit.Game3DRendererIsDefault, audit.ToMultilineString());
            Assert.IsTrue(audit.Legacy2DScenesPinned, audit.ToMultilineString());
            Assert.IsTrue(audit.RendererContractMatches, audit.ToMultilineString());
            Assert.IsTrue(audit.GameGraphicsAssetsMatch, audit.ToMultilineString());
            Assert.That(audit.Legacy2DRendererIndex, Is.EqualTo(0));
            Assert.AreNotEqual(audit.Legacy2DRendererIndex, audit.Game3DRendererIndex);
            Assert.Greater(audit.Game3DRendererIndex, -1);
        }

        [Test]
        public void PreviewCameraUsesGame3DRendererAndKeepsVisualReviewExplicit()
        {
            NomadRenderingSpikeAudit audit = NomadRenderingSpikePipeline.Audit();

            Assert.IsTrue(audit.PreviewSceneContractMatches, audit.ToMultilineString());
            Assert.IsTrue(audit.Passed, audit.ToMultilineString());
            Assert.That(audit.RenderingVerdict, Does.StartWith("manual_review_required"));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SceneAsset>(
                NomadRenderingSpikePipeline.PreviewScenePath));
        }
    }
}
