#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Game.NomadWorkshop.Foundation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.NomadWorkshop.PlayMode.Tests
{
    /// <summary>在真实车体候选中复用三居民行动、身份和旅程边界；核对车体图集和重建后的空间姿态。</summary>
    public sealed class NomadReferenceVehiclePlayModeTests : NomadResidentCrewPlayModeTests
    {
        protected override string ScenePath => "Assets/Game/NomadWorkshop/Scenes/ReferenceVehicleSample.unity";

        [UnityTest]
        public IEnumerator VehicleAtlases_KeepShellDeckAndCockpitBindingsAndPose_AfterWorldRestore()
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            var context = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<NomadFoundationContext>()).Single();
            var read = context.ExecuteCommand(new GetFoundationReadModelCommand());
            var panels = Panels(scene);
            Vector3[] positions = panels.Select(r => r.transform.position).ToArray();
            Assert.That(panels.Length, Is.EqualTo(6));
            Assert.That(panels.Count(r => r.sharedMaterial.name == "NW5_ShellAtlas"), Is.EqualTo(4));
            Assert.That(panels.Count(r => r.sharedMaterial.name == "NW5_DeckAtlas"), Is.EqualTo(1));
            Assert.That(panels.Count(r => r.sharedMaterial.name == "NW5_CockpitAtlas"), Is.EqualTo(1));
            foreach (Renderer panel in panels)
            {
                Assert.That(panel.GetComponent<Collider>(), Is.Null);
                Material mat = panel.sharedMaterial;
                string stem = mat.name.Substring(0, mat.name.Length - "Atlas".Length);
                Assert.That(mat.GetTexture("_BaseMap").name, Is.EqualTo(stem + "_Color"));
                Assert.That(mat.GetTexture("_BumpMap").name, Is.EqualTo(stem + "_Normal"));
                Assert.That(mat.GetTexture("_MetallicGlossMap").name, Is.EqualTo(stem + "_Surface"));
                Assert.That(mat.IsKeywordEnabled("_NORMALMAP"), Is.True);
                Assert.That(mat.IsKeywordEnabled("_METALLICSPECGLOSSMAP"), Is.True);
                // 四段直边框可作保守包围盒检查；驾驶舱合并网格含首尾牵引环，
                // 其跨车 AABB 不能当成实际墙体，具体顶点净空由导出器校验。
                if (mat.name == "NW5_ShellAtlas")
                    foreach (var resident in read.Residents)
                        Assert.That(panel.bounds.Contains(context.transform.TransformPoint(resident.ResidentLocalPosition.CurrentValue + Vector3.up*.5f)), Is.False,
                            "初始居民不能被新增纯表现边框包住。");
                if (mat.name == "NW5_DeckAtlas")
                    Assert.That(panel.bounds.max.y, Is.LessThanOrEqualTo(context.transform.position.y + .01f),
                        "装饰地板不能高于现有脚底/导航平面。");
            }
            var checkpoint = context.ExecuteCommand(new CaptureFoundationCheckpointCommand());
            context.ExecuteCommand(new RestoreFoundationCheckpointCommand(checkpoint));
            yield return null; yield return null;
            Renderer[] after = Panels(scene);
            Assert.That(after.Length, Is.EqualTo(6));
            for (int i = 0; i < after.Length; i++) Assert.That(Vector3.Distance(after[i].transform.position, positions[i]), Is.LessThan(.001f));
            LogAssert.NoUnexpectedReceived();
        }

        private static Renderer[] Panels(Scene scene) => scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Renderer>()).Where(r => r.sharedMaterial != null &&
                (r.sharedMaterial.name == "NW5_ShellAtlas" || r.sharedMaterial.name == "NW5_DeckAtlas" || r.sharedMaterial.name == "NW5_CockpitAtlas"))
            .OrderBy(r => r.name, System.StringComparer.Ordinal).ToArray();
    }
}
#endif
