using System.Collections;
using Game.NomadWorkshop.Foundation;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.NomadWorkshop.PlayMode.Tests
{
    public sealed partial class NomadFoundationVerticalSlicePlayModeTests
    {
        [UnityTest]
        public IEnumerator ResidentReadModel_ResetAndRestoreKeepExistingSubscriptionsAndVisualsLive()
        {
            _worldView.enabled = false;
            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            FoundationReadModel read = _context.ExecuteCommand(new GetFoundationReadModelCommand());
            FoundationResidentModelState record = _model.PrimaryResident;
            float observedThirst = -1f;
            using var subscription = read.PrimaryResident.ResidentThirst.Subscribe(value => observedThirst = value);
            var saved = _context.ExecuteCommand(new CaptureFoundationCheckpointCommand());
            saved.Residents[0].ThirstPermille = 250;
            saved.Residents[0].Pose = new Game.NomadWorkshop.Simulation.Persistence.QuantizedDeckPose(1000, 2000, 900);

            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(saved));
            Assert.That(observedThirst, Is.EqualTo(0.25f).Within(0.0001f));
            Transform residentVisual = _worldView.transform.Find("Vehicle Deck Root/Resident 01");
            Assert.That(Vector3.Distance(residentVisual.localPosition,
                read.PrimaryResident.ResidentLocalPosition.CurrentValue), Is.LessThan(0.001f),
                "现有 WorldView 必须仍跟随被恢复的居民记录，不能要求重建画面或重新订阅。");

            Assert.That(_context.ExecuteCommand(new ResetFoundationForSoakHarnessCommand(1741)), Is.True);
            Assert.That(_model.PrimaryResident, Is.SameAs(record));
            FoundationReadModel afterReset = _context.ExecuteCommand(new GetFoundationReadModelCommand());
            Assert.That(afterReset.PrimaryResident.ResidentThirst, Is.SameAs(read.PrimaryResident.ResidentThirst));
            Assert.That(observedThirst, Is.EqualTo(0.78f).Within(0.0001f));
            StepJourney(1000);
            Assert.That(observedThirst, Is.GreaterThan(0.78f), "旧只读束应继续收到正式业务步推进的需求变化。");

            _context.ExecuteCommand(new RestoreFoundationCheckpointCommand(saved));
            Assert.That(_model.PrimaryResident, Is.SameAs(record));
            Assert.That(observedThirst, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(read.PrimaryResident.StableId, Is.EqualTo(saved.Residents[0].ResidentId));
            Assert.That(Vector3.Distance(residentVisual.localPosition,
                read.PrimaryResident.ResidentLocalPosition.CurrentValue), Is.LessThan(0.001f));
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator BuildFeedback_DoesNotReplaceResidentTaskOrDiagnostic()
        {
            _worldView.enabled = false;
            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            FoundationReadModel read = _context.ExecuteCommand(new GetFoundationReadModelCommand());
            string task = read.PrimaryResident.CurrentTask.CurrentValue;
            string blocker = read.PrimaryResident.LastBlocker.CurrentValue;
            _context.ExecuteCommand(new BeginFacilityPlacementCommand("drinking-station"));
            _context.ExecuteCommand(new MoveFacilityPreviewCommand(0, 0));
            yield return null;
            _context.ExecuteCommand(new ConfirmFacilityPlacementCommand());
            Assert.That(read.BuildTransactionPhase.CurrentValue,
                Is.EqualTo(FoundationBuildTransactionPhase.UpdatingCandidateNavigation));
            StringAssert.Contains("正在验证", read.BuildFeedback.CurrentValue);
            Assert.That(read.PrimaryResident.CurrentTask.CurrentValue, Is.EqualTo(task));
            Assert.That(read.PrimaryResident.LastBlocker.CurrentValue, Is.EqualTo(blocker));
            yield return WaitForBuildTransaction();
            StringAssert.Contains("已建造", read.BuildFeedback.CurrentValue);
            Assert.That(read.PrimaryResident.CurrentTask.CurrentValue, Is.EqualTo(task));
            Assert.That(read.PrimaryResident.LastBlocker.CurrentValue, Is.EqualTo(blocker));
        }

        [Test]
        public void ResidentRecords_SerializationPreservesIdentityAndDoesNotSharePersonalReactiveState()
        {
            var first = new FoundationResidentModelState("resident-02", 0xF02UL);
            first.ResidentLocalPosition.Value = new Vector3(1f, 0f, 2f);
            first.ResidentThirst.Value = 0.31f;
            first.CompletedDrinkCount.Value = 4;
            var restored = JsonUtility.FromJson<FoundationResidentModelState>(JsonUtility.ToJson(first));
            var firstRead = new FoundationResidentReadModel(first);
            var restoredRead = new FoundationResidentReadModel(restored);
            Assert.That(restoredRead.StableId, Is.EqualTo("resident-02"));
            Assert.That(restoredRead.OwnerId, Is.EqualTo(0xF02UL));
            Assert.That(restoredRead.ResidentLocalPosition.CurrentValue, Is.EqualTo(new Vector3(1f, 0f, 2f)));
            Assert.That(restoredRead.ResidentThirst.CurrentValue, Is.EqualTo(0.31f));
            Assert.That(restoredRead.CompletedDrinkCount.CurrentValue, Is.EqualTo(4));
            first.ResidentLocalPosition.Value = Vector3.zero;
            first.ResidentThirst.Value = 0.8f;
            first.CompletedDrinkCount.Value++;
            Assert.That(firstRead.ResidentThirst.CurrentValue, Is.EqualTo(0.8f));
            Assert.That(restoredRead.ResidentLocalPosition.CurrentValue, Is.EqualTo(new Vector3(1f, 0f, 2f)));
            Assert.That(restoredRead.ResidentThirst.CurrentValue, Is.EqualTo(0.31f));
            Assert.That(restoredRead.CompletedDrinkCount.CurrentValue, Is.EqualTo(4));
        }
    }
}
