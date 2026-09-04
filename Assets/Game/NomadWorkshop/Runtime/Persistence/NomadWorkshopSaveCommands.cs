using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Framework.Command;
using Game.Framework.Storage;
using Game.NomadWorkshop.Simulation.Persistence;

namespace Game.NomadWorkshop.Persistence
{
    /// <summary>游牧工坊存档 key。key 是落盘契约，只增不改；槽位 id 被限制为单个安全段。</summary>
    public static class NomadWorkshopStorageKeys
    {
        public const string ProgressPrefix = "nomad-workshop/progress/";
        public const string DefaultSlotId = "slot-1";

        public static string ProgressSlot(string slotId)
        {
            if (string.IsNullOrWhiteSpace(slotId))
                throw new ArgumentException("存档槽位 id 不能为空。", nameof(slotId));
            for (var i = 0; i < slotId.Length; i++)
            {
                char c = slotId[i];
                bool legal = c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' ||
                             c >= '0' && c <= '9' || c == '-' || c == '_';
                if (!legal)
                    throw new ArgumentException(
                        $"存档槽位 id '{slotId}' 含非法字符 '{c}'；只允许字母、数字、-、_。",
                        nameof(slotId));
            }

            string key = ProgressPrefix + slotId;
            StorageKey.Validate(key);
            return key;
        }
    }

    /// <summary>
    /// 把调用方在同一帧捕获的完整业务快照经框架存储原子落盘。捕获世界与冻结提交边界由游戏 System 负责；
    /// Command 只负责契约校验和持久化编排。
    /// </summary>
    public readonly struct SaveNomadWorkshopProgressCommand : IAsyncCommand
    {
        private readonly string _slotId;
        private readonly NomadWorkshopSaveData _snapshot;

        public SaveNomadWorkshopProgressCommand(string slotId, NomadWorkshopSaveData snapshot)
        {
            _slotId = slotId;
            _snapshot = snapshot;
        }

        public async UniTask ExecuteAsync(
            ICommandContext ctx,
            CancellationToken cancellationToken)
        {
            string key = NomadWorkshopStorageKeys.ProgressSlot(_slotId);
            NomadWorkshopSaveContract.ValidateForSave(_snapshot);
            await ctx.GetUtility<IStorageUtility>().Save(key, _snapshot, cancellationToken);
        }
    }

    /// <summary>
    /// 读取并验证一个游戏进度槽。null 只表示没有可用存档；版本不支持或业务不变量损坏会明确抛出，
    /// 不能静默把已有进度当作新游戏。
    /// </summary>
    public readonly struct LoadNomadWorkshopProgressCommand : IAsyncCommand<NomadWorkshopSaveData>
    {
        private readonly string _slotId;

        public LoadNomadWorkshopProgressCommand(string slotId) => _slotId = slotId;

        public async UniTask<NomadWorkshopSaveData> ExecuteAsync(
            ICommandContext ctx,
            CancellationToken cancellationToken)
        {
            string key = NomadWorkshopStorageKeys.ProgressSlot(_slotId);
            NomadWorkshopSaveData data = await ctx.GetUtility<IStorageUtility>()
                .Load<NomadWorkshopSaveData>(key, cancellationToken);
            return NomadWorkshopSaveContract.PrepareAfterLoad(data);
        }
    }
}
