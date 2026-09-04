using System;
using System.Collections.Generic;
using Game.NomadWorkshop.Simulation;
using UnityEngine;

namespace Game.NomadWorkshop
{
    /// <summary>
    /// Foundation Prototype 的实体物流垂直切片：居民把食材和水从有容量的来源搬进厨房，
    /// 厨房再原子地产出餐食与污水；饮水后的水也会延迟成为排泄物，经厕所暂存后由居民清运。
    /// 它是可丢弃的 3D Harness；资源与生理真值位于纯 C# 模拟层。
    /// </summary>
    public sealed class FoundationResourceFlowSpikeController : MonoBehaviour
    {
        private const ulong ResidentId = 0xADA01UL;

        [SerializeField, Min(0.1f)] private float simulationSpeed = 1f;
        [SerializeField, Min(0.1f)] private float residentMoveSpeed = 2.4f;
        [SerializeField, Min(0.05f)] private float pickupSeconds = 0.55f;
        [SerializeField, Min(0.05f)] private float deliverySeconds = 0.55f;
        [SerializeField, Min(0.05f)] private float processingSeconds = 2.6f;
        [SerializeField, Min(0.1f)] private float drinkMetabolismSeconds = 4.2f;
        [SerializeField] private GameObject fieldKitchenPrefab;
        [SerializeField] private GameObject humanoidPrefab;
        [SerializeField] private RuntimeAnimatorController humanoidController;

        private readonly List<Material> _runtimeMaterials = new();
        private ResourceFlowLedger _resourceFlow;
        private ResourceInventory _pantry;
        private ResourceInventory _waterTank;
        private ResourceInventory _residentItemCarry;
        private ResourceInventory _residentLiquidCarry;
        private ResourceInventory _kitchenFoodInput;
        private ResourceInventory _kitchenWaterInput;
        private ResourceInventory _kitchenMealOutput;
        private ResourceInventory _kitchenWasteOutput;
        private ResourceInventory _mealShelf;
        private ResourceInventory _wasteTank;
        private ResourceInventory _drinkingStation;
        private ResourceInventory _toiletHolding;
        private ResidentWaterCycle _residentWaterCycle;
        private HaulTaskLease _activeHaul;
        private ProcessTaskLease _activeProcess;
        private ResidentWaterActionLease _activeResidentAction;
        private ResourceFlowBlocker _lastBlocker;

        private Transform _residentRoot;
        private Transform _residentVisual;
        private ResidentHumanoidPresentation _humanoidPresentation;
        private Transform _kitchenRoot;
        private Transform _foodPickupPoint;
        private Transform _waterPickupPoint;
        private Transform _foodDeliveryPoint;
        private Transform _waterDeliveryPoint;
        private Transform _workPoint;
        private Transform _mealPickupPoint;
        private Transform _wastePickupPoint;
        private Transform _mealDeliveryPoint;
        private Transform _wasteDeliveryPoint;
        private Transform _drinkingDeliveryPoint;
        private Transform _drinkingInteractionPoint;
        private Transform _toiletInteractionPoint;
        private Transform _toiletWastePickupPoint;
        private Transform _centerDoorHinge;
        private Quaternion _centerDoorClosedRotation;
        private GameObject _foodCargoVisual;
        private GameObject _waterCargoVisual;
        private GameObject _mealCargoVisual;
        private GameObject _wasteCargoVisual;
        private Transform _kitchenMealIndicator;
        private Transform _kitchenWasteIndicator;
        private Transform _mealIndicator;
        private Transform _wasteIndicator;
        private Transform _drinkingWaterIndicator;
        private Transform _toiletWasteIndicator;
        private Vector3 _residentStart;
        private Vector3 _moveTarget;
        private Vector3 _deliveryTarget;
        private float _phaseRemaining;
        private float _visualClock;
        private float _doorOpenAmount;
        private float _doorTarget;
        private int _batchIndex;
        private int _sanitationCycleCount;
        private bool _simulationPaused;
        private bool _worldBuilt;
        private SpikePhase _phase;
        private FlowRoute _activeRoute;
        private ResidentAction _residentAction;
        private string _currentTask = "等待初始化";
        private string _currentContract = string.Empty;
        private GUIStyle _titleStyle;
        private GUIStyle _bodyStyle;
        private GUIStyle _accentStyle;
        private GUIStyle _warningStyle;
        private Vector2 _guiScroll;

        public enum SpikePhase
        {
            Initializing,
            MovingToPickup,
            PickingUp,
            MovingToDelivery,
            Delivering,
            MovingToKitchenWork,
            Processing,
            MovingToResidentAction,
            ResidentAction,
            Metabolizing,
            Completed,
            Blocked,
        }

        private enum FlowRoute
        {
            None,
            KitchenFoodInput,
            KitchenWaterInput,
            MealOutput,
            KitchenWasteOutput,
            DrinkingWater,
            ToiletWaste,
        }

        private enum ResidentAction
        {
            None,
            Drink,
            UseToilet,
        }

        public bool HasGeneratedWorld => _worldBuilt;
        public bool HasGeneratedResident => _residentRoot != null;
        public bool HasKitchen => _kitchenRoot != null;
        public bool UsesParametricKitchen => fieldKitchenPrefab != null && _kitchenRoot != null;
        public bool HasVisibleCargo =>
            (_foodCargoVisual != null && _foodCargoVisual.activeSelf) ||
            (_waterCargoVisual != null && _waterCargoVisual.activeSelf) ||
            (_mealCargoVisual != null && _mealCargoVisual.activeSelf) ||
            (_wasteCargoVisual != null && _wasteCargoVisual.activeSelf);
        public int CompletedBatchCount => _batchIndex;
        public int CompletedSanitationCycleCount => _sanitationCycleCount;
        public SpikePhase Phase => _phase;
        public ResourceFlowBlocker LastBlocker => _lastBlocker;
        public float CenterDoorOpenAmount => _doorOpenAmount;
        public int PantryFood => _pantry?.GetAmount(NomadResourceIds.FoodIngredient) ?? 0;
        public int WaterTankMilliliters => _waterTank?.GetAmount(NomadResourceIds.Water) ?? 0;
        public int KitchenMealOutput => _kitchenMealOutput?.GetAmount(NomadResourceIds.PreparedMeal) ?? 0;
        public int KitchenWasteOutputMilliliters =>
            _kitchenWasteOutput?.GetAmount(NomadResourceIds.WasteWater) ?? 0;
        public int PreparedMeals => _mealShelf?.GetAmount(NomadResourceIds.PreparedMeal) ?? 0;
        public int WasteWaterMilliliters =>
            _wasteTank?.GetAmount(NomadResourceIds.WasteWater) ?? 0;
        public int HumanWasteMilliliters =>
            _wasteTank?.GetAmount(NomadResourceIds.HumanWaste) ?? 0;
        public int VehicleWasteTotalMilliliters => _wasteTank?.TotalAmount ?? 0;
        public int DrinkingStationWaterMilliliters =>
            _drinkingStation?.GetAmount(NomadResourceIds.Water) ?? 0;
        public int ToiletHoldingWasteMilliliters =>
            _toiletHolding?.GetAmount(NomadResourceIds.HumanWaste) ?? 0;
        public int ResidentCarriedHumanWasteMilliliters =>
            _residentLiquidCarry?.GetAmount(NomadResourceIds.HumanWaste) ?? 0;
        public int BodyWaterMilliliters =>
            _residentWaterCycle?.BodyWater.GetAmount(NomadResourceIds.Water) ?? 0;
        public int BladderWasteMilliliters =>
            _residentWaterCycle?.Bladder.GetAmount(NomadResourceIds.HumanWaste) ?? 0;
        public float Thirst => _residentWaterCycle?.Thirst ?? 0f;
        public float ExcretionPressure => _residentWaterCycle?.ExcretionPressure ?? 0f;
        public bool IsSimulationPaused => _simulationPaused;

