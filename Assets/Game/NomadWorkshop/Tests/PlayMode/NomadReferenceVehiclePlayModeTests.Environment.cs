#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Game.NomadWorkshop.Foundation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.NomadWorkshop.PlayMode.Tests
{
    public sealed partial class NomadReferenceVehiclePlayModeTests
    {
        [UnityTest]
        public IEnumerator Desert_PreservesPbrAndAllSceneryBindings_AfterWorldRestore()
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            var context = FindRoofSceneComponent<NomadFoundationContext>(scene);
            var binding = FindRoofSceneComponent<FoundationEnvironmentVisual>(scene);
            Assert.That(binding.ScrollingSurfaces.Count, Is.EqualTo(3));
            Assert.That(binding.SceneryGroups.Count, Is.EqualTo(16));
            Assert.That(binding.GetComponentsInChildren<Collider>(true), Is.Empty);
            Vector3[] positions = binding.SceneryGroups.Select(t => t.position).ToArray();
            foreach (var surface in binding.ScrollingSurfaces)
            {
                Material material = surface.Renderer.sharedMaterial;
                Assert.That(material.GetTexture("_BaseMap").wrapMode, Is.EqualTo(TextureWrapMode.Repeat));
                Assert.That(surface.Renderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off));
                if (material.name == "NW6_Ground")
                {
                    Assert.That(surface.MetersPerUvUnit, Is.EqualTo(6));
                    Assert.That(material.GetTexture("_BumpMap").name, Is.EqualTo("NW6_Ground_Normal"));
                    Assert.That(material.GetTexture("_MetallicGlossMap").name, Is.EqualTo("NW6_Ground_Surface"));
                }
                else
                {
                    Assert.That(material.name, Is.EqualTo("NW6_Track"));
                    Assert.That(surface.MetersPerUvUnit, Is.EqualTo(1.4f));
                    Assert.That(material.GetFloat("_ZWrite"), Is.Zero);
                    Assert.That(material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"), Is.True);
                }
            }
            foreach (Transform group in binding.SceneryGroups)
                Assert.That(group.GetComponentsInChildren<Renderer>().Any(r => r.sharedMaterial.name == "NW6_Sandstone"), Is.True);
            var checkpoint = context.ExecuteCommand(new CaptureFoundationCheckpointCommand());
            context.ExecuteCommand(new RestoreFoundationCheckpointCommand(checkpoint));
            yield return null; yield return null;
            var restored = FindRoofSceneComponent<FoundationEnvironmentVisual>(scene);
            Assert.That(restored.SceneryGroups.Select(t => t.position), Is.EqualTo(positions));
            Assert.That(restored.ScrollingSurfaces.Count, Is.EqualTo(3));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
#endif
