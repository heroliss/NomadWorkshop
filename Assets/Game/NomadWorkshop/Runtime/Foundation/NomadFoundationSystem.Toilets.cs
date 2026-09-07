using System;
using System.Collections.Generic;
using Game.NomadWorkshop.Simulation;

namespace Game.NomadWorkshop.Foundation
{
    public sealed partial class NomadFoundationSystem
    {
        private readonly Dictionary<string, ResourceInventory> _toiletInventories = new(StringComparer.Ordinal);

        private static string BuildToiletInventoryId(string facilityInstanceId) =>
            $"facility:{facilityInstanceId}:toilet-waste";

        private bool TrySelectUsableToilet(
            int waste, out FoundationFacilityState selected, out float bestPathLength,
            out bool hasReachableToilet)
        {
            selected = default;
            bestPathLength = float.PositiveInfinity;
            hasReachableToilet = false;
            foreach (FoundationFacilityState facility in _model.Facilities)
            {
                if (!IsToiletReadyForUse(facility.InstanceId)) continue;
                if (!_toiletInventories.TryGetValue(facility.InstanceId, out ResourceInventory inventory) ||
                    !_definitions.TryGetValue(facility.DefinitionId, out var definition) ||
                    !TryMeasureFacilityPath(_resident.State.ResidentLocalPosition.Value, facility, definition,
                        out float pathLength, ToiletInteractionGroupId))
                    continue;
                hasReachableToilet = true;
                if (_resourceFlow.GetAvailableCapacity(inventory) < waste || pathLength >= bestPathLength)
                    continue;
                selected = facility;
                bestPathLength = pathLength;
            }
            return bestPathLength < float.PositiveInfinity;
        }

        private bool TryRetargetActiveToiletUse(
            in FoundationFacilityState facility, out ResourceFlowBlocker blocker)
        {
            blocker = ResourceFlowBlocker.None;
            if (_resident.MoveIntent.TravelPhase != FoundationResidentPhase.MovingToToilet) return true;
            if (!_toiletInventories.TryGetValue(facility.InstanceId, out ResourceInventory inventory))
                return false;
            if (_resident.ActiveWaterAction != null &&
                _resident.ActiveWaterAction.Request.Outputs[0].Inventory == inventory)
                return true;

            // 如厕尚未提交，不存在手持中间产物。先取消旧预留，再在同一模拟调用内预留新实例；
            // 新实例失败时由调用方回到决策，不保留“走向 B、结果写入 A”的半套行动。
            _resident.ActiveWaterAction?.Dispose();
            _resident.ActiveWaterAction = null;
            int waste = _resident.WaterCycle.Bladder.GetAmount(NomadResourceIds.HumanWaste);
            return waste > 0 && _resident.WaterCycle.TryReserveToiletUse(
                _resourceFlow, inventory, $"toilet:{_resident.StableId}:{_resident.ActionSequence}",
                $"facility:{facility.InstanceId}:toilet-use", waste,
                out _resident.ActiveWaterAction, out blocker);
        }

        private bool IsToiletCompatibleWithActiveMove(in FoundationFacilityState facility)
        {
            if (!IsToiletReadyForUse(facility.InstanceId)) return false;
            if (!_toiletInventories.TryGetValue(facility.InstanceId, out ResourceInventory inventory))
                return false;
            return _resident.ActiveWaterAction != null &&
                   _resident.ActiveWaterAction.Request.Outputs[0].Inventory == inventory ||
                   _resourceFlow.GetAvailableCapacity(inventory) >=
                   _resident.WaterCycle.Bladder.GetAmount(NomadResourceIds.HumanWaste);
        }
    }
}