        /// <summary>完成上一批后请求下一批；运行中或已阻塞时拒绝，避免覆盖当前任务和阻塞证据。</summary>
        public bool RequestNextBatch()
        {
            if (_phase != SpikePhase.Completed) return false;
            BeginBatch();
            return true;
        }

        /// <summary>在一个厨房批次完成后运行一次可观察的饮水、代谢、如厕与清运链。</summary>
        public bool RequestWaterSanitationCycle()
        {
            if (_phase != SpikePhase.Completed || _sanitationCycleCount > 0) return false;
            BeginSanitationWaterHaul();
            return true;
        }

        /// <summary>供 Harness UI、自动截图和诊断工具稳定冻结可观察阶段。</summary>
        public void SetSimulationPaused(bool paused)
        {
            _simulationPaused = paused;
        }

        /// <summary>由 Editor 场景生成器写入可替换表现资产；模拟层不依赖这些引用。</summary>
        public void ConfigurePresentationAssets(
            GameObject configuredKitchenPrefab,
            GameObject configuredHumanoidPrefab,
            RuntimeAnimatorController configuredHumanoidController)
        {
            if (_worldBuilt)
                throw new InvalidOperationException("表现资产必须在场景进入 Play Mode 前配置。 ");
            fieldKitchenPrefab = configuredKitchenPrefab;
            humanoidPrefab = configuredHumanoidPrefab;
            humanoidController = configuredHumanoidController;
        }

        /// <summary>只供隔离测试在 Start 前缩短等待；不改变任务和经济语义。</summary>
        public void ConfigureTimingsForTests(float configuredSimulationSpeed, float configuredMoveSpeed)
        {
            if (configuredSimulationSpeed <= 0f)
                throw new ArgumentOutOfRangeException(nameof(configuredSimulationSpeed));
            if (configuredMoveSpeed <= 0f)
                throw new ArgumentOutOfRangeException(nameof(configuredMoveSpeed));
            simulationSpeed = configuredSimulationSpeed;
            residentMoveSpeed = configuredMoveSpeed;
            pickupSeconds = 0.01f;
            deliverySeconds = 0.01f;
            processingSeconds = 0.01f;
        }

        private void Awake()
        {
            InitializeInventories();
            BuildWorld();
        }

        private void Start()
        {
            BeginBatch();
        }

        private void Update()
        {
            float realDelta = Time.unscaledDeltaTime;
            _visualClock += realDelta;
            TickDoor(realDelta);
            AnimateResident(realDelta);
            if (_simulationPaused) return;

            float simulationDelta = realDelta * simulationSpeed;
            ResidentWaterCycleTick waterTick = _residentWaterCycle.Advance(simulationDelta, _resourceFlow);
            if (_phase == SpikePhase.Metabolizing)
            {
                if (waterTick.Blocker.IsBlocked)
                {
                    _lastBlocker = waterTick.Blocker;
                    SetBlocked("体内水代谢");
                }
                else if (waterTick.MetabolizedMilliliters > 0 &&
                         _residentWaterCycle.BodyWater.TotalAmount == 0)
                {
                    BeginToiletUse();
                }
            }

            switch (_phase)
            {
                case SpikePhase.MovingToPickup:
                    if (MoveResident(simulationDelta, _moveTarget))
                        BeginTimedPhase(SpikePhase.PickingUp, pickupSeconds, ResidentAnimationSemantic.Pickup);
                    break;
                case SpikePhase.PickingUp:
                    if (TickTimer(simulationDelta)) CompletePickup();
                    break;
                case SpikePhase.MovingToDelivery:
                    if (MoveResident(simulationDelta, _deliveryTarget))
                        BeginTimedPhase(SpikePhase.Delivering, deliverySeconds, ResidentAnimationSemantic.Pickup);
                    break;
                case SpikePhase.Delivering:
                    if (TickTimer(simulationDelta)) CompleteDelivery();
                    break;
                case SpikePhase.MovingToKitchenWork:
                    if (MoveResident(simulationDelta, _moveTarget))
                    {
                        _doorTarget = 1f;
                        BeginTimedPhase(SpikePhase.Processing, processingSeconds, ResidentAnimationSemantic.Work);
                    }
                    break;
                case SpikePhase.Processing:
                    if (TickTimer(simulationDelta)) CompleteProcessing();
                    break;
                case SpikePhase.MovingToResidentAction:
                    if (MoveResident(simulationDelta, _moveTarget))
                    {
                        ResidentAnimationSemantic semantic = _residentAction == ResidentAction.Drink
                            ? ResidentAnimationSemantic.Pickup
                            : ResidentAnimationSemantic.Rest;
                        BeginTimedPhase(SpikePhase.ResidentAction, deliverySeconds, semantic);
                    }
                    break;
                case SpikePhase.ResidentAction:
                    if (TickTimer(simulationDelta)) CompleteResidentAction();
                    break;
            }
        }

        private void OnDestroy()
        {
            _activeHaul?.Dispose();
            _activeProcess?.Dispose();
            _activeResidentAction?.Dispose();
            for (int i = 0; i < _runtimeMaterials.Count; i++)
            {
                Material material = _runtimeMaterials[i];
                if (material != null) Destroy(material);
            }
        }

        private void InitializeInventories()
        {
            _activeHaul?.Dispose();
            _activeProcess?.Dispose();
            _activeResidentAction?.Dispose();
            _activeHaul = null;
            _activeProcess = null;
            _activeResidentAction = null;
            _resourceFlow = new ResourceFlowLedger();
            _pantry = new ResourceInventory(
                "pantry",
                ResourceMeasure.Item,
                6,
                new ResourceQuantity(NomadResourceIds.FoodIngredient, 3));
            _waterTank = new ResourceInventory(
                "clean-water-tank",
                ResourceMeasure.Milliliter,
                3_000,
                new ResourceQuantity(NomadResourceIds.Water, 2_000));
            _residentItemCarry = new ResourceInventory(
                "resident-ada-item-carry",
                ResourceMeasure.Item,
                1);
            _residentLiquidCarry = new ResourceInventory(
                "resident-ada-liquid-carry",
                ResourceMeasure.Milliliter,
                500);
            _kitchenFoodInput = new ResourceInventory(
                "kitchen-food-input",
                ResourceMeasure.Item,
                1);
            _kitchenWaterInput = new ResourceInventory(
                "kitchen-water-input",
                ResourceMeasure.Milliliter,
                500);
            _kitchenMealOutput = new ResourceInventory(
                "kitchen-meal-output",
                ResourceMeasure.Item,
                1);
            _kitchenWasteOutput = new ResourceInventory(
                "kitchen-waste-output",
                ResourceMeasure.Milliliter,
                500);
            _mealShelf = new ResourceInventory(
                "prepared-meal-shelf",
                ResourceMeasure.Item,
                3);
            _wasteTank = new ResourceInventory(
                "vehicle-waste-tank",
                ResourceMeasure.Milliliter,
                1_000);
            _drinkingStation = new ResourceInventory(
                "drinking-station",
                ResourceMeasure.Milliliter,
                ResidentWaterCycle.DefaultDrinkServingMilliliters);
            _toiletHolding = new ResourceInventory(
                "toilet-holding",
                ResourceMeasure.Milliliter,
                500);
            _residentWaterCycle = new ResidentWaterCycle(
                "ada",
                ResidentId,
                ResidentWaterCycle.DefaultDrinkServingMilliliters /
                Mathf.Max(0.01f, drinkMetabolismSeconds));
            _lastBlocker = ResourceFlowBlocker.None;
            _batchIndex = 0;
            _sanitationCycleCount = 0;
            _activeRoute = FlowRoute.None;
            _residentAction = ResidentAction.None;
            _phase = SpikePhase.Initializing;
        }

