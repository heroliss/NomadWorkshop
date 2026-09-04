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
    public sealed class NomadFoundationSystem : MonoSystemBase
    {
        private const ulong ResidentOwnerId = 0xF01UL;
        private const float DrinkNeedThreshold = 0.55f;
        private const float ToiletNeedThreshold = 0.58f;
        private const float ResidentVisualHeight = 0.55f;
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

        [Header("共享定义")]
        [SerializeField, Tooltip("车辆主甲板尺寸、业务坐标与默认吸附参数。")]
        private DeckLayoutDefinition deckLayout;
        [SerializeField, Tooltip("本切片允许建造或预置的设施定义；运行时按稳定 id 建立目录。")]
        private NomadFacilityDefinition[] facilityDefinitions =
            Array.Empty<NomadFacilityDefinition>();

        [Header("居民灰盒节奏")]
        [SerializeField, Tooltip("居民的甲板局部出生位置；Y 只用于灰盒表现，寻路使用 X/Z。")]
        private Vector3 residentStartLocalPosition = new(0f, ResidentVisualHeight, 2.4f);
        [SerializeField, Min(0.1f), Tooltip("居民沿连续 NavMesh 路径移动的米/秒。")]
        private float residentMoveSpeed = 2.8f;
        [SerializeField, Min(0.01f), Tooltip("取得容器或完成一次装水动作的灰盒时长；搬运毫升数不直接线性放大动画时间。")]
        private float pickupSeconds = 0.45f;
        [SerializeField, Min(0.01f), Tooltip("把容器中的物资交付到设施库存的灰盒动作时长。")]
        private float deliverySeconds = 0.45f;
        [SerializeField, Min(0.01f), Tooltip("居民在饮水站完成一次饮水的灰盒动作时长。")]
        private float drinkingSeconds = 1.2f;
        [SerializeField, Min(0.01f), Tooltip("居民在旱厕完成一次排泄物转移的灰盒动作时长。")]
        private float toiletSeconds = 1.4f;
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

        [Header("空闲休闲灰盒节奏")]
        [SerializeField, Min(0.1f), Tooltip("发呆或散步到达后的停留时长；当前短循环用于更快观察行为分布。")]
        private float leisureSeconds = 1.2f;
        [SerializeField, Min(0f), Tooltip("每模拟秒积累的娱乐缺口；休闲候选会按各自恢复量降低它。")]
        private float recreationGrowthPerSecond = 0.01f;
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
        [SerializeField, Min(1), Tooltip("当前共享旱厕暂存桶容量（mL）；满后必须等待未来清运链，不会吞掉排泄物。")]
        private int toiletHoldingCapacityMilliliters =
            DefaultToiletHoldingCapacityMilliliters;

        private readonly Dictionary<string, NomadFacilityDefinition> _definitions =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, DeckNavigationObstacleHandle> _navigationObstacles =
            new(StringComparer.Ordinal);
        private readonly List<ContinuousPlacedFacility> _previewCommittedFacilities = new();
        private readonly Dictionary<string, FacilityAccessEvaluation> _committedFacilityAccess =
            new(StringComparer.Ordinal);
        private readonly List<FoundationFacilityAccessState> _facilityAccessProjection = new();

        private NomadFoundationModel _model;
        private DeckNavigationUtility _navigation;
        private ContinuousFacilityPlacementLedger _placementLedger;
        private ContinuousDeckReachabilityProbe _previewReachabilityProbe;
        private FoundationInteractionSpaceRuntime _interactionSpaces;
        private DeckPlacementSnapSettings _snapSettings;
        private NomadFacilityDefinition _activePlacementDefinition;
        private DeckPose _previewPose;
        private int _placementBeganFrame;
        private int _nextFacilitySequence;
        private int _waterTaskSequence;
        private int _leisureSequence;
        private long _residentDecisionSequence;
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
        private ResourceInventory _drinkingStation;
        private ResourceInventory _toiletHolding;
        private ResidentWaterCycle _residentWaterCycle;
        private readonly ResidentActionPlanEvaluator _actionPlanEvaluator = new();
        private readonly ResidentActionPlanPolicy _actionPlanPolicy = new();
        private readonly UtilityDecisionEngine _decisionEngine = new();
        private HaulTaskLease _activeHaul;
        private ResidentWaterActionLease _activeResidentAction;
        private FoundationWaterCanLocation _waterCanLocation;
        private bool _waterCanPickupWasAtSource;
        private bool _drinkAfterActiveHaul;
        private FoundationResidentPhase _residentPhase;
        private FoundationResidentMoveIntent _activeMoveIntent;
        private bool _hasActiveMoveIntent;
        private Vector3[] _activePathCorners = Array.Empty<Vector3>();
        private float _activeDockingYawDegrees;
        private bool _hasActiveDockingYaw;
        private int _nextPathCornerIndex;
        private float _routeRetryRemaining;
        private float _phaseDuration;
        private float _phaseRemaining;
        private float _residentRecreation;
        private float _activeLeisureRestore;
        private FoundationLeisureKind _activeLeisureKind;
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
            float deltaTime = Time.unscaledDeltaTime * clampedSpeed;
            ResidentWaterCycleTick physiologyTick = _residentWaterCycle.Advance(
                deltaTime,
                _resourceFlow);
            ObservePhysiologyBackpressure(physiologyTick.Blocker);
            _residentRecreation = Mathf.Clamp01(
                _residentRecreation + recreationGrowthPerSecond * deltaTime);

            switch (_residentPhase)
            {
                case FoundationResidentPhase.WaitingForRoute:
                    _routeRetryRemaining -= deltaTime;
                    if (_routeRetryRemaining <= 0f) TryResumeActiveMove();
                    break;
                case FoundationResidentPhase.WaitingForFacility:
                case FoundationResidentPhase.Idle:
                    TryStartResidentRoutine();
                    break;
                case FoundationResidentPhase.MovingToWaterCan:
                    if (AdvanceResidentAlongPath(deltaTime))
                    {
                        ClearActiveMoveIntent();
                        BeginTimedPhase(
                            FoundationResidentPhase.PickingUpWaterCan,
                            pickupSeconds,
                            "取得唯一防漏水罐");
                    }
                    break;
                case FoundationResidentPhase.PickingUpWaterCan:
                    if (TickTimer(deltaTime)) CompleteWaterCanPickup();
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
                        if (_activeHaul != null)
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
            }

            WriteSimulationProjection();
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
                _model.CurrentTask.Value = "等待导航事务安全回滚后退出建造模式";
                return;
            }

            ClearPlacementSelection(exitBuildMode: true);
        }

        public void BeginPlacement(string definitionId)
        {
            if (!_initialized ||
                _model.BuildTransactionPhase.Value != FoundationBuildTransactionPhase.Idle ||
                string.IsNullOrWhiteSpace(definitionId) ||
                !_definitions.TryGetValue(definitionId, out NomadFacilityDefinition definition) ||
                !definition.Buildable)
                return;

            _activePlacementDefinition = definition;
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
                _model.CurrentTask.Value = "正在验证新设施对连续导航与交互位的影响";
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
                _model.LastBlocker.Value = exception.Message;
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
                _model.CurrentTask.Value = "等待当前导航更新完成后取消候选设施";
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
            if (_model != null) _model.IsPaused.Value = paused;
        }

        public void SetSimulationSpeed(float speed)
        {
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
                _model.CurrentTask.Value = "等待导航事务回滚后复位";
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
        }

