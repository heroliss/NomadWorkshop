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
    /// <summary>在真实车体候选中复用三居民行动、身份和旅程边界；另核对新边框始终跟随同一甲板表现。</summary>
    public sealed class NomadReferenceVehiclePlayModeTests : NomadResidentCrewPlayModeTests
    {
        protected override string ScenePath => "Assets/Game/NomadWorkshop/Scenes/ReferenceVehicleSample.unity";

        [UnityTest]
        public IEnumerator ShellAtlas_IsUsedByFourPanels_AndWorldRestoreKeepsTheSameVehicleDefinition()
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            var context = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<NomadFoundationContext>()).Single();
            var read = context.ExecuteCommand(new GetFoundationReadModelCommand());
            var panels = Panels(scene);
            Vector3[] positions = panels.Select(r => r.transform.position).ToArray();
            Assert.That(panels.Length, Is.EqualTo(4));
            foreach (Renderer panel in panels)
            {
                Assert.That(panel.GetComponent<Collider>(), Is.Null);
                Material mat = panel.sharedMaterial;
                Assert.That(mat.GetTexture("_BaseMap").name, Is.EqualTo("NW5_Shell_Color"));
                Assert.That(mat.GetTexture("_BumpMap").name, Is.EqualTo("NW5_Shell_Normal"));
                Assert.That(mat.GetTexture("_MetallicGlossMap").name, Is.EqualTo("NW5_Shell_Surface"));
                Assert.That(mat.IsKeywordEnabled("_NORMALMAP"), Is.True);
                Assert.That(mat.IsKeywordEnabled("_METALLICSPECGLOSSMAP"), Is.True);
                // A bulwark must remain outside the original +/-5.4 x +/-4.2 m playable rectangle.
                foreach (var resident in read.Residents)
                    Assert.That(panel.bounds.Contains(context.transform.TransformPoint(resident.ResidentLocalPosition.CurrentValue + Vector3.up*.5f)), Is.False,
                        "初始居民不能被新增纯表现钢板包住。");
            }
            var checkpoint = context.ExecuteCommand(new CaptureFoundationCheckpointCommand());
            context.ExecuteCommand(new RestoreFoundationCheckpointCommand(checkpoint));
            yield return null; yield return null;
            Renderer[] after = Panels(scene);
            Assert.That(after.Length, Is.EqualTo(4));
            for (int i = 0; i < after.Length; i++) Assert.That(Vector3.Distance(after[i].transform.position, positions[i]), Is.LessThan(.001f));
            LogAssert.NoUnexpectedReceived();
        }

        private static Renderer[] Panels(Scene scene) => scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Renderer>()).Where(r => r.sharedMaterial != null && r.sharedMaterial.name == "NW5_ShellAtlas")
            .OrderBy(r => r.name, System.StringComparer.Ordinal).ToArray();
    }
}
#endif
