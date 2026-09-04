using System;
using System.Collections.Generic;
using Game.Framework.Common;
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
        [SerializeField, Tooltip("车辆甲板的表现空间；设施、居民、网格和镜头焦点都使用它的局部坐标。")]
        private Transform deckRoot;
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

        [Header("图形基线")]
        [SerializeField, Tooltip("进入场景后是否为实时 Reflection Probe 捕获一次车辆周围环境。只捕获一次，不会每帧更新；低端平台可关闭。")]
        private bool captureReflectionProbeOnStart = true;

        [Header("输入保护")]
        [SerializeField, Min(0f), Tooltip("左上开发面板占用的屏幕宽度；该区域不向 3D 世界透传点击和滚轮。")]
        private float debugPanelWidth = 410f;
        [SerializeField, Min(0f), Tooltip("左上开发面板占用的屏幕高度。")]
        private float debugPanelHeight = 730f;

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
        private readonly Dictionary<string, FacilityVisual> _facilityVisuals =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, FoundationFacilityAccessState> _facilityAccess =
            new(StringComparer.Ordinal);
        private readonly List<Material> _runtimeMaterials = new();
        private readonly List<Renderer> _ghostRenderers = new();
        private readonly List<Renderer[]> _interactionSlotRenderers = new();
        private MaterialPropertyBlock _facilityStatusProperties;

        private FoundationBuildOption[] _buildOptions = Array.Empty<FoundationBuildOption>();
        private FoundationPlacementPreviewState _preview;
        private FoundationFacilityState[] _facilityStates = Array.Empty<FoundationFacilityState>();
        private Transform _facilityRoot;
        private Transform _gridRoot;
        private Transform _ghostRoot;
        private Transform _interactionPreviewRoot;
        private Transform _residentRoot;
        private Transform _waterCanVisual;
        private Transform _waterCanFillVisual;
        private FoundationWaterCanLocation _waterCanLocation;
        private string _waterCanAnchorFacilityInstanceId = string.Empty;
        private Material _ghostValidMaterial;
        private Material _ghostInvalidMaterial;
        private Material _ghostPartialMaterial;
        private Material _ghostPendingMaterial;
        private Material _slotReachableMaterial;
        private Material _slotBlockedMaterial;
        private Material _slotPendingMaterial;
        private Material _slotSharedMaterial;
        private Material _slotOccupiedMaterial;
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
            ReflectionProbe configuredReflectionProbe = null)
        {
            deckLayout = configuredLayout;
            facilityDefinitions = configuredDefinitions;
            deckRoot = configuredDeckRoot;
            worldCamera = configuredCamera;
            keyLight = configuredLight;
            fillLight = configuredFillLight;
            skyboxMaterial = configuredSkyboxMaterial;
            reflectionProbe = configuredReflectionProbe;
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
                RebuildFacilities(this.ExecuteCommand(new GetFoundationFacilitiesCommand())));
            Bag.Subscribe(readModel.FacilityAccessRevision, _ =>
                UpdateFacilityAccess(
                    this.ExecuteCommand(new GetFoundationFacilityAccessCommand())));
            Bag.Subscribe(readModel.ResidentLocalPosition, position =>
            {
                if (_residentRoot != null) _residentRoot.localPosition = position;
            });
            Bag.Subscribe(readModel.ResidentLocalYawDegrees, yaw =>
            {
                if (_residentRoot != null)
                    _residentRoot.localRotation = Quaternion.Euler(0f, yaw, 0f);
            });
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
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
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
            bool overDebugPanel = IsOverDebugPanel(pointer);
            if (!overDebugPanel)
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
            if (!overDebugPanel && TryGetPointerPose(pointer, out DeckPose pose))
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

            if (!overDebugPanel && mouse.rightButton.wasPressedThisFrame)
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
                if (!IsOverDebugPanel(centroid) &&
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

        private bool IsOverDebugPanel(Vector2 pointer) =>
            pointer.x <= debugPanelWidth &&
            pointer.y >= Screen.height - debugPanelHeight;

        private void ValidateReferences()
        {
            if (deckLayout == null)
                throw new MissingReferenceException("NomadFoundationWorldView 缺少 DeckLayoutDefinition。 ");
            if (deckRoot == null)
                throw new MissingReferenceException("NomadFoundationWorldView 缺少 Deck Root。 ");
            if (worldCamera == null)
                throw new MissingReferenceException("NomadFoundationWorldView 缺少 World Camera。 ");
        }

        private void BuildDefinitionMap()
        {
            _definitions.Clear();
            for (var i = 0; i < facilityDefinitions.Length; i++)
            {
                NomadFacilityDefinition definition = facilityDefinitions[i];
                if (definition == null)
                    throw new MissingReferenceException($"WorldView 设施定义第 {i} 项为空。 ");
                if (!_definitions.TryAdd(definition.Id, definition))
                    throw new InvalidOperationException($"WorldView 设施 id '{definition.Id}' 重复。 ");

            }
        }

        private void BuildGrayboxWorld()
        {
            _grayboxFactory = new FoundationFacilityGrayboxFactory();
            _cameraController = new FoundationOrbitCameraController(
                worldCamera,
                deckRoot,
                deckLayout.DeckCenterLocal,
                new Vector3(11f, 13f, -12f));
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
                RenderSettings.ambientMode = AmbientMode.Skybox;
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
                keyLight.color = new Color(1f, 0.89f, 0.72f);
                keyLight.intensity = 1.6f;
                keyLight.shadows = LightShadows.Soft;
                keyLight.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
                RenderSettings.sun = keyLight;
            }

            if (fillLight != null)
            {
                fillLight.type = LightType.Directional;
                fillLight.color = new Color(0.52f, 0.68f, 1f);
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

            CreatePrimitive(
                PrimitiveType.Cube,
                "Deck",
                deckRoot,
                deckLayout.DeckCenterLocal + Vector3.down * 0.22f,
                deckLayout.DeckSize,
                deckMaterial);

            Vector3 center = deckLayout.DeckCenterLocal;
            _placementGrid = new FoundationPlacementGridVisual(
                deckRoot,
                deckLayout.CreateBounds(),
                gridMaterial);
            _gridRoot = _placementGrid.Root;
            _placementGrid.Rebuild(
                deckLayout.DefaultSnapSettings.PositionStepMillimeters);
            _placementGrid.SetVisible(false);

            CreatePrimitive(
                PrimitiveType.Cube,
                "Wasteland Ground",
                deckRoot,
                center + new Vector3(0f, -0.7f, 0f),
                new Vector3(80f, 0.5f, 80f),
                groundMaterial);

            _facilityRoot = new GameObject("Facilities").transform;
            _facilityRoot.SetParent(deckRoot, false);
            _ghostRoot = new GameObject("Placement Ghost").transform;
            _ghostRoot.SetParent(deckRoot, false);
            _interactionPreviewRoot = new GameObject("Placement Interaction Slots").transform;
            _interactionPreviewRoot.SetParent(deckRoot, false);
            _residentRoot = new GameObject("Resident 01").transform;
            _residentRoot.SetParent(deckRoot, false);
            BuildResidentVisual(_residentRoot);
            BuildWaterCanVisual();
        }

        private void BuildResidentVisual(Transform root)
        {
            Material body = CreateLitMaterial("M_Resident", new Color(0.78f, 0.56f, 0.28f));
            CreatePrimitive(
                PrimitiveType.Capsule,
                "Body",
                root,
                new Vector3(0f, 0.55f, 0f),
                new Vector3(0.42f, 0.55f, 0.42f),
                body);
        }

        private void BuildWaterCanVisual()
        {
            Material shell = CreateLitMaterial("M_WaterCan", new Color(0.12f, 0.58f, 0.63f));
            Material hardware = CreateLitMaterial("M_WaterCanHardware", new Color(0.08f, 0.13f, 0.14f));
            Material water = CreateLitMaterial("M_WaterCanFilled", new Color(0.1f, 0.72f, 1f));
            _waterCanVisual = new GameObject("Water Can 01 [physical carrier]").transform;
            _waterCanVisual.SetParent(deckRoot, false);
            CreatePrimitive(
                PrimitiveType.Cube,
                "Can Body",
                _waterCanVisual,
                new Vector3(0f, 0.2f, 0f),
                new Vector3(0.34f, 0.4f, 0.24f),
                shell);
            CreatePrimitive(
                PrimitiveType.Cube,
                "Handle Left",
                _waterCanVisual,
                new Vector3(-0.11f, 0.47f, 0f),
                new Vector3(0.055f, 0.18f, 0.055f),
                hardware);
            CreatePrimitive(
                PrimitiveType.Cube,
                "Handle Right",
                _waterCanVisual,
                new Vector3(0.11f, 0.47f, 0f),
                new Vector3(0.055f, 0.18f, 0.055f),
                hardware);
            CreatePrimitive(
                PrimitiveType.Cube,
                "Handle Top",
                _waterCanVisual,
                new Vector3(0f, 0.56f, 0f),
                new Vector3(0.27f, 0.055f, 0.055f),
                hardware);
            CreatePrimitive(
                PrimitiveType.Cylinder,
                "Sealed Cap",
                _waterCanVisual,
                new Vector3(0.12f, 0.43f, 0f),
                new Vector3(0.07f, 0.045f, 0.07f),
                hardware);
            _waterCanFillVisual = CreatePrimitive(
                PrimitiveType.Cube,
                "Contains Water",
                _waterCanVisual,
                new Vector3(0f, 0.2f, -0.126f),
                new Vector3(0.22f, 0.22f, 0.015f),
                water).transform;
            _waterCanFillVisual.gameObject.SetActive(false);
            UpdateWaterCanVisual();
        }

        private void RebuildFacilities(IReadOnlyList<FoundationFacilityState> facilities)
        {
            if (_facilityRoot == null) return;
            _facilityStates = new FoundationFacilityState[facilities.Count];
            for (var i = 0; i < facilities.Count; i++) _facilityStates[i] = facilities[i];
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
                root.localPosition = deckLayout.PoseToLocal(pose, 0.02f);
                root.localRotation = Quaternion.Euler(0f, (float)pose.YawDegrees, 0f);
                Renderer[] bodyRenderers = _grayboxFactory.Build(root, definition);
                var interactionRoot = new GameObject("Interaction Slots (build mode)")
                    .transform;
                interactionRoot.SetParent(root, false);
                var slotRenderers = new List<Renderer[]>();
                BuildInteractionSlotVisuals(interactionRoot, definition, slotRenderers);
                interactionRoot.gameObject.SetActive(false);
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
                        bodyRenderers,
                        slotRenderers.ToArray(),
                        groupVisuals.ToArray()));
            }
            ApplyFacilityAccessVisuals();
            UpdateWaterCanVisual();
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
                bool showStatusTint = inBuildMode &&
                                      displayAccess is FoundationFacilityAccess.Unreachable or
                                          FoundationFacilityAccess.PartiallyReachable;
                Color tint = displayAccess == FoundationFacilityAccess.Unreachable
                    ? new Color(1f, 0.08f, 0.05f, 1f)
                    : new Color(1f, 0.42f, 0.08f, 1f);
                for (var rendererIndex = 0;
                     rendererIndex < visual.BodyRenderers.Length;
                     rendererIndex++)
                {
                    Renderer renderer = visual.BodyRenderers[rendererIndex];
                    if (renderer == null) continue;
                    if (!showStatusTint)
                    {
                        renderer.SetPropertyBlock(null);
                        continue;
                    }

                    _facilityStatusProperties.Clear();
                    Material material = renderer.sharedMaterial;
                    if (material != null && material.HasProperty("_BaseColor"))
                        _facilityStatusProperties.SetColor("_BaseColor", tint);
                    if (material != null && material.HasProperty("_Color"))
                        _facilityStatusProperties.SetColor("_Color", tint);
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
            }
        }

        private void OnInteractionModeChanged(FoundationInteractionMode mode)
        {
            _interactionMode = mode;
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
            if (_waterCanLocation == FoundationWaterCanLocation.Resident)
            {
                _waterCanVisual.gameObject.SetActive(_residentRoot != null);
                if (_residentRoot == null) return;
                _waterCanVisual.SetParent(_residentRoot, false);
                _waterCanVisual.localPosition = new Vector3(0.42f, 0.32f, 0f);
                _waterCanVisual.localRotation = Quaternion.identity;
                return;
            }

            NomadFacilityFunction expectedFunction =
                _waterCanLocation == FoundationWaterCanLocation.VehicleWaterTank
                    ? NomadFacilityFunction.VehicleWaterTank
                    : NomadFacilityFunction.DrinkingStation;
            for (var i = 0; i < _facilityStates.Length; i++)
            {
                FoundationFacilityState state = _facilityStates[i];
                if (!_definitions.TryGetValue(
                        state.DefinitionId,
                        out NomadFacilityDefinition definition) ||
                    definition.Function != expectedFunction ||
                    (!string.IsNullOrEmpty(_waterCanAnchorFacilityInstanceId) &&
                     !string.Equals(
                         state.InstanceId,
                         _waterCanAnchorFacilityInstanceId,
                         StringComparison.Ordinal)))
                    continue;

                Quaternion rotation = Quaternion.Euler(0f, (float)state.Pose.YawDegrees, 0f);
                Vector3 sideOffset = expectedFunction == NomadFacilityFunction.VehicleWaterTank
                    ? new Vector3(definition.PrototypeSize.x * 0.72f + 0.22f, 0.02f, 0f)
                    : new Vector3(definition.PrototypeSize.x * 0.62f + 0.22f, 0.02f, 0f);
                _waterCanVisual.SetParent(deckRoot, false);
                _waterCanVisual.localPosition = deckLayout.PoseToLocal(state.Pose) + rotation * sideOffset;
                _waterCanVisual.localRotation = rotation;
                _waterCanVisual.gameObject.SetActive(true);
                return;
            }

            _waterCanVisual.gameObject.SetActive(false);
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
                Renderer[] bodyRenderers,
                Renderer[][] slotRenderers,
                InteractionGroupVisual[] interactionGroups)
            {
                State = state;
                Root = root;
                InteractionRoot = interactionRoot;
                BodyRenderers = bodyRenderers ?? Array.Empty<Renderer>();
                SlotRenderers = slotRenderers ?? Array.Empty<Renderer[]>();
                InteractionGroups = interactionGroups ??
                                    Array.Empty<InteractionGroupVisual>();
            }

            public FoundationFacilityState State { get; }
            public Transform Root { get; }
            public Transform InteractionRoot { get; }
            public Renderer[] BodyRenderers { get; }
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
