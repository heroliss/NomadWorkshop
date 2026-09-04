using System;
using Game.Framework.Common;
using Game.Framework.View;
using Game.NomadWorkshop.Simulation;
using R3;
using UnityEngine;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>
    /// 开发期 IMGUI 调试 View。它与正式 3D View 使用同一读模型和 Command，后续换成 UGUI/UI Toolkit
    /// 不会改变 System；当前重点是让人和 AI 都能快速观察、暂停、加速和复位。
    /// </summary>
    public sealed class NomadFoundationDebugView : MonoViewBase
    {
        private FoundationBuildOption[] _buildOptions = Array.Empty<FoundationBuildOption>();
        private bool _ready;
        private bool _paused;
        private float _speed;
        private long _simulationTick;
        private long _lifeDay;
        private int _lifeMinuteOfDay;
        private int _lifeDayProgressPermille;
        private long _climateYear;
        private int _seasonIndex;
        private int _climateWeekInSeason;
        private FoundationInteractionMode _interactionMode;
        private FoundationPlacementPreviewState _preview;
        private FoundationFacilityAccessState[] _facilityAccess =
            Array.Empty<FoundationFacilityAccessState>();
        private FoundationBuildTransactionPhase _buildTransactionPhase;
        private int _positionSnapMillimeters;
        private int _rotationSnapDeciDegrees;
        private bool _showPlacementGrid;
        private FoundationResidentPhase _residentPhase;
        private float _remainingPathMeters;
        private int _remainingPathCorners;
        private string _activePathSummary = string.Empty;
        private FoundationWaterCanLocation _waterCanLocation;
        private string _waterCanAnchorFacilityInstanceId = string.Empty;
        private FoundationFacilityInventoryState[] _facilityInventories =
            Array.Empty<FoundationFacilityInventoryState>();
        private int _waterCanWaterMilliliters;
        private int _waterCanCapacityMilliliters;
        private FoundationActionPlanProjection _latestActionPlan;
        private float _thirst;
        private float _recreation;
        private int _vehicleWaterMilliliters;
        private int _vehicleWaterCapacityMilliliters;
        private int _stationWaterMilliliters;
        private int _stationWaterCapacityMilliliters;
        private int _bodyWaterMilliliters;
        private int _bodyWaterCapacityMilliliters;
        private int _bladderWasteMilliliters;
        private int _bladderCapacityMilliliters;
        private int _toiletHoldingWasteMilliliters;
        private int _toiletHoldingCapacityMilliliters;
        private int _completedDrinks;
        private int _completedToiletUses;
        private int _completedLeisure;
        private int _completedDaydreams;
        private int _completedWanders;
        private float _actionProgress;
        private string _currentTask = string.Empty;
        private string _lastBlocker = string.Empty;
        private GUIStyle _titleStyle;
        private GUIStyle _sectionStyle;
        private GUIStyle _smallStyle;

        protected override void Awake()
        {
            base.Awake();
            FoundationReadModel readModel = this.ExecuteCommand(new GetFoundationReadModelCommand());
            _buildOptions = this.ExecuteCommand(new GetFoundationBuildOptionsCommand());
            Bag.Subscribe(readModel.IsReady, value => _ready = value);
            Bag.Subscribe(readModel.IsPaused, value => _paused = value);
            Bag.Subscribe(readModel.SimulationSpeed, value => _speed = value);
            Bag.Subscribe(readModel.SimulationTick, value => _simulationTick = value);
            Bag.Subscribe(readModel.LifeDay, value => _lifeDay = value);
            Bag.Subscribe(readModel.LifeMinuteOfDay, value => _lifeMinuteOfDay = value);
            Bag.Subscribe(
                readModel.LifeDayProgressPermille,
                value => _lifeDayProgressPermille = value);
            Bag.Subscribe(readModel.ClimateYear, value => _climateYear = value);
            Bag.Subscribe(readModel.SeasonIndex, value => _seasonIndex = value);
            Bag.Subscribe(
                readModel.ClimateWeekInSeason,
                value => _climateWeekInSeason = value);
            Bag.Subscribe(readModel.InteractionMode, value => _interactionMode = value);
            Bag.Subscribe(readModel.PlacementPreview, value => _preview = value);
            Bag.Subscribe(readModel.FacilityAccessRevision, _ =>
                _facilityAccess = this.ExecuteCommand(
                    new GetFoundationFacilityAccessCommand()));
            Bag.Subscribe(readModel.FacilityInventoryRevision, _ =>
                _facilityInventories = this.ExecuteCommand(
                    new GetFoundationFacilityInventoriesCommand()));
            Bag.Subscribe(readModel.BuildTransactionPhase, value => _buildTransactionPhase = value);
            Bag.Subscribe(readModel.PositionSnapMillimeters, value => _positionSnapMillimeters = value);
            Bag.Subscribe(readModel.RotationSnapDeciDegrees, value => _rotationSnapDeciDegrees = value);
            Bag.Subscribe(readModel.ShowPlacementGrid, value => _showPlacementGrid = value);
            Bag.Subscribe(readModel.ResidentPhase, value => _residentPhase = value);
            Bag.Subscribe(readModel.RemainingPathMeters, value => _remainingPathMeters = value);
            Bag.Subscribe(readModel.RemainingPathCorners, value => _remainingPathCorners = value);
            Bag.Subscribe(readModel.ActivePathSummary, value => _activePathSummary = value);
            Bag.Subscribe(readModel.WaterCanLocation, value => _waterCanLocation = value);
            Bag.Subscribe(
                readModel.WaterCanAnchorFacilityInstanceId,
                value => _waterCanAnchorFacilityInstanceId = value ?? string.Empty);
            Bag.Subscribe(
                readModel.WaterCanWaterMilliliters,
                value => _waterCanWaterMilliliters = value);
            Bag.Subscribe(
                readModel.WaterCanCapacityMilliliters,
                value => _waterCanCapacityMilliliters = value);
            Bag.Subscribe(readModel.LatestActionPlan, value => _latestActionPlan = value);
            Bag.Subscribe(readModel.ResidentThirst, value => _thirst = value);
            Bag.Subscribe(readModel.ResidentRecreation, value => _recreation = value);
            Bag.Subscribe(
                readModel.VehicleWaterMilliliters,
                value => _vehicleWaterMilliliters = value);
            Bag.Subscribe(
                readModel.VehicleWaterCapacityMilliliters,
                value => _vehicleWaterCapacityMilliliters = value);
            Bag.Subscribe(
                readModel.DrinkingStationWaterMilliliters,
                value => _stationWaterMilliliters = value);
            Bag.Subscribe(
                readModel.DrinkingStationCapacityMilliliters,
                value => _stationWaterCapacityMilliliters = value);
            Bag.Subscribe(
                readModel.BodyWaterMilliliters,
                value => _bodyWaterMilliliters = value);
            Bag.Subscribe(
                readModel.BodyWaterCapacityMilliliters,
                value => _bodyWaterCapacityMilliliters = value);
            Bag.Subscribe(
                readModel.BladderWasteMilliliters,
                value => _bladderWasteMilliliters = value);
            Bag.Subscribe(
                readModel.BladderCapacityMilliliters,
                value => _bladderCapacityMilliliters = value);
            Bag.Subscribe(
                readModel.ToiletHoldingWasteMilliliters,
                value => _toiletHoldingWasteMilliliters = value);
            Bag.Subscribe(
                readModel.ToiletHoldingCapacityMilliliters,
                value => _toiletHoldingCapacityMilliliters = value);
            Bag.Subscribe(readModel.CompletedDrinkCount, value => _completedDrinks = value);
            Bag.Subscribe(readModel.CompletedToiletUseCount, value => _completedToiletUses = value);
            Bag.Subscribe(readModel.CompletedLeisureCount, value => _completedLeisure = value);
            Bag.Subscribe(readModel.CompletedDaydreamCount, value => _completedDaydreams = value);
            Bag.Subscribe(readModel.CompletedWanderCount, value => _completedWanders = value);
            Bag.Subscribe(readModel.ActionProgress, value => _actionProgress = value);
            Bag.Subscribe(readModel.CurrentTask, value => _currentTask = value);
            Bag.Subscribe(readModel.LastBlocker, value => _lastBlocker = value);
        }

        private void OnGUI()
        {
            EnsureStyles();
            const float width = 390f;
            GUILayout.BeginArea(
                new Rect(14f, 14f, width, Mathf.Min(Screen.height - 28f, 710f)),
                GUI.skin.box);
            GUILayout.Label("游牧工坊 · 最小垂直切片", _titleStyle);
            GUILayout.Label(
                "连续建造 → 实体容器搬水 → 饮水；完整方案参与 Utility 决策",
                _smallStyle);
            GUILayout.Label(
                $"第 {_lifeDay} 生活日 · {_lifeMinuteOfDay / 60:00}:" +
                $"{_lifeMinuteOfDay % 60:00} · 气候年 {_climateYear}",
                _smallStyle);
            GUILayout.Label(
                $"季节相位 {_seasonIndex + 1} 第 {_climateWeekInSeason} 周 · " +
                $"日进度 {_lifeDayProgressPermille / 10f:0.0}% · " +
                $"统一 Tick {_simulationTick / 1000d:0.000}s",
                _smallStyle);

            GUILayout.Space(7f);
            GUILayout.Label("建造", _sectionStyle);
            if (GUILayout.Button(
                    _interactionMode == FoundationInteractionMode.Build
                        ? "退出建造模式（B / 空幽灵时 Esc）"
                        : "进入建造模式（B）",
                    GUILayout.Height(30f)))
            {
                if (_interactionMode == FoundationInteractionMode.Build)
                    this.ExecuteCommand(new ExitFoundationBuildModeCommand());
                else
                    this.ExecuteCommand(new EnterFoundationBuildModeCommand());
            }

            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled &&
                          _interactionMode == FoundationInteractionMode.Build;
            GUILayout.BeginHorizontal();
            for (var i = 0; i < _buildOptions.Length; i++)
            {
                FoundationBuildOption option = _buildOptions[i];
                if (GUILayout.Button($"{option.Shortcut}  {option.DisplayName}", GUILayout.Height(30f)))
                    this.ExecuteCommand(new BeginFacilityPlacementCommand(option.DefinitionId));
            }
            GUILayout.EndHorizontal();
            GUI.enabled = previousEnabled;

            if (_preview.Active)
            {
                GUILayout.Label(
                    $"摆放：{GetBuildDisplayName(_preview.DefinitionId)}  " +
                    $"X={_preview.Pose.XMeters:0.###}m  Z={_preview.Pose.ZMeters:0.###}m  " +
                    $"朝向 {_preview.Pose.YawDegrees:0.#}°");
                bool pending = _preview.Failure ==
                               FoundationPlacementFailure.TransactionInProgress;
                GUI.color = pending
                    ? new Color(1f, 0.78f, 0.34f)
                    : _preview.HasReachabilityWarning
                        ? new Color(1f, 0.58f, 0.28f)
                        : _preview.CanConfirm
                            ? new Color(0.55f, 1f, 0.68f)
                            : new Color(1f, 0.55f, 0.45f);
                string placementMessage = pending
                    ? "正在更新 NavMesh"
                    : _preview.HasReachabilityWarning
                        ? $"可建造（可达性警告）：{Describe(_preview.Failure)}；左键仍可确认"
                        : _preview.CanConfirm
                            ? "可建造：左键确认"
                            : $"不可建造：{Describe(_preview.Failure)}";
                GUILayout.Label(placementMessage);
                GUI.color = Color.white;
                if (_preview.RealtimeReachabilityEvaluated)
                {
                    GUILayout.Label(
                        $"交互位：{_preview.ReachableInteractionSlotCount}/" +
                        $"{_preview.InteractionSlotCount} 可达 · 实时通路预检 " +
                        $"{_preview.RealtimeProbeMilliseconds:0.###} ms · " +
                        $"访问 {_preview.RealtimeVisitedCells}/{_preview.RealtimeProbeCellCount} 采样点",
                        _smallStyle);
                }
                int affectedFacilities = CountPreviewAccessIssues();
                int degradedFacilities = CountPreviewAccessDegradations();
                if (affectedFacilities > 0)
                {
                    string changeSummary = degradedFacilities > 0
                        ? $"其中 {degradedFacilities} 座的设施级可达等级变差"
                        : "没有设施级可达等级进一步下降";
                    GUILayout.Label(
                        $"建造后 {affectedFacilities} 座既有设施存在不可达功能点，" +
                        $"{changeSummary}；" +
                        "它们与全部停靠位已在建造视图中持续标色。",
                        _smallStyle);
                }
                else if (_preview.InteractionSlotCount > 0)
                {
                    GUILayout.Label(
                        $"交互位：{_preview.InteractionSlotCount} 个 · 几何预检通过后计算通路",
                        _smallStyle);
                }
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Q 左转"))
                    this.ExecuteCommand(new RotateFacilityPreviewCommand(-1));
                if (GUILayout.Button("E 右转"))
                    this.ExecuteCommand(new RotateFacilityPreviewCommand(1));
                if (GUILayout.Button("Esc / 取消"))
                    this.ExecuteCommand(new CancelFacilityPlacementCommand());
                GUILayout.EndHorizontal();
                GUILayout.Label("右键逆时针旋转；中键拖动 / 滚轮，或双指拖动 / 捏合控制镜头。", _smallStyle);
                if (_buildTransactionPhase != FoundationBuildTransactionPhase.Idle)
                    GUILayout.Label($"NavMesh 事务：{Describe(_buildTransactionPhase)}", _smallStyle);
            }
            else if (_interactionMode == FoundationInteractionMode.Build)
            {
                GUILayout.Label(
                    "建造诊断已开启：全部设施功能点与停靠位持续显示；数字键 1–3 选择设施。",
                    _smallStyle);
            }
            else
            {
                GUILayout.Label("普通模式：按 B 或上方按钮进入建造。滚轮 / 中键或双指手势控制镜头。", _smallStyle);
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button($"位置吸附 {DescribePositionSnap(_positionSnapMillimeters)}"))
                this.ExecuteCommand(new SetFoundationPositionSnapCommand(
                    DeckPlacementSnapPresets.NextPlayerPositionStep(
                        _positionSnapMillimeters)));
            if (GUILayout.Button($"旋转吸附 {DescribeRotationSnap(_rotationSnapDeciDegrees)}"))
                this.ExecuteCommand(new SetFoundationRotationSnapCommand(
                    NextValue(_rotationSnapDeciDegrees, 0, 50, 150, 450, 900)));
            GUI.enabled = previousEnabled && _positionSnapMillimeters > 0;
            string gridButtonLabel = _positionSnapMillimeters == 0
                ? "自由模式无网格"
                : _showPlacementGrid ? "关网格" : "开网格";
            if (GUILayout.Button(gridButtonLabel))
                this.ExecuteCommand(new SetFoundationGridVisibleCommand(!_showPlacementGrid));
            GUI.enabled = previousEnabled;
            GUILayout.EndHorizontal();

            GUILayout.Space(7f);
            GUILayout.Label("居民与资源", _sectionStyle);
            GUILayout.Label($"状态：{Describe(_residentPhase)}  ·  {_currentTask}");
            if (_remainingPathMeters > 0f)
                GUILayout.Label(
                    $"NavMesh 路径：剩余 {_remainingPathMeters:0.00}m / " +
                    $"{_remainingPathCorners} 拐点 · {_activePathSummary}",
                    _smallStyle);
            DrawMeter("口渴", _thirst);
            DrawMeter("娱乐缺口", _recreation);
            DrawMeter("当前动作", _actionProgress);
            GUILayout.Label(
                $"车辆水箱 {FormatVolume(_vehicleWaterMilliliters, _vehicleWaterCapacityMilliliters)}   " +
                $"全部饮水站 {FormatVolume(_stationWaterMilliliters, _stationWaterCapacityMilliliters)}");
            for (var i = 0; i < _facilityInventories.Length; i++)
            {
                FoundationFacilityInventoryState inventory = _facilityInventories[i];
                GUILayout.Label(
                    $"  {inventory.FacilityInstanceId}/{inventory.CompartmentId}  " +
                    FormatVolume(inventory.Amount, inventory.Capacity),
                    _smallStyle);
            }
            GUILayout.Label(
                $"体内待代谢水 {FormatVolume(_bodyWaterMilliliters, _bodyWaterCapacityMilliliters)}   " +
                $"膀胱内容物 {FormatVolume(_bladderWasteMilliliters, _bladderCapacityMilliliters)}",
                _smallStyle);
            GUILayout.Label(
                $"旱厕暂存桶 {FormatVolume(_toiletHoldingWasteMilliliters, _toiletHoldingCapacityMilliliters)}",
                _smallStyle);
            GUILayout.Label(
                $"唯一防漏水罐：{Describe(_waterCanLocation)} · " +
                $"锚点 {DescribeAnchor(_waterCanAnchorFacilityInstanceId)} · " +
                $"内含水 {FormatVolume(_waterCanWaterMilliliters, _waterCanCapacityMilliliters)}",
                _smallStyle);
            if (_latestActionPlan.Evaluated)
            {
                GUILayout.Label(
                    $"最近方案：{_latestActionPlan.DisplayName} · " +
                    $"{(_latestActionPlan.Selected ? "已选中" : "未选中")} · " +
                    $"{_latestActionPlan.StepCount} 步 / {_latestActionPlan.TotalDurationSeconds:0.00}s · " +
                    $"行程 {_latestActionPlan.TravelDistanceMeters:0.00}m / " +
                    $"{_latestActionPlan.TravelSeconds:0.00}s · 效用 {_latestActionPlan.TotalUtility:0.000}",
                    _smallStyle);
                GUILayout.Label(
                    $"方案成本：行程 {_latestActionPlan.TravelCost:0.000} + " +
                    $"动作 {_latestActionPlan.ActiveCost:0.000} + " +
                    $"体力 {_latestActionPlan.EffortCost:0.000} + " +
                    $"风险 {_latestActionPlan.ExpectedRiskCost:0.000} = " +
                    $"{_latestActionPlan.PlanCost:0.000}",
                    _smallStyle);
            }
            GUILayout.Label(
                $"已完成饮水：{_completedDrinks}   如厕：{_completedToiletUses}   " +
                $"自主休闲：{_completedLeisure}（发呆 {_completedDaydreams} / 散步 {_completedWanders}）");
            if (!string.IsNullOrEmpty(_lastBlocker))
            {
                bool hardBlocked = _residentPhase == FoundationResidentPhase.Blocked;
                GUI.color = hardBlocked
                    ? new Color(1f, 0.55f, 0.42f)
                    : new Color(1f, 0.78f, 0.34f);
                GUILayout.Label($"{(hardBlocked ? "硬阻塞" : "状态诊断")}：{_lastBlocker}");
                GUI.color = Color.white;
            }

            GUILayout.Space(7f);
            GUILayout.Label("Harness", _sectionStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_paused ? "继续" : "暂停"))
                this.ExecuteCommand(new SetFoundationPausedCommand(!_paused));
            if (GUILayout.Button("0.5×")) this.ExecuteCommand(new SetFoundationSpeedCommand(0.5f));
            if (GUILayout.Button("1×")) this.ExecuteCommand(new SetFoundationSpeedCommand(1f));
            if (GUILayout.Button("4×")) this.ExecuteCommand(new SetFoundationSpeedCommand(4f));
            if (GUILayout.Button("复位")) this.ExecuteCommand(new ResetFoundationSliceCommand());
            GUILayout.EndHorizontal();
            GUILayout.Label($"Ready={_ready} · Speed={_speed:0.##}×", _smallStyle);
            GUILayout.EndArea();
        }

        private static void DrawMeter(string label, float value)
        {
            Rect rect = GUILayoutUtility.GetRect(1f, 19f, GUILayout.ExpandWidth(true));
            GUI.Box(rect, GUIContent.none);
            var fill = rect;
            fill.width *= Mathf.Clamp01(value);
            GUI.color = new Color(0.18f, 0.76f, 0.82f);
            GUI.DrawTexture(fill, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(rect, $"  {label}  {value:P0}");
        }

        private void EnsureStyles()
        {
            if (_titleStyle != null) return;
            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.95f, 0.78f, 0.42f) },
            };
            _sectionStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.45f, 0.88f, 0.88f) },
            };
            _smallStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                wordWrap = true,
                normal = { textColor = new Color(0.74f, 0.78f, 0.78f) },
            };
        }

        private static string Describe(FoundationPlacementFailure failure) => failure switch
        {
            FoundationPlacementFailure.FootprintOutOfBounds => "设施超出甲板",
            FoundationPlacementFailure.FootprintOverlapsFacility => "设施连续占地重叠",
            FoundationPlacementFailure.DeckLevelUnavailable => "甲板层不可用",
            FoundationPlacementFailure.RequiredInteractionUnreachable =>
                "候选或既有设施存在不可达功能点",
            FoundationPlacementFailure.NavigationUpdateFailed => "NavMesh 更新失败",
            FoundationPlacementFailure.TransactionInProgress => "正在校验导航与交互位",
            FoundationPlacementFailure.DuplicateInstanceId => "实例身份重复",
            FoundationPlacementFailure.InvalidRequest => "摆放数据无效",
            _ => failure.ToString(),
        };

        private int CountPreviewAccessIssues()
        {
            var result = 0;
            for (var i = 0; i < _facilityAccess.Length; i++)
            {
                FoundationFacilityAccessState state = _facilityAccess[i];
                if (state.PreviewEvaluated &&
                    IsAccessIssue(state.PreviewAccess))
                    result++;
            }
            return result;
        }

        private int CountPreviewAccessDegradations()
        {
            var result = 0;
            for (var i = 0; i < _facilityAccess.Length; i++)
            {
                FoundationFacilityAccessState state = _facilityAccess[i];
                if (state.PreviewEvaluated &&
                    AccessSeverity(state.PreviewAccess) >
                    AccessSeverity(state.CommittedAccess))
                    result++;
            }
            return result;
        }

        private static bool IsAccessIssue(FoundationFacilityAccess access) =>
            access is FoundationFacilityAccess.PartiallyReachable or
                FoundationFacilityAccess.Unreachable;

        private static int AccessSeverity(FoundationFacilityAccess access) => access switch
        {
            FoundationFacilityAccess.Reachable => 0,
            FoundationFacilityAccess.PartiallyReachable => 1,
            FoundationFacilityAccess.Unreachable => 2,
            _ => -1,
        };

        private string GetBuildDisplayName(string definitionId)
        {
            for (var i = 0; i < _buildOptions.Length; i++)
            {
                if (string.Equals(
                        _buildOptions[i].DefinitionId,
                        definitionId,
                        StringComparison.Ordinal))
                    return _buildOptions[i].DisplayName;
            }
            return definitionId;
        }

        private static string Describe(FoundationBuildTransactionPhase phase) => phase switch
        {
            FoundationBuildTransactionPhase.UpdatingCandidateNavigation => "更新候选导航",
            FoundationBuildTransactionPhase.RollingBackNavigation => "回滚候选导航",
            _ => "空闲",
        };

        private static string DescribePositionSnap(int millimeters) =>
            millimeters == 0 ? "自由（无网格）" : $"{millimeters / 1000f:0.##}m";

        private static string DescribeRotationSnap(int deciDegrees) =>
            deciDegrees == 0 ? "关" : $"{deciDegrees / 10f:0.#}°";

        private static string FormatVolume(int amountMilliliters, int capacityMilliliters) =>
            $"{ResourceAmountFormatting.Format(amountMilliliters, ResourceMeasure.Milliliter)} / " +
            ResourceAmountFormatting.Format(capacityMilliliters, ResourceMeasure.Milliliter);

        private static string DescribeAnchor(string facilityInstanceId) =>
            string.IsNullOrEmpty(facilityInstanceId) ? "随居民携带" : facilityInstanceId;

        private static int NextValue(int current, params int[] values)
        {
            for (var i = 0; i < values.Length; i++)
            {
                if (values[i] == current) return values[(i + 1) % values.Length];
            }
            return values[0];
        }

        private static string Describe(FoundationResidentPhase phase) => phase switch
        {
            FoundationResidentPhase.WaitingForRoute => "等待路线重试",
            FoundationResidentPhase.WaitingForFacility => "等待设施",
            FoundationResidentPhase.Idle => "空闲",
            FoundationResidentPhase.MovingToWaterCan => "前往水罐",
            FoundationResidentPhase.PickingUpWaterCan => "取得水罐",
            FoundationResidentPhase.MovingToWaterSource => "前往水箱",
            FoundationResidentPhase.PickingUpWater => "取水",
            FoundationResidentPhase.MovingToDrinkingStation => "搬水",
            FoundationResidentPhase.DeliveringWater => "放入饮水站",
            FoundationResidentPhase.Drinking => "饮水",
            FoundationResidentPhase.MovingToToilet => "前往旱厕",
            FoundationResidentPhase.UsingToilet => "如厕",
            FoundationResidentPhase.MovingToLeisure => "前往空地",
            FoundationResidentPhase.Relaxing => "休息娱乐",
            FoundationResidentPhase.Blocked => "阻塞",
            _ => phase.ToString(),
        };

        private static string Describe(FoundationWaterCanLocation location) => location switch
        {
            FoundationWaterCanLocation.VehicleWaterTank => "车辆水箱旁",
            FoundationWaterCanLocation.DrinkingStation => "饮水站旁",
            FoundationWaterCanLocation.Resident => "居民手中",
            _ => location.ToString(),
        };
    }
}
