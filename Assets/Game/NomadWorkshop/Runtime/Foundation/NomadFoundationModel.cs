using System.Collections.Generic;
using Game.Framework.Model;
using R3;
using UnityEngine;
using UnityEngine.Serialization;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>
    /// Foundation 切片的 Inspector 可观察数据面。纯模拟对象由 System 拥有，本 Model 只保存游戏真值记录与只读投影，
    /// 避免 View 直接抓取模拟内核形成第二条写路径。
    /// </summary>
    public sealed class NomadFoundationModel : MonoModelBase
    {
        [field: SerializeField] public RP<bool> IsReady { get; private set; } = new(false);
        [field: SerializeField] public RP<bool> IsPaused { get; private set; } = new(false);
        [field: SerializeField] public RP<float> SimulationSpeed { get; private set; } = new(1f);
        [field: Header("统一模拟时钟（运行时只读观察）")]
        [field: SerializeField, Tooltip("从本局起点累计的唯一模拟毫秒；暂停时不增长，需求、动作与日历都由它推进。")]
        public RP<long> SimulationTick { get; private set; } = new(0L);
        [field: SerializeField, Tooltip("从 1 开始的居民生活日，由统一模拟毫秒投影。")]
        public RP<long> LifeDay { get; private set; } = new(1L);
        [field: SerializeField, Tooltip("当前生活日内的分钟，范围 0–1439。")]
        public RP<int> LifeMinuteOfDay { get; private set; } = new(0);
        [field: SerializeField, Tooltip("当前生活日的千分比进度，范围 0–999。")]
        public RP<int> LifeDayProgressPermille { get; private set; } = new(0);
        [field: SerializeField, Tooltip("从 1 开始的压缩气候年；它与生活日共享同一个模拟 Tick。")]
        public RP<long> ClimateYear { get; private set; } = new(1L);
        [field: SerializeField, Tooltip("当前季节的零基相位索引；模拟层暂不硬编码季节名称。")]
        public RP<int> SeasonIndex { get; private set; } = new(0);
        [field: SerializeField, Tooltip("当前季节内从 1 开始的气候周。")]
        public RP<int> ClimateWeekInSeason { get; private set; } = new(1);
        [field: SerializeField, Tooltip("当前季节的千分比连续进度，范围 0–999。")]
        public RP<int> SeasonProgressPermille { get; private set; } = new(0);
        [field: SerializeField] public RP<FoundationInteractionMode> InteractionMode { get; private set; } =
            new(FoundationInteractionMode.Observe);
        [field: SerializeField] public RP<FoundationPlacementPreviewState> PlacementPreview { get; private set; } =
            new(FoundationPlacementPreviewState.Inactive);
        [field: SerializeField] public RP<FoundationBuildTransactionPhase> BuildTransactionPhase { get; private set; } =
            new(FoundationBuildTransactionPhase.Idle);
        [field: SerializeField] public RP<int> PositionSnapMillimeters { get; private set; } = new(200);
        [field: SerializeField] public RP<int> RotationSnapDeciDegrees { get; private set; } = new(450);
        [field: SerializeField] public RP<bool> ShowPlacementGrid { get; private set; } = new(true);
        [field: SerializeField] public RP<int> FacilityRevision { get; private set; } = new(0);
        [field: SerializeField] public RP<int> FacilityAccessRevision { get; private set; } = new(0);
        [field: SerializeField, Tooltip("设施实例库存只读投影的版本；变化时 View 应重新取得快照。")]
        public RP<int> FacilityInventoryRevision { get; private set; } = new(0);
        [field: SerializeField, Tooltip("设施磨损、积尘、维护欠账和具体故障投影的版本；真值仍由 System 独占。")]
        public RP<int> FacilityConditionRevision { get; private set; } = new(0);

        [Header("居民与物质链（运行时只读观察）")]
        [field: SerializeField] public RP<FoundationResidentPhase> ResidentPhase { get; private set; } =
            new(FoundationResidentPhase.WaitingForFacility);
        [field: SerializeField] public RP<Vector3> ResidentLocalPosition { get; private set; } = new(Vector3.zero);
        [field: SerializeField] public RP<float> ResidentLocalYawDegrees { get; private set; } = new(0f);
        [field: SerializeField] public RP<float> RemainingPathMeters { get; private set; } = new(0f);
        [field: SerializeField] public RP<int> RemainingPathCorners { get; private set; } = new(0);
        [field: SerializeField] public RP<string> ActivePathSummary { get; private set; } = new(string.Empty);
        [field: SerializeField] public RP<bool> ResidentCarryingWater { get; private set; } = new(false);
        [field: SerializeField] public RP<FoundationWaterCanLocation> WaterCanLocation { get; private set; } =
            new(FoundationWaterCanLocation.VehicleWaterTank);
        [field: SerializeField, Tooltip("水罐不在居民手中时所依附的精确设施实例；不能只靠设施类型猜测。")]
        public RP<string> WaterCanAnchorFacilityInstanceId { get; private set; } = new(string.Empty);
        [field: SerializeField, Tooltip("水罐在设施放置区域中的精确局部姿态；居民携带时为空。")]
        public RP<FoundationItemPlacementState> WaterCanPlacement { get; private set; } = new(default);
        [field: SerializeField] public RP<int> WaterCanWaterMilliliters { get; private set; } = new(0);
        [field: SerializeField] public RP<int> WaterCanCapacityMilliliters { get; private set; } = new(0);
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
        [field: SerializeField] public RP<int> VehicleWaterMilliliters { get; private set; } = new(0);
        [field: SerializeField] public RP<int> VehicleWaterCapacityMilliliters { get; private set; } = new(0);
        [field: SerializeField] public RP<int> DrinkingStationWaterMilliliters { get; private set; } = new(0);
        [field: SerializeField] public RP<int> DrinkingStationCapacityMilliliters { get; private set; } = new(0);
        [field: SerializeField] public RP<int> BodyWaterMilliliters { get; private set; } = new(0);
        [field: SerializeField] public RP<int> BodyWaterCapacityMilliliters { get; private set; } = new(0);
        [field: SerializeField] public RP<int> BladderWasteMilliliters { get; private set; } = new(0);
        [field: SerializeField] public RP<int> BladderCapacityMilliliters { get; private set; } = new(0);
        [field: SerializeField] public RP<int> ToiletHoldingWasteMilliliters { get; private set; } = new(0);
        [field: SerializeField] public RP<int> ToiletHoldingCapacityMilliliters { get; private set; } = new(0);
        [field: SerializeField] public RP<float> ActionProgress { get; private set; } = new(0f);
        [field: SerializeField] public RP<int> CompletedDrinkCount { get; private set; } = new(0);
        [field: SerializeField] public RP<int> CompletedToiletUseCount { get; private set; } = new(0);
        [field: SerializeField] public RP<int> CompletedLeisureCount { get; private set; } = new(0);
        [field: SerializeField] public RP<int> CompletedDaydreamCount { get; private set; } = new(0);
        [field: SerializeField] public RP<int> CompletedWanderCount { get; private set; } = new(0);
        [field: SerializeField, Tooltip("缺少床椅时在地面完成的低质量恢复次数。")]
        public RP<int> CompletedGroundRestCount { get; private set; } = new(0);
        [field: SerializeField, Tooltip("已完成的真实爱好次数；与只恢复疲劳/压力的基础休整分开观察。")]
        public RP<int> CompletedHobbyCount { get; private set; } = new(0);
        [field: SerializeField] public RP<string> CurrentTask { get; private set; } = new("等待初始化");
        [field: SerializeField] public RP<string> LastBlocker { get; private set; } = new(string.Empty);

        [SerializeField] private List<FoundationFacilityState> facilities = new();
        [SerializeField] private List<FoundationFacilityAccessState> facilityAccess = new();
        [SerializeField] private List<FoundationFacilityInventoryState> facilityInventories = new();
        [SerializeField] private List<FoundationFacilityConditionState> facilityConditions = new();

        internal IReadOnlyList<FoundationFacilityState> Facilities => facilities;

        internal void ReplaceFacilities(IReadOnlyList<FoundationFacilityState> source)
        {
            facilities.Clear();
            if (source != null)
            {
                for (var i = 0; i < source.Count; i++) facilities.Add(source[i]);
            }
            FacilityRevision.Value++;
        }

        internal void AddFacility(FoundationFacilityState facility)
        {
            facilities.Add(facility);
            FacilityRevision.Value++;
        }

        internal bool ContainsFacility(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) return false;
            for (var i = 0; i < facilities.Count; i++)
            {
                if (string.Equals(
                        facilities[i].InstanceId,
                        instanceId,
                        System.StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        internal bool RemoveFacility(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) return false;
            for (var i = 0; i < facilities.Count; i++)
            {
                if (!string.Equals(
                        facilities[i].InstanceId,
                        instanceId,
                        System.StringComparison.Ordinal))
                    continue;

                facilities.RemoveAt(i);
                FacilityRevision.Value++;
                return true;
            }
            return false;
        }

        internal FoundationFacilityState[] GetFacilitySnapshot() => facilities.ToArray();

        internal void ReplaceFacilityAccess(IReadOnlyList<FoundationFacilityAccessState> source)
        {
            if (HasSameFacilityAccess(source)) return;

            facilityAccess.Clear();
            if (source != null)
            {
                for (var i = 0; i < source.Count; i++) facilityAccess.Add(source[i]);
            }
            FacilityAccessRevision.Value++;
        }

        internal FoundationFacilityAccessState[] GetFacilityAccessSnapshot() =>
            facilityAccess.ToArray();

        internal void ReplaceFacilityInventories(
            IReadOnlyList<FoundationFacilityInventoryState> source)
        {
            int sourceCount = source?.Count ?? 0;
            if (facilityInventories.Count == sourceCount)
            {
                var unchanged = true;
                for (var i = 0; i < sourceCount; i++)
                {
                    if (facilityInventories[i].Equals(source[i])) continue;
                    unchanged = false;
                    break;
                }
                if (unchanged) return;
            }

            facilityInventories.Clear();
            if (source != null)
            {
                for (var i = 0; i < source.Count; i++) facilityInventories.Add(source[i]);
            }
            FacilityInventoryRevision.Value++;
        }

        internal FoundationFacilityInventoryState[] GetFacilityInventorySnapshot() =>
            facilityInventories.ToArray();

        internal void ReplaceFacilityConditions(
            IReadOnlyList<FoundationFacilityConditionState> source)
        {
            int sourceCount = source?.Count ?? 0;
            if (facilityConditions.Count == sourceCount)
            {
                var unchanged = true;
                for (var i = 0; i < sourceCount; i++)
                {
                    if (facilityConditions[i].Equals(source[i])) continue;
                    unchanged = false;
                    break;
                }
                if (unchanged) return;
            }

            facilityConditions.Clear();
            if (source != null)
            {
                for (var i = 0; i < source.Count; i++) facilityConditions.Add(source[i]);
            }
            FacilityConditionRevision.Value++;
        }

        internal FoundationFacilityConditionState[] GetFacilityConditionSnapshot() =>
            facilityConditions.ToArray();

        private bool HasSameFacilityAccess(IReadOnlyList<FoundationFacilityAccessState> source)
        {
            int sourceCount = source?.Count ?? 0;
            if (facilityAccess.Count != sourceCount) return false;
            for (var i = 0; i < sourceCount; i++)
            {
                if (!facilityAccess[i].Equals(source[i])) return false;
            }
            return true;
        }
    }
}