        private void BeginBatch()
        {
            if (!_worldBuilt) return;
            _lastBlocker = ResourceFlowBlocker.None;
            HideCargo();
            BeginHaul(
                new HaulTaskRequest(
                    $"batch-{_batchIndex + 1}:haul-food",
                    ResidentId,
                    _pantry,
                    _residentItemCarry,
                    _kitchenFoodInput,
                    NomadResourceIds.FoodIngredient,
                    1,
                    "厨房需要一份食材",
                    new[] { "station:pantry-access", "station:kitchen-food-input" }),
                _foodPickupPoint.position,
                _foodDeliveryPoint.position,
                FlowRoute.KitchenFoodInput,
                "搬运食材");
        }

        private void BeginWaterHaul()
        {
            BeginHaul(
                new HaulTaskRequest(
                    $"batch-{_batchIndex + 1}:haul-water",
                    ResidentId,
                    _waterTank,
                    _residentLiquidCarry,
                    _kitchenWaterInput,
                    NomadResourceIds.Water,
                    500,
                    "厨房需要 500 mL 清水",
                    new[] { "station:clean-water", "station:kitchen-water-input" }),
                _waterPickupPoint.position,
                _waterDeliveryPoint.position,
                FlowRoute.KitchenWaterInput,
                "搬运清水");
        }

        private void BeginMealOutputHaul()
        {
            BeginHaul(
                new HaulTaskRequest(
                    $"batch-{_batchIndex + 1}:store-meal",
                    ResidentId,
                    _kitchenMealOutput,
                    _residentItemCarry,
                    _mealShelf,
                    NomadResourceIds.PreparedMeal,
                    1,
                    "把刚制作的餐食放入成品餐架",
                    new[] { "station:kitchen-meal-output", "station:prepared-meal-shelf" }),
                _mealPickupPoint.position,
                _mealDeliveryPoint.position,
                FlowRoute.MealOutput,
                "收存餐食");
        }

        private void BeginWasteOutputHaul()
        {
            BeginHaul(
                new HaulTaskRequest(
                    $"batch-{_batchIndex + 1}:drain-kitchen-waste",
                    ResidentId,
                    _kitchenWasteOutput,
                    _residentLiquidCarry,
                    _wasteTank,
                    NomadResourceIds.WasteWater,
                    500,
                    "把厨房污水桶送入有容量的污水罐",
                    new[] { "station:kitchen-waste-output", "station:vehicle-waste-tank" }),
                _wastePickupPoint.position,
                _wasteDeliveryPoint.position,
                FlowRoute.KitchenWasteOutput,
                "清运厨房污水");
        }

        private void BeginSanitationWaterHaul()
        {
            _lastBlocker = ResourceFlowBlocker.None;
            HideCargo();
            BeginHaul(
                new HaulTaskRequest(
                    $"sanitation-{_sanitationCycleCount + 1}:fill-drinking-station",
                    ResidentId,
                    _waterTank,
                    _residentLiquidCarry,
                    _drinkingStation,
                    NomadResourceIds.Water,
                    ResidentWaterCycle.DefaultDrinkServingMilliliters,
                    "Ada 口渴，需要把 300 mL 清水送到饮水台",
                    new[] { "station:clean-water", "station:drinking" }),
                _waterPickupPoint.position,
                _drinkingDeliveryPoint.position,
                FlowRoute.DrinkingWater,
                "给饮水台送水");
        }

        private void BeginToiletWasteHaul()
        {
            BeginHaul(
                new HaulTaskRequest(
                    $"sanitation-{_sanitationCycleCount + 1}:empty-toilet",
                    ResidentId,
                    _toiletHolding,
                    _residentLiquidCarry,
                    _wasteTank,
                    NomadResourceIds.HumanWaste,
                    _toiletHolding.GetAmount(NomadResourceIds.HumanWaste),
                    "把厕所暂存桶中的排泄物清运到车辆废物罐",
                    new[] { "station:toilet-canister", "station:vehicle-waste-tank" }),
                _toiletWastePickupPoint.position,
                _wasteDeliveryPoint.position,
                FlowRoute.ToiletWaste,
                "清运厕所暂存桶");
        }

        private void BeginHaul(
            HaulTaskRequest request,
            Vector3 pickupPoint,
            Vector3 deliveryPoint,
            FlowRoute route,
            string displayName)
        {
            if (!_resourceFlow.TryReserveHaul(request, out _activeHaul, out _lastBlocker))
            {
                SetBlocked(displayName);
                return;
            }

            _moveTarget = GroundPoint(pickupPoint);
            _deliveryTarget = GroundPoint(deliveryPoint);
            _activeRoute = route;
            _phase = SpikePhase.MovingToPickup;
            _doorTarget = ReferenceEquals(request.Source, _kitchenMealOutput) ? 1f : 0f;
            _currentTask = displayName;
            _currentContract =
                $"{request.Source.Id} → {request.Carrier.Id} → {request.Destination.Id}\n" +
                $"{request.Resource} × " +
                $"{ResourceAmountFormatting.Format(request.Amount, request.Resource.Measure)}；" +
                $"原因：{request.Reason}";
            SetResidentSemantic(ResidentAnimationSemantic.Move);
        }

        private void CompletePickup()
        {
            _activeHaul.PickUp();
            ShowCargo(_activeHaul.Request.Resource);
            _phase = SpikePhase.MovingToDelivery;
            _doorTarget = ReferenceEquals(_activeHaul.Request.Destination, _kitchenFoodInput) ? 1f : 0f;
            SetResidentSemantic(ResidentAnimationSemantic.Move);
        }

        private void CompleteDelivery()
        {
            _activeHaul.Deliver();
            _activeHaul = null;
            HideCargo();
            _doorTarget = 0f;
            RefreshOutputIndicators();

            switch (_activeRoute)
            {
                case FlowRoute.KitchenFoodInput:
                    BeginWaterHaul();
                    break;
                case FlowRoute.KitchenWaterInput:
                    BeginProcessing();
                    break;
                case FlowRoute.MealOutput:
                    BeginWasteOutputHaul();
                    break;
                case FlowRoute.KitchenWasteOutput:
                    CompleteBatch();
                    break;
                case FlowRoute.DrinkingWater:
                    BeginDrink();
                    break;
                case FlowRoute.ToiletWaste:
                    CompleteSanitationCycle();
                    break;
                default:
                    throw new InvalidOperationException($"未定义搬运路线 {_activeRoute} 的后续阶段。 ");
            }
        }

