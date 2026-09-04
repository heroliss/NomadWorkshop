using Game.NomadWorkshop.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.NomadWorkshop.Editor.Tests
{
    /// <summary>锁定首个参数化道具从玩法尺寸到普通 Unity Prefab 的作者闭环。</summary>
    public sealed class NomadFieldKitchenPrototypePipelineTests
    {
        [Test]
        public void GeneratedFieldKitchenMatchesAuthoringAndRuntimeAssetContract()
        {
            NomadFieldKitchenPrototypeAudit audit =
                NomadFieldKitchenPrototypePipeline.Audit();

            Assert.IsTrue(audit.ProfileValid, audit.ToMultilineString());
            Assert.IsTrue(audit.ProBuilderPackageResolved, audit.ToMultilineString());
            Assert.IsTrue(audit.HierarchyMatches, audit.ToMultilineString());
            Assert.IsTrue(audit.MeshContractMatches, audit.ToMultilineString());
            Assert.IsTrue(audit.MaterialContractMatches, audit.ToMultilineString());
            Assert.IsTrue(audit.ColliderContractMatches, audit.ToMultilineString());
            Assert.IsTrue(audit.InteractionContractMatches, audit.ToMultilineString());
            Assert.IsTrue(audit.PreviewSceneExists, audit.ToMultilineString());
            Assert.IsTrue(audit.Passed, audit.ToMultilineString());

            Assert.AreEqual("6.1.2", audit.ProBuilderVersion);
            Assert.AreEqual(4, audit.MeshFilterCount,
                "运行时只应有一个合并静态主体和三扇独立门，不保留每个基础体的 GameObject。");
            Assert.That(audit.VertexCount, Is.GreaterThan(250));
            Assert.That(audit.TriangleCount, Is.InRange(250, 12000));
            Assert.That(audit.ColliderSize.x, Is.EqualTo(2.3f).Within(0.0001f));
            Assert.That(audit.ColliderSize.y, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(audit.ColliderSize.z, Is.EqualTo(0.88f).Within(0.0001f));
        }

        [Test]
        public void DefaultProfileKeepsRodinComparisonScaleAndSafePartRatios()
        {
            NomadFieldKitchenPrototypeProfile profile =
                AssetDatabase.LoadAssetAtPath<NomadFieldKitchenPrototypeProfile>(
                    NomadFieldKitchenPrototypePipeline.ProfilePath);

            Assert.IsNotNull(profile);
            Assert.DoesNotThrow(profile.ValidateOrThrow);
            Assert.That(profile.Width, Is.EqualTo(2.3f).Within(0.0001f));
            Assert.That(profile.Height, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(profile.Depth, Is.EqualTo(0.88f).Within(0.0001f));
            Assert.That(profile.CounterHeight, Is.EqualTo(1.02f).Within(0.0001f));
            Assert.That(profile.LeftTowerWidth, Is.LessThan(profile.Width * 0.4f));
            Assert.That(profile.BevelWidth, Is.LessThan(profile.PanelThickness * 0.45f));
        }

        [Test]
        public void PrefabSeparatesGameplayContractFromReplaceableVisuals()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                NomadFieldKitchenPrototypePipeline.PrefabPath);

            Assert.IsNotNull(prefab);
            Transform prototype = prefab.transform.Find(
                NomadFieldKitchenPrototypePipeline.PrototypeVisualName);
            Transform final = prefab.transform.Find(
                NomadFieldKitchenPrototypePipeline.FinalVisualName);
            Assert.IsNotNull(prototype);
            Assert.IsNotNull(final);
            Assert.IsTrue(prototype.gameObject.activeSelf);
            Assert.IsFalse(final.gameObject.activeSelf);
            Assert.AreEqual(0, final.childCount,
                "最终视觉槽当前应保持空白；外部 AI Mesh 进入时只能替换这里，不改玩法根。 ");

            Assert.AreEqual(1, prefab.GetComponentsInChildren<BoxCollider>(true).Length);
            FacilityInteractionAnchor anchor =
                prefab.GetComponent<FacilityInteractionAnchor>();
            Assert.IsNotNull(anchor);
            Assert.IsTrue(anchor.IsConfigured);
            Assert.AreEqual("PrepareMeal", anchor.ActionId);

            AssertDoorHinge(prefab.transform,
                NomadFieldKitchenPrototypePipeline.LeftLowerHingeName);
            AssertDoorHinge(prefab.transform,
                NomadFieldKitchenPrototypePipeline.CenterHingeName);
            AssertDoorHinge(prefab.transform,
                NomadFieldKitchenPrototypePipeline.RightLowerHingeName);
        }

        [Test]
        public void PreviewSceneKeepsVersionedReusableRootContract()
        {
            Scene previous = SceneManager.GetActiveScene();
            Scene preview = EditorSceneManager.OpenScene(
                NomadFieldKitchenPrototypePipeline.PreviewScenePath,
                OpenSceneMode.Additive);
            try
            {
                GameObject[] roots = preview.GetRootGameObjects();
                CollectionAssert.AreEquivalent(
                    new[]
                    {
                        NomadFieldKitchenPrototypePipeline.PreviewMarkerName,
                        NomadFieldKitchenPrototypePipeline.AssetId,
                        "PreviewGround",
                        "Main Camera",
                        "Key Light",
                        "Cool Fill",
                        "Rim Light",
                    },
                    System.Array.ConvertAll(roots, root => root.name));

                GameObject instance = System.Array.Find(
                    roots,
                    root => root.name == NomadFieldKitchenPrototypePipeline.AssetId);
                Assert.IsNotNull(instance);
                Assert.AreEqual(
                    NomadFieldKitchenPrototypePipeline.PrefabPath,
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance));
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded)
                    SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(preview, true);
            }
        }

        private static void AssertDoorHinge(Transform root, string hingeName)
        {
            Transform hinge = root.Find(
                NomadFieldKitchenPrototypePipeline.PrototypeVisualName + "/" + hingeName);
            Assert.IsNotNull(hinge, hingeName);
            Assert.That(Quaternion.Angle(hinge.localRotation, Quaternion.identity),
                Is.LessThan(0.01f), hingeName + " 的 Prefab 默认姿态必须闭合。");
            Assert.AreEqual(1, hinge.GetComponentsInChildren<MeshFilter>(true).Length,
                hingeName + " 应烘焙为单一门 Mesh，不保留基础几何体层级。");
        }
    }
}
