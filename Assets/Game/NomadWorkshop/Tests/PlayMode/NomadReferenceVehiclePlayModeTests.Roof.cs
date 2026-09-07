#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using System.Reflection;
using Game.NomadWorkshop.Foundation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.NomadWorkshop.PlayMode.Tests
{
    public sealed partial class NomadReferenceVehiclePlayModeTests
    {
        [UnityTest]
        public IEnumerator RoofShortcut_BuildAndRestoreKeepViewPreference_AndPausedWorldUnchanged()
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            var context = FindRoofSceneComponent<NomadFoundationContext>(scene);
            var view = FindRoofSceneComponent<NomadFoundationWorldView>(scene);
            var ui = FindRoofSceneComponent<NomadFoundationDebugView>(scene);
            var read = context.ExecuteCommand(new GetFoundationReadModelCommand());
            FoundationRoofPresentation roof = view.RoofPresentation;
            Assert.That(roof, Is.Not.Null);
            Assert.That(ui.HasRoofControls, Is.True);
            Assert.That(roof.ExteriorVisible, Is.False);
            long tick = read.SimulationTick.CurrentValue;
            string initial = PausedWorldState(read);
            Keyboard previous = Keyboard.current;
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            keyboard.MakeCurrent();
            try
            {
                yield return PressKey(keyboard, Key.H);
                Assert.That(roof.ExteriorVisible, Is.True, "H 必须经 Input System 和 World View.Update 生效。");
                var checkpoint = context.ExecuteCommand(new CaptureFoundationCheckpointCommand());
                context.ExecuteCommand(new RestoreFoundationCheckpointCommand(checkpoint));
                yield return null; yield return null;
                Assert.That(view.RoofPresentation, Is.SameAs(roof));
                Assert.That(roof.ExteriorVisible, Is.True, "业务恢复不能清除当前观看偏好。");
                ui.TogglePanelForTests(FoundationHudPanel.Build);
                Assert.That(roof.BuildCutaway, Is.True);
                Assert.That(roof.ExteriorVisible, Is.False);
                yield return PressKey(keyboard, Key.H);
                Assert.That(roof.ExteriorPreferred, Is.True, "强制剖开时按 H 不改变退出后的外观偏好。");
                ui.TogglePanelForTests(FoundationHudPanel.Resident);
                Assert.That(roof.ExteriorVisible, Is.True);
                yield return PressKey(keyboard, Key.H);
                Assert.That(roof.ExteriorVisible, Is.False);
                Assert.That(read.SimulationTick.CurrentValue, Is.EqualTo(tick));
                Assert.That(PausedWorldState(read), Is.EqualTo(initial));
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
                if (previous != null && previous.added) previous.MakeCurrent();
            }
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator RoofBuildCutaway_KeepsDeckPickingAndOnlyBlocksVisibleButton()
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            var context = FindRoofSceneComponent<NomadFoundationContext>(scene);
            var view = FindRoofSceneComponent<NomadFoundationWorldView>(scene);
            var ui = FindRoofSceneComponent<NomadFoundationDebugView>(scene);
            var camera = (Camera)typeof(NomadFoundationWorldView).GetField("worldCamera", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
            Transform deck = view.transform.Find("Vehicle Deck Root");
            Vector2 point = camera.WorldToScreenPoint(deck.TransformPoint(new Vector3(-3.0f, 0f, -.75f)));
            MethodInfo pick = typeof(NomadFoundationWorldView).GetMethod("TryGetPointerPose", BindingFlags.Instance | BindingFlags.NonPublic);
            var before = new object[] { point, null };
            Assert.That(pick.Invoke(view, before), Is.True);
            view.RoofPresentation.ToggleExteriorPreference();
            context.ExecuteCommand(new EnterFoundationBuildModeCommand());
            yield return null;
            var after = new object[] { point, null };
            Assert.That(pick.Invoke(view, after), Is.True);
            Assert.That(after[1], Is.EqualTo(before[1]), "自动剖开后仍应选中同一甲板位置。");
            var roof = FindRoofSceneComponent<FoundationRoofVisual>(scene);
            Assert.That(roof.RoofRenderers.All(r => r.enabled && r.shadowCastingMode == ShadowCastingMode.ShadowsOnly), Is.True);
            Rect button = FoundationHudLayout.GetRoofControlRect(Screen.width, Screen.height);
            Vector2 buttonPoint = new(button.center.x, Screen.height - button.center.y);
            Assert.That(ui.IsScreenPointBlocked(buttonPoint), Is.True);
            Assert.That(ui.IsScreenPointBlocked(new Vector2(button.xMax + 4f, buttonPoint.y)), Is.False);
            context.ExecuteCommand(new ExitFoundationBuildModeCommand());
            Assert.That(view.RoofPresentation.ExteriorVisible, Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator RoofToggling_KeepsWarmLightsCollisionAndRealCarriedWaterTaskAlive()
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            var context = FindRoofSceneComponent<NomadFoundationContext>(scene);
            var view = FindRoofSceneComponent<NomadFoundationWorldView>(scene);
            var binding = FindRoofSceneComponent<FoundationRoofVisual>(scene);
            var read = context.ExecuteCommand(new GetFoundationReadModelCommand());
            var lights = binding.GetComponentsInChildren<Light>(true);
            Assert.That(lights.Length, Is.EqualTo(2));
            var colliders = view.GetComponentsInChildren<Collider>(true);
            var colliderStates = colliders.Select(c => c.enabled).ToArray();
            context.ExecuteCommand(new SetFoundationSpeedCommand(4f));
            context.ExecuteCommand(new SetFoundationPausedCommand(false));
            float deadline = Time.realtimeSinceStartup + 30f;
            while (!(read.WaterCanWaterMilliliters.CurrentValue > 0 &&
                     read.WaterCanLocation.CurrentValue == FoundationWaterCanLocation.Resident &&
                     read.Residents.Any(r => r.ResidentPhase.CurrentValue == FoundationResidentPhase.MovingToDrinkingStation)))
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "屋顶切换期间未完成真实取水并进入搬运。");
                view.RoofPresentation.ToggleExteriorPreference();
                yield return null;
            }
            context.ExecuteCommand(new SetFoundationPausedCommand(true));
            yield return null;
            string state = PausedWorldState(read);
            foreach (var _ in Enumerable.Range(0, 4))
            {
                view.RoofPresentation.ToggleExteriorPreference();
                yield return null;
                Assert.That(PausedWorldState(read), Is.EqualTo(state));
                Assert.That(lights.All(l => l.enabled && l.gameObject.activeInHierarchy), Is.True);
                Assert.That(colliders.Select(c => c.enabled), Is.EqualTo(colliderStates));
            }
            Transform can = view.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Water Can 01 [physical carrier]");
            Transform carrier = can.parent;
            Assert.That(carrier.GetComponent<ResidentHumanoidPresentation>(), Is.Not.Null);
            Vector3 position = carrier.position;
            context.ExecuteCommand(new SetFoundationPausedCommand(false));
            deadline = Time.realtimeSinceStartup + 5f;
            while (Vector3.Distance(carrier.position, position) < .03f && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(Vector3.Distance(carrier.position, position), Is.GreaterThan(.03f));
            context.ExecuteCommand(new SetFoundationPausedCommand(true));
            LogAssert.NoUnexpectedReceived();
        }

        private static IEnumerator PressKey(Keyboard keyboard, Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            yield return null; yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
        }

        private static T FindRoofSceneComponent<T>(Scene scene) where T : Component =>
            scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true)).Single();

        private static string PausedWorldState(FoundationReadModel read) =>
            $"{read.SimulationTick.CurrentValue}/{read.VehicleWaterMilliliters.CurrentValue}/{read.DrinkingStationWaterMilliliters.CurrentValue}/" +
            $"{read.WaterCanWaterMilliliters.CurrentValue}/{read.WaterCanLocation.CurrentValue}/{read.WaterCanCarrierId.CurrentValue}/" +
            string.Join(";", read.Residents.Select(r => $"{r.StableId}:{r.ResidentPhase.CurrentValue}:{r.ResidentLocalPosition.CurrentValue:R}"));
    }
}
#endif
