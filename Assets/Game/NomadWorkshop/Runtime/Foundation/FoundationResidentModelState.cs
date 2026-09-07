using System;
using R3;
using UnityEngine;
using UnityEngine.Serialization;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>
    /// 一名居民的 Model 记录。位置、个人行动投影和计数属于此身份，共享库存不放在这里。
    /// Model 拥有记录；执行器引用它，取消或替换执行器不会替换 R3 属性、转移订阅或复制世界资源。
    /// View 经 <see cref="FoundationResidentReadModel"/> 观察，写入仍走 System/Command。
    /// </summary>
    [Serializable]
    public sealed class FoundationResidentModelState
    {
        [SerializeField] private string stableId;
        [SerializeField] private ulong ownerId;

        /// <summary>检查点、库存和驾驶使用的稳定居民身份。</summary>
        public string StableId => stableId;
        /// <summary>资源、空间租约和确定性随机使用的运行期身份；同一世界内不能重复。</summary>
        public ulong OwnerId => ownerId;
        internal string PersonalInventoryId => $"{StableId}:personal";
        internal string BodyWaterInventoryId => $"{StableId}:body-water";
        internal string BladderInventoryId => $"{StableId}:bladder";

        /// <summary>只建立个人数据，不创建世界库存；身份为空或 owner 为零时拒绝。</summary>
        public FoundationResidentModelState(string stableId, ulong ownerId)
        {
            if (string.IsNullOrWhiteSpace(stableId)) throw new ArgumentException("居民稳定身份不能为空。", nameof(stableId));
            if (ownerId == 0UL) throw new ArgumentOutOfRangeException(nameof(ownerId), "居民 owner 不能为零。");
            this.stableId = stableId;
            this.ownerId = ownerId;
        }

        [field: SerializeField] public RP<FoundationResidentPhase> ResidentPhase { get; private set; } =
            new(FoundationResidentPhase.WaitingForFacility);
        [field: SerializeField] public RP<Vector3> ResidentLocalPosition { get; private set; } = new(Vector3.zero);
        [field: SerializeField] public RP<float> ResidentLocalYawDegrees { get; private set; } = new(0f);
        [field: SerializeField] public RP<float> RemainingPathMeters { get; private set; } = new(0f);
        /// <summary>当前移动连续没有可观察位移的业务时长；逐人记录，不由其他居民的活动清零。</summary>
        [field: SerializeField] public RP<long> MovementStallMilliseconds { get; private set; } = new(0L);
        [field: SerializeField] public RP<int> RemainingPathCorners { get; private set; } = new(0);
        [field: SerializeField] public RP<string> ActivePathSummary { get; private set; } = new(string.Empty);
        [field: SerializeField] public RP<bool> ResidentCarryingWater { get; private set; } = new(false);
        [field: SerializeField, Tooltip("居民手中普通世界物品的短暂表现投影；存档仍回退到移动事务来源。")]
        public RP<FoundationCarriedWorldItemState> ResidentCarriedWorldItem { get; private set; } =
            new(default);
        [field: SerializeField] public RP<FoundationActionPlanProjection> LatestActionPlan { get; private set; } =
            new(FoundationActionPlanProjection.None);
        [field: SerializeField, Tooltip("口渴缺口，0 表示不渴，1 表示达到危险上限。")]
        public RP<float> ResidentThirst { get; private set; } = new(0f);
        [field: SerializeField, Tooltip("正向健康值，1 表示健康，0 表示死亡；严重缺水会平滑加速损害健康。")]
        public RP<float> ResidentHealth { get; private set; } = new(1f);
        [field: FormerlySerializedAs("<ResidentRecreation>k__BackingField")]
        [field: SerializeField, Tooltip("正向娱乐满足度，0 表示极度无聊，1 表示兴趣得到充分满足；普通发呆和闲逛不会补充它。")]
        public RP<float> ResidentEntertainment { get; private set; } = new(0f);
        [field: SerializeField, Tooltip("正向心情值，0 表示极差，1 表示极好；娱乐不足、疲劳和压力会连续影响它。")]
        public RP<float> ResidentMood { get; private set; } = new(0f);
        [field: SerializeField, Tooltip("疲劳负担，0 表示精力充足，1 表示极度疲劳。玩家界面会反向显示为精力。")]
        public RP<float> ResidentFatigue { get; private set; } = new(0f);
        [field: SerializeField, Tooltip("压力负担，0 表示平静，1 表示压力极高；缺水、憋尿、阻塞与疲劳都会增加它。")]
        public RP<float> ResidentStress { get; private set; } = new(0f);
        [field: SerializeField, Tooltip("当前状态下的预期工作速度乘数；100% 为标准人力，具体工作开始时还会固定采样少量个人波动。")]
        public RP<float> ResidentWorkEfficiency { get; private set; } = new(1f);
        [field: SerializeField] public RP<int> BodyWaterMilliliters { get; private set; } = new(0);
        [field: SerializeField] public RP<int> BodyWaterCapacityMilliliters { get; private set; } = new(0);
        [field: SerializeField] public RP<int> BladderWasteMilliliters { get; private set; } = new(0);
        [field: SerializeField] public RP<int> BladderCapacityMilliliters { get; private set; } = new(0);
        [field: SerializeField] public RP<float> ActionProgress { get; private set; } = new(0f);
        [field: SerializeField, Tooltip("当前有效工作位上的设施操作；身份和进度为同一只读表现快照，不进入存档。")]
        public RP<FoundationFacilityWorkState> FacilityWork { get; private set; } = new(default);
        [field: SerializeField] public RP<int> CompletedDrinkCount { get; private set; } = new(0);
        [field: SerializeField] public RP<int> CompletedToiletUseCount { get; private set; } = new(0);
        [field: SerializeField] public RP<int> CompletedLeisureCount { get; private set; } = new(0);
        [field: SerializeField] public RP<int> CompletedDaydreamCount { get; private set; } = new(0);
        [field: SerializeField] public RP<int> CompletedWanderCount { get; private set; } = new(0);
        [field: SerializeField, Tooltip("缺少床椅时在地面完成的低质量恢复次数。")]
        public RP<int> CompletedGroundRestCount { get; private set; } = new(0);
        [field: SerializeField, Tooltip("已完成的真实爱好次数；与只恢复疲劳/压力的基础休整分开观察。")]
        public RP<int> CompletedHobbyCount { get; private set; } = new(0);
        [field: SerializeField, Tooltip("已由居民完成原子拿起与放下的普通世界物品次数。")]
        public RP<int> CompletedWorldItemMoveCount { get; private set; } = new(0);
        [field: SerializeField, Tooltip("居民实际拿取并消耗维修包、在故障功能点完成的水箱维修次数。")]
        public RP<int> CompletedWaterTankRepairCount { get; private set; } = new(0);
        [field: SerializeField] public RP<string> CurrentTask { get; private set; } = new("等待初始化");
        [field: SerializeField] public RP<string> LastBlocker { get; private set; } = new(string.Empty);
    }
}
