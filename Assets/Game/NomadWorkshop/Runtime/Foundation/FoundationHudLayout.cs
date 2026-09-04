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
    }

    /// <summary>
    /// Foundation 首版 HUD 的唯一布局真值。IMGUI 绘制与 3D 输入拦截共用这些矩形，
    /// 避免窗口尺寸变化后，透明的旧固定区域继续吞掉建造和镜头输入。
    /// </summary>
    public static class FoundationHudLayout
    {
        public const float Margin = 14f;
        public const float CompactResidentCardHeight = 112f;
        public const float ToolbarHeight = 36f;
        public const float InformationPanelTop = 58f;

        public static FoundationHudPanel Toggle(
            FoundationHudPanel current,
            FoundationHudPanel requested) =>
            current == requested ? FoundationHudPanel.None : requested;

        public static Rect GetCompactResidentCardRect(float screenWidth)
        {
            float availableWidth = Mathf.Max(0f, screenWidth - Margin * 2f);
            float width = Mathf.Min(370f, availableWidth);
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
                Mathf.Max(120f, screenHeight - InformationPanelTop - Margin));
        }

        /// <summary>
        /// Input System 的屏幕坐标从左下起算，IMGUI 从左上起算；转换后只检查真实可见 UI。
        /// </summary>
        public static bool IsScreenPointBlocked(
            Vector2 screenPoint,
            float screenWidth,
            float screenHeight,
            bool informationPanelVisible)
        {
            var guiPoint = new Vector2(screenPoint.x, screenHeight - screenPoint.y);
            if (GetCompactResidentCardRect(screenWidth).Contains(guiPoint) ||
                GetCornerToolbarRect(screenWidth).Contains(guiPoint))
                return true;

            return informationPanelVisible &&
                   GetInformationPanelRect(screenWidth, screenHeight).Contains(guiPoint);
        }
    }
}
