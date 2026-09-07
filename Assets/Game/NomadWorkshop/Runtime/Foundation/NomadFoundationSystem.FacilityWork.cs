namespace Game.NomadWorkshop.Foundation
{
    public sealed partial class NomadFoundationSystem
    {
        /// <summary>只从当前居民仍持有的精确工作位发布已到岗动作，不推断最近设施。</summary>
        private void PublishResidentFacilityWork()
        {
            FoundationFacilityWorkState state = default;
            if (_resident.InteractionSpace is { IsActive: true } lease &&
                IsFacilityWorkPhase(_resident.Phase))
            {
                var slot = lease.Slot;
                state = new FoundationFacilityWorkState(slot.FacilityInstanceId, slot.GroupId, slot.SlotId,
                    _resident.Phase, _resident.State.ActionProgress.Value,
                    IsWaterCanContactPhase(_resident.Phase) ? _resident.WaterCanContactPlacement : default);
            }
            _resident.State.FacilityWork.Value = state;
        }

        private static bool IsFacilityWorkPhase(FoundationResidentPhase phase) => phase is
            FoundationResidentPhase.PickingUpWaterCan or FoundationResidentPhase.PickingUpWater or
            FoundationResidentPhase.LiftingWaterCan or FoundationResidentPhase.PlacingWaterCan or
            FoundationResidentPhase.ReleasingWaterCan or
            FoundationResidentPhase.DeliveringWater or FoundationResidentPhase.Drinking or
            FoundationResidentPhase.UsingToilet or FoundationResidentPhase.EnjoyingHobby or
            FoundationResidentPhase.PickingUpWorldItem or FoundationResidentPhase.PlacingWorldItem or
            FoundationResidentPhase.PickingUpRepairPart or FoundationResidentPhase.RepairingFacility or
            FoundationResidentPhase.Driving or FoundationResidentPhase.DeliveringStopWater or
            FoundationResidentPhase.DetachingWasteBucket or FoundationResidentPhase.InstallingWasteBucket or
            FoundationResidentPhase.DeliveringStopSpare;
    }
}
