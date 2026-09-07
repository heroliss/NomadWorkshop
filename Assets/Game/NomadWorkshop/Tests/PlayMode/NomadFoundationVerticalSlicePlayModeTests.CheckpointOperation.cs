using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Framework.Storage;
using Game.NomadWorkshop.Foundation;
using Game.NomadWorkshop.Persistence;
using Game.NomadWorkshop.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.NomadWorkshop.PlayMode.Tests
{
    public sealed partial class NomadFoundationVerticalSlicePlayModeTests
    {
        private ControlledFoundationStorage _checkpointStorage;
        private const string PlayerTestSlot = "manual-trip";

        // 真实 FileStorageProvider 外包一层可控的物理完成边界。读取先取旧字节再等待，故意不在门后
        // 检查取消，证明业务必须拒绝迟到结果；不替换运行中的 Utility，也不写入玩家 persistentDataPath。
        private sealed class ControlledFoundationStorage : IStorageProvider
        {
            private readonly string _parent = Path.GetFullPath(Path.Combine(Application.temporaryCachePath, "nomad-checkpoint-tests"));
            private readonly FileStorageProvider _files;
            private UniTaskCompletionSource _readGate;
            private UniTaskCompletionSource _writeGate;
            internal volatile bool ReadEntered, WriteEntered, Disposed;
            internal Exception WriteFailure;
            internal ControlledFoundationStorage() => _files = new FileStorageProvider(Path.Combine(_parent, Guid.NewGuid().ToString("N")));
            internal string SlotPath => Path.Combine(_files.RootPath, NomadWorkshopStorageKeys.ProgressSlot(PlayerTestSlot) + ".sav");
            internal void HoldRead() { ReadEntered = false; _readGate = new UniTaskCompletionSource(); }
            internal void HoldWrite() { WriteEntered = false; _writeGate = new UniTaskCompletionSource(); }
            internal void ReleaseRead() => _readGate?.TrySetResult();
            internal void ReleaseAll() { ReleaseRead(); _writeGate?.TrySetResult(); }
            public async UniTask<byte[]> ReadAsync(string key, CancellationToken ct)
            {
                var bytes = await _files.ReadAsync(key, ct);
                if (_readGate != null) { ReadEntered = true; await _readGate.Task; _readGate = null; }
                return bytes;
            }
            public async UniTask WriteAsync(string key, byte[] bytes, CancellationToken ct)
            {
                if (_writeGate != null) { WriteEntered = true; await _writeGate.Task; _writeGate = null; }
                if (WriteFailure != null) throw WriteFailure;
                await _files.WriteAsync(key, bytes, ct);
            }
            public UniTask<byte[]> ReadBackupAsync(string key, CancellationToken ct) => _files.ReadBackupAsync(key, ct);
            public bool Exists(string key) => _files.Exists(key);
            public UniTask DeleteAsync(string key, CancellationToken ct) => _files.DeleteAsync(key, ct);
            public UniTask<IReadOnlyList<string>> ListKeysAsync(string prefix, CancellationToken ct) => _files.ListKeysAsync(prefix, ct);
            public void Dispose()
            {
                _files.Dispose();
                // 只有本次夹具创建的 GUID 子目录可清理；框架保证接纳的 IO 已到物理终态。
                string path = Path.GetFullPath(_files.RootPath);
                if (!path.StartsWith(_parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("测试存档目录超出隔离根。");
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                Disposed = true;
            }
        }

        private async UniTask WaitForStorageGate(Func<bool> entered)
        {
            float deadline = Time.realtimeSinceStartup + 5f;
            while (!entered() && Time.realtimeSinceStartup < deadline) await UniTask.Yield();
            Assert.That(entered(), Is.True, "IO 未进入预期的物理等待点。");
        }

        private static async UniTask ExpectCheckpointCanceled(UniTask task)
        {
            try { await task; }
            catch (OperationCanceledException) { return; }
            Assert.Fail("撤销的操作必须以 OperationCanceledException 结束。");
        }

        [UnityTest]
        public IEnumerator PlayerCheckpoint_RealFilesFreezeAndRestoreNativeCohort_ThenResumeNeeds()
        {
            yield return PrepareNativeCohort();
            _context.ExecuteCommand(new SetFoundationJourneyDestinationCommand(NomadJourneyEndpoint.Destination));
            yield return null;
            yield return UniTask.ToCoroutine(async () =>
            {
                _checkpointStorage.HoldWrite();
                var save = _context.ExecuteCommandAsync(new SaveFoundationCheckpointCommand(PlayerTestSlot)).Preserve();
                await WaitForStorageGate(() => _checkpointStorage.WriteEntered);
                long tick = _model.SimulationTick.Value;
                var motors = NativeMotors();
                var positions = motors.Select(x => x.transform.position).ToArray();
                Assert.That(_model.CheckpointBusy.Value && _model.IsPaused.Value, Is.True);
                for (var frame = 0; frame < 8; frame++) await UniTask.Yield();
                Assert.That(_model.SimulationTick.Value, Is.EqualTo(tick));
                for (var i = 0; i < motors.Length; i++)
                    Assert.That(Vector3.Distance(motors[i].transform.position, positions[i]), Is.LessThan(0.003f));
                _checkpointStorage.ReleaseAll();
                await save;
                Assert.That(_model.IsPaused.Value || _model.CheckpointBusy.Value, Is.False);
                Assert.That(File.Exists(_checkpointStorage.SlotPath), Is.True);
                Assert.That(_model.CheckpointFeedback.Value, Does.Contain("已保存"));
                _context.ExecuteCommand(new SetFoundationPausedCommand(true));
                _context.ExecuteCommand(new RunFoundationSoakHarnessCommand(1000, 100));
                Assert.That(_model.SimulationTick.Value, Is.GreaterThan(tick));
                bool loaded = await _context.ExecuteCommandAsync(new LoadFoundationCheckpointCommand(PlayerTestSlot));
                Assert.That(loaded, Is.True);
                Assert.That(_model.SimulationTick.Value, Is.EqualTo(tick));
                Assert.That(_model.IsPaused.Value && !_model.CheckpointBusy.Value, Is.True);
                Assert.That(_model.JourneyDestination.Value, Is.EqualTo(NomadJourneyEndpoint.Destination));
                await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
                Assert.That(motors.All(x => x == null), Is.True);
                Assert.That(NativeMotors().Length, Is.EqualTo(3));
                _context.ExecuteCommand(new SetFoundationPausedCommand(false));
                float deadline = Time.realtimeSinceStartup + 20f;
                while (Time.realtimeSinceStartup < deadline && ReadCohort().Residents.Any(x => x.CompletedDrinkCount.CurrentValue == 0))
                    await UniTask.Yield();
                Assert.That(ReadCohort().Residents.All(x => x.CompletedDrinkCount.CurrentValue > 0), Is.True, CohortDiagnostic());
                _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            });
        }

        [UnityTest]
        public IEnumerator PlayerCheckpoint_MissingOrIncompatibleData_PreservesCurrentWorldAndPause() => UniTask.ToCoroutine(async () =>
        {
            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            string before = JsonUtility.ToJson(CaptureStopCheckpoint());
            Assert.That(await _context.ExecuteCommandAsync(new LoadFoundationCheckpointCommand(PlayerTestSlot)), Is.False);
            Assert.That(_model.CheckpointFeedback.Value, Does.Contain("没有可用"));
            Assert.That(JsonUtility.ToJson(CaptureStopCheckpoint()), Is.EqualTo(before));
            var bad = CaptureStopCheckpoint();
            bad.Version = int.MaxValue;
            await _context.GetUtility<IStorageUtility>().Save(NomadWorkshopStorageKeys.ProgressSlot(PlayerTestSlot), bad);
            bool rejected = false;
            try { await _context.ExecuteCommandAsync(new LoadFoundationCheckpointCommand(PlayerTestSlot)); }
            catch (NotSupportedException) { rejected = true; }
            Assert.That(rejected, Is.True);
            Assert.That(_model.CheckpointFeedback.Value, Does.Contain("读取失败"));
            Assert.That(_model.IsPaused.Value && !_model.CheckpointBusy.Value, Is.True);
            Assert.That(JsonUtility.ToJson(CaptureStopCheckpoint()), Is.EqualTo(before));
        });

        [UnityTest]
        public IEnumerator PlayerCheckpoint_CorruptMainUsesBackup_BothCorruptKeepWorld() => UniTask.ToCoroutine(async () =>
        {
            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            await _context.ExecuteCommandAsync(new SaveFoundationCheckpointCommand(PlayerTestSlot));
            long originalTick = _model.SimulationTick.Value;
            _context.ExecuteCommand(new RunFoundationSoakHarnessCommand(1000, 100));
            await _context.ExecuteCommandAsync(new SaveFoundationCheckpointCommand(PlayerTestSlot));
            File.WriteAllText(_checkpointStorage.SlotPath, "{broken");
            LogAssert.Expect(LogType.Warning, new Regex("主文件反序列化失败"));
            LogAssert.Expect(LogType.Warning, new Regex("主文件不可用，已回退上一版备份"));
            Assert.That(await _context.ExecuteCommandAsync(new LoadFoundationCheckpointCommand(PlayerTestSlot)), Is.True);
            Assert.That(_model.SimulationTick.Value, Is.EqualTo(originalTick));
            string before = JsonUtility.ToJson(CaptureStopCheckpoint());
            File.WriteAllText(_checkpointStorage.SlotPath + ".bak", "{also-broken");
            LogAssert.Expect(LogType.Warning, new Regex("主文件反序列化失败"));
            LogAssert.Expect(LogType.Warning, new Regex("备份反序列化失败"));
            LogAssert.Expect(LogType.Error, new Regex("主文件与备份均无法反序列化"));
            Assert.That(await _context.ExecuteCommandAsync(new LoadFoundationCheckpointCommand(PlayerTestSlot)), Is.False);
            Assert.That(JsonUtility.ToJson(CaptureStopCheckpoint()), Is.EqualTo(before));
            Assert.That(_model.IsPaused.Value && !_model.CheckpointBusy.Value, Is.True);
            LogAssert.NoUnexpectedReceived();
        });

        [UnityTest]
        public IEnumerator PlayerCheckpoint_CancelDelayedLoad_RejectsLateBytesAndReleasesPause() => UniTask.ToCoroutine(async () =>
        {
            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            await _context.ExecuteCommandAsync(new SaveFoundationCheckpointCommand(PlayerTestSlot));
            _context.ExecuteCommand(new RunFoundationSoakHarnessCommand(1000, 100));
            _context.ExecuteCommand(new SetFoundationPausedCommand(false));
            _checkpointStorage.HoldRead();
            var load = _context.ExecuteCommandAsync(new LoadFoundationCheckpointCommand(PlayerTestSlot)).Preserve();
            await WaitForStorageGate(() => _checkpointStorage.ReadEntered);
            long tick = _model.SimulationTick.Value;
            _context.ExecuteCommand(new CancelFoundationCheckpointCommand());
            Assert.That(_model.IsPaused.Value || _model.CheckpointBusy.Value, Is.False);
            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            string feedback = _model.CheckpointFeedback.Value;
            _checkpointStorage.ReleaseAll();
            await ExpectCheckpointCanceled(load.AsUniTask());
            Assert.That(_model.SimulationTick.Value, Is.EqualTo(tick));
            Assert.That(_model.IsPaused.Value, Is.True, "迟到完成不能覆盖取消后玩家自己的暂停选择。");
            Assert.That(_model.CheckpointFeedback.Value, Is.EqualTo(feedback));
        });

        [UnityTest]
        public IEnumerator PlayerCheckpoint_ResetDuringDelayedLoad_CannotOverwriteNewWorld() => UniTask.ToCoroutine(async () =>
        {
            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            await _context.ExecuteCommandAsync(new SaveFoundationCheckpointCommand(PlayerTestSlot));
            _checkpointStorage.HoldRead();
            var load = _context.ExecuteCommandAsync(new LoadFoundationCheckpointCommand(PlayerTestSlot)).Preserve();
            await WaitForStorageGate(() => _checkpointStorage.ReadEntered);
            Assert.That(_context.ExecuteCommand(new ResetFoundationForSoakHarnessCommand(94217)), Is.True);
            string reset = JsonUtility.ToJson(CaptureStopCheckpoint());
            string feedback = _model.CheckpointFeedback.Value;
            _checkpointStorage.ReleaseAll();
            await ExpectCheckpointCanceled(load.AsUniTask());
            Assert.That(JsonUtility.ToJson(CaptureStopCheckpoint()), Is.EqualTo(reset));
            Assert.That(CaptureStopCheckpoint().WorldSeed, Is.EqualTo(94217));
            Assert.That(_model.CheckpointFeedback.Value, Is.EqualTo(feedback));
            Assert.That(_model.IsPaused.Value && !_model.CheckpointBusy.Value, Is.True);
        });

        [UnityTest]
        public IEnumerator PlayerCheckpoint_DestroyContextDuringDelayedLoad_DrainsProviderWithoutDeadViewNotifications() => UniTask.ToCoroutine(async () =>
        {
            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            await _context.ExecuteCommandAsync(new SaveFoundationCheckpointCommand(PlayerTestSlot));
            _checkpointStorage.HoldRead();
            var load = _context.ExecuteCommandAsync(new LoadFoundationCheckpointCommand(PlayerTestSlot)).Preserve();
            await WaitForStorageGate(() => _checkpointStorage.ReadEntered);
            UnityEngine.Object.Destroy(_root);
            await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
            Assert.That(_context == null, Is.True);
            Assert.That(_checkpointStorage.Disposed, Is.False, "接纳的 IO 尚未返回，Provider 不能提前释放。");
            _checkpointStorage.ReleaseAll();
            await ExpectCheckpointCanceled(load.AsUniTask());
            await UniTask.Yield();
            Assert.That(_checkpointStorage.Disposed, Is.True);
            LogAssert.NoUnexpectedReceived();
        });

        [UnityTest]
        public IEnumerator PlayerCheckpoint_DuplicateBuildAndUnpauseCannotCrossPendingSave_WriteFailureRestoresControl() => UniTask.ToCoroutine(async () =>
        {
            _context.ExecuteCommand(new SetFoundationPausedCommand(false));
            _checkpointStorage.HoldWrite();
            _checkpointStorage.WriteFailure = new IOException("测试介质拒绝写入");
            var save = _context.ExecuteCommandAsync(new SaveFoundationCheckpointCommand(PlayerTestSlot)).Preserve();
            await WaitForStorageGate(() => _checkpointStorage.WriteEntered);
            bool rejected = false;
            try { await _context.ExecuteCommandAsync(new LoadFoundationCheckpointCommand(PlayerTestSlot)); }
            catch (InvalidOperationException) { rejected = true; }
            Assert.That(rejected, Is.True);
            _context.ExecuteCommand(new EnterFoundationBuildModeCommand());
            _context.ExecuteCommand(new BeginFacilityPlacementCommand("drinking-station"));
            _context.ExecuteCommand(new SetFoundationPausedCommand(false));
            Assert.That(_model.IsPaused.Value, Is.True);
            Assert.That(_model.InteractionMode.Value, Is.Not.EqualTo(FoundationInteractionMode.Build));
            Assert.That(_model.BuildFeedback.Value, Does.Contain("存读档"));
            Assert.That(_context.ExecuteCommand(new RunFoundationSoakHarnessCommand(1000, 100)).StopReason,
                Is.EqualTo(FoundationSoakStopReason.CheckpointOperationActive));
            _checkpointStorage.ReleaseAll();
            bool failed = false;
            try { await save; } catch (IOException) { failed = true; }
            Assert.That(failed, Is.True);
            Assert.That(_model.CheckpointFeedback.Value, Does.Contain("测试介质拒绝写入"));
            Assert.That(_model.IsPaused.Value || _model.CheckpointBusy.Value, Is.False);
        });

        [UnityTest]
        public IEnumerator PlayerCheckpoint_ActiveNavigationTransactionRejectsSave_AndStillCommitsBuilding()
        {
            _worldView.enabled = false;
            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            _context.ExecuteCommand(new BeginFacilityPlacementCommand("drinking-station"));
            _context.ExecuteCommand(new MoveFacilityPreviewCommand(0, 0));
            yield return null;
            _context.ExecuteCommand(new ConfirmFacilityPlacementCommand());
            Assert.That(_model.BuildTransactionPhase.Value, Is.EqualTo(FoundationBuildTransactionPhase.UpdatingCandidateNavigation));
            var save = _context.ExecuteCommandAsync(new SaveFoundationCheckpointCommand(PlayerTestSlot)).Preserve();
            Assert.That(_model.BuildTransactionPhase.Value, Is.EqualTo(FoundationBuildTransactionPhase.UpdatingCandidateNavigation));
            yield return UniTask.ToCoroutine(async () =>
            {
                bool rejected = false;
                try { await save; } catch (InvalidOperationException) { rejected = true; }
                Assert.That(rejected, Is.True);
                Assert.That(_model.CheckpointFeedback.Value, Does.Contain("等待建造"));
                Assert.That(_model.CheckpointBusy.Value, Is.False);
                Assert.That(File.Exists(_checkpointStorage.SlotPath), Is.False);
            });
            yield return WaitForBuildTransaction();
            Assert.That(_context.ExecuteCommand(new GetFoundationFacilitiesCommand()).Length, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator PlayerCheckpoint_OldCanceledLoadCannotFinishNewQueuedSave() => UniTask.ToCoroutine(async () =>
        {
            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            await _context.ExecuteCommandAsync(new SaveFoundationCheckpointCommand(PlayerTestSlot));
            _checkpointStorage.HoldRead();
            var oldLoad = _context.ExecuteCommandAsync(new LoadFoundationCheckpointCommand(PlayerTestSlot)).Preserve();
            await WaitForStorageGate(() => _checkpointStorage.ReadEntered);
            _context.ExecuteCommand(new CancelFoundationCheckpointCommand());
            _checkpointStorage.HoldWrite();
            var newSave = _context.ExecuteCommandAsync(new SaveFoundationCheckpointCommand(PlayerTestSlot)).Preserve();
            _checkpointStorage.ReleaseRead();
            await ExpectCheckpointCanceled(oldLoad.AsUniTask());
            await WaitForStorageGate(() => _checkpointStorage.WriteEntered);
            Assert.That(_model.CheckpointBusy.Value && _model.IsPaused.Value, Is.True);
            Assert.That(_model.CheckpointFeedback.Value, Does.Contain("正在保存"), "旧读取的 finally 不能清掉新操作状态。");
            _checkpointStorage.ReleaseAll();
            await newSave;
            Assert.That(_model.CheckpointBusy.Value, Is.False);
            Assert.That(_model.CheckpointFeedback.Value, Does.Contain("已保存"));
        });

        [UnityTest]
        public IEnumerator PlayerCheckpoint_ExternalCancellationRemainsCancellation_AndNeverAppliesLateBytes() => UniTask.ToCoroutine(async () =>
        {
            _context.ExecuteCommand(new SetFoundationPausedCommand(true));
            await _context.ExecuteCommandAsync(new SaveFoundationCheckpointCommand(PlayerTestSlot));
            _context.ExecuteCommand(new RunFoundationSoakHarnessCommand(1000, 100));
            string before = JsonUtility.ToJson(CaptureStopCheckpoint());
            using var source = new CancellationTokenSource();
            _checkpointStorage.HoldRead();
            var load = _context.ExecuteCommandAsync(new LoadFoundationCheckpointCommand(PlayerTestSlot), source.Token).Preserve();
            await WaitForStorageGate(() => _checkpointStorage.ReadEntered);
            source.Cancel();
            _checkpointStorage.ReleaseAll();
            await ExpectCheckpointCanceled(load.AsUniTask());
            Assert.That(JsonUtility.ToJson(CaptureStopCheckpoint()), Is.EqualTo(before));
            Assert.That(_model.CheckpointBusy.Value, Is.False);
            Assert.That(_model.CheckpointFeedback.Value, Does.Contain("已取消"));
        });
    }
}
