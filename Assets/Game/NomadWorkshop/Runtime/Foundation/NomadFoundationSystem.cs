using System;
using System.Collections.Generic;
using System.Text;
using Game.Framework.Common;
using Game.Framework.Systems;
using Game.NomadWorkshop.Navigation;
using Game.NomadWorkshop.Simulation;
using UnityEngine;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>
    /// Foundation 切片的唯一实时逻辑所有者：连续建造、可回滚 NavMesh 更新、居民路径与水搬运。
    /// Update 推进持续状态机，离散玩家意图仍只经 Command 进入。
    /// </summary>
    public sealed partial class NomadFoundationSystem : MonoSystemBase
    {
        private const int SimulationStepMilliseconds = 10;
        private const int MaximumSimulationStepsPerFrame = 100;
        private const float DrinkNeedThreshold = 0.55f;
        private const string BladderOpportunityRandomStreamId =
            "resident-need-opportunity:bladder";
        private const float MaximumTravelSampleOffset = 0.36f;
        private const int FreeRotationStepDeciDegrees = 50;
        private const int PreviewReachabilityCellMillimeters = 100;
        // 100 mm 采样格内任意点到最近格心至多约 71 mm；75 mm 能覆盖量化误差，
        // 又不会像旧 360 mm 容差那样隔着设施把另一侧格子误判成目标可达。
        private const int PreviewEndpointSampleRadiusMillimeters = 75;
        private const int VehicleWaterCapacityMilliliters = 100_000;
        private const int InitialVehicleWaterMilliliters = 60_000;
        private const int WaterCanCapacityMilliliters = 5_000;
        private const int WaterHaulBatchMilliliters = 2_000;
        private const int DrinkingStationCapacityMilliliters = 6_000;
        private const int DrinkingStationRestockTargetMilliliters = 4_000;
        private const int DefaultToiletHoldingCapacityMilliliters = 12_000;
        private const int MaximumPreviewInteractionSlots = 64;
        private const float ResidentExactNavMeshTolerance = 0.02f;
        private const string WaterCanItemId = "water-can-01";
        private const string WaterCanDefinitionId = "water-can";
        private const string WaterCanParkingRegionLocalId = "water-can-parking";
        private const string StarterCupItemId = "cup-01";
        private const string StarterCupDefinitionId = "drinking-cup";
        private const string CountertopRegionLocalId = "countertop-center";
        private const string WaterValveRepairKitItemId = "water-valve-kit-01";
        private const string WaterValveRepairKitDefinitionId = "water-valve-repair-kit";
        private const string MaintenanceTrayRegionLocalId = "maintenance-tray";
        private const string WaterPickupInteractionGroupId = "water-pickup";
        private const string MaintenanceSupplyInteractionGroupId = "maintenance-supply";
        private const string ServiceValveInteractionGroupId = "service-valve";
        private const string DrinkAndDeliverInteractionGroupId = "drink-and-deliver";
        private const string ToiletInteractionGroupId = "use-toilet";
        private const string HobbyInteractionGroupId = "paint-and-observe";
        private const string KitchenInteractionGroupId = "cook";
        private static readonly NomadCalendarPolicy CalendarPolicy = NomadCalendarPolicy.Default;
        private static readonly NomadEnvironmentSchedule EnvironmentSchedule =
            NomadEnvironmentSchedule.Default;
        // 50% 以下不为如厕单独生成意图，随后平滑非线性上升；90% 起必然进入紧急处理。
        // 相同数学原语也可用于饥饿、疲劳、卫生等需求，只需使用各自可调参数和随机流。
        private static readonly NeedPressureCurve BladderPressureCurve = new(
            onsetDeficit: 0.5f,
            urgentDeficit: 0.9f,
            responseExponent: 2f,
            maximumPressure: 3.5f);

        [Header("共享定义")]
        [SerializeField, Tooltip("车辆主甲板尺寸、业务坐标与默认吸附参数。")]
        private DeckLayoutDefinition deckLayout;
        [SerializeField, Tooltip("本切片允许建造或预置的设施定义；运行时按稳定 id 建立目录。")]
        private NomadFacilityDefinition[] facilityDefinitions =
            Array.Empty<NomadFacilityDefinition>();
        [SerializeField, Tooltip("水罐、杯子等可搬动物品的稳定占地与灰盒表现定义。")]
        private NomadWorldItemDefinition[] worldItemDefinitions =
            Array.Empty<NomadWorldItemDefinition>();
        [SerializeField, Tooltip("确定性世界种子；居民决策、需求机会与行动级随机流都从它派生，运行检查点会保存并恢复。")]
        private int worldSeed = 1729;

        [Header("居民灰盒节奏")]
        [SerializeField, Tooltip("居民脚底根节点的甲板局部出生位置；运行时会把 Y 规范为甲板表面 0，胶囊半高只由 View 的子视觉负责。")]
        private Vector3 residentStartLocalPosition = new(0f, 0f, 2.4f);
        [SerializeField, Min(0.1f), Tooltip("居民沿连续 NavMesh 路径移动的米/秒。")]
        private float residentMoveSpeed = 2.8f;
        [SerializeField, Min(0.01f), Tooltip("取得容器或完成一次装水动作的灰盒时长；搬运毫升数不直接线性放大动画时间。")]
        private float pickupSeconds = 1.2f;
        [SerializeField, Min(0.01f), Tooltip("把容器中的物资交付到设施库存的灰盒动作时长。")]
        private float deliverySeconds = 1.2f;
        [SerializeField, Min(0.01f), Tooltip("居民在饮水站完成一次饮水的灰盒动作时长。")]
        private float drinkingSeconds = 3.2f;
        [SerializeField, Min(0.01f), Tooltip("居民在旱厕完成一次排泄物转移的灰盒动作时长。")]
        private float toiletSeconds = 4f;
        [SerializeField, Min(0.1f), Tooltip("一份 300 mL 饮水全部转化为膀胱内容物所需的模拟秒数；这是玩法节奏，不是现实生理时长。")]
        private float drinkMetabolismSeconds = 8f;
        [SerializeField, Range(0f, 1f), Tooltip("新场景中居民的初始口渴缺口。")]
        private float initialThirst = 0.78f;
        [SerializeField, Min(0f), Tooltip("每模拟秒增加的口渴缺口；每份 300 mL 饮水按玩法参数降低它。")]
        private float thirstIncreasePerSecond = 0.004f;
        [SerializeField, Min(1), Tooltip("居民体内已饮用、尚未完成代谢的水容量（mL）。")]
        private int bodyWaterCapacityMilliliters =
            ResidentWaterCycle.DefaultBodyWaterCapacityMilliliters;
        [SerializeField, Min(1), Tooltip("膀胱内容物容量（mL）；排泄压力按实际体积比例计算。")]
        private int bladderCapacityMilliliters =
            ResidentWaterCycle.DefaultBladderCapacityMilliliters;
        [SerializeField, Range(0.25f, 16f), Tooltip("场景初始化后的模拟倍率。暂停独立控制。")]
        private float initialSimulationSpeed = 1f;
        [SerializeField, Min(0.1f), Tooltip("路线暂时失效时的自动重试间隔；避免每帧重复查询 NavMesh。")]
        private float routeRetrySeconds = 0.5f;
        [SerializeField, Min(0.1f), Tooltip("空闲或等待时重新评估下一项主要行动的模拟秒间隔；需求概率只在这些决策边界采样，不会逐帧掷骰。")]
        private float residentDecisionRetrySeconds = 0.5f;

        [Header("车辆水箱状态（整数确定性积分）")]
        [SerializeField, Tooltip("每模拟毫秒增加的等效磨损细分单位；1,000,000 单位 = 1‰，默认约每生活日 10.2‰。")]
        private long waterTankWearUnitsPerMillisecond = 17L;
        [SerializeField, Tooltip("每模拟毫秒增加的维护欠账细分单位；默认约每生活日 150‰，完成保养会降低它。")]
        private long waterTankMaintenanceDebtUnitsPerMillisecond = 250L;
        [SerializeField, Tooltip("每模拟毫秒增加的积尘细分单位；默认约每生活日 100.2‰，沙尘冲击会额外增加。")]
        private long waterTankDustUnitsPerMillisecond = 167L;
        [SerializeField, Tooltip("设备状态为零时，每模拟秒仍会累计的基础微风险；风险达到本轮预取样阈值才产生具体故障。")]
        private long waterTankBaseMicroHazardPerSecond = 50L;
        [SerializeField, Tooltip("每 1‰ 等效磨损、每模拟秒贡献的微风险。它影响故障概率，不把磨损直接等同于损坏。")]
        private long waterTankWearMicroHazardPerPermilleSecond = 1L;
        [SerializeField, Tooltip("每 1‰ 维护欠账、每模拟秒贡献的微风险；首版让拖延保养成为主要可控风险来源。")]
        private long waterTankMaintenanceMicroHazardPerPermilleSecond = 2L;
        [SerializeField, Tooltip("每 1‰ 积尘、每模拟秒贡献的微风险；清洁会降低后续风险率，但不会倒扣过去暴露。")]
        private long waterTankDustMicroHazardPerPermilleSecond = 1L;
        [SerializeField, Tooltip("沙尘暴强度 100% 时额外增加的每毫秒等效磨损细分单位；会按天气强度线性缩放。")]
        private long waterTankSandstormWearUnitsPerMillisecond = 55L;
        [SerializeField, Tooltip("沙尘暴强度 100% 时额外增加的每毫秒维护欠账细分单位。")]
        private long waterTankSandstormMaintenanceDebtUnitsPerMillisecond = 120L;
        [SerializeField, Tooltip("沙尘暴强度 100% 时额外增加的每毫秒积尘细分单位，是首版天气影响的主要来源。")]
        private long waterTankSandstormDustUnitsPerMillisecond = 2_000L;
        [SerializeField, Tooltip("沙尘暴强度 100% 时额外增加的每模拟秒基础微风险；不会直接伪造故障。")]
        private long waterTankSandstormBaseMicroHazardPerSecond = 1_000L;

        [Header("居民身心连续状态")]
        [SerializeField, Range(0f, 1f), Tooltip("新场景中居民的正向娱乐满足度；普通发呆和闲逛不会提高它。")]
        private float initialEntertainment = 0.68f;
        [SerializeField, Range(0f, 1f), Tooltip("新场景中居民的正向心情基线。")]
        private float initialMood = 0.7f;
        [SerializeField, Range(0f, 1f), Tooltip("新场景中居民的疲劳负担；0 为精力充足，1 为极度疲劳。")]
        private float initialFatigue = 0.28f;
        [SerializeField, Range(0f, 1f), Tooltip("新场景中居民的压力负担；0 为平静，1 为压力极高。")]
        private float initialStress = 0.22f;
        [SerializeField, Range(0f, 1f), Tooltip("新场景中居民的正向健康值；严重缺水会损害健康，归零后居民死亡。")]
        private float initialHealth = 1f;
        [SerializeField, Range(0.5f, 1.5f), Tooltip("居民的长期基础工作效率；100% 表示完成标准人力时间恰好需要标准时长。")]
        private float residentBaseWorkEfficiency = 1f;
        [SerializeField, Range(0f, 0.25f), Tooltip("每次工作开始时固定采样的速度波动半径；0.08 表示在预期效率上下各浮动最多 8%，不会逐帧抖动。")]
        private float workPaceVariation = 0.08f;

        [Header("居民维修灰盒节奏")]
        [SerializeField, Min(0.1f), Tooltip("使用已搬到故障点的维修包处理水箱出水阀所需标准人力秒；实际时长由行动开始时固定采样的工作效率换算。")]
        private float waterTankRepairSeconds = 16f;

        [Header("空闲休整灰盒节奏")]
        [SerializeField, Min(0.1f), Tooltip("发呆或散步到达后的停留时长；首版保留足够观察窗口，后续再由性格与身心状态形成随机区间。")]
        private float leisureSeconds = 3.5f;
        [SerializeField, Min(0.1f), Tooltip("缺少床或座椅时坐卧地面的低质量休息时长；它恢复较慢并轻微降低心情。")]
        private float groundRestSeconds = 20f;
        [SerializeField, Min(0.1f), Tooltip("居民在观景画架完成一次作画爱好的时长；只有实际使用设施的阶段才恢复娱乐满足度。")]
        private float hobbySeconds = 8f;
        [SerializeField, Range(0f, 1f), Tooltip("居民 01 对作画与观景的个人偏好。首版放在 System 便于 Inspector 调试，后续迁入居民档案数据。")]
        private float residentPaintingAffinity = 0.34f;
        [SerializeField, Min(0.2f), Tooltip("随机散步目标与当前位置的最小路径距离。")]
        private float minimumWanderDistance = 0.9f;
        [SerializeField, Min(0.5f), Tooltip("随机散步目标与当前位置的最大请求半径。")]
        private float maximumWanderDistance = 2.8f;

        [Header("实体搬运容器")]
        [SerializeField, Tooltip("水罐的真实搬运能力；缺少 LiquidTight 时水任务会在取罐前硬阻塞。")]
        private CargoContainerCapability waterCanCapabilities =
            CargoContainerCapability.LiquidTight | CargoContainerCapability.Sealable;
        [SerializeField, Range(0f, 1f), Tooltip("水罐洁净度；以后会参与水品质与污染风险。")]
        private float waterCanCleanliness = 0.96f;
        [SerializeField, Min(1), Tooltip("新建每座旱厕的独立暂存桶容量（mL）；满后等待清运或使用其他有容量厕所。")]
        private int toiletHoldingCapacityMilliliters =
            DefaultToiletHoldingCapacityMilliliters;

        private readonly Dictionary<string, NomadFacilityDefinition> _definitions =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, NomadWorldItemDefinition> _worldItemDefinitions =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, PlacementFootprint> _worldItemFootprints =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, DeckNavigationObstacleHandle> _navigationObstacles =
            new(StringComparer.Ordinal);
        private readonly List<ContinuousPlacedFacility> _previewCommittedFacilities = new();
        private readonly Dictionary<string, FacilityAccessEvaluation> _committedFacilityAccess =
            new(StringComparer.Ordinal);
        private readonly List<FoundationFacilityAccessState> _facilityAccessProjection = new();
        private readonly Dictionary<string, ResourceInventory> _drinkingStationInventories =
            new(StringComparer.Ordinal);
        private readonly List<FoundationFacilityInventoryState> _facilityInventoryProjection = new();

        private NomadFoundationModel _model;
        private FoundationResidentExecution _resident;
        private DeckNavigationUtility _navigation;
        private ContinuousFacilityPlacementLedger _placementLedger;
        private PlacementRegionLedger _worldItemPlacementLedger;
        private ContinuousDeckReachabilityProbe _previewReachabilityProbe;
        private FoundationInteractionSpaceRuntime _interactionSpaces;
        private DeckPlacementSnapSettings _snapSettings;
        private NomadFacilityDefinition _activePlacementDefinition;
        private DeckPose _previewPose;
        private int _placementBeganFrame;
        private int _nextFacilitySequence;
        private int _residentClearanceMillimeters;
        private bool _previewResidentOriginResolved;
        private DeckPose _previewResolvedResidentOrigin;
        private bool _pendingResidentOriginResolved;
        private DeckPose _pendingResolvedResidentOrigin;
        private ulong _previewSharedSlotMask;

        private AsyncOperation _navigationUpdate;
        private ContinuousFacilityPlacementRequest _pendingPlacement;
        private DeckNavigationObstacleHandle _pendingObstacle;
        private FoundationPlacementFailure _rollbackFailure;
        private bool _cancelPlacementRequested;
        private bool _resetRequested;
        private bool _exitBuildModeRequested;

        private ResourceFlowLedger _resourceFlow;
        private ResourceInventory _vehicleWater;
        private ResourceInventory _waterCan;
        private readonly ResidentActionPlanEvaluator _actionPlanEvaluator = new();
        private readonly ResidentActionPlanPolicy _actionPlanPolicy = new();
        private readonly UtilityDecisionEngine _decisionEngine = new();
        private readonly UtilityDecisionPolicy _residentDecisionPolicy =
            ResidentLeisurePlanFactory.CreateSelectionPolicy();
        private readonly NomadSimulationClock _simulationClock =
            new(stepMilliseconds: SimulationStepMilliseconds);
        private FoundationWaterCanLocation _waterCanLocation;
        private string _waterCanAnchorFacilityInstanceId = string.Empty;
        private PlacementRegionItem _waterCanPlacement;
        private float _dockingNavMeshTolerance;
        private bool _initialized;

        protected override void Awake()
        {
            base.Awake();
            ValidateAndBuildCatalog();
        }

        private void Start()
        {
            _model = this.GetModel<NomadFoundationModel>();
            _navigation = this.GetUtility<DeckNavigationUtility>();
            ResetScenarioNow();
        }

        private void Update()
        {
            if (!_initialized) return;
            ReadNativeLocomotionFrame();
            if (_model.BuildTransactionPhase.Value != FoundationBuildTransactionPhase.Idle)
            {
                AdvanceBuildTransaction();
                WriteSimulationProjection();
                return;
            }
            if (_model.IsPaused.Value) return;

            float clampedSpeed = Mathf.Clamp(_model.SimulationSpeed.Value, 0.25f, 16f);
            if (!Mathf.Approximately(clampedSpeed, _model.SimulationSpeed.Value))
                _model.SimulationSpeed.Value = clampedSpeed;
            _simulationClock.AccumulateFrame(
                Time.unscaledDeltaTime,
                clampedSpeed);
            // 每个阶段只消费固定业务步，长帧 / 倍速只增加步数。上限保护 Editor 响应；
            // 没来得及执行的预算留在时钟中，下帧继续，不静默吞掉生活或旅途时间。
            for (var step = 0; step < MaximumSimulationStepsPerFrame; step++)
            {
                if (!_simulationClock.TryAdvanceStep()) break;
                AdvanceSimulation(SimulationStepMilliseconds);
                if (_model.IsPaused.Value ||
                    _model.BuildTransactionPhase.Value != FoundationBuildTransactionPhase.Idle)
                    break;
            }
        }

        /// <summary>
        /// 推进一次已经提交到统一时钟的模拟步。实时 Update 与长时 Harness 共用这条路径，
        /// 避免跑数工具复制一份随后悄悄漂移的生理、设施状态或行动状态机。
        /// </summary>
        private void AdvanceSimulation(long deltaMilliseconds)
        {
            if (deltaMilliseconds < 0L) throw new ArgumentOutOfRangeException(nameof(deltaMilliseconds));
            if (deltaMilliseconds > 0L)
            {
                AdvanceFacilityConditionsTo(_simulationClock.SimulationTick);
                foreach (var resident in _residents)
                {
                    using var scope = UseResident(resident);
                    AdvanceResident(deltaMilliseconds);
                }
            }
            WriteSimulationProjection();
        }

        private void AdvanceResident(long deltaMilliseconds)
        {
            if (deltaMilliseconds < 0L)
                throw new ArgumentOutOfRangeException(
                    nameof(deltaMilliseconds),
                    "Foundation 模拟步长不能为负数。 ");
            if (deltaMilliseconds == 0L)
            {
                return;
            }

            if (_resident.Phase == FoundationResidentPhase.Dead)
            {
                return;
            }

            float deltaTime = deltaMilliseconds / 1000f;
            ResidentWaterCycleTick physiologyTick = _resident.WaterCycle.Advance(
                deltaTime,
                _resourceFlow);
            ObservePhysiologyBackpressure(physiologyTick.Blocker);
            _resident.Wellbeing.Advance(
                deltaTime,
                ResolveWellbeingActivity(),
                new ResidentWellbeingDrivers(
                    _resident.WaterCycle.Thirst,
                    _resident.WaterCycle.ExcretionPressure,
                    _resident.Phase is FoundationResidentPhase.Blocked or
                        FoundationResidentPhase.WaitingForRoute),
                _resident.LeisureOutcomeScale);
            if (!_resident.Wellbeing.IsAlive)
            {
                HandleResidentDeath();
                return;
            }

            // 等待驾驶路线时也要允许需求中断，不能因路径重试而饿着或渴着等待到死亡。
            if (IsDrivingAction() && MustLeaveDriving())
                StopDrivingAction("已安全停车，先处理居民需求");
            AdvanceStopVisitRecall();

            switch (_resident.Phase)
            {
                case FoundationResidentPhase.MovingToStopSpare:
                    if (AdvanceResidentAlongPath(deltaTime))
                        BeginTimedPhase(FoundationResidentPhase.PickingUpStopSpare, pickupSeconds, "拿取驿站预留维修包");
                    break;
                case FoundationResidentPhase.PickingUpStopSpare:
                    if (TickTimer(deltaTime)) CompleteStopSparePickup();
                    break;
                case FoundationResidentPhase.ReturningStopSpare:
                    if (AdvanceResidentAlongPath(deltaTime))
                    {
                        ClearActiveMoveIntent();
                        BeginTimedPhase(FoundationResidentPhase.DeliveringStopSpare, deliverySeconds, "在维护托盘交付备件并结束外勤");
                    }
                    break;
                case FoundationResidentPhase.DeliveringStopSpare:
                    if (TickTimer(deltaTime)) CompleteStopSpareDelivery();
                    break;
                case FoundationResidentPhase.MovingToWasteBucket:
                    if (AdvanceResidentAlongPath(deltaTime))
                    {
                        ClearActiveMoveIntent();
                        BeginTimedPhase(FoundationResidentPhase.DetachingWasteBucket, pickupSeconds, "从旱厕取出密封污物桶");
                    }
                    break;
                case FoundationResidentPhase.DetachingWasteBucket:
                    if (TickTimer(deltaTime)) CompleteWasteBucketPickup();
                    break;
                case FoundationResidentPhase.MovingToWasteReceiver:
                    if (AdvanceResidentAlongPath(deltaTime))
                        BeginTimedPhase(FoundationResidentPhase.EmptyingWasteBucket, deliverySeconds, "在驿站接收口倾倒污物");
                    break;
                case FoundationResidentPhase.EmptyingWasteBucket:
                    if (TickTimer(deltaTime)) CompleteWasteDisposal();
                    break;
                case FoundationResidentPhase.ReturningWasteBucket:
                    if (AdvanceResidentAlongPath(deltaTime))
                    {
                        ClearActiveMoveIntent();
                        BeginTimedPhase(FoundationResidentPhase.InstallingWasteBucket, deliverySeconds, "装回旱厕污物桶");
                    }
                    break;
                case FoundationResidentPhase.InstallingWasteBucket:
                    if (TickTimer(deltaTime)) CompleteWasteBucketInstallation();
                    break;
                case FoundationResidentPhase.MovingToStopWater:
                    if (AdvanceResidentAlongPath(deltaTime))
                        BeginTimedPhase(FoundationResidentPhase.FillingAtStop, pickupSeconds, "从有限水源向水罐装水");
                    break;
                case FoundationResidentPhase.FillingAtStop:
                    if (TickTimer(deltaTime)) CompleteStopWaterPickup();
                    break;
                case FoundationResidentPhase.ReturningFromStopWater:
                    if (AdvanceResidentAlongPath(deltaTime))
                    {
                        ClearActiveMoveIntent();
                        BeginTimedPhase(FoundationResidentPhase.DeliveringStopWater, deliverySeconds, "在车辆水箱旁归还水罐与补给");
                    }
                    break;
                case FoundationResidentPhase.DeliveringStopWater:
                    if (TickTimer(deltaTime)) CompleteStopWaterDelivery();
                    break;
                case FoundationResidentPhase.WaitingForRoute:
                    _resident.RouteRetryRemaining -= deltaTime;
                    if (_resident.RouteRetryRemaining <= 0f) TryResumeActiveMove();
                    break;
                case FoundationResidentPhase.WaitingForFacility:
                case FoundationResidentPhase.Idle:
                    _resident.DecisionRetryRemaining -= deltaTime;
                    if (_resident.DecisionRetryRemaining <= 0f)
                    {
                        _resident.DecisionRetryRemaining = Mathf.Max(
                            0.1f,
                            residentDecisionRetrySeconds);
                        if (TryRespondToWaitingTraffic()) break;
                        TryStartResidentRoutine();
                    }
                    break;
                case FoundationResidentPhase.MovingToWaterCan:
                    if (AdvanceResidentAlongPath(deltaTime))
                        BeginWaterCanPickup();
                    break;
                case FoundationResidentPhase.PickingUpWaterCan:
                    if (TickTimer(deltaTime)) CompleteWaterCanPickup();
                    break;
                case FoundationResidentPhase.LiftingWaterCan:
                    if (TickTimer(deltaTime)) CompleteWaterCanLift();
                    break;
                case FoundationResidentPhase.MovingToWaterCanParking:
                    if (AdvanceResidentAlongPath(deltaTime)) BeginWaterCanPlacement();
                    break;
                case FoundationResidentPhase.PlacingWaterCan:
                    if (TickTimer(deltaTime)) CompleteWaterCanPlacement();
                    break;
                case FoundationResidentPhase.ReleasingWaterCan:
                    if (TickTimer(deltaTime)) CompleteWaterCanRelease();
                    break;
                case FoundationResidentPhase.MovingToWaterSource:
                    if (AdvanceResidentAlongPath(deltaTime))
                    {
                        ClearActiveMoveIntent();
                        BeginTimedPhase(
                            FoundationResidentPhase.PickingUpWater,
                            pickupSeconds,
                            "用防漏水罐从车辆水箱装水");
                    }
                    break;
                case FoundationResidentPhase.PickingUpWater:
                    if (TickTimer(deltaTime)) CompleteWaterPickup();
                    break;
                case FoundationResidentPhase.MovingToDrinkingStation:
                    if (AdvanceResidentAlongPath(deltaTime))
                    {
                        ClearActiveMoveIntent();
                        if (_resident.ActiveHaul != null)
                        {
                            BeginTimedPhase(
                                FoundationResidentPhase.DeliveringWater,
                                deliverySeconds,
                                "把水真实放入饮水站");
                        }
                        else
                        {
                            BeginTimedPhase(
                                FoundationResidentPhase.Drinking,
                                drinkingSeconds,
                                "在饮水站喝水");
                        }
                    }
                    break;
                case FoundationResidentPhase.DeliveringWater:
                    if (TickTimer(deltaTime)) CompleteWaterDelivery();
                    break;
                case FoundationResidentPhase.Drinking:
                    if (TickTimer(deltaTime)) CompleteDrink();
                    break;
                case FoundationResidentPhase.MovingToToilet:
                    if (AdvanceResidentAlongPath(deltaTime))
                    {
                        ClearActiveMoveIntent();
                        BeginTimedPhase(
                            FoundationResidentPhase.UsingToilet,
                            toiletSeconds,
                            "使用旱厕并把排泄物留在真实暂存桶");
                    }
                    break;
                case FoundationResidentPhase.UsingToilet:
                    if (TickTimer(deltaTime)) CompleteToiletUse();
                    break;
                case FoundationResidentPhase.MovingToLeisure:
                    if (AdvanceResidentAlongPath(deltaTime))
                    {
                        ClearActiveMoveIntent();
                        BeginTimedPhase(
                            FoundationResidentPhase.Relaxing,
                            leisureSeconds,
                            "在可达空地停留并观察周围");
                    }
                    break;
                case FoundationResidentPhase.Relaxing:
                    if (TickTimer(deltaTime)) CompleteLeisure();
                    break;
                case FoundationResidentPhase.RestingOnGround:
                    if (TickTimer(deltaTime)) CompleteGroundRest();
                    break;
                case FoundationResidentPhase.MovingToHobby:
                    if (AdvanceResidentAlongPath(deltaTime))
                    {
                        ClearActiveMoveIntent();
                        BeginTimedPhase(
                            FoundationResidentPhase.EnjoyingHobby,
                            hobbySeconds,
                            "在观景画架作画并观察车外景色");
                    }
                    break;
                case FoundationResidentPhase.EnjoyingHobby:
                    if (TickTimer(deltaTime)) CompleteHobby();
                    break;
                case FoundationResidentPhase.MovingToWorldItemSource:
                    if (AdvanceResidentAlongPath(deltaTime))
                    {
                        ClearActiveMoveIntent();
                        BeginTimedPhase(
                            FoundationResidentPhase.PickingUpWorldItem,
                            pickupSeconds,
                            "从已锁定来源姿态拿起世界物品");
                    }
                    break;
                case FoundationResidentPhase.PickingUpWorldItem:
                    if (TickTimer(deltaTime)) CompleteWorldItemPickup();
                    break;
                case FoundationResidentPhase.MovingToWorldItemDestination:
                    if (AdvanceResidentAlongPath(deltaTime))
                    {
                        ClearActiveMoveIntent();
                        BeginTimedPhase(
                            FoundationResidentPhase.PlacingWorldItem,
                            deliverySeconds,
                            "把手中世界物品放到已锁定目标姿态");
                    }
                    break;
                case FoundationResidentPhase.PlacingWorldItem:
                    if (TickTimer(deltaTime)) CompleteWorldItemPlacement();
                    break;
                case FoundationResidentPhase.MovingToRepairPart:
                    if (AdvanceResidentAlongPath(deltaTime))
                    {
                        ClearActiveMoveIntent();
                        BeginTimedPhase(
                            FoundationResidentPhase.PickingUpRepairPart,
                            pickupSeconds,
                            "从水箱维护托盘拿取出水阀维修包");
                    }
                    break;
                case FoundationResidentPhase.PickingUpRepairPart:
                    if (TickTimer(deltaTime)) CompleteRepairPartPickup();
                    break;
                case FoundationResidentPhase.MovingToRepairTarget:
                    if (AdvanceResidentAlongPath(deltaTime))
                    {
                        ClearActiveMoveIntent();
                        BeginTimedPhase(
                            FoundationResidentPhase.RepairingFacility,
                            waterTankRepairSeconds,
                            "在出水阀功能点更换卡滞部件");
                    }
                    break;
                case FoundationResidentPhase.RepairingFacility:
                    if (TickTimer(deltaTime)) CompletePrimaryWaterTankRepair();
                    break;
                case FoundationResidentPhase.MovingToDriver:
                    if (MustLeaveDriving()) StopDrivingAction("途中需求升高，取消到岗并保持停车");
                    else if (AdvanceResidentAlongPath(deltaTime)) CompleteDrivingApproach();
                    break;
                case FoundationResidentPhase.Driving:
                    AdvanceDriving(deltaMilliseconds);
                    break;
            }

        }

        public FoundationBuildOption[] GetBuildOptionsSnapshot()
        {
            var result = new List<FoundationBuildOption>();
            for (var i = 0; i < facilityDefinitions.Length; i++)
            {
                NomadFacilityDefinition definition = facilityDefinitions[i];
                if (definition == null || !definition.Buildable) continue;
                result.Add(new FoundationBuildOption(
                    definition.Id,
                    definition.DisplayName,
                    result.Count + 1));
            }
            return result.ToArray();
        }

        public void EnterBuildMode()
        {
            if (RejectBuildDuringCheckpoint()) return;
            if (!_initialized || _model == null) return;
            _model.InteractionMode.Value = FoundationInteractionMode.Build;
        }

        public void ExitBuildMode()
        {
            if (!_initialized || _model == null) return;
            if (_model.BuildTransactionPhase.Value != FoundationBuildTransactionPhase.Idle)
            {
                _exitBuildModeRequested = true;
                _cancelPlacementRequested = true;
                _model.BuildFeedback.Value = "等待导航事务安全回滚后退出建造模式";
                return;
            }

            ClearPlacementSelection(exitBuildMode: true);
        }

        public void BeginPlacement(string definitionId)
        {
            if (RejectBuildDuringCheckpoint()) return;
            if (FindStopVisitor() != null)
            {
                _model.BuildFeedback.Value = "请先召回车外作业者并装回容器，再调整甲板布局";
                return;
            }
            if (!_initialized ||
                _model.BuildTransactionPhase.Value != FoundationBuildTransactionPhase.Idle ||
                string.IsNullOrWhiteSpace(definitionId) ||
                !_definitions.TryGetValue(definitionId, out NomadFacilityDefinition definition) ||
                !definition.Buildable)
                return;

            _activePlacementDefinition = definition;
            _model.BuildFeedback.Value = string.Empty;
            _previewPose = _snapSettings.Apply(default);
            _placementBeganFrame = Time.frameCount;
            _model.InteractionMode.Value = FoundationInteractionMode.Build;
            RefreshPreview();
        }

        public void MovePreview(int xMillimeters, int zMillimeters)
        {
            if (_activePlacementDefinition == null ||
                _model.BuildTransactionPhase.Value != FoundationBuildTransactionPhase.Idle)
                return;

            DeckPose next = _snapSettings.Apply(new DeckPose(
                xMillimeters,
                zMillimeters,
                _previewPose.YawDeciDegrees,
                _previewPose.DeckLevel));
            if (next == _previewPose) return;
            _previewPose = next;
            RefreshPreview();
        }

        public void RotatePreview(int direction)
        {
            if (_activePlacementDefinition == null || direction == 0 ||
                _model.BuildTransactionPhase.Value != FoundationBuildTransactionPhase.Idle)
                return;

            int step = _snapSettings.RotationStepDeciDegrees > 0
                ? _snapSettings.RotationStepDeciDegrees
                : FreeRotationStepDeciDegrees;
            _previewPose = new DeckPose(
                _previewPose.XMillimeters,
                _previewPose.ZMillimeters,
                _previewPose.YawDeciDegrees + Math.Sign(direction) * step,
                _previewPose.DeckLevel);
            RefreshPreview();
        }

        public void SetPositionSnap(int millimeters)
        {
            if (millimeters < 0 || millimeters > 2000) return;
            _snapSettings = new DeckPlacementSnapSettings(
                millimeters,
                _snapSettings.RotationStepDeciDegrees,
                _snapSettings.OriginXMillimeters,
                _snapSettings.OriginZMillimeters);
            if (_model != null) _model.PositionSnapMillimeters.Value = millimeters;
            ApplyChangedSnapToPreview();
        }

        public void SetRotationSnap(int deciDegrees)
        {
            if (deciDegrees < 0 || deciDegrees > 3600) return;
            _snapSettings = new DeckPlacementSnapSettings(
                _snapSettings.PositionStepMillimeters,
                deciDegrees,
                _snapSettings.OriginXMillimeters,
                _snapSettings.OriginZMillimeters);
            if (_model != null) _model.RotationSnapDeciDegrees.Value = deciDegrees;
            ApplyChangedSnapToPreview();
        }

        public void SetGridVisible(bool visible)
        {
            if (_model != null) _model.ShowPlacementGrid.Value = visible;
        }

        public bool ConfirmPlacement()
        {
            if (RejectBuildDuringCheckpoint()) return false;
            if (FindStopVisitor() != null) return false;
            if (_activePlacementDefinition == null ||
                _model.BuildTransactionPhase.Value != FoundationBuildTransactionPhase.Idle ||
                Time.frameCount <= _placementBeganFrame)
                return false;

            RefreshPreview();
            if (!_model.PlacementPreview.Value.CanConfirm) return false;

            ContinuousFacilityPlacementRequest request = CreatePreviewRequest();

            try
            {
                _pendingPlacement = request;
                _pendingResidentOriginResolved = _previewResidentOriginResolved;
                _pendingResolvedResidentOrigin = _previewResolvedResidentOrigin;
                _pendingObstacle = _navigation.CreateFacilityObstacle(
                    request.InstanceId,
                    request.Pose,
                    request.Footprint);
                _navigationUpdate = _navigation.BeginUpdate();
                _model.BuildTransactionPhase.Value =
                    FoundationBuildTransactionPhase.UpdatingCandidateNavigation;
                SuspendNativeLocomotion();
                _model.BuildFeedback.Value = "正在验证新设施对连续导航与交互位的影响";
                _cancelPlacementRequested = false;
                _exitBuildModeRequested = false;
                SetPreview(FoundationPlacementFailure.TransactionInProgress);
                return true;
            }
            catch (Exception exception)
            {
                _navigation.DeactivateAndDestroyObstacle(_pendingObstacle);
                ClearPendingPlacement();
                SetPreview(FoundationPlacementFailure.NavigationUpdateFailed);
                _model.BuildFeedback.Value = exception.Message;
                return false;
            }
        }

        public void CancelPlacement()
        {
            if (_model != null &&
                _model.BuildTransactionPhase.Value ==
                FoundationBuildTransactionPhase.UpdatingCandidateNavigation)
            {
                _cancelPlacementRequested = true;
                _model.BuildFeedback.Value = "等待当前导航更新完成后取消候选设施";
                return;
            }
            if (_model != null &&
                _model.BuildTransactionPhase.Value ==
                FoundationBuildTransactionPhase.RollingBackNavigation)
            {
                _cancelPlacementRequested = true;
                return;
            }

            ClearPlacementSelection(exitBuildMode: false);
        }

        public void SetPaused(bool paused)
        {
            if (_checkpointOperation != null) return;
            if (_model != null) _model.IsPaused.Value = paused;
            if (paused) SuspendNativeLocomotion();
            else if (_model != null)
                foreach (var resident in _residents) ConfigureNativeMotion(resident);
        }

        public void SetSimulationSpeed(float speed)
        {
            if (_checkpointOperation != null) return;
            if (float.IsNaN(speed) || float.IsInfinity(speed)) return;
            initialSimulationSpeed = Mathf.Clamp(speed, 0.25f, 16f);
            if (_model != null) _model.SimulationSpeed.Value = initialSimulationSpeed;
        }

        /// <summary>重建同一个确定性 Foundation 起点；进行中的 NavMesh 更新会先完成安全回滚。</summary>
        public void ResetScenario()
        {
            if (_model != null &&
                _model.BuildTransactionPhase.Value != FoundationBuildTransactionPhase.Idle)
            {
                _resetRequested = true;
                _cancelPlacementRequested = true;
                _model.BuildFeedback.Value = "等待导航事务回滚后复位";
                return;
            }

            ResetScenarioNow();
        }

        /// <summary>隔离测试可缩短等待，不改变物质、连续摆放或导航事务规则。</summary>
        public void ConfigureTimingsForTests(float simulationSpeed, float moveSpeed)
        {
            SetSimulationSpeed(simulationSpeed);
            residentMoveSpeed = Mathf.Max(0.1f, moveSpeed);
            pickupSeconds = 0.01f;
            deliverySeconds = 0.01f;
            drinkingSeconds = 0.01f;
            leisureSeconds = 0.05f;
            groundRestSeconds = 0.05f;
            hobbySeconds = 0.5f;
            waterTankRepairSeconds = 0.1f;
        }

#if UNITY_EDITOR
        /// <summary>只供未激活 GameObject 上的隔离装配；必须在 Awake 前调用。</summary>
        public void ConfigureDefinitionsForTests(
            DeckLayoutDefinition configuredLayout,
            NomadFacilityDefinition[] configuredDefinitions,
            NomadWorldItemDefinition[] configuredWorldItemDefinitions = null)
        {
            if (_initialized)
                throw new InvalidOperationException("测试定义必须在 NomadFoundationSystem.Awake 前配置。");
            initialResidentCount = 1;
            nativeLocomotion = false;
            deckLayout = configuredLayout;
            facilityDefinitions = configuredDefinitions;
            worldItemDefinitions = configuredWorldItemDefinitions ??
                                   Array.Empty<NomadWorldItemDefinition>();
        }

        /// <summary>隔离测试可替换容器能力与洁净度，再经 ResetScenario 重建同一业务起点。</summary>
        public void ConfigureWaterCanForTests(
            CargoContainerCapability capabilities,
            float cleanliness)
        {
            waterCanCapabilities = capabilities;
            waterCanCleanliness = Mathf.Clamp01(cleanliness);
        }

        /// <summary>隔离测试设置居民身心起点和基础工作能力；修改后调用 ResetScenario。</summary>
        public void ConfigureHealthForTests(
            float health,
            float fatigue,
            float stress,
            float baseWorkEfficiency = 1f)
        {
            initialHealth = Mathf.Clamp01(health);
            initialFatigue = Mathf.Clamp01(fatigue);
            initialStress = Mathf.Clamp01(stress);
            residentBaseWorkEfficiency = Mathf.Clamp(baseWorkEfficiency, 0.5f, 1.5f);
        }

        /// <summary>隔离测试设置完整地面休息时长；修改后调用 ResetScenario。</summary>
        public void ConfigureGroundRestForTests(float seconds)
        {
            groundRestSeconds = Mathf.Max(0.1f, seconds);
        }

        /// <summary>
        /// 只供 PlayMode 回归压缩生理时间；修改后调用 ResetScenario，确保正式和测试都从同一构造路径建库存。
        /// </summary>
        public void ConfigurePhysiologyForTests(
            float configuredDrinkMetabolismSeconds,
            float configuredInitialThirst,
            float configuredThirstIncreasePerSecond,
            int configuredToiletHoldingCapacityMilliliters =
                DefaultToiletHoldingCapacityMilliliters,
            int configuredBodyWaterCapacityMilliliters =
                ResidentWaterCycle.DefaultBodyWaterCapacityMilliliters,
            int configuredBladderCapacityMilliliters =
                ResidentWaterCycle.DefaultBladderCapacityMilliliters)
        {
            drinkMetabolismSeconds = Mathf.Max(0.01f, configuredDrinkMetabolismSeconds);
            initialThirst = Mathf.Clamp01(configuredInitialThirst);
            thirstIncreasePerSecond = Mathf.Max(0f, configuredThirstIncreasePerSecond);
            toiletHoldingCapacityMilliliters = Mathf.Max(
                1,
                configuredToiletHoldingCapacityMilliliters);
            bodyWaterCapacityMilliliters = Mathf.Max(
                1,
                configuredBodyWaterCapacityMilliliters);
            bladderCapacityMilliliters = Mathf.Max(
                1,
                configuredBladderCapacityMilliliters);
            toiletSeconds = 0.01f;
            routeRetrySeconds = 0.01f;
        }

        /// <summary>隔离测试可设置身心起点与作画偏好；修改后调用 ResetScenario 统一重建。</summary>
        public void ConfigureWellbeingForTests(
            float entertainment,
            float mood,
            float fatigue,
            float stress,
            float paintingAffinity = 0.9f)
        {
            initialEntertainment = Mathf.Clamp01(entertainment);
            initialMood = Mathf.Clamp01(mood);
            initialFatigue = Mathf.Clamp01(fatigue);
            initialStress = Mathf.Clamp01(stress);
            residentPaintingAffinity = Mathf.Clamp01(paintingAffinity);
        }

        /// <summary>
        /// 长时模拟回归可恢复接近正式玩法的休闲时长，避免普通快速 PlayMode 配置把
        /// 0.05 秒发呆和 0.5 秒爱好的完成次数误读成实际时间占比。
        /// </summary>
        public void ConfigureLeisureTimingsForTests(
            float configuredLeisureSeconds,
            float configuredGroundRestSeconds,
            float configuredHobbySeconds)
        {
            leisureSeconds = Mathf.Max(0.1f, configuredLeisureSeconds);
            groundRestSeconds = Mathf.Max(0.1f, configuredGroundRestSeconds);
            hobbySeconds = Mathf.Max(0.1f, configuredHobbySeconds);
        }
#endif

        private void ResetScenarioNow(CheckpointOperation allowedOperation = null)
        {
            if (_model == null || _navigation == null) return;
            if (!ReferenceEquals(allowedOperation, _checkpointOperation)) RevokeCheckpointOperation(publish: true);

            RecreateResidentExecutions(initialResidentCount);
            ClearActivePath();
            ClearActiveMoveIntent();
            ClearPendingPlacement();
            _initialized = false;
            _model.IsReady.Value = false;
            _model.BuildTransactionPhase.Value = FoundationBuildTransactionPhase.Idle;
            _resetRequested = false;
            _cancelPlacementRequested = false;
            _exitBuildModeRequested = false;

            foreach (DeckNavigationObstacleHandle obstacle in _navigationObstacles.Values)
                _navigation.DeactivateAndDestroyObstacle(obstacle);
            _navigationObstacles.Clear();
            _facilityConditions.Clear();
            _committedFacilityAccess.Clear();
            _facilityAccessProjection.Clear();
            _model.ReplaceFacilityAccess(_facilityAccessProjection);
            _drinkingStationInventories.Clear();
            _toiletInventories.Clear();
            _facilityInventoryProjection.Clear();
            _model.ReplaceFacilityInventories(_facilityInventoryProjection);

            _placementLedger = deckLayout.CreatePlacementLedger();
            _worldItemPlacementLedger = new PlacementRegionLedger();
            _waterCanPlacement = null;
            _residentClearanceMillimeters = Mathf.CeilToInt(
                (_navigation.NavigationAgentRadiusMeters +
                 _navigation.EffectiveVoxelSizeMeters * 0.5f) * 1000f);
            _dockingNavMeshTolerance = Mathf.Max(
                ResidentExactNavMeshTolerance,
                _navigation.EffectiveVoxelSizeMeters * 0.55f);
            int interactionSpaceMergeDistanceMillimeters = Mathf.CeilToInt(
                _navigation.NavigationAgentRadiusMeters * 2000f);
            _previewReachabilityProbe = new ContinuousDeckReachabilityProbe(
                deckLayout.CreateBounds(),
                PreviewReachabilityCellMillimeters,
                _residentClearanceMillimeters,
                PreviewEndpointSampleRadiusMillimeters);
            _interactionSpaces?.Dispose();
            _interactionSpaces = new FoundationInteractionSpaceRuntime(
                interactionSpaceMergeDistanceMillimeters);
            _previewResidentOriginResolved = false;
            _pendingResidentOriginResolved = false;
            _previewSharedSlotMask = 0UL;
            _snapSettings = deckLayout.DefaultSnapSettings;
            _model.PositionSnapMillimeters.Value = _snapSettings.PositionStepMillimeters;
            _model.RotationSnapDeciDegrees.Value = _snapSettings.RotationStepDeciDegrees;
            _model.ShowPlacementGrid.Value = true;
            _nextFacilitySequence = 1;
            _resident.ActionSequence = 0;
            _resident.LeisureSequence = 0;
            // 统一把游标解释为“下一次要消费的事件序号”；首个居民决策沿用历史序号 1。
            _resident.DecisionSequence = 1;
            _resident.BladderOpportunitySequence = 0;
            _resident.WorkActionSequence = 0;
            _resident.LastPublishedDecisionDiagnostic = string.Empty;
            _resident.RouteRetryRemaining = 0f;
            _resident.DecisionRetryRemaining = 0f;
            _simulationClock.Restore(0L);
            ResetJourney();
            ResetStopWater();

            var initialFacilities = new List<FoundationFacilityState>();
            for (var i = 0; i < facilityDefinitions.Length; i++)
            {
                NomadFacilityDefinition definition = facilityDefinitions[i];
                if (definition == null || !definition.PlaceAtStart) continue;

                string instanceId = $"initial-{definition.Id}";
                var request = new ContinuousFacilityPlacementRequest(
                    instanceId,
                    definition.Id,
                    definition.StartPose,
                    definition.CreateFootprint(),
                    definition.CreateFunctionalClearanceFootprint());
                if (!_placementLedger.TryPlace(
                        request,
                        out _,
                        out ContinuousPlacementFailure failure))
                    throw new InvalidOperationException(
                        $"初始设施 '{definition.Id}' 无法摆放：{failure}。");

                DeckNavigationObstacleHandle obstacle = _navigation.CreateFacilityObstacle(
                    instanceId,
                    request.Pose,
                    request.Footprint);
                _navigationObstacles.Add(instanceId, obstacle);
                _facilityConditions.Add(
                    instanceId,
                    CreateFacilityCondition(instanceId));
                initialFacilities.Add(new FoundationFacilityState(
                    instanceId,
                    definition.Id,
                    request.Pose));
            }
            _model.ReplaceFacilities(initialFacilities);
            for (var i = 0; i < initialFacilities.Count; i++)
            {
                RegisterFacilityPlacementRegions(initialFacilities[i]);
                TryCreateStarterCupForFacility(initialFacilities[i]);
                TryCreateStarterRepairKitForFacility(initialFacilities[i]);
            }
            for (var residentIndex = 0; residentIndex < _residents.Count; residentIndex++)
            {
                var state = _residents[residentIndex].State;
                state.ResidentLocalPosition.Value = ToNavigationPoint(residentStartLocalPosition +
                    (residentIndex == 0 ? Vector3.zero : Vector3.right * (residentIndex == 1 ? -0.8f : 0.8f)));
                state.ResidentLocalYawDegrees.Value = 180f;
            }

            _navigation.BuildNow();
            RebuildCommittedInteractionSpaces(reacquireActiveSpace: false);
            if (!AreAllRequiredInteractionsReachable(null, null))
                throw new InvalidOperationException("初始设施布局切断了居民或必需交互位的连续通路。");
            RefreshCommittedFacilityAccess();

            _resourceFlow = new ResourceFlowLedger();
            _vehicleWater = new ResourceInventory(
                "vehicle-water-tank",
                ResourceMeasure.Milliliter,
                VehicleWaterCapacityMilliliters,
                new ResourceQuantity(
                    NomadResourceIds.Water,
                    InitialVehicleWaterMilliliters));
            _waterCan = new ResourceInventory(
                "water-can-01",
                ResourceMeasure.Milliliter,
                WaterCanCapacityMilliliters);
            string initialWaterCanAnchor = TryFindPlacedFacility(
                NomadFacilityFunction.VehicleWaterTank,
                out FoundationFacilityState initialWaterTank,
                out _)
                ? initialWaterTank.InstanceId
                : string.Empty;
            for (var i = 0; i < initialFacilities.Count; i++) AddFacilityInventory(initialFacilities[i]);
            foreach (var resident in _residents)
            {
                using var scope = UseResident(resident);
                InitializeResidentForNewScenario();
            }
            _model.SimulationSpeed.Value = initialSimulationSpeed;
            SetWaterCanLocation(FoundationWaterCanLocation.VehicleWaterTank, initialWaterCanAnchor);
            _model.WaterCanWaterMilliliters.Value = 0;
            _model.WaterCanCapacityMilliliters.Value = _waterCan.Capacity;
            _model.VehicleWaterCapacityMilliliters.Value = _vehicleWater.Capacity;
            _model.BuildFeedback.Value = string.Empty;
            ClearPlacementSelection(exitBuildMode: true);
            WriteSimulationProjection();

            _initialized = true;
            _model.IsReady.Value = true;
        }

        private void InitializeResidentForNewScenario()
        {
            _resident.DecisionSequence = 1L;
            _resident.State.ResidentCarryingWater.Value = false;
            _resident.State.ResidentCarriedWorldItem.Value = default;
            _resident.ActiveWaterSourceFacilityInstanceId = string.Empty;
            _resident.ActiveWaterTargetFacilityInstanceId = string.Empty;
            _resident.WaterCanContactPlacement = default;
            _resident.Wellbeing = new ResidentWellbeing(
                initialEntertainment,
                initialMood,
                initialFatigue,
                initialStress,
                initialHealth);
            _resident.WorkEfficiency = CalculateExpectedWorkEfficiency();
            _resident.LeisureOutcomeScale = 1f;
            _resident.WaterCycle = new ResidentWaterCycle(
                _resident.StableId,
                _resident.OwnerId,
                ResidentWaterCycle.DefaultDrinkServingMilliliters /
                Mathf.Max(0.01f, drinkMetabolismSeconds),
                drinkServingMilliliters: ResidentWaterCycle.DefaultDrinkServingMilliliters,
                initialThirst: initialThirst,
                thirstIncreasePerSecond: thirstIncreasePerSecond,
                thirstReliefPerServing: 0.72f,
                bodyWaterCapacityMilliliters: bodyWaterCapacityMilliliters,
                bladderCapacityMilliliters: bladderCapacityMilliliters);

            _resident.State.BodyWaterCapacityMilliliters.Value = _resident.WaterCycle.BodyWater.Capacity;
            _resident.State.BladderCapacityMilliliters.Value = _resident.WaterCycle.Bladder.Capacity;
            _resident.State.LatestActionPlan.Value = FoundationActionPlanProjection.None;
            _resident.State.MovementStallMilliseconds.Value = 0L;
            _resident.State.CompletedDrinkCount.Value = 0;
            _resident.State.CompletedToiletUseCount.Value = 0;
            _resident.State.CompletedLeisureCount.Value = 0;
            _resident.State.CompletedDaydreamCount.Value = 0;
            _resident.State.CompletedWanderCount.Value = 0;
            _resident.State.CompletedGroundRestCount.Value = 0;
            _resident.State.CompletedHobbyCount.Value = 0;
            _resident.State.CompletedWorldItemMoveCount.Value = 0;
            _resident.State.CompletedWaterTankRepairCount.Value = 0;
            _resident.State.LastBlocker.Value = string.Empty;
            _resident.State.ActionProgress.Value = 0f;
            _resident.LeisureKind = FoundationLeisureKind.None;
            SetResidentPhase(
                !_resident.Wellbeing.IsAlive
                    ? FoundationResidentPhase.Dead
                    : HasPlacedFacility(NomadFacilityFunction.DrinkingStation)
                        ? FoundationResidentPhase.Idle
                        : FoundationResidentPhase.WaitingForFacility,
                _resident.Wellbeing.IsAlive
                    ? "等待玩家建造饮水站"
                    : "居民初始健康为零，已经死亡");
        }

        private void ValidateAndBuildCatalog()
        {
            if (deckLayout == null)
                throw new MissingReferenceException("NomadFoundationSystem 缺少 DeckLayoutDefinition。");
            if (facilityDefinitions == null || facilityDefinitions.Length == 0)
                throw new MissingReferenceException("NomadFoundationSystem 至少需要一个设施定义。");
            if (worldItemDefinitions == null || worldItemDefinitions.Length == 0)
                throw new MissingReferenceException("NomadFoundationSystem 至少需要一个世界物品定义。");

            _worldItemDefinitions.Clear();
            _worldItemFootprints.Clear();
            for (var i = 0; i < worldItemDefinitions.Length; i++)
            {
                NomadWorldItemDefinition itemDefinition = worldItemDefinitions[i];
                if (itemDefinition == null)
                    throw new MissingReferenceException($"世界物品定义数组第 {i} 项为空。");
                itemDefinition.ValidateOrThrow();
                if (!_worldItemDefinitions.TryAdd(itemDefinition.Id, itemDefinition))
                    throw new InvalidOperationException(
                        $"世界物品稳定 id '{itemDefinition.Id}' 重复。");
                _worldItemFootprints.Add(itemDefinition.Id, itemDefinition.CreateFootprint());
            }
            if (!_worldItemFootprints.ContainsKey(WaterCanDefinitionId) ||
                !_worldItemFootprints.ContainsKey(StarterCupDefinitionId) ||
                !_worldItemFootprints.ContainsKey(WaterValveRepairKitDefinitionId))
                throw new InvalidOperationException(
                    $"Foundation 需要 '{WaterCanDefinitionId}' 与 " +
                    $"'{StarterCupDefinitionId}'、'{WaterValveRepairKitDefinitionId}' " +
                    "三种世界物品定义。");

            _definitions.Clear();
            var functions = new HashSet<NomadFacilityFunction>();
            for (var i = 0; i < facilityDefinitions.Length; i++)
            {
                NomadFacilityDefinition definition = facilityDefinitions[i];
                if (definition == null)
                    throw new MissingReferenceException($"设施定义数组第 {i} 项为空。");
                definition.ValidateOrThrow();
                definition.ValidateModelSpaceSnapshot();
                if (!_definitions.TryAdd(definition.Id, definition))
                    throw new InvalidOperationException($"设施稳定 id '{definition.Id}' 重复。");
                definition.CreateFootprint();
                definition.CreateFunctionalClearanceFootprint();
                if ((definition.Function is NomadFacilityFunction.VehicleWaterTank or
                     NomadFacilityFunction.DrinkingStation) &&
                    !definition.TryGetPlacementRegion(
                        WaterCanParkingRegionLocalId,
                        out _))
                    throw new InvalidOperationException(
                        $"设施 '{definition.Id}' 缺少水罐停放区域 " +
                        $"'{WaterCanParkingRegionLocalId}'。");
                int interactionSlotCount = CountInteractionSlots(definition);
                if (interactionSlotCount > MaximumPreviewInteractionSlots)
                    throw new InvalidOperationException(
                        $"设施 '{definition.Id}' 有 {interactionSlotCount} 个交互位，" +
                        $"超过实时预览上限 {MaximumPreviewInteractionSlots}。");
                functions.Add(definition.Function);
            }

            if (!functions.Contains(NomadFacilityFunction.VehicleWaterTank) ||
                !functions.Contains(NomadFacilityFunction.DrinkingStation))
                throw new InvalidOperationException("最小水循环需要车辆水箱与饮水站定义。");
        }

        private void ApplyChangedSnapToPreview()
        {
            if (_activePlacementDefinition == null ||
                _model == null ||
                _model.BuildTransactionPhase.Value != FoundationBuildTransactionPhase.Idle)
                return;
            _previewPose = _snapSettings.Apply(_previewPose);
            RefreshPreview();
        }

        private void RefreshPreview()
        {
            ContinuousFacilityPlacementRequest request = CreatePreviewRequest();
            int interactionSlotCount = CountInteractionSlots(_activePlacementDefinition);
            FoundationPlacementFailure failure =
                MapPlacementFailure(_placementLedger.Evaluate(request));
            if (failure != FoundationPlacementFailure.None)
            {
                _previewResidentOriginResolved = false;
                _previewSharedSlotMask = 0UL;
                PublishFacilityAccessProjection(previewEvaluated: false);
                SetPreview(
                    failure,
                    0UL,
                    0UL,
                    interactionSlotCount,
                    false,
                    0f,
                    0,
                    _previewReachabilityProbe.CellCount);
                return;
            }

            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            failure = EvaluateRealtimeReachability(request, out ulong reachableSlotMask);
            long finished = System.Diagnostics.Stopwatch.GetTimestamp();
            float elapsedMilliseconds = (float)(
                (finished - started) * 1000d /
                System.Diagnostics.Stopwatch.Frequency);
            SetPreview(
                failure,
                reachableSlotMask,
                _previewSharedSlotMask,
                interactionSlotCount,
                true,
                elapsedMilliseconds,
                _previewReachabilityProbe.VisitedCellCount,
                _previewReachabilityProbe.CellCount);
        }

        private FoundationPlacementFailure EvaluateRealtimeReachability(
            in ContinuousFacilityPlacementRequest candidate,
            out ulong reachableCandidateSlotMask)
        {
            _placementLedger.CopyStableSnapshotTo(_previewCommittedFacilities);
            _interactionSpaces.RebuildPreview(candidate, _activePlacementDefinition);
            _previewSharedSlotMask = _interactionSpaces.BuildPreviewSharedMask(
                candidate.InstanceId,
                _activePlacementDefinition);
            DeckPose residentPose = deckLayout.LocalToPose(
                ToNavigationPoint(_resident.State.ResidentLocalPosition.Value));
            if (!_previewReachabilityProbe.RebuildAllowingOriginRelocation(
                    _previewCommittedFacilities,
                    candidate,
                    residentPose,
                    out DeckPose resolvedOrigin))
            {
                _previewResidentOriginResolved = false;
                reachableCandidateSlotMask = 0UL;
                PublishFacilityAccessProjection(
                    previewEvaluated: true,
                    previewProbeAvailable: false,
                    previewCandidate: candidate);
                return FoundationPlacementFailure.RequiredInteractionUnreachable;
            }
            _previewResidentOriginResolved = true;
            _previewResolvedResidentOrigin = resolvedOrigin;

            FacilityAccessEvaluation candidateAccess = EvaluatePreviewFacilityAccess(
                candidate.Pose,
                _activePlacementDefinition,
                candidate);
            reachableCandidateSlotMask = candidateAccess.ReachableSlotMask;
            PublishFacilityAccessProjection(
                previewEvaluated: true,
                previewProbeAvailable: true,
                previewCandidate: candidate);

            bool existingFunctionsReachable = true;
            IReadOnlyList<FoundationFacilityAccessState> accessStates =
                _facilityAccessProjection;
            for (var i = 0; i < accessStates.Count; i++)
            {
                if (accessStates[i].PreviewAccess != FoundationFacilityAccess.Reachable)
                    existingFunctionsReachable = false;
            }

            return candidateAccess.Access == FoundationFacilityAccess.Reachable &&
                   existingFunctionsReachable
                ? FoundationPlacementFailure.None
                : FoundationPlacementFailure.RequiredInteractionUnreachable;
        }

        private FacilityAccessEvaluation EvaluatePreviewFacilityAccess(
            in DeckPose facilityPose,
            NomadFacilityDefinition definition,
            ContinuousFacilityPlacementRequest? previewCandidate)
        {
            ulong reachableMask = 0UL;
            var flattenedSlotIndex = 0;
            var reachableGroupCount = 0;
            IReadOnlyList<NomadFacilityInteractionGroupDefinition> groups =
                definition.InteractionGroups;
            for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                NomadFacilityInteractionGroupDefinition group = groups[groupIndex];
                bool groupHasReachableSlot = false;
                for (var slotIndex = 0; slotIndex < group.AlternativeSlots.Count; slotIndex++)
                {
                    DeckPose slotPose = group.AlternativeSlots[slotIndex].Resolve(facilityPose);
                    // Flood Fill 只证明同一连通区；精确站姿还必须容得下当前 Agent 胶囊。
                    // 这一步同时检查候选自身和所有既有设施，避免 45° 紧贴时“附近有绿格”
                    // 被误当成停靠点本身可站立。
                    if (IsResidentPoseClear(slotPose, previewCandidate) &&
                        _previewReachabilityProbe.IsReachable(slotPose))
                    {
                        reachableMask |= 1UL << flattenedSlotIndex;
                        groupHasReachableSlot = true;
                    }
                    flattenedSlotIndex++;
                }

                if (groupHasReachableSlot) reachableGroupCount++;
            }
            return new FacilityAccessEvaluation(
                ClassifyFacilityAccess(groups.Count, reachableGroupCount),
                reachableMask,
                flattenedSlotIndex);
        }

        private FacilityAccessEvaluation EvaluateCommittedFacilityAccess(
            Vector3 origin,
            in DeckPose facilityPose,
            NomadFacilityDefinition definition)
        {
            ulong reachableMask = 0UL;
            var flattenedSlotIndex = 0;
            var reachableGroupCount = 0;
            IReadOnlyList<NomadFacilityInteractionGroupDefinition> groups =
                definition.InteractionGroups;
            for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                NomadFacilityInteractionGroupDefinition group = groups[groupIndex];
                bool groupHasReachableSlot = false;
                for (var slotIndex = 0; slotIndex < group.AlternativeSlots.Count; slotIndex++)
                {
                    DeckPose slotPose = group.AlternativeSlots[slotIndex].Resolve(facilityPose);
                    if (TryCalculateDockingPath(origin, slotPose, out _))
                    {
                        reachableMask |= 1UL << flattenedSlotIndex;
                        groupHasReachableSlot = true;
                    }
                    flattenedSlotIndex++;
                }

                if (groupHasReachableSlot) reachableGroupCount++;
            }
            return new FacilityAccessEvaluation(
                ClassifyFacilityAccess(groups.Count, reachableGroupCount),
                reachableMask,
                flattenedSlotIndex);
        }

        private void RefreshCommittedFacilityAccess()
        {
            _committedFacilityAccess.Clear();
            bool hasOrigin = TryResolveResidentNavigationOrigin(out Vector3 origin);
            IReadOnlyList<FoundationFacilityState> facilities = _model.Facilities;
            for (var i = 0; i < facilities.Count; i++)
            {
                FoundationFacilityState state = facilities[i];
                FacilityAccessEvaluation evaluation;
                if (!_definitions.TryGetValue(
                        state.DefinitionId,
                        out NomadFacilityDefinition definition))
                {
                    evaluation = FacilityAccessEvaluation.Unknown;
                }
                else if (definition.InteractionGroups.Count == 0)
                {
                    evaluation = FacilityAccessEvaluation.NoInteractionRequired;
                }
                else
                {
                    evaluation = hasOrigin
                        ? EvaluateCommittedFacilityAccess(origin, state.Pose, definition)
                        : FacilityAccessEvaluation.Unreachable(
                            CountInteractionSlots(definition));
                }
                _committedFacilityAccess[state.InstanceId] = evaluation;
            }
            PublishFacilityAccessProjection(previewEvaluated: false);
        }

        private void PublishFacilityAccessProjection(
            bool previewEvaluated,
            bool previewProbeAvailable = false,
            ContinuousFacilityPlacementRequest? previewCandidate = null)
        {
            _facilityAccessProjection.Clear();
            IReadOnlyList<FoundationFacilityState> facilities = _model.Facilities;
            for (var i = 0; i < facilities.Count; i++)
            {
                FoundationFacilityState state = facilities[i];
                _committedFacilityAccess.TryGetValue(
                    state.InstanceId,
                    out FacilityAccessEvaluation committed);
                _definitions.TryGetValue(
                    state.DefinitionId,
                    out NomadFacilityDefinition definition);

                FacilityAccessEvaluation preview = FacilityAccessEvaluation.Unknown;
                if (previewEvaluated && definition != null)
                {
                    preview = previewProbeAvailable
                        ? EvaluatePreviewFacilityAccess(
                            state.Pose,
                            definition,
                            previewCandidate)
                        : definition.InteractionGroups.Count == 0
                            ? FacilityAccessEvaluation.NoInteractionRequired
                            : FacilityAccessEvaluation.Unreachable(
                                CountInteractionSlots(definition));
                }

                _facilityAccessProjection.Add(new FoundationFacilityAccessState(
                    state.InstanceId,
                    committed.Access,
                    committed.ReachableSlotMask,
                    _interactionSpaces?.BuildCommittedSharedMask(
                        state.InstanceId,
                        definition) ?? 0UL,
                    _interactionSpaces?.BuildCommittedOccupiedMask(
                        state.InstanceId,
                        definition) ?? 0UL,
                    committed.InteractionSlotCount,
                    previewEvaluated,
                    preview.Access,
                    preview.ReachableSlotMask,
                    previewEvaluated
                        ? _interactionSpaces?.BuildPreviewSharedMask(
                            state.InstanceId,
                            definition) ?? 0UL
                        : 0UL));
            }
            _model.ReplaceFacilityAccess(_facilityAccessProjection);
        }

        private void RebuildCommittedInteractionSpaces(bool reacquireActiveSpace)
        {
            _interactionSpaces.RebuildCommitted(
                _model.Facilities,
                _definitions,
                reacquireActiveSpace);
            foreach (var resident in _residents)
            {
                using var scope = UseResident(resident);
                if (_resident.InteractionSpace is not { IsActive: false }) continue;
                ReleaseActiveTasks();
                SetResidentPhase(FoundationResidentPhase.Idle, "交互空间发生冲突，已安全取消并重新评估");
                _resident.DecisionRetryRemaining = 0f;
            }
        }

        private bool TryResolveResidentNavigationOrigin(out Vector3 resolved)
        {
            Vector3 current = ToNavigationPoint(_resident.State.ResidentLocalPosition.Value);
            DeckPose currentPose = deckLayout.LocalToPose(current);
            if (_navigation.TrySampleLocalPosition(
                    current,
                    ResidentExactNavMeshTolerance,
                    out resolved) &&
                HorizontalDistance(current, resolved) <= ResidentExactNavMeshTolerance &&
                IsResidentPoseClear(deckLayout.LocalToPose(resolved)))
                return true;

            _placementLedger.CopyStableSnapshotTo(_previewCommittedFacilities);
            if (_previewReachabilityProbe.RebuildAllowingOriginRelocation(
                    _previewCommittedFacilities,
                    null,
                    currentPose,
                    out DeckPose fallbackPose))
            {
                Vector3 fallback = ToNavigationPoint(deckLayout.PoseToLocal(fallbackPose));
                if (_navigation.TrySampleLocalPosition(
                        fallback,
                        MaximumTravelSampleOffset,
                        out resolved) &&
                    HorizontalDistance(fallback, resolved) <= MaximumTravelSampleOffset &&
                    IsResidentPoseClear(deckLayout.LocalToPose(resolved)))
                    return true;
            }

            float relocationRadius = Mathf.Sqrt(
                deckLayout.DeckSize.x * deckLayout.DeckSize.x +
                deckLayout.DeckSize.z * deckLayout.DeckSize.z);
            return _navigation.TrySampleLocalPosition(current, relocationRadius, out resolved) &&
                   IsResidentPoseClear(deckLayout.LocalToPose(resolved));
        }

        private bool EnsureResidentHasNavigablePosition(
            bool hasPreviewResolvedOrigin,
            in DeckPose previewResolvedOrigin,
            out bool relocated)
        {
            Vector3 current = ToNavigationPoint(_resident.State.ResidentLocalPosition.Value);
            if (_navigation.TrySampleLocalPosition(
                    current,
                    ResidentExactNavMeshTolerance,
                    out Vector3 sampled) &&
                HorizontalDistance(current, sampled) <= ResidentExactNavMeshTolerance &&
                IsResidentPoseClear(deckLayout.LocalToPose(sampled)))
            {
                relocated = false;
                return true;
            }

            if (hasPreviewResolvedOrigin &&
                TryResolveSafeResidentPosition(previewResolvedOrigin, out sampled))
            {
                ApplyResidentRelocation(sampled);
                relocated = true;
                return true;
            }

            _placementLedger.CopyStableSnapshotTo(_previewCommittedFacilities);
            DeckPose currentPose = deckLayout.LocalToPose(current);
            if (_previewReachabilityProbe.RebuildAllowingOriginRelocation(
                    _previewCommittedFacilities,
                    null,
                    currentPose,
                    out DeckPose fallbackPose) &&
                TryResolveSafeResidentPosition(fallbackPose, out sampled))
            {
                ApplyResidentRelocation(sampled);
                relocated = true;
                return true;
            }

            float relocationRadius = Mathf.Sqrt(
                deckLayout.DeckSize.x * deckLayout.DeckSize.x +
                deckLayout.DeckSize.z * deckLayout.DeckSize.z);
            if (!_navigation.TrySampleLocalPosition(current, relocationRadius, out sampled) ||
                !IsResidentPoseClear(deckLayout.LocalToPose(sampled)))
            {
                relocated = false;
                return false;
            }

            ApplyResidentRelocation(sampled);
            relocated = true;
            return true;
        }

        private bool TryResolveSafeResidentPosition(
            in DeckPose preferredPose,
            out Vector3 sampled)
        {
            Vector3 preferred = ToNavigationPoint(deckLayout.PoseToLocal(preferredPose));
            return _navigation.TrySampleLocalPosition(
                       preferred,
                       MaximumTravelSampleOffset,
                       out sampled) &&
                   HorizontalDistance(preferred, sampled) <= MaximumTravelSampleOffset &&
                   IsResidentPoseClear(deckLayout.LocalToPose(sampled));
        }

        private bool IsResidentPoseClear(
            in DeckPose pose,
            ContinuousFacilityPlacementRequest? additionalPlacement = null)
        {
            DeckBounds bounds = deckLayout.CreateBounds();
            if (pose.DeckLevel != bounds.DeckLevel ||
                pose.XMillimeters < bounds.MinXMillimeters + _residentClearanceMillimeters ||
                pose.XMillimeters > bounds.MaxXMillimeters - _residentClearanceMillimeters ||
                pose.ZMillimeters < bounds.MinZMillimeters + _residentClearanceMillimeters ||
                pose.ZMillimeters > bounds.MaxZMillimeters - _residentClearanceMillimeters)
                return false;

            IReadOnlyList<FoundationFacilityState> facilities = _model.Facilities;
            for (var i = 0; i < facilities.Count; i++)
            {
                FoundationFacilityState facility = facilities[i];
                if (_definitions.TryGetValue(
                        facility.DefinitionId,
                        out NomadFacilityDefinition definition) &&
                    definition.CreateFootprint().ContainsPoint(
                        facility.Pose,
                        pose,
                        _residentClearanceMillimeters))
                    return false;
            }

            if (additionalPlacement.HasValue)
            {
                ContinuousFacilityPlacementRequest candidate = additionalPlacement.Value;
                if (candidate.Pose.DeckLevel == pose.DeckLevel &&
                    candidate.Footprint != null &&
                    candidate.Footprint.ContainsPoint(
                        candidate.Pose,
                        pose,
                        _residentClearanceMillimeters))
                    return false;
            }
            return true;
        }

        private void ApplyResidentRelocation(Vector3 sampled)
        {
            ClearActivePath();
            _resident.State.ResidentLocalPosition.Value = ToNavigationPoint(sampled);
        }

        private static FoundationFacilityAccess ClassifyFacilityAccess(
            int groupCount,
            int reachableGroupCount)
        {
            if (groupCount <= 0 || reachableGroupCount >= groupCount)
                return FoundationFacilityAccess.Reachable;
            return reachableGroupCount <= 0
                ? FoundationFacilityAccess.Unreachable
                : FoundationFacilityAccess.PartiallyReachable;
        }

        private void AdvanceBuildTransaction()
        {
            if (_navigationUpdate == null || !_navigationUpdate.isDone) return;

            switch (_model.BuildTransactionPhase.Value)
            {
                case FoundationBuildTransactionPhase.UpdatingCandidateNavigation:
                    if (_cancelPlacementRequested || _resetRequested)
                    {
                        BeginRollback(FoundationPlacementFailure.None);
                        return;
                    }

                    NomadFacilityDefinition pendingDefinition;
                    bool residentHasPosition;
                    bool residentRelocated;
                    try
                    {
                        if (!_definitions.TryGetValue(
                                _pendingPlacement.DefinitionId,
                                out pendingDefinition))
                        {
                            BeginRollback(FoundationPlacementFailure.InvalidRequest);
                            return;
                        }
                        if (_navigationObstacles.ContainsKey(_pendingPlacement.InstanceId) ||
                            _model.ContainsFacility(_pendingPlacement.InstanceId))
                        {
                            BeginRollback(FoundationPlacementFailure.DuplicateInstanceId);
                            return;
                        }
                        if (!_placementLedger.TryPlace(
                                _pendingPlacement,
                                out _,
                                out ContinuousPlacementFailure placementFailure))
                        {
                            BeginRollback(MapPlacementFailure(placementFailure));
                            return;
                        }

                        CommitPendingPlacementTruth();
                        RebuildCommittedInteractionSpaces(reacquireActiveSpace: true);
                        residentHasPosition = true;
                        residentRelocated = false;
                        for (var index = 0; index < _residents.Count; index++)
                        {
                            using var scope = UseResident(_residents[index]);
                            residentHasPosition &= EnsureResidentHasNavigablePosition(
                                index == 0 && _pendingResidentOriginResolved,
                                _pendingResolvedResidentOrigin, out bool relocated);
                            residentRelocated |= relocated;
                        }
                        RefreshCommittedFacilityAccess();
                    }
                    catch (Exception exception)
                    {
                        RollbackPartiallyCommittedPlacementTruth();
                        _model.BuildFeedback.Value = exception.Message;
                        BeginRollback(FoundationPlacementFailure.NavigationUpdateFailed);
                        return;
                    }
                    FinishSuccessfulPlacement(
                        pendingDefinition,
                        residentHasPosition,
                        residentRelocated);
                    break;
                case FoundationBuildTransactionPhase.RollingBackNavigation:
                    FinishRollback();
                    break;
            }
        }

        private void CommitPendingPlacementTruth()
        {
            _navigationObstacles.Add(_pendingPlacement.InstanceId, _pendingObstacle);
            var facility = new FoundationFacilityState(
                _pendingPlacement.InstanceId,
                _pendingPlacement.DefinitionId,
                _pendingPlacement.Pose);
            RegisterFacilityPlacementRegions(facility);
            _model.AddFacility(facility);
            _facilityConditions.Add(
                _pendingPlacement.InstanceId,
                CreateFacilityCondition(_pendingPlacement.InstanceId));
            AddFacilityInventory(facility);
            TryCreateStarterCupForFacility(facility);
        }

        private void RollbackPartiallyCommittedPlacementTruth()
        {
            string instanceId = _pendingPlacement.InstanceId;
            if (string.IsNullOrWhiteSpace(instanceId)) return;
            _placementLedger.Remove(instanceId);
            RemovePlacedItemsOwnedByFacility(instanceId);
            RemoveFacilityPlacementRegions(instanceId, _pendingPlacement.DefinitionId);
            _navigationObstacles.Remove(instanceId);
            _model.RemoveFacility(instanceId);
            _facilityConditions.Remove(instanceId);
            RemoveFacilityInventory(instanceId);
            _committedFacilityAccess.Remove(instanceId);
            RebuildCommittedInteractionSpaces(reacquireActiveSpace: true);
        }

        private void FinishSuccessfulPlacement(
            NomadFacilityDefinition definition,
            bool residentHasPosition,
            bool residentRelocated)
        {
            _nextFacilitySequence++;
            string placedName = definition.DisplayName;
            ClearPendingPlacement();
            _model.BuildTransactionPhase.Value = FoundationBuildTransactionPhase.Idle;
            ClearPlacementSelection(exitBuildMode: false);
            if (!residentHasPosition)
            {
                Block(
                    "设施已落地，但甲板已没有可供居民站立的导航区域",
                    ResourceFlowBlocker.None);
                return;
            }
            foreach (var resident in _residents)
            {
                using var scope = UseResident(resident);
                ReplanActiveMoveAfterPlacement();
            }
            RebindNativeLocomotion();
            _model.BuildFeedback.Value = residentRelocated
                ? $"已建造：{placedName}；居民已自动避让施工占地"
                : $"已建造：{placedName}";
        }

        private void BeginRollback(FoundationPlacementFailure failure)
        {
            _rollbackFailure = failure;
            _navigation.DeactivateAndDestroyObstacle(_pendingObstacle);
            try
            {
                _navigationUpdate = _navigation.BeginUpdate();
                _model.BuildTransactionPhase.Value =
                    FoundationBuildTransactionPhase.RollingBackNavigation;
                _model.BuildFeedback.Value = "候选无效，正在恢复提交前的导航数据";
            }
            catch (Exception exception)
            {
                _model.BuildFeedback.Value = exception.Message;
                _navigation.BuildNow();
                FinishRollback();
            }
        }

        private void FinishRollback()
        {
            FoundationPlacementFailure failure = _rollbackFailure;
            bool cancel = _cancelPlacementRequested;
            bool reset = _resetRequested;
            bool exitBuildMode = _exitBuildModeRequested;
            ClearPendingPlacement();
            _model.BuildTransactionPhase.Value = FoundationBuildTransactionPhase.Idle;
            _rollbackFailure = FoundationPlacementFailure.None;
            _cancelPlacementRequested = false;
            _resetRequested = false;
            _exitBuildModeRequested = false;

            if (reset)
            {
                ResetScenarioNow();
                return;
            }
            RebindNativeLocomotion();
            RefreshCommittedFacilityAccess();
            if (cancel)
            {
                ClearPlacementSelection(exitBuildMode);
                return;
            }

            SetPreview(failure);
            _model.BuildFeedback.Value = failure == FoundationPlacementFailure.None
                ? "候选设施已取消"
                : "候选设施未通过连续导航验证";
        }

        private bool AreAllRequiredInteractionsReachable(
            ContinuousFacilityPlacementRequest? additionalPlacement,
            NomadFacilityDefinition additionalDefinition)
        {
            Vector3 anchor = ToNavigationPoint(residentStartLocalPosition);
            foreach (var execution in _residents)
                if (!TryCalculatePath(anchor, ToNavigationPoint(execution.State.ResidentLocalPosition.Value),
                        MaximumTravelSampleOffset, _dockingNavMeshTolerance, out _)) return false;

            IReadOnlyList<FoundationFacilityState> facilities = _model.Facilities;
            for (var i = 0; i < facilities.Count; i++)
            {
                FoundationFacilityState state = facilities[i];
                if (!_definitions.TryGetValue(state.DefinitionId, out NomadFacilityDefinition definition) ||
                    !AreRequiredGroupsReachable(anchor, state.Pose, definition))
                    return false;
            }

            return !additionalPlacement.HasValue || additionalDefinition != null &&
                AreRequiredGroupsReachable(
                    anchor,
                    additionalPlacement.Value.Pose,
                    additionalDefinition);
        }

        private bool AreRequiredGroupsReachable(
            Vector3 anchor,
            in DeckPose facilityPose,
            NomadFacilityDefinition definition)
        {
            IReadOnlyList<NomadFacilityInteractionGroupDefinition> groups =
                definition.InteractionGroups;
            for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                NomadFacilityInteractionGroupDefinition group = groups[groupIndex];
                if (!group.RequiredForOperation) continue;

                bool foundReachableSlot = false;
                for (var slotIndex = 0; slotIndex < group.AlternativeSlots.Count; slotIndex++)
                {
                    DeckPose slotPose = group.AlternativeSlots[slotIndex].Resolve(facilityPose);
                    if (!TryCalculateDockingPath(anchor, slotPose, out _)) continue;
                    foundReachableSlot = true;
                    break;
                }

                if (!foundReachableSlot) return false;
            }
            return true;
        }

        private bool TryCalculateDockingPath(
            Vector3 localStart,
            in DeckPose exactDockingPose,
            out DeckNavPathProbe probe)
        {
            if (!IsResidentPoseClear(exactDockingPose))
            {
                probe = default;
                return false;
            }

            Vector3 localEnd = ToNavigationPoint(deckLayout.PoseToLocal(exactDockingPose));
            return TryCalculatePath(
                localStart,
                localEnd,
                MaximumTravelSampleOffset,
                _dockingNavMeshTolerance,
                out probe);
        }

        private bool TryCalculateTravelPath(
            Vector3 localStart,
            Vector3 localEnd,
            out DeckNavPathProbe probe)
            => TryCalculatePath(
                localStart,
                localEnd,
                MaximumTravelSampleOffset,
                MaximumTravelSampleOffset,
                out probe);

        private bool TryCalculatePath(
            Vector3 localStart,
            Vector3 localEnd,
            float maximumStartOffset,
            float maximumEndOffset,
            out DeckNavPathProbe probe)
        {
            if (!_navigation.TryCalculateCompleteLocalPath(localStart, localEnd, out probe))
                return false;
            return HorizontalDistance(probe.SampledStart, localStart) <= maximumStartOffset &&
                   HorizontalDistance(probe.SampledEnd, localEnd) <= maximumEndOffset;
        }

        private void SetPreview(FoundationPlacementFailure failure)
        {
            FoundationPlacementPreviewState previous = _model.PlacementPreview.Value;
            bool canReuseProbe = previous.Active &&
                string.Equals(
                    previous.DefinitionId,
                    _activePlacementDefinition.Id,
                    StringComparison.Ordinal) &&
                previous.Pose == _previewPose;
            SetPreview(
                failure,
                canReuseProbe ? GetReachableSlotMask(previous) : 0UL,
                canReuseProbe ? GetSharedSlotMask(previous) : _previewSharedSlotMask,
                canReuseProbe
                    ? previous.InteractionSlotCount
                    : CountInteractionSlots(_activePlacementDefinition),
                canReuseProbe && previous.RealtimeReachabilityEvaluated,
                canReuseProbe ? previous.RealtimeProbeMilliseconds : 0f,
                canReuseProbe ? previous.RealtimeVisitedCells : 0,
                canReuseProbe
                    ? previous.RealtimeProbeCellCount
                    : _previewReachabilityProbe?.CellCount ?? 0);
        }

        private void SetPreview(
            FoundationPlacementFailure failure,
            ulong reachableSlotMask,
            ulong sharedSpaceSlotMask,
            int interactionSlotCount,
            bool realtimeReachabilityEvaluated,
            float realtimeProbeMilliseconds,
            int realtimeVisitedCells,
            int realtimeProbeCellCount)
        {
            _model.PlacementPreview.Value = new FoundationPlacementPreviewState(
                true,
                _activePlacementDefinition.Id,
                _previewPose,
                failure,
                reachableSlotMask,
                sharedSpaceSlotMask,
                interactionSlotCount,
                realtimeReachabilityEvaluated,
                realtimeProbeMilliseconds,
                realtimeVisitedCells,
                realtimeProbeCellCount);
        }

        private static ulong GetReachableSlotMask(in FoundationPlacementPreviewState preview)
        {
            ulong result = 0UL;
            for (var i = 0; i < preview.InteractionSlotCount; i++)
            {
                if (preview.IsInteractionSlotReachable(i)) result |= 1UL << i;
            }
            return result;
        }

        private static ulong GetSharedSlotMask(in FoundationPlacementPreviewState preview)
        {
            ulong result = 0UL;
            for (var i = 0; i < preview.InteractionSlotCount; i++)
            {
                if (preview.IsInteractionSlotSharingSpace(i)) result |= 1UL << i;
            }
            return result;
        }

        private static int CountInteractionSlots(NomadFacilityDefinition definition)
        {
            var result = 0;
            IReadOnlyList<NomadFacilityInteractionGroupDefinition> groups =
                definition.InteractionGroups;
            for (var i = 0; i < groups.Count; i++)
                result += groups[i].AlternativeSlots.Count;
            return result;
        }

        private ContinuousFacilityPlacementRequest CreatePreviewRequest() => new(
            $"facility-{_nextFacilitySequence:D4}",
            _activePlacementDefinition.Id,
            _previewPose,
            _activePlacementDefinition.CreateFootprint(),
            _activePlacementDefinition.CreateFunctionalClearanceFootprint());

        private void TryStartResidentRoutine()
        {
            var options = new List<FoundationResidentDecisionOption>(8);
            var pendingDiagnostic = new PendingDecisionDiagnostic();
            AddPrimaryWaterTankRepairDecisionOption(options, ref pendingDiagnostic);
            AddToiletDecisionOption(options, ref pendingDiagnostic);
            AddWaterDecisionOptions(options, ref pendingDiagnostic);
            AddDrivingDecisionOption(options);
            AddStopWaterDecisionOption(options);
            AddStopWasteDecisionOption(options);
            AddStopSpareDecisionOption(options);
            AddHobbyDecisionOption(options);
            AddGroundRestDecisionOption(options);
            AddLeisureDecisionOptions(options);

            var candidates = new ResidentActionCandidate[options.Count];
            for (var i = 0; i < options.Count; i++)
                candidates[i] = options[i].Evaluation.Candidate;
            var context = new ResidentDecisionContext(
                worldSeed: worldSeed,
                residentId: _resident.OwnerId,
                decisionSequence: _resident.DecisionSequence++,
                needs: CreateResidentNeedSnapshot(),
                candidates: candidates);
            ResidentDecisionResult decision = _decisionEngine.Decide(
                context,
                _residentDecisionPolicy);
            FoundationResidentDecisionOption selected = FindSelectedOption(
                options,
                decision.Selected);

            PublishPendingDecisionDiagnostic(pendingDiagnostic);

            if (selected == null)
            {
                SetResidentPhase(
                    FoundationResidentPhase.Idle,
                    "当前没有净效用足够且可执行的行动，稍后重新评估");
                return;
            }

            WriteLatestActionPlan(selected.Evaluation, decision);
            BeginSelectedResidentOption(selected);
        }

        private ResidentNeedState[] CreateResidentNeedSnapshot()
        {
            ResidentNeedState[] wellbeing = _resident.Wellbeing.CreateDecisionNeedSnapshot();
            var result = new List<ResidentNeedState>(2 + wellbeing.Length)
            {
                new ResidentNeedState(
                    ResidentNeed.Thirst,
                    _resident.WaterCycle.Thirst,
                    thirstIncreasePerSecond),
                new ResidentNeedState(
                    ResidentNeed.Bladder,
                    _resident.WaterCycle.ExcretionPressure,
                    0f,
                    pressureCurve: BladderPressureCurve),
            };
            result.AddRange(wellbeing);
            return result.ToArray();
        }

        private void AddToiletDecisionOption(
            ICollection<FoundationResidentDecisionOption> options,
            ref PendingDecisionDiagnostic pendingDiagnostic)
        {
            int waste = _resident.WaterCycle.Bladder.GetAmount(NomadResourceIds.HumanWaste);
            float pressure = _resident.WaterCycle.ExcretionPressure;
            if (waste <= 0 || !ShouldOfferBladderAction(pressure)) return;

            bool urgent = BladderPressureCurve.IsUrgent(pressure);
            ResidentDecisionRiskTier tier = urgent
                ? ResidentDecisionRiskTier.Urgent
                : ResidentDecisionRiskTier.Routine;
            if (!TrySelectUsableToilet(
                    waste,
                    out FoundationFacilityState toilet,
                    out float pathLength,
                    out bool hasReachableToilet))
            {
                options.Add(CreateBlockedNeedOption(
                    FoundationResidentDecisionKind.Toilet,
                    $"toilet:{_resident.StableId}:{_resident.ActionSequence + 1}:unavailable",
                    "use-toilet",
                    "寻找可达旱厕",
                    hasReachableToilet ? ResidentActionPlanBlockReason.DestinationFull :
                        ResidentActionPlanBlockReason.InteractionUnavailable,
                    hasReachableToilet ? "可达旱厕容量不足，需要清运或使用另一座" : "没有可达旱厕",
                    new NeedEffect(ResidentNeed.Bladder, 1f),
                    tier,
                    urgent ? pressure : 0f));
                if (urgent)
                    pendingDiagnostic.Consider(
                        hasReachableToilet ? "DestinationFull · 可达旱厕容量不足，需要清运或建造另一座" :
                            "PhysiologyBackpressure · 膀胱接近满载，需要建造可达旱厕",
                        tier,
                        pressure);
                return;
            }

            var proposal = new ResidentActionPlanProposal(
                $"toilet:{_resident.StableId}:{_resident.ActionSequence + 1}",
                "use-toilet",
                "前往旱厕并排空膀胱")
            {
                Steps = new[]
                {
                    CreateTravelStep(pathLength, "前往旱厕"),
                    new ResidentActionStepEstimate(
                        ResidentActionStepKind.UseFacility,
                        toiletSeconds,
                        label: "使用旱厕"),
                },
                BaseUtility = 0.035f,
                DelayUrgency = pressure,
                RiskTier = tier,
                RiskPriority = urgent ? pressure : 0f,
                NeedEffects = new[] { new NeedEffect(ResidentNeed.Bladder, 1f) },
                ReservationKeys = new[] { $"facility:{toilet.InstanceId}:toilet-use" },
            };
            ResidentActionPlanEvaluation evaluation = _actionPlanEvaluator.Evaluate(
                proposal,
                CreateResidentDecisionCondition(),
                _actionPlanPolicy);
            options.Add(new FoundationResidentDecisionOption(
                FoundationResidentDecisionKind.Toilet,
                evaluation)
            {
                TargetFacility = toilet,
            });
        }

        private bool ShouldOfferBladderAction(float excretionPressure)
        {
            float probability = BladderPressureCurve.EvaluateOpportunityProbability(
                excretionPressure);
            if (probability <= 0f) return false;

            double roll = DeterministicRandom.Sample01(
                worldSeed: worldSeed,
                ownerId: _resident.OwnerId,
                streamId: BladderOpportunityRandomStreamId,
                eventSequence: _resident.BladderOpportunitySequence++);
            return BladderPressureCurve.ShouldOfferAction(excretionPressure, roll);
        }

        private void AddWaterDecisionOptions(
            ICollection<FoundationResidentDecisionOption> options,
            ref PendingDecisionDiagnostic pendingDiagnostic)
        {
            bool needsDrink = _resident.WaterCycle.Thirst >= DrinkNeedThreshold;
            bool bodyCanDrink = _resident.WaterCycle.BodyWater.FreeCapacity >=
                                ResidentWaterCycle.DefaultDrinkServingMilliliters;
            bool hasFeasibleRecovery = false;
            if (needsDrink && !bodyCanDrink)
            {
                bool bladderFull = _resident.WaterCycle.Bladder.FreeCapacity <= 0;
                bool urgentThirst =
                    _resident.WaterCycle.Thirst >= _residentDecisionPolicy.UrgentNeedDeficit;
                pendingDiagnostic.Consider(
                    bladderFull
                    ? $"DestinationFull · {_resident.State.BodyWaterInventoryId} · 膀胱已满，需要可达旱厕"
                    : $"DestinationFull · {_resident.State.BodyWaterInventoryId} · 正在等待体内水继续代谢",
                    urgentThirst
                        ? ResidentDecisionRiskTier.Urgent
                        : ResidentDecisionRiskTier.Routine,
                    urgentThirst ? _resident.WaterCycle.Thirst : 0f);
            }

            if (needsDrink && bodyCanDrink && TrySelectDrinkingStation(
                    requireDrinkServing: true,
                    requireRestock: false,
                    out FoundationFacilityState stockedStation,
                    out _,
                    out float stockedPathLength))
            {
                options.Add(CreateDirectDrinkOption(stockedStation, stockedPathLength));
                hasFeasibleRecovery = true;
            }

            bool hasRestockStation = TrySelectDrinkingStation(
                    requireDrinkServing: false,
                    requireRestock: true,
                    out FoundationFacilityState station,
                    out ResourceInventory stationInventory,
                    out _);
            bool hasAnyWaterSource = TryFindPlacedFacility(
                    NomadFacilityFunction.VehicleWaterTank,
                    out _,
                    out _);
            bool hasWaterSource = TryFindOperationalWaterSource(
                out FoundationFacilityState source);
            bool sourceHasWater = _vehicleWater.GetAmount(NomadResourceIds.Water) > 0;
            bool drinkAfterDelivery = needsDrink && bodyCanDrink && !hasFeasibleRecovery;
            if (hasRestockStation && hasWaterSource && sourceHasWater)
            {
                int haulMilliliters = CalculateWaterHaulMilliliters(
                    stationInventory,
                    drinkAfterDelivery);
                if (haulMilliliters > 0)
                {
                    ResidentActionPlanEvaluation evaluation = EvaluateWaterRestockPlan(
                        source,
                        station,
                        stationInventory,
                        drinkAfterDelivery,
                        haulMilliliters,
                        out FoundationFacilityState waterCanFacility,
                        out bool waterCanAtSource);
                    options.Add(new FoundationResidentDecisionOption(
                        FoundationResidentDecisionKind.WaterRestock,
                        evaluation)
                    {
                        SourceFacility = source,
                        TargetFacility = station,
                        TargetInventory = stationInventory,
                        TransferMilliliters = haulMilliliters,
                        DrinkAfterDelivery = drinkAfterDelivery,
                        WaterCanFacility = waterCanFacility,
                        WaterCanAtSource = waterCanAtSource,
                    });
                    if (drinkAfterDelivery && evaluation.Feasibility.IsFeasible)
                        hasFeasibleRecovery = true;
                    else if (drinkAfterDelivery)
                        pendingDiagnostic.Consider(
                            evaluation.Candidate.BlockReason,
                            evaluation.Candidate.RiskTier,
                            evaluation.Candidate.RiskPriority);
                }
            }

            if (!needsDrink || hasFeasibleRecovery) return;

            string reason = !bodyCanDrink
                ? "体内待代谢水已满，需要等待代谢或如厕"
                : !hasRestockStation
                    ? "没有可补水的可达饮水站实例"
                    : !hasAnyWaterSource
                        ? "找不到车辆水箱设施"
                        : !hasWaterSource
                            ? "车辆水箱出水阀卡滞，需要先修理"
                        : !sourceHasWater
                            ? "车辆水箱已经没有可饮用水"
                            : "当前没有可装入水罐并送达饮水站的水量";
            options.Add(CreateBlockedNeedOption(
                FoundationResidentDecisionKind.DirectDrink,
                $"drink:{_resident.StableId}:{_resident.ActionSequence + 1}:unavailable",
                "drink-water",
                "寻找可执行饮水方案",
                ResidentActionPlanBlockReason.PrerequisiteUnavailable,
                reason,
                new NeedEffect(ResidentNeed.Thirst, 0.72f),
                _resident.WaterCycle.Thirst >= _residentDecisionPolicy.UrgentNeedDeficit
                    ? ResidentDecisionRiskTier.Urgent
                    : ResidentDecisionRiskTier.Routine,
                _resident.WaterCycle.Thirst >= _residentDecisionPolicy.UrgentNeedDeficit
                    ? _resident.WaterCycle.Thirst
                    : 0f));
            bool urgent =
                _resident.WaterCycle.Thirst >= _residentDecisionPolicy.UrgentNeedDeficit;
            pendingDiagnostic.Consider(
                $"RouteUnavailable · {reason}",
                urgent
                    ? ResidentDecisionRiskTier.Urgent
                    : ResidentDecisionRiskTier.Routine,
                urgent ? _resident.WaterCycle.Thirst : 0f);
        }

        private int CalculateWaterHaulMilliliters(
            ResourceInventory stationInventory,
            bool drinkAfterDelivery)
        {
            int desiredMilliliters = drinkAfterDelivery
                ? WaterHaulBatchMilliliters
                : Math.Max(
                    0,
                    DrinkingStationRestockTargetMilliliters -
                    stationInventory.GetAmount(NomadResourceIds.Water));
            return Math.Min(
                desiredMilliliters,
                Math.Min(
                    _vehicleWater.GetAmount(NomadResourceIds.Water),
                    Math.Min(_waterCan.FreeCapacity, stationInventory.FreeCapacity)));
        }

        private FoundationResidentDecisionOption CreateDirectDrinkOption(
            in FoundationFacilityState station,
            float pathLength)
        {
            bool urgent = _resident.WaterCycle.Thirst >= _residentDecisionPolicy.UrgentNeedDeficit;
            var proposal = new ResidentActionPlanProposal(
                $"drink:{_resident.StableId}:{_resident.ActionSequence + 1}",
                "drink-water",
                "前往饮水站直接饮水")
            {
                Steps = new[]
                {
                    CreateTravelStep(pathLength, "前往有水的饮水站"),
                    new ResidentActionStepEstimate(
                        ResidentActionStepKind.Consume,
                        drinkingSeconds,
                        label: "在饮水站饮水"),
                },
                BaseUtility = 0.035f,
                DelayUrgency = _resident.WaterCycle.Thirst,
                RiskTier = urgent
                    ? ResidentDecisionRiskTier.Urgent
                    : ResidentDecisionRiskTier.Routine,
                RiskPriority = urgent ? _resident.WaterCycle.Thirst : 0f,
                NeedEffects = new[] { new NeedEffect(ResidentNeed.Thirst, 0.72f) },
                ReservationKeys = new[] { $"facility:{station.InstanceId}:drink" },
            };
            return new FoundationResidentDecisionOption(
                FoundationResidentDecisionKind.DirectDrink,
                _actionPlanEvaluator.Evaluate(
                    proposal,
                    CreateResidentDecisionCondition(),
                    _actionPlanPolicy))
            {
                TargetFacility = station,
            };
        }

        private void AddLeisureDecisionOptions(
            ICollection<FoundationResidentDecisionOption> options)
        {
            ResidentDecisionCondition condition = CreateResidentDecisionCondition();
            bool hasWanderTarget = TrySelectWanderTarget(
                out Vector3 wanderTarget,
                out DeckNavPathProbe wanderPath,
                out string wanderLabel);
            if (hasWanderTarget)
            {
                options.Add(new FoundationResidentDecisionOption(
                    FoundationResidentDecisionKind.Wander,
                    _actionPlanEvaluator.Evaluate(
                        ResidentLeisurePlanFactory.CreateWander(
                            wanderPath.PathLength,
                            Mathf.Max(0.1f, residentMoveSpeed),
                            leisureSeconds,
                            targetLabel: wanderLabel),
                        condition,
                        _actionPlanPolicy))
                {
                    WanderTarget = wanderTarget,
                    WanderLabel = wanderLabel,
                });
            }
            options.Add(new FoundationResidentDecisionOption(
                FoundationResidentDecisionKind.Daydream,
                _actionPlanEvaluator.Evaluate(
                    ResidentLeisurePlanFactory.CreateDaydream(leisureSeconds),
                    condition,
                    _actionPlanPolicy)));
        }

        private void AddGroundRestDecisionOption(
            ICollection<FoundationResidentDecisionOption> options)
        {
            ResidentActionPlanProposal proposal =
                ResidentLeisurePlanFactory.CreateGroundRest(groundRestSeconds);
            options.Add(new FoundationResidentDecisionOption(
                FoundationResidentDecisionKind.GroundRest,
                _actionPlanEvaluator.Evaluate(
                    proposal,
                    CreateResidentDecisionCondition(),
                    _actionPlanPolicy)));
        }

        private void AddHobbyDecisionOption(
            ICollection<FoundationResidentDecisionOption> options)
        {
            if (!TrySelectReachableFacility(
                    NomadFacilityFunction.HobbyPoint,
                    out FoundationFacilityState facility,
                    out float pathLength,
                    HobbyInteractionGroupId) ||
                !_definitions.TryGetValue(
                    facility.DefinitionId,
                    out NomadFacilityDefinition definition))
                return;

            ResidentActionPlanProposal proposal =
                ResidentLeisurePlanFactory.CreateHobbyAtFacility(
                    facility.InstanceId,
                    definition.DisplayName,
                    pathLength,
                    Mathf.Max(0.1f, residentMoveSpeed),
                    hobbySeconds,
                    residentPaintingAffinity);
            options.Add(new FoundationResidentDecisionOption(
                FoundationResidentDecisionKind.Hobby,
                _actionPlanEvaluator.Evaluate(
                    proposal,
                    CreateResidentDecisionCondition(),
                    _actionPlanPolicy))
            {
                TargetFacility = facility,
            });
        }

        private FoundationResidentDecisionOption CreateBlockedNeedOption(
            FoundationResidentDecisionKind kind,
            string id,
            string intentId,
            string displayName,
            ResidentActionPlanBlockReason blockReason,
            string detail,
            NeedEffect needEffect,
            ResidentDecisionRiskTier riskTier,
            float riskPriority)
        {
            var proposal = new ResidentActionPlanProposal(id, intentId, displayName)
            {
                Feasibility = ResidentActionPlanFeasibility.Blocked(blockReason, detail),
                BaseUtility = 0.01f,
                RiskTier = riskTier,
                RiskPriority = riskPriority,
                NeedEffects = new[] { needEffect },
            };
            return new FoundationResidentDecisionOption(
                kind,
                _actionPlanEvaluator.Evaluate(
                    proposal,
                    CreateResidentDecisionCondition(),
                    _actionPlanPolicy));
        }

        private ResidentDecisionCondition CreateResidentDecisionCondition() => new(
            motionSickness: 0f,
            timeSensitivity: 1f,
            effortAversion: ResidentPerformance.CalculateEffortAversion(
                _resident.Wellbeing.Health,
                _resident.Wellbeing.Fatigue),
            riskAversion: 1f);

        private ResidentActionStepEstimate CreateTravelStep(
            float distanceMeters,
            string label) => new(
            ResidentActionStepKind.Travel,
            distanceMeters / Mathf.Max(0.1f, residentMoveSpeed),
            distanceMeters,
            label);

        private void PublishPendingDecisionDiagnostic(
            in PendingDecisionDiagnostic pendingDiagnostic)
        {
            if (pendingDiagnostic.HasValue)
            {
                _resident.LastPublishedDecisionDiagnostic = pendingDiagnostic.Message;
                _resident.State.LastBlocker.Value = pendingDiagnostic.Message;
                return;
            }

            bool ownsVisibleDiagnostic = string.Equals(
                _resident.State.LastBlocker.Value,
                _resident.LastPublishedDecisionDiagnostic,
                StringComparison.Ordinal);
            bool isPhysiologyBackpressure = _resident.State.LastBlocker.Value.StartsWith(
                "PhysiologyBackpressure",
                StringComparison.Ordinal);
            if (ownsVisibleDiagnostic || isPhysiologyBackpressure)
                _resident.State.LastBlocker.Value = string.Empty;
            _resident.LastPublishedDecisionDiagnostic = string.Empty;
        }

        private static FoundationResidentDecisionOption FindSelectedOption(
            IReadOnlyList<FoundationResidentDecisionOption> options,
            ResidentActionCandidate selected)
        {
            if (selected == null) return null;
            for (var i = 0; i < options.Count; i++)
            {
                if (ReferenceEquals(options[i].Evaluation.Candidate, selected))
                    return options[i];
            }
            return null;
        }

        private void BeginSelectedResidentOption(FoundationResidentDecisionOption option)
        {
            switch (option.Kind)
            {
                case FoundationResidentDecisionKind.Toilet:
                    BeginToiletUse(option.TargetFacility);
                    return;
                case FoundationResidentDecisionKind.DirectDrink:
                    _resident.ActionSequence++;
                    _resident.State.LastBlocker.Value = string.Empty;
                    BeginDrink(option.TargetFacility);
                    return;
                case FoundationResidentDecisionKind.WaterRestock:
                    BeginWaterRestock(option);
                    return;
                case FoundationResidentDecisionKind.Wander:
                case FoundationResidentDecisionKind.Daydream:
                    BeginLeisure(option);
                    return;
                case FoundationResidentDecisionKind.GroundRest:
                    BeginGroundRest();
                    return;
                case FoundationResidentDecisionKind.Hobby:
                    BeginHobby(option);
                    return;
                case FoundationResidentDecisionKind.RepairWaterTank:
                    BeginPrimaryWaterTankRepair(option);
                    return;
                case FoundationResidentDecisionKind.Drive:
                    BeginDrivingApproach(option);
                    return;
                case FoundationResidentDecisionKind.StopWater:
                    BeginStopWater(option);
                    return;
                case FoundationResidentDecisionKind.StopWaste:
                    BeginStopWaste(option);
                    return;
                case FoundationResidentDecisionKind.StopSpare:
                    BeginStopSpare(option);
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(option.Kind), option.Kind, null);
            }
        }

        private void BeginToiletUse(in FoundationFacilityState toilet)
        {
            int waste = _resident.WaterCycle.Bladder.GetAmount(NomadResourceIds.HumanWaste);
            if (waste <= 0)
            {
                SetResidentPhase(
                    FoundationResidentPhase.Idle,
                    "如厕方案开始前膀胱已经排空，重新评估");
                return;
            }

            _resident.ActionSequence++;
            if (!_resident.WaterCycle.TryReserveToiletUse(
                    _resourceFlow,
                    _toiletInventories[toilet.InstanceId],
                    $"toilet:{_resident.StableId}:{_resident.ActionSequence}",
                    $"facility:{toilet.InstanceId}:toilet-use",
                    waste,
                    out _resident.ActiveWaterAction,
                    out ResourceFlowBlocker blocker))
            {
                if (blocker.Reason == ResourceFlowBlockReason.DestinationFull)
                {
                    _resident.State.LastBlocker.Value =
                        $"{blocker.Reason} · {blocker.InventoryId} · 旱厕暂存桶需要清运";
                    SetResidentPhase(
                        FoundationResidentPhase.Idle,
                        "如厕方案在预留时失效，稍后重新决策");
                }
                else
                {
                    Block("无法开始如厕", blocker);
                }
                return;
            }

            _resident.State.LastBlocker.Value = string.Empty;
            TryBeginMove(
                FoundationResidentPhase.MovingToToilet,
                "统一 Utility 已选择如厕：前往可达旱厕",
                toilet,
                interactionGroupId: ToiletInteractionGroupId);
        }

        private void BeginWaterRestock(FoundationResidentDecisionOption option)
        {
            _resident.ActionSequence++;
            var request = new HaulTaskRequest(
                $"water-haul:{_resident.StableId}:{_resident.ActionSequence}",
                _resident.OwnerId,
                _vehicleWater,
                _waterCan,
                option.TargetInventory,
                NomadResourceIds.Water,
                option.TransferMilliliters,
                option.DrinkAfterDelivery
                    ? "居民为迫切饮水取得防漏水罐，把水从车辆水箱搬到饮水站"
                    : "居民在空闲时取得防漏水罐，低优先级补充饮水站库存",
                new[]
                {
                    $"facility:{option.SourceFacility.InstanceId}:water-pickup",
                    $"facility:{option.TargetFacility.InstanceId}:water-delivery",
                    $"carrier:{_waterCan.Id}",
                });

            if (!_resourceFlow.TryReserveHaul(
                    request,
                    out _resident.ActiveHaul,
                    out ResourceFlowBlocker blocker))
            {
                if (option.DrinkAfterDelivery)
                    Block("无法领取紧急搬水任务", blocker);
                else
                    SetResidentPhase(
                        FoundationResidentPhase.Idle,
                        "例行补水候选在预留时失效，稍后重试");
                return;
            }

            _resident.State.LastBlocker.Value = string.Empty;
            _resident.ActiveWaterSourceFacilityInstanceId = option.SourceFacility.InstanceId;
            _resident.ActiveWaterTargetFacilityInstanceId = option.TargetFacility.InstanceId;
            _resident.DrinkAfterActiveHaul = option.DrinkAfterDelivery;
            if (_waterCanLocation == FoundationWaterCanLocation.Resident)
            {
                TryBeginMove(
                    FoundationResidentPhase.MovingToWaterSource,
                    "已持有空水罐：沿连续 NavMesh 路径前往车辆水箱",
                    option.SourceFacility,
                    interactionGroupId: WaterPickupInteractionGroupId);
                return;
            }

            TryBeginMove(
                FoundationResidentPhase.MovingToWaterCan,
                option.DrinkAfterDelivery
                    ? "统一 Utility 选择紧急补水：先前往唯一防漏水罐"
                    : "统一 Utility 选择例行补货：先前往唯一防漏水罐",
                option.WaterCanFacility,
                allowAlternativeFacility: false,
                interactionGroupId: GetWaterCanAccessInteractionGroup(option.WaterCanFacility));
        }

        private string GetWaterCanAccessInteractionGroup(
            in FoundationFacilityState facility)
        {
            if (!_definitions.TryGetValue(
                    facility.DefinitionId,
                    out NomadFacilityDefinition definition))
                return string.Empty;

            return definition.Function switch
            {
                NomadFacilityFunction.VehicleWaterTank or NomadFacilityFunction.DrinkingStation => WaterCanAccessInteractionGroupId,
                _ => string.Empty,
            };
        }

        private void BeginLeisure(FoundationResidentDecisionOption option)
        {
            _resident.LeisureOutcomeScale = ResidentWellbeing.SampleLeisureOutcomeScale(
                worldSeed: worldSeed,
                residentId: _resident.OwnerId,
                leisureSequence: _resident.LeisureSequence++);
            if (option.Kind == FoundationResidentDecisionKind.Wander)
            {
                _resident.LeisureKind = FoundationLeisureKind.Wander;
                if (!TryAssignTravelPath(option.WanderTarget))
                {
                    _resident.LeisureOutcomeScale = 1f;
                    _resident.LeisureKind = FoundationLeisureKind.None;
                    SetResidentPhase(
                        FoundationResidentPhase.Idle,
                        "散步目标刚刚失效，稍后重新选择空地");
                    return;
                }
                SetResidentPhase(
                    FoundationResidentPhase.MovingToLeisure,
                    $"统一 Utility 选择休闲：沿连续 NavMesh 散步到 {option.WanderLabel}");
                return;
            }

            _resident.LeisureKind = FoundationLeisureKind.Daydream;
            BeginTimedPhase(
                FoundationResidentPhase.Relaxing,
                leisureSeconds,
                "统一 Utility 选择休闲：在原地发呆并观察四周");
        }

        private void BeginGroundRest()
        {
            _resident.LeisureOutcomeScale = ResidentWellbeing.SampleLeisureOutcomeScale(
                worldSeed: worldSeed,
                residentId: _resident.OwnerId,
                leisureSequence: _resident.LeisureSequence++);
            _resident.LeisureKind = FoundationLeisureKind.GroundRest;
            _resident.State.LastBlocker.Value = string.Empty;
            BeginTimedPhase(
                FoundationResidentPhase.RestingOnGround,
                groundRestSeconds,
                "缺少更合适的床椅，在当前安全地面坐下或躺下休息");
        }

        private void BeginHobby(FoundationResidentDecisionOption option)
        {
            _resident.LeisureOutcomeScale = ResidentWellbeing.SampleLeisureOutcomeScale(
                worldSeed: worldSeed,
                residentId: _resident.OwnerId,
                leisureSequence: _resident.LeisureSequence++);
            _resident.LeisureKind = FoundationLeisureKind.Hobby;
            _resident.State.LastBlocker.Value = string.Empty;
            if (TryBeginMove(
                    FoundationResidentPhase.MovingToHobby,
                    $"统一 Utility 选择爱好：前往 {option.TargetFacility.InstanceId} 的观景画架",
                    option.TargetFacility,
                    interactionGroupId: HobbyInteractionGroupId))
                return;

            // TryBeginMove 会保留语义移动意图并进入等待重试；只有初始化前置条件异常时才需要清理。
            if (_resident.HasMoveIntent) return;
            _resident.LeisureOutcomeScale = 1f;
            _resident.LeisureKind = FoundationLeisureKind.None;
        }

        private bool TrySelectWanderTarget(
            out Vector3 target,
            out DeckNavPathProbe selectedPath,
            out string label)
        {
            Vector3 current = ToNavigationPoint(_resident.State.ResidentLocalPosition.Value);
            DeckBounds bounds = deckLayout.CreateBounds();
            float margin = (_residentClearanceMillimeters + 120) / 1000f;
            float minX = bounds.MinXMillimeters / 1000f + margin;
            float maxX = bounds.MaxXMillimeters / 1000f - margin;
            float minZ = bounds.MinZMillimeters / 1000f + margin;
            float maxZ = bounds.MaxZMillimeters / 1000f - margin;
            float minimumDistance = Mathf.Max(0.2f, minimumWanderDistance);
            float maximumDistance = Mathf.Max(minimumDistance, maximumWanderDistance);

            for (var attempt = 0; attempt < 16; attempt++)
            {
                float normalizedRadius = (attempt % 4 + 1f) / 4f;
                float radius = Mathf.Lerp(minimumDistance, maximumDistance, normalizedRadius);
                float angleDegrees = (_resident.LeisureSequence * 83 + attempt * 137.508f) % 360f;
                float angle = angleDegrees * Mathf.Deg2Rad;
                var requested = new Vector3(
                    Mathf.Clamp(current.x + Mathf.Cos(angle) * radius, minX, maxX),
                    0f,
                    Mathf.Clamp(current.z + Mathf.Sin(angle) * radius, minZ, maxZ));
                if (HorizontalDistance(current, requested) < minimumDistance ||
                    !TryCalculateTravelPath(current, requested, out DeckNavPathProbe path) ||
                    path.PathLength < minimumDistance)
                    continue;

                target = ToNavigationPoint(path.SampledEnd);
                selectedPath = path;
                label = $"({target.x:0.00}, {target.z:0.00})";
                return true;
            }

            target = default;
            selectedPath = default;
            label = string.Empty;
            return false;
        }

        private ResidentActionPlanEvaluation EvaluateWaterRestockPlan(
            in FoundationFacilityState source,
            in FoundationFacilityState station,
            ResourceInventory stationInventory,
            bool drinkAfterDelivery,
            int haulMilliliters,
            out FoundationFacilityState waterCanFacility,
            out bool waterCanAtSource)
        {
            waterCanAtSource = _waterCanLocation == FoundationWaterCanLocation.VehicleWaterTank &&
                               string.Equals(
                                   _waterCanAnchorFacilityInstanceId,
                                   source.InstanceId,
                                   StringComparison.Ordinal);
            if (_waterCanLocation == FoundationWaterCanLocation.Resident)
            {
                waterCanFacility = default;
            }
            else
            {
                NomadFacilityFunction anchorFunction = waterCanAtSource
                    ? NomadFacilityFunction.VehicleWaterTank
                    : NomadFacilityFunction.DrinkingStation;
                if (!TryFindFacilityByInstanceId(
                        _waterCanAnchorFacilityInstanceId,
                        anchorFunction,
                        out waterCanFacility))
                    waterCanFacility = waterCanAtSource ? source : station;
            }

            CargoTransportOptionEvaluation carrierEvaluation =
                EvaluateWaterCanCargo(haulMilliliters);

            ResidentActionPlanFeasibility feasibility = carrierEvaluation.IsFeasible
                ? ResidentActionPlanFeasibility.Available
                : MapCargoFeasibility(carrierEvaluation.BlockReason);
            var steps = new List<ResidentActionStepEstimate>(8);
            if (feasibility.IsFeasible &&
                !TryBuildWaterRestockSteps(
                    source,
                    station,
                    drinkAfterDelivery,
                    waterCanFacility,
                    steps))
            {
                feasibility = ResidentActionPlanFeasibility.Blocked(
                    ResidentActionPlanBlockReason.TargetUnreachable,
                    "完整补水路线中至少有一段无法通过连续 NavMesh");
            }

            float stockDeficit = Mathf.Clamp01(
                (DrinkingStationRestockTargetMilliliters -
                 stationInventory.GetAmount(NomadResourceIds.Water)) /
                (float)DrinkingStationRestockTargetMilliliters);
            var proposal = new ResidentActionPlanProposal(
                $"water-restock:{_resident.StableId}:{_resident.ActionSequence + 1}",
                drinkAfterDelivery ? "drink-water" : "restock-drinking-station",
                drinkAfterDelivery ? "取防漏水罐、补水并饮用" : "取防漏水罐并例行补水")
            {
                Steps = steps.ToArray(),
                Feasibility = feasibility,
                BaseUtility = 0.04f,
                WorkUrgency = drinkAfterDelivery
                    ? 0.32f + _resident.WaterCycle.Thirst * 0.32f
                    : 0.18f + stockDeficit * 0.18f,
                DependencyValue = drinkAfterDelivery ? 0.18f : 0.04f,
                RiskTier = drinkAfterDelivery && _resident.WaterCycle.Thirst >= 0.82f
                    ? ResidentDecisionRiskTier.Urgent
                    : ResidentDecisionRiskTier.Routine,
                RiskPriority = drinkAfterDelivery ? _resident.WaterCycle.Thirst : 0f,
                DelayUrgency = drinkAfterDelivery ? _resident.WaterCycle.Thirst : stockDeficit * 0.25f,
                Effort = 0.24f,
                WorkIntensity = 0.3f,
                FailureProbability = carrierEvaluation.ContaminationTransferRisk,
                FailureSeverity = 0.35f,
                NeedEffects = drinkAfterDelivery
                    ? new[] { new NeedEffect(ResidentNeed.Thirst, 0.72f) }
                    : Array.Empty<NeedEffect>(),
                ReservationKeys = new[]
                {
                    $"facility:{source.InstanceId}:water-pickup",
                    $"facility:{station.InstanceId}:water-delivery",
                    $"carrier:{_waterCan.Id}",
                },
            };

            return _actionPlanEvaluator.Evaluate(
                proposal,
                CreateResidentDecisionCondition(),
                _actionPlanPolicy);
        }

        private bool TryBuildWaterRestockSteps(
            in FoundationFacilityState source,
            in FoundationFacilityState station,
            bool drinkAfterDelivery,
            in FoundationFacilityState waterCanFacility,
            ICollection<ResidentActionStepEstimate> steps)
        {
            if (!_definitions.TryGetValue(source.DefinitionId, out NomadFacilityDefinition sourceDefinition) ||
                !_definitions.TryGetValue(station.DefinitionId, out NomadFacilityDefinition stationDefinition))
                return false;

            Vector3 cursor = ToNavigationPoint(_resident.State.ResidentLocalPosition.Value);
            Vector3 sourceGoal;
            if (_waterCanLocation != FoundationWaterCanLocation.Resident)
            {
                if (!_definitions.TryGetValue(
                        waterCanFacility.DefinitionId,
                        out NomadFacilityDefinition waterCanDefinition))
                    return false;
                if (!TrySelectBestInteractionSlot(
                        cursor,
                        waterCanFacility,
                        waterCanDefinition,
                        out Vector3 waterCanGoal,
                        out _,
                        out _,
                        out float acquireTravelMeters,
                        out _,
                        GetWaterCanAccessInteractionGroup(waterCanFacility)))
                    return false;

                AddTravelStep(steps, acquireTravelMeters, "前往防漏水罐");
                steps.Add(new ResidentActionStepEstimate(
                    ResidentActionStepKind.AcquireItem,
                    EstimateWorkDuration(pickupSeconds + WaterCanLiftSeconds),
                    label: "接触并抬起唯一防漏水罐"));
                cursor = waterCanGoal;
            }

            if (!TrySelectBestInteractionSlot(cursor, source, sourceDefinition, out sourceGoal,
                    out _, out _, out float sourceTravelMeters, out _, WaterPickupInteractionGroupId))
                return false;
            AddTravelStep(steps, sourceTravelMeters, "携带空水罐从停放区前往装水位");

            steps.Add(new ResidentActionStepEstimate(
                ResidentActionStepKind.Transfer,
                EstimateWorkDuration(pickupSeconds),
                label: "把车辆水箱中的水装入防漏水罐"));

            if (!TrySelectBestInteractionSlot(
                    sourceGoal,
                    station,
                    stationDefinition,
                    out Vector3 stationGoal,
                    out _,
                    out _,
                    out float stationTravelMeters,
                    out _,
                    DrinkAndDeliverInteractionGroupId))
                return false;
            AddTravelStep(steps, stationTravelMeters, "携带有水的水罐前往饮水站");
            steps.Add(new ResidentActionStepEstimate(
                ResidentActionStepKind.Transfer,
                EstimateWorkDuration(deliverySeconds),
                label: "把水罐中的水倒入饮水站"));
            if (!TrySelectBestInteractionSlot(stationGoal, station, stationDefinition,
                    out Vector3 parkingGoal, out _, out _, out float parkingTravelMeters, out _,
                    WaterCanAccessInteractionGroupId))
                return false;
            AddTravelStep(steps, parkingTravelMeters, "携带空罐走到侧面停放区");
            steps.Add(new ResidentActionStepEstimate(ResidentActionStepKind.Transfer,
                EstimateWorkDuration(deliverySeconds + WaterCanReleaseSeconds), label: "落罐、松手并起身"));
            if (drinkAfterDelivery)
            {
                if (!TryMeasureFacilityPath(parkingGoal, station, stationDefinition,
                        out float toDrink, DrinkAndDeliverInteractionGroupId)) return false;
                AddTravelStep(steps, toDrink, "从侧面停放位返回饮水位");
                steps.Add(new ResidentActionStepEstimate(
                    ResidentActionStepKind.Consume,
                    drinkingSeconds,
                    label: "在饮水站饮水"));
            }
            return true;
        }

        private void AddTravelStep(
            ICollection<ResidentActionStepEstimate> steps,
            float distanceMeters,
            string label)
        {
            steps.Add(new ResidentActionStepEstimate(
                ResidentActionStepKind.Travel,
                distanceMeters / Mathf.Max(0.1f, residentMoveSpeed),
                distanceMeters,
                label));
        }

        private void WriteLatestActionPlan(
            ResidentActionPlanEvaluation plan,
            ResidentDecisionResult decision)
        {
            CandidateDecisionTrace trace = null;
            for (var i = 0; i < decision.Traces.Count; i++)
            {
                if (!ReferenceEquals(decision.Traces[i].Candidate, plan.Candidate)) continue;
                trace = decision.Traces[i];
                break;
            }
            ActionPlanUtilityBreakdown utility = plan.Utility;
            _resident.State.LatestActionPlan.Value = new FoundationActionPlanProjection(
                plan.Candidate.Id,
                plan.Candidate.DisplayName,
                plan.Feasibility.IsFeasible,
                ReferenceEquals(decision.Selected, plan.Candidate),
                plan.Candidate.BlockReason,
                plan.Steps.Count,
                utility.TotalDurationSeconds,
                utility.TravelSeconds,
                utility.TravelDistanceMeters,
                utility.ContextBenefit,
                utility.TravelCost,
                utility.QueueCost + utility.ActiveTimeCost,
                utility.EffortCost + utility.MotionWorkCost,
                utility.ExpectedRiskCost,
                utility.PlanCost,
                trace?.Score.Total ?? 0f,
                trace == null ? 0f : (float)trace.Probability,
                trace?.RiskTier ?? ResidentDecisionRiskTier.Routine,
                trace?.RiskPriority ?? 0f);
        }

        private static ResidentActionPlanFeasibility MapCargoFeasibility(
            CargoTransportBlockReason reason) => reason switch
        {
            CargoTransportBlockReason.CapacityInsufficient =>
                ResidentActionPlanFeasibility.Blocked(
                    ResidentActionPlanBlockReason.CarrierCapacityInsufficient,
                    "防漏水罐当前没有足够空余容量"),
            CargoTransportBlockReason.CarrierUnavailable =>
                ResidentActionPlanFeasibility.Blocked(
                    ResidentActionPlanBlockReason.PrerequisiteUnavailable,
                    "唯一防漏水罐当前不可取得"),
            CargoTransportBlockReason.None => ResidentActionPlanFeasibility.Available,
            _ => ResidentActionPlanFeasibility.Blocked(
                ResidentActionPlanBlockReason.MissingCompatibleCarrier,
                "水不能徒手搬运，且当前水罐缺少防漏能力"),
        };

        private void CompleteWaterCanPickup()
        {
            if (_waterCanLocation == FoundationWaterCanLocation.Resident)
            {
                Block("居民尝试重复取得同一个防漏水罐", ResourceFlowBlocker.None);
                return;
            }

            SetWaterCanLocation(FoundationWaterCanLocation.Resident);
            BeginTimedPhase(FoundationResidentPhase.LiftingWaterCan, WaterCanLiftSeconds,
                "接管同一只水罐，从实际停放点抬起");
        }

        private void CompleteWaterCanLift()
        {
            _resident.WaterCanContactPlacement = default;
            if (IsStopWaterVisit)
            {
                BeginStopWaterOutbound();
                return;
            }
            if (!TryFindFacilityByInstanceId(
                    _resident.ActiveWaterSourceFacilityInstanceId,
                    NomadFacilityFunction.VehicleWaterTank,
                    out FoundationFacilityState source))
            {
                Block("取得水罐后找不到车辆水箱设施", ResourceFlowBlocker.None);
                return;
            }

            TryBeginMove(
                FoundationResidentPhase.MovingToWaterSource,
                "携带空水罐沿连续 NavMesh 路径前往车辆水箱",
                source,
                interactionGroupId: WaterPickupInteractionGroupId);
        }

        private void CompleteWaterPickup()
        {
            if (_resident.ActiveHaul == null ||
                _resident.ActiveHaul.State != HaulTaskState.Reserved)
            {
                ReleaseActiveTasks();
                ClearActivePath();
                PublishCurrentFacilityAccessProjection();
                SetResidentPhase(
                    FoundationResidentPhase.Idle,
                    "取水任务已经失效，正在重新评估");
                _resident.DecisionRetryRemaining = 0f;
                return;
            }
            if (!IsWaterSourceOperational(_resident.ActiveWaterSourceFacilityInstanceId))
            {
                CancelPendingWaterHaulForFault(_resident.ActiveWaterSourceFacilityInstanceId);
                return;
            }

            _resident.ActiveHaul.PickUp();
            if (!TryFindFacilityByInstanceId(
                    _resident.ActiveWaterTargetFacilityInstanceId,
                    NomadFacilityFunction.DrinkingStation,
                    out FoundationFacilityState station))
            {
                Block("饮水站在搬运途中消失", ResourceFlowBlocker.None);
                return;
            }

            TryBeginMove(
                FoundationResidentPhase.MovingToDrinkingStation,
                "携带装有水的防漏水罐沿连续路径前往饮水站",
                station,
                interactionGroupId: DrinkAndDeliverInteractionGroupId);
        }

        private void CompleteWaterDelivery()
        {
            bool shouldDrink = (_resident.DrinkAfterActiveHaul ||
                                _resident.WaterCycle.Thirst >= DrinkNeedThreshold) &&
                               _resident.WaterCycle.BodyWater.FreeCapacity >=
                               ResidentWaterCycle.DefaultDrinkServingMilliliters;
            string deliveredStationInstanceId = _resident.ActiveWaterTargetFacilityInstanceId;
            _resident.ActiveHaul.Deliver();
            _resident.ActiveHaul = null;
            _resident.DrinkAfterActiveHaul = shouldDrink;
            _resident.ActiveWaterSourceFacilityInstanceId = string.Empty;
            BeginWaterCanParking(deliveredStationInstanceId, FoundationWaterCanLocation.DrinkingStation);
        }

        private void BeginDrink(FoundationFacilityState station)
        {
            if (!_drinkingStationInventories.TryGetValue(
                    station.InstanceId,
                    out ResourceInventory stationInventory))
            {
                Block(
                    $"饮水站 {station.InstanceId} 缺少实例库存",
                    ResourceFlowBlocker.None);
                return;
            }

            if (!_resident.WaterCycle.TryReserveDrink(
                    _resourceFlow,
                    stationInventory,
                    $"drink:{_resident.StableId}:{_resident.ActionSequence}",
                    $"facility:{station.InstanceId}:drink",
                    ResidentWaterCycle.DefaultDrinkServingMilliliters,
                    out _resident.ActiveWaterAction,
                    out ResourceFlowBlocker blocker))
            {
                if (blocker.Reason == ResourceFlowBlockReason.DestinationFull)
                {
                    _resident.State.LastBlocker.Value =
                        $"{blocker.Reason} · {blocker.InventoryId} · 等待代谢或如厕后再饮水";
                    _resident.ActiveWaterTargetFacilityInstanceId = string.Empty;
                    ReleaseActiveInteractionSpace(publishProjection: true);
                    SetResidentPhase(
                        FoundationResidentPhase.Idle,
                        "饮水候选在预留时发现体内容量不足，稍后统一重新决策");
                }
                else
                {
                    Block("无法开始饮水", blocker);
                }
                return;
            }

            _resident.ActiveWaterTargetFacilityInstanceId = station.InstanceId;
            if (!TryBeginMove(
                    FoundationResidentPhase.MovingToDrinkingStation,
                    "沿连续 NavMesh 路径前往饮水站",
                    station,
                    allowAlternativeFacility: false,
                    interactionGroupId: DrinkAndDeliverInteractionGroupId))
                return;

            if (AdvanceResidentAlongPath(0f))
            {
                ClearActiveMoveIntent();
                BeginTimedPhase(FoundationResidentPhase.Drinking, drinkingSeconds, "在饮水站喝水");
            }
        }

        private void CompleteDrink()
        {
            _resident.ActiveWaterAction.Commit();
            _resident.ActiveWaterAction = null;
            ReleaseActiveInteractionSpace(publishProjection: true);
            _resident.State.CompletedDrinkCount.Value++;
            _resident.ActiveWaterTargetFacilityInstanceId = string.Empty;
            SetResidentPhase(FoundationResidentPhase.Idle, "完成一次饮水，水已进入居民身体");
        }

        private void CompleteToiletUse()
        {
            _resident.ActiveWaterAction.Commit();
            _resident.ActiveWaterAction = null;
            ReleaseActiveInteractionSpace(publishProjection: true);
            _resident.State.CompletedToiletUseCount.Value++;
            _resident.State.LastBlocker.Value = string.Empty;
            SetResidentPhase(
                FoundationResidentPhase.Idle,
                "完成一次如厕，排泄物已进入旱厕暂存桶");
        }

        private void CompleteLeisure()
        {
            _resident.LeisureOutcomeScale = 1f;
            _resident.State.CompletedLeisureCount.Value++;
            if (_resident.LeisureKind == FoundationLeisureKind.Daydream)
                _resident.State.CompletedDaydreamCount.Value++;
            else if (_resident.LeisureKind == FoundationLeisureKind.Wander)
                _resident.State.CompletedWanderCount.Value++;
            _resident.LeisureKind = FoundationLeisureKind.None;
            SetResidentPhase(
                FoundationResidentPhase.Idle,
                "完成一次自主休整：疲劳与压力得到缓解，娱乐满足度未被虚构补充");
        }

        private void CompleteGroundRest()
        {
            _resident.LeisureOutcomeScale = 1f;
            _resident.LeisureKind = FoundationLeisureKind.None;
            _resident.State.CompletedGroundRestCount.Value++;
            SetResidentPhase(
                FoundationResidentPhase.Idle,
                "完成一次地面休息：健康和疲劳得到有限恢复，但舒适度不足略微影响心情");
        }

        private void CompleteHobby()
        {
            ReleaseActiveInteractionSpace(publishProjection: true);
            _resident.LeisureOutcomeScale = 1f;
            _resident.LeisureKind = FoundationLeisureKind.None;
            _resident.State.CompletedLeisureCount.Value++;
            _resident.State.CompletedHobbyCount.Value++;
            SetResidentPhase(
                FoundationResidentPhase.Idle,
                "完成一次作画与观景：娱乐满足、心情和压力已按连续身心模型结算");
        }

        /// <summary>
        /// 把 Unity 行动状态机折叠成纯模拟可理解的负荷类型。休闲的随机效果系数在行动开始时固定，
        /// 此处只负责连续结算，不因帧数或 View 是否打开而重新抽样。
        /// </summary>
        private ResidentWellbeingActivity ResolveWellbeingActivity()
        {
            if (_resident.Phase == FoundationResidentPhase.EnjoyingHobby)
                return ResidentWellbeingActivity.Hobby;

            if (_resident.Phase == FoundationResidentPhase.RestingOnGround)
                return ResidentWellbeingActivity.GroundRest;

            if (_resident.Phase == FoundationResidentPhase.MovingToLeisure ||
                _resident.Phase == FoundationResidentPhase.Relaxing)
            {
                return _resident.LeisureKind switch
                {
                    FoundationLeisureKind.Wander => ResidentWellbeingActivity.Wander,
                    FoundationLeisureKind.Daydream => ResidentWellbeingActivity.Daydream,
                    _ => ResidentWellbeingActivity.Routine,
                };
            }

            return _resident.Phase switch
            {
                FoundationResidentPhase.MovingToWaterCan or
                    FoundationResidentPhase.MovingToWaterCanParking or
                    FoundationResidentPhase.MovingToWaterSource or
                    FoundationResidentPhase.MovingToDrinkingStation or
                    FoundationResidentPhase.MovingToToilet or
                    FoundationResidentPhase.MovingToHobby or
                    FoundationResidentPhase.MovingToWorldItemSource or
                    FoundationResidentPhase.MovingToWorldItemDestination or
                    FoundationResidentPhase.MovingToRepairPart or
                    FoundationResidentPhase.MovingToDriver or
                    FoundationResidentPhase.MovingToStopWater or
                    FoundationResidentPhase.MovingToStopSpare or
                    FoundationResidentPhase.ReturningStopSpare or
                    FoundationResidentPhase.MovingToWasteBucket or
                    FoundationResidentPhase.MovingToWasteReceiver or
                    FoundationResidentPhase.ReturningWasteBucket or
                    FoundationResidentPhase.ReturningFromStopWater or
                    FoundationResidentPhase.MovingToRepairTarget =>
                    ResidentWellbeingActivity.Travel,
                FoundationResidentPhase.PickingUpWaterCan or
                    FoundationResidentPhase.LiftingWaterCan or FoundationResidentPhase.PlacingWaterCan or
                    FoundationResidentPhase.ReleasingWaterCan or
                    FoundationResidentPhase.PickingUpWater or
                    FoundationResidentPhase.DeliveringWater or
                    FoundationResidentPhase.PickingUpWorldItem or
                    FoundationResidentPhase.PlacingWorldItem or
                    FoundationResidentPhase.PickingUpRepairPart or
                    FoundationResidentPhase.FillingAtStop or
                    FoundationResidentPhase.DetachingWasteBucket or
                    FoundationResidentPhase.EmptyingWasteBucket or
                    FoundationResidentPhase.InstallingWasteBucket or
                    FoundationResidentPhase.DeliveringStopWater or
                    FoundationResidentPhase.PickingUpStopSpare or
                    FoundationResidentPhase.DeliveringStopSpare or
                    FoundationResidentPhase.RepairingFacility =>
                    ResidentWellbeingActivity.Work,
                FoundationResidentPhase.Driving => ResidentWellbeingActivity.Work,
                FoundationResidentPhase.Drinking or
                    FoundationResidentPhase.UsingToilet =>
                    ResidentWellbeingActivity.PersonalCare,
                _ => ResidentWellbeingActivity.Routine,
            };
        }

        private bool TryBeginMove(
            FoundationResidentPhase phase,
            string task,
            in FoundationFacilityState facility,
            bool allowAlternativeFacility = true,
            string interactionGroupId = "")
        {
            if (!_definitions.TryGetValue(
                    facility.DefinitionId,
                    out NomadFacilityDefinition definition))
            {
                Block($"设施 {facility.InstanceId} 缺少定义", ResourceFlowBlocker.None);
                return false;
            }

            _resident.MoveIntent = new FoundationResidentMoveIntent(
                phase,
                task,
                definition.Function,
                facility.InstanceId,
                allowAlternativeFacility,
                interactionGroupId);
            _resident.HasMoveIntent = true;
            return TryResumeActiveMove();
        }

        /// <summary>
        /// 从语义移动意图重新选择工作位。优先保留原设施；它完全不可用时才考虑同功能设施，
        /// 从而让建造后的恢复既稳定又不会永远追逐已经失效的世界坐标。
        /// </summary>
        private bool TryResumeActiveMove()
        {
            if (!_resident.HasMoveIntent) return false;

            ReleaseActiveInteractionSpace(publishProjection: false);
            ClearActivePath();
            ResourceFlowBlocker retargetBlocker = ResourceFlowBlocker.None;
            bool hasCandidate = TrySelectFacilityInteractionForIntent(
                    _resident.MoveIntent,
                    out FoundationFacilityState selectedFacility,
                    out DeckPose dockingPose,
                    out string slotLabel,
                    out InteractionSpaceSlot selectedSlot);
            bool resourceTargetReady = hasCandidate &&
                                       TryRetargetActiveWaterHaul(
                                           selectedFacility,
                                           out retargetBlocker) &&
                                       TryRetargetActiveToiletUse(selectedFacility, out retargetBlocker);
            if (!resourceTargetReady ||
                !TryAcquireInteractionSpace(selectedSlot) ||
                !TryAssignDockingPath(dockingPose))
            {
                ReleaseActiveInteractionSpace(publishProjection: false);
                ClearActivePath();
                if (_resident.MoveIntent.TravelPhase == FoundationResidentPhase.MovingToToilet ||
                    (_resident.MoveIntent.TravelPhase == FoundationResidentPhase.MovingToDrinkingStation &&
                     _resident.ActiveHaul?.State != HaulTaskState.Carrying))
                {
                    // 尚未消费的身体/站点预约不应在丢失工作位后长期占着资源；重新竞标仍不会转移水量。
                    // 已取出的水不同：取消 Haul 只释放预留，货物仍留在罐里。必须保留交付意图等待/改道，
                    // 否则下一次任务只搬新水，残留水永远无法送达，还会阻断要求空罐的驿站取水。
                    ReleaseActiveTasks();
                    SetResidentPhase(FoundationResidentPhase.Idle, "饮水或如厕的通路/容量已改变，取消未提交行动并重评");
                    _resident.DecisionRetryRemaining = 0f;
                    PublishCurrentFacilityAccessProjection();
                    return false;
                }
                if (_resident.MoveIntent.TravelPhase == FoundationResidentPhase.MovingToHobby)
                {
                    // 爱好没有物资或中间结果需要保护；目标失效时回到统一决策，比长期占住
                    // WaitingForRoute 更安全，否则后续口渴 / 如厕等新需求无法得到评估。
                    ClearActiveMoveIntent();
                    _resident.LeisureOutcomeScale = 1f;
                    _resident.LeisureKind = FoundationLeisureKind.None;
                    _resident.State.LastBlocker.Value = string.Empty;
                    PublishCurrentFacilityAccessProjection();
                    SetResidentPhase(
                        FoundationResidentPhase.Idle,
                        "观景画架当前不可达，已放弃软性爱好并准备重新决策");
                    return false;
                }
                _resident.RouteRetryRemaining = Mathf.Max(0.1f, routeRetrySeconds);
                SetResidentPhase(
                    FoundationResidentPhase.WaitingForRoute,
                    $"路线暂不可达：{_resident.MoveIntent.Task}；将自动换 Slot、换同功能设施或重试");
                _resident.State.LastBlocker.Value = retargetBlocker.IsBlocked
                    ? $"{retargetBlocker.Reason} · 搬运资源目标无法改道 · " +
                      selectedFacility.InstanceId
                    : $"RouteUnavailable · {_resident.MoveIntent.FacilityFunction} · " +
                      _resident.MoveIntent.PreferredFacilityInstanceId;
                PublishCurrentFacilityAccessProjection();
                return false;
            }

            _resident.MoveIntent = _resident.MoveIntent.Retarget(selectedFacility.InstanceId);
            _resident.RouteRetryRemaining = 0f;
            _resident.State.LastBlocker.Value = string.Empty;
            PublishCurrentFacilityAccessProjection();
            SetResidentPhase(
                _resident.MoveIntent.TravelPhase,
                $"{_resident.MoveIntent.Task} · {selectedFacility.InstanceId}/{slotLabel}");
            return true;
        }

        private bool TryRetargetActiveWaterHaul(
            in FoundationFacilityState selectedFacility,
            out ResourceFlowBlocker blocker)
        {
            blocker = ResourceFlowBlocker.None;
            if (_resident.ActiveHaul == null ||
                _resident.ActiveHaul.State != HaulTaskState.Carrying ||
                _resident.MoveIntent.TravelPhase !=
                FoundationResidentPhase.MovingToDrinkingStation ||
                string.Equals(
                    selectedFacility.InstanceId,
                    _resident.ActiveWaterTargetFacilityInstanceId,
                    StringComparison.Ordinal))
                return true;

            if (!_drinkingStationInventories.TryGetValue(
                    selectedFacility.InstanceId,
                    out ResourceInventory destination))
            {
                blocker = new ResourceFlowBlocker(
                    ResourceFlowBlockReason.DestinationFull,
                    BuildDrinkingStationInventoryId(selectedFacility.InstanceId),
                    NomadResourceIds.Water);
                return false;
            }

            if (!_resident.ActiveHaul.TryRetargetDestination(
                    destination,
                    new[]
                    {
                        $"facility:{_resident.ActiveWaterSourceFacilityInstanceId}:water-pickup",
                        $"facility:{selectedFacility.InstanceId}:water-delivery",
                        $"carrier:{_waterCan.Id}",
                    },
                    out blocker))
                return false;

            _resident.ActiveWaterTargetFacilityInstanceId = selectedFacility.InstanceId;
            return true;
        }

        private bool TrySelectFacilityInteractionForIntent(
            in FoundationResidentMoveIntent intent,
            out FoundationFacilityState selectedFacility,
            out DeckPose selectedDockingPose,
            out string selectedLabel,
            out InteractionSpaceSlot selectedSlot)
        {
            if (TryFindFacilityByInstanceId(
                    intent.PreferredFacilityInstanceId,
                    intent.FacilityFunction,
                    out FoundationFacilityState preferred) &&
                IsFacilityCompatibleWithActiveMove(intent, preferred) &&
                TrySelectBestInteractionSlot(
                    ToNavigationPoint(_resident.State.ResidentLocalPosition.Value),
                    preferred,
                    _definitions[preferred.DefinitionId],
                    out _,
                    out selectedDockingPose,
                    out selectedLabel,
                    out _,
                    out selectedSlot,
                    intent.InteractionGroupId))
            {
                selectedFacility = preferred;
                return true;
            }

            selectedFacility = default;
            selectedDockingPose = default;
            selectedLabel = string.Empty;
            selectedSlot = default;
            if (!intent.AllowAlternativeFacility) return false;

            float bestLength = float.PositiveInfinity;
            IReadOnlyList<FoundationFacilityState> facilities = _model.Facilities;
            for (var i = 0; i < facilities.Count; i++)
            {
                FoundationFacilityState candidate = facilities[i];
                if (string.Equals(
                        candidate.InstanceId,
                        intent.PreferredFacilityInstanceId,
                        StringComparison.Ordinal) ||
                    !_definitions.TryGetValue(
                        candidate.DefinitionId,
                        out NomadFacilityDefinition definition) ||
                    definition.Function != intent.FacilityFunction ||
                    !IsFacilityCompatibleWithActiveMove(intent, candidate) ||
                    !TrySelectBestInteractionSlot(
                        ToNavigationPoint(_resident.State.ResidentLocalPosition.Value),
                        candidate,
                        definition,
                        out _,
                        out DeckPose dockingPose,
                        out string label,
                        out float pathLength,
                        out InteractionSpaceSlot slot,
                        intent.InteractionGroupId) ||
                    pathLength >= bestLength)
                    continue;

                bestLength = pathLength;
                selectedFacility = candidate;
                selectedDockingPose = dockingPose;
                selectedLabel = label;
                selectedSlot = slot;
            }
            return bestLength < float.PositiveInfinity;
        }

        private bool IsFacilityCompatibleWithActiveMove(
            in FoundationResidentMoveIntent intent,
            in FoundationFacilityState candidate)
        {
            if (intent.TravelPhase == FoundationResidentPhase.MovingToToilet)
                return IsToiletCompatibleWithActiveMove(candidate);
            if (intent.TravelPhase == FoundationResidentPhase.MovingToWaterSource &&
                !IsWaterSourceOperational(candidate.InstanceId))
                return false;

            if (_resident.ActiveHaul == null ||
                _resident.ActiveHaul.State != HaulTaskState.Carrying ||
                intent.TravelPhase != FoundationResidentPhase.MovingToDrinkingStation ||
                string.Equals(
                    candidate.InstanceId,
                    _resident.ActiveWaterTargetFacilityInstanceId,
                    StringComparison.Ordinal))
                return true;

            return _drinkingStationInventories.TryGetValue(
                       candidate.InstanceId,
                       out ResourceInventory inventory) &&
                   _resourceFlow.GetAvailableCapacity(inventory) >= _resident.ActiveHaul.Request.Amount;
        }

        private bool TrySelectBestInteractionSlot(
            in FoundationFacilityState facility,
            NomadFacilityDefinition definition,
            out Vector3 selectedPosition,
            out string selectedLabel,
            out InteractionSpaceSlot selectedSlot,
            string interactionGroupId = "")
            => TrySelectBestInteractionSlot(
                ToNavigationPoint(_resident.State.ResidentLocalPosition.Value),
                facility,
                definition,
                out selectedPosition,
                out _,
                out selectedLabel,
                out _,
                out selectedSlot,
                interactionGroupId);

        private bool TrySelectBestInteractionSlot(
            Vector3 start,
            in FoundationFacilityState facility,
            NomadFacilityDefinition definition,
            out Vector3 selectedPosition,
            out DeckPose selectedDockingPose,
            out string selectedLabel,
            out float selectedPathLength,
            out InteractionSpaceSlot selectedSlot,
            string interactionGroupId = "")
        {
            selectedPosition = default;
            selectedDockingPose = default;
            selectedLabel = string.Empty;
            selectedSlot = default;
            float bestLength = float.PositiveInfinity;
            start = ToNavigationPoint(start);
            IReadOnlyList<NomadFacilityInteractionGroupDefinition> groups =
                definition.InteractionGroups;
            for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                NomadFacilityInteractionGroupDefinition group = groups[groupIndex];
                if (!string.IsNullOrEmpty(interactionGroupId) &&
                    !string.Equals(
                        group.GroupId,
                        interactionGroupId,
                        StringComparison.Ordinal))
                    continue;
                for (var slotIndex = 0; slotIndex < group.AlternativeSlots.Count; slotIndex++)
                {
                    NomadFacilityInteractionSlotDefinition slotDefinition =
                        group.AlternativeSlots[slotIndex];
                    DeckPose slotPose = slotDefinition.Resolve(facility.Pose);
                    InteractionSpaceSlot candidateSlot =
                        FoundationInteractionSpaceRuntime.ResolveSlot(
                            facility,
                            group,
                            slotDefinition);
                    if (!_interactionSpaces.IsAvailable(candidateSlot.Address))
                        continue;
                    if (!TryCalculateDockingPath(start, slotPose, out DeckNavPathProbe path) ||
                        path.PathLength >= bestLength)
                        continue;

                    bestLength = path.PathLength;
                    selectedPosition = ToNavigationPoint(deckLayout.PoseToLocal(slotPose));
                    selectedDockingPose = slotPose;
                    selectedLabel = $"{group.GroupId}/{slotDefinition.SlotId}";
                    selectedSlot = candidateSlot;
                }
            }
            selectedPathLength = bestLength < float.PositiveInfinity ? bestLength : 0f;
            return bestLength < float.PositiveInfinity;
        }

        private bool TryAcquireInteractionSpace(in InteractionSpaceSlot slot)
        {
            if (_resident.InteractionSpace != null) return false;
            return _interactionSpaces.TryAcquire(
                _resident.OwnerId, slot, out _resident.InteractionSpace);
        }

        private void ReleaseActiveInteractionSpace(bool publishProjection)
        {
            bool changed = _resident.ReleaseInteractionSpace();
            if (changed) PublishResidentFacilityWork();
            if (changed && publishProjection)
                PublishCurrentFacilityAccessProjection();
        }

        private void PublishCurrentFacilityAccessProjection()
        {
            if (_model == null) return;
            FoundationPlacementPreviewState preview = _model.PlacementPreview.Value;
            bool previewEvaluated = preview.Active &&
                                    preview.RealtimeReachabilityEvaluated;
            ContinuousFacilityPlacementRequest? previewCandidate =
                previewEvaluated && _activePlacementDefinition != null
                    ? CreatePreviewRequest()
                    : null;
            PublishFacilityAccessProjection(
                previewEvaluated,
                previewEvaluated && _previewResidentOriginResolved,
                previewCandidate);
        }

        private bool TryAssignDockingPath(in DeckPose dockingPose)
        {
            Vector3 start = ToNavigationPoint(_resident.State.ResidentLocalPosition.Value);
            if (!TryCalculateDockingPath(start, dockingPose, out DeckNavPathProbe path))
                return false;

            Vector3 exactGoal = ToNavigationPoint(deckLayout.PoseToLocal(dockingPose));
            return AssignPath(
                path,
                exactGoal,
                hasDockingYaw: true,
                (float)dockingPose.YawDegrees);
        }

        private bool TryAssignTravelPath(Vector3 requestedGoal)
        {
            Vector3 start = ToNavigationPoint(_resident.State.ResidentLocalPosition.Value);
            requestedGoal = ToNavigationPoint(requestedGoal);
            if (!TryCalculateTravelPath(start, requestedGoal, out DeckNavPathProbe path))
                return false;
            return AssignPath(
                path,
                ToNavigationPoint(path.SampledEnd),
                hasDockingYaw: false,
                0f);
        }

        private bool AssignPath(
            in DeckNavPathProbe path,
            Vector3 exactGoal,
            bool hasDockingYaw,
            float dockingYawDegrees)
        {
            var cornerCount = path.Corners.Count;
            bool appendExactGoal = cornerCount == 0 ||
                HorizontalDistance(path.Corners[cornerCount - 1], exactGoal) > 0.001f;
            _resident.PathCorners = new Vector3[cornerCount + (appendExactGoal ? 1 : 0)];

            for (var i = 0; i < cornerCount; i++)
            {
                _resident.PathCorners[i] = ToNavigationPoint(path.Corners[i]);
            }
            exactGoal = ToNavigationPoint(exactGoal);
            if (appendExactGoal) _resident.PathCorners[cornerCount] = exactGoal;
            _resident.HasDockingYaw = hasDockingYaw;
            _resident.DockingYawDegrees = Mathf.Repeat(dockingYawDegrees, 360f);
            Vector3 current = _resident.State.ResidentLocalPosition.Value;
            _resident.NextPathCornerIndex = _resident.PathCorners.Length > 0 &&
                                   HorizontalDistance(current, _resident.PathCorners[0]) <= 0.01f
                ? 1
                : 0;
            _resident.State.ActivePathSummary.Value = DescribePath(_resident.PathCorners);
            WriteRemainingPathProjection();
            return _resident.PathCorners.Length > 0 && AssignNativePath(path.SampledEnd);
        }

        private bool AdvanceResidentAlongPath(float deltaTime)
        {
            if (_resident.PathCorners.Length == 0)
            {
                if (_resident.HasMoveIntent)
                {
                    _resident.RouteRetryRemaining = Mathf.Max(0.1f, routeRetrySeconds);
                    SetResidentPhase(
                        FoundationResidentPhase.WaitingForRoute,
                        "当前路线失效，正在等待自动重新解析移动意图");
                }
                else
                {
                    Block("居民处于移动阶段，但没有可执行 NavMesh 路径", ResourceFlowBlocker.None);
                }
                return false;
            }

            if (UsesNativeLocomotion) return AdvanceNativeResidentPath(deltaTime);
            Vector3 current = _resident.State.ResidentLocalPosition.Value;
            float remainingDistance = residentMoveSpeed * Mathf.Max(0f, deltaTime);
            while (_resident.NextPathCornerIndex < _resident.PathCorners.Length)
            {
                Vector3 target = _resident.PathCorners[_resident.NextPathCornerIndex];
                Vector3 facing = target - current;
                if (facing.sqrMagnitude > 0.000001f)
                    _resident.State.ResidentLocalYawDegrees.Value = Mathf.Repeat(
                        Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg,
                        360f);
                float distance = Vector3.Distance(current, target);
                if (distance <= 0.0001f)
                {
                    current = target;
                    _resident.NextPathCornerIndex++;
                    continue;
                }

                if (remainingDistance <= 0f) break;
                if (distance <= remainingDistance)
                {
                    current = target;
                    remainingDistance -= distance;
                    _resident.NextPathCornerIndex++;
                    continue;
                }

                current = Vector3.MoveTowards(current, target, remainingDistance);
                remainingDistance = 0f;
            }

            if (current != _resident.State.ResidentLocalPosition.Value)
                _resident.State.ResidentLocalPosition.Value = current;
            WriteRemainingPathProjection();

            if (_resident.NextPathCornerIndex < _resident.PathCorners.Length) return false;
            if (_resident.HasDockingYaw)
                _resident.State.ResidentLocalYawDegrees.Value = _resident.DockingYawDegrees;
            ClearActivePath();
            return true;
        }

        private void ReplanActiveMoveAfterPlacement()
        {
            if (_resident.Phase == FoundationResidentPhase.MovingToLeisure)
            {
                ClearActivePath();
                _resident.LeisureOutcomeScale = 1f;
                _resident.LeisureKind = FoundationLeisureKind.None;
                SetResidentPhase(
                    FoundationResidentPhase.Idle,
                    "建造改变了散步路线，已放弃旧空地点并准备重新选择");
                return;
            }

            if (!_resident.HasMoveIntent || TryResumeActiveMove()) return;

            bool canSafelyReplan = _resident.ActiveWaterAction != null ||
                                   (_resident.ActiveHaul != null &&
                                    _resident.ActiveHaul.State == HaulTaskState.Reserved) ||
                                   _resident.ActiveWorldItemMove != null ||
                                   _resident.ActiveRepairPartUse != null;
            if (!canSafelyReplan) return;

            ReleaseActiveTasks();
            ClearActivePath();
            PublishCurrentFacilityAccessProjection();
            _resident.State.LastBlocker.Value = string.Empty;
            SetResidentPhase(
                FoundationResidentPhase.Idle,
                "建造使未提交任务路线失效，已释放预留并准备重新选择目标");
        }

        private void WriteRemainingPathProjection()
        {
            float remaining = 0f;
            Vector3 cursor = _resident.State.ResidentLocalPosition.Value;
            for (var i = _resident.NextPathCornerIndex; i < _resident.PathCorners.Length; i++)
            {
                remaining += Vector3.Distance(cursor, _resident.PathCorners[i]);
                cursor = _resident.PathCorners[i];
            }

            SetFloat(_resident.State.RemainingPathMeters, remaining);
            int corners = Math.Max(0, _resident.PathCorners.Length - _resident.NextPathCornerIndex);
            if (_resident.State.RemainingPathCorners.Value != corners)
                _resident.State.RemainingPathCorners.Value = corners;
        }

        private void ClearActivePath()
        {
            _resident.ClearPath();
            if (_model == null) return;
            _resident.State.MovementStallMilliseconds.Value = 0L;
            SetFloat(_resident.State.RemainingPathMeters, 0f);
            if (_resident.State.RemainingPathCorners.Value != 0) _resident.State.RemainingPathCorners.Value = 0;
            if (!string.IsNullOrEmpty(_resident.State.ActivePathSummary.Value))
                _resident.State.ActivePathSummary.Value = string.Empty;
        }

        private void ClearActiveMoveIntent()
        {
            _resident.ClearMoveIntent();
        }

        private static string DescribePath(IReadOnlyList<Vector3> corners)
        {
            var builder = new StringBuilder();
            for (var i = 0; i < corners.Count; i++)
            {
                if (i > 0) builder.Append(" → ");
                builder.Append('(')
                    .Append(corners[i].x.ToString("0.00"))
                    .Append(", ")
                    .Append(corners[i].z.ToString("0.00"))
                    .Append(')');
            }
            return builder.ToString();
        }

        private void BeginTimedPhase(
            FoundationResidentPhase phase,
            float duration,
            string task)
        {
            float resolvedDuration = duration;
            if (IsWorkTimedPhase(phase))
            {
                _resident.WorkEfficiency = ResidentPerformance.SampleWorkEfficiency(
                    worldSeed,
                    _resident.OwnerId,
                    _resident.WorkActionSequence++,
                    CalculateExpectedWorkEfficiency(),
                    workPaceVariation);
                resolvedDuration /= _resident.WorkEfficiency;
            }
            else
            {
                _resident.WorkEfficiency = CalculateExpectedWorkEfficiency();
            }

            _resident.PhaseDuration = Mathf.Max(0.001f, resolvedDuration);
            _resident.PhaseRemaining = _resident.PhaseDuration;
            _resident.State.ActionProgress.Value = 0f;
            SetResidentPhase(phase, task);
        }

        private float CalculateExpectedWorkEfficiency() =>
            ResidentPerformance.CalculateExpectedWorkEfficiency(
                residentBaseWorkEfficiency,
                _resident.Wellbeing.Health,
                _resident.Wellbeing.Fatigue,
                _resident.Wellbeing.Stress);

        private float EstimateWorkDuration(float standardDuration) =>
            standardDuration / CalculateExpectedWorkEfficiency();

        private static bool IsWorkTimedPhase(FoundationResidentPhase phase) =>
            phase is FoundationResidentPhase.PickingUpWaterCan or
                FoundationResidentPhase.LiftingWaterCan or FoundationResidentPhase.PlacingWaterCan or
                FoundationResidentPhase.ReleasingWaterCan or
                FoundationResidentPhase.PickingUpWater or
                FoundationResidentPhase.DeliveringWater or
                FoundationResidentPhase.PickingUpWorldItem or
                FoundationResidentPhase.PlacingWorldItem or
                FoundationResidentPhase.PickingUpRepairPart or
                FoundationResidentPhase.FillingAtStop or
                FoundationResidentPhase.DetachingWasteBucket or
                FoundationResidentPhase.EmptyingWasteBucket or
                FoundationResidentPhase.InstallingWasteBucket or
                FoundationResidentPhase.DeliveringStopWater or
                    FoundationResidentPhase.PickingUpStopSpare or
                    FoundationResidentPhase.DeliveringStopSpare or
                FoundationResidentPhase.RepairingFacility;

        private bool TickTimer(float deltaTime)
        {
            _resident.PhaseRemaining = Mathf.Max(0f, _resident.PhaseRemaining - deltaTime);
            _resident.State.ActionProgress.Value = 1f - _resident.PhaseRemaining / _resident.PhaseDuration;
            PublishResidentFacilityWork();
            return _resident.PhaseRemaining <= 0f;
        }

        private void SetResidentPhase(FoundationResidentPhase phase, string task)
        {
            _resident.Phase = phase;
            _resident.State.ResidentPhase.Value = phase;
            _resident.State.CurrentTask.Value = task;
            if (phase != FoundationResidentPhase.PickingUpWaterCan &&
                phase != FoundationResidentPhase.LiftingWaterCan &&
                phase != FoundationResidentPhase.PlacingWaterCan &&
                phase != FoundationResidentPhase.ReleasingWaterCan &&
                phase != FoundationResidentPhase.PickingUpWater &&
                phase != FoundationResidentPhase.DeliveringWater &&
                phase != FoundationResidentPhase.PickingUpWorldItem &&
                phase != FoundationResidentPhase.PlacingWorldItem &&
                phase != FoundationResidentPhase.PickingUpRepairPart &&
                phase != FoundationResidentPhase.RepairingFacility &&
                phase != FoundationResidentPhase.Drinking &&
                phase != FoundationResidentPhase.UsingToilet &&
                phase != FoundationResidentPhase.Relaxing &&
                phase != FoundationResidentPhase.RestingOnGround &&
                phase != FoundationResidentPhase.EnjoyingHobby)
                _resident.State.ActionProgress.Value = 0f;
            PublishResidentFacilityWork();
        }

        private void HandleResidentDeath()
        {
            ReleaseActiveTasks();
            ReleaseActiveInteractionSpace(publishProjection: true);
            ClearActivePath();
            ClearActiveMoveIntent();
            _resident.LeisureOutcomeScale = 1f;
            _resident.LeisureKind = FoundationLeisureKind.None;
            _resident.PhaseDuration = 0f;
            _resident.PhaseRemaining = 0f;
            SetResidentPhase(
                FoundationResidentPhase.Dead,
                "健康归零，居民已经死亡");
            _resident.State.LastBlocker.Value = "ResidentDied · 健康归零；普通休息不能复活居民";
        }

        private void Block(string task, ResourceFlowBlocker blocker)
        {
            ReleaseActiveTasks();
            ClearActivePath();
            PublishCurrentFacilityAccessProjection();
            // 共享工作位或唯一工具被另一居民预留是临时背压。多人候选在领用时可能失效，
            // 释放本次行动后回到有限频率决策；不能让一次正常争用永久阻断生理行动。
            if (blocker.Reason == ResourceFlowBlockReason.InteractionUnavailable)
            {
                SetResidentPhase(FoundationResidentPhase.Idle, "工作位或工具正在使用，稍后重新选择行动");
                _resident.State.LastBlocker.Value = "InteractionUnavailable · 等待共享工作位或工具";
                _resident.DecisionRetryRemaining = Mathf.Max(0.1f, residentDecisionRetrySeconds);
                return;
            }
            SetResidentPhase(FoundationResidentPhase.Blocked, task);
            _resident.State.LastBlocker.Value = blocker.IsBlocked
                ? $"{blocker.Reason} · {blocker.InventoryId} · {blocker.Resource}"
                : task;
        }

        private bool TrySelectDrinkingStation(
            bool requireDrinkServing,
            bool requireRestock,
            out FoundationFacilityState selectedStation,
            out ResourceInventory selectedInventory,
            out float bestPathLength)
        {
            selectedStation = default;
            selectedInventory = null;
            bestPathLength = float.PositiveInfinity;
            Vector3 residentPosition = ToNavigationPoint(_resident.State.ResidentLocalPosition.Value);
            IReadOnlyList<FoundationFacilityState> facilities = _model.Facilities;
            for (var i = 0; i < facilities.Count; i++)
            {
                FoundationFacilityState candidate = facilities[i];
                if (!_definitions.TryGetValue(
                        candidate.DefinitionId,
                        out NomadFacilityDefinition definition) ||
                    definition.Function != NomadFacilityFunction.DrinkingStation ||
                    !_drinkingStationInventories.TryGetValue(
                        candidate.InstanceId,
                        out ResourceInventory inventory))
                    continue;

                int water = inventory.GetAmount(NomadResourceIds.Water);
                if (requireDrinkServing &&
                    water < ResidentWaterCycle.DefaultDrinkServingMilliliters)
                    continue;
                int restockTarget = Math.Min(
                    DrinkingStationRestockTargetMilliliters,
                    inventory.Capacity);
                if (requireRestock &&
                    (water >= restockTarget || inventory.FreeCapacity <= 0))
                    continue;
                if (!TryMeasureFacilityPath(
                        residentPosition,
                        candidate,
                        definition,
                        out float pathLength,
                        DrinkAndDeliverInteractionGroupId) ||
                    pathLength >= bestPathLength)
                    continue;

                bestPathLength = pathLength;
                selectedStation = candidate;
                selectedInventory = inventory;
            }
            return selectedInventory != null;
        }

        private bool TrySelectReachableFacility(
            NomadFacilityFunction function,
            out FoundationFacilityState selected,
            out float bestPathLength,
            string interactionGroupId = "")
        {
            selected = default;
            bestPathLength = float.PositiveInfinity;
            Vector3 residentPosition = ToNavigationPoint(_resident.State.ResidentLocalPosition.Value);
            IReadOnlyList<FoundationFacilityState> facilities = _model.Facilities;
            for (var i = 0; i < facilities.Count; i++)
            {
                FoundationFacilityState candidate = facilities[i];
                if (!_definitions.TryGetValue(
                        candidate.DefinitionId,
                        out NomadFacilityDefinition definition) ||
                    definition.Function != function ||
                    !TryMeasureFacilityPath(
                        residentPosition,
                        candidate,
                        definition,
                        out float pathLength,
                        interactionGroupId) ||
                    pathLength >= bestPathLength)
                    continue;

                bestPathLength = pathLength;
                selected = candidate;
            }
            return bestPathLength < float.PositiveInfinity;
        }

        /// <summary>
        /// 任务候选只验证设施是否能从连续 NavMesh 到达，不读取瞬时 Slot 租约。
        /// 真正开始移动时会先释放居民自己的旧 Slot，再由 TryResumeActiveMove 原子取得新 Slot。
        /// </summary>
        private bool TryMeasureFacilityPath(
            Vector3 start,
            in FoundationFacilityState facility,
            NomadFacilityDefinition definition,
            out float bestPathLength,
            string interactionGroupId = "")
        {
            bestPathLength = float.PositiveInfinity;
            IReadOnlyList<NomadFacilityInteractionGroupDefinition> groups =
                definition.InteractionGroups;
            for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                NomadFacilityInteractionGroupDefinition group = groups[groupIndex];
                if (!string.IsNullOrEmpty(interactionGroupId) &&
                    !string.Equals(
                        group.GroupId,
                        interactionGroupId,
                        StringComparison.Ordinal))
                    continue;
                for (var slotIndex = 0; slotIndex < group.AlternativeSlots.Count; slotIndex++)
                {
                    DeckPose slotPose = group.AlternativeSlots[slotIndex].Resolve(facility.Pose);
                    if (!TryCalculateDockingPath(start, slotPose, out DeckNavPathProbe path) ||
                        path.PathLength >= bestPathLength)
                        continue;
                    bestPathLength = path.PathLength;
                }
            }
            return bestPathLength < float.PositiveInfinity;
        }

        private void AddFacilityInventory(in FoundationFacilityState facility)
        {
            if (!_definitions.TryGetValue(
                    facility.DefinitionId,
                    out NomadFacilityDefinition definition))
                return;

            if (definition.Function == NomadFacilityFunction.Toilet)
            {
                _toiletInventories.Add(facility.InstanceId, new ResourceInventory(
                    BuildToiletInventoryId(facility.InstanceId), ResourceMeasure.Milliliter,
                    Mathf.Max(1, toiletHoldingCapacityMilliliters)));
                return;
            }
            if (definition.Function != NomadFacilityFunction.DrinkingStation) return;

            _drinkingStationInventories.Add(
                facility.InstanceId,
                new ResourceInventory(
                    BuildDrinkingStationInventoryId(facility.InstanceId),
                    ResourceMeasure.Milliliter,
                    DrinkingStationCapacityMilliliters));
        }

        private void RemoveFacilityInventory(string facilityInstanceId)
        {
            if (string.IsNullOrWhiteSpace(facilityInstanceId)) return;
            _drinkingStationInventories.Remove(facilityInstanceId);
            _toiletInventories.Remove(facilityInstanceId);
        }

        /// <summary>
        /// 把设施 Authoring 中的局部区域解析成甲板世界姿态并登记。区域不是 NavMesh 障碍，
        /// 但其完整矩形已随设施请求进入功能净空，后建设施不能静默侵占。
        /// </summary>
        private void RegisterFacilityPlacementRegions(in FoundationFacilityState facility)
        {
            if (_worldItemPlacementLedger == null)
                throw new InvalidOperationException("世界物品放置账本尚未初始化。");
            if (!_definitions.TryGetValue(
                    facility.DefinitionId,
                    out NomadFacilityDefinition definition))
                throw new InvalidOperationException(
                    $"设施 {facility.InstanceId} 引用了未知定义 {facility.DefinitionId}。");

            IReadOnlyList<NomadPlacementRegionDefinition> regions = definition.PlacementRegions;
            for (var i = 0; i < regions.Count; i++)
            {
                PlacementRegionDefinition region = regions[i].CreateRegion(
                    facility.InstanceId,
                    facility.Pose);
                PlacementRegionFailure failure = _worldItemPlacementLedger.RegisterRegion(region);
                if (failure != PlacementRegionFailure.None)
                    throw new InvalidOperationException(
                        $"设施 {facility.InstanceId} 的放置区域 {region.LocalRegionId} " +
                        $"无法登记：{failure}。");
            }
        }

        private void RemoveFacilityPlacementRegions(
            string facilityInstanceId,
            string definitionId)
        {
            if (_worldItemPlacementLedger == null ||
                !_definitions.TryGetValue(definitionId, out NomadFacilityDefinition definition))
                return;

            IReadOnlyList<NomadPlacementRegionDefinition> regions = definition.PlacementRegions;
            for (var i = 0; i < regions.Count; i++)
            {
                string regionId = PlacementRegionLedger.ComposeRegionId(
                    facilityInstanceId,
                    regions[i].RegionId);
                if (_worldItemPlacementLedger.TryGetRegion(regionId, out _) &&
                    !_worldItemPlacementLedger.RemoveRegion(regionId))
                    throw new InvalidOperationException(
                        $"设施 {facilityInstanceId} 的放置区域 {regionId} 仍被物品或任务占用，不能回滚。");
            }
        }

        /// <summary>
        /// 第一座带中央台面区域的设施会提供一只空杯，作为区域容量、精确姿态与存档的最小运行样本。
        /// 杯子暂不接入饮水行动；它不会凭空复制到后续厨房。
        /// </summary>
        private void TryCreateStarterCupForFacility(in FoundationFacilityState facility)
        {
            if (_worldItemPlacementLedger.TryGetPlacement(StarterCupItemId, out _)) return;
            if (!_definitions.TryGetValue(
                    facility.DefinitionId,
                    out NomadFacilityDefinition facilityDefinition) ||
                !facilityDefinition.TryGetPlacementRegion(CountertopRegionLocalId, out _))
                return;

            string regionId = PlacementRegionLedger.ComposeRegionId(
                facility.InstanceId,
                CountertopRegionLocalId);
            if (!_worldItemPlacementLedger.TryRestorePlacement(
                    StarterCupItemId,
                    GetRequiredWorldItemFootprint(StarterCupDefinitionId),
                    regionId,
                    PlacementRegionPose.Centered,
                    out _,
                    out PlacementRegionFailure failure))
                throw new InvalidOperationException(
                    $"设施 {facility.InstanceId} 的初始杯子无法放到台面：{failure}。");
            PublishWorldItemPlacements();
        }

        /// <summary>
        /// 新局只在初始车辆水箱维护托盘上生成一份真实备件。读档路径只恢复存档中的物品，
        /// 不会因为维修包已经被消耗而偷偷补货。
        /// </summary>
        private void TryCreateStarterRepairKitForFacility(
            in FoundationFacilityState facility)
        {
            if (_worldItemPlacementLedger.TryGetPlacement(
                    WaterValveRepairKitItemId,
                    out _))
                return;
            if (!_definitions.TryGetValue(
                    facility.DefinitionId,
                    out NomadFacilityDefinition facilityDefinition) ||
                facilityDefinition.Function != NomadFacilityFunction.VehicleWaterTank ||
                !facilityDefinition.TryGetPlacementRegion(
                    MaintenanceTrayRegionLocalId,
                    out _))
                return;

            string regionId = PlacementRegionLedger.ComposeRegionId(
                facility.InstanceId,
                MaintenanceTrayRegionLocalId);
            if (!_worldItemPlacementLedger.TryRestorePlacement(
                    WaterValveRepairKitItemId,
                    GetRequiredWorldItemFootprint(WaterValveRepairKitDefinitionId),
                    regionId,
                    PlacementRegionPose.Centered,
                    out _,
                    out PlacementRegionFailure failure))
                throw new InvalidOperationException(
                    $"设施 {facility.InstanceId} 的初始维修包无法放到维护托盘：{failure}。");
            PublishWorldItemPlacements();
        }

        private void RemovePlacedItemsOwnedByFacility(string facilityInstanceId)
        {
            if (_worldItemPlacementLedger == null ||
                string.IsNullOrWhiteSpace(facilityInstanceId))
                return;

            IReadOnlyList<PlacementRegionItem> snapshot =
                _worldItemPlacementLedger.CreateStableSnapshot();
            for (var i = 0; i < snapshot.Count; i++)
            {
                PlacementRegionItem item = snapshot[i];
                if (!string.Equals(
                        item.Region.OwnerEntityId,
                        facilityInstanceId,
                        StringComparison.Ordinal))
                    continue;
                if (string.Equals(item.ItemId, WaterCanItemId, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"设施 {facilityInstanceId} 仍停放唯一水罐，不能回滚建造事务。");
                _worldItemPlacementLedger.RemoveItem(item.ItemId);
            }
            PublishWorldItemPlacements();
        }

        private PlacementFootprint GetRequiredWorldItemFootprint(string definitionId)
        {
            if (_worldItemFootprints.TryGetValue(definitionId, out PlacementFootprint footprint))
                return footprint;
            throw new InvalidOperationException($"缺少世界物品占地定义 '{definitionId}'。");
        }

        private void PublishWorldItemPlacements()
        {
            if (_model == null || _worldItemPlacementLedger == null) return;
            IReadOnlyList<PlacementRegionItem> snapshot =
                _worldItemPlacementLedger.CreateStableSnapshot();
            var projection = new FoundationItemPlacementState[snapshot.Count];
            for (var i = 0; i < snapshot.Count; i++)
                projection[i] = new FoundationItemPlacementState(snapshot[i]);
            _model.ReplaceWorldItemPlacements(projection);
        }

        private static string BuildDrinkingStationInventoryId(string facilityInstanceId) =>
            $"facility:{facilityInstanceId}:drinking-water";

        private bool HasPlacedFacility(NomadFacilityFunction function) =>
            TryFindPlacedFacility(function, out _, out _);

        private bool TryFindPlacedFacility(
            NomadFacilityFunction function,
            out FoundationFacilityState state,
            out ContinuousPlacedFacility placement)
        {
            IReadOnlyList<FoundationFacilityState> facilities = _model.Facilities;
            for (var i = 0; i < facilities.Count; i++)
            {
                FoundationFacilityState candidate = facilities[i];
                if (!_definitions.TryGetValue(
                        candidate.DefinitionId,
                        out NomadFacilityDefinition definition) ||
                    definition.Function != function)
                    continue;

                state = candidate;
                return _placementLedger.TryGetPlacement(candidate.InstanceId, out placement);
            }

            state = default;
            placement = null;
            return false;
        }

        private bool TryFindFacilityByInstanceId(
            string instanceId,
            NomadFacilityFunction expectedFunction,
            out FoundationFacilityState state)
        {
            if (!string.IsNullOrWhiteSpace(instanceId))
            {
                IReadOnlyList<FoundationFacilityState> facilities = _model.Facilities;
                for (var i = 0; i < facilities.Count; i++)
                {
                    FoundationFacilityState candidate = facilities[i];
                    if (!string.Equals(
                            candidate.InstanceId,
                            instanceId,
                            StringComparison.Ordinal) ||
                        !_definitions.TryGetValue(
                            candidate.DefinitionId,
                            out NomadFacilityDefinition definition) ||
                        definition.Function != expectedFunction)
                        continue;

                    state = candidate;
                    return true;
                }
            }

            state = default;
            return false;
        }

        private void ObservePhysiologyBackpressure(ResourceFlowBlocker blocker)
        {
            if (!blocker.IsBlocked ||
                blocker.Reason != ResourceFlowBlockReason.DestinationFull ||
                _model == null)
                return;

            // 代谢遇到满膀胱时不打断正在进行的安全动作，只把真实背压投影给 Inspector。
            // 下一次居民决策会优先生成如厕意图；没有厕所时仍可休闲和等待，不进入永久 Blocked。
            if (string.IsNullOrEmpty(_resident.State.LastBlocker.Value) ||
                _resident.State.LastBlocker.Value.StartsWith("PhysiologyBackpressure", StringComparison.Ordinal))
                _resident.State.LastBlocker.Value =
                    $"PhysiologyBackpressure · {blocker.InventoryId} · 需要如厕或清运";
        }

        private void WriteSimulationProjection()
        {
            WriteJourneyProjection();
            WriteStopWaterProjection();
            if (_resident.WaterCycle == null) return;
            NomadCalendarSnapshot calendar = CalendarPolicy.Project(
                _simulationClock.SimulationTick);
            SetLong(_model.SimulationTick, _simulationClock.SimulationTick);
            SetLong(_model.LifeDay, calendar.LifeDay);
            SetInt(_model.LifeMinuteOfDay, calendar.LifeMinuteOfDay);
            SetInt(_model.LifeDayProgressPermille, calendar.LifeDayProgressPermille);
            SetLong(_model.ClimateYear, calendar.ClimateYear);
            SetInt(_model.SeasonIndex, calendar.SeasonIndex);
            SetInt(_model.ClimateWeekInSeason, calendar.ClimateWeekInSeason);
            SetInt(_model.SeasonProgressPermille, calendar.SeasonProgressPermille);
            NomadEnvironmentSnapshot environment = EnvironmentSchedule.Project(
                worldSeed,
                _simulationClock.SimulationTick);
            if (_model.CurrentWeather.Value != environment.Weather)
                _model.CurrentWeather.Value = environment.Weather;
            SetInt(_model.SandstormIntensityPermille, environment.IntensityPermille);
            SetInt(
                _model.VehicleWaterMilliliters,
                _vehicleWater.GetAmount(NomadResourceIds.Water));
            SetInt(
                _model.WaterCanWaterMilliliters,
                _waterCan.GetAmount(NomadResourceIds.Water));
            WriteFacilityInventoryProjection();
            WriteFacilityConditionProjection();
            foreach (var resident in _residents)
            {
                using var scope = UseResident(resident);
                WriteResidentProjection();
            }
        }

        private void WriteResidentProjection()
        {
            if (_resident.WaterCycle == null) return;
            SetFloat(_resident.State.ResidentThirst, _resident.WaterCycle.Thirst);
            SetFloat(_resident.State.ResidentHealth, _resident.Wellbeing.Health);
            SetFloat(_resident.State.ResidentEntertainment, _resident.Wellbeing.Entertainment);
            SetFloat(_resident.State.ResidentMood, _resident.Wellbeing.Mood);
            SetFloat(_resident.State.ResidentFatigue, _resident.Wellbeing.Fatigue);
            SetFloat(_resident.State.ResidentStress, _resident.Wellbeing.Stress);
            SetFloat(
                _resident.State.ResidentWorkEfficiency,
                IsWorkTimedPhase(_resident.Phase)
                    ? _resident.WorkEfficiency
                    : CalculateExpectedWorkEfficiency());
            bool carryingWater = _waterCanLocation == FoundationWaterCanLocation.Resident &&
                                 _waterCanCarrierId == _resident.StableId &&
                                 _waterCan.GetAmount(NomadResourceIds.Water) > 0;
            if (_resident.State.ResidentCarryingWater.Value != carryingWater)
                _resident.State.ResidentCarryingWater.Value = carryingWater;
            SetInt(
                _resident.State.BodyWaterMilliliters,
                _resident.WaterCycle.BodyWater.GetAmount(NomadResourceIds.Water));
            SetInt(
                _resident.State.BladderWasteMilliliters,
                _resident.WaterCycle.Bladder.GetAmount(NomadResourceIds.HumanWaste));
        }

        private void WriteFacilityInventoryProjection()
        {
            var totalWater = 0;
            var totalCapacity = 0;
            var totalWaste = 0;
            var totalWasteCapacity = 0;
            _facilityInventoryProjection.Clear();
            IReadOnlyList<FoundationFacilityState> facilities = _model.Facilities;
            for (var i = 0; i < facilities.Count; i++)
            {
                FoundationFacilityState facility = facilities[i];
                if (_toiletInventories.TryGetValue(facility.InstanceId, out ResourceInventory toilet))
                {
                    int waste = toilet.GetAmount(NomadResourceIds.HumanWaste);
                    totalWaste = checked(totalWaste + waste);
                    totalWasteCapacity = checked(totalWasteCapacity + toilet.Capacity);
                    _facilityInventoryProjection.Add(new FoundationFacilityInventoryState(
                        facility.InstanceId, toilet.Id, "toilet-waste", NomadResourceIds.HumanWaste.Value,
                        toilet.Measure, waste, toilet.Capacity));
                }
                if (!_drinkingStationInventories.TryGetValue(
                        facility.InstanceId,
                        out ResourceInventory inventory))
                    continue;

                int amount = inventory.GetAmount(NomadResourceIds.Water);
                totalWater = checked(totalWater + amount);
                totalCapacity = checked(totalCapacity + inventory.Capacity);
                _facilityInventoryProjection.Add(new FoundationFacilityInventoryState(
                    facility.InstanceId,
                    inventory.Id,
                    "drinking-water",
                    NomadResourceIds.Water.Value,
                    inventory.Measure,
                    amount,
                    inventory.Capacity));
            }

            SetInt(_model.DrinkingStationWaterMilliliters, totalWater);
            SetInt(_model.DrinkingStationCapacityMilliliters, totalCapacity);
            SetInt(_model.ToiletHoldingWasteMilliliters, totalWaste);
            SetInt(_model.ToiletHoldingCapacityMilliliters, totalWasteCapacity);
            _facilityInventoryProjection.Sort(static (a, b) => string.CompareOrdinal(a.InventoryId, b.InventoryId));
            _model.ReplaceFacilityInventories(_facilityInventoryProjection);
        }

        private void ClearPlacementSelection(bool exitBuildMode)
        {
            _activePlacementDefinition = null;
            if (_model == null) return;
            _previewResidentOriginResolved = false;
            _previewResolvedResidentOrigin = default;
            _previewSharedSlotMask = 0UL;
            if (exitBuildMode)
                _model.InteractionMode.Value = FoundationInteractionMode.Observe;
            _model.PlacementPreview.Value = FoundationPlacementPreviewState.Inactive;
            PublishFacilityAccessProjection(previewEvaluated: false);
        }

        private void ClearPendingPlacement()
        {
            _navigationUpdate = null;
            _pendingPlacement = default;
            _pendingObstacle = null;
            _pendingResidentOriginResolved = false;
            _pendingResolvedResidentOrigin = default;
        }

        private static FoundationPlacementFailure MapPlacementFailure(
            ContinuousPlacementFailure failure) => failure switch
        {
            ContinuousPlacementFailure.None => FoundationPlacementFailure.None,
            ContinuousPlacementFailure.InvalidRequest => FoundationPlacementFailure.InvalidRequest,
            ContinuousPlacementFailure.DuplicateInstanceId =>
                FoundationPlacementFailure.DuplicateInstanceId,
            ContinuousPlacementFailure.DeckLevelUnavailable =>
                FoundationPlacementFailure.DeckLevelUnavailable,
            ContinuousPlacementFailure.FootprintOutOfBounds =>
                FoundationPlacementFailure.FootprintOutOfBounds,
            ContinuousPlacementFailure.FootprintOverlapsFacility =>
                FoundationPlacementFailure.FootprintOverlapsFacility,
            ContinuousPlacementFailure.FunctionalClearanceOutOfBounds =>
                FoundationPlacementFailure.FunctionalClearanceOutOfBounds,
            ContinuousPlacementFailure.FunctionalClearanceOverlapsFacility =>
                FoundationPlacementFailure.FunctionalClearanceOverlapsFacility,
            _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, null),
        };

        /// <summary>
        /// 一次决策可能同时发现多个不可执行方案。这里只选择最值得展示的一条诊断，
        /// 不改变候选可行性或 Utility；同层紧迫度相同则保留稳定的候选构造顺序。
        /// </summary>
        private struct PendingDecisionDiagnostic
        {
            public string Message { get; private set; }
            public ResidentDecisionRiskTier RiskTier { get; private set; }
            public float RiskPriority { get; private set; }
            public bool HasValue => !string.IsNullOrEmpty(Message);

            public void Consider(
                string message,
                ResidentDecisionRiskTier riskTier,
                float riskPriority)
            {
                if (string.IsNullOrWhiteSpace(message)) return;
                float normalizedPriority = Mathf.Clamp01(riskPriority);
                if (HasValue &&
                    (riskTier < RiskTier ||
                     riskTier == RiskTier && normalizedPriority <= RiskPriority))
                    return;

                Message = message;
                RiskTier = riskTier;
                RiskPriority = normalizedPriority;
            }
        }

        private readonly struct FacilityAccessEvaluation
        {
            public FacilityAccessEvaluation(
                FoundationFacilityAccess access,
                ulong reachableSlotMask,
                int interactionSlotCount)
            {
                Access = access;
                ReachableSlotMask = reachableSlotMask;
                InteractionSlotCount = interactionSlotCount;
            }

            public FoundationFacilityAccess Access { get; }
            public ulong ReachableSlotMask { get; }
            public int InteractionSlotCount { get; }

            public static FacilityAccessEvaluation Unknown => new(
                FoundationFacilityAccess.Unknown,
                0UL,
                0);

            public static FacilityAccessEvaluation NoInteractionRequired => new(
                FoundationFacilityAccess.Reachable,
                0UL,
                0);

            public static FacilityAccessEvaluation Unreachable(int interactionSlotCount) => new(
                FoundationFacilityAccess.Unreachable,
                0UL,
                interactionSlotCount);
        }

        private static Vector3 ToNavigationPoint(Vector3 localPosition) =>
            new(localPosition.x, 0f, localPosition.z);

        private static float HorizontalDistance(Vector3 left, Vector3 right)
        {
            float x = left.x - right.x;
            float z = left.z - right.z;
            return Mathf.Sqrt(x * x + z * z);
        }

        private static void SetFloat(R3.RP<float> property, float value)
        {
            if (!Mathf.Approximately(property.Value, value)) property.Value = value;
        }

        private static void SetInt(R3.RP<int> property, int value)
        {
            if (property.Value != value) property.Value = value;
        }

        private static void SetLong(R3.RP<long> property, long value)
        {
            if (property.Value != value) property.Value = value;
        }

        private void SetWaterCanLocation(
            FoundationWaterCanLocation location,
            string anchorFacilityInstanceId = "",
            string savedRegionId = "",
            PlacementRegionPose? savedLocalPose = null)
        {
            string anchor = anchorFacilityInstanceId?.Trim() ?? string.Empty;
            if (location == FoundationWaterCanLocation.Resident)
            {
                _worldItemPlacementLedger?.RemoveItem(WaterCanItemId);
                _waterCanPlacement = null;
                anchor = string.Empty;
            }
            else
            {
                if (_worldItemPlacementLedger == null)
                    throw new InvalidOperationException("世界物品放置账本尚未初始化。");
                if (string.IsNullOrWhiteSpace(anchor))
                    throw new InvalidOperationException("非携带状态的水罐必须绑定精确设施实例。");

                string expectedRegionId = PlacementRegionLedger.ComposeRegionId(
                    anchor,
                    WaterCanParkingRegionLocalId);
                string regionId = string.IsNullOrWhiteSpace(savedRegionId)
                    ? expectedRegionId
                    : savedRegionId.Trim();
                if (!string.Equals(regionId, expectedRegionId, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"水罐存档区域 {regionId} 不属于锚点设施 {anchor} 的 " +
                        $"{WaterCanParkingRegionLocalId}。");

                if (_worldItemPlacementLedger.TryGetPlacement(
                        WaterCanItemId,
                        out PlacementRegionItem existing))
                {
                    bool samePose = string.Equals(
                                        existing.Region.RegionId,
                                        regionId,
                                        StringComparison.Ordinal) &&
                                    (!savedLocalPose.HasValue ||
                                     existing.LocalPose == savedLocalPose.Value);
                    if (!samePose)
                        throw new InvalidOperationException(
                            "水罐仍占用旧放置区域；必须先由居民真实取走，不能在设施间瞬移。");
                    _waterCanPlacement = existing;
                }
                else
                {
                    PlacementRegionReservation reservation;
                    PlacementRegionFailure failure;
                    bool reserved = savedLocalPose.HasValue
                        ? _worldItemPlacementLedger.TryReserveExact(
                            WaterCanItemId,
                            GetRequiredWorldItemFootprint(WaterCanDefinitionId),
                            regionId,
                            savedLocalPose.Value,
                            out reservation,
                            out failure)
                        : _worldItemPlacementLedger.TryReserveStable(
                            WaterCanItemId,
                            GetRequiredWorldItemFootprint(WaterCanDefinitionId),
                            regionId,
                            out reservation,
                            out failure);
                    if (!reserved)
                        throw new InvalidOperationException(
                            $"水罐无法放入区域 {regionId}：{failure}。");
                    using (reservation)
                    {
                        if (!reservation.TryCommit(out _waterCanPlacement, out failure))
                            throw new InvalidOperationException(
                                $"水罐区域预留 {regionId} 提交失败：{failure}。");
                    }
                }
            }

            _waterCanCarrierId = location == FoundationWaterCanLocation.Resident
                ? _resident.StableId : string.Empty;
            _model.WaterCanCarrierId.Value = _waterCanCarrierId;
            _waterCanLocation = location;
            _waterCanAnchorFacilityInstanceId = anchor;
            if (_model == null) return;
            if (_model.WaterCanLocation.Value != location)
                _model.WaterCanLocation.Value = location;
            if (!string.Equals(
                    _model.WaterCanAnchorFacilityInstanceId.Value,
                    _waterCanAnchorFacilityInstanceId,
                    StringComparison.Ordinal))
                _model.WaterCanAnchorFacilityInstanceId.Value =
                    _waterCanAnchorFacilityInstanceId;
            FoundationItemPlacementState placementState = _waterCanPlacement == null
                ? default
                : new FoundationItemPlacementState(_waterCanPlacement);
            if (!_model.WaterCanPlacement.Value.Equals(placementState))
                _model.WaterCanPlacement.Value = placementState;
            PublishWorldItemPlacements();
        }

        private void ReleaseActiveTasks()
        {
            if (_resident == null) return;
            bool worldItemsChanged = _resident.CancelAction();
            if (worldItemsChanged && _model != null)
            {
                _resident.State.ResidentCarriedWorldItem.Value = default;
                PublishWorldItemPlacements();
            }
            ClearActivePath();
        }

        protected override void OnDestroy()
        {
            RevokeCheckpointOperation(publish: false);
            DisposeResidentExecutions(publishProjection: false);
            _interactionSpaces?.Dispose();
            _journey?.Dispose();
            if (_navigation != null)
            {
                _navigation.DeactivateAndDestroyObstacle(_pendingObstacle);
                foreach (DeckNavigationObstacleHandle obstacle in _navigationObstacles.Values)
                    _navigation.DeactivateAndDestroyObstacle(obstacle);
            }
            _navigationObstacles.Clear();
            base.OnDestroy();
        }
    }
}
