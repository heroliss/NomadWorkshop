using Game.NomadWorkshop.Editor;
using NUnit.Framework;
using UnityEditor;

namespace Game.NomadWorkshop.Editor.Tests
{
    /// <summary>固定 Renderer2D 默认值与隔离 Universal Renderer 3D 预览的共存契约。</summary>
    public sealed class NomadRenderingSpikePipelineTests
    {
        [Test]
        public void SecondaryUniversalRendererDoesNotReplaceRenderer2DDefault()
        {
            NomadRenderingSpikeAudit audit = NomadRenderingSpikePipeline.Audit();

            Assert.IsTrue(audit.PipelineAssetMatches, audit.ToMultilineString());
            Assert.IsTrue(audit.DefaultRendererPreserved, audit.ToMultilineString());
            Assert.IsTrue(audit.SecondaryRendererRegistered, audit.ToMultilineString());
            Assert.IsTrue(audit.RendererContractMatches, audit.ToMultilineString());
            Assert.AreNotEqual(audit.DefaultRendererIndex, audit.SecondaryRendererIndex);
            Assert.Greater(audit.SecondaryRendererIndex, -1);
        }

        [Test]
        public void PreviewCameraUsesSecondaryRendererAndKeepsVisualReviewExplicit()
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
