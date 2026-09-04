using System;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>
    /// 居民移动的语义目标。它保存“为何移动、需要哪类设施”，而不是只保存一次寻路得到的旧坐标；
    /// 因此建造改变 NavMesh 后，System 可以先换同设施 Slot，再换同功能设施，最后进入可重试等待。
    /// </summary>
    internal readonly struct FoundationResidentMoveIntent
    {
        public FoundationResidentMoveIntent(
            FoundationResidentPhase travelPhase,
            string task,
            NomadFacilityFunction facilityFunction,
            string preferredFacilityInstanceId,
            bool allowAlternativeFacility)
        {
            TravelPhase = travelPhase;
            Task = string.IsNullOrWhiteSpace(task)
                ? throw new ArgumentException("移动意图必须说明任务。", nameof(task))
                : task.Trim();
            FacilityFunction = facilityFunction;
            PreferredFacilityInstanceId = preferredFacilityInstanceId?.Trim() ?? string.Empty;
            AllowAlternativeFacility = allowAlternativeFacility;
        }

        public FoundationResidentPhase TravelPhase { get; }
        public string Task { get; }
        public NomadFacilityFunction FacilityFunction { get; }
        public string PreferredFacilityInstanceId { get; }
        public bool AllowAlternativeFacility { get; }

        public FoundationResidentMoveIntent Retarget(string facilityInstanceId) => new(
            TravelPhase,
            Task,
            FacilityFunction,
            facilityInstanceId,
            AllowAlternativeFacility);
    }
}
