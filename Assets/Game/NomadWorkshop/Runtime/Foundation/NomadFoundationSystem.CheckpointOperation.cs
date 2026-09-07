using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Game.NomadWorkshop.Foundation
{
    public sealed partial class NomadFoundationSystem
    {
        private CheckpointOperation _checkpointOperation;

        /// <summary>
        /// 一次存读档对当前世界的临时所有权。System 撤销它，Command 在 IO 物理终态后释放 CTS；
        /// 已撤销操作不能恢复世界、写反馈或改变暂停选择。取消不承诺撤回已原子提交的磁盘写入。
        /// </summary>
        internal sealed class CheckpointOperation : IDisposable
        {
            internal readonly CancellationTokenSource Cancellation;
            internal readonly CancellationToken ContextLifetime;
            internal readonly bool WasPaused;
            internal readonly bool IsSaving;
            internal CancellationToken Token => Cancellation.Token;

            internal CheckpointOperation(bool wasPaused, bool isSaving, CancellationToken execution,
                CancellationToken contextLifetime, CancellationToken systemLifetime)
            {
                WasPaused = wasPaused;
                IsSaving = isSaving;
                ContextLifetime = contextLifetime;
                Cancellation = CancellationTokenSource.CreateLinkedTokenSource(execution, contextLifetime, systemLifetime);
            }

            public void Dispose() => Cancellation.Dispose();
        }

        internal CheckpointOperation BeginCheckpointOperation(bool saving, CancellationToken execution,
            CancellationToken contextLifetime)
        {
            execution.ThrowIfCancellationRequested();
            contextLifetime.ThrowIfCancellationRequested();
            EnsureCheckpointRuntimeReady();
            if (_checkpointOperation != null)
                throw new InvalidOperationException("已有存读档正在进行，请等待或取消后再操作。");
            if (_model.BuildTransactionPhase.Value != FoundationBuildTransactionPhase.Idle)
            {
                _model.CheckpointFeedback.Value = "请等待建造提交或回滚结束后再存读档。";
                throw new InvalidOperationException(_model.CheckpointFeedback.Value);
            }

            var operation = new CheckpointOperation(_model.IsPaused.Value, saving, execution,
                contextLifetime, this.GetCancellationTokenOnDestroy());
            SetPaused(true);
            _checkpointOperation = operation;
            _model.CheckpointBusy.Value = true;
            _model.CheckpointFeedback.Value = saving ? "正在保存旅程……" : "正在读取旅程……";
            return operation;
        }

        internal void RequireCheckpointOperation(CheckpointOperation operation)
        {
            operation.Token.ThrowIfCancellationRequested();
            if (this == null || !ReferenceEquals(operation, _checkpointOperation))
                throw new OperationCanceledException(operation.Token);
        }

        internal void EndCheckpointOperation(CheckpointOperation operation, string feedback)
        {
            if (this == null || !ReferenceEquals(operation, _checkpointOperation)) return;
            _checkpointOperation = null;
            // Context 可能先于子节点销毁。此时只能撤销所有权，不向仍存活的 View 发布数据。
            if (operation.ContextLifetime.IsCancellationRequested || _model == null) return;
            SetPaused(operation.WasPaused);
            _model.CheckpointBusy.Value = false;
            _model.CheckpointFeedback.Value = feedback;
        }

        /// <summary>撤销当前等待并恢复操作前的暂停选择；已提交的磁盘保存可能保留。</summary>
        public void CancelCheckpointOperation() => RevokeCheckpointOperation(publish: true);

        private void RevokeCheckpointOperation(bool publish)
        {
            var operation = _checkpointOperation;
            if (operation == null) return;
            _checkpointOperation = null;
            operation.Cancellation.Cancel();
            if (!publish || operation.ContextLifetime.IsCancellationRequested || _model == null) return;
            SetPaused(operation.WasPaused);
            _model.CheckpointBusy.Value = false;
            _model.CheckpointFeedback.Value = operation.IsSaving
                ? "已取消保存等待；已经写入的存档可能保留。"
                : "已取消读取，当前旅程继续保留。";
        }

        private bool RejectBuildDuringCheckpoint()
        {
            if (_checkpointOperation == null) return false;
            _model.BuildFeedback.Value = "存读档期间暂时不能建造，请等待或取消。";
            return true;
        }
    }
}
