using System;
using UnityEngine;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>
    /// 一名居民已经到岗且仍持有效工作位租约的表现快照。身份、阶段和进度原子发布；
    /// 默认值表示没有设施操作，不包含路径/预留能力，也不作为资源交接或存档真值。
    /// </summary>
    [Serializable]
    public struct FoundationFacilityWorkState : IEquatable<FoundationFacilityWorkState>
    {
        [SerializeField] private string facilityInstanceId;
        [SerializeField] private string groupId;
        [SerializeField] private string slotId;
        [SerializeField] private FoundationResidentPhase phase;
        [SerializeField] private float progress;
        [SerializeField] private FoundationItemPlacementState itemContactPlacement;

        public FoundationFacilityWorkState(string facilityInstanceId, string groupId, string slotId,
            FoundationResidentPhase phase, float progress, FoundationItemPlacementState itemContactPlacement = default)
        {
            this.facilityInstanceId = facilityInstanceId ?? string.Empty;
            this.groupId = groupId ?? string.Empty;
            this.slotId = slotId ?? string.Empty;
            this.phase = phase;
            this.progress = Mathf.Clamp01(progress);
            this.itemContactPlacement = itemContactPlacement;
        }

        public bool Active => !string.IsNullOrEmpty(facilityInstanceId);
        public string FacilityInstanceId => facilityInstanceId ?? string.Empty;
        public string GroupId => groupId ?? string.Empty;
        public string SlotId => slotId ?? string.Empty;
        public FoundationResidentPhase Phase => phase;
        public float Progress => progress;
        /// <summary>拾放时的实际来源或已预留落点；只供求接触姿态，不授予账本提交权限。</summary>
        public FoundationItemPlacementState ItemContactPlacement => itemContactPlacement;

        public bool Equals(FoundationFacilityWorkState other) =>
            FacilityInstanceId == other.FacilityInstanceId && GroupId == other.GroupId &&
            SlotId == other.SlotId && phase == other.phase && progress.Equals(other.progress) &&
            itemContactPlacement.Equals(other.itemContactPlacement);
        public override bool Equals(object obj) => obj is FoundationFacilityWorkState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(FacilityInstanceId, GroupId, SlotId, phase, progress, itemContactPlacement);
    }
}