        private void BeginDrink()
        {
            if (!_residentWaterCycle.TryReserveDrink(
                    _resourceFlow,
                    _drinkingStation,
                    $"sanitation-{_sanitationCycleCount + 1}:drink",
                    "station:drinking",
                    ResidentWaterCycle.DefaultDrinkServingMilliliters,
                    out _activeResidentAction,
                    out _lastBlocker))
            {
                SetBlocked("饮水");
                return;
            }

            _residentAction = ResidentAction.Drink;
            _moveTarget = GroundPoint(_drinkingInteractionPoint.position);
            _phase = SpikePhase.MovingToResidentAction;
            _currentTask = "Ada 饮水";
            _currentContract =
                "输入：饮水台清水 300 mL；提交后进入 Ada 体内待代谢库存\n" +
                "口渴只在行动提交时缓解；取消不会吞水。";
            SetResidentSemantic(ResidentAnimationSemantic.Move);
        }

        private void BeginToiletUse()
        {
            if (!_residentWaterCycle.TryReserveToiletUse(
                    _resourceFlow,
                    _toiletHolding,
                    $"sanitation-{_sanitationCycleCount + 1}:use-toilet",
                    "station:toilet",
                    _residentWaterCycle.Bladder.GetAmount(NomadResourceIds.HumanWaste),
                    out _activeResidentAction,
                    out _lastBlocker))
            {
                SetBlocked("使用厕所");
                return;
            }

            _residentAction = ResidentAction.UseToilet;
            _moveTarget = GroundPoint(_toiletInteractionPoint.position);
            _phase = SpikePhase.MovingToResidentAction;
            _currentTask = "Ada 使用厕所";
            _currentContract =
                "输入：Ada 当前全部膀胱内容物\n" +
                "输出：同体积进入厕所暂存桶；桶满会阻止本次行动。";
            SetResidentSemantic(ResidentAnimationSemantic.Move);
        }

        private void CompleteResidentAction()
        {
            ResidentAction completedAction = _residentAction;
            _activeResidentAction.Commit();
            _activeResidentAction = null;
            _residentAction = ResidentAction.None;
            _lastBlocker = ResourceFlowBlocker.None;
            RefreshOutputIndicators();

            if (completedAction == ResidentAction.Drink)
            {
                _phase = SpikePhase.Metabolizing;
                _currentTask = "水在体内延迟代谢";
                _currentContract =
                    "体内水仍是守恒物质；排泄压力会连续上升，代谢周期结束后才转为膀胱内容物。";
                SetResidentSemantic(ResidentAnimationSemantic.Idle);
            }
            else if (completedAction == ResidentAction.UseToilet)
            {
                BeginToiletWasteHaul();
            }
            else
            {
                throw new InvalidOperationException("居民水循环行动缺少后续阶段。 ");
            }
        }

        private void CompleteSanitationCycle()
        {
            _sanitationCycleCount++;
            _phase = SpikePhase.Completed;
            _currentTask = "饮水与排泄闭环完成";
            _currentContract =
                "清水经饮水台、体内代谢、厕所暂存与人工清运进入车辆废物罐；跨空间物质没有瞬移。";
            _lastBlocker = ResourceFlowBlocker.None;
            SetResidentSemantic(ResidentAnimationSemantic.Idle);
            RefreshOutputIndicators();
        }

        private void BeginProcessing()
        {
            var request = new ProcessTaskRequest(
                $"batch-{_batchIndex + 1}:prepare-meal",
                ResidentId,
                "制作一份餐食并把清洗污水留在污水罐",
                new[]
                {
                    new InventoryResourceQuantity(
                        _kitchenFoodInput,
                        NomadResourceIds.FoodIngredient,
                        1),
                    new InventoryResourceQuantity(
                        _kitchenWaterInput,
                        NomadResourceIds.Water,
                        500),
                },
                new[]
                {
                    new InventoryResourceQuantity(
                        _kitchenMealOutput,
                        NomadResourceIds.PreparedMeal,
                        1),
                    new InventoryResourceQuantity(
                        _kitchenWasteOutput,
                        NomadResourceIds.WasteWater,
                        500),
                },
                new[] { "station:kitchen-work" });

            if (!_resourceFlow.TryReserveProcess(request, out _activeProcess, out _lastBlocker))
            {
                SetBlocked("制作餐食");
                return;
            }

            _moveTarget = GroundPoint(_workPoint.position);
            _phase = SpikePhase.MovingToKitchenWork;
            _currentTask = "制作餐食";
            _currentContract =
                "输入：厨房食材 1 件 + 清水 500 mL\n" +
                "输出：餐食 1 件 + 污水 500 mL（本地容量已预留）";
            SetResidentSemantic(ResidentAnimationSemantic.Move);
        }

        private void CompleteProcessing()
        {
            _activeProcess.Commit();
            _activeProcess = null;
            _doorTarget = 0f;
            _lastBlocker = ResourceFlowBlocker.None;
            RefreshOutputIndicators();
            BeginMealOutputHaul();
        }

        private void CompleteBatch()
        {
            _batchIndex++;
            _phase = SpikePhase.Completed;
            _currentTask = $"第 {_batchIndex} 批完成";
            _currentContract = "输入、厨房本地输出和两段成品清运均已结算；跨空间资源没有瞬移。";
            _doorTarget = 0f;
            _lastBlocker = ResourceFlowBlocker.None;
            SetResidentSemantic(ResidentAnimationSemantic.Idle);
            RefreshOutputIndicators();
        }

        private void SetBlocked(string attemptedTask)
        {
            _phase = SpikePhase.Blocked;
            _currentTask = attemptedTask + "被阻塞";
            _currentContract = DescribeBlocker(_lastBlocker);
            _doorTarget = 0f;
            SetResidentSemantic(ResidentAnimationSemantic.Idle);
        }

        private void BeginTimedPhase(
            SpikePhase phase,
            float duration,
            ResidentAnimationSemantic semantic)
        {
            _phase = phase;
            _phaseRemaining = duration;
            SetResidentSemantic(semantic);
        }

        private bool TickTimer(float deltaTime)
        {
            _phaseRemaining -= deltaTime;
            return _phaseRemaining <= 0f;
        }

        private bool MoveResident(float deltaTime, Vector3 target)
        {
            Vector3 current = _residentRoot.position;
            Vector3 next = Vector3.MoveTowards(current, target, residentMoveSpeed * deltaTime);
            Vector3 direction = next - current;
            _residentRoot.position = next;
            if (direction.sqrMagnitude > 0.000001f)
            {
                direction.y = 0f;
                if (direction.sqrMagnitude > 0.000001f)
                    _residentRoot.rotation = Quaternion.Slerp(
                        _residentRoot.rotation,
                        Quaternion.LookRotation(direction.normalized, Vector3.up),
                        Mathf.Clamp01(deltaTime * 9f));
            }
            return (target - next).sqrMagnitude <= 0.0001f;
        }

        private void TickDoor(float deltaTime)
        {
            _doorOpenAmount = Mathf.MoveTowards(_doorOpenAmount, _doorTarget, deltaTime * 2.7f);
            if (_centerDoorHinge != null)
            {
                _centerDoorHinge.localRotation =
                    _centerDoorClosedRotation * Quaternion.Euler(0f, 102f * _doorOpenAmount, 0f);
            }
        }

