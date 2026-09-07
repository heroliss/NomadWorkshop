using UnityEngine;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>右上角互斥信息面板；建造模式本身仍是独立的玩家交互状态。</summary>
    public enum FoundationHudPanel
    {
        None,
        Resident,
        Build,
        Developer,
        Journey,
    }

    /// <summary>
    /// Foundation 首版 HUD 的唯一布局真值。IMGUI 绘制与 3D 输入拦截共用这些矩形，
    /// 避免窗口尺寸变化后，透明的旧固定区域继续吞掉建造和镜头输入。
    /// </summary>
    public static class FoundationHudLayout
    {
        public const float Margin = 14f;
        public const float CompactResidentCardHeight = 132f;
        public const float ToolbarHeight = 36f;
        public const float InformationPanelTop = 140f;
        public const float MinimumCanvasWidth = 960f;
        public const float MinimumCanvasHeight = 540f;

        /// <summary>绘制矩阵与屏幕拾取共享同一缩放；高分辨率保持正常字号，小窗口等比缩小。</summary>
        public static float GetCanvasScale(float screenWidth, float screenHeight) =>
            screenWidth <= 0f || screenHeight <= 0f ? 1f :
                Mathf.Min(1f, screenWidth / MinimumCanvasWidth, screenHeight / MinimumCanvasHeight);

        public static Vector2 GetCanvasSize(float screenWidth, float screenHeight) =>
            new Vector2(screenWidth, screenHeight) / GetCanvasScale(screenWidth, screenHeight);

        public static FoundationHudPanel Toggle(
            FoundationHudPanel current,
            FoundationHudPanel requested) =>
            current == requested ? FoundationHudPanel.None : requested;

        /// <summary>
        /// 判断面板切换是否必须退出建造模式。建造模式会驱动世界中的网格、幽灵和停靠点，
        /// 因此不能只关闭面板而把领域交互状态遗留在后台。
        /// </summary>
        public static bool ShouldExitBuildMode(
            FoundationInteractionMode interactionMode,
            FoundationHudPanel nextPanel) =>
            interactionMode == FoundationInteractionMode.Build &&
            nextPanel != FoundationHudPanel.Build;

        /// <summary>打开建造面板时，确保领域交互状态也进入建造模式。</summary>
        public static bool ShouldEnterBuildMode(
            FoundationInteractionMode interactionMode,
            FoundationHudPanel nextPanel) =>
            interactionMode != FoundationInteractionMode.Build &&
            nextPanel == FoundationHudPanel.Build;

        public static Rect GetCompactResidentCardRect(float screenWidth)
        {
            float availableWidth = Mathf.Max(0f, screenWidth - Margin * 2f);
            float width = Mathf.Min(352f, availableWidth);
            return new Rect(Margin, Margin, width, CompactResidentCardHeight);
        }

        public static Rect GetCornerToolbarRect(float screenWidth)
        {
            float availableWidth = Mathf.Max(0f, screenWidth - Margin * 2f);
            float width = Mathf.Min(310f, availableWidth);
            return new Rect(
                Mathf.Max(Margin, screenWidth - width - Margin),
                Margin,
                width,
                ToolbarHeight);
        }

        public static Rect GetInformationPanelRect(float screenWidth, float screenHeight)
        {
            float availableWidth = Mathf.Max(0f, screenWidth - Margin * 2f);
            float width = Mathf.Min(390f, availableWidth);
            return new Rect(
                Mathf.Max(Margin, screenWidth - width - Margin),
                InformationPanelTop,
                width,
                Mathf.Max(0f, screenHeight - InformationPanelTop - 96f));
        }

        public static Rect GetSupplyRect(float canvasWidth) =>
            new(Mathf.Max(Margin, canvasWidth - 390f - Margin), 58f, Mathf.Min(390f, Mathf.Max(0f, canvasWidth - Margin * 2f)), 74f);

        public static Rect GetTimeControlsRect(float canvasWidth, float canvasHeight) =>
            new(Mathf.Max(Margin, canvasWidth - 320f - Margin), Mathf.Max(0f, canvasHeight - 82f),
                Mathf.Min(320f, Mathf.Max(0f, canvasWidth - Margin * 2f)), 68f);

        /// <summary>可选屋顶切换控件固定左下；小窗口仍将可点击矩形限制在屏幕内。</summary>
        public static Rect GetRoofControlRect(float screenWidth, float screenHeight)
        {
            float left = Mathf.Min(Margin, Mathf.Max(0f, screenWidth * .5f));
            float bottom = Mathf.Min(Margin, Mathf.Max(0f, screenHeight * .5f));
            float width = Mathf.Min(210f, Mathf.Max(0f, screenWidth - left * 2f));
            float height = Mathf.Min(ToolbarHeight, Mathf.Max(0f, screenHeight - bottom * 2f));
            return new Rect(left, screenHeight - bottom - height, width, height);
        }

        /// <summary>
        /// Input System 的屏幕坐标从左下起算，IMGUI 从左上起算；转换后只检查真实可见 UI。
        /// </summary>
        public static bool IsScreenPointBlocked(
            Vector2 screenPoint,
            float screenWidth,
            float screenHeight,
            bool informationPanelVisible,
            bool roofControlsVisible = false)
        {
            float scale = GetCanvasScale(screenWidth, screenHeight);
            Vector2 size = GetCanvasSize(screenWidth, screenHeight);
            var guiPoint = new Vector2(screenPoint.x, screenHeight - screenPoint.y) / scale;
            if (GetCompactResidentCardRect(size.x).Contains(guiPoint) ||
                GetCornerToolbarRect(size.x).Contains(guiPoint) ||
                GetSupplyRect(size.x).Contains(guiPoint) ||
                GetTimeControlsRect(size.x, size.y).Contains(guiPoint))
                return true;

            if (roofControlsVisible && GetRoofControlRect(size.x, size.y).Contains(guiPoint))
                return true;

            return informationPanelVisible &&
                   GetInformationPanelRect(size.x, size.y).Contains(guiPoint);
        }
    }
}
