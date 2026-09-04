using Game.Framework.Command;
using R3;
using UnityEngine;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>View 一次取得 Foundation 的只读响应式数据束；所有写入仍只能走 Command。</summary>
    public readonly struct FoundationReadModel
    {
        public readonly ReadOnlyReactiveProperty<bool> IsReady;
        public readonly ReadOnlyReactiveProperty<bool> IsPaused;
        public readonly ReadOnlyReactiveProperty<float> SimulationSpeed;
        public readonly ReadOnlyReactiveProperty<long> SimulationTick;
        public readonly ReadOnlyReactiveProperty<long> LifeDay;
        public readonly ReadOnlyReactiveProperty<int> LifeMinuteOfDay;
        public readonly ReadOnlyReactiveProperty<int> LifeDayProgressPermille;
        public readonly ReadOnlyReactiveProperty<long> ClimateYear;
        public readonly ReadOnlyReactiveProperty<int> SeasonIndex;
        public readonly ReadOnlyReactiveProperty<int> ClimateWeekInSeason;
        public readonly ReadOnlyReactiveProperty<int> SeasonProgressPermille;
        public readonly ReadOnlyReactiveProperty<FoundationInteractionMode> InteractionMode;
        public readonly ReadOnlyReactiveProperty<FoundationPlacementPreviewState> PlacementPreview;
        public readonly ReadOnlyReactiveProperty<FoundationBuildTransactionPhase> BuildTransactionPhase;
        public readonly ReadOnlyReactiveProperty<int> PositionSnapMillimeters;
        public readonly ReadOnlyReactiveProperty<int> RotationSnapDeciDegrees;
        public readonly ReadOnlyReactiveProperty<bool> ShowPlacementGrid;
        public readonly ReadOnlyReactiveProperty<int> FacilityRevision;
        public readonly ReadOnlyReactiveProperty<int> FacilityAccessRevision;
        public readonly ReadOnlyReactiveProperty<int> FacilityInventoryRevision;
        public readonly ReadOnlyReactiveProperty<FoundationResidentPhase> ResidentPhase;
        public readonly ReadOnlyReactiveProperty<Vector3> ResidentLocalPosition;
        public readonly ReadOnlyReactiveProperty<float> ResidentLocalYawDegrees;
        public readonly ReadOnlyReactiveProperty<float> RemainingPathMeters;
        public readonly ReadOnlyReactiveProperty<int> RemainingPathCorners;
        public readonly ReadOnlyReactiveProperty<string> ActivePathSummary;
        public readonly ReadOnlyReactiveProperty<bool> ResidentCarryingWater;
        public readonly ReadOnlyReactiveProperty<FoundationWaterCanLocation> WaterCanLocation;
        public readonly ReadOnlyReactiveProperty<string> WaterCanAnchorFacilityInstanceId;
        public readonly ReadOnlyReactiveProperty<int> WaterCanWaterMilliliters;
        public readonly ReadOnlyReactiveProperty<int> WaterCanCapacityMilliliters;
        public readonly ReadOnlyReactiveProperty<FoundationActionPlanProjection> LatestActionPlan;
        public readonly ReadOnlyReactiveProperty<float> ResidentThirst;
        public readonly ReadOnlyReactiveProperty<float> ResidentEntertainment;
        public readonly ReadOnlyReactiveProperty<float> ResidentMood;
        public readonly ReadOnlyReactiveProperty<float> ResidentFatigue;
        public readonly ReadOnlyReactiveProperty<float> ResidentStress;
        public readonly ReadOnlyReactiveProperty<int> VehicleWaterMilliliters;
        public readonly ReadOnlyReactiveProperty<int> VehicleWaterCapacityMilliliters;
        public readonly ReadOnlyReactiveProperty<int> DrinkingStationWaterMilliliters;
        public readonly ReadOnlyReactiveProperty<int> DrinkingStationCapacityMilliliters;
        public readonly ReadOnlyReactiveProperty<int> BodyWaterMilliliters;
        public readonly ReadOnlyReactiveProperty<int> BodyWaterCapacityMilliliters;
        public readonly ReadOnlyReactiveProperty<int> BladderWasteMilliliters;
        public readonly ReadOnlyReactiveProperty<int> BladderCapacityMilliliters;
        public readonly ReadOnlyReactiveProperty<int> ToiletHoldingWasteMilliliters;
        public readonly ReadOnlyReactiveProperty<int> ToiletHoldingCapacityMilliliters;
        public readonly ReadOnlyReactiveProperty<float> ActionProgress;
        public readonly ReadOnlyReactiveProperty<int> CompletedDrinkCount;
        public readonly ReadOnlyReactiveProperty<int> CompletedToiletUseCount;
        public readonly ReadOnlyReactiveProperty<int> CompletedLeisureCount;
        public readonly ReadOnlyReactiveProperty<int> CompletedDaydreamCount;
        public readonly ReadOnlyReactiveProperty<int> CompletedWanderCount;
        public readonly ReadOnlyReactiveProperty<int> CompletedHobbyCount;
        public readonly ReadOnlyReactiveProperty<string> CurrentTask;
        public readonly ReadOnlyReactiveProperty<string> LastBlocker;

        public FoundationReadModel(NomadFoundationModel model)
        {
            IsReady = model.IsReady;
            IsPaused = model.IsPaused;
            SimulationSpeed = model.SimulationSpeed;
            SimulationTick = model.SimulationTick;
            LifeDay = model.LifeDay;
            LifeMinuteOfDay = model.LifeMinuteOfDay;
            LifeDayProgressPermille = model.LifeDayProgressPermille;
            ClimateYear = model.ClimateYear;
            SeasonIndex = model.SeasonIndex;
            ClimateWeekInSeason = model.ClimateWeekInSeason;
            SeasonProgressPermille = model.SeasonProgressPermille;
            InteractionMode = model.InteractionMode;
            PlacementPreview = model.PlacementPreview;
            BuildTransactionPhase = model.BuildTransactionPhase;
            PositionSnapMillimeters = model.PositionSnapMillimeters;
            RotationSnapDeciDegrees = model.RotationSnapDeciDegrees;
            ShowPlacementGrid = model.ShowPlacementGrid;
            FacilityRevision = model.FacilityRevision;
            FacilityAccessRevision = model.FacilityAccessRevision;
            FacilityInventoryRevision = model.FacilityInventoryRevision;
            ResidentPhase = model.ResidentPhase;
            ResidentLocalPosition = model.ResidentLocalPosition;
            ResidentLocalYawDegrees = model.ResidentLocalYawDegrees;
            RemainingPathMeters = model.RemainingPathMeters;
            RemainingPathCorners = model.RemainingPathCorners;
            ActivePathSummary = model.ActivePathSummary;
            ResidentCarryingWater = model.ResidentCarryingWater;
            WaterCanLocation = model.WaterCanLocation;
            WaterCanAnchorFacilityInstanceId = model.WaterCanAnchorFacilityInstanceId;
            WaterCanWaterMilliliters = model.WaterCanWaterMilliliters;
            WaterCanCapacityMilliliters = model.WaterCanCapacityMilliliters;
            LatestActionPlan = model.LatestActionPlan;
            ResidentThirst = model.ResidentThirst;
            ResidentEntertainment = model.ResidentEntertainment;
            ResidentMood = model.ResidentMood;
            ResidentFatigue = model.ResidentFatigue;
            ResidentStress = model.ResidentStress;
            VehicleWaterMilliliters = model.VehicleWaterMilliliters;
            VehicleWaterCapacityMilliliters = model.VehicleWaterCapacityMilliliters;
            DrinkingStationWaterMilliliters = model.DrinkingStationWaterMilliliters;
            DrinkingStationCapacityMilliliters = model.DrinkingStationCapacityMilliliters;
            BodyWaterMilliliters = model.BodyWaterMilliliters;
            BodyWaterCapacityMilliliters = model.BodyWaterCapacityMilliliters;
            BladderWasteMilliliters = model.BladderWasteMilliliters;
            BladderCapacityMilliliters = model.BladderCapacityMilliliters;
            ToiletHoldingWasteMilliliters = model.ToiletHoldingWasteMilliliters;
            ToiletHoldingCapacityMilliliters = model.ToiletHoldingCapacityMilliliters;
            ActionProgress = model.ActionProgress;
            CompletedDrinkCount = model.CompletedDrinkCount;
            CompletedToiletUseCount = model.CompletedToiletUseCount;
            CompletedLeisureCount = model.CompletedLeisureCount;
            CompletedDaydreamCount = model.CompletedDaydreamCount;
            CompletedWanderCount = model.CompletedWanderCount;
            CompletedHobbyCount = model.CompletedHobbyCount;
            CurrentTask = model.CurrentTask;
            LastBlocker = model.LastBlocker;
        }
    }

    public readonly struct GetFoundationReadModelCommand : ICommand<FoundationReadModel>
    {
        public FoundationReadModel Execute(ICommandContext ctx) =>
            new(ctx.GetModel<NomadFoundationModel>());
    }

    public readonly struct GetFoundationFacilitiesCommand : ICommand<FoundationFacilityState[]>
    {
        public FoundationFacilityState[] Execute(ICommandContext ctx) =>
            ctx.GetModel<NomadFoundationModel>().GetFacilitySnapshot();
    }

    public readonly struct GetFoundationFacilityAccessCommand :
        ICommand<FoundationFacilityAccessState[]>
    {
        public FoundationFacilityAccessState[] Execute(ICommandContext ctx) =>
            ctx.GetModel<NomadFoundationModel>().GetFacilityAccessSnapshot();
    }

    public readonly struct GetFoundationFacilityInventoriesCommand :
        ICommand<FoundationFacilityInventoryState[]>
    {
        public FoundationFacilityInventoryState[] Execute(ICommandContext ctx) =>
            ctx.GetModel<NomadFoundationModel>().GetFacilityInventorySnapshot();
    }

    public readonly struct GetFoundationBuildOptionsCommand : ICommand<FoundationBuildOption[]>
    {
        public FoundationBuildOption[] Execute(ICommandContext ctx) =>
            ctx.GetSystem<NomadFoundationSystem>().GetBuildOptionsSnapshot();
    }

    public readonly struct EnterFoundationBuildModeCommand : ICommand
    {
        public void Execute(ICommandContext ctx) =>
            ctx.GetSystem<NomadFoundationSystem>().EnterBuildMode();
    }

    public readonly struct ExitFoundationBuildModeCommand : ICommand
    {
        public void Execute(ICommandContext ctx) =>
            ctx.GetSystem<NomadFoundationSystem>().ExitBuildMode();
    }

    public readonly struct BeginFacilityPlacementCommand : ICommand
    {
        private readonly string _definitionId;

        public BeginFacilityPlacementCommand(string definitionId) => _definitionId = definitionId;

        public void Execute(ICommandContext ctx) =>
            ctx.GetSystem<NomadFoundationSystem>().BeginPlacement(_definitionId);
    }

    public readonly struct MoveFacilityPreviewCommand : ICommand
    {
        private readonly int _xMillimeters;
        private readonly int _zMillimeters;

        public MoveFacilityPreviewCommand(int xMillimeters, int zMillimeters)
        {
            _xMillimeters = xMillimeters;
            _zMillimeters = zMillimeters;
        }

        public void Execute(ICommandContext ctx) =>
            ctx.GetSystem<NomadFoundationSystem>().MovePreview(_xMillimeters, _zMillimeters);
    }

    public readonly struct RotateFacilityPreviewCommand : ICommand
    {
        private readonly int _direction;

        public RotateFacilityPreviewCommand(int direction) => _direction = direction;

        public void Execute(ICommandContext ctx) =>
            ctx.GetSystem<NomadFoundationSystem>().RotatePreview(_direction);
    }

    public readonly struct SetFoundationPositionSnapCommand : ICommand
    {
        private readonly int _millimeters;

        public SetFoundationPositionSnapCommand(int millimeters) => _millimeters = millimeters;

        public void Execute(ICommandContext ctx) =>
            ctx.GetSystem<NomadFoundationSystem>().SetPositionSnap(_millimeters);
    }

    public readonly struct SetFoundationRotationSnapCommand : ICommand
    {
        private readonly int _deciDegrees;

        public SetFoundationRotationSnapCommand(int deciDegrees) => _deciDegrees = deciDegrees;

        public void Execute(ICommandContext ctx) =>
            ctx.GetSystem<NomadFoundationSystem>().SetRotationSnap(_deciDegrees);
    }

    public readonly struct SetFoundationGridVisibleCommand : ICommand
    {
        private readonly bool _visible;

        public SetFoundationGridVisibleCommand(bool visible) => _visible = visible;

        public void Execute(ICommandContext ctx) =>
            ctx.GetSystem<NomadFoundationSystem>().SetGridVisible(_visible);
    }

    public readonly struct ConfirmFacilityPlacementCommand : ICommand
    {
        public void Execute(ICommandContext ctx) =>
            ctx.GetSystem<NomadFoundationSystem>().ConfirmPlacement();
    }

    public readonly struct CancelFacilityPlacementCommand : ICommand
    {
        public void Execute(ICommandContext ctx) =>
            ctx.GetSystem<NomadFoundationSystem>().CancelPlacement();
    }

    public readonly struct SetFoundationPausedCommand : ICommand
    {
        private readonly bool _paused;

        public SetFoundationPausedCommand(bool paused) => _paused = paused;

        public void Execute(ICommandContext ctx) =>
            ctx.GetSystem<NomadFoundationSystem>().SetPaused(_paused);
    }

    public readonly struct SetFoundationSpeedCommand : ICommand
    {
        private readonly float _speed;

        public SetFoundationSpeedCommand(float speed) => _speed = speed;

        public void Execute(ICommandContext ctx) =>
            ctx.GetSystem<NomadFoundationSystem>().SetSimulationSpeed(_speed);
    }

    public readonly struct ResetFoundationSliceCommand : ICommand
    {
        public void Execute(ICommandContext ctx) =>
            ctx.GetSystem<NomadFoundationSystem>().ResetScenario();
    }
}