#if UNITY_EDITOR
        /// <summary>只供未激活 GameObject 上的隔离装配；必须在 Awake 前调用。</summary>
        public void ConfigureDefinitionsForTests(
            DeckLayoutDefinition configuredLayout,
            NomadFacilityDefinition[] configuredDefinitions)
        {
            if (_initialized)
                throw new InvalidOperationException("测试定义必须在 NomadFoundationSystem.Awake 前配置。");
            deckLayout = configuredLayout;
            facilityDefinitions = configuredDefinitions;
        }

        /// <summary>隔离测试可替换容器能力与洁净度，再经 ResetScenario 重建同一业务起点。</summary>
        public void ConfigureWaterCanForTests(
            CargoContainerCapability capabilities,
            float cleanliness)
        {
            waterCanCapabilities = capabilities;
            waterCanCleanliness = Mathf.Clamp01(cleanliness);
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
#endif

        private void ResetScenarioNow()
        {
            if (_model == null || _navigation == null) return;

            ReleaseActiveTasks();
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
            _committedFacilityAccess.Clear();
            _facilityAccessProjection.Clear();
            _model.ReplaceFacilityAccess(_facilityAccessProjection);

            _placementLedger = deckLayout.CreatePlacementLedger();
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
            _waterTaskSequence = 0;
            _leisureSequence = 0;
            _residentDecisionSequence = 0;
            _routeRetryRemaining = 0f;

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
                    definition.CreateFootprint());
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
                initialFacilities.Add(new FoundationFacilityState(
                    instanceId,
                    definition.Id,
                    request.Pose));
            }
            _model.ReplaceFacilities(initialFacilities);
            _model.ResidentLocalPosition.Value = residentStartLocalPosition;
            _model.ResidentLocalYawDegrees.Value = 180f;

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
            _waterCanLocation = FoundationWaterCanLocation.VehicleWaterTank;
            _waterCanPickupWasAtSource = false;
            _residentRecreation = 0.32f;
            _activeLeisureRestore = 0f;
            _drinkingStation = new ResourceInventory(
                "drinking-station",
                ResourceMeasure.Milliliter,
                DrinkingStationCapacityMilliliters);
            _toiletHolding = new ResourceInventory(
                "toilet-holding",
                ResourceMeasure.Milliliter,
                Mathf.Max(1, toiletHoldingCapacityMilliliters));
            _residentWaterCycle = new ResidentWaterCycle(
                "resident-01",
                ResidentOwnerId,
                ResidentWaterCycle.DefaultDrinkServingMilliliters /
                Mathf.Max(0.01f, drinkMetabolismSeconds),
                drinkServingMilliliters: ResidentWaterCycle.DefaultDrinkServingMilliliters,
                initialThirst: initialThirst,
                thirstIncreasePerSecond: thirstIncreasePerSecond,
                thirstReliefPerServing: 0.72f,
                bodyWaterCapacityMilliliters: bodyWaterCapacityMilliliters,
                bladderCapacityMilliliters: bladderCapacityMilliliters);

            _model.SimulationSpeed.Value = initialSimulationSpeed;
            _model.ResidentCarryingWater.Value = false;
            _model.WaterCanLocation.Value = _waterCanLocation;
            _model.WaterCanWaterMilliliters.Value = 0;
            _model.WaterCanCapacityMilliliters.Value = _waterCan.Capacity;
            _model.VehicleWaterCapacityMilliliters.Value = _vehicleWater.Capacity;
            _model.DrinkingStationCapacityMilliliters.Value = _drinkingStation.Capacity;
            _model.BodyWaterCapacityMilliliters.Value = _residentWaterCycle.BodyWater.Capacity;
            _model.BladderCapacityMilliliters.Value = _residentWaterCycle.Bladder.Capacity;
            _model.ToiletHoldingCapacityMilliliters.Value = _toiletHolding.Capacity;
            _model.LatestActionPlan.Value = FoundationActionPlanProjection.None;
            _model.CompletedDrinkCount.Value = 0;
            _model.CompletedToiletUseCount.Value = 0;
            _model.CompletedLeisureCount.Value = 0;
            _model.CompletedDaydreamCount.Value = 0;
            _model.CompletedWanderCount.Value = 0;
            _model.LastBlocker.Value = string.Empty;
            _model.ActionProgress.Value = 0f;
            _activeLeisureKind = FoundationLeisureKind.None;
            ClearPlacementSelection(exitBuildMode: true);
            SetResidentPhase(
                HasPlacedFacility(NomadFacilityFunction.DrinkingStation)
                    ? FoundationResidentPhase.Idle
                    : FoundationResidentPhase.WaitingForFacility,
                "等待玩家建造饮水站");
            WriteSimulationProjection();

            _initialized = true;
            _model.IsReady.Value = true;
        }

        private void ValidateAndBuildCatalog()
        {
            if (deckLayout == null)
                throw new MissingReferenceException("NomadFoundationSystem 缺少 DeckLayoutDefinition。");
            if (facilityDefinitions == null || facilityDefinitions.Length == 0)
                throw new MissingReferenceException("NomadFoundationSystem 至少需要一个设施定义。");

            _definitions.Clear();
            var functions = new HashSet<NomadFacilityFunction>();
            for (var i = 0; i < facilityDefinitions.Length; i++)
            {
                NomadFacilityDefinition definition = facilityDefinitions[i];
                if (definition == null)
                    throw new MissingReferenceException($"设施定义数组第 {i} 项为空。");
                definition.ValidateOrThrow();
                if (!_definitions.TryAdd(definition.Id, definition))
                    throw new InvalidOperationException($"设施稳定 id '{definition.Id}' 重复。");
                definition.CreateFootprint();
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
                ToNavigationPoint(_model.ResidentLocalPosition.Value));
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
                reacquireActiveSpace,
                ResidentOwnerId);
        }

        private bool TryResolveResidentNavigationOrigin(out Vector3 resolved)
        {
            Vector3 current = ToNavigationPoint(_model.ResidentLocalPosition.Value);
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
            Vector3 current = ToNavigationPoint(_model.ResidentLocalPosition.Value);
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
            sampled.y = ResidentVisualHeight;
            _model.ResidentLocalPosition.Value = sampled;
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
                        residentHasPosition = EnsureResidentHasNavigablePosition(
                            _pendingResidentOriginResolved,
                            _pendingResolvedResidentOrigin,
                            out residentRelocated);
                        RefreshCommittedFacilityAccess();
                    }
                    catch (Exception exception)
                    {
                        RollbackPartiallyCommittedPlacementTruth();
                        _model.LastBlocker.Value = exception.Message;
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
            _model.AddFacility(new FoundationFacilityState(
                _pendingPlacement.InstanceId,
                _pendingPlacement.DefinitionId,
                _pendingPlacement.Pose));
        }

        private void RollbackPartiallyCommittedPlacementTruth()
        {
            string instanceId = _pendingPlacement.InstanceId;
            if (string.IsNullOrWhiteSpace(instanceId)) return;
            _placementLedger.Remove(instanceId);
            _navigationObstacles.Remove(instanceId);
            _model.RemoveFacility(instanceId);
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
            ReplanActiveMoveAfterPlacement();
            if (_residentPhase is FoundationResidentPhase.Idle or
                FoundationResidentPhase.WaitingForFacility)
                _model.CurrentTask.Value = residentRelocated
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
                _model.CurrentTask.Value = "候选无效，正在恢复提交前的导航数据";
            }
            catch (Exception exception)
            {
                _model.LastBlocker.Value = exception.Message;
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
            RefreshCommittedFacilityAccess();
            if (cancel)
            {
                ClearPlacementSelection(exitBuildMode);
                return;
            }

            SetPreview(failure);
            _model.CurrentTask.Value = failure == FoundationPlacementFailure.None
                ? "候选设施已取消"
                : "候选设施未通过连续导航验证";
        }

        private bool AreAllRequiredInteractionsReachable(
            ContinuousFacilityPlacementRequest? additionalPlacement,
            NomadFacilityDefinition additionalDefinition)
        {
            Vector3 anchor = ToNavigationPoint(residentStartLocalPosition);
            Vector3 resident = ToNavigationPoint(_model.ResidentLocalPosition.Value);
            if (!TryCalculatePath(
                    anchor,
                    resident,
                    MaximumTravelSampleOffset,
                    _dockingNavMeshTolerance,
                    out _))
                return false;

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
            _activePlacementDefinition.CreateFootprint());

        private void TryStartResidentRoutine()
        {
            // 排泄压力优先于继续摄入水。这样“身体库存满”会生成可解释的厕所需求，
            // 而不是把 ResourceFlow 的 DestinationFull 当作永久 AI 终态。
            if (TryStartToiletRoutine()) return;
            TryStartWaterRoutine();
        }

        private bool TryStartToiletRoutine()
        {
            int waste = _residentWaterCycle.Bladder.GetAmount(NomadResourceIds.HumanWaste);
            if (waste <= 0 || _residentWaterCycle.ExcretionPressure < ToiletNeedThreshold)
                return false;

            if (!TryFindPlacedFacility(
                    NomadFacilityFunction.Toilet,
                    out FoundationFacilityState toilet,
                    out _))
            {
                if (_residentWaterCycle.ExcretionPressure < 0.9f) return false;

                _model.LastBlocker.Value =
                    "PhysiologyBackpressure · 膀胱接近满载，需要建造可达旱厕";
                TryStartLeisureRoutine();
                return true;
            }

            if (!_residentWaterCycle.TryReserveToiletUse(
                    _resourceFlow,
                    _toiletHolding,
                    $"toilet:{_waterTaskSequence + 1}",
                    $"facility:{toilet.InstanceId}:toilet-use",
                    waste,
                    out _activeResidentAction,
                    out ResourceFlowBlocker blocker))
            {
                if (blocker.Reason == ResourceFlowBlockReason.DestinationFull)
                {
                    _model.LastBlocker.Value =
                        $"{blocker.Reason} · {blocker.InventoryId} · 旱厕暂存桶需要清运";
                    TryStartLeisureRoutine();
                    return true;
                }

                Block("无法开始如厕", blocker);
                return true;
            }

            _model.LastBlocker.Value = string.Empty;
            TryBeginMove(
                FoundationResidentPhase.MovingToToilet,
                "排泄压力升高：前往可达旱厕",
                toilet);
            return true;
        }

        private void TryStartWaterRoutine()
        {
            bool needsDrink = _residentWaterCycle.Thirst >= DrinkNeedThreshold;
            if (needsDrink && _residentWaterCycle.BodyWater.FreeCapacity <= 0)
            {
                bool bladderFull = _residentWaterCycle.Bladder.FreeCapacity <= 0;
                _model.LastBlocker.Value = bladderFull
                    ? "DestinationFull · resident-01:body-water · 膀胱已满，需要可达旱厕"
                    : "DestinationFull · resident-01:body-water · 正在等待体内水继续代谢";
                TryStartLeisureRoutine();
                return;
            }
            if (!TryFindPlacedFacility(
                    NomadFacilityFunction.DrinkingStation,
                    out FoundationFacilityState station,
                    out _))
            {
                if (needsDrink)
                    SetResidentPhase(
                        FoundationResidentPhase.WaitingForFacility,
                        "口渴：等待玩家建造可达的饮水站");
                else
                    TryStartLeisureRoutine();
                return;
            }

            int stationWater = _drinkingStation.GetAmount(NomadResourceIds.Water);
            int restockTarget = Math.Min(
                DrinkingStationRestockTargetMilliliters,
                _drinkingStation.Capacity);
            if (needsDrink && stationWater > 0)
            {
                _model.LastBlocker.Value = string.Empty;
                BeginDrink(station);
                return;
            }

            if (!needsDrink && stationWater >= restockTarget)
            {
                TryStartLeisureRoutine();
                return;
            }

            if (!TryFindPlacedFacility(
                    NomadFacilityFunction.VehicleWaterTank,
                    out FoundationFacilityState source,
                    out _))
            {
                if (needsDrink) Block("找不到车辆水箱设施", ResourceFlowBlocker.None);
                else TryStartLeisureRoutine();
                return;
            }

            if (_vehicleWater.GetAmount(NomadResourceIds.Water) <= 0)
            {
                if (needsDrink) Block("车辆水箱已经没有可饮用水", ResourceFlowBlocker.None);
                else TryStartLeisureRoutine();
                return;
            }

            TryStartWaterRestock(source, station, needsDrink);
        }

        private void TryStartWaterRestock(
            in FoundationFacilityState source,
            in FoundationFacilityState station,
            bool drinkAfterDelivery)
        {
            int haulMilliliters = CalculateWaterHaulMilliliters(drinkAfterDelivery);
            if (haulMilliliters <= 0)
            {
                if (drinkAfterDelivery)
                    Block("当前没有可装入水罐并送达饮水站的水量", ResourceFlowBlocker.None);
                else
                    TryStartLeisureRoutine();
                return;
            }

            ResidentActionPlanEvaluation plan = EvaluateWaterRestockPlan(
                source,
                station,
                drinkAfterDelivery,
                haulMilliliters,
                out FoundationFacilityState waterCanFacility,
                out bool waterCanAtSource);
            ResidentDecisionResult decision = DecideWaterRestockPlan(plan);
            WriteLatestActionPlan(plan, decision);

            if (!plan.Feasibility.IsFeasible)
            {
                Block(plan.Candidate.BlockReason, ResourceFlowBlocker.None);
                return;
            }

            if (!decision.HasSelection)
            {
                SetResidentPhase(
                    FoundationResidentPhase.Idle,
                    "补水完整方案的净效用暂时不足，稍后重新评估");
                return;
            }

            _waterTaskSequence++;
            var request = new HaulTaskRequest(
                $"water-haul:{_waterTaskSequence}",
                ResidentOwnerId,
                _vehicleWater,
                _waterCan,
                _drinkingStation,
                NomadResourceIds.Water,
                haulMilliliters,
                drinkAfterDelivery
                    ? "居民为迫切饮水取得防漏水罐，把水从车辆水箱搬到饮水站"
                    : "居民在空闲时取得防漏水罐，低优先级补充饮水站库存",
                new[]
                {
                    $"facility:{source.InstanceId}:water-pickup",
                    $"facility:{station.InstanceId}:water-delivery",
                    $"carrier:{_waterCan.Id}",
                });

            if (!_resourceFlow.TryReserveHaul(request, out _activeHaul, out ResourceFlowBlocker blocker))
            {
                if (drinkAfterDelivery) Block("无法领取紧急搬水任务", blocker);
                else SetResidentPhase(FoundationResidentPhase.Idle, "例行补水当前不可领取，稍后重试");
                return;
            }

            _drinkAfterActiveHaul = drinkAfterDelivery;
            _waterCanPickupWasAtSource = waterCanAtSource;
            if (_waterCanLocation == FoundationWaterCanLocation.Resident)
            {
                TryBeginMove(
                    FoundationResidentPhase.MovingToWaterSource,
                    "已持有空水罐：沿连续 NavMesh 路径前往车辆水箱",
                    source);
                return;
            }

            TryBeginMove(
                FoundationResidentPhase.MovingToWaterCan,
                drinkAfterDelivery
                    ? "紧急补水：先前往唯一防漏水罐所在位置"
                    : "低优先级补货：先前往唯一防漏水罐所在位置",
                waterCanFacility,
                allowAlternativeFacility: false);
        }

        private int CalculateWaterHaulMilliliters(bool drinkAfterDelivery)
        {
            int desiredMilliliters = drinkAfterDelivery
                ? WaterHaulBatchMilliliters
                : Math.Max(
                    0,
                    DrinkingStationRestockTargetMilliliters -
                    _drinkingStation.GetAmount(NomadResourceIds.Water));
            return Math.Min(
                desiredMilliliters,
                Math.Min(
                    _vehicleWater.GetAmount(NomadResourceIds.Water),
                    Math.Min(_waterCan.FreeCapacity, _drinkingStation.FreeCapacity)));
        }

        private void TryStartLeisureRoutine()
        {
            var condition = new ResidentDecisionCondition(motionSickness: 0f);
            var evaluations = new List<ResidentActionPlanEvaluation>(2);
            bool hasWanderTarget = TrySelectWanderTarget(
                out Vector3 wanderTarget,
                out DeckNavPathProbe wanderPath,
                out string wanderLabel);
            if (hasWanderTarget)
            {
                evaluations.Add(_actionPlanEvaluator.Evaluate(
                    ResidentLeisurePlanFactory.CreateWander(
                        wanderPath.PathLength,
                        Mathf.Max(0.1f, residentMoveSpeed),
                        leisureSeconds,
                        recreationRestore: 0.32f,
                        targetLabel: wanderLabel),
                    condition,
                    _actionPlanPolicy));
            }
            evaluations.Add(_actionPlanEvaluator.Evaluate(
                ResidentLeisurePlanFactory.CreateDaydream(
                    leisureSeconds,
                    recreationRestore: 0.22f),
                condition,
                _actionPlanPolicy));

            var candidates = new ResidentActionCandidate[evaluations.Count];
            for (var i = 0; i < evaluations.Count; i++)
                candidates[i] = evaluations[i].Candidate;
            var decision = new ResidentDecisionContext(
                worldSeed: 1729,
                residentId: ResidentOwnerId,
                decisionSequence: ++_residentDecisionSequence,
                needs: new[]
                {
                    new ResidentNeedState(
                        ResidentNeed.Recreation,
                        _residentRecreation,
                        recreationGrowthPerSecond,
                        importance: 0.75f),
                },
                candidates: candidates);
            ResidentDecisionResult result = _decisionEngine.Decide(decision);
            ResidentActionPlanEvaluation selectedPlan = FindSelectedPlan(
                evaluations,
                result.Selected);
            if (selectedPlan == null)
            {
                SetResidentPhase(
                    FoundationResidentPhase.Idle,
                    "休闲候选的净效用暂时不足，稍后重新评估");
                return;
            }

            WriteLatestActionPlan(selectedPlan, result);
            _activeLeisureRestore = GetNeedRestore(
                selectedPlan.Candidate,
                ResidentNeed.Recreation);
            _leisureSequence++;
            if (string.Equals(
                    selectedPlan.Candidate.Id,
                    ResidentLeisurePlanFactory.WanderCandidateId,
                    StringComparison.Ordinal) &&
                hasWanderTarget)
            {
                _activeLeisureKind = FoundationLeisureKind.Wander;
                if (!TryAssignTravelPath(wanderTarget))
                {
                    _activeLeisureRestore = 0f;
                    _activeLeisureKind = FoundationLeisureKind.None;
                    SetResidentPhase(
                        FoundationResidentPhase.Idle,
                        "散步目标刚刚失效，稍后重新选择空地");
                    return;
                }
                SetResidentPhase(
                    FoundationResidentPhase.MovingToLeisure,
                    $"没有必要任务：沿连续 NavMesh 散步到 {wanderLabel}");
                return;
            }

            _activeLeisureKind = FoundationLeisureKind.Daydream;
            BeginTimedPhase(
                FoundationResidentPhase.Relaxing,
                leisureSeconds,
                "没有必要任务：在原地发呆并观察四周");
        }

        private bool TrySelectWanderTarget(
            out Vector3 target,
            out DeckNavPathProbe selectedPath,
            out string label)
        {
            Vector3 current = ToNavigationPoint(_model.ResidentLocalPosition.Value);
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
                float angleDegrees = (_leisureSequence * 83 + attempt * 137.508f) % 360f;
                float angle = angleDegrees * Mathf.Deg2Rad;
                var requested = new Vector3(
                    Mathf.Clamp(current.x + Mathf.Cos(angle) * radius, minX, maxX),
                    0f,
                    Mathf.Clamp(current.z + Mathf.Sin(angle) * radius, minZ, maxZ));
                if (HorizontalDistance(current, requested) < minimumDistance ||
                    !TryCalculateTravelPath(current, requested, out DeckNavPathProbe path) ||
                    path.PathLength < minimumDistance)
                    continue;

                target = path.SampledEnd;
                target.y = ResidentVisualHeight;
                selectedPath = path;
                label = $"({target.x:0.00}, {target.z:0.00})";
                return true;
            }

            target = default;
            selectedPath = default;
            label = string.Empty;
            return false;
        }

        private static ResidentActionPlanEvaluation FindSelectedPlan(
            IReadOnlyList<ResidentActionPlanEvaluation> evaluations,
            ResidentActionCandidate selected)
        {
            if (selected == null) return null;
            for (var i = 0; i < evaluations.Count; i++)
            {
                if (ReferenceEquals(evaluations[i].Candidate, selected)) return evaluations[i];
            }
            return null;
        }

        private static float GetNeedRestore(
            ResidentActionCandidate candidate,
            ResidentNeed need)
        {
            float result = 0f;
            NeedEffect[] effects = candidate.NeedEffects ?? Array.Empty<NeedEffect>();
            for (var i = 0; i < effects.Length; i++)
            {
                if (effects[i].Need == need) result += effects[i].Restore;
            }
            return Mathf.Clamp01(result);
        }

        private ResidentActionPlanEvaluation EvaluateWaterRestockPlan(
            in FoundationFacilityState source,
            in FoundationFacilityState station,
            bool drinkAfterDelivery,
            int haulMilliliters,
            out FoundationFacilityState waterCanFacility,
            out bool waterCanAtSource)
        {
            waterCanAtSource = _waterCanLocation == FoundationWaterCanLocation.VehicleWaterTank;
            waterCanFacility = waterCanAtSource ? source : station;

            var requirement = new CargoTransportRequirement(
                NomadResourceIds.Water,
                haulMilliliters,
                CargoContainerCapability.LiquidTight,
                allowBareHands: false,
                bareHandsMaxAmount: 0,
                contaminationSensitivity: 0.8f);
            var carrier = new CargoCarrierOption(
                _waterCan.Id,
                CargoCarrierKind.Container,
                _waterCan.FreeCapacity,
                waterCanCleanliness,
                pickupSeconds,
                waterCanCapabilities,
                isAvailable: true);
            CargoTransportOptionEvaluation carrierEvaluation =
                CargoTransportPlanner.Evaluate(requirement, carrier);

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
                    waterCanAtSource,
                    steps))
            {
                feasibility = ResidentActionPlanFeasibility.Blocked(
                    ResidentActionPlanBlockReason.TargetUnreachable,
                    "完整补水路线中至少有一段无法通过连续 NavMesh");
            }

            float stockDeficit = Mathf.Clamp01(
                (DrinkingStationRestockTargetMilliliters -
                 _drinkingStation.GetAmount(NomadResourceIds.Water)) /
                (float)DrinkingStationRestockTargetMilliliters);
            var proposal = new ResidentActionPlanProposal(
                $"water-restock:{_waterTaskSequence + 1}",
                "restock-drinking-station",
                drinkAfterDelivery ? "取防漏水罐、补水并饮用" : "取防漏水罐并例行补水")
            {
                Steps = steps.ToArray(),
                Feasibility = feasibility,
                BaseUtility = 0.04f,
                WorkUrgency = drinkAfterDelivery
                    ? 0.32f + _residentWaterCycle.Thirst * 0.32f
                    : 0.18f + stockDeficit * 0.18f,
                DependencyValue = drinkAfterDelivery ? 0.18f : 0.04f,
                EmergencyPriority = drinkAfterDelivery && _residentWaterCycle.Thirst >= 0.82f
                    ? 1f
                    : 0f,
                DelayUrgency = drinkAfterDelivery ? _residentWaterCycle.Thirst : stockDeficit * 0.25f,
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
                new ResidentDecisionCondition(
                    motionSickness: 0f,
                    timeSensitivity: 1f,
                    effortAversion: 1f,
                    riskAversion: 1f),
                _actionPlanPolicy);
        }

        private bool TryBuildWaterRestockSteps(
            in FoundationFacilityState source,
            in FoundationFacilityState station,
            bool drinkAfterDelivery,
            in FoundationFacilityState waterCanFacility,
            bool waterCanAtSource,
            ICollection<ResidentActionStepEstimate> steps)
        {
            if (!_definitions.TryGetValue(source.DefinitionId, out NomadFacilityDefinition sourceDefinition) ||
                !_definitions.TryGetValue(station.DefinitionId, out NomadFacilityDefinition stationDefinition))
                return false;

            Vector3 cursor = ToNavigationPoint(_model.ResidentLocalPosition.Value);
            Vector3 sourceGoal;
            if (_waterCanLocation != FoundationWaterCanLocation.Resident)
            {
                NomadFacilityDefinition waterCanDefinition = waterCanAtSource
                    ? sourceDefinition
                    : stationDefinition;
                if (!TrySelectBestInteractionSlot(
                        cursor,
                        waterCanFacility,
                        waterCanDefinition,
                        out Vector3 waterCanGoal,
                        out _,
                        out _,
                        out float acquireTravelMeters,
                        out _))
                    return false;

                AddTravelStep(steps, acquireTravelMeters, "前往防漏水罐");
                steps.Add(new ResidentActionStepEstimate(
                    ResidentActionStepKind.AcquireItem,
                    pickupSeconds,
                    label: "取得唯一防漏水罐"));
                cursor = waterCanGoal;
            }

            if (waterCanAtSource && _waterCanLocation != FoundationWaterCanLocation.Resident)
            {
                sourceGoal = cursor;
            }
            else
            {
                if (!TrySelectBestInteractionSlot(
                        cursor,
                        source,
                        sourceDefinition,
                        out sourceGoal,
                        out _,
                        out _,
                        out float sourceTravelMeters,
                        out _))
                    return false;
                AddTravelStep(steps, sourceTravelMeters, "携带空水罐前往车辆水箱");
            }

            steps.Add(new ResidentActionStepEstimate(
                ResidentActionStepKind.Transfer,
                pickupSeconds,
                label: "把车辆水箱中的水装入防漏水罐"));

            if (!TrySelectBestInteractionSlot(
                    sourceGoal,
                    station,
                    stationDefinition,
                    out _,
                    out _,
                    out _,
                    out float stationTravelMeters,
                    out _))
                return false;
            AddTravelStep(steps, stationTravelMeters, "携带有水的水罐前往饮水站");
            steps.Add(new ResidentActionStepEstimate(
                ResidentActionStepKind.Transfer,
                deliverySeconds,
                label: "把水罐中的水倒入饮水站"));
            if (drinkAfterDelivery)
            {
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

        private ResidentDecisionResult DecideWaterRestockPlan(ResidentActionPlanEvaluation plan)
        {
            var context = new ResidentDecisionContext(
                worldSeed: 1729,
                residentId: ResidentOwnerId,
                decisionSequence: ++_residentDecisionSequence,
                needs: new[]
                {
                    new ResidentNeedState(
                        ResidentNeed.Thirst,
                        _residentWaterCycle.Thirst,
                        0.004f),
                },
                candidates: new[] { plan.Candidate });
            return _decisionEngine.Decide(context);
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
            _model.LatestActionPlan.Value = new FoundationActionPlanProjection(
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
                trace == null ? 0f : (float)trace.Probability);
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
            if (_waterCanPickupWasAtSource)
            {
                BeginTimedPhase(
                    FoundationResidentPhase.PickingUpWater,
                    pickupSeconds,
                    "已取得防漏水罐，正在从车辆水箱装水");
                return;
            }

            if (!TryFindPlacedFacility(
                    NomadFacilityFunction.VehicleWaterTank,
                    out FoundationFacilityState source,
                    out _))
            {
                Block("取得水罐后找不到车辆水箱设施", ResourceFlowBlocker.None);
                return;
            }

            TryBeginMove(
                FoundationResidentPhase.MovingToWaterSource,
                "携带空水罐沿连续 NavMesh 路径前往车辆水箱",
                source);
        }

        private void CompleteWaterPickup()
        {
            _activeHaul.PickUp();
            if (!TryFindPlacedFacility(
                    NomadFacilityFunction.DrinkingStation,
                    out FoundationFacilityState station,
                    out _))
            {
                Block("饮水站在搬运途中消失", ResourceFlowBlocker.None);
                return;
            }

            TryBeginMove(
                FoundationResidentPhase.MovingToDrinkingStation,
                "携带装有水的防漏水罐沿连续路径前往饮水站",
                station);
        }

        private void CompleteWaterDelivery()
        {
            bool shouldDrink = _drinkAfterActiveHaul ||
                               _residentWaterCycle.Thirst >= DrinkNeedThreshold;
            _activeHaul.Deliver();
            _activeHaul = null;
            _drinkAfterActiveHaul = false;
            SetWaterCanLocation(FoundationWaterCanLocation.DrinkingStation);

            if (!shouldDrink)
            {
                ReleaseActiveInteractionSpace(publishProjection: true);
                SetResidentPhase(
                    FoundationResidentPhase.Idle,
                    "完成低优先级饮水站补货，没有额外饮水");
                return;
            }

            if (!TryFindPlacedFacility(
                    NomadFacilityFunction.DrinkingStation,
                    out FoundationFacilityState station,
                    out _))
            {
                Block("饮水站在任务途中消失", ResourceFlowBlocker.None);
                return;
            }
            BeginDrink(station);
        }

        private void BeginDrink(FoundationFacilityState station)
        {
            if (!_residentWaterCycle.TryReserveDrink(
                    _resourceFlow,
                    _drinkingStation,
                    $"drink:{_waterTaskSequence}",
                    $"facility:{station.InstanceId}:drink",
                    ResidentWaterCycle.DefaultDrinkServingMilliliters,
                    out _activeResidentAction,
                    out ResourceFlowBlocker blocker))
            {
                if (blocker.Reason == ResourceFlowBlockReason.DestinationFull)
                {
                    _model.LastBlocker.Value =
                        $"{blocker.Reason} · {blocker.InventoryId} · 等待代谢或如厕后再饮水";
                    TryStartLeisureRoutine();
                }
                else
                {
                    Block("无法开始饮水", blocker);
                }
                return;
            }

            if (!TryBeginMove(
                    FoundationResidentPhase.MovingToDrinkingStation,
                    "沿连续 NavMesh 路径前往饮水站",
                    station))
                return;

            if (AdvanceResidentAlongPath(0f))
            {
                ClearActiveMoveIntent();
                BeginTimedPhase(FoundationResidentPhase.Drinking, drinkingSeconds, "在饮水站喝水");
            }
        }

        private void CompleteDrink()
        {
            _activeResidentAction.Commit();
            _activeResidentAction = null;
            ReleaseActiveInteractionSpace(publishProjection: true);
            _model.CompletedDrinkCount.Value++;
            SetResidentPhase(FoundationResidentPhase.Idle, "完成一次饮水，水已进入居民身体");
        }

        private void CompleteToiletUse()
        {
            _activeResidentAction.Commit();
            _activeResidentAction = null;
            ReleaseActiveInteractionSpace(publishProjection: true);
            _model.CompletedToiletUseCount.Value++;
            _model.LastBlocker.Value = string.Empty;
            SetResidentPhase(
                FoundationResidentPhase.Idle,
                "完成一次如厕，排泄物已进入旱厕暂存桶");
        }

        private void CompleteLeisure()
        {
            _residentRecreation = Mathf.Clamp01(
                _residentRecreation - _activeLeisureRestore);
            _activeLeisureRestore = 0f;
            _model.CompletedLeisureCount.Value++;
            if (_activeLeisureKind == FoundationLeisureKind.Daydream)
                _model.CompletedDaydreamCount.Value++;
            else if (_activeLeisureKind == FoundationLeisureKind.Wander)
                _model.CompletedWanderCount.Value++;
            _activeLeisureKind = FoundationLeisureKind.None;
            SetResidentPhase(
                FoundationResidentPhase.Idle,
                "完成一次自主休闲，娱乐缺口已经降低");
        }

        private bool TryBeginMove(
            FoundationResidentPhase phase,
            string task,
            in FoundationFacilityState facility,
            bool allowAlternativeFacility = true)
        {
            if (!_definitions.TryGetValue(
                    facility.DefinitionId,
                    out NomadFacilityDefinition definition))
            {
                Block($"设施 {facility.InstanceId} 缺少定义", ResourceFlowBlocker.None);
                return false;
            }

            _activeMoveIntent = new FoundationResidentMoveIntent(
                phase,
                task,
                definition.Function,
                facility.InstanceId,
                allowAlternativeFacility);
            _hasActiveMoveIntent = true;
            return TryResumeActiveMove();
        }

        /// <summary>
        /// 从语义移动意图重新选择工作位。优先保留原设施；它完全不可用时才考虑同功能设施，
        /// 从而让建造后的恢复既稳定又不会永远追逐已经失效的世界坐标。
        /// </summary>
        private bool TryResumeActiveMove()
        {
            if (!_hasActiveMoveIntent) return false;

            ReleaseActiveInteractionSpace(publishProjection: false);
            ClearActivePath();
            if (!TrySelectFacilityInteractionForIntent(
                    _activeMoveIntent,
                    out FoundationFacilityState selectedFacility,
                    out DeckPose dockingPose,
                    out string slotLabel,
                    out InteractionSpaceSlot selectedSlot) ||
                !TryAcquireInteractionSpace(selectedSlot) ||
                !TryAssignDockingPath(dockingPose))
            {
                ReleaseActiveInteractionSpace(publishProjection: false);
                ClearActivePath();
                _routeRetryRemaining = Mathf.Max(0.1f, routeRetrySeconds);
                SetResidentPhase(
                    FoundationResidentPhase.WaitingForRoute,
                    $"路线暂不可达：{_activeMoveIntent.Task}；将自动换 Slot、换同功能设施或重试");
                _model.LastBlocker.Value =
                    $"RouteUnavailable · {_activeMoveIntent.FacilityFunction} · " +
                    _activeMoveIntent.PreferredFacilityInstanceId;
                PublishCurrentFacilityAccessProjection();
                return false;
            }

            _activeMoveIntent = _activeMoveIntent.Retarget(selectedFacility.InstanceId);
            _routeRetryRemaining = 0f;
            _model.LastBlocker.Value = string.Empty;
            PublishCurrentFacilityAccessProjection();
            SetResidentPhase(
                _activeMoveIntent.TravelPhase,
                $"{_activeMoveIntent.Task} · {selectedFacility.InstanceId}/{slotLabel}");
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
                TrySelectBestInteractionSlot(
                    ToNavigationPoint(_model.ResidentLocalPosition.Value),
                    preferred,
                    _definitions[preferred.DefinitionId],
                    out _,
                    out selectedDockingPose,
                    out selectedLabel,
                    out _,
                    out selectedSlot))
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
                    !TrySelectBestInteractionSlot(
                        ToNavigationPoint(_model.ResidentLocalPosition.Value),
                        candidate,
                        definition,
                        out _,
                        out DeckPose dockingPose,
                        out string label,
                        out float pathLength,
                        out InteractionSpaceSlot slot) ||
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

        private bool TrySelectBestInteractionSlot(
            in FoundationFacilityState facility,
            NomadFacilityDefinition definition,
            out Vector3 selectedPosition,
            out string selectedLabel,
            out InteractionSpaceSlot selectedSlot)
            => TrySelectBestInteractionSlot(
                ToNavigationPoint(_model.ResidentLocalPosition.Value),
                facility,
                definition,
                out selectedPosition,
                out _,
                out selectedLabel,
                out _,
                out selectedSlot);

        private bool TrySelectBestInteractionSlot(
            Vector3 start,
            in FoundationFacilityState facility,
            NomadFacilityDefinition definition,
            out Vector3 selectedPosition,
            out DeckPose selectedDockingPose,
            out string selectedLabel,
            out float selectedPathLength,
            out InteractionSpaceSlot selectedSlot)
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
            return _interactionSpaces.TryAcquire(ResidentOwnerId, slot);
        }

        private void ReleaseActiveInteractionSpace(bool publishProjection)
        {
            bool changed = _interactionSpaces != null && _interactionSpaces.Release();
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
            Vector3 start = ToNavigationPoint(_model.ResidentLocalPosition.Value);
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
            Vector3 start = ToNavigationPoint(_model.ResidentLocalPosition.Value);
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
            _activePathCorners = new Vector3[cornerCount + (appendExactGoal ? 1 : 0)];

            for (var i = 0; i < cornerCount; i++)
            {
                Vector3 corner = path.Corners[i];
                corner.y = ResidentVisualHeight;
                _activePathCorners[i] = corner;
            }
            exactGoal.y = ResidentVisualHeight;
            if (appendExactGoal) _activePathCorners[cornerCount] = exactGoal;
            _hasActiveDockingYaw = hasDockingYaw;
            _activeDockingYawDegrees = Mathf.Repeat(dockingYawDegrees, 360f);
            Vector3 current = _model.ResidentLocalPosition.Value;
            _nextPathCornerIndex = _activePathCorners.Length > 0 &&
                                   HorizontalDistance(current, _activePathCorners[0]) <= 0.01f
                ? 1
                : 0;
            _model.ActivePathSummary.Value = DescribePath(_activePathCorners);
            WriteRemainingPathProjection();
            return _activePathCorners.Length > 0;
        }

        private bool AdvanceResidentAlongPath(float deltaTime)
        {
            if (_activePathCorners.Length == 0)
            {
                if (_hasActiveMoveIntent)
                {
                    _routeRetryRemaining = Mathf.Max(0.1f, routeRetrySeconds);
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

            Vector3 current = _model.ResidentLocalPosition.Value;
            float remainingDistance = residentMoveSpeed * Mathf.Max(0f, deltaTime);
            while (_nextPathCornerIndex < _activePathCorners.Length)
            {
                Vector3 target = _activePathCorners[_nextPathCornerIndex];
                Vector3 facing = target - current;
                if (facing.sqrMagnitude > 0.000001f)
                    _model.ResidentLocalYawDegrees.Value = Mathf.Repeat(
                        Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg,
                        360f);
                float distance = Vector3.Distance(current, target);
                if (distance <= 0.0001f)
                {
                    current = target;
                    _nextPathCornerIndex++;
                    continue;
                }

                if (remainingDistance <= 0f) break;
                if (distance <= remainingDistance)
                {
                    current = target;
                    remainingDistance -= distance;
                    _nextPathCornerIndex++;
                    continue;
                }

                current = Vector3.MoveTowards(current, target, remainingDistance);
                remainingDistance = 0f;
            }

            if (current != _model.ResidentLocalPosition.Value)
                _model.ResidentLocalPosition.Value = current;
            WriteRemainingPathProjection();

            if (_nextPathCornerIndex < _activePathCorners.Length) return false;
            if (_hasActiveDockingYaw)
                _model.ResidentLocalYawDegrees.Value = _activeDockingYawDegrees;
            ClearActivePath();
            return true;
        }

        private void ReplanActiveMoveAfterPlacement()
        {
            if (_residentPhase == FoundationResidentPhase.MovingToLeisure)
            {
                ClearActivePath();
                _activeLeisureRestore = 0f;
                _activeLeisureKind = FoundationLeisureKind.None;
                SetResidentPhase(
                    FoundationResidentPhase.Idle,
                    "建造改变了散步路线，已放弃旧空地点并准备重新选择");
                return;
            }

            if (_hasActiveMoveIntent) TryResumeActiveMove();
        }

        private void WriteRemainingPathProjection()
        {
            float remaining = 0f;
            Vector3 cursor = _model.ResidentLocalPosition.Value;
            for (var i = _nextPathCornerIndex; i < _activePathCorners.Length; i++)
            {
                remaining += Vector3.Distance(cursor, _activePathCorners[i]);
                cursor = _activePathCorners[i];
            }

            SetFloat(_model.RemainingPathMeters, remaining);
            int corners = Math.Max(0, _activePathCorners.Length - _nextPathCornerIndex);
            if (_model.RemainingPathCorners.Value != corners)
                _model.RemainingPathCorners.Value = corners;
        }

        private void ClearActivePath()
        {
            _activePathCorners = Array.Empty<Vector3>();
            _nextPathCornerIndex = 0;
            _hasActiveDockingYaw = false;
            _activeDockingYawDegrees = 0f;
            if (_model == null) return;
            SetFloat(_model.RemainingPathMeters, 0f);
            if (_model.RemainingPathCorners.Value != 0) _model.RemainingPathCorners.Value = 0;
            if (!string.IsNullOrEmpty(_model.ActivePathSummary.Value))
                _model.ActivePathSummary.Value = string.Empty;
        }

        private void ClearActiveMoveIntent()
        {
            _activeMoveIntent = default;
            _hasActiveMoveIntent = false;
            _routeRetryRemaining = 0f;
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
            _phaseDuration = Mathf.Max(0.001f, duration);
            _phaseRemaining = _phaseDuration;
            _model.ActionProgress.Value = 0f;
            SetResidentPhase(phase, task);
        }

        private bool TickTimer(float deltaTime)
        {
            _phaseRemaining = Mathf.Max(0f, _phaseRemaining - deltaTime);
            _model.ActionProgress.Value = 1f - _phaseRemaining / _phaseDuration;
            return _phaseRemaining <= 0f;
        }

        private void SetResidentPhase(FoundationResidentPhase phase, string task)
        {
            _residentPhase = phase;
            _model.ResidentPhase.Value = phase;
            _model.CurrentTask.Value = task;
            if (phase != FoundationResidentPhase.PickingUpWaterCan &&
                phase != FoundationResidentPhase.PickingUpWater &&
                phase != FoundationResidentPhase.DeliveringWater &&
                phase != FoundationResidentPhase.Drinking &&
                phase != FoundationResidentPhase.UsingToilet &&
                phase != FoundationResidentPhase.Relaxing)
                _model.ActionProgress.Value = 0f;
        }

        private void Block(string task, ResourceFlowBlocker blocker)
        {
            ReleaseActiveTasks();
            ClearActivePath();
            PublishCurrentFacilityAccessProjection();
            SetResidentPhase(FoundationResidentPhase.Blocked, task);
            _model.LastBlocker.Value = blocker.IsBlocked
                ? $"{blocker.Reason} · {blocker.InventoryId} · {blocker.Resource}"
                : task;
        }

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
            if (string.IsNullOrEmpty(_model.LastBlocker.Value) ||
                _model.LastBlocker.Value.StartsWith("PhysiologyBackpressure", StringComparison.Ordinal))
                _model.LastBlocker.Value =
                    $"PhysiologyBackpressure · {blocker.InventoryId} · 需要如厕或清运";
        }

        private void WriteSimulationProjection()
        {
            if (_residentWaterCycle == null) return;
            SetFloat(_model.ResidentThirst, _residentWaterCycle.Thirst);
            SetFloat(_model.ResidentRecreation, _residentRecreation);
            SetInt(
                _model.VehicleWaterMilliliters,
                _vehicleWater.GetAmount(NomadResourceIds.Water));
            SetInt(
                _model.WaterCanWaterMilliliters,
                _waterCan.GetAmount(NomadResourceIds.Water));
            bool carryingWater = _waterCanLocation == FoundationWaterCanLocation.Resident &&
                                 _waterCan.GetAmount(NomadResourceIds.Water) > 0;
            if (_model.ResidentCarryingWater.Value != carryingWater)
                _model.ResidentCarryingWater.Value = carryingWater;
            SetInt(
                _model.DrinkingStationWaterMilliliters,
                _drinkingStation.GetAmount(NomadResourceIds.Water));
            SetInt(
                _model.BodyWaterMilliliters,
                _residentWaterCycle.BodyWater.GetAmount(NomadResourceIds.Water));
            SetInt(
                _model.BladderWasteMilliliters,
                _residentWaterCycle.Bladder.GetAmount(NomadResourceIds.HumanWaste));
            SetInt(
                _model.ToiletHoldingWasteMilliliters,
                _toiletHolding.GetAmount(NomadResourceIds.HumanWaste));
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
            _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, null),
        };

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

        private void SetWaterCanLocation(FoundationWaterCanLocation location)
        {
            _waterCanLocation = location;
            if (_model != null && _model.WaterCanLocation.Value != location)
                _model.WaterCanLocation.Value = location;
        }

        private void ReleaseActiveTasks()
        {
            _activeHaul?.Dispose();
            _activeHaul = null;
            _drinkAfterActiveHaul = false;
            _activeResidentAction?.Dispose();
            _activeResidentAction = null;
            ReleaseActiveInteractionSpace(publishProjection: false);
            _activeLeisureRestore = 0f;
            _activeLeisureKind = FoundationLeisureKind.None;
            ClearActiveMoveIntent();
        }

        protected override void OnDestroy()
        {
            ReleaseActiveTasks();
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
