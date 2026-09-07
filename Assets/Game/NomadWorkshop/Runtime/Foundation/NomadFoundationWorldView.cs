using System;
using System.Collections.Generic;
using Game.Framework.Common;
using Game.Framework.UI;
using Game.Framework.View;
using Game.NomadWorkshop.Simulation;
using R3;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>
    /// Foundation 的 3D 表现与桌面 / 触屏输入 View。它只订阅读模型、发送离散 Command；设施 Transform、幽灵和居民模型
    /// 都由逻辑状态派生，替换成正式 Prefab 时无需改摆放或水循环规则。
    /// </summary>
    public sealed class NomadFoundationWorldView : MonoViewBase
    {
        [Header("场景引用")]
        [SerializeField, Tooltip("主甲板尺寸、量化坐标与默认吸附设置的共享定义。")]
        private DeckLayoutDefinition deckLayout;
        [SerializeField, Tooltip("用于把设施业务记录转换为可替换 3D 表现的定义目录。")]
        private NomadFacilityDefinition[] facilityDefinitions =
            Array.Empty<NomadFacilityDefinition>();
        [SerializeField, Tooltip("可搬动物品的占地与灰盒表现目录；运行位置仍由 Model 的区域姿态投影决定。")]
        private NomadWorldItemDefinition[] worldItemDefinitions =
            Array.Empty<NomadWorldItemDefinition>();
        [SerializeField, Tooltip("车辆甲板的表现空间；设施、居民、网格和镜头焦点都使用它的局部坐标。")]
        private Transform deckRoot;
        private Transform _stopVisualRoot;
        private Transform _stopWorldItemRoot;
        private string _carriedWasteBucketFacilityId = string.Empty;
        private readonly Dictionary<string, WasteBucketVisual> _wasteBucketVisuals = new(StringComparer.Ordinal);
        [SerializeField, Tooltip("Foundation 世界相机。View 只操作表现镜头，不改玩法状态。")]
        private Camera worldCamera;
        [SerializeField, Tooltip("灰盒主方向光；替换正式灯光方案后可继续由表现层拥有。")]
        private Light keyLight;
        [SerializeField, Tooltip("灰盒冷色补光；用于保留背光面和深色部件的轮廓，不参与玩法逻辑。")]
        private Light fillLight;
        [SerializeField, Tooltip("Foundation 的程序化天空盒；同时提供环境漫反射和 PBR 高光的远景反射。留空时回退到稳定纯色灰盒。")]
        private Material skyboxMaterial;
        [SerializeField, Tooltip("覆盖车辆甲板的局部反射探针。灰盒几何在运行时生成，因此由本 View 在首帧完成后主动捕获一次。")]
        private ReflectionProbe reflectionProbe;
        [SerializeField, Tooltip("当前场景的 HUD View；用于按真实可见矩形阻止 UI 下方的建造与镜头输入。隔离测试可留空并使用同布局的保守回退。")]
        private NomadFoundationDebugView screenUi;

        [Header("可选美术样板")]
        [SerializeField, Tooltip("可选甲板/车架表现 Prefab；不带玩法 Collider，不改变共享布局。")]
        private GameObject vehicleVisualPrefab;
        [SerializeField, Tooltip("可选荒漠地景，只包含 Renderer，不参与导航与建造。")]
        private GameObject environmentVisualPrefab;
        [SerializeField, Tooltip("可选驿站外观，由实际停靠状态控制可见性。")]
        private GameObject waystationVisualPrefab;
        [SerializeField, Tooltip("可选 Humanoid 模型。与 Controller 同时配置后取代胶囊表现。")]
        private GameObject residentVisualPrefab;
        [SerializeField, Tooltip("可选手提容器 Prefab，需有完整 FoundationCarriedContainerRig；留空使用原灰盒。")]
        private GameObject waterCanVisualPrefab;
        [SerializeField, Tooltip("可选：按居民稳定 id 固定外观。没有对应项时使用默认模型；不按列表顺序或随机数选择。")]
        private ResidentVisualBinding[] residentVisualBindings = Array.Empty<ResidentVisualBinding>();
        [SerializeField, Tooltip("共享五语义动作 Controller；持物需要启用该 Controller 的 IK Pass。")]
        private RuntimeAnimatorController residentAnimationController;
        private bool _animationPaused;
        private float _animationMultiplier = 1f;
        private FoundationBuildTransactionPhase _animationBuildPhase;
        private FoundationReadModel _journeyReadModel;
        private FoundationJourneyPresentation _journeyPresentation;
        private FoundationRoofPresentation _roofPresentation;

        /// <summary>当前模型的可选屋顶显示会话；生命周期由本 View 的 Bag 拥有。</summary>
        public FoundationRoofPresentation RoofPresentation => _roofPresentation;
        private Color ClearWeatherKeyColor => vehicleVisualPrefab != null
            ? new Color(1f, .96f, .88f) : new Color(1f, .89f, .72f);
        private float ClearWeatherKeyIntensity => vehicleVisualPrefab != null ? 1.3f : 1.6f;

        [Header("图形基线")]
        [SerializeField, Tooltip("进入场景后是否为实时 Reflection Probe 捕获一次车辆周围环境。只捕获一次，不会每帧更新；低端平台可关闭。")]
        private bool captureReflectionProbeOnStart = true;

        [Header("镜头操作")]
        [SerializeField, Min(0f), Tooltip("按住鼠标中键拖动时，每个屏幕像素对应的轨道旋转角度。")]
        private float cameraOrbitDegreesPerPixel = 0.2f;
        [SerializeField, Min(0f), Tooltip("滚轮每个输入单位改变的镜头距离。滚轮单位由设备驱动决定，因此与触屏捏合分别调节；当前值保留人工试玩后的 1.0。")]
        private float cameraZoomMetersPerScrollUnit = 1f;
        [SerializeField, Min(0f), Tooltip("双指同向拖动时，每个屏幕像素对应的轨道旋转角度。手指动作尺度与鼠标不同，所以不强行共用灵敏度。")]
        private float cameraTouchOrbitDegreesPerPixel = 0.12f;
        [SerializeField, Min(0f), Tooltip("双指距离每变化一个屏幕像素时改变的镜头距离（米）。张开拉近、捏合拉远。")]
        private float cameraPinchZoomMetersPerPixel = 0.04f;

        private readonly Dictionary<string, NomadFacilityDefinition> _definitions =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, NomadWorldItemDefinition> _worldItemDefinitions =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, Material> _worldItemMaterials =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, FacilityVisual> _facilityVisuals =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, FoundationFacilityAccessState> _facilityAccess =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, FoundationFacilityConditionState> _facilityConditions =
            new(StringComparer.Ordinal);
        private readonly List<Material> _runtimeMaterials = new();
        private readonly List<Renderer> _ghostRenderers = new();
        private readonly List<Renderer[]> _interactionSlotRenderers = new();
        private MaterialPropertyBlock _facilityStatusProperties;

        private FoundationBuildOption[] _buildOptions = Array.Empty<FoundationBuildOption>();
        private FoundationPlacementPreviewState _preview;
        private FoundationFacilityState[] _facilityStates = Array.Empty<FoundationFacilityState>();
        private Transform _facilityRoot;
        private Transform _worldItemRoot;
        private Transform _gridRoot;
        private Transform _ghostRoot;
        private Transform _interactionPreviewRoot;
        private readonly Dictionary<string, ResidentVisual> _residentVisuals = new(StringComparer.Ordinal);
        private readonly Dictionary<string, GameObject> _residentPrefabs = new(StringComparer.Ordinal);
        private string _waterCanCarrierId = string.Empty;
        private string _wasteBucketCarrierId = string.Empty;

        [Serializable]
        private sealed class ResidentVisualBinding
        {
            [Tooltip("居民的 StableId，例如 resident-01。")]
            public string residentId;
            [Tooltip("该居民的普通 Humanoid 包装 Prefab；外观仅归表现层所有。")]
            public GameObject prefab;
        }

        private sealed class ResidentVisual
        {
            internal string StableId;
            internal Transform Root;
            internal Transform Body;
            internal Transform CarryAnchor;
            internal Material Material;
            internal ResidentHumanoidPresentation Humanoid;
            internal FoundationResidentCarryIK CarryIK;
            internal FoundationResidentPhase Phase;
            internal FoundationFacilityWorkState FacilityWork;
            internal bool HasCarriedItem;
            internal Vector3 PreviousPosition;
            internal float AnimationMoveSpeed;
            internal float StandingShoulderHeight;
        }

        private Transform FindResidentRoot(string id) =>
            _residentVisuals.TryGetValue(id ?? string.Empty, out var visual) ? visual.Root : null;
        private Transform _waterCanVisual;
        private FoundationCarriedContainerRig _waterCanRig;
        private Transform _waterCanFillVisual;
        private Transform _waterCanCap;
        private const float WaterCanPourDegrees = 100f;
        private FoundationWaterCanLocation _waterCanLocation;
        private string _waterCanAnchorFacilityInstanceId = string.Empty;
        private FoundationItemPlacementState _waterCanPlacement;
        private Material _ghostValidMaterial;
        private Material _ghostInvalidMaterial;
        private Material _ghostPartialMaterial;
        private Material _ghostPendingMaterial;
        private Material _slotReachableMaterial;
        private Material _slotBlockedMaterial;
        private Material _slotPendingMaterial;
        private Material _slotSharedMaterial;
        private Material _slotOccupiedMaterial;
        private Material _placementRegionMaterial;
        private string _previewVisualDefinitionId = string.Empty;
        private FoundationInteractionMode _interactionMode;
        private FoundationPlacementGridVisual _placementGrid;
        private FoundationOrbitCameraController _cameraController;
        private FoundationFacilityGrayboxFactory _grayboxFactory;
        private bool _showPlacementGrid;
        private int _positionSnapMillimeters;
        private int _lastPointerXMillimeters;
        private int _lastPointerZMillimeters;
        private bool _hasPointerPose;
        private bool _touchCameraGestureActive;
        private ParticleSystem _sandstormParticles;
        private NomadWeatherKind _currentWeather;
        private int _sandstormIntensityPermille;

#if UNITY_EDITOR
        /// <summary>只供未激活 GameObject 上的隔离测试装配；正式场景由 Editor Pipeline 接线。</summary>
        public void ConfigureForTests(
            DeckLayoutDefinition configuredLayout,
            NomadFacilityDefinition[] configuredDefinitions,
            Transform configuredDeckRoot,
            Camera configuredCamera,
            Light configuredLight,
            Light configuredFillLight = null,
            Material configuredSkyboxMaterial = null,
            ReflectionProbe configuredReflectionProbe = null,
            NomadFoundationDebugView configuredScreenUi = null,
            NomadWorldItemDefinition[] configuredWorldItemDefinitions = null)
        {
            deckLayout = configuredLayout;
            facilityDefinitions = configuredDefinitions;
            worldItemDefinitions = configuredWorldItemDefinitions ??
                                   Array.Empty<NomadWorldItemDefinition>();
            deckRoot = configuredDeckRoot;
            worldCamera = configuredCamera;
            keyLight = configuredLight;
            fillLight = configuredFillLight;
            skyboxMaterial = configuredSkyboxMaterial;
            reflectionProbe = configuredReflectionProbe;
            screenUi = configuredScreenUi;
        }
#endif

        protected override void Awake()
        {
            base.Awake();
            _facilityStatusProperties = new MaterialPropertyBlock();
            ValidateReferences();
            BuildDefinitionMap();
            BuildGrayboxWorld();

            FoundationReadModel readModel = this.ExecuteCommand(new GetFoundationReadModelCommand());
            _journeyReadModel = readModel;
            Bag.Subscribe(readModel.StopAccessOpen, visible => _stopVisualRoot.gameObject.SetActive(visible));
            Bag.Subscribe(readModel.CarriedWasteBucketFacilityId, id =>
            {
                _carriedWasteBucketFacilityId = id;
                UpdateWasteBucketLocations();
            });
            _buildOptions = this.ExecuteCommand(new GetFoundationBuildOptionsCommand());
            Bag.Subscribe(readModel.InteractionMode, OnInteractionModeChanged);
            Bag.Subscribe(readModel.PlacementPreview, OnPreviewChanged);
            Bag.Subscribe(readModel.PositionSnapMillimeters, value =>
            {
                _positionSnapMillimeters = value;
                _placementGrid?.Rebuild(value);
                UpdatePlacementGridVisibility();
            });
            Bag.Subscribe(readModel.ShowPlacementGrid, visible =>
            {
                _showPlacementGrid = visible;
                UpdatePlacementGridVisibility();
            });
            Bag.Subscribe(readModel.FacilityRevision, _ =>
            {
                RebuildFacilities(this.ExecuteCommand(new GetFoundationFacilitiesCommand()));
                // 建造与检查点恢复都会重建空间；旧路段的短寿命特效不跨这条边界保留。
                _journeyPresentation?.ClearTransientEffects();
            });
            Bag.Subscribe(readModel.FacilityAccessRevision, _ =>
                UpdateFacilityAccess(
                    this.ExecuteCommand(new GetFoundationFacilityAccessCommand())));
            Bag.Subscribe(readModel.FacilityConditionRevision, _ =>
                UpdateFacilityConditions(
                    this.ExecuteCommand(new GetFoundationFacilityConditionsCommand())));
            Bag.Subscribe(readModel.CurrentWeather, weather =>
            {
                _currentWeather = weather;
                UpdateEnvironmentVisual();
            });
            Bag.Subscribe(readModel.SandstormIntensityPermille, intensity =>
            {
                _sandstormIntensityPermille = intensity;
                UpdateEnvironmentVisual();
            });
            Bag.Subscribe(readModel.WorldItemPlacementRevision, _ =>
                RebuildWorldItems(
                    this.ExecuteCommand(new GetFoundationWorldItemPlacementsCommand())));
            // 直接使用后端中立绑定引擎，为世界 GameObject 行持有独立订阅；不为 3D 表现依赖 UGUI。
            ReactiveListBinding.Bind(Bag, readModel.Residents,
                (resident, rowBag) =>
                {
                    ResidentVisual visual = BuildResidentVisual(resident);
                    rowBag.Add(Disposable.Create(() =>
                    {
                        _runtimeMaterials.Remove(visual.Material);
                        if (visual.Material != null) Destroy(visual.Material);
                    }));
                    rowBag.Subscribe(resident.ResidentLocalPosition, position => visual.Root.localPosition = position);
                    rowBag.Subscribe(resident.ResidentLocalYawDegrees, yaw => visual.Root.localRotation = Quaternion.Euler(0f, yaw, 0f));
                    rowBag.Subscribe(resident.ResidentPhase, phase => UpdateResidentBodyPose(visual, phase));
                    rowBag.Subscribe(resident.FacilityWork, work => visual.FacilityWork = work);
                    rowBag.Subscribe(resident.ResidentCarriedWorldItem, carried => RebuildCarriedWorldItem(visual, carried));
                    return visual;
                },
                (index, visual) => { visual.Root.SetSiblingIndex(index); UpdateWaterCanVisual(); UpdateWasteBucketLocations(); },
                visual =>
                {
                    _residentVisuals.Remove(visual.StableId);
                    if (visual.Root != null)
                    {
                        // 水罐和污物桶由世界拥有；移除居民行不能连带销毁挂在其手中的共享物体。
                        if (_waterCanVisual != null && _waterCanVisual.IsChildOf(visual.Root))
                            _waterCanVisual.SetParent(deckRoot, false);
                        foreach (var bucket in _wasteBucketVisuals.Values)
                            if (bucket.Root != null && bucket.Root.IsChildOf(visual.Root))
                                bucket.Root.SetParent(bucket.FacilityRoot, false);
                        visual.Root.SetParent(null, false);
                        Destroy(visual.Root.gameObject);
                    }
                },
                (index, visual) => visual.Root.SetSiblingIndex(index));
            Bag.Subscribe(readModel.IsPaused, value => _animationPaused = value);
            Bag.Subscribe(readModel.SimulationSpeed, value => _animationMultiplier = value);
            Bag.Subscribe(readModel.BuildTransactionPhase, value => _animationBuildPhase = value);
            Bag.Subscribe(readModel.WaterCanCarrierId, id => { _waterCanCarrierId = id; UpdateWaterCanVisual(); });
            Bag.Subscribe(readModel.WasteBucketCarrierId, id => { _wasteBucketCarrierId = id; UpdateWasteBucketLocations(); });
            Bag.Subscribe(readModel.WaterCanLocation, location =>
            {
                _waterCanLocation = location;
                UpdateWaterCanVisual();
            });
            Bag.Subscribe(readModel.WaterCanAnchorFacilityInstanceId, instanceId =>
            {
                _waterCanAnchorFacilityInstanceId = instanceId ?? string.Empty;
                UpdateWaterCanVisual();
            });
            Bag.Subscribe(readModel.WaterCanPlacement, placement =>
            {
                _waterCanPlacement = placement;
                UpdateWaterCanVisual();
            });
            Bag.Subscribe(readModel.WaterCanWaterMilliliters, amount =>
            {
                if (_waterCanFillVisual != null)
                    _waterCanFillVisual.gameObject.SetActive(amount > 0);
            });
        }

        private void Start()
        {
            // 甲板、设施和居民都是 Awake 时生成；延迟到 Start 才抓取，避免探针只看见空场景。
            // Null Graphics Device（例如纯逻辑测试）不做 GPU 捕获，保持验证稳定。
            if (!captureReflectionProbeOnStart || reflectionProbe == null ||
                !reflectionProbe.isActiveAndEnabled ||
                reflectionProbe.mode != ReflectionProbeMode.Realtime ||
                SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                return;
            reflectionProbe.RenderProbe();
        }

        private void Update()
        {
            UpdateResidentAnimations();
            UpdateFacilityWorkVisuals();
            if (_journeyPresentation != null)
                _journeyPresentation.Render(
                    _journeyReadModel.JourneyPositionMicrometers.CurrentValue,
                    _journeyReadModel.SimulationTick.CurrentValue,
                    _journeyReadModel.JourneyStatus.CurrentValue == NomadJourneyStatus.Moving);
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.hKey.wasPressedThisFrame)
                    _roofPresentation?.ToggleExteriorPreference();
                if (keyboard.bKey.wasPressedThisFrame)
                {
                    if (_interactionMode == FoundationInteractionMode.Build)
                        this.ExecuteCommand(new ExitFoundationBuildModeCommand());
                    else
                        this.ExecuteCommand(new EnterFoundationBuildModeCommand());
                }

                for (var i = 0;
                     _interactionMode == FoundationInteractionMode.Build &&
                     i < _buildOptions.Length && i < 9;
                     i++)
                {
                    if (WasBuildShortcutPressed(keyboard, i))
                        this.ExecuteCommand(new BeginFacilityPlacementCommand(_buildOptions[i].DefinitionId));
                }

                if (_preview.Active && keyboard.qKey.wasPressedThisFrame)
                    this.ExecuteCommand(new RotateFacilityPreviewCommand(-1));
                if (_preview.Active && keyboard.eKey.wasPressedThisFrame)
                    this.ExecuteCommand(new RotateFacilityPreviewCommand(1));
                if (keyboard.escapeKey.wasPressedThisFrame)
                {
                    if (_preview.Active)
                        this.ExecuteCommand(new CancelFacilityPlacementCommand());
                    else if (_interactionMode == FoundationInteractionMode.Build)
                        this.ExecuteCommand(new ExitFoundationBuildModeCommand());
                }
            }

            // 双指手势优先于触屏模拟出的鼠标事件；直到两根手指全部抬起前都吞掉该序列，
            // 避免捏合结束时的模拟左键意外确认建造。
            if (HandleTouchCameraInput()) return;

            Mouse mouse = Mouse.current;
            if (mouse == null || worldCamera == null) return;
            Vector2 pointer = mouse.position.ReadValue();
            bool overScreenUi = IsOverScreenUi(pointer);
            if (!overScreenUi)
            {
                if (mouse.middleButton.isPressed)
                    _cameraController?.Orbit(
                        mouse.delta.ReadValue(),
                        cameraOrbitDegreesPerPixel);
                float scroll = mouse.scroll.ReadValue().y;
                if (!Mathf.Approximately(scroll, 0f))
                    _cameraController?.Zoom(scroll, cameraZoomMetersPerScrollUnit);
            }

            if (!_preview.Active) return;
            if (!overScreenUi && TryGetPointerPose(pointer, out DeckPose pose))
            {
                if (!_hasPointerPose ||
                    pose.XMillimeters != _lastPointerXMillimeters ||
                    pose.ZMillimeters != _lastPointerZMillimeters)
                {
                    _hasPointerPose = true;
                    _lastPointerXMillimeters = pose.XMillimeters;
                    _lastPointerZMillimeters = pose.ZMillimeters;
                    this.ExecuteCommand(new MoveFacilityPreviewCommand(
                        pose.XMillimeters,
                        pose.ZMillimeters));
                }

                if (mouse.leftButton.wasPressedThisFrame)
                    this.ExecuteCommand(new ConfirmFacilityPlacementCommand());
            }

            if (!overScreenUi && mouse.rightButton.wasPressedThisFrame)
                this.ExecuteCommand(new RotateFacilityPreviewCommand(-1));
        }

        private bool HandleTouchCameraInput()
        {
            Touchscreen touchscreen = Touchscreen.current;
            if (touchscreen == null || worldCamera == null) return false;

            Vector2 firstPosition = default;
            Vector2 firstDelta = default;
            Vector2 secondPosition = default;
            Vector2 secondDelta = default;
            var activeTouchCount = 0;
            for (var i = 0; i < touchscreen.touches.Count; i++)
            {
                var touch = touchscreen.touches[i];
                if (!touch.press.isPressed) continue;

                if (activeTouchCount == 0)
                {
                    firstPosition = touch.position.ReadValue();
                    firstDelta = touch.delta.ReadValue();
                }
                else
                {
                    secondPosition = touch.position.ReadValue();
                    secondDelta = touch.delta.ReadValue();
                }

                activeTouchCount++;
                if (activeTouchCount == 2) break;
            }

            if (activeTouchCount >= 2)
            {
                _touchCameraGestureActive = true;
                Vector2 centroid = (firstPosition + secondPosition) * 0.5f;
                if (!IsOverScreenUi(centroid) &&
                    FoundationTwoPointerGestureUtility.TryCalculate(
                        firstPosition,
                        firstDelta,
                        secondPosition,
                        secondDelta,
                        out FoundationTwoPointerGesture gesture))
                {
                    if (gesture.OrbitDeltaPixels.sqrMagnitude > 0.0001f)
                        _cameraController?.Orbit(
                            gesture.OrbitDeltaPixels,
                            cameraTouchOrbitDegreesPerPixel);
                    if (!Mathf.Approximately(gesture.PinchDeltaPixels, 0f))
                        _cameraController?.Zoom(
                            gesture.PinchDeltaPixels,
                            cameraPinchZoomMetersPerPixel);
                }
                return true;
            }

            if (!_touchCameraGestureActive) return false;
            if (activeTouchCount == 0) _touchCameraGestureActive = false;
            return true;
        }

        private bool IsOverScreenUi(Vector2 pointer)
        {
            if (screenUi != null) return screenUi.IsScreenPointBlocked(pointer);

            // 隔离测试不一定装配 IMGUI View；回退仍按真实 HUD 布局计算，绝不恢复旧的大矩形魔法数。
            return FoundationHudLayout.IsScreenPointBlocked(
                pointer,
                Screen.width,
                Screen.height,
                _interactionMode == FoundationInteractionMode.Build);
        }

        private void ValidateReferences()
        {
            if (deckLayout == null)
                throw new MissingReferenceException("NomadFoundationWorldView 缺少 DeckLayoutDefinition。 ");
            if (deckRoot == null)
                throw new MissingReferenceException("NomadFoundationWorldView 缺少 Deck Root。 ");
            if (worldCamera == null)
                throw new MissingReferenceException("NomadFoundationWorldView 缺少 World Camera。 ");
            _residentPrefabs.Clear();
            foreach (ResidentVisualBinding binding in residentVisualBindings)
            {
                if (binding == null || string.IsNullOrWhiteSpace(binding.residentId) || binding.prefab == null)
                    throw new MissingReferenceException("居民外观绑定必须提供稳定 id 和模型 Prefab。");
                if (!_residentPrefabs.TryAdd(binding.residentId, binding.prefab))
                    throw new InvalidOperationException($"居民外观 id '{binding.residentId}' 重复。");
            }
            if (_residentPrefabs.Count > 0 && residentAnimationController == null)
                throw new MissingReferenceException("居民外观绑定需要共享动作 Controller。");
        }

        private void BuildDefinitionMap()
        {
            _definitions.Clear();
            for (var i = 0; i < facilityDefinitions.Length; i++)
            {
                NomadFacilityDefinition definition = facilityDefinitions[i];
                if (definition == null)
                    throw new MissingReferenceException($"WorldView 设施定义第 {i} 项为空。 ");
                definition.ValidateModelSpaceSnapshot();
                if (!_definitions.TryAdd(definition.Id, definition))
                    throw new InvalidOperationException($"WorldView 设施 id '{definition.Id}' 重复。 ");
                if (definition.Prefab != null)
                    foreach (var rig in definition.Prefab.GetComponentsInChildren<FoundationFacilityArtRig>(true))
                        rig.ValidateAgainst(definition);
            }

            _worldItemDefinitions.Clear();
            for (var i = 0; i < worldItemDefinitions.Length; i++)
            {
                NomadWorldItemDefinition definition = worldItemDefinitions[i];
                if (definition == null)
                    throw new MissingReferenceException($"WorldView 世界物品定义第 {i} 项为空。 ");
                definition.ValidateOrThrow();
                if (!_worldItemDefinitions.TryAdd(definition.Id, definition))
                    throw new InvalidOperationException(
                        $"WorldView 世界物品 id '{definition.Id}' 重复。 ");
            }
        }

        private void BuildGrayboxWorld()
        {
            _grayboxFactory = new FoundationFacilityGrayboxFactory();
            _cameraController = new FoundationOrbitCameraController(
                worldCamera,
                deckRoot,
                deckLayout.DeckCenterLocal,
                vehicleVisualPrefab != null ? new Vector3(9f, 15f, 11f) : new Vector3(11f, 13f, -12f));
            worldCamera.fieldOfView = 42f;
            worldCamera.clearFlags = skyboxMaterial != null
                ? CameraClearFlags.Skybox
                : CameraClearFlags.SolidColor;
            worldCamera.backgroundColor = new Color(0.055f, 0.075f, 0.085f);
            worldCamera.nearClipPlane = 0.1f;
            worldCamera.farClipPlane = 120f;

            // 正式 Foundation 场景使用程序化天空提供漫反射与远景高光，局部探针再补车辆周围环境。
            // 隔离测试不必装配图形资产，因此仍保留纯色 + Flat Ambient 的可预测回退。
            if (skyboxMaterial != null)
            {
                RenderSettings.ambientMode = vehicleVisualPrefab != null ? AmbientMode.Trilight : AmbientMode.Skybox;
                if (vehicleVisualPrefab != null)
                {
                    // 动态生成的居民没有烘焙探针；明确提供天空、地平线与沙地反光，保留帽檐下的面部。
                    RenderSettings.ambientSkyColor = new Color(.66f, .69f, .73f);
                    RenderSettings.ambientEquatorColor = new Color(.65f, .60f, .52f);
                    RenderSettings.ambientGroundColor = new Color(.50f, .46f, .38f);
                }
                RenderSettings.ambientIntensity = 1.02f;
                RenderSettings.skybox = skyboxMaterial;
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
                RenderSettings.reflectionIntensity = 0.95f;
            }
            else
            {
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.235f, 0.255f, 0.265f);
                RenderSettings.ambientIntensity = 1f;
                RenderSettings.skybox = null;
                RenderSettings.reflectionIntensity = 0.55f;
            }

            if (keyLight != null)
            {
                keyLight.type = LightType.Directional;
                keyLight.color = ClearWeatherKeyColor;
                keyLight.intensity = ClearWeatherKeyIntensity;
                keyLight.shadows = LightShadows.Soft;
                keyLight.shadowStrength = vehicleVisualPrefab != null ? .78f : 1f;
                keyLight.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
                RenderSettings.sun = keyLight;
            }

            if (fillLight != null)
            {
                fillLight.type = LightType.Directional;
                fillLight.color = vehicleVisualPrefab != null ? new Color(.79f, .84f, .85f) : new Color(0.52f, 0.68f, 1f);
                fillLight.intensity = 0.52f;
                fillLight.shadows = LightShadows.None;
                fillLight.transform.rotation = Quaternion.Euler(42f, 145f, 0f);
            }

            Material deckMaterial = CreateLitMaterial(
                "M_Deck",
                new Color(0.18f, 0.23f, 0.24f),
                0.42f,
                0.36f);
            Material gridMaterial = CreateLitMaterial(
                "M_Grid",
                new Color(0.24f, 0.34f, 0.35f));
            Material groundMaterial = CreateLitMaterial(
                "M_Ground",
                new Color(0.25f, 0.15f, 0.09f),
                0f,
                0.14f);
            _ghostValidMaterial = CreateTransparentMaterial(
                "M_GhostValid",
                new Color(0.18f, 0.95f, 0.55f, 0.48f));
            _ghostInvalidMaterial = CreateTransparentMaterial(
                "M_GhostInvalid",
                new Color(1f, 0.22f, 0.14f, 0.55f));
            _ghostPartialMaterial = CreateTransparentMaterial(
                "M_GhostPartial",
                new Color(1f, 0.52f, 0.12f, 0.55f));
            _ghostPendingMaterial = CreateTransparentMaterial(
                "M_GhostPending",
                new Color(1f, 0.72f, 0.14f, 0.55f));
            _slotReachableMaterial = CreateLitMaterial(
                "M_InteractionSlotReachable",
                new Color(0.15f, 1f, 0.48f));
            _slotBlockedMaterial = CreateLitMaterial(
                "M_InteractionSlotBlocked",
                new Color(1f, 0.16f, 0.12f));
            _slotPendingMaterial = CreateLitMaterial(
                "M_InteractionSlotPending",
                new Color(1f, 0.68f, 0.1f));
            _slotSharedMaterial = CreateLitMaterial(
                "M_InteractionSlotSharedSpace",
                new Color(1f, 0.78f, 0.08f));
            _slotOccupiedMaterial = CreateLitMaterial(
                "M_InteractionSlotOccupied",
                new Color(0.72f, 0.28f, 1f));
            _placementRegionMaterial = CreateTransparentMaterial(
                "M_PlacementRegion",
                new Color(0.12f, 0.78f, 1f, 0.34f));
            CreateSandstormVisual();

            Transform vehicleRoot = null;
            if (vehicleVisualPrefab != null)
            {
                GameObject vehicle = Instantiate(vehicleVisualPrefab, deckRoot, false);
                vehicle.name = "移动工坊 · 可替换车架表现";
                vehicle.transform.localPosition = deckLayout.DeckCenterLocal;
                vehicleRoot = vehicle.transform;
                FoundationRoofVisual[] roofs = vehicle.GetComponentsInChildren<FoundationRoofVisual>(true);
                if (roofs.Length > 0)
                {
                    _roofPresentation = new FoundationRoofPresentation(roofs);
                    Bag.Add(_roofPresentation);
                    if (screenUi != null) Bag.Add(screenUi.BindRoofDisplay(_roofPresentation));
                }
            }
            else
            {
                CreatePrimitive(
                    PrimitiveType.Cube,
                    "Deck",
                    deckRoot,
                    deckLayout.DeckCenterLocal + Vector3.down * 0.22f,
                    deckLayout.DeckSize,
                    deckMaterial);
            }

            Vector3 center = deckLayout.DeckCenterLocal;
            _stopVisualRoot = new GameObject("干河驿站 · 取水区").transform;
            _stopVisualRoot.SetParent(deckRoot, false);
            _stopWorldItemRoot = new GameObject("驿站物资").transform;
            _stopWorldItemRoot.SetParent(_stopVisualRoot, false);
            Vector3 waterPoint = center + new Vector3(deckLayout.DeckSize.x * 0.5f + 3f, 0f, 0f);
            if (waystationVisualPrefab != null)
            {
                GameObject station = Instantiate(waystationVisualPrefab, _stopVisualRoot, false);
                station.transform.localPosition = waterPoint;
            }
            else
            {
            CreatePrimitive(PrimitiveType.Cube, "停靠通路", _stopVisualRoot,
                waterPoint - Vector3.right + Vector3.down * 0.11f, new Vector3(6f, 0.2f, 4f),
                CreateLitMaterial("M_StopApron", new Color(0.49f, 0.4f, 0.24f)));
            CreatePrimitive(PrimitiveType.Cube, "有限水源", _stopVisualRoot,
                waterPoint + new Vector3(0f, 0.55f, 0.9f), new Vector3(1.2f, 1.1f, 0.7f),
                CreateLitMaterial("M_StopWater", new Color(0.12f, 0.57f, 0.72f)));
            CreatePrimitive(PrimitiveType.Cube, "污物接收罐", _stopVisualRoot,
                waterPoint + new Vector3(0f, 0.5f, -1.6f), new Vector3(1.1f, 1f, 0.6f),
                CreateLitMaterial("M_StopWaste", new Color(0.57f, 0.32f, 0.13f)));
            CreatePrimitive(PrimitiveType.Cube, "备件台", _stopVisualRoot,
                waterPoint + new Vector3(1.2f, 0.35f, 1.35f), new Vector3(1.1f, 0.7f, 0.55f),
                CreateLitMaterial("M_StopSupply", new Color(0.28f, 0.36f, 0.32f)));
            }
            _stopVisualRoot.gameObject.SetActive(false);
            _placementGrid = new FoundationPlacementGridVisual(
                deckRoot,
                deckLayout.CreateBounds(),
                gridMaterial);
            _gridRoot = _placementGrid.Root;
            _placementGrid.Rebuild(
                deckLayout.DefaultSnapSettings.PositionStepMillimeters);
            _placementGrid.SetVisible(false);

            if (environmentVisualPrefab != null)
            {
                GameObject environment = Instantiate(environmentVisualPrefab, deckRoot, false);
                environment.transform.localPosition = center;
                if (vehicleRoot != null)
                {
                    _journeyPresentation = new FoundationJourneyPresentation(vehicleRoot, environment.transform);
                    Bag.Add(_journeyPresentation);
                }
            }
            else CreatePrimitive(
                PrimitiveType.Cube,
                "Wasteland Ground",
                deckRoot,
                center + new Vector3(0f, -0.7f, 0f),
                new Vector3(80f, 0.5f, 80f),
                groundMaterial);

            _facilityRoot = new GameObject("Facilities").transform;
            _facilityRoot.SetParent(deckRoot, false);
            _worldItemRoot = new GameObject("World Items").transform;
            _worldItemRoot.SetParent(deckRoot, false);
            _ghostRoot = new GameObject("Placement Ghost").transform;
            _ghostRoot.SetParent(deckRoot, false);
            _interactionPreviewRoot = new GameObject("Placement Interaction Slots").transform;
            _interactionPreviewRoot.SetParent(deckRoot, false);
            BuildWaterCanVisual();
        }

        private ResidentVisual BuildResidentVisual(FoundationResidentReadModel resident)
        {
            string suffix = resident.StableId.Substring(resident.StableId.LastIndexOf('-') + 1);
            var root = new GameObject($"Resident {suffix}").transform;
            root.SetParent(deckRoot, false);
            var visual = new ResidentVisual { StableId = resident.StableId, Root = root };
            Color color = (resident.OwnerId % 3UL) switch
            { 0UL => new Color(0.35f, 0.68f, 0.85f), 1UL => new Color(0.78f, 0.56f, 0.28f),
                _ => new Color(0.48f, 0.78f, 0.45f) };
            Material body = CreateLitMaterial($"M_{resident.StableId}", color);
            visual.Material = body;
            visual.Body = CreatePrimitive(
                PrimitiveType.Capsule,
                "Body",
                root,
                new Vector3(0f, 0.55f, 0f),
                new Vector3(0.42f, 0.55f, 0.42f),
                body).transform;
            visual.CarryAnchor = new GameObject("Right Hand Carry Anchor").transform;
            visual.CarryAnchor.SetParent(root, false);
            // 灰盒居民还没有骨骼；先让表现消费稳定手部锚点，正式 Humanoid/IK 只替换锚点驱动。
            visual.CarryAnchor.localPosition = new Vector3(0.34f, 0.68f, 0.12f);
            visual.CarryAnchor.localRotation = Quaternion.Euler(0f, 0f, -8f);
            // 表现可随读档重建，选择依据始终是存续的居民身份；不得消耗玩法随机流。
            GameObject residentPrefab = _residentPrefabs.TryGetValue(resident.StableId, out GameObject boundPrefab)
                ? boundPrefab : residentVisualPrefab;
            if (residentPrefab != null && residentAnimationController != null)
            {
                ResidentHumanoidPresentation humanoid = root.gameObject.AddComponent<ResidentHumanoidPresentation>();
                if (!humanoid.TryInitialize(residentPrefab, residentAnimationController))
                    throw new InvalidOperationException("美术样板居民缺少有效 Humanoid 或五个语义动作状态。");
                visual.Humanoid = humanoid;
                visual.Body.gameObject.SetActive(false);
                visual.CarryAnchor.localPosition = _waterCanRig.GetGripPosition(humanoid);
                visual.StandingShoulderHeight = root.InverseTransformPoint(
                    humanoid.Animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position).y;
                visual.CarryAnchor.localRotation = Quaternion.identity;
                visual.CarryIK = humanoid.Animator.gameObject.AddComponent<FoundationResidentCarryIK>();
                visual.CarryIK.Configure(humanoid.Animator, root);
                ApplyWorkwearIdentity(humanoid.VisualRoot, resident.OwnerId);
            }
            _residentVisuals.Add(resident.StableId, visual);
            return visual;
        }

        private static void UpdateResidentBodyPose(ResidentVisual visual, FoundationResidentPhase phase)
        {
            visual.Phase = phase;
            if (visual.Humanoid != null) return;
            if (visual.Body == null) return;
            bool lying = phase is FoundationResidentPhase.RestingOnGround or
                FoundationResidentPhase.Dead;
            visual.Body.localPosition = lying
                ? new Vector3(0f, 0.42f, 0f)
                : new Vector3(0f, 0.55f, 0f);
            visual.Body.localRotation = lying
                ? Quaternion.Euler(0f, 0f, 90f)
                : Quaternion.identity;
        }

        /// <summary>
        /// 只根据已提交的身体位移与业务阶段更新表现；被碰撞挡住时不持续原地走路。
        /// 暂停/建造导航事务立即冻结，不由动画触发到岗或物品交接。
        /// </summary>
        private void UpdateResidentAnimations()
        {
            bool frozen = _animationPaused || _animationBuildPhase != FoundationBuildTransactionPhase.Idle;
            foreach (ResidentVisual visual in _residentVisuals.Values)
            {
                if (visual.Humanoid == null) continue;
                Vector3 position = visual.Root.localPosition;
                float distance = Vector3.Distance(position, visual.PreviousPosition);
                visual.PreviousPosition = position;
                // 读档和首次挂接的位置跳变不应变成一个极高速步态。
                float actualSpeed = distance < 2f && Time.deltaTime > 0f ? distance / Time.deltaTime : 0f;
                // 到岗快照意味着身体已经停在工作位，不能继续用走路速度的平滑尾巴覆盖工作动作。
                visual.AnimationMoveSpeed = frozen || visual.FacilityWork.Active ? 0f : Mathf.Lerp(
                    visual.AnimationMoveSpeed, actualSpeed, 1f - Mathf.Exp(-12f * Time.deltaTime));
                ResidentAnimationSemantic semantic = visual.AnimationMoveSpeed > 0.08f
                    ? ResidentAnimationSemantic.Move
                    : visual.Phase switch
                    {
                        FoundationResidentPhase.PickingUpWorldItem or
                        FoundationResidentPhase.PickingUpRepairPart or
                        FoundationResidentPhase.PickingUpStopSpare => ResidentAnimationSemantic.Pickup,
                        FoundationResidentPhase.RepairingFacility => ResidentAnimationSemantic.Work,
                        FoundationResidentPhase.PickingUpWater or FoundationResidentPhase.DeliveringWater or
                        FoundationResidentPhase.DeliveringStopWater => ResidentAnimationSemantic.Idle,
                        FoundationResidentPhase.RestingOnGround or
                        FoundationResidentPhase.UsingToilet => ResidentAnimationSemantic.Rest,
                        _ => ResidentAnimationSemantic.Idle,
                    };
                bool dead = visual.Phase == FoundationResidentPhase.Dead;
                visual.Humanoid.VisualRoot.localRotation = dead
                    ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.identity;
                visual.Humanoid.VisualRoot.localPosition = dead ? Vector3.up * 0.35f : Vector3.zero;
                // 冻结期间保留原来的状态与混合进度，恢复后再按真实阶段选择动作。
                if (!frozen && !dead) visual.Humanoid.SetSemantic(semantic);
                float playback = semantic == ResidentAnimationSemantic.Move
                    ? Mathf.Clamp(visual.AnimationMoveSpeed / 1.35f, 0.15f, 16f)
                    : _animationMultiplier;
                visual.Humanoid.SetPlaybackSpeed(frozen || dead ? 0f : playback);
                bool carrying = visual.HasCarriedItem ||
                    (_waterCanLocation == FoundationWaterCanLocation.Resident && _waterCanCarrierId == visual.StableId) ||
                    (!string.IsNullOrEmpty(_carriedWasteBucketFacilityId) && _wasteBucketCarrierId == visual.StableId);
                bool hasWaterCan = _waterCanLocation == FoundationWaterCanLocation.Resident &&
                    _waterCanCarrierId == visual.StableId;
                if (hasWaterCan) ApplyWaterCanWorkPose(visual);
                float groundReach = 0f, contactWeight = 0f;
                bool handling = !dead && ApplyWaterCanContactPose(visual, out groundReach, out contactWeight);
                visual.CarryIK.SetGroundReach(handling ? groundReach : 0f);
                if (handling) visual.CarryIK.SetContainerGrip(_waterCanRig, contactWeight);
                else if (hasWaterCan && !dead) visual.CarryIK.SetContainerGrip(_waterCanRig);
                else visual.CarryIK.SetRightGrip(carrying && !dead ? visual.CarryAnchor : null);
            }
        }

        private void ApplyWaterCanWorkPose(ResidentVisual visual)
        {
            FoundationFacilityWorkState work = visual.FacilityWork;
            FacilityVisual facility = null;
            bool supported = work.Active && _facilityVisuals.TryGetValue(work.FacilityInstanceId, out facility) &&
                facility.WorkRig != null;
            Transform inlet = null;
            bool pouring = supported && facility.WorkRig.TryGetWaterInlet(work, out inlet);
            float envelope = pouring ? FoundationFacilityArtRig.WorkEnvelope(work.Progress) : 0f;
            float lift = 0f;
            float forwardReach = 0f;
            if (pouring)
            {
                Vector3 mouthAtFullTilt = visual.CarryAnchor.localPosition +
                    Quaternion.AngleAxis(WaterCanPourDegrees, Vector3.right) *
                    (_waterCanRig.OpeningLocalPosition - _waterCanRig.CarryPivotLocalPosition);
                Vector3 localInlet = visual.Root.InverseTransformPoint(inlet.position);
                float inletHeight = localInlet.y;
                lift = Mathf.Max(visual.StandingShoulderHeight - .06f - visual.CarryAnchor.localPosition.y,
                    inletHeight + .12f - mouthAtFullTilt.y);
                forwardReach = Mathf.Clamp(localInlet.z - mouthAtFullTilt.z, 0f, .08f);
                // 高位入口需要把臂长留给抬升；肩部附近逐步收回前伸，不按设施类型分支。
                forwardReach *= 1f - Mathf.InverseLerp(visual.StandingShoulderHeight - .30f,
                    visual.StandingShoulderHeight - .10f, inletHeight);
            }
            // 保留侧向壳体净空，向操作口前伸少量；限幅保持弯肘余量。
            // 设施站位与入口必须处于可达范围，不能靠无限拉长手臂补偿远处入口。
            Vector3 grip = visual.CarryAnchor.localPosition + new Vector3(
                Mathf.Sign(visual.CarryAnchor.localPosition.x) * .02f, lift, forwardReach) * envelope;
            Quaternion rotation = Quaternion.AngleAxis(WaterCanPourDegrees * envelope, Vector3.right);
            _waterCanVisual.localRotation = rotation;
            _waterCanVisual.localPosition = grip - rotation * _waterCanRig.CarryPivotLocalPosition;
            _waterCanCap.gameObject.SetActive(!supported ||
                (work.Phase != FoundationResidentPhase.PickingUpWater && !pouring));
        }

        private bool ApplyWaterCanContactPose(ResidentVisual visual, out float groundReach, out float contactWeight)
        {
            groundReach = contactWeight = 0f;
            FoundationFacilityWorkState work = visual.FacilityWork;
            FoundationItemPlacementState placement = work.ItemContactPlacement;
            if (!work.Active || !placement.Active || work.Phase is not (
                    FoundationResidentPhase.PickingUpWaterCan or FoundationResidentPhase.LiftingWaterCan or
                    FoundationResidentPhase.PlacingWaterCan or FoundationResidentPhase.ReleasingWaterCan)) return false;
            Vector3 grounded = deckRoot.TransformPoint(deckLayout.PoseToLocal(placement.WorldPose,
                placement.SupportHeightMillimeters / 1000f));
            Quaternion groundedRotation = deckRoot.rotation * Quaternion.Euler(0f, (float)placement.WorldPose.YawDegrees, 0f);
            Vector3 carried = visual.Root.TransformPoint(visual.CarryAnchor.localPosition -
                _waterCanRig.CarryPivotLocalPosition);
            float movement = Mathf.SmoothStep(0f, 1f, work.Progress);
            float lift = 0f;
            switch (work.Phase)
            {
                case FoundationResidentPhase.PickingUpWaterCan:
                    groundReach = Mathf.SmoothStep(0f, 1f, work.Progress / .65f);
                    contactWeight = Mathf.SmoothStep(0f, 1f, (work.Progress - .5f) / .25f);
                    break;
                case FoundationResidentPhase.LiftingWaterCan:
                    groundReach = 1f - movement;
                    contactWeight = 1f;
                    lift = movement;
                    break;
                case FoundationResidentPhase.PlacingWaterCan:
                    groundReach = movement;
                    contactWeight = 1f;
                    lift = 1f - movement;
                    break;
                case FoundationResidentPhase.ReleasingWaterCan:
                    groundReach = 1f - movement;
                    contactWeight = 1f - Mathf.SmoothStep(0f, 1f, work.Progress / .3f);
                    break;
            }
            // 沿当前物品相对身体的外侧绕过膝盖；不同区域朝向不能共用固定的前推方向。
            Vector3 position = Vector3.Lerp(grounded, carried, lift);
            Vector3 outward = Vector3.ProjectOnPlane(position - visual.Root.position, visual.Root.up).normalized;
            Vector3 clearanceArc = (outward + visual.Root.up) *
                (_waterCanRig.LateralClearance * Mathf.Sin(Mathf.PI * lift));
            _waterCanVisual.SetPositionAndRotation(position + clearanceArc,
                Quaternion.Slerp(groundedRotation, visual.Root.rotation, lift));
            _waterCanCap.gameObject.SetActive(true);
            return true;
        }

        private void UpdateFacilityWorkVisuals()
        {
            foreach (var facility in _facilityVisuals.Values)
                if (facility.WorkRig != null) facility.WorkRig.ResetWorkPose();
            foreach (var resident in _residentVisuals.Values)
            {
                FoundationFacilityWorkState work = resident.FacilityWork;
                if (!work.Active || !_facilityVisuals.TryGetValue(work.FacilityInstanceId, out var facility) ||
                    facility.WorkRig == null) continue;
                Transform can = _waterCanLocation == FoundationWaterCanLocation.Resident &&
                    _waterCanCarrierId == resident.StableId ? _waterCanVisual : null;
                facility.WorkRig.Apply(work, can);
            }
        }

        private static void ApplyWorkwearIdentity(Transform root, ulong ownerId)
        {
            Color color = (ownerId % 3UL) switch
            {
                1UL => new Color(.13f, .28f, .25f),
                2UL => new Color(.48f, .27f, .10f),
                _ => new Color(.36f, .16f, .10f)
            };
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", color);
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                    if (materials[i] != null && materials[i].name == "NW1_Workshirt")
                        renderer.SetPropertyBlock(block, i);
            }
        }

        private void BuildWaterCanVisual()
        {
            if (waterCanVisualPrefab != null)
                _waterCanVisual = Instantiate(waterCanVisualPrefab, deckRoot).transform;
            else
            {
                Material shell = CreateLitMaterial("M_WaterCan", new Color(0.12f, 0.58f, 0.63f));
                Material hardware = CreateLitMaterial("M_WaterCanHardware", new Color(0.2f, 0.26f, 0.27f), .42f, .34f);
                Material water = CreateLitMaterial("M_WaterCanFilled", new Color(0.1f, 0.72f, 1f));
                _waterCanVisual = FoundationWaterCanVisualFactory.Create(deckRoot, shell, hardware, water, out _);
            }
            _waterCanVisual.name = "Water Can 01 [physical carrier]";
            _waterCanRig = _waterCanVisual.GetComponent<FoundationCarriedContainerRig>();
            if (_waterCanRig == null) throw new InvalidOperationException("水罐外观缺少完整容器绑定。");
            _waterCanRig.ValidateBindings();
            _waterCanFillVisual = _waterCanRig.FillIndicator;
            _waterCanCap = _waterCanRig.Closure;
            UpdateWaterCanVisual();
        }

        private void RebuildWorldItems(IReadOnlyList<FoundationItemPlacementState> items)
        {
            if (_worldItemRoot == null) return;
            DestroyChildren(_worldItemRoot);
            DestroyChildren(_stopWorldItemRoot);
            if (items == null) return;

            for (var i = 0; i < items.Count; i++)
            {
                FoundationItemPlacementState item = items[i];
                if (!item.Active || !_worldItemDefinitions.TryGetValue(
                        item.DefinitionId,
                        out NomadWorldItemDefinition definition))
                    continue;
                // 水罐已有随居民手部切换和液位显示的专用表现；区域列表只为它提供同一位置真值。
                if (definition.PrototypeStyle == NomadWorldItemPrototypeStyle.WaterCan) continue;

                var root = new GameObject(
                    $"{definition.DisplayName} [{item.ItemId}]").transform;
                root.SetParent(item.OwnerEntityId == NomadFoundationSystem.StopSupplyOwnerId
                    ? _stopWorldItemRoot : _worldItemRoot, false);
                root.localPosition = deckLayout.PoseToLocal(
                    item.WorldPose,
                    item.SupportHeightMillimeters / 1000f);
                root.localRotation = Quaternion.Euler(
                    0f,
                    (float)item.WorldPose.YawDegrees,
                    0f);
                BuildWorldItemPrototype(root, definition);
            }
        }

        private void RebuildCarriedWorldItem(ResidentVisual visual, FoundationCarriedWorldItemState carried)
        {
            visual.HasCarriedItem = carried.Active;
            if (visual.CarryAnchor == null) return;
            DestroyChildren(visual.CarryAnchor);
            if (!carried.Active || !_worldItemDefinitions.TryGetValue(
                    carried.DefinitionId,
                    out NomadWorldItemDefinition definition) ||
                definition.PrototypeStyle == NomadWorldItemPrototypeStyle.WaterCan)
                return;

            var root = new GameObject(
                $"{definition.DisplayName} [{carried.ItemId}] (carried)").transform;
            root.SetParent(visual.CarryAnchor, false);
            BuildWorldItemPrototype(root, definition);
        }

        private void BuildWorldItemPrototype(
            Transform root,
            NomadWorldItemDefinition definition)
        {
            Vector2 footprint = definition.FootprintSizeMeters;
            float height = definition.HeightMeters;
            Material material = GetWorldItemMaterial(definition);
            switch (definition.PrototypeStyle)
            {
                case NomadWorldItemPrototypeStyle.Box:
                    CreatePrimitive(
                        PrimitiveType.Cube,
                        "Prototype Body",
                        root,
                        new Vector3(0f, height * 0.5f, 0f),
                        new Vector3(footprint.x, height, footprint.y),
                        material);
                    break;
                case NomadWorldItemPrototypeStyle.Cylinder:
                    CreatePrimitive(
                        PrimitiveType.Cylinder,
                        "Prototype Body",
                        root,
                        new Vector3(0f, height * 0.5f, 0f),
                        new Vector3(footprint.x * 0.5f, height * 0.5f, footprint.y * 0.5f),
                        material);
                    break;
                case NomadWorldItemPrototypeStyle.Cup:
                    BuildCupPrototype(root, definition, material);
                    break;
            }
        }

        private void BuildCupPrototype(
            Transform root,
            NomadWorldItemDefinition definition,
            Material material)
        {
            Vector2 footprint = definition.FootprintSizeMeters;
            float height = definition.HeightMeters;
            // Unity 内置 Cylinder 的 X/Z 缩放量是最终直径，高度则是原始 2m 的一半。
            float bodyDiameterX = footprint.x * 0.8f;
            float bodyDiameterZ = footprint.y * 0.8f;
            CreatePrimitive(
                PrimitiveType.Cylinder,
                "Enamel Cup Body",
                root,
                new Vector3(0f, height * 0.5f, 0f),
                new Vector3(bodyDiameterX, height * 0.5f, bodyDiameterZ),
                material);
            CreatePrimitive(
                PrimitiveType.Cylinder,
                "Dark Inner Opening",
                root,
                new Vector3(0f, height + 0.001f, 0f),
                new Vector3(bodyDiameterX * 0.76f, 0.003f, bodyDiameterZ * 0.76f),
                GetWorldItemInnerMaterial(definition));

            float handleX = bodyDiameterX * 0.5f + footprint.x * 0.12f;
            float handleWidth = footprint.x * 0.24f;
            float handleThickness = Mathf.Max(0.007f, footprint.x * 0.08f);
            CreatePrimitive(
                PrimitiveType.Cube,
                "Handle Top",
                root,
                new Vector3(handleX, height * 0.72f, 0f),
                new Vector3(handleWidth, handleThickness, handleThickness),
                material);
            CreatePrimitive(
                PrimitiveType.Cube,
                "Handle Outer",
                root,
                new Vector3(handleX + handleWidth * 0.5f, height * 0.5f, 0f),
                new Vector3(handleThickness, height * 0.44f, handleThickness),
                material);
            CreatePrimitive(
                PrimitiveType.Cube,
                "Handle Bottom",
                root,
                new Vector3(handleX, height * 0.28f, 0f),
                new Vector3(handleWidth, handleThickness, handleThickness),
                material);
        }

        private Material GetWorldItemMaterial(NomadWorldItemDefinition definition)
        {
            if (_worldItemMaterials.TryGetValue(definition.Id, out Material material))
                return material;
            material = CreateLitMaterial(
                $"M_{definition.Id}",
                definition.PrototypeColor,
                definition.PrototypeStyle == NomadWorldItemPrototypeStyle.Cup ? 0.18f : 0.08f,
                definition.PrototypeStyle == NomadWorldItemPrototypeStyle.Cup ? 0.5f : 0.28f);
            _worldItemMaterials.Add(definition.Id, material);
            return material;
        }

        private Material GetWorldItemInnerMaterial(NomadWorldItemDefinition definition)
        {
            string materialKey = definition.Id + ":inner";
            if (_worldItemMaterials.TryGetValue(materialKey, out Material material))
                return material;
            material = CreateLitMaterial(
                $"M_{definition.Id}_Inner",
                new Color(0.08f, 0.09f, 0.085f),
                0.08f,
                0.2f);
            _worldItemMaterials.Add(materialKey, material);
            return material;
        }

        private void RebuildFacilities(IReadOnlyList<FoundationFacilityState> facilities)
        {
            if (_facilityRoot == null) return;
            _facilityStates = new FoundationFacilityState[facilities.Count];
            for (var i = 0; i < facilities.Count; i++) _facilityStates[i] = facilities[i];
            // 正在携带的桶已挂到居民，重建设施时也必须释放它，避免留下第二个容器表现。
            foreach (var bucket in _wasteBucketVisuals.Values)
                if (bucket.Root != null && !bucket.Root.IsChildOf(_facilityRoot)) Destroy(bucket.Root.gameObject);
            _wasteBucketVisuals.Clear();
            _facilityVisuals.Clear();
            DestroyChildren(_facilityRoot);
            for (var i = 0; i < facilities.Count; i++)
            {
                FoundationFacilityState state = facilities[i];
                if (!_definitions.TryGetValue(state.DefinitionId, out NomadFacilityDefinition definition))
                    continue;

                var root = new GameObject($"{definition.DisplayName} [{state.InstanceId}]").transform;
                root.SetParent(_facilityRoot, false);
                DeckPose pose = state.Pose;
                // 模型标记与物品支撑共用甲板原点；装饰抬高应在模型内部表达，不能偏移整个设施。
                root.localPosition = deckLayout.PoseToLocal(pose);
                root.localRotation = Quaternion.Euler(0f, (float)pose.YawDegrees, 0f);
                Renderer[] bodyRenderers = _grayboxFactory.Build(root, definition);
                if (definition.Function == NomadFacilityFunction.Toilet)
                {
                    Transform bucket = root.Find("Detachable Waste Bucket");
                    _wasteBucketVisuals.Add(state.InstanceId, new WasteBucketVisual(bucket, root));
                }
                var interactionRoot = new GameObject("Interaction Slots (build mode)")
                    .transform;
                interactionRoot.SetParent(root, false);
                var slotRenderers = new List<Renderer[]>();
                BuildInteractionSlotVisuals(interactionRoot, definition, slotRenderers);
                interactionRoot.gameObject.SetActive(false);
                var placementRegionRoot = new GameObject(
                    "Placement Regions (build mode)").transform;
                placementRegionRoot.SetParent(root, false);
                BuildPlacementRegionVisuals(
                    placementRegionRoot,
                    definition,
                    _placementRegionMaterial,
                    null);
                placementRegionRoot.gameObject.SetActive(false);
                var functionWarningRoot = new GameObject(
                    "Inaccessible Function Warnings (build mode)").transform;
                functionWarningRoot.SetParent(root, false);
                var groupVisuals = new List<InteractionGroupVisual>();
                BuildInteractionGroupWarningVisuals(
                    functionWarningRoot,
                    definition,
                    groupVisuals);
                _facilityVisuals.Add(
                    state.InstanceId,
                    new FacilityVisual(
                        state,
                        root,
                        interactionRoot,
                        placementRegionRoot,
                        bodyRenderers,
                        slotRenderers.ToArray(),
                        groupVisuals.ToArray(),
                        definition.Prefab != null));
            }
            ApplyFacilityAccessVisuals();
            UpdateWaterCanVisual();
            UpdateWasteBucketLocations();
        }

        private void UpdateWasteBucketLocations()
        {
            foreach (var entry in _wasteBucketVisuals)
            {
                WasteBucketVisual bucket = entry.Value;
                if (bucket.Root == null) continue;
                Transform carrier = FindResidentRoot(_wasteBucketCarrierId);
                bool carried = entry.Key == _carriedWasteBucketFacilityId && carrier != null;
                bucket.Root.SetParent(carried ? carrier : bucket.FacilityRoot, false);
                bucket.Root.localPosition = carried ? new Vector3(0.42f, 0.24f, 0f) : bucket.InstalledPosition;
                if (carried && _residentVisuals.TryGetValue(_wasteBucketCarrierId, out ResidentVisual visual) && visual.Humanoid != null)
                    bucket.Root.localPosition = visual.CarryAnchor.localPosition - Vector3.up * .44f;
                bucket.Root.localRotation = Quaternion.identity;
            }
        }

        private sealed class WasteBucketVisual
        {
            internal WasteBucketVisual(Transform root, Transform facilityRoot)
            { Root = root; FacilityRoot = facilityRoot; InstalledPosition = root.localPosition; }
            internal Transform Root { get; }
            internal Transform FacilityRoot { get; }
            internal Vector3 InstalledPosition { get; }
        }

        private void UpdateFacilityAccess(
            IReadOnlyList<FoundationFacilityAccessState> states)
        {
            _facilityAccess.Clear();
            if (states != null)
            {
                for (var i = 0; i < states.Count; i++)
                    _facilityAccess[states[i].InstanceId] = states[i];
            }
            ApplyFacilityAccessVisuals();
        }

        private void UpdateFacilityConditions(
            IReadOnlyList<FoundationFacilityConditionState> states)
        {
            _facilityConditions.Clear();
            if (states != null)
            {
                for (var i = 0; i < states.Count; i++)
                    _facilityConditions[states[i].InstanceId] = states[i];
            }
            ApplyFacilityAccessVisuals();
        }

        /// <summary>
        /// 创建不参与玩法碰撞的程序化沙尘层。天气真值仍来自 System；这里仅用粒子、雾和灯光
        /// 表现同一个强度投影，后续替换 VFX Graph 不会改变设施老化或存档语义。
        /// </summary>
        private void CreateSandstormVisual()
        {
            var dust = new GameObject("Environment · Sandstorm Dust");
            // ParticleSystem 加到激活对象时会先按默认 playOnAwake 启动；先禁用再配置，
            // 避免运行中修改 duration 触发 Unity Assert，也避免清朗开局闪过一帧沙尘。
            dust.SetActive(false);
            dust.transform.SetParent(deckRoot, false);
            dust.transform.localPosition = deckLayout.DeckCenterLocal + new Vector3(0f, 2.2f, 0f);
            _sandstormParticles = dust.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = _sandstormParticles.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 2.4f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.3f, 2.4f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.075f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.78f, 0.47f, 0.2f, 0.14f),
                new Color(0.93f, 0.7f, 0.35f, 0.34f));
            main.maxParticles = 700;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            ParticleSystem.EmissionModule emission = _sandstormParticles.emission;
            emission.rateOverTime = 0f;
            ParticleSystem.ShapeModule shape = _sandstormParticles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(
                deckLayout.DeckSize.x + 8f,
                4.5f,
                deckLayout.DeckSize.z + 8f);

            ParticleSystem.VelocityOverLifetimeModule velocity =
                _sandstormParticles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            // Unity 要求三轴速度使用相同曲线模式；固定轴也用上下界相同的 TwoConstants。
            velocity.x = new ParticleSystem.MinMaxCurve(-7.5f, -7.5f);
            velocity.y = new ParticleSystem.MinMaxCurve(-0.35f, 0.1f);
            velocity.z = new ParticleSystem.MinMaxCurve(-2.4f, -2.4f);

            ParticleSystemRenderer renderer = dust.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 5.5f;
            renderer.velocityScale = 0.16f;
            renderer.sharedMaterial = CreateTransparentMaterial(
                "M_SandstormDust",
                new Color(0.86f, 0.55f, 0.24f, 0.48f));
            dust.SetActive(false);
        }

        private void UpdateEnvironmentVisual()
        {
            if (_sandstormParticles == null) return;

            bool sandstorm = _currentWeather == NomadWeatherKind.Sandstorm &&
                             _sandstormIntensityPermille > 0;
            float intensity = Mathf.Clamp01(_sandstormIntensityPermille / 1000f);
            if (_sandstormParticles.gameObject.activeSelf != sandstorm)
                _sandstormParticles.gameObject.SetActive(sandstorm);

            if (sandstorm)
            {
                ParticleSystem.EmissionModule emission = _sandstormParticles.emission;
                emission.rateOverTime = Mathf.Lerp(80f, 230f, intensity);
                if (!_sandstormParticles.isPlaying) _sandstormParticles.Play();

                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogColor = Color.Lerp(
                    new Color(0.38f, 0.3f, 0.23f),
                    new Color(0.52f, 0.35f, 0.22f),
                    intensity);
                // 俯视相机离甲板较远；雾距必须按可玩空间校准，风暴可以压低对比度，
                // 但不能把设施、居民和建造反馈全部染成纯色。
                RenderSettings.fogStartDistance = Mathf.Lerp(24f, 14f, intensity);
                RenderSettings.fogEndDistance = Mathf.Lerp(70f, 45f, intensity);
                RenderSettings.ambientIntensity = Mathf.Lerp(0.94f, 0.76f, intensity);
                if (keyLight != null)
                {
                    keyLight.color = Color.Lerp(
                        ClearWeatherKeyColor,
                        new Color(0.95f, 0.55f, 0.24f),
                        intensity);
                    keyLight.intensity = Mathf.Lerp(ClearWeatherKeyIntensity, 1.12f, intensity);
                }
                if (fillLight != null) fillLight.intensity = Mathf.Lerp(0.52f, 0.32f, intensity);
                return;
            }

            if (_sandstormParticles.isPlaying)
                _sandstormParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            RenderSettings.fog = false;
            RenderSettings.ambientIntensity = skyboxMaterial != null ? 1.02f : 1f;
            if (keyLight != null)
            {
                keyLight.color = ClearWeatherKeyColor;
                keyLight.intensity = ClearWeatherKeyIntensity;
            }
            if (fillLight != null) fillLight.intensity = 0.52f;
        }

        private void ApplyFacilityAccessVisuals()
        {
            bool inBuildMode = _interactionMode == FoundationInteractionMode.Build;
            foreach (KeyValuePair<string, FacilityVisual> item in _facilityVisuals)
            {
                FacilityVisual visual = item.Value;
                bool hasAccess = _facilityAccess.TryGetValue(
                    item.Key,
                    out FoundationFacilityAccessState access);
                FoundationFacilityAccess displayAccess = hasAccess
                    ? access.DisplayAccess
                    : FoundationFacilityAccess.Unknown;
                bool showAccessTint = inBuildMode &&
                                      displayAccess is FoundationFacilityAccess.Unreachable or
                                          FoundationFacilityAccess.PartiallyReachable;
                bool hasCondition = _facilityConditions.TryGetValue(
                    item.Key,
                    out FoundationFacilityConditionState condition);
                bool hasLocalIndicator = visual.WorkRig != null && visual.WorkRig.ConditionIndicator != null;
                if (visual.WorkRig != null)
                    visual.WorkRig.ApplyCondition(hasCondition && !condition.IsOperational,
                        hasCondition && condition.Warning == FacilityConditionWarning.Critical);
                bool showFaultTint = !hasLocalIndicator && hasCondition && !condition.IsOperational;
                bool showDustTint = hasCondition && condition.DustPermille > 0;
                bool showCriticalTint = !hasLocalIndicator && hasCondition &&
                                        condition.Warning == FacilityConditionWarning.Critical;
                bool showStatusTint = showAccessTint || showFaultTint ||
                                      showDustTint || showCriticalTint;
                Color tint;
                float tintStrength;
                if (showAccessTint)
                {
                    tint = displayAccess == FoundationFacilityAccess.Unreachable
                        ? new Color(1f, 0.08f, 0.05f, 1f)
                        : new Color(1f, 0.42f, 0.08f, 1f);
                    tintStrength = 0.82f;
                }
                else if (showFaultTint)
                {
                    tint = new Color(0.86f, 0.12f, 0.055f, 1f);
                    tintStrength = visual.HasAuthoredArt ? .12f : .68f;
                }
                else if (showCriticalTint)
                {
                    tint = new Color(0.88f, 0.38f, 0.08f, 1f);
                    tintStrength = visual.HasAuthoredArt ? .08f : .3f;
                }
                else
                {
                    tint = new Color(0.55f, 0.34f, 0.17f, 1f);
                    tintStrength = Mathf.Lerp(
                        visual.HasAuthoredArt ? .025f : .06f,
                        visual.HasAuthoredArt ? .16f : .38f,
                        condition.DustPermille / 1000f);
                }
                for (var rendererIndex = 0;
                     rendererIndex < visual.BodyRenderers.Length;
                     rendererIndex++)
                {
                    Renderer renderer = visual.BodyRenderers[rendererIndex];
                    if (renderer == null) continue;
                    // 水流与警示灯有独立材质投影；外壳积尘不能覆盖它们的颜色。
                    if (renderer is LineRenderer || hasLocalIndicator && renderer == visual.WorkRig.ConditionIndicator)
                        continue;
                    if (!showStatusTint)
                    {
                        renderer.SetPropertyBlock(null);
                        continue;
                    }

                    _facilityStatusProperties.Clear();
                    Material material = renderer.sharedMaterial;
                    if (material != null)
                    {
                        Color baseColor = material.HasProperty("_BaseColor")
                            ? material.GetColor("_BaseColor")
                            : material.HasProperty("_Color")
                                ? material.GetColor("_Color")
                                : Color.white;
                        Color blended = Color.Lerp(baseColor, tint, tintStrength);
                        if (material.HasProperty("_BaseColor"))
                            _facilityStatusProperties.SetColor("_BaseColor", blended);
                        if (material.HasProperty("_Color"))
                            _facilityStatusProperties.SetColor("_Color", blended);
                        if (hasCondition && condition.DustPermille > 0 &&
                            material.HasProperty("_Smoothness"))
                        {
                            float dustRoughness = Mathf.Lerp(
                                1f,
                                0.28f,
                                condition.DustPermille / 1000f);
                            _facilityStatusProperties.SetFloat(
                                "_Smoothness",
                                material.GetFloat("_Smoothness") * dustRoughness);
                        }
                    }
                    renderer.SetPropertyBlock(_facilityStatusProperties);
                }

                for (var slotIndex = 0;
                     slotIndex < visual.SlotRenderers.Length;
                     slotIndex++)
                {
                    Material slotMaterial;
                    if (!hasAccess || !access.IsDisplaySlotReachable(slotIndex))
                        slotMaterial = _slotBlockedMaterial;
                    else if (access.IsDisplaySlotOccupied(slotIndex))
                        slotMaterial = _slotOccupiedMaterial;
                    else if (access.IsDisplaySlotSharingSpace(slotIndex))
                        slotMaterial = _slotSharedMaterial;
                    else
                        slotMaterial = _slotReachableMaterial;
                    Renderer[] renderers = visual.SlotRenderers[slotIndex];
                    for (var rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                    {
                        if (renderers[rendererIndex] != null)
                            renderers[rendererIndex].sharedMaterial = slotMaterial;
                    }
                }

                for (var groupIndex = 0;
                     groupIndex < visual.InteractionGroups.Length;
                     groupIndex++)
                {
                    InteractionGroupVisual groupVisual =
                        visual.InteractionGroups[groupIndex];
                    bool groupReachable = hasAccess &&
                                          IsInteractionGroupReachable(
                                              access,
                                              groupVisual.FirstSlotIndex,
                                              groupVisual.SlotCount);
                    groupVisual.WarningRoot.gameObject.SetActive(
                        inBuildMode &&
                        hasAccess &&
                        displayAccess != FoundationFacilityAccess.Unknown &&
                        !groupReachable);
                }

                visual.InteractionRoot.gameObject.SetActive(inBuildMode);
                visual.PlacementRegionRoot.gameObject.SetActive(inBuildMode);
            }
        }

        private void OnInteractionModeChanged(FoundationInteractionMode mode)
        {
            _interactionMode = mode;
            _roofPresentation?.SetBuildCutaway(mode == FoundationInteractionMode.Build);
            ApplyFacilityAccessVisuals();
            UpdatePlacementGridVisibility();
        }

        private void UpdatePlacementGridVisibility()
        {
            _placementGrid?.SetVisible(
                _interactionMode == FoundationInteractionMode.Build &&
                _showPlacementGrid &&
                _positionSnapMillimeters > 0);
        }

        private void UpdateWaterCanVisual()
        {
            if (_waterCanVisual == null || deckRoot == null) return;
            _waterCanCap.gameObject.SetActive(true);
            if (_waterCanLocation == FoundationWaterCanLocation.Resident)
            {
                Transform carrier = FindResidentRoot(_waterCanCarrierId);
                _waterCanVisual.gameObject.SetActive(carrier != null);
                if (carrier == null) return;
                _waterCanVisual.SetParent(carrier, false);
                _waterCanVisual.localPosition = new Vector3(0.42f, 0.32f, 0f);
                if (_residentVisuals.TryGetValue(_waterCanCarrierId, out ResidentVisual visual) &&
                    visual.Humanoid != null)
                    _waterCanVisual.localPosition = visual.CarryAnchor.localPosition - _waterCanRig.CarryPivotLocalPosition;
                _waterCanVisual.localRotation = Quaternion.identity;
                return;
            }

            if (!_waterCanPlacement.Active ||
                !string.Equals(
                    _waterCanPlacement.OwnerEntityId,
                    _waterCanAnchorFacilityInstanceId,
                    StringComparison.Ordinal))
            {
                _waterCanVisual.gameObject.SetActive(false);
                return;
            }

            DeckPose pose = _waterCanPlacement.WorldPose;
            _waterCanVisual.SetParent(deckRoot, false);
            _waterCanVisual.localPosition = deckLayout.PoseToLocal(
                pose,
                _waterCanPlacement.SupportHeightMillimeters / 1000f);
            _waterCanVisual.localRotation = Quaternion.Euler(0f, (float)pose.YawDegrees, 0f);
            _waterCanVisual.gameObject.SetActive(true);
        }

        private void OnPreviewChanged(FoundationPlacementPreviewState preview)
        {
            bool resetPointer = preview.Active != _preview.Active ||
                                !string.Equals(
                                    preview.DefinitionId,
                                    _preview.DefinitionId,
                                    StringComparison.Ordinal);
            _preview = preview;
            if (resetPointer) _hasPointerPose = false;
            if (_ghostRoot == null || _interactionPreviewRoot == null) return;
            if (!preview.Active || !_definitions.TryGetValue(
                    preview.DefinitionId,
                    out NomadFacilityDefinition definition))
            {
                _ghostRoot.gameObject.SetActive(false);
                _interactionPreviewRoot.gameObject.SetActive(false);
                ApplyFacilityAccessVisuals();
                return;
            }

            EnsurePreviewVisuals(definition);
            _ghostRoot.gameObject.SetActive(true);
            _interactionPreviewRoot.gameObject.SetActive(true);
            FoundationFacilityAccess candidateAccess = ClassifyPreviewAccess(
                definition,
                preview);
            Material ghostMaterial;
            if (preview.Failure == FoundationPlacementFailure.TransactionInProgress)
                ghostMaterial = _ghostPendingMaterial;
            else if (!preview.CanConfirm)
                ghostMaterial = _ghostInvalidMaterial;
            else
            {
                ghostMaterial = candidateAccess switch
                {
                    FoundationFacilityAccess.Unreachable => _ghostInvalidMaterial,
                    FoundationFacilityAccess.PartiallyReachable => _ghostPartialMaterial,
                    _ => _ghostValidMaterial,
                };
            }
            DeckPose pose = preview.Pose;
            _ghostRoot.localPosition = deckLayout.PoseToLocal(pose);
            _ghostRoot.localRotation = Quaternion.Euler(0f, (float)pose.YawDegrees, 0f);
            _interactionPreviewRoot.localPosition = deckLayout.PoseToLocal(pose);
            _interactionPreviewRoot.localRotation = Quaternion.Euler(
                0f,
                (float)pose.YawDegrees,
                0f);
            for (var i = 0; i < _ghostRenderers.Count; i++)
                _ghostRenderers[i].sharedMaterial = ghostMaterial;

            bool pending = preview.Failure == FoundationPlacementFailure.TransactionInProgress;
            for (var i = 0; i < _interactionSlotRenderers.Count; i++)
            {
                Material slotMaterial;
                if (pending)
                    slotMaterial = _slotPendingMaterial;
                else if (!preview.RealtimeReachabilityEvaluated ||
                         !preview.IsInteractionSlotReachable(i))
                    slotMaterial = _slotBlockedMaterial;
                else if (preview.IsInteractionSlotSharingSpace(i))
                    slotMaterial = _slotSharedMaterial;
                else
                    slotMaterial = _slotReachableMaterial;
                Renderer[] renderers = _interactionSlotRenderers[i];
                for (var rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                    renderers[rendererIndex].sharedMaterial = slotMaterial;
            }
            ApplyFacilityAccessVisuals();
        }

        private static FoundationFacilityAccess ClassifyPreviewAccess(
            NomadFacilityDefinition definition,
            in FoundationPlacementPreviewState preview)
        {
            if (!preview.RealtimeReachabilityEvaluated)
                return FoundationFacilityAccess.Unknown;

            IReadOnlyList<NomadFacilityInteractionGroupDefinition> groups =
                definition.InteractionGroups;
            var flattenedSlotIndex = 0;
            var reachableGroupCount = 0;
            for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                bool groupReachable = false;
                IReadOnlyList<NomadFacilityInteractionSlotDefinition> slots =
                    groups[groupIndex].AlternativeSlots;
                for (var slotIndex = 0; slotIndex < slots.Count; slotIndex++)
                {
                    groupReachable |= preview.IsInteractionSlotReachable(flattenedSlotIndex);
                    flattenedSlotIndex++;
                }
                if (groupReachable) reachableGroupCount++;
            }

            if (groups.Count == 0 || reachableGroupCount == groups.Count)
                return FoundationFacilityAccess.Reachable;
            return reachableGroupCount == 0
                ? FoundationFacilityAccess.Unreachable
                : FoundationFacilityAccess.PartiallyReachable;
        }

        private void EnsurePreviewVisuals(NomadFacilityDefinition definition)
        {
            if (string.Equals(
                    _previewVisualDefinitionId,
                    definition.Id,
                    StringComparison.Ordinal))
                return;

            DestroyChildren(_ghostRoot);
            DestroyChildren(_interactionPreviewRoot);
            _ghostRenderers.Clear();
            _interactionSlotRenderers.Clear();
            _previewVisualDefinitionId = definition.Id;

            var functionalPreview = new GameObject("Functional Graybox Preview").transform;
            functionalPreview.SetParent(_ghostRoot, false);
            Renderer[] functionalRenderers = _grayboxFactory.Build(
                functionalPreview,
                definition);
            for (var i = 0; i < functionalRenderers.Length; i++)
                _ghostRenderers.Add(functionalRenderers[i]);

            ContinuousFacilityFootprint footprint = definition.CreateFootprint();
            for (var i = 0; i < footprint.Parts.Count; i++)
            {
                DeckFootprintPart part = footprint.Parts[i];
                GameObject visual = CreatePrimitive(
                    PrimitiveType.Cube,
                    $"Footprint Part {i + 1:D2}",
                    _ghostRoot,
                    new Vector3(
                        part.LocalCenterXMillimeters / 1000f,
                        0.09f,
                        part.LocalCenterZMillimeters / 1000f),
                    new Vector3(
                        part.WidthMillimeters / 1000f,
                        0.12f,
                        part.DepthMillimeters / 1000f),
                    _ghostInvalidMaterial);
                visual.transform.localRotation = Quaternion.Euler(
                        0f,
                        part.LocalYawDeciDegrees / 10f,
                        0f);
                if (visual.TryGetComponent(out Renderer renderer))
                    _ghostRenderers.Add(renderer);
            }

            BuildPlacementRegionVisuals(
                _ghostRoot,
                definition,
                _ghostInvalidMaterial,
                _ghostRenderers);

            BuildInteractionSlotVisuals(
                _interactionPreviewRoot,
                definition,
                _interactionSlotRenderers);
        }

        private void BuildInteractionSlotVisuals(
            Transform parent,
            NomadFacilityDefinition definition,
            ICollection<Renderer[]> destination)
        {
            var flattenedSlotIndex = 0;
            IReadOnlyList<NomadFacilityInteractionGroupDefinition> groups =
                definition.InteractionGroups;
            for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                NomadFacilityInteractionGroupDefinition group = groups[groupIndex];
                for (var slotIndex = 0; slotIndex < group.AlternativeSlots.Count; slotIndex++)
                {
                    NomadFacilityInteractionSlotDefinition slot =
                        group.AlternativeSlots[slotIndex];
                    var slotRoot = new GameObject(
                        $"Slot {flattenedSlotIndex + 1:D2} · {group.GroupId}/{slot.SlotId}").transform;
                    slotRoot.SetParent(parent, false);
                    slotRoot.localPosition = new Vector3(
                        slot.LocalPositionMeters.x,
                        0.06f,
                        slot.LocalPositionMeters.y);
                    slotRoot.localRotation = Quaternion.Euler(
                        0f,
                        slot.LocalYawDegrees,
                        0f);

                    GameObject disc = CreatePrimitive(
                        PrimitiveType.Cylinder,
                        "Reachability Disc",
                        slotRoot,
                        Vector3.zero,
                        new Vector3(0.24f, 0.025f, 0.24f),
                        _slotBlockedMaterial);
                    GameObject direction = CreatePrimitive(
                        PrimitiveType.Cube,
                        "Facing Marker",
                        slotRoot,
                        new Vector3(0f, 0.035f, 0.17f),
                        new Vector3(0.075f, 0.055f, 0.26f),
                        _slotBlockedMaterial);
                    destination.Add(new[]
                    {
                        disc.GetComponent<Renderer>(),
                        direction.GetComponent<Renderer>(),
                    });
                    flattenedSlotIndex++;
                }
            }
        }

        private void BuildPlacementRegionVisuals(
            Transform parent,
            NomadFacilityDefinition definition,
            Material material,
            ICollection<Renderer> destination)
        {
            IReadOnlyList<NomadPlacementRegionDefinition> regions =
                definition.PlacementRegions;
            for (var i = 0; i < regions.Count; i++)
            {
                NomadPlacementRegionDefinition region = regions[i];
                GameObject visual = CreatePrimitive(
                    PrimitiveType.Cube,
                    $"Region {i + 1:D2} · {region.RegionId}",
                    parent,
                    new Vector3(
                        region.LocalCenterMeters.x,
                        region.SupportHeightMeters + 0.018f,
                        region.LocalCenterMeters.y),
                    new Vector3(region.SizeMeters.x, 0.025f, region.SizeMeters.y),
                    material);
                visual.transform.localRotation = Quaternion.Euler(
                    0f,
                    region.LocalYawDegrees,
                    0f);
                if (destination != null && visual.TryGetComponent(out Renderer renderer))
                    destination.Add(renderer);
            }
        }

        private void BuildInteractionGroupWarningVisuals(
            Transform parent,
            NomadFacilityDefinition definition,
            ICollection<InteractionGroupVisual> destination)
        {
            var firstSlotIndex = 0;
            IReadOnlyList<NomadFacilityInteractionGroupDefinition> groups =
                definition.InteractionGroups;
            for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                NomadFacilityInteractionGroupDefinition group = groups[groupIndex];
                Vector2 center = Vector2.zero;
                int slotCount = group.AlternativeSlots.Count;
                for (var slotIndex = 0; slotIndex < slotCount; slotIndex++)
                    center += group.AlternativeSlots[slotIndex].LocalPositionMeters;
                if (slotCount > 0) center /= slotCount;

                var warningRoot = new GameObject(
                    $"Unavailable Function · {group.GroupId}").transform;
                warningRoot.SetParent(parent, false);
                warningRoot.localPosition = new Vector3(center.x, 0.08f, center.y);
                GameObject diamond = CreatePrimitive(
                    PrimitiveType.Cube,
                    "Warning Diamond",
                    warningRoot,
                    new Vector3(0f, 0.06f, 0f),
                    new Vector3(0.2f, 0.08f, 0.2f),
                    _slotBlockedMaterial);
                diamond.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
                CreatePrimitive(
                    PrimitiveType.Cube,
                    "Warning Stem",
                    warningRoot,
                    new Vector3(0f, 0.19f, 0f),
                    new Vector3(0.065f, 0.18f, 0.065f),
                    _slotBlockedMaterial);
                warningRoot.gameObject.SetActive(false);
                destination.Add(new InteractionGroupVisual(
                    warningRoot,
                    firstSlotIndex,
                    slotCount));
                firstSlotIndex += slotCount;
            }
        }

        private static bool IsInteractionGroupReachable(
            in FoundationFacilityAccessState access,
            int firstSlotIndex,
            int slotCount)
        {
            for (var slotOffset = 0; slotOffset < slotCount; slotOffset++)
            {
                if (access.IsDisplaySlotReachable(firstSlotIndex + slotOffset))
                    return true;
            }
            return false;
        }

        private bool TryGetPointerPose(Vector2 screenPoint, out DeckPose pose)
        {
            Ray ray = worldCamera.ScreenPointToRay(screenPoint);
            var plane = new Plane(deckRoot.up, deckRoot.position);
            if (!plane.Raycast(ray, out float distance))
            {
                pose = default;
                return false;
            }

            Vector3 localPoint = deckRoot.InverseTransformPoint(ray.GetPoint(distance));
            pose = deckLayout.LocalToPose(localPoint);
            return true;
        }

        private static bool WasBuildShortcutPressed(Keyboard keyboard, int zeroBasedIndex) =>
            zeroBasedIndex switch
            {
                0 => keyboard.digit1Key.wasPressedThisFrame,
                1 => keyboard.digit2Key.wasPressedThisFrame,
                2 => keyboard.digit3Key.wasPressedThisFrame,
                3 => keyboard.digit4Key.wasPressedThisFrame,
                4 => keyboard.digit5Key.wasPressedThisFrame,
                5 => keyboard.digit6Key.wasPressedThisFrame,
                6 => keyboard.digit7Key.wasPressedThisFrame,
                7 => keyboard.digit8Key.wasPressedThisFrame,
                8 => keyboard.digit9Key.wasPressedThisFrame,
                _ => false,
            };

        private GameObject CreatePrimitive(
            PrimitiveType type,
            string objectName,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Material material)
        {
            GameObject instance = GameObject.CreatePrimitive(type);
            instance.name = objectName;
            instance.transform.SetParent(parent, false);
            instance.transform.localPosition = localPosition;
            instance.transform.localScale = localScale;
            if (instance.TryGetComponent(out Renderer renderer)) renderer.sharedMaterial = material;
            if (instance.TryGetComponent(out Collider collider)) Destroy(collider);
            return instance;
        }

        private Material CreateLitMaterial(
            string materialName,
            Color color,
            float metallic = 0f,
            float smoothness = 0.22f)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = materialName, color = color };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            _runtimeMaterials.Add(material);
            return material;
        }

        private Material CreateTransparentMaterial(string materialName, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = materialName, color = color };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = 3000;
            _runtimeMaterials.Add(material);
            return material;
        }

        private static void DestroyChildren(Transform parent)
        {
            for (var i = parent.childCount - 1; i >= 0; i--)
                Destroy(parent.GetChild(i).gameObject);
        }

        private readonly struct InteractionGroupVisual
        {
            public InteractionGroupVisual(
                Transform warningRoot,
                int firstSlotIndex,
                int slotCount)
            {
                WarningRoot = warningRoot;
                FirstSlotIndex = firstSlotIndex;
                SlotCount = slotCount;
            }

            public Transform WarningRoot { get; }
            public int FirstSlotIndex { get; }
            public int SlotCount { get; }
        }

        private sealed class FacilityVisual
        {
            public FacilityVisual(
                FoundationFacilityState state,
                Transform root,
                Transform interactionRoot,
                Transform placementRegionRoot,
                Renderer[] bodyRenderers,
                Renderer[][] slotRenderers,
                InteractionGroupVisual[] interactionGroups,
                bool hasAuthoredArt)
            {
                State = state;
                Root = root;
                InteractionRoot = interactionRoot;
                PlacementRegionRoot = placementRegionRoot;
                BodyRenderers = bodyRenderers ?? Array.Empty<Renderer>();
                WorkRig = root.GetComponentInChildren<FoundationFacilityArtRig>();
                HasAuthoredArt = hasAuthoredArt;
                SlotRenderers = slotRenderers ?? Array.Empty<Renderer[]>();
                InteractionGroups = interactionGroups ??
                                    Array.Empty<InteractionGroupVisual>();
            }

            public FoundationFacilityState State { get; }
            public Transform Root { get; }
            public Transform InteractionRoot { get; }
            public Transform PlacementRegionRoot { get; }
            public Renderer[] BodyRenderers { get; }
            public FoundationFacilityArtRig WorkRig { get; }
            public bool HasAuthoredArt { get; }
            public Renderer[][] SlotRenderers { get; }
            public InteractionGroupVisual[] InteractionGroups { get; }
        }

        private void OnDrawGizmosSelected()
        {
            if (deckLayout == null) return;
            int stepMillimeters = Application.isPlaying
                ? _positionSnapMillimeters
                : deckLayout.DefaultSnapSettings.PositionStepMillimeters;
            if (stepMillimeters <= 0) return;
            Transform root = deckRoot != null ? deckRoot : transform;
            Gizmos.matrix = root.localToWorldMatrix;
            Gizmos.color = new Color(0.2f, 0.9f, 0.8f, 0.6f);
            DeckBounds bounds = deckLayout.CreateBounds();
            int firstX = Mathf.CeilToInt(
                bounds.MinXMillimeters / (float)stepMillimeters) * stepMillimeters;
            for (int x = firstX; x <= bounds.MaxXMillimeters; x += stepMillimeters)
            {
                float localX = x / 1000f;
                float z0 = bounds.MinZMillimeters / 1000f;
                float z1 = bounds.MaxZMillimeters / 1000f;
                Gizmos.DrawLine(new Vector3(localX, 0.03f, z0), new Vector3(localX, 0.03f, z1));
            }
            int firstZ = Mathf.CeilToInt(
                bounds.MinZMillimeters / (float)stepMillimeters) * stepMillimeters;
            for (int z = firstZ; z <= bounds.MaxZMillimeters; z += stepMillimeters)
            {
                float localZ = z / 1000f;
                float x0 = bounds.MinXMillimeters / 1000f;
                float x1 = bounds.MaxXMillimeters / 1000f;
                Gizmos.DrawLine(new Vector3(x0, 0.03f, localZ), new Vector3(x1, 0.03f, localZ));
            }
        }

        protected override void OnDestroy()
        {
            if (_stopVisualRoot != null) Destroy(_stopVisualRoot.gameObject);
            _placementGrid?.Dispose();
            _placementGrid = null;
            _grayboxFactory?.Dispose();
            _grayboxFactory = null;
            for (var i = 0; i < _runtimeMaterials.Count; i++)
            {
                if (_runtimeMaterials[i] != null) Destroy(_runtimeMaterials[i]);
            }
            _runtimeMaterials.Clear();
            base.OnDestroy();
        }
    }
}
