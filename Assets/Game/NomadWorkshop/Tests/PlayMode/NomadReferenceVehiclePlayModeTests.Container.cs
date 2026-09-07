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
    public sealed partial class NomadReferenceVehiclePlayModeTests
    {
        [UnityTest]
        public IEnumerator AuthoredContainerBindingAndIdentitySurviveWorldRestore()
        {
            Scene scene=SceneManager.GetSceneByPath(ScenePath);
            var context=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<NomadFoundationContext>()).Single();
            var rig=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<FoundationCarriedContainerRig>(true)).Single();
            Assert.That(rig.ClearanceVolumes.Count,Is.EqualTo(2),"参考车辆必须实际接入新模型，不能悄悄回退到灰盒。");
            Assert.That(rig.CarryPivotLocalPosition.y,Is.EqualTo(.49f).Within(.001f));
            Assert.That(rig.GetComponentsInChildren<MeshRenderer>(true).All(r=>r.sharedMaterial.name=="NW11_WaterCanAtlas"),Is.True);
            int id=rig.GetInstanceID();
            var checkpoint=context.ExecuteCommand(new CaptureFoundationCheckpointCommand());
            context.ExecuteCommand(new RestoreFoundationCheckpointCommand(checkpoint));
            yield return null; yield return null;
            var after=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<FoundationCarriedContainerRig>(true)).Single();
            Assert.That(after.GetInstanceID(),Is.EqualTo(id));
            Assert.DoesNotThrow(after.ValidateBindings);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
#endif
