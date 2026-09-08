using System.Collections.Generic;
using Game.Framework.Model;
using Game.NomadWorkshop.Simulation;
using R3;
using ObservableCollections;
using UnityEngine;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>
    /// Foundation 切片的 Inspector 可观察数据面。纯模拟对象由 System 拥有，本 Model 只保存游戏真值记录与只读投影，
    /// 避免 View 直接抓取模拟内核形成第二条写路径。
    /// </summary>
    public sealed class NomadFoundationModel : MonoModelBase
    {
        [field: SerializeField] public RP<bool> IsReady { get; private set; } = new(false);
        [field: Header("首段旅途（运行时只读观察）")]
        [field: SerializeField] public RP<long> JourneyPositionMicrometers { get; private set; } = new(0L);
        [field: SerializeField] public RP<int> StopWaterMilliliters { get; private set; } = new(0);
        [field: SerializeField] public RP<bool> StopWaterRequested { get; private set; } = new(false);
        [field: SerializeField] public RP<bool> StopWaterActive { get; private set; } = new(false);
        [field: SerializeField] public RP<int> StopWasteMilliliters { get; private set; } = new(0);
        [field: SerializeField] public RP<int> StopWasteCapacityMilliliters { get; private set; } = new(0);
        [field: SerializeField] public RP<bool> StopWasteRequested { get; private set; } = new(false);
        [field: SerializeField] public RP<bool> StopWasteActive { get; private set; } = new(false);
        [field: SerializeField] public RP<string> CarriedWasteBucketFacilityId { get; private set; } = new(string.Empty);
        [field: SerializeField] public RP<int> CarriedWasteMilliliters { get; private set; } = new(0);
        [field: SerializeField] public RP<string> StopWorkFeedback { get; private set; } = new(string.Empty);
        [field: SerializeField] public RP<int> StopSpareCount { get; private set; } = new(0);
        [field: SerializeField] public RP<bool> StopSpareRequested { get; private set; } = new(false);
        [field: SerializeField] public RP<bool> StopSpareActive { get; private set; } = new(false);
        [field: SerializeField] public RP<bool> StopAccessOpen { get; private set; } = new(false);
        [field: SerializeField] public RP<long> JourneyFuelPicoliters { get; private set; } = new(0L);
        [field: SerializeField] public RP<NomadJourneyEndpoint> JourneyDestination { get; private set; } =
            new(NomadJourneyEndpoint.None);
        [field: SerializeField] public RP<string> JourneyDestinationAnchorId { get; private set; } =
            new(string.Empty);
        [field: SerializeField] public RP<NomadJourneyStatus> JourneyStatus { get; private set; } =
            new(NomadJourneyStatus.NoDestination);
        [field: SerializeField] public RP<bool> IsPaused { get; private set; } = new(false);
        [field: SerializeField] public RP<bool> CheckpointBusy { get; private set; } = new(false);
        [field: SerializeField] public RP<string> CheckpointFeedback { get; private set; } = new("手动保存旅程，下次可从这里继续。");
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
        [field: SerializeField, Tooltip("由统一 Tick 与世界种子确定性投影的当前天气；不保存第二只天气时钟。")]
        public RP<NomadWeatherKind> CurrentWeather { get; private set; } =
            new(NomadWeatherKind.Clear);
        [field: SerializeField, Tooltip("当前沙尘暴强度千分比；晴朗时为 0。")]
        public RP<int> SandstormIntensityPermille { get; private set; } = new(0);
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
        [field: SerializeField, Tooltip("已落位世界物品投影的版本；区域账本变化后递增。")]
        public RP<int> WorldItemPlacementRevision { get; private set; } = new(0);

        [field: SerializeField] public RP<FoundationWaterCanLocation> WaterCanLocation { get; private set; } =
            new(FoundationWaterCanLocation.VehicleWaterTank);
        [field: SerializeField, Tooltip("水罐不在居民手中时所依附的精确设施实例；不能只靠设施类型猜测。")]
        public RP<string> WaterCanAnchorFacilityInstanceId { get; private set; } = new(string.Empty);
        [field: SerializeField, Tooltip("水罐在设施放置区域中的精确局部姿态；居民携带时为空。")]
        public RP<FoundationItemPlacementState> WaterCanPlacement { get; private set; } = new(default);
        [field: SerializeField] public RP<int> WaterCanWaterMilliliters { get; private set; } = new(0);
        [field: SerializeField] public RP<int> WaterCanCapacityMilliliters { get; private set; } = new(0);
        [field: SerializeField] public RP<int> VehicleWaterMilliliters { get; private set; } = new(0);
        [field: SerializeField] public RP<int> VehicleWaterCapacityMilliliters { get; private set; } = new(0);
        [field: SerializeField] public RP<int> DrinkingStationWaterMilliliters { get; private set; } = new(0);
        [field: SerializeField] public RP<int> DrinkingStationCapacityMilliliters { get; private set; } = new(0);
        [field: SerializeField, Tooltip("全部旱厕污物桶的总量，含携带中但尚未倾倒的桶；容器身份与位置分别投影。")]
        public RP<int> ToiletHoldingWasteMilliliters { get; private set; } = new(0);
        [field: SerializeField, Tooltip("全部已建旱厕实例的容量总和；未建厕所时为 0。")]
        public RP<int> ToiletHoldingCapacityMilliliters { get; private set; } = new(0);

        /// <summary>首位居民的独立记录；复位与读取保留记录及其属性身份，执行器只重建瞬时行动。</summary>
        [field: SerializeField]
        public FoundationResidentModelState PrimaryResident { get; private set; } = new("resident-01", 0xF01UL);

        private readonly ObservableList<FoundationResidentReadModel> residents = new();
        internal IReadOnlyObservableList<FoundationResidentReadModel> Residents => residents;

        internal void EnsureResidentCount(int count)
        {
            if (count < 1 || count > 3) throw new System.ArgumentOutOfRangeException(nameof(count));
            if (residents.Count == 0) residents.Add(new FoundationResidentReadModel(PrimaryResident));
            while (residents.Count > count) residents.RemoveAt(residents.Count - 1);
            while (residents.Count < count)
            {
                int index = residents.Count + 1;
                residents.Add(new FoundationResidentReadModel(
                    new FoundationResidentModelState($"resident-{index:00}", 0xF00UL + (ulong)index)));
            }
        }

        [field: SerializeField] public RP<string> WaterCanCarrierId { get; private set; } = new(string.Empty);
        [field: SerializeField] public RP<string> WasteBucketCarrierId { get; private set; } = new(string.Empty);
        [field: SerializeField] public RP<string> DepartureFeedback { get; private set; } = new(string.Empty);

        /// <summary>世界建造事务的反馈，不覆盖任何居民正在执行的任务或个人阻塞。</summary>
        [field: SerializeField] public RP<string> BuildFeedback { get; private set; } = new(string.Empty);

        [SerializeField] private List<FoundationFacilityState> facilities = new();
        [SerializeField] private List<FoundationFacilityAccessState> facilityAccess = new();
        [SerializeField] private List<FoundationFacilityInventoryState> facilityInventories = new();
        [SerializeField] private List<FoundationFacilityConditionState> facilityConditions = new();
        [SerializeField] private List<FoundationItemPlacementState> worldItemPlacements = new();

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

        internal void ReplaceWorldItemPlacements(
            IReadOnlyList<FoundationItemPlacementState> source)
        {
            int sourceCount = source?.Count ?? 0;
            if (worldItemPlacements.Count == sourceCount)
            {
                var unchanged = true;
                for (var i = 0; i < sourceCount; i++)
                {
                    if (worldItemPlacements[i].Equals(source[i])) continue;
                    unchanged = false;
                    break;
                }
                if (unchanged) return;
            }

            worldItemPlacements.Clear();
            if (source != null)
            {
                for (var i = 0; i < source.Count; i++)
                    worldItemPlacements.Add(source[i]);
            }
            WorldItemPlacementRevision.Value++;
        }

        internal FoundationItemPlacementState[] GetWorldItemPlacementSnapshot() =>
            worldItemPlacements.ToArray();

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
