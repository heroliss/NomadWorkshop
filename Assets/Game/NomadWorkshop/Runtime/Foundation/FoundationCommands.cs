using System.ComponentModel;
using Game.Framework.Command;
using Game.NomadWorkshop.Simulation;
using R3;
using ObservableCollections;
using UnityEngine;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>View 一次取得 Foundation 的只读响应式数据束；所有写入仍只能走 Command。</summary>
    public readonly struct FoundationReadModel
    {
        public readonly ReadOnlyReactiveProperty<bool> IsReady;
        public readonly ReadOnlyReactiveProperty<long> JourneyPositionMicrometers;
        public readonly ReadOnlyReactiveProperty<int> StopWaterMilliliters;
        public readonly ReadOnlyReactiveProperty<bool> StopWaterRequested;
        public readonly ReadOnlyReactiveProperty<bool> StopWaterActive;
        public readonly ReadOnlyReactiveProperty<int> StopWasteMilliliters;
        public readonly ReadOnlyReactiveProperty<int> StopWasteCapacityMilliliters;
        public readonly ReadOnlyReactiveProperty<bool> StopWasteRequested;
        public readonly ReadOnlyReactiveProperty<bool> StopWasteActive;
        public readonly ReadOnlyReactiveProperty<string> CarriedWasteBucketFacilityId;
        public readonly ReadOnlyReactiveProperty<int> StopSpareCount;
        public readonly ReadOnlyReactiveProperty<bool> StopSpareRequested;
        public readonly ReadOnlyReactiveProperty<bool> StopSpareActive;
        public readonly ReadOnlyReactiveProperty<int> CarriedWasteMilliliters;
        public readonly ReadOnlyReactiveProperty<string> StopWorkFeedback;
        public readonly ReadOnlyReactiveProperty<bool> StopAccessOpen;
        public readonly ReadOnlyReactiveProperty<long> JourneyFuelPicoliters;
        public readonly ReadOnlyReactiveProperty<NomadJourneyEndpoint> JourneyDestination;
        public readonly ReadOnlyReactiveProperty<NomadJourneyStatus> JourneyStatus;
        public readonly ReadOnlyReactiveProperty<bool> IsPaused;
        public readonly ReadOnlyReactiveProperty<bool> CheckpointBusy;
        public readonly ReadOnlyReactiveProperty<string> CheckpointFeedback;
        public readonly ReadOnlyReactiveProperty<float> SimulationSpeed;
        public readonly ReadOnlyReactiveProperty<long> SimulationTick;
        public readonly ReadOnlyReactiveProperty<long> LifeDay;
        public readonly ReadOnlyReactiveProperty<int> LifeMinuteOfDay;
        public readonly ReadOnlyReactiveProperty<int> LifeDayProgressPermille;
        public readonly ReadOnlyReactiveProperty<long> ClimateYear;
        public readonly ReadOnlyReactiveProperty<int> SeasonIndex;
        public readonly ReadOnlyReactiveProperty<int> ClimateWeekInSeason;
        public readonly ReadOnlyReactiveProperty<int> SeasonProgressPermille;
        public readonly ReadOnlyReactiveProperty<NomadWeatherKind> CurrentWeather;
        public readonly ReadOnlyReactiveProperty<int> SandstormIntensityPermille;
        public readonly ReadOnlyReactiveProperty<FoundationInteractionMode> InteractionMode;
        public readonly ReadOnlyReactiveProperty<FoundationPlacementPreviewState> PlacementPreview;
        public readonly ReadOnlyReactiveProperty<FoundationBuildTransactionPhase> BuildTransactionPhase;
        public readonly ReadOnlyReactiveProperty<int> PositionSnapMillimeters;
        public readonly ReadOnlyReactiveProperty<int> RotationSnapDeciDegrees;
        public readonly ReadOnlyReactiveProperty<bool> ShowPlacementGrid;
        public readonly ReadOnlyReactiveProperty<int> FacilityRevision;
        public readonly ReadOnlyReactiveProperty<int> FacilityAccessRevision;
        public readonly ReadOnlyReactiveProperty<int> FacilityInventoryRevision;
        public readonly ReadOnlyReactiveProperty<int> FacilityConditionRevision;
        public readonly ReadOnlyReactiveProperty<int> WorldItemPlacementRevision;
        public readonly ReadOnlyReactiveProperty<FoundationWaterCanLocation> WaterCanLocation;
        public readonly ReadOnlyReactiveProperty<string> WaterCanAnchorFacilityInstanceId;
        public readonly ReadOnlyReactiveProperty<FoundationItemPlacementState> WaterCanPlacement;
        public readonly ReadOnlyReactiveProperty<int> WaterCanWaterMilliliters;
        public readonly ReadOnlyReactiveProperty<int> WaterCanCapacityMilliliters;
        public readonly ReadOnlyReactiveProperty<int> VehicleWaterMilliliters;
        public readonly ReadOnlyReactiveProperty<int> VehicleWaterCapacityMilliliters;
        public readonly ReadOnlyReactiveProperty<int> DrinkingStationWaterMilliliters;
        public readonly ReadOnlyReactiveProperty<int> DrinkingStationCapacityMilliliters;
        public readonly ReadOnlyReactiveProperty<int> ToiletHoldingWasteMilliliters;
        public readonly ReadOnlyReactiveProperty<int> ToiletHoldingCapacityMilliliters;

        public readonly FoundationResidentReadModel PrimaryResident;
        public readonly IReadOnlyObservableList<FoundationResidentReadModel> Residents;
        public readonly ReadOnlyReactiveProperty<string> WaterCanCarrierId;
        public readonly ReadOnlyReactiveProperty<string> WasteBucketCarrierId;
        public readonly ReadOnlyReactiveProperty<string> DepartureFeedback;
        public readonly ReadOnlyReactiveProperty<string> BuildFeedback;

        public FoundationReadModel(NomadFoundationModel model)
        {
            PrimaryResident = new FoundationResidentReadModel(model.PrimaryResident);
            Residents = model.Residents;
            WaterCanCarrierId = model.WaterCanCarrierId;
            WasteBucketCarrierId = model.WasteBucketCarrierId;
            DepartureFeedback = model.DepartureFeedback;
            BuildFeedback = model.BuildFeedback;
            IsReady = model.IsReady;
            JourneyPositionMicrometers = model.JourneyPositionMicrometers;
            StopWaterMilliliters = model.StopWaterMilliliters;
            StopWaterRequested = model.StopWaterRequested;
            StopWaterActive = model.StopWaterActive;
            StopWasteMilliliters = model.StopWasteMilliliters;
            StopWasteCapacityMilliliters = model.StopWasteCapacityMilliliters;
            StopWasteRequested = model.StopWasteRequested;
            StopWasteActive = model.StopWasteActive;
            CarriedWasteBucketFacilityId = model.CarriedWasteBucketFacilityId;
            StopSpareCount = model.StopSpareCount;
            StopSpareRequested = model.StopSpareRequested;
            StopSpareActive = model.StopSpareActive;
            CarriedWasteMilliliters = model.CarriedWasteMilliliters;
            StopWorkFeedback = model.StopWorkFeedback;
            StopAccessOpen = model.StopAccessOpen;
            JourneyFuelPicoliters = model.JourneyFuelPicoliters;
            JourneyDestination = model.JourneyDestination;
            JourneyStatus = model.JourneyStatus;
            IsPaused = model.IsPaused;
            CheckpointBusy = model.CheckpointBusy;
            CheckpointFeedback = model.CheckpointFeedback;
            SimulationSpeed = model.SimulationSpeed;
            SimulationTick = model.SimulationTick;
            LifeDay = model.LifeDay;
            LifeMinuteOfDay = model.LifeMinuteOfDay;
            LifeDayProgressPermille = model.LifeDayProgressPermille;
            ClimateYear = model.ClimateYear;
            SeasonIndex = model.SeasonIndex;
            ClimateWeekInSeason = model.ClimateWeekInSeason;
            SeasonProgressPermille = model.SeasonProgressPermille;
            CurrentWeather = model.CurrentWeather;
            SandstormIntensityPermille = model.SandstormIntensityPermille;
            InteractionMode = model.InteractionMode;
            PlacementPreview = model.PlacementPreview;
            BuildTransactionPhase = model.BuildTransactionPhase;
            PositionSnapMillimeters = model.PositionSnapMillimeters;
            RotationSnapDeciDegrees = model.RotationSnapDeciDegrees;
            ShowPlacementGrid = model.ShowPlacementGrid;
            FacilityRevision = model.FacilityRevision;
            FacilityAccessRevision = model.FacilityAccessRevision;
            FacilityInventoryRevision = model.FacilityInventoryRevision;
            FacilityConditionRevision = model.FacilityConditionRevision;
            WorldItemPlacementRevision = model.WorldItemPlacementRevision;
            WaterCanLocation = model.WaterCanLocation;
            WaterCanAnchorFacilityInstanceId = model.WaterCanAnchorFacilityInstanceId;
            WaterCanPlacement = model.WaterCanPlacement;
            WaterCanWaterMilliliters = model.WaterCanWaterMilliliters;
            WaterCanCapacityMilliliters = model.WaterCanCapacityMilliliters;
            VehicleWaterMilliliters = model.VehicleWaterMilliliters;
            VehicleWaterCapacityMilliliters = model.VehicleWaterCapacityMilliliters;
            DrinkingStationWaterMilliliters = model.DrinkingStationWaterMilliliters;
            DrinkingStationCapacityMilliliters = model.DrinkingStationCapacityMilliliters;
            ToiletHoldingWasteMilliliters = model.ToiletHoldingWasteMilliliters;
            ToiletHoldingCapacityMilliliters = model.ToiletHoldingCapacityMilliliters;
        }
    }

    [Description("读取 Foundation 的响应式只读状态束")]
    public readonly struct GetFoundationReadModelCommand : ICommand<FoundationReadModel>
    {
        public FoundationReadModel Execute(ICommandContext ctx) =>
            new(ctx.GetModel<NomadFoundationModel>());
    }

    /// <summary>请求一次有限水源取水，或召回取水者；返回是否接受意图，不直接转移资源。</summary>
    [Description("派出取水者或召回")]
    public readonly struct RequestFoundationStopWaterCommand : ICommand<bool>
    {
        private readonly bool _collect;
        public RequestFoundationStopWaterCommand(bool collect) => _collect = collect;
        public bool Execute(ICommandContext ctx) => ctx.GetSystem<NomadFoundationSystem>().RequestStopWater(_collect);
    }

    /// <summary>请求一次整桶清运或召回；返回是否接受意图，倾倒只能在实际接收点发生。</summary>
    [Description("清运旱厕污物或召回")]
    public readonly struct RequestFoundationStopWasteCommand : ICommand<bool>
    {
        private readonly bool _dispose;
        public RequestFoundationStopWasteCommand(bool dispose) => _dispose = dispose;
        public bool Execute(ICommandContext ctx) => ctx.GetSystem<NomadFoundationSystem>().RequestStopWaste(_dispose);
    }

    /// <summary>请求一次有限备件往返或召回；物品只在居民到位后交接。</summary>
    [Description("派遣或召回驿站备件补给者")]
    public readonly struct RequestFoundationStopSpareCommand : ICommand<bool>
    {
        private readonly bool _collect;
        public RequestFoundationStopSpareCommand(bool collect) => _collect = collect;
        public bool Execute(ICommandContext ctx) => ctx.GetSystem<NomadFoundationSystem>().RequestStopSpare(_collect);
    }

    /// <summary>选择首段路线端点或取消目标；不会直接授予驾驶权或改变居民位置。</summary>
    [Description("设定旅途目标")]
    public readonly struct SetFoundationJourneyDestinationCommand : ICommand
    {
        private readonly NomadJourneyEndpoint _destination;
        public SetFoundationJourneyDestinationCommand(NomadJourneyEndpoint destination) =>
            _destination = destination;
        public void Execute(ICommandContext ctx) =>
            ctx.GetSystem<NomadFoundationSystem>().SetJourneyDestination(_destination);
    }

    [Description("读取已提交设施快照")]
    public readonly struct GetFoundationFacilitiesCommand : ICommand<FoundationFacilityState[]>
    {
        public FoundationFacilityState[] Execute(ICommandContext ctx) =>
            ctx.GetModel<NomadFoundationModel>().GetFacilitySnapshot();
    }

    [Description("读取设施功能点与可达性快照")]
    public readonly struct GetFoundationFacilityAccessCommand :
        ICommand<FoundationFacilityAccessState[]>
    {
        public FoundationFacilityAccessState[] Execute(ICommandContext ctx) =>
            ctx.GetModel<NomadFoundationModel>().GetFacilityAccessSnapshot();
    }

    [Description("读取每座设施的实例库存快照")]
    public readonly struct GetFoundationFacilityInventoriesCommand :
        ICommand<FoundationFacilityInventoryState[]>
    {
        public FoundationFacilityInventoryState[] Execute(ICommandContext ctx) =>
            ctx.GetModel<NomadFoundationModel>().GetFacilityInventorySnapshot();
    }

    [Description("读取设施磨损、保养、积灰与故障快照")]
    public readonly struct GetFoundationFacilityConditionsCommand :
        ICommand<FoundationFacilityConditionState[]>
    {
        public FoundationFacilityConditionState[] Execute(ICommandContext ctx) =>
            ctx.GetModel<NomadFoundationModel>().GetFacilityConditionSnapshot();
    }

    [Description("读取已落位世界物品及其精确区域姿态")]
    public readonly struct GetFoundationWorldItemPlacementsCommand :
        ICommand<FoundationItemPlacementState[]>
    {
        public FoundationItemPlacementState[] Execute(ICommandContext ctx) =>
            ctx.GetModel<NomadFoundationModel>().GetWorldItemPlacementSnapshot();
    }

    [Description("启动首只杯具的原子拿取、携带与放下验证")]
    public readonly struct TryStartFoundationCupMoveCommand : ICommand<bool>
    {
        private readonly string _residentId;

        /// <summary>指定执行居民；省略时保留首位居民的开发验证入口。</summary>
        public TryStartFoundationCupMoveCommand(string residentId) => _residentId = residentId;

        public bool Execute(ICommandContext ctx) =>
            ctx.GetSystem<NomadFoundationSystem>().TryStartCupMoveHarness(_residentId);
    }

    /// <summary>
    /// 暂停态运行生产固定步并逐步审计。总时长须为 10 ms 的正整数倍；输入分块可为
    /// 10–1000 ms 的任意整数，不改变内部业务步；预算上限按实际业务步数计算。
    /// </summary>
    [Description("暂停态按统一业务步运行 Foundation 长时模拟并返回结构化证据")]
    public readonly struct RunFoundationSoakHarnessCommand :
        ICommand<FoundationSoakRunResult>
    {
        private readonly long _durationMilliseconds;
        private readonly int _stepMilliseconds;
        private readonly long _observableStallLimitMilliseconds;

        public RunFoundationSoakHarnessCommand(
            long durationMilliseconds,
            int stepMilliseconds = 100,
            long observableStallLimitMilliseconds = 30_000L)
        {
            _durationMilliseconds = durationMilliseconds;
            _stepMilliseconds = stepMilliseconds;
            _observableStallLimitMilliseconds = observableStallLimitMilliseconds;
        }

        public FoundationSoakRunResult Execute(ICommandContext ctx) =>
            ctx.GetSystem<NomadFoundationSystem>().RunSoakHarness(
                _durationMilliseconds,
                _stepMilliseconds,
                _observableStallLimitMilliseconds);
    }

    [Description("开发 Harness 切换世界 Seed 并丢弃当前进度后复位")]
    public readonly struct ResetFoundationForSoakHarnessCommand : ICommand<bool>
    {
        private readonly int _worldSeed;

        public ResetFoundationForSoakHarnessCommand(int worldSeed) =>
            _worldSeed = worldSeed;

        public bool Execute(ICommandContext ctx) =>
            ctx.GetSystem<NomadFoundationSystem>().TryResetForSoakHarness(_worldSeed);
    }

    [Description("读取当前可建造设施选项")]
    public readonly struct GetFoundationBuildOptionsCommand : ICommand<FoundationBuildOption[]>
    {
        public FoundationBuildOption[] Execute(ICommandContext ctx) =>
            ctx.GetSystem<NomadFoundationSystem>().GetBuildOptionsSnapshot();
    }

    [Description("进入 Foundation 建造模式")]
    public readonly struct EnterFoundationBuildModeCommand : ICommand
    {
        public void Execute(ICommandContext ctx) =>
            ctx.GetSystem<NomadFoundationSystem>().EnterBuildMode();
    }

    [Description("退出 Foundation 建造模式并取消未提交预览")]
    public readonly struct ExitFoundationBuildModeCommand : ICommand
    {
        public void Execute(ICommandContext ctx) =>
            ctx.GetSystem<NomadFoundationSystem>().ExitBuildMode();
    }

    [Description("开始放置指定设施的建造预览")]
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

    [Description("确认当前设施放置事务")]
    public readonly struct ConfirmFacilityPlacementCommand : ICommand
    {
        public void Execute(ICommandContext ctx) =>
            ctx.GetSystem<NomadFoundationSystem>().ConfirmPlacement();
    }

    [Description("取消当前设施放置事务")]
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
