using System;
using UnityEngine;

namespace Game.NomadWorkshop.Navigation
{
    /// <summary>
    /// 一个设施交互组中的候选贴靠姿势。Transform 表示居民最终站位与朝向；手部目标只服务表现，
    /// 不拥有库存、预留或动作完成真值。
    /// </summary>
    public sealed class FacilityInteractionSlot : MonoBehaviour
    {
        [SerializeField, Tooltip("同一交互组内稳定且唯一的候选位 id。")]
        private string slotId = "slot";
        [SerializeField, Tooltip("抵达后希望播放的居民动作语义；具体动画由表现层映射。")]
        private ResidentAnimationSemantic animationSemantic =
            ResidentAnimationSemantic.Pickup;
        [SerializeField, Tooltip("可选的主手 IK 目标；不拥有库存或动作完成真值。")]
        private Transform primaryHandTarget;
        [SerializeField, Min(0.01f), Tooltip("NavMesh 接近阶段切换到精确贴靠的距离（米）。")]
        private float approachTolerance = 0.18f;
        [SerializeField, Min(0.005f), Tooltip("进入交互动作前允许的位置误差（米）。")]
        private float dockingPositionTolerance = 0.035f;
        [SerializeField, Range(0.1f, 30f), Tooltip("进入交互动作前允许的朝向误差（度）。")]
        private float dockingAngleTolerance = 2f;

        public string SlotId => slotId;
        public ResidentAnimationSemantic AnimationSemantic => animationSemantic;
        public Transform PrimaryHandTarget => primaryHandTarget;
        public float ApproachTolerance => approachTolerance;
        public float DockingPositionTolerance => dockingPositionTolerance;
        public float DockingAngleTolerance => dockingAngleTolerance;
        public Vector3 WorldPosition => transform.position;
        public Quaternion WorldRotation => transform.rotation;

        public bool IsConfigured => !string.IsNullOrWhiteSpace(slotId);

        /// <summary>供生成式 Harness 装配；正式 Prefab 仍由 Inspector 固化相同字段。</summary>
        public void ConfigureRuntime(
            string configuredSlotId,
            ResidentAnimationSemantic configuredSemantic,
            Transform configuredPrimaryHandTarget,
            float configuredApproachTolerance = 0.18f,
            float configuredDockingPositionTolerance = 0.035f,
            float configuredDockingAngleTolerance = 2f)
        {
            if (string.IsNullOrWhiteSpace(configuredSlotId))
                throw new ArgumentException("设施交互候选位必须提供稳定 slot id。", nameof(configuredSlotId));

            slotId = configuredSlotId.Trim();
            animationSemantic = configuredSemantic;
            primaryHandTarget = configuredPrimaryHandTarget;
            approachTolerance = Mathf.Max(0.01f, configuredApproachTolerance);
            dockingPositionTolerance = Mathf.Max(0.005f, configuredDockingPositionTolerance);
            dockingAngleTolerance = Mathf.Clamp(configuredDockingAngleTolerance, 0.1f, 30f);
        }

        private void OnValidate()
        {
            slotId = slotId?.Trim() ?? string.Empty;
            approachTolerance = Mathf.Max(0.01f, approachTolerance);
            dockingPositionTolerance = Mathf.Max(0.005f, dockingPositionTolerance);
            dockingAngleTolerance = Mathf.Clamp(dockingAngleTolerance, 0.1f, 30f);
        }
    }
}
