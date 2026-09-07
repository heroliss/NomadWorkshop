using System;
using Game.NomadWorkshop.Simulation;

namespace Game.NomadWorkshop.Foundation
{
    public sealed partial class NomadFoundationSystem
    {
        private const string WaterCanAccessInteractionGroupId = "water-can-access";
        private float WaterCanLiftSeconds => pickupSeconds * .5f;
        private float WaterCanReleaseSeconds => pickupSeconds * .35f;

        private static bool IsWaterCanContactPhase(FoundationResidentPhase phase) => phase is
            FoundationResidentPhase.PickingUpWaterCan or FoundationResidentPhase.LiftingWaterCan or
            FoundationResidentPhase.PlacingWaterCan or FoundationResidentPhase.ReleasingWaterCan;

        /// <summary>到达独立侧面工作位后锁定实际来源；还未接触时不改水罐所有权或区域占用。</summary>
        private void BeginWaterCanPickup()
        {
            ClearActiveMoveIntent();
            if (_waterCanPlacement == null || _waterCanLocation == FoundationWaterCanLocation.Resident ||
                _resident.InteractionSpace is not { IsActive: true } lease ||
                lease.Slot.GroupId != WaterCanAccessInteractionGroupId ||
                lease.Slot.FacilityInstanceId != _waterCanPlacement.Region.OwnerEntityId)
            {
                Block("拿取水罐时实际停放位置或侧面工作位已失效", ResourceFlowBlocker.None);
                return;
            }
            _resident.WaterCanContactPlacement = new FoundationItemPlacementState(_waterCanPlacement);
            BeginTimedPhase(FoundationResidentPhase.PickingUpWaterCan, pickupSeconds, "蹲身接触停放区中的同一只水罐");
        }

        /// <summary>倾倒已结算水；空罐仍归当前居民，预留精确落点后实际走到侧面，不能直接跨设施落位。</summary>
        private void BeginWaterCanParking(string facilityId, FoundationWaterCanLocation location)
        {
            if (_waterCanLocation != FoundationWaterCanLocation.Resident || _waterCanCarrierId != _resident.StableId ||
                !TryFindFacilityState(facilityId, out FoundationFacilityState facility))
                throw new InvalidOperationException("放回水罐需要当前居民持有的实体水罐和有效设施。");
            _resident.WaterCanParkingReservation?.Dispose();
            _resident.WaterCanParkingReservation = null;
            string regionId = PlacementRegionLedger.ComposeRegionId(facilityId, WaterCanParkingRegionLocalId);
            if (!_worldItemPlacementLedger.TryReserveStable(WaterCanItemId,
                    GetRequiredWorldItemFootprint(WaterCanDefinitionId), regionId,
                    out _resident.WaterCanParkingReservation, out PlacementRegionFailure failure))
            {
                Block("无法预留水罐停放位置：" + failure, ResourceFlowBlocker.None);
                return;
            }
            _resident.WaterCanParkingLocation = location;
            _resident.WaterCanContactPlacement = new FoundationItemPlacementState(
                _resident.WaterCanParkingReservation.Candidate);
            TryBeginMove(FoundationResidentPhase.MovingToWaterCanParking, "携带空水罐走向侧面停放区",
                facility, allowAlternativeFacility: false, interactionGroupId: WaterCanAccessInteractionGroupId);
        }

        private void BeginWaterCanPlacement()
        {
            ClearActiveMoveIntent();
            if (_resident.WaterCanParkingReservation is not { IsActive: true } ||
                _resident.InteractionSpace is not { IsActive: true } lease ||
                lease.Slot.GroupId != WaterCanAccessInteractionGroupId ||
                lease.Slot.FacilityInstanceId != _resident.WaterCanContactPlacement.OwnerEntityId)
                throw new InvalidOperationException("落罐前丢失已预留的落点或对应工作位。");
            BeginTimedPhase(FoundationResidentPhase.PlacingWaterCan, deliverySeconds, "蹲身将水罐落到已预留的位置");
        }

        private void CompleteWaterCanPlacement()
        {
            PlacementRegionReservation reservation = _resident.WaterCanParkingReservation;
            if (reservation == null || !reservation.TryCommit(out _, out PlacementRegionFailure failure))
                throw new InvalidOperationException("落罐时预留已失效，不能再随机选择另一个落点。");
            _resident.WaterCanParkingReservation = null;
            FoundationItemPlacementState target = _resident.WaterCanContactPlacement;
            // SetWaterCanLocation 复用已提交的同一姿态；库存结算早已完成，不在此重复 Deliver。
            SetWaterCanLocation(_resident.WaterCanParkingLocation, target.OwnerEntityId, target.RegionId, target.LocalPose);
            BeginTimedPhase(FoundationResidentPhase.ReleasingWaterCan, WaterCanReleaseSeconds,
                "水罐已稳定落位，松手并起身离开接触位");
        }

        private void CompleteWaterCanRelease()
        {
            string facilityId = _resident.WaterCanContactPlacement.OwnerEntityId;
            _resident.WaterCanContactPlacement = default;
            ReleaseActiveInteractionSpace(publishProjection: true);
            if (IsStopWaterVisit)
            {
                CompleteStopWaterParking();
                return;
            }
            bool shouldDrink = _resident.DrinkAfterActiveHaul;
            _resident.DrinkAfterActiveHaul = false;
            _resident.ActiveWaterSourceFacilityInstanceId = string.Empty;
            _resident.ActiveWaterTargetFacilityInstanceId = string.Empty;
            if (shouldDrink && TryFindFacilityByInstanceId(facilityId,
                    NomadFacilityFunction.DrinkingStation, out FoundationFacilityState station))
            {
                BeginDrink(station);
                return;
            }
            SetResidentPhase(FoundationResidentPhase.Idle, "完成补水并放好水罐，准备下一项工作");
            _resident.DecisionRetryRemaining = 0f;
        }
    }
}
