using Game.Framework.Command;
using R3;
using UnityEngine;

namespace Game.NomadWorkshop.Navigation
{
    /// <summary>View 可持有的连续导航只读投影。</summary>
    public readonly struct NavigationSpikeReadModel
    {
        public readonly ReadOnlyReactiveProperty<bool> IsReady;
        public readonly ReadOnlyReactiveProperty<NavigationInteractionSpikePhase> Phase;
        public readonly ReadOnlyReactiveProperty<string> CurrentStatus;
        public readonly ReadOnlyReactiveProperty<string> LastBlocker;
        public readonly ReadOnlyReactiveProperty<float> OpenPathLengthRatio;
        public readonly ReadOnlyReactiveProperty<int> OpenPathCorners;
        public readonly ReadOnlyReactiveProperty<float> ObstaclePathLengthRatio;
        public readonly ReadOnlyReactiveProperty<int> ObstaclePathCorners;
        public readonly ReadOnlyReactiveProperty<bool> CrossingCompleted;
        public readonly ReadOnlyReactiveProperty<float> MinimumAgentSeparation;
        public readonly ReadOnlyReactiveProperty<int> StuckRecoveryCount;
        public readonly ReadOnlyReactiveProperty<string> ResidentASelectedSlot;
        public readonly ReadOnlyReactiveProperty<string> ResidentBSelectedSlot;
        public readonly ReadOnlyReactiveProperty<int> ReservationContentionCount;
        public readonly ReadOnlyReactiveProperty<int> CompletedInteractionCount;
        public readonly ReadOnlyReactiveProperty<float> DoorOpenProgress;
        public readonly ReadOnlyReactiveProperty<int> CabinetItemCount;
        public readonly ReadOnlyReactiveProperty<int> ResidentAItemCount;
        public readonly ReadOnlyReactiveProperty<int> ResidentBItemCount;
        public readonly ReadOnlyReactiveProperty<Vector3> ResidentAPosition;
        public readonly ReadOnlyReactiveProperty<Vector3> ResidentBPosition;

        public NavigationSpikeReadModel(NomadNavigationSpikeModel model)
        {
            IsReady = model.IsReady;
            Phase = model.Phase;
            CurrentStatus = model.CurrentStatus;
            LastBlocker = model.LastBlocker;
            OpenPathLengthRatio = model.OpenPathLengthRatio;
            OpenPathCorners = model.OpenPathCorners;
            ObstaclePathLengthRatio = model.ObstaclePathLengthRatio;
            ObstaclePathCorners = model.ObstaclePathCorners;
            CrossingCompleted = model.CrossingCompleted;
            MinimumAgentSeparation = model.MinimumAgentSeparation;
            StuckRecoveryCount = model.StuckRecoveryCount;
            ResidentASelectedSlot = model.ResidentASelectedSlot;
            ResidentBSelectedSlot = model.ResidentBSelectedSlot;
            ReservationContentionCount = model.ReservationContentionCount;
            CompletedInteractionCount = model.CompletedInteractionCount;
            DoorOpenProgress = model.DoorOpenProgress;
            CabinetItemCount = model.CabinetItemCount;
            ResidentAItemCount = model.ResidentAItemCount;
            ResidentBItemCount = model.ResidentBItemCount;
            ResidentAPosition = model.ResidentAPosition;
            ResidentBPosition = model.ResidentBPosition;
        }
    }

    public readonly struct GetNavigationSpikeReadModelCommand : ICommand<NavigationSpikeReadModel>
    {
        public NavigationSpikeReadModel Execute(ICommandContext ctx) =>
            new(ctx.GetModel<NomadNavigationSpikeModel>());
    }

    public readonly struct RestartNavigationSpikeCommand : ICommand
    {
        public void Execute(ICommandContext ctx) =>
            ctx.GetSystem<NomadNavigationSpikeSystem>().RestartScenario();
    }
}