        private void AnimateResident(float deltaTime)
        {
            if (_humanoidPresentation != null)
            {
                _humanoidPresentation.SetPlaybackSpeed(_simulationPaused ? 0f : simulationSpeed);
                return;
            }
            if (_residentVisual == null) return;

            bool moving = _phase == SpikePhase.MovingToPickup ||
                          _phase == SpikePhase.MovingToDelivery ||
                          _phase == SpikePhase.MovingToKitchenWork ||
                          _phase == SpikePhase.MovingToResidentAction;
            float bob = moving && !_simulationPaused ? Mathf.Sin(_visualClock * 11f) * 0.035f : 0f;
            Vector3 local = _residentVisual.localPosition;
            local.y = bob;
            _residentVisual.localPosition = Vector3.Lerp(
                _residentVisual.localPosition,
                local,
                Mathf.Clamp01(deltaTime * 12f));
        }

        private void SetResidentSemantic(ResidentAnimationSemantic semantic)
        {
            _humanoidPresentation?.SetSemantic(semantic);
        }

        private void BuildWorld()
        {
            CreatePrimitive(
                "Desert",
                PrimitiveType.Cube,
                new Vector3(0f, -0.55f, 0f),
                new Vector3(30f, 0.4f, 24f),
                new Color(0.34f, 0.25f, 0.16f));
            CreatePrimitive(
                "VehicleDeck",
                PrimitiveType.Cube,
                new Vector3(0f, -0.14f, 0f),
                new Vector3(12f, 0.42f, 8f),
                new Color(0.18f, 0.22f, 0.23f));
            CreateDeckRails();
            CreatePantry();
            CreateWaterTank();
            CreateKitchen();
            CreateMealShelf();
            CreateWasteTank();
            CreateDrinkingStation();
            CreateToilet();
            CreateResident();
            SetupCameraAndLights();
            RefreshOutputIndicators();
            _worldBuilt = true;
        }

        private void CreateDeckRails()
        {
            Color rail = new(0.43f, 0.33f, 0.21f);
            CreatePrimitive("FrontBulkhead", PrimitiveType.Cube, new Vector3(-5.78f, 0.42f, 0f),
                new Vector3(0.28f, 1.05f, 7.5f), rail);
            CreatePrimitive("RearRail", PrimitiveType.Cube, new Vector3(5.82f, 0.22f, 0f),
                new Vector3(0.16f, 0.5f, 7.5f), rail);
            CreatePrimitive("UpperRail", PrimitiveType.Cube, new Vector3(0f, 0.22f, 3.75f),
                new Vector3(11.4f, 0.5f, 0.16f), rail);
            CreatePrimitive("LowerRail", PrimitiveType.Cube, new Vector3(0f, 0.22f, -3.75f),
                new Vector3(11.4f, 0.5f, 0.16f), rail);
        }

        private void CreatePantry()
        {
            var root = new GameObject("Pantry_Source");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = new Vector3(-3.75f, 0f, -2.1f);
            CreatePrimitive("PantryBody", PrimitiveType.Cube, new Vector3(0f, 0.55f, 0f),
                new Vector3(1.55f, 1.1f, 1.15f), new Color(0.25f, 0.46f, 0.43f), root.transform, true);
            CreatePrimitive("FoodCrate", PrimitiveType.Cube, new Vector3(0f, 1.2f, 0f),
                new Vector3(1.05f, 0.28f, 0.78f), new Color(0.78f, 0.45f, 0.17f), root.transform, true);
            _foodPickupPoint = CreatePoint(root.transform, "Interaction_Pantry", new Vector3(0f, 0f, -1.05f));
        }

        private void CreateWaterTank()
        {
            var root = new GameObject("CleanWaterTank_Source");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = new Vector3(-3.65f, 0f, 1.75f);
            CreatePrimitive("Tank", PrimitiveType.Cylinder, new Vector3(0f, 0.72f, 0f),
                new Vector3(0.95f, 0.72f, 0.95f), new Color(0.12f, 0.51f, 0.72f), root.transform, true);
            CreatePrimitive("TankBand", PrimitiveType.Cylinder, new Vector3(0f, 1.22f, 0f),
                new Vector3(1.02f, 0.1f, 1.02f), new Color(0.78f, 0.5f, 0.2f), root.transform, true);
            _waterPickupPoint = CreatePoint(root.transform, "Interaction_CleanWater", new Vector3(0f, 0f, -1.05f));
        }

        private void CreateKitchen()
        {
            GameObject kitchen;
            if (fieldKitchenPrefab != null)
            {
                kitchen = Instantiate(fieldKitchenPrefab, transform, false);
                kitchen.name = "FieldKitchen_Processing";
            }
            else
            {
                kitchen = CreateFallbackKitchen();
            }

            kitchen.transform.localPosition = new Vector3(1f, 0.08f, 1.65f);
            kitchen.transform.localRotation = Quaternion.identity;
            kitchen.transform.localScale = Vector3.one;
            _kitchenRoot = kitchen.transform;

            Transform anchors = _kitchenRoot.Find("Anchors");
            _workPoint = FindOrCreatePoint(anchors, "WorkPosition", new Vector3(0f, 0f, -1.15f));
            Transform storage = FindOrCreatePoint(anchors, "StorageAccess", new Vector3(0f, 0f, -0.9f));
            Transform water = FindOrCreatePoint(anchors, "WaterInput", new Vector3(1.55f, 0f, 0.15f));
            Transform waste = FindOrCreatePoint(anchors, "WasteOutput", new Vector3(1.55f, 0f, 0.25f));
            _foodDeliveryPoint = CreateApproachPoint(storage, "Approach_FoodInput", Vector3.back * 0.72f);
            _waterDeliveryPoint = CreateApproachPoint(water, "Approach_WaterInput", Vector3.right * 0.7f);
            _mealPickupPoint = CreateApproachPoint(storage, "Approach_MealOutput", Vector3.back * 0.72f);
            _wastePickupPoint = CreateApproachPoint(waste, "Approach_WasteOutput", Vector3.right * 0.7f);

            _kitchenMealIndicator = CreatePrimitive(
                "KitchenMealOutput_Visual",
                PrimitiveType.Cylinder,
                new Vector3(0.57f, 1.12f, -0.15f),
                new Vector3(0.34f, 0.045f, 0.34f),
                new Color(0.95f, 0.69f, 0.2f),
                _kitchenRoot,
                true).transform;
            _kitchenWasteIndicator = CreatePrimitive(
                "KitchenWasteOutput_Visual",
                PrimitiveType.Cylinder,
                new Vector3(1.32f, 0.35f, 0.22f),
                new Vector3(0.24f, 0.3f, 0.24f),
                new Color(0.42f, 0.64f, 0.22f),
                _kitchenRoot,
                true).transform;

            _centerDoorHinge = FindDeepChild(_kitchenRoot, "Hinge_Center");
            if (_centerDoorHinge != null) _centerDoorClosedRotation = _centerDoorHinge.localRotation;
        }

        private GameObject CreateFallbackKitchen()
        {
            var root = new GameObject("FieldKitchen_GrayboxFallback");
            root.transform.SetParent(transform, false);
            CreatePrimitive("Body", PrimitiveType.Cube, new Vector3(0f, 0.95f, 0f),
                new Vector3(2.3f, 1.9f, 0.88f), new Color(0.16f, 0.44f, 0.4f), root.transform, true);
            Transform hinge = CreatePoint(root.transform, "Hinge_Center", new Vector3(-0.34f, 0.9f, -0.46f));
            CreatePrimitive("Door_Center_Visual", PrimitiveType.Cube, new Vector3(0.34f, 0f, 0f),
                new Vector3(0.65f, 0.85f, 0.06f), new Color(0.72f, 0.76f, 0.72f), hinge, true);
            Transform anchors = CreatePoint(root.transform, "Anchors", Vector3.zero);
            CreatePoint(anchors, "WorkPosition", new Vector3(0f, 0f, -1.15f));
            CreatePoint(anchors, "StorageAccess", new Vector3(0f, 0f, -0.55f));
            CreatePoint(anchors, "WaterInput", new Vector3(1.15f, 0f, 0.2f));
            CreatePoint(anchors, "WasteOutput", new Vector3(1.15f, 0f, 0.28f));
            return root;
        }

