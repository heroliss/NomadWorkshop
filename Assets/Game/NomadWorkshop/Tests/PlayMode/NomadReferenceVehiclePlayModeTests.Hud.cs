#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using Game.NomadWorkshop.Foundation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.NomadWorkshop.PlayMode.Tests
{
    public sealed partial class NomadReferenceVehiclePlayModeTests
    {
        [UnityTest]
        public IEnumerator PlayerHud_SelectionAndSpeedPreservePausedWorld_SpaceResumesAndPauses()
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            var context = FindRoofSceneComponent<NomadFoundationContext>(scene);
            var ui = FindRoofSceneComponent<NomadFoundationDebugView>(scene);
            var read = context.ExecuteCommand(new GetFoundationReadModelCommand());
            string before = PausedWorldState(read);
            for (int i = 2; i >= 0; i--)
            {
                ui.SelectResidentForTests(i);
                Assert.That(ui.SelectedResidentIndexForTests, Is.EqualTo(i));
                Assert.That(HudField<float>(ui, "_thirst"), Is.EqualTo(read.Residents[i].ResidentThirst.CurrentValue));
                Assert.That(HudField<FoundationResidentPhase>(ui, "_residentPhase"), Is.EqualTo(read.Residents[i].ResidentPhase.CurrentValue));
                yield return null;
            }
            Assert.That(ui.SetPlaybackSpeedForTests(4f), Is.True);
            Assert.That(read.SimulationSpeed.CurrentValue, Is.EqualTo(4f));
            Assert.That(PausedWorldState(read), Is.EqualTo(before));
            Keyboard previous = Keyboard.current;
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            keyboard.MakeCurrent();
            try
            {
                long tick = read.SimulationTick.CurrentValue;
                yield return PressKey(keyboard, Key.Space);
                Assert.That(read.IsPaused.CurrentValue, Is.False, "经 Input System 的空格键应进入 View.Update。");
                float deadline = Time.realtimeSinceStartup + 3f;
                while (read.SimulationTick.CurrentValue == tick && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(read.SimulationTick.CurrentValue, Is.GreaterThan(tick));
                yield return PressKey(keyboard, Key.Space);
                Assert.That(read.IsPaused.CurrentValue, Is.True);
                tick = read.SimulationTick.CurrentValue;
                for (int i = 0; i < 5; i++) yield return null;
                Assert.That(read.SimulationTick.CurrentValue, Is.EqualTo(tick));
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
                if (previous != null && previous.added) previous.MakeCurrent();
            }
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator PlayerHud_RealWaterDeliveryUpdatesResources_AndSelectedResidentAfterRestore()
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            var context = FindRoofSceneComponent<NomadFoundationContext>(scene);
            var ui = FindRoofSceneComponent<NomadFoundationDebugView>(scene);
            var read = context.ExecuteCommand(new GetFoundationReadModelCommand());
            long initialWater = read.VehicleWaterMilliliters.CurrentValue;
            ui.SelectResidentForTests(2);
            ui.SetPlaybackSpeedForTests(4f);
            ui.TogglePauseForTests();
            float deadline = Time.realtimeSinceStartup + 30f;
            while (read.DrinkingStationWaterMilliliters.CurrentValue == 0 && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(read.DrinkingStationWaterMilliliters.CurrentValue, Is.GreaterThan(0));
            Assert.That(read.VehicleWaterMilliliters.CurrentValue, Is.LessThan(initialWater));
            Assert.That(ui.TogglePauseForTests(), Is.True);
            Assert.That(HudField<int>(ui, "_vehicleWaterMilliliters"), Is.EqualTo(read.VehicleWaterMilliliters.CurrentValue));
            Assert.That(HudField<int>(ui, "_stationWaterMilliliters"), Is.EqualTo(read.DrinkingStationWaterMilliliters.CurrentValue));
            var checkpoint = context.ExecuteCommand(new CaptureFoundationCheckpointCommand());
            context.ExecuteCommand(new RestoreFoundationCheckpointCommand(checkpoint));
            yield return null; yield return null;
            Assert.That(ui.SelectedResidentIndexForTests, Is.EqualTo(2));
            Assert.That(HudField<float>(ui, "_thirst"), Is.EqualTo(read.Residents[2].ResidentThirst.CurrentValue));
            Assert.That(HudField<FoundationResidentPhase>(ui, "_residentPhase"), Is.EqualTo(read.Residents[2].ResidentPhase.CurrentValue));
            Assert.That(HudField<int>(ui, "_stationWaterMilliliters"), Is.EqualTo(read.DrinkingStationWaterMilliliters.CurrentValue));
            LogAssert.NoUnexpectedReceived();
        }

        private static T HudField<T>(NomadFoundationDebugView view, string name) =>
            (T)typeof(NomadFoundationDebugView).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
    }
}
#endif
