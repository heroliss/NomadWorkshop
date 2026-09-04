using Game.Framework.Model;
using R3;
using UnityEngine;

namespace Game.NomadWorkshop.Navigation
{
    public enum NavigationInteractionSpikePhase
    {
        Booting,
        Crossing,
        FirstApproach,
        FirstDocking,
        FirstOpening,
        FirstClosing,
        SecondApproach,
        SecondDocking,
        SecondOpening,
        SecondClosing,
        Completed,
        Blocked,
    }

    /// <summary>连续导航 Harness 的 Inspector 数据面；业务库存仍由纯 C# 资源账本拥有。</summary>
    public sealed class NomadNavigationSpikeModel : MonoModelBase
    {
        [field: SerializeField] public RP<bool> IsReady { get; private set; } = new(false);
        [field: SerializeField] public RP<NavigationInteractionSpikePhase> Phase { get; private set; } =
            new(NavigationInteractionSpikePhase.Booting);
        [field: SerializeField] public RP<string> CurrentStatus { get; private set; } = new("等待初始化");
        [field: SerializeField] public RP<string> LastBlocker { get; private set; } = new(string.Empty);

        [Header("路径证据")]
        [field: SerializeField] public RP<float> OpenPathLengthRatio { get; private set; } = new(0f);
        [field: SerializeField] public RP<int> OpenPathCorners { get; private set; } = new(0);
        [field: SerializeField] public RP<float> ObstaclePathLengthRatio { get; private set; } = new(0f);
        [field: SerializeField] public RP<int> ObstaclePathCorners { get; private set; } = new(0);
        [field: SerializeField] public RP<bool> CrossingCompleted { get; private set; } = new(false);
        [field: SerializeField] public RP<float> MinimumAgentSeparation { get; private set; } = new(999f);
        [field: SerializeField] public RP<int> StuckRecoveryCount { get; private set; } = new(0);

        [Header("候选停靠位与互斥")]
        [field: SerializeField] public RP<string> ResidentASelectedSlot { get; private set; } =
            new(string.Empty);
        [field: SerializeField] public RP<string> ResidentBSelectedSlot { get; private set; } =
            new(string.Empty);
        [field: SerializeField] public RP<int> ReservationContentionCount { get; private set; } = new(0);
        [field: SerializeField] public RP<int> CompletedInteractionCount { get; private set; } = new(0);
        [field: SerializeField] public RP<float> DoorOpenProgress { get; private set; } = new(0f);

        [Header("真实物品位置")]
        [field: SerializeField] public RP<int> CabinetItemCount { get; private set; } = new(0);
        [field: SerializeField] public RP<int> ResidentAItemCount { get; private set; } = new(0);
        [field: SerializeField] public RP<int> ResidentBItemCount { get; private set; } = new(0);
        [field: SerializeField] public RP<Vector3> ResidentAPosition { get; private set; } = new(Vector3.zero);
        [field: SerializeField] public RP<Vector3> ResidentBPosition { get; private set; } = new(Vector3.zero);

        internal void ResetProjection()
        {
            IsReady.Value = false;
            Phase.Value = NavigationInteractionSpikePhase.Booting;
            CurrentStatus.Value = "正在构建连续导航";
            LastBlocker.Value = string.Empty;
            OpenPathLengthRatio.Value = 0f;
            OpenPathCorners.Value = 0;
            ObstaclePathLengthRatio.Value = 0f;
            ObstaclePathCorners.Value = 0;
            CrossingCompleted.Value = false;
            MinimumAgentSeparation.Value = 999f;
            StuckRecoveryCount.Value = 0;
            ResidentASelectedSlot.Value = string.Empty;
            ResidentBSelectedSlot.Value = string.Empty;
            ReservationContentionCount.Value = 0;
            CompletedInteractionCount.Value = 0;
            DoorOpenProgress.Value = 0f;
            CabinetItemCount.Value = 0;
            ResidentAItemCount.Value = 0;
            ResidentBItemCount.Value = 0;
            ResidentAPosition.Value = Vector3.zero;
            ResidentBPosition.Value = Vector3.zero;
        }
    }
}