        private void CreateMealShelf()
        {
            var root = new GameObject("PreparedMealShelf_Output");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = new Vector3(4.35f, 0f, -1.95f);
            CreatePrimitive("Shelf", PrimitiveType.Cube, new Vector3(0f, 0.45f, 0f),
                new Vector3(1.35f, 0.9f, 0.9f), new Color(0.48f, 0.34f, 0.23f), root.transform, true);
            _mealIndicator = CreatePrimitive("PreparedMeals", PrimitiveType.Cylinder, new Vector3(0f, 1.02f, 0f),
                new Vector3(0.75f, 0.08f, 0.75f), new Color(0.91f, 0.67f, 0.22f), root.transform, true).transform;
            _mealDeliveryPoint = CreatePoint(root.transform, "Interaction_MealShelf", new Vector3(0f, 0f, -1f));
        }

        private void CreateWasteTank()
        {
            var root = new GameObject("VehicleWasteTank_Output");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = new Vector3(4.25f, 0f, 1.95f);
            CreatePrimitive("WasteTank", PrimitiveType.Cylinder, new Vector3(0f, 0.58f, 0f),
                new Vector3(0.82f, 0.58f, 0.82f), new Color(0.29f, 0.34f, 0.25f), root.transform, true);
            _wasteIndicator = CreatePrimitive("WasteFill", PrimitiveType.Cylinder, new Vector3(0f, 0.25f, 0f),
                new Vector3(0.62f, 0.05f, 0.62f), new Color(0.48f, 0.7f, 0.23f), root.transform, true).transform;
            _wasteDeliveryPoint = CreatePoint(root.transform, "Interaction_WasteTank", new Vector3(0f, 0f, -1f));
        }

        private void CreateDrinkingStation()
        {
            var root = new GameObject("DrinkingStation_Buffer");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = new Vector3(-1.65f, 0f, 0.45f);
            CreatePrimitive("Pedestal", PrimitiveType.Cube, new Vector3(0f, 0.48f, 0f),
                new Vector3(0.82f, 0.96f, 0.72f), new Color(0.2f, 0.4f, 0.43f), root.transform, true);
            CreatePrimitive("Tap", PrimitiveType.Cube, new Vector3(0f, 1.04f, 0.04f),
                new Vector3(0.45f, 0.18f, 0.38f), new Color(0.68f, 0.72f, 0.7f), root.transform, true);
            _drinkingWaterIndicator = CreatePrimitive(
                "BufferedWater",
                PrimitiveType.Cylinder,
                new Vector3(0f, 1.19f, -0.08f),
                new Vector3(0.2f, 0.1f, 0.2f),
                new Color(0.12f, 0.58f, 0.82f),
                root.transform,
                true).transform;
            _drinkingDeliveryPoint = CreatePoint(
                root.transform,
                "Interaction_DrinkingDelivery",
                new Vector3(-0.7f, 0f, -0.65f));
            _drinkingInteractionPoint = CreatePoint(
                root.transform,
                "Interaction_Drink",
                new Vector3(0.35f, 0f, -0.82f));
        }

        private void CreateToilet()
        {
            var root = new GameObject("DryToilet_Holding");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = new Vector3(1.2f, 0f, -2.35f);
            CreatePrimitive("ToiletBase", PrimitiveType.Cube, new Vector3(0f, 0.42f, 0f),
                new Vector3(0.9f, 0.84f, 1.05f), new Color(0.34f, 0.37f, 0.34f), root.transform, true);
            CreatePrimitive("Seat", PrimitiveType.Cylinder, new Vector3(0f, 0.88f, -0.08f),
                new Vector3(0.48f, 0.07f, 0.55f), new Color(0.72f, 0.69f, 0.58f), root.transform, true);
            CreatePrimitive("PrivacyBack", PrimitiveType.Cube, new Vector3(0f, 1.12f, 0.46f),
                new Vector3(0.95f, 1.45f, 0.12f), new Color(0.25f, 0.29f, 0.27f), root.transform, true);
            _toiletWasteIndicator = CreatePrimitive(
                "HoldingCanisterFill",
                PrimitiveType.Cylinder,
                new Vector3(0f, 0.18f, 0f),
                new Vector3(0.46f, 0.1f, 0.5f),
                new Color(0.57f, 0.45f, 0.2f),
                root.transform,
                true).transform;
            _toiletInteractionPoint = CreatePoint(
                root.transform,
                "Interaction_Toilet",
                new Vector3(0f, 0f, -1.05f));
            _toiletWastePickupPoint = CreatePoint(
                root.transform,
                "Interaction_ToiletCanister",
                new Vector3(0.72f, 0f, -0.75f));
        }

        private void CreateResident()
        {
            var root = new GameObject("Resident_Ada_Logistics");
            root.transform.SetParent(transform, false);
            _residentStart = new Vector3(-0.7f, 0.12f, -1.25f);
            root.transform.localPosition = _residentStart;
            _residentRoot = root.transform;

            var presentation = root.AddComponent<ResidentHumanoidPresentation>();
            if (presentation.TryInitialize(humanoidPrefab, humanoidController))
            {
                _humanoidPresentation = presentation;
                _residentVisual = presentation.VisualRoot;
            }
            else
            {
                Destroy(presentation);
                var visual = new GameObject("ProceduralResidentVisual");
                visual.transform.SetParent(root.transform, false);
                _residentVisual = visual.transform;
                CreatePrimitive("Body", PrimitiveType.Capsule, new Vector3(0f, 0.82f, 0f),
                    new Vector3(0.58f, 0.72f, 0.5f), new Color(0.18f, 0.62f, 0.68f), visual.transform, true);
                CreatePrimitive("Head", PrimitiveType.Sphere, new Vector3(0f, 1.64f, 0f),
                    new Vector3(0.43f, 0.43f, 0.43f), new Color(0.84f, 0.65f, 0.5f), visual.transform, true);
            }

            Transform cargoRoot = CreatePoint(root.transform, "CarriedCargo", new Vector3(0f, 0.82f, 0.42f));
            _foodCargoVisual = CreatePrimitive("FoodIngredient_Cargo", PrimitiveType.Cube, Vector3.zero,
                new Vector3(0.42f, 0.32f, 0.38f), new Color(0.84f, 0.46f, 0.14f), cargoRoot, true);
            _waterCargoVisual = CreatePrimitive("WaterCanister_Cargo", PrimitiveType.Cylinder, Vector3.zero,
                new Vector3(0.34f, 0.28f, 0.34f), new Color(0.12f, 0.58f, 0.82f), cargoRoot, true);
            _mealCargoVisual = CreatePrimitive("PreparedMeal_Cargo", PrimitiveType.Cylinder, Vector3.zero,
                new Vector3(0.38f, 0.075f, 0.38f), new Color(0.95f, 0.69f, 0.2f), cargoRoot, true);
            _wasteCargoVisual = CreatePrimitive("WasteWater_Cargo", PrimitiveType.Cylinder, Vector3.zero,
                new Vector3(0.3f, 0.32f, 0.3f), new Color(0.42f, 0.64f, 0.22f), cargoRoot, true);
            HideCargo();
        }

