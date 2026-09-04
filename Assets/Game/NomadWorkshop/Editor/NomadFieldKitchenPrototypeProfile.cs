using System;
using UnityEngine;

namespace Game.NomadWorkshop.Editor
{
    /// <summary>
    /// 野战厨房参数化灰盒的作者输入。它只描述玩法尺度和少量可读性参数，
    /// 不尝试成为任意道具都能表达的通用建模语言。
    /// </summary>
    [CreateAssetMenu(
        fileName = "NW_FieldKitchen_Prototype_01_Profile",
        menuName = "SSFramework/游牧工坊/参数化野战厨房配置")]
    public sealed class NomadFieldKitchenPrototypeProfile : ScriptableObject
    {
        [Header("玩法尺寸（米）")]
        [SerializeField, Min(0.5f)] private float width = 2.3f;
        [SerializeField, Min(0.5f)] private float height = 2f;
        [SerializeField, Min(0.3f)] private float depth = 0.88f;
        [SerializeField, Min(0.3f)] private float counterHeight = 1.02f;
        [SerializeField, Min(0.02f)] private float counterThickness = 0.08f;
        [SerializeField, Min(0.2f)] private float leftTowerWidth = 0.64f;
        [SerializeField, Min(0.02f)] private float frameThickness = 0.055f;
        [SerializeField, Min(0.01f)] private float panelThickness = 0.04f;
        [SerializeField, Min(0.001f)] private float doorGap = 0.018f;
        [SerializeField, Min(0.03f)] private float footHeight = 0.12f;
        [SerializeField, Min(0f)] private float bevelWidth = 0.012f;

        [Header("原型材质颜色")]
        [SerializeField] private Color wornTeal = new(0.075f, 0.29f, 0.31f, 1f);
        [SerializeField] private Color darkMetal = new(0.055f, 0.065f, 0.072f, 1f);
        [SerializeField] private Color safetyOrange = new(0.86f, 0.255f, 0.055f, 1f);
        [SerializeField] private Color stainlessSteel = new(0.49f, 0.53f, 0.55f, 1f);
        [SerializeField] private Color rubber = new(0.025f, 0.028f, 0.03f, 1f);

        public float Width => width;
        public float Height => height;
        public float Depth => depth;
        public float CounterHeight => counterHeight;
        public float CounterThickness => counterThickness;
        public float LeftTowerWidth => leftTowerWidth;
        public float FrameThickness => frameThickness;
        public float PanelThickness => panelThickness;
        public float DoorGap => doorGap;
        public float FootHeight => footHeight;
        public float BevelWidth => bevelWidth;
        public Color WornTeal => wornTeal;
        public Color DarkMetal => darkMetal;
        public Color SafetyOrange => safetyOrange;
        public Color StainlessSteel => stainlessSteel;
        public Color Rubber => rubber;

        /// <summary>在任何资产写入前拒绝不可能形成封闭设施层级的参数组合。</summary>
        public void ValidateOrThrow()
        {
            RequireFinitePositive(width, nameof(width));
            RequireFinitePositive(height, nameof(height));
            RequireFinitePositive(depth, nameof(depth));
            RequireFinitePositive(counterHeight, nameof(counterHeight));
            RequireFinitePositive(counterThickness, nameof(counterThickness));
            RequireFinitePositive(leftTowerWidth, nameof(leftTowerWidth));
            RequireFinitePositive(frameThickness, nameof(frameThickness));
            RequireFinitePositive(panelThickness, nameof(panelThickness));
            RequireFinitePositive(doorGap, nameof(doorGap));
            RequireFinitePositive(footHeight, nameof(footHeight));
            if (!IsFinite(bevelWidth) || bevelWidth < 0f)
                throw new InvalidOperationException("bevelWidth 必须是有限的非负数。");

            if (counterHeight <= footHeight + panelThickness * 4f)
                throw new InvalidOperationException("操作台高度不足以容纳下柜、底脚和板材厚度。");
            if (height <= counterHeight + counterThickness * 2f)
                throw new InvalidOperationException("总高度必须明显高于操作台，才能容纳左侧静态控制区。");
            if (leftTowerWidth <= frameThickness * 4f ||
                leftTowerWidth >= width - frameThickness * 6f)
                throw new InvalidOperationException("左侧塔柜宽度会挤压门板、立柱或右侧工作区。");
            if (panelThickness >= Mathf.Min(width, depth) * 0.25f)
                throw new InvalidOperationException("板材厚度相对设施尺寸过大。");
            if (doorGap * 6f >= width - leftTowerWidth)
                throw new InvalidOperationException("门缝会吃掉中央和右侧门板的有效宽度。");
            if (bevelWidth >= Mathf.Min(frameThickness, panelThickness) * 0.45f)
                throw new InvalidOperationException("倒角宽度必须小于最薄构件厚度的 45%。");
        }

        private static void RequireFinitePositive(float value, string fieldName)
        {
            if (!IsFinite(value) || value <= 0f)
                throw new InvalidOperationException($"{fieldName} 必须是有限正数。");
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
