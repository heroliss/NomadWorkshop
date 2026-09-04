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
            NomadWorkshopSaveData checkpoint =
                ctx.GetSystem<NomadFoundationSystem>().CaptureCheckpoint();
            await ctx.ExecuteCommandAsync(
                new SaveNomadWorkshopProgressCommand(_slotId, checkpoint),
                cancellationToken);
        }
    }

    /// <summary>
    /// 从统一存储槽读取、验证并恢复 Foundation。返回 false 只表示槽位不存在；损坏或不兼容会明确抛出。
    /// </summary>
    public readonly struct LoadFoundationCheckpointCommand : IAsyncCommand<bool>
    {
        private readonly string _slotId;

        public LoadFoundationCheckpointCommand(string slotId) => _slotId = slotId;

        public async UniTask<bool> ExecuteAsync(
            ICommandContext ctx,
            CancellationToken cancellationToken)
        {
            NomadWorkshopSaveData checkpoint = await ctx.ExecuteCommandAsync<
                LoadNomadWorkshopProgressCommand,
                NomadWorkshopSaveData>(
                new LoadNomadWorkshopProgressCommand(_slotId),
                cancellationToken);
            if (checkpoint == null) return false;
            ctx.GetSystem<NomadFoundationSystem>().RestoreCheckpoint(checkpoint);
            return true;
        }
    }
}