        private void SetupCameraAndLights()
        {
            Camera cameraComponent = FindMainCameraInOwnScene();
            GameObject cameraObject;
            if (cameraComponent == null)
            {
                cameraObject = new GameObject("Main Camera");
                cameraObject.transform.SetParent(transform, false);
                cameraObject.tag = "MainCamera";
                cameraComponent = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
            }
            else
            {
                cameraObject = cameraComponent.gameObject;
            }

            cameraComponent.orthographic = true;
            cameraComponent.orthographicSize = 7.2f;
            cameraComponent.nearClipPlane = 0.1f;
            cameraComponent.farClipPlane = 80f;
            cameraComponent.backgroundColor = new Color(0.1f, 0.085f, 0.07f);
            cameraComponent.rect = new Rect(0.38f, 0f, 0.62f, 1f);
            cameraObject.transform.position = new Vector3(10.8f, 12.8f, -11.8f);
            cameraObject.transform.LookAt(new Vector3(0f, 0.35f, 0f));

            var keyObject = new GameObject("Sun_Key");
            keyObject.transform.SetParent(transform, false);
            Light key = keyObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.45f;
            key.color = new Color(1f, 0.82f, 0.65f);
            keyObject.transform.rotation = Quaternion.Euler(48f, -38f, 0f);

            var fillObject = new GameObject("Sky_Fill");
            fillObject.transform.SetParent(transform, false);
            Light fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.42f;
            fill.color = new Color(0.45f, 0.62f, 1f);
            fillObject.transform.rotation = Quaternion.Euler(52f, 142f, 0f);
        }

        private Camera FindMainCameraInOwnScene()
        {
            Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera candidate = cameras[i];
                if (candidate.gameObject.scene == gameObject.scene && candidate.CompareTag("MainCamera"))
                    return candidate;
            }
            return null;
        }

        private GameObject CreatePrimitive(
            string objectName,
            PrimitiveType primitive,
            Vector3 position,
            Vector3 scale,
            Color color,
            Transform parent = null,
            bool localSpace = false)
        {
            GameObject instance = GameObject.CreatePrimitive(primitive);
            instance.name = objectName;
            instance.transform.SetParent(parent ?? transform, false);
            if (localSpace) instance.transform.localPosition = position;
            else instance.transform.position = position;
            instance.transform.localScale = scale;
            if (instance.TryGetComponent(out Collider collider)) collider.enabled = false;
            instance.GetComponent<Renderer>().sharedMaterial = CreateMaterial(color);
            return instance;
        }

