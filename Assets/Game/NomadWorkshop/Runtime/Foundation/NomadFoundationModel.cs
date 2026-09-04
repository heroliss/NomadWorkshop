using System.Collections.Generic;
using Game.Framework.Model;
using R3;
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
        [field: SerializeField] public RP<bool> IsPaused { get; private set; } = new(false);
        [field: SerializeField] public RP<float> SimulationSpeed { get; private set; } = new(1f);
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
        [field: SerializeField] public RP<int> WaterCanWaterMilliliters { get; private set; } = new(0);
        [field: SerializeField] public RP<int> WaterCanCapacityMilliliters { get; private set; } = new(0);
        [field: SerializeField] public RP<FoundationActionPlanProjection> LatestActionPlan { get; private set; } =
            new(FoundationActionPlanProjection.None);
        [field: SerializeField] public RP<float> ResidentThirst { get; private set; } = new(0f);
        [field: SerializeField] public RP<float> ResidentRecreation { get; private set; } = new(0f);
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
        [field: SerializeField] public RP<string> CurrentTask { get; private set; } = new("等待初始化");
        [field: SerializeField] public RP<string> LastBlocker { get; private set; } = new(string.Empty);

        [SerializeField] private List<FoundationFacilityState> facilities = new();
        [SerializeField] private List<FoundationFacilityAccessState> facilityAccess = new();

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
