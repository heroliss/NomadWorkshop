using Game.Framework.Common;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.NomadWorkshop.Foundation
{
    // 普通玩家常驻 HUD 与纯观看会话；详细面板和既有读模型接线留在主文件。
    public sealed partial class NomadFoundationDebugView
    {
        private FoundationHudTheme _hudTheme;
        private GUIStyle _resourceValueStyle;
        private GUIStyle _resourceLabelStyle;
        private static readonly float[] PlaybackSpeeds = { .5f, 1f, 4f };
        private float CanvasWidth => FoundationHudLayout.GetCanvasSize(Screen.width, Screen.height).x;
        private float CanvasHeight => FoundationHudLayout.GetCanvasSize(Screen.width, Screen.height).y;
        private bool TimeControlsAvailable => _ready && !_checkpointBusy;

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame && GUIUtility.keyboardControl == 0)
                TogglePause();
        }

        private bool TogglePause()
        {
            if (!TimeControlsAvailable) return false;
            this.ExecuteCommand(new SetFoundationPausedCommand(!_paused));
            return true;
        }

        private bool SetPlaybackSpeed(float speed)
        {
            if (!TimeControlsAvailable) return false;
            this.ExecuteCommand(new SetFoundationSpeedCommand(speed));
            return true;
        }

        private void DrawPlayerResidentCard()
        {
            Rect outer = FoundationHudLayout.GetCompactResidentCardRect(CanvasWidth);
            GUI.Box(outer, GUIContent.none);
            GUI.Label(new Rect(outer.x + 12f, outer.y + 7f, 180f, 21f), "同行居民", _sectionStyle);
            GUI.Label(new Rect(outer.xMax - 65f, outer.y + 9f, 52f, 18f), $"{_residents?.Count ?? 0} 人", _meterValueStyle);
            int count = _residents?.Count ?? 0;
            float width = (outer.width - 24f - Mathf.Max(0, count - 1) * 4f) / Mathf.Max(1, count);
            for (int i = 0; i < count; i++)
            {
                var tab = new Rect(outer.x + 12f + i * (width + 4f), outer.y + 32f, width, 27f);
                if (GUI.Button(tab, $"居民 {i + 1:00}", i == _selectedResidentIndex ? _hudTheme.SelectedButton : GUI.skin.button))
                    SelectResident(i);
            }
            GUI.Label(new Rect(outer.x + 12f, outer.y + 63f, outer.width - 24f, 21f),
                new GUIContent(Describe(_residentPhase), _currentTask), _compactNameStyle);
            var status = new Rect(outer.x + 12f, outer.y + 87f, outer.width - 20f, 26f);
            DrawMiniStatus(status, 0, "水", "水分", 1f - _thirst, new Color(.31f, .65f, .76f));
            DrawMiniStatus(status, 1, "健", "健康", _health, new Color(.54f, .70f, .46f));
            DrawMiniStatus(status, 2, "能", "精力", 1f - _fatigue, new Color(.64f, .72f, .48f));
            DrawMiniStatus(status, 3, "心", "心情", _mood, FoundationHudTheme.Brass);
            DrawProgressBar(new Rect(outer.x + 12f, outer.y + 120f, outer.width - 24f, 4f), _actionProgress, FoundationHudTheme.Teal);
        }

        private void DrawSupplies()
        {
            Rect outer = FoundationHudLayout.GetSupplyRect(CanvasWidth);
            GUI.Box(outer, GUIContent.none);
            GUI.Label(new Rect(outer.x + 12f, outer.y + 7f, outer.width - 24f, 20f), JourneySummary(), _smallStyle);
            float column = (outer.width - 24f) / 3f;
            SupplyCell(new Rect(outer.x + 12f, outer.y + 28f, column, 36f), "储水",
                $"{_vehicleWaterMilliliters / 1000f:0.#} L", $"车辆水箱：{FormatVolume(_vehicleWaterMilliliters, _vehicleWaterCapacityMilliliters)}");
            SupplyCell(new Rect(outer.x + 12f + column, outer.y + 28f, column, 36f), "饮水站",
                $"{_stationWaterMilliliters / 1000f:0.#} L", $"已送达饮水站：{FormatVolume(_stationWaterMilliliters, _stationWaterCapacityMilliliters)}；水箱中的水仍需居民搬运。");
            SupplyCell(new Rect(outer.x + 12f + column * 2f, outer.y + 28f, column, 36f), "燃料",
                $"{_journeyFuelPicoliters / 1_000_000_000_000d:0.00} L", "仅在实际行驶时消耗燃料；打开旅程选择目的地。");
        }

        private void SupplyCell(Rect rect, string label, string value, string tooltip)
        {
            _resourceValueStyle ??= new GUIStyle(_compactNameStyle) { fontSize = 15, padding = new RectOffset() };
            _resourceLabelStyle ??= new GUIStyle(_smallStyle)
            {
                fontSize = 11, padding = new RectOffset(), alignment = TextAnchor.MiddleLeft, wordWrap = false,
            };
            GUI.Label(new Rect(rect.x, rect.y, rect.width, 18f), new GUIContent(label, tooltip), _resourceLabelStyle);
            GUI.Label(new Rect(rect.x, rect.y + 17f, rect.width, 21f), new GUIContent(value, tooltip), _resourceValueStyle);
        }

        private string JourneySummary() => _journeyStatus switch
        {
            Simulation.NomadJourneyStatus.Moving => "行驶中 · 前往" + DestinationName,
            Simulation.NomadJourneyStatus.AwaitingDriver => "准备出发 · 等待驾驶员",
            Simulation.NomadJourneyStatus.Arrived => "已抵达 · " + DestinationName,
            Simulation.NomadJourneyStatus.FuelExhausted => "燃料耗尽 · 已停车",
            _ => "停车休整 · 在旅程面板选择目的地",
        };

        private string DestinationName => !string.IsNullOrEmpty(_journeyDestinationAnchorId)
            ? $"路线锚点 · {_journeyDestinationAnchorId}"
            : _journeyDestination == Simulation.NomadJourneyEndpoint.Destination ? "干河驿站" :
            _journeyDestination == Simulation.NomadJourneyEndpoint.Origin ? "旧营地" : "未指定";

        private void DrawTimeControls()
        {
            Rect outer = FoundationHudLayout.GetTimeControlsRect(CanvasWidth, CanvasHeight);
            GUI.Box(outer, GUIContent.none);
            string weather = _currentWeather == Simulation.NomadWeatherKind.Sandstorm ? "沙尘" : "晴朗";
            GUI.Label(new Rect(outer.x + 10f, outer.y + 7f, 218f, 18f),
                $"第 {_lifeDay} 天  {_lifeMinuteOfDay / 60:00}:{_lifeMinuteOfDay % 60:00} · {weather}", _smallStyle);
            GUI.Label(new Rect(outer.xMax - 88f, outer.y + 7f, 76f, 18f),
                _checkpointBusy ? "存读档中…" : _paused ? "已暂停" : $"{_speed:0.##}× 运行", _meterValueStyle);
            bool previous = GUI.enabled;
            GUI.enabled = previous && TimeControlsAvailable;
            float y = outer.y + 31f;
            if (GUI.Button(new Rect(outer.x + 10f, y, 98f, 27f), new GUIContent(_paused ? "继续 [空格]" : "暂停 [空格]", _checkpointBusy ? _checkpointFeedback : "暂停或继续模拟，观察和镜头操作仍可使用。")))
                TogglePause();
            for (int i = 0; i < PlaybackSpeeds.Length; i++)
                if (GUI.Button(new Rect(outer.x + 113f + i * 65f, y, 61f, 27f), $"{PlaybackSpeeds[i]:0.#}×",
                        Mathf.Approximately(_speed, PlaybackSpeeds[i]) ? _hudTheme.SelectedButton : GUI.skin.button))
                    SetPlaybackSpeed(PlaybackSpeeds[i]);
            GUI.enabled = previous;
        }

#if UNITY_EDITOR
        /// <summary>与可见时间按钮共用入口，验证忙碌/未就绪时不会发出模拟操作。</summary>
        public bool TogglePauseForTests() => TogglePause();
        public bool SetPlaybackSpeedForTests(float speed) => SetPlaybackSpeed(speed);
        public void SelectResidentForTests(int index) => SelectResident(index);
        public int SelectedResidentIndexForTests => _selectedResidentIndex;
#endif
    }
}
