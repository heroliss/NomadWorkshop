using System;
using Game.Framework.Common;
using Game.Framework.View;
using Game.NomadWorkshop.Simulation;
using R3;
using UnityEngine;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>
    /// 首版 IMGUI 信息 View：默认只显示玩家需要的居民摘要，详情与开发控制台按需展开。
    /// 它与正式 3D View 使用同一读模型和 Command，后续换成 UGUI / UI Toolkit 不会改变 System；
    /// 当前重点是先验证信息层级、状态语义，以及让人和 AI 都能快速观察、暂停、加速和复位。
    /// </summary>
    public sealed class NomadFoundationDebugView : MonoViewBase
    {
        private enum FoundationInformationPanel
        {
            None,
            Resident,
            Developer,
        }

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
        private float _entertainment;
        private float _mood;
        private float _fatigue;
        private float _stress;
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
        [SerializeField, HideInInspector]
        private FoundationInformationPanel _openPanel;
        private Vector2 _residentPanelScroll;
        private Vector2 _developerPanelScroll;
        private GUIStyle _titleStyle;
        private GUIStyle _sectionStyle;
        private GUIStyle _smallStyle;
        private GUIStyle _compactNameStyle;
        private GUIStyle _compactTaskStyle;
        private GUIStyle _meterLabelStyle;
        private GUIStyle _meterValueStyle;
        private GUIStyle _iconStyle;

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
            Bag.Subscribe(readModel.ResidentEntertainment, value => _entertainment = value);
            Bag.Subscribe(readModel.ResidentMood, value => _mood = value);
            Bag.Subscribe(readModel.ResidentFatigue, value => _fatigue = value);
            Bag.Subscribe(readModel.ResidentStress, value => _stress = value);
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
            DrawCompactResidentCard();
            DrawCornerToolbar();

            if (_openPanel == FoundationInformationPanel.Resident)
                DrawResidentDetailsPanel();
            else if (_openPanel == FoundationInformationPanel.Developer ||
                     _interactionMode == FoundationInteractionMode.Build)
                DrawDeveloperPanel();

            DrawTooltip();
        }

        /// <summary>
        /// 默认常驻信息只保留一个居民摘要卡，避免开发数据遮住车辆。当前只有一名居民，后续改为集合绑定时
        /// 这里会成为居民列表的紧凑行，而 System 与读模型仍不需要知道具体 UI 技术。
        /// </summary>
        private void DrawCompactResidentCard()
        {
            float width = Mathf.Min(370f, Mathf.Max(280f, Screen.width - 28f));
            var outer = new Rect(14f, 14f, width, 112f);
            DrawPanelBackground(
                outer,
                new Color(0.10f, 0.13f, 0.14f, 0.94f),
                new Color(0.22f, 0.66f, 0.68f, 0.95f));

            GUILayout.BeginArea(new Rect(outer.x + 10f, outer.y + 8f, outer.width - 20f, outer.height - 16f));
            GUILayout.BeginHorizontal();
            Rect avatar = GUILayoutUtility.GetRect(28f, 28f, GUILayout.Width(28f), GUILayout.Height(28f));
            DrawIconBadge(avatar, "人", new Color(0.2f, 0.72f, 0.74f));
            GUILayout.BeginVertical();
            GUILayout.Label("居民 01", _compactNameStyle, GUILayout.Height(18f));
            GUILayout.Label(
                $"{Describe(_residentPhase)} · {_currentTask}",
                _compactTaskStyle,
                GUILayout.Height(17f));
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.Space(2f);
            GUILayout.BeginHorizontal();
            DrawMiniStatus("水", "水分", 1f - _thirst, new Color(0.2f, 0.72f, 0.94f));
            DrawMiniStatus("能", "精力", 1f - _fatigue, new Color(0.35f, 0.82f, 0.48f));
            DrawMiniStatus("心", "心情", _mood, new Color(0.92f, 0.62f, 0.3f));
            GUILayout.EndHorizontal();

            Rect actionRect = GUILayoutUtility.GetRect(1f, 5f, GUILayout.ExpandWidth(true));
            DrawProgressBar(actionRect, _actionProgress, new Color(0.22f, 0.78f, 0.82f));
            GUILayout.EndArea();
        }

        private void DrawCornerToolbar()
        {
            float width = Mathf.Min(310f, Mathf.Max(250f, Screen.width - 28f));
            var outer = new Rect(Mathf.Max(14f, Screen.width - width - 14f), 14f, width, 36f);
            DrawPanelBackground(
                outer,
                new Color(0.09f, 0.11f, 0.12f, 0.94f),
                new Color(0.28f, 0.32f, 0.33f, 0.95f));
            GUILayout.BeginArea(new Rect(outer.x + 4f, outer.y + 4f, outer.width - 8f, outer.height - 8f));
            GUILayout.BeginHorizontal();
            if (DrawToolbarButton(
                    new GUIContent("居民详情", "查看居民的水分、精力、心情、娱乐、压力与生理状态。"),
                    _openPanel == FoundationInformationPanel.Resident))
            {
                _openPanel = _openPanel == FoundationInformationPanel.Resident
                    ? FoundationInformationPanel.None
                    : FoundationInformationPanel.Resident;
            }

            if (DrawToolbarButton(
                    new GUIContent(
                        _interactionMode == FoundationInteractionMode.Build ? "退出建造" : "建造",
                        "进入建造模式并打开设施、吸附与可达性控制。"),
                    _interactionMode == FoundationInteractionMode.Build))
            {
                if (_interactionMode == FoundationInteractionMode.Build)
                {
                    this.ExecuteCommand(new ExitFoundationBuildModeCommand());
                    _openPanel = FoundationInformationPanel.None;
                }
                else
                {
                    this.ExecuteCommand(new EnterFoundationBuildModeCommand());
                    _openPanel = FoundationInformationPanel.Developer;
                }
            }

            if (DrawToolbarButton(
                    new GUIContent("开发", "打开完整的方案、资源、建造与 Harness 诊断。"),
                    _openPanel == FoundationInformationPanel.Developer))
            {
                _openPanel = _openPanel == FoundationInformationPanel.Developer
                    ? FoundationInformationPanel.None
                    : FoundationInformationPanel.Developer;
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawResidentDetailsPanel()
        {
            float width = Mathf.Min(390f, Mathf.Max(300f, Screen.width - 28f));
            float height = Mathf.Max(160f, Screen.height - 72f);
            var outer = new Rect(Mathf.Max(14f, Screen.width - width - 14f), 58f, width, height);
            DrawPanelBackground(
                outer,
                new Color(0.08f, 0.105f, 0.115f, 0.97f),
                new Color(0.24f, 0.7f, 0.72f, 0.95f));

            GUILayout.BeginArea(new Rect(outer.x + 10f, outer.y + 8f, outer.width - 20f, outer.height - 16f));
            _residentPanelScroll = GUILayout.BeginScrollView(_residentPanelScroll);
            GUILayout.Label("居民 01 · 身心状态", _titleStyle);
            GUILayout.Label(
                $"{Describe(_residentPhase)} · {_currentTask}",
                _smallStyle);
            GUILayout.Space(6f);

            DrawStatusMeter(
                "水",
                "水分",
                1f - _thirst,
                higherIsBetter: true,
                new Color(0.2f, 0.72f, 0.94f),
                "由口渴缺口反向显示；越高表示当前越不需要喝水。");
            DrawStatusMeter(
                "能",
                "精力",
                1f - _fatigue,
                higherIsBetter: true,
                new Color(0.35f, 0.82f, 0.48f),
                "疲劳负担的反向显示；发呆与闲逛只能缓慢恢复。");
            DrawStatusMeter(
                "心",
                "心情",
                _mood,
                higherIsBetter: true,
                new Color(0.92f, 0.62f, 0.3f),
                "娱乐不足、持续压力和疲劳会让心情连续下降。");
            DrawStatusMeter(
                "趣",
                "娱乐满足",
                _entertainment,
                higherIsBetter: true,
                new Color(0.72f, 0.46f, 0.92f),
                "只有真实爱好、社交或娱乐设施才会明显提高；普通发呆和闲逛不会补充。");
            DrawStatusMeter(
                "压",
                "压力",
                _stress,
                higherIsBetter: false,
                new Color(0.28f, 0.78f, 0.68f),
                "越低越好；缺水、憋尿、阻塞、疲劳和无聊都会增加压力。");
            DrawStatusMeter(
                "尿",
                "膀胱负担",
                SafeRatio(_bladderWasteMilliliters, _bladderCapacityMilliliters),
                higherIsBetter: false,
                new Color(0.34f, 0.76f, 0.88f),
                "达到 50% 后如厕机会平滑上升，90% 后进入紧迫风险层。");

            GUILayout.Space(7f);
            GUILayout.Label("当前行动", _sectionStyle);
            DrawStatusMeter(
                "行",
                "动作进度",
                _actionProgress,
                higherIsBetter: true,
                new Color(0.22f, 0.78f, 0.82f),
                "仅表示当前阶段的表现进度，不代表整个复合行动已经提交结果。");
            if (_remainingPathMeters > 0f)
            {
                GUILayout.Label(
                    $"剩余路程 {_remainingPathMeters:0.00} m · {_remainingPathCorners} 个路径拐点",
                    _smallStyle);
            }

            GUILayout.Space(7f);
            GUILayout.Label("生理与生活解释", _sectionStyle);
            GUILayout.Label(
                "发呆和闲逛属于休整：它们缓慢降低疲劳与压力，并略微改善心情；" +
                "娱乐满足度仍会下降，直到居民真正进行爱好、社交或使用娱乐设施。",
                _smallStyle);
            GUILayout.Label(
                $"体内待代谢水 {FormatVolume(_bodyWaterMilliliters, _bodyWaterCapacityMilliliters)} · " +
                $"膀胱内容物 {FormatVolume(_bladderWasteMilliliters, _bladderCapacityMilliliters)}",
                _smallStyle);
            GUILayout.Label(
                $"完成：饮水 {_completedDrinks} · 如厕 {_completedToiletUses} · " +
                $"休整 {_completedLeisure}（发呆 {_completedDaydreams} / 闲逛 {_completedWanders}）",
                _smallStyle);

            if (!string.IsNullOrEmpty(_lastBlocker))
            {
                GUILayout.Space(5f);
                Color previous = GUI.color;
                GUI.color = _residentPhase == FoundationResidentPhase.Blocked
                    ? new Color(1f, 0.48f, 0.4f)
                    : new Color(1f, 0.72f, 0.34f);
                GUILayout.Label($"状态提示：{_lastBlocker}", _smallStyle);
                GUI.color = previous;
            }
            else if (_entertainment < 0.35f)
            {
                GUILayout.Space(5f);
                Color previous = GUI.color;
                GUI.color = new Color(0.88f, 0.62f, 1f);
                GUILayout.Label("娱乐偏低：当前 Foundation 尚未放置真实娱乐设施。", _smallStyle);
                GUI.color = previous;
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawMiniStatus(
            string symbol,
            string label,
            float value,
            Color accent)
        {
            GUILayout.BeginVertical(GUILayout.MinWidth(80f), GUILayout.ExpandWidth(true));
            GUILayout.BeginHorizontal();
            Rect icon = GUILayoutUtility.GetRect(17f, 17f, GUILayout.Width(17f), GUILayout.Height(17f));
            DrawIconBadge(icon, symbol, accent);
            GUILayout.Label(label, _meterLabelStyle, GUILayout.Width(30f));
            GUILayout.FlexibleSpace();
            GUILayout.Label(Mathf.Clamp01(value).ToString("P0"), _meterValueStyle, GUILayout.Width(34f));
            GUILayout.EndHorizontal();
            Rect bar = GUILayoutUtility.GetRect(1f, 6f, GUILayout.ExpandWidth(true));
            DrawProgressBar(bar, value, EvaluateStateColor(value, higherIsBetter: true, accent));
            GUILayout.EndVertical();
        }

        private void DrawStatusMeter(
            string symbol,
            string label,
            float value,
            bool higherIsBetter,
            Color accent,
            string tooltip)
        {
            float normalized = Mathf.Clamp01(value);
            Color stateColor = EvaluateStateColor(normalized, higherIsBetter, accent);
            GUILayout.BeginHorizontal();
            Rect icon = GUILayoutUtility.GetRect(24f, 24f, GUILayout.Width(24f), GUILayout.Height(24f));
            DrawIconBadge(icon, symbol, stateColor);
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent(label, tooltip), _meterLabelStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label(normalized.ToString("P0"), _meterValueStyle, GUILayout.Width(42f));
            GUILayout.EndHorizontal();
            Rect bar = GUILayoutUtility.GetRect(1f, 10f, GUILayout.ExpandWidth(true));
            DrawProgressBar(bar, normalized, stateColor);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.Space(3f);
        }

        private static bool DrawToolbarButton(GUIContent content, bool active)
        {
            Color previous = GUI.backgroundColor;
            if (active) GUI.backgroundColor = new Color(0.24f, 0.78f, 0.8f);
            bool clicked = GUILayout.Button(content, GUILayout.Height(28f), GUILayout.ExpandWidth(true));
            GUI.backgroundColor = previous;
            return clicked;
        }

        private void DrawIconBadge(Rect rect, string symbol, Color color)
        {
            Color previous = GUI.color;
            GUI.color = new Color(color.r, color.g, color.b, 0.92f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(rect, symbol, _iconStyle);
            GUI.color = previous;
        }

        private static void DrawProgressBar(Rect rect, float value, Color fillColor)
        {
            Color previous = GUI.color;
            GUI.color = new Color(0.02f, 0.035f, 0.04f, 0.92f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            Rect inner = new(rect.x + 1f, rect.y + 1f, Mathf.Max(0f, rect.width - 2f), Mathf.Max(0f, rect.height - 2f));
            inner.width *= Mathf.Clamp01(value);
            GUI.color = fillColor;
            GUI.DrawTexture(inner, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static void DrawPanelBackground(Rect rect, Color background, Color border)
        {
            Color previous = GUI.color;
            GUI.color = border;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = background;
            GUI.DrawTexture(
                new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f),
                Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static Color EvaluateStateColor(float value, bool higherIsBetter, Color accent)
        {
            float health = higherIsBetter ? Mathf.Clamp01(value) : 1f - Mathf.Clamp01(value);
            Color warning = health < 0.35f
                ? new Color(0.95f, 0.3f, 0.24f)
                : new Color(0.96f, 0.66f, 0.24f);
            return Color.Lerp(warning, accent, Mathf.SmoothStep(0f, 1f, health));
        }

        private void DrawTooltip()
        {
            if (string.IsNullOrWhiteSpace(GUI.tooltip)) return;
            float width = Mathf.Min(420f, Screen.width - 28f);
            var rect = new Rect(14f, Mathf.Max(14f, Screen.height - 54f), width, 40f);
            DrawPanelBackground(
                rect,
                new Color(0.06f, 0.075f, 0.08f, 0.98f),
                new Color(0.42f, 0.62f, 0.64f, 0.95f));
            GUI.Label(
                new Rect(rect.x + 8f, rect.y + 5f, rect.width - 16f, rect.height - 10f),
                GUI.tooltip,
                _smallStyle);
        }

        private static float SafeRatio(int amount, int capacity) =>
            capacity <= 0 ? 0f : Mathf.Clamp01(amount / (float)capacity);

        private void DrawDeveloperPanel()
        {
            const float width = 390f;
            float x = Mathf.Max(14f, Screen.width - width - 14f);
            GUILayout.BeginArea(
                new Rect(x, 58f, width, Mathf.Max(120f, Screen.height - 72f)),
                GUI.skin.box);
            _developerPanelScroll = GUILayout.BeginScrollView(_developerPanelScroll);
            GUILayout.Label("游牧工坊 · 开发控制台", _titleStyle);
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
            DrawMeter("娱乐满足", _entertainment);
            DrawMeter("心情", _mood);
            DrawMeter("疲劳", _fatigue);
            DrawMeter("压力", _stress);
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
                    $"风险层：{DescribeRiskTier(_latestActionPlan.RiskTier)} · " +
                    $"层内紧迫度 {_latestActionPlan.RiskPriority:P0} · " +
                    $"选中概率 {_latestActionPlan.SelectionProbability:P1}",
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
            GUILayout.EndScrollView();
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

        private static string DescribeRiskTier(ResidentDecisionRiskTier tier) => tier switch
        {
            ResidentDecisionRiskTier.Urgent => "紧迫",
            ResidentDecisionRiskTier.Severe => "严重",
            ResidentDecisionRiskTier.Critical => "危及生命 / 车辆",
            _ => "日常",
        };

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
            _compactNameStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(3, 0, 0, 0),
                normal = { textColor = new Color(0.94f, 0.96f, 0.94f) },
            };
            _compactTaskStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                clipping = TextClipping.Clip,
                padding = new RectOffset(3, 0, 0, 0),
                normal = { textColor = new Color(0.68f, 0.74f, 0.74f) },
            };
            _meterLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.84f, 0.88f, 0.86f) },
            };
            _meterValueStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color(0.8f, 0.84f, 0.82f) },
            };
            _iconStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white },
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
            FoundationResidentPhase.Relaxing => "自主休整",
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