        private Material CreateMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { color = color };
            _runtimeMaterials.Add(material);
            return material;
        }

        private static Transform CreatePoint(Transform parent, string name, Vector3 localPosition)
        {
            var point = new GameObject(name);
            point.transform.SetParent(parent, false);
            point.transform.localPosition = localPosition;
            return point.transform;
        }

        private static Transform FindOrCreatePoint(Transform parent, string name, Vector3 fallbackLocalPosition)
        {
            if (parent == null) throw new InvalidOperationException("厨房缺少 Anchors 根节点。 ");
            Transform existing = parent.Find(name);
            return existing != null ? existing : CreatePoint(parent, name, fallbackLocalPosition);
        }

        private static Transform CreateApproachPoint(Transform anchor, string name, Vector3 worldOffset)
        {
            Transform point = CreatePoint(anchor, name, Vector3.zero);
            point.position = anchor.position + worldOffset;
            return point;
        }

        private static Transform FindDeepChild(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDeepChild(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        private static Vector3 GroundPoint(Vector3 point)
        {
            point.y = 0.12f;
            return point;
        }

        private void ShowCargo(ResourceId resource)
        {
            _foodCargoVisual.SetActive(resource == NomadResourceIds.FoodIngredient);
            _waterCargoVisual.SetActive(resource == NomadResourceIds.Water);
            _mealCargoVisual.SetActive(resource == NomadResourceIds.PreparedMeal);
            _wasteCargoVisual.SetActive(resource == NomadResourceIds.WasteWater);
            if (resource == NomadResourceIds.HumanWaste)
                _wasteCargoVisual.SetActive(true);
        }

        private void HideCargo()
        {
            if (_foodCargoVisual != null) _foodCargoVisual.SetActive(false);
            if (_waterCargoVisual != null) _waterCargoVisual.SetActive(false);
            if (_mealCargoVisual != null) _mealCargoVisual.SetActive(false);
            if (_wasteCargoVisual != null) _wasteCargoVisual.SetActive(false);
        }

        private void RefreshOutputIndicators()
        {
            if (_kitchenMealIndicator != null)
                _kitchenMealIndicator.gameObject.SetActive(KitchenMealOutput > 0);
            if (_kitchenWasteIndicator != null)
                _kitchenWasteIndicator.gameObject.SetActive(
                    KitchenWasteOutputMilliliters > 0);
            if (_mealIndicator != null)
            {
                int count = PreparedMeals;
                _mealIndicator.gameObject.SetActive(count > 0);
                _mealIndicator.localScale = new Vector3(0.75f, 0.08f + count * 0.055f, 0.75f);
            }
            if (_wasteIndicator != null)
            {
                int amountMilliliters = VehicleWasteTotalMilliliters;
                float fill = amountMilliliters / (float)_wasteTank.Capacity;
                _wasteIndicator.gameObject.SetActive(amountMilliliters > 0);
                _wasteIndicator.localPosition = new Vector3(0f, 0.18f + fill * 0.46f, 0f);
                _wasteIndicator.localScale = new Vector3(0.62f, 0.05f + fill * 0.36f, 0.62f);
            }
            if (_drinkingWaterIndicator != null)
                _drinkingWaterIndicator.gameObject.SetActive(
                    DrinkingStationWaterMilliliters > 0);
            if (_toiletWasteIndicator != null)
                _toiletWasteIndicator.gameObject.SetActive(
                    ToiletHoldingWasteMilliliters > 0);
        }

        private void ResetScenario()
        {
            InitializeInventories();
            _residentRoot.position = _residentStart;
            _residentRoot.rotation = Quaternion.identity;
            _doorTarget = 0f;
            _doorOpenAmount = 0f;
            if (_centerDoorHinge != null) _centerDoorHinge.localRotation = _centerDoorClosedRotation;
            RefreshOutputIndicators();
            BeginBatch();
        }

        private static string DescribeBlocker(ResourceFlowBlocker blocker)
        {
            string target = string.IsNullOrEmpty(blocker.InventoryId) ? string.Empty : $"（{blocker.InventoryId}）";
            return blocker.Reason switch
            {
                ResourceFlowBlockReason.SourceInsufficient => $"来源库存不足{target}：{blocker.Resource}",
                ResourceFlowBlockReason.CarrierFull => $"居民携带空间已满{target}",
                ResourceFlowBlockReason.DestinationFull => $"输出或目的库存已满{target}",
                ResourceFlowBlockReason.TaskAlreadyReserved => "同一个稳定任务已经被领取",
                ResourceFlowBlockReason.InteractionUnavailable => "所需交互位正被其他任务占用",
                _ => "没有阻塞",
            };
        }

        private void OnGUI()
        {
            EnsureGuiStyles();
            float width = Mathf.Min(420f, Screen.width * 0.36f);
            GUILayout.BeginArea(new Rect(18f, 18f, width, Screen.height - 36f), GUI.skin.box);
            GUILayout.Label("游牧工坊 · 物流与水循环 Foundation", _titleStyle);
            GUILayout.Label(
                "物质不会瞬移：厨房链验证实体搬运与加工，饮水链再验证连续需求、延迟代谢、如厕暂存和人工清运。",
                _bodyStyle);
            GUILayout.Space(8f);

            GUILayout.Label($"当前：{_currentTask}", _phase == SpikePhase.Blocked ? _warningStyle : _accentStyle);
            GUILayout.Label(_currentContract, _bodyStyle);
            GUILayout.Label($"阶段：{PhaseName(_phase)}", _bodyStyle);

            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_simulationPaused ? "继续" : "暂停"))
                SetSimulationPaused(!_simulationPaused);
            GUI.enabled = _phase == SpikePhase.Completed;
            if (GUILayout.Button("再制作一批")) RequestNextBatch();
            GUI.enabled = true;
            if (GUILayout.Button("重置场景")) ResetScenario();
            GUILayout.EndHorizontal();

            GUI.enabled = _phase == SpikePhase.Completed && _sanitationCycleCount == 0;
            if (GUILayout.Button("运行一次饮水 → 排泄 → 厕所清运链"))
                RequestWaterSanitationCycle();
            GUI.enabled = true;

            GUILayout.Space(6f);
            _guiScroll = GUILayout.BeginScrollView(_guiScroll, false, true);
            GUILayout.Label("真实库存 / 总容量", _accentStyle);
            DrawInventory("食材储柜", PantryFood, _pantry.Capacity, ResourceMeasure.Item);
            DrawInventory("净水箱", WaterTankMilliliters, _waterTank.Capacity, ResourceMeasure.Milliliter);
            DrawInventory(
                "Ada 物品携带",
                _residentItemCarry.TotalAmount,
                _residentItemCarry.Capacity,
                ResourceMeasure.Item);
            DrawInventory(
                "Ada 液体容器",
                _residentLiquidCarry.TotalAmount,
                _residentLiquidCarry.Capacity,
                ResourceMeasure.Milliliter);
            DrawInventory("厨房食材口", _kitchenFoodInput.TotalAmount, _kitchenFoodInput.Capacity, ResourceMeasure.Item);
            DrawInventory("厨房进水口", _kitchenWaterInput.TotalAmount, _kitchenWaterInput.Capacity, ResourceMeasure.Milliliter);
            DrawInventory("厨房餐食口", KitchenMealOutput, _kitchenMealOutput.Capacity, ResourceMeasure.Item);
            DrawInventory("厨房污水口", KitchenWasteOutputMilliliters, _kitchenWasteOutput.Capacity, ResourceMeasure.Milliliter);
            DrawInventory("成品餐架", PreparedMeals, _mealShelf.Capacity, ResourceMeasure.Item);
            DrawInventory("车辆废物罐", VehicleWasteTotalMilliliters, _wasteTank.Capacity, ResourceMeasure.Milliliter);
            GUILayout.Label(
                $"其中：厨房污水 {ResourceAmountFormatting.Format(WasteWaterMilliliters, ResourceMeasure.Milliliter)} / " +
                $"人体排泄物 {ResourceAmountFormatting.Format(HumanWasteMilliliters, ResourceMeasure.Milliliter)}",
                _bodyStyle);

            GUILayout.Space(6f);
            GUILayout.Label("Ada 生理状态与局部容器", _accentStyle);
            DrawMeter("口渴", Thirst);
            DrawMeter("排泄压力", ExcretionPressure);
            DrawInventory("饮水台", DrinkingStationWaterMilliliters, _drinkingStation.Capacity, ResourceMeasure.Milliliter);
            DrawInventory("体内待代谢水", BodyWaterMilliliters, _residentWaterCycle.BodyWater.Capacity, ResourceMeasure.Milliliter);
            DrawInventory("膀胱内容物", BladderWasteMilliliters, _residentWaterCycle.Bladder.Capacity, ResourceMeasure.Milliliter);
            DrawInventory("厕所暂存桶", ToiletHoldingWasteMilliliters, _toiletHolding.Capacity, ResourceMeasure.Milliliter);

            GUILayout.Space(5f);
            GUILayout.Label(
                "提示：车辆废物罐容量为 1 L。两批厨房污水会装满；继续生产时，无法清运的物质会留在原容器并明确显示阻塞。",
                _bodyStyle);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private static void DrawMeter(string label, float value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(112f));
            Rect rect = GUILayoutUtility.GetRect(150f, 16f, GUILayout.ExpandWidth(true));
            GUI.Box(rect, GUIContent.none);
            float ratio = Mathf.Clamp01(value);
            Color previous = GUI.color;
            GUI.color = Color.Lerp(new Color(0.22f, 0.67f, 0.55f), new Color(0.88f, 0.35f, 0.2f), ratio);
            GUI.Box(new Rect(rect.x + 2f, rect.y + 2f, (rect.width - 4f) * ratio, rect.height - 4f), GUIContent.none);
            GUI.color = previous;
            GUILayout.Label($"{ratio * 100f:0}%", GUILayout.Width(52f));
            GUILayout.EndHorizontal();
        }

        private static void DrawInventory(
            string label,
            int amount,
            int capacity,
            ResourceMeasure measure)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(112f));
            Rect rect = GUILayoutUtility.GetRect(150f, 16f, GUILayout.ExpandWidth(true));
            GUI.Box(rect, GUIContent.none);
            float ratio = capacity <= 0 ? 0f : (float)amount / capacity;
            Color previous = GUI.color;
            GUI.color = ratio >= 0.999f ? new Color(0.88f, 0.35f, 0.2f) : new Color(0.22f, 0.67f, 0.55f);
            GUI.Box(new Rect(rect.x + 2f, rect.y + 2f, (rect.width - 4f) * ratio, rect.height - 4f), GUIContent.none);
            GUI.color = previous;
            GUILayout.Label(
                $"{ResourceAmountFormatting.Format(amount, measure)} / " +
                ResourceAmountFormatting.Format(capacity, measure),
                GUILayout.Width(108f));
            GUILayout.EndHorizontal();
        }

        private void EnsureGuiStyles()
        {
            if (_titleStyle != null) return;
            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
            };
            _bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                wordWrap = true,
            };
            _accentStyle = new GUIStyle(_bodyStyle) { fontStyle = FontStyle.Bold };
            _accentStyle.normal.textColor = new Color(0.96f, 0.77f, 0.28f);
            _warningStyle = new GUIStyle(_accentStyle);
            _warningStyle.normal.textColor = new Color(1f, 0.34f, 0.2f);
        }

        private static string PhaseName(SpikePhase phase)
        {
            return phase switch
            {
                SpikePhase.MovingToPickup => "前往来源",
                SpikePhase.PickingUp => "拾取并结算到随身库存",
                SpikePhase.MovingToDelivery => "携带中",
                SpikePhase.Delivering => "送达目的库存",
                SpikePhase.MovingToKitchenWork => "前往厨房工作位",
                SpikePhase.Processing => "加工中",
                SpikePhase.MovingToResidentAction => "前往生理设施",
                SpikePhase.ResidentAction => "生理行动提交中",
                SpikePhase.Metabolizing => "体内延迟代谢",
                SpikePhase.Completed => "流程已完成",
                SpikePhase.Blocked => "被容量或资源阻塞",
                _ => "初始化",
            };
        }
    }
}
