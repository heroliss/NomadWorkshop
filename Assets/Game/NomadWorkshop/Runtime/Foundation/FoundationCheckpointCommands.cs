using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Framework.Command;
using Game.NomadWorkshop.Persistence;
using Game.NomadWorkshop.Simulation.Persistence;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>在主线程捕获当前 Foundation 已提交业务真值，不直接执行文件 IO。</summary>
    public readonly struct CaptureFoundationCheckpointCommand :
        ICommand<NomadWorkshopSaveData>
    {
        public NomadWorkshopSaveData Execute(ICommandContext ctx) =>
            ctx.GetSystem<NomadFoundationSystem>().CaptureCheckpoint();
    }

    /// <summary>从内存检查点重建 Foundation；瞬时路径、租约和引擎对象由 System 重新生成。</summary>
    public readonly struct RestoreFoundationCheckpointCommand : ICommand
    {
        private readonly NomadWorkshopSaveData _checkpoint;

        public RestoreFoundationCheckpointCommand(NomadWorkshopSaveData checkpoint) =>
            _checkpoint = checkpoint;

        public void Execute(ICommandContext ctx) =>
            ctx.GetSystem<NomadFoundationSystem>().RestoreCheckpoint(_checkpoint);
    }

    /// <summary>捕获同一帧业务检查点，再复用项目统一的版本校验与原子存储命令落盘。</summary>
    public readonly struct SaveFoundationCheckpointCommand : IAsyncCommand
    {
        private readonly string _slotId;

        public SaveFoundationCheckpointCommand(string slotId) => _slotId = slotId;

        public async UniTask ExecuteAsync(
            ICommandContext ctx,
            CancellationToken cancellationToken)
        {
            var system = ctx.GetSystem<NomadFoundationSystem>();
            using var operation = system.BeginCheckpointOperation(true, cancellationToken, ctx.CancellationToken);
            string feedback = "保存未完成。";
            try
            {
                system.RequireCheckpointOperation(operation);
                NomadWorkshopSaveData checkpoint = system.CaptureCheckpoint();
                await ctx.ExecuteCommandAsync(new SaveNomadWorkshopProgressCommand(_slotId, checkpoint), operation.Token);
                system.RequireCheckpointOperation(operation);
                feedback = "旅程已保存到手动槽。";
            }
            catch (OperationCanceledException)
            {
                feedback = "已取消保存等待；已经写入的存档可能保留。";
                throw;
            }
            catch (Exception error)
            {
                feedback = $"保存失败：{error.Message}";
                throw;
            }
            finally { system.EndCheckpointOperation(operation, feedback); }
        }
    }

    /// <summary>
    /// 从统一存储槽读取、验证并恢复 Foundation。false 表示没有可用数据（含缺档和主备均损坏），
    /// 保留当前世界；业务校验/不兼容异常仍传播，取消保持 OperationCanceledException。
    /// </summary>
    public readonly struct LoadFoundationCheckpointCommand : IAsyncCommand<bool>
    {
        private readonly string _slotId;

        public LoadFoundationCheckpointCommand(string slotId) => _slotId = slotId;

        public async UniTask<bool> ExecuteAsync(
            ICommandContext ctx,
            CancellationToken cancellationToken)
        {
            var system = ctx.GetSystem<NomadFoundationSystem>();
            using var operation = system.BeginCheckpointOperation(false, cancellationToken, ctx.CancellationToken);
            string feedback = "读取未完成。";
            try
            {
                NomadWorkshopSaveData checkpoint = await ctx.ExecuteCommandAsync<
                    LoadNomadWorkshopProgressCommand, NomadWorkshopSaveData>(
                    new LoadNomadWorkshopProgressCommand(_slotId), operation.Token);
                system.RequireCheckpointOperation(operation);
                if (checkpoint == null)
                {
                    feedback = "没有可用的旅程存档（尚未保存或文件无法读取）；当前旅程已保留。";
                    return false;
                }
                system.RestoreCheckpoint(checkpoint, operation);
                system.RequireCheckpointOperation(operation);
                feedback = "旅程已读取，居民将从保存的业务状态重新安排工作。";
                return true;
            }
            catch (OperationCanceledException)
            {
                feedback = "已取消读取，当前旅程已保留。";
                throw;
            }
            catch (Exception error)
            {
                feedback = $"读取失败：{error.Message}";
                throw;
            }
            finally { system.EndCheckpointOperation(operation, feedback); }
        }
    }

    /// <summary>取消当前存读档等待；迟到的读取不能再覆盖世界。</summary>
    public readonly struct CancelFoundationCheckpointCommand : ICommand
    {
        public void Execute(ICommandContext ctx) => ctx.GetSystem<NomadFoundationSystem>().CancelCheckpointOperation();
    }
}
