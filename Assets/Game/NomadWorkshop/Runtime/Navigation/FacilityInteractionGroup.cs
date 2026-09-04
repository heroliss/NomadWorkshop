using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.NomadWorkshop.Navigation
{
    /// <summary>
    /// 一个具有真实容量语义的设施入口。组内 Slot 是同一份容量的备选站姿，而不是默认并发位；
    /// 真正可并行的长操作台应声明多个 InteractionGroup。
    /// </summary>
    public sealed class FacilityInteractionGroup : MonoBehaviour
    {
        [SerializeField, Tooltip("设施内一个功能点的稳定 id，例如 faucet 或 cabinet。")]
        private string groupId = "interaction";
        [SerializeField, Tooltip("全部备选位不可达时，该设施是否应被判定为缺少必要功能。")]
        private bool requiredForOperation = true;
        [SerializeField, Tooltip("共享同一使用容量的备选站姿；并行工位应拆成多个交互组。")]
        private FacilityInteractionSlot[] alternativeSlots =
            Array.Empty<FacilityInteractionSlot>();

        public string GroupId => groupId;
        public bool RequiredForOperation => requiredForOperation;
        public IReadOnlyList<FacilityInteractionSlot> AlternativeSlots => alternativeSlots;
        public string ReservationKey => $"facility-interaction:{groupId}";

        /// <summary>供生成式 Harness 装配；组内候选位共享一个互斥预留键。</summary>
        public void ConfigureRuntime(
            string configuredGroupId,
            bool configuredRequiredForOperation,
            FacilityInteractionSlot[] configuredAlternativeSlots)
        {
            if (string.IsNullOrWhiteSpace(configuredGroupId))
                throw new ArgumentException("设施交互组必须提供稳定 group id。", nameof(configuredGroupId));
            if (configuredAlternativeSlots == null)
                throw new ArgumentNullException(nameof(configuredAlternativeSlots));

            groupId = configuredGroupId.Trim();
            requiredForOperation = configuredRequiredForOperation;
            alternativeSlots = (FacilityInteractionSlot[])configuredAlternativeSlots.Clone();
            ValidateOrThrow();
        }

        public void ValidateOrThrow()
        {
            if (string.IsNullOrWhiteSpace(groupId))
                throw new InvalidOperationException($"设施 '{name}' 的交互组 id 为空。");
            if (alternativeSlots == null || alternativeSlots.Length == 0)
                throw new InvalidOperationException($"交互组 '{groupId}' 没有候选停靠位。");

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < alternativeSlots.Length; i++)
            {
                FacilityInteractionSlot slot = alternativeSlots[i];
                if (slot == null || !slot.IsConfigured)
                    throw new InvalidOperationException($"交互组 '{groupId}' 的第 {i} 个候选位无效。");
                if (!ids.Add(slot.SlotId))
                    throw new InvalidOperationException(
                        $"交互组 '{groupId}' 出现重复 slot id：'{slot.SlotId}'。");
            }
        }

        private void OnValidate() => groupId = groupId?.Trim() ?? string.Empty;
    }
}
