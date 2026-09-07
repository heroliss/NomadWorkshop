#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using Game.NomadWorkshop.Foundation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.NomadWorkshop.PlayMode.Tests
{
    /// <summary>三人固定配方复用既有工作/旅程契约，并验证人物身份与外观在读档、重建后仍一致。</summary>
    public class NomadResidentCrewPlayModeTests : NomadWarmWorkshopPlayModeTests
    {
        protected override string ScenePath => "Assets/Game/NomadWorkshop/Scenes/ResidentCrewSample.unity";
        protected virtual string WorkshirtPrefix => "NW3_Shirt_";
        protected override string ExpectedWorkshirtMaterial(ResidentHumanoidPresentation resident) => resident.name switch
        {
            "Resident 01" => WorkshirtPrefix + "Mechanic",
            "Resident 02" => WorkshirtPrefix + "Caretaker",
            "Resident 03" => WorkshirtPrefix + "Driver",
            _ => throw new InvalidOperationException("未声明的居民外观：" + resident.name)
        };

        [UnityTest]
        public IEnumerator ThreeIdentities_RetainDistinctAppearances_AfterCheckpointAndWorldRebuild()
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            NomadFoundationContext context = scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<NomadFoundationContext>(true)).Single();
            string[] before = AppearanceIdentities(scene);
            var checkpoint = context.ExecuteCommand(new CaptureFoundationCheckpointCommand());
            context.ExecuteCommand(new RestoreFoundationCheckpointCommand(checkpoint));
            yield return null;
            yield return null;
            Assert.That(AppearanceIdentities(scene), Is.EqualTo(before));
            Assert.That(context.ExecuteCommand(new ResetFoundationForSoakHarnessCommand(2718)), Is.True);
            yield return null;
            yield return null;
            Assert.That(AppearanceIdentities(scene), Is.EqualTo(before), "改变玩法 Seed 不能改变固定居民的外观绑定。");
            LogAssert.NoUnexpectedReceived();
        }

        private string[] AppearanceIdentities(Scene scene)
        {
            var residents = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ResidentHumanoidPresentation>())
                .OrderBy(r => r.name, StringComparer.Ordinal).ToArray();
            Assert.That(residents.Length, Is.EqualTo(3));
            string[] expectedHair = { "Hair_Buzzed", "Hair_Buns", "Hair_Beard" };
            var result = new string[residents.Length];
            for (int i = 0; i < residents.Length; i++)
            {
                var renderers = residents[i].VisualRoot.GetComponentsInChildren<SkinnedMeshRenderer>();
                Assert.That(renderers.Any(r => r.name == expectedHair[i]), Is.True, residents[i].name + " 发型/胡须绑定错误。");
                Assert.That(renderers.All(r => r.quality == SkinQuality.Bone4), Is.True);
                string shirt = renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null)
                    .Select(m => m.name).Distinct().Single(n => n.StartsWith(WorkshirtPrefix, StringComparison.Ordinal));
                Assert.That(shirt, Is.EqualTo(ExpectedWorkshirtMaterial(residents[i])));
                result[i] = residents[i].name + "/" + shirt + "/" + expectedHair[i];
            }
            Assert.That(result.Distinct().Count(), Is.EqualTo(3));
            return result;
        }
    }

    public sealed class NomadMechanicStairPlayModeTests : NomadStairTraversalPlayModeTests
    {
        protected override string ScenePath => "Assets/Game/NomadWorkshop/Scenes/ResidentCrewMechanicStairs.unity";
    }
    public sealed class NomadCaretakerStairPlayModeTests : NomadStairTraversalPlayModeTests
    {
        protected override string ScenePath => "Assets/Game/NomadWorkshop/Scenes/ResidentCrewCaretakerStairs.unity";
    }
    public sealed class NomadDriverStairPlayModeTests : NomadStairTraversalPlayModeTests
    {
        protected override string ScenePath => "Assets/Game/NomadWorkshop/Scenes/ResidentCrewDriverStairs.unity";
    }

    public sealed class NomadWorkwearMechanicStairPlayModeTests : NomadStairTraversalPlayModeTests
    {
        protected override string ScenePath => "Assets/Game/NomadWorkshop/Scenes/WorkwearMechanicStairs.unity";
    }
    public sealed class NomadWorkwearCaretakerStairPlayModeTests : NomadStairTraversalPlayModeTests
    {
        protected override string ScenePath => "Assets/Game/NomadWorkshop/Scenes/WorkwearCaretakerStairs.unity";
    }
    public sealed class NomadWorkwearDriverStairPlayModeTests : NomadStairTraversalPlayModeTests
    {
        protected override string ScenePath => "Assets/Game/NomadWorkshop/Scenes/WorkwearDriverStairs.unity";
    }
}
#endif
