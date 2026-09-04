using System;
using System.Collections.Generic;
using Game.NomadWorkshop.Simulation;
using UnityEngine;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>灰盒阶段的物品表现模板；正式 Prefab 接入后不改变放置与存档契约。</summary>
    public enum NomadWorldItemPrototypeStyle
    {
        Box,
        Cylinder,
        Cup,
        WaterCan,
    }

    /// <summary>
    /// 可搬动物品的稳定定义。二维占地属于纯模拟层，颜色与灰盒样式只帮助早期观察；
    /// 物品实例、所在区域和局部姿态由运行时账本与存档拥有。
    /// </summary>
    [CreateAssetMenu(
        fileName = "NW_WorldItem",
        menuName = "SSFramework/游牧工坊/世界物品定义")]
    public sealed class NomadWorldItemDefinition : ScriptableObject
    {
        [Header("稳定身份")]
        [SerializeField, Tooltip("跨场景与存档稳定的物品定义 id；发布存档后不要随意改名。")]
        private string id = string.Empty;
        [SerializeField, Tooltip("面向玩家与开发诊断显示的中文名称。")]
        private string displayName = string.Empty;
        [SerializeField, Tooltip("PlacementRegion 用来筛选兼容表面的类别 id，例如 cup 或 water-can。")]
        private string categoryId = string.Empty;

        [Header("放置占地")]
        [SerializeField, Tooltip("物品底部沿自身 X/Z 的完整尺寸（米）。首版用矩形近似圆形杯底。")]
        private Vector2 footprintSizeMeters = new(0.1f, 0.1f);
        [SerializeField, Min(0.01f), Tooltip("从支撑面到物品最高点的高度（米）。")]
        private float heightMeters = 0.1f;
        [SerializeField, Min(0f), Tooltip("占地外额外保留的安全边距（米），用于避免视觉贴靠或轻微穿模。")]
        private float safetyMarginMeters = 0.01f;
        [SerializeField, Tooltip("几何是否允许任意局部角度。圆杯等旋转对称物体可开启；稳定自动选位仍从下方候选角度开始。")]
        private bool allowAnyYaw;
        [SerializeField, Tooltip("自动稳定选位依次尝试的局部角度（度）；精确恢复还会受“允许任意角度”控制。")]
        private float[] stableYawDegrees = { 0f };

        [Header("可替换灰盒表现")]
        [SerializeField, Tooltip("无正式 Prefab 时使用的灰盒造型。")]
        private NomadWorldItemPrototypeStyle prototypeStyle;
        [SerializeField, Tooltip("灰盒主色；正式材质不会从这里反向写入玩法定义。")]
        private Color prototypeColor = Color.white;

        public string Id => id;
        public string DisplayName => displayName;
        public string CategoryId => categoryId;
        public Vector2 FootprintSizeMeters => footprintSizeMeters;
        public float HeightMeters => heightMeters;
        public float SafetyMarginMeters => safetyMarginMeters;
        public bool AllowAnyYaw => allowAnyYaw;
        public IReadOnlyList<float> StableYawDegrees => stableYawDegrees;
        public NomadWorldItemPrototypeStyle PrototypeStyle => prototypeStyle;
        public Color PrototypeColor => prototypeColor;

        public PlacementFootprint CreateFootprint()
        {
            ValidateOrThrow();
            var yawDeciDegrees = new int[stableYawDegrees.Length];
            for (var i = 0; i < stableYawDegrees.Length; i++)
                yawDeciDegrees[i] = Mathf.RoundToInt(stableYawDegrees[i] * 10f);
            return new PlacementFootprint(
                id,
                categoryId,
                Mathf.RoundToInt(footprintSizeMeters.x * 1000f),
                Mathf.RoundToInt(footprintSizeMeters.y * 1000f),
                Mathf.RoundToInt(heightMeters * 1000f),
                Mathf.RoundToInt(safetyMarginMeters * 1000f),
                yawDeciDegrees,
                allowAnyYaw);
        }

        public void ValidateOrThrow()
        {
            Sanitize();
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidOperationException($"世界物品定义 '{name}' 缺少稳定 id。");
            if (string.IsNullOrWhiteSpace(displayName))
                throw new InvalidOperationException($"世界物品定义 '{name}' 缺少显示名称。");
            if (string.IsNullOrWhiteSpace(categoryId))
                throw new InvalidOperationException($"世界物品定义 '{name}' 缺少放置类别。");
            if (!Enum.IsDefined(typeof(NomadWorldItemPrototypeStyle), prototypeStyle))
                throw new InvalidOperationException($"世界物品定义 '{name}' 的灰盒样式无效。");
            if (stableYawDegrees == null || stableYawDegrees.Length == 0)
                throw new InvalidOperationException($"世界物品定义 '{name}' 至少需要一个稳定选位角度。");
        }

        private void OnValidate() => Sanitize();

        private void Sanitize()
        {
            id = id?.Trim() ?? string.Empty;
            displayName = displayName?.Trim() ?? string.Empty;
            categoryId = categoryId?.Trim() ?? string.Empty;
            footprintSizeMeters.x = float.IsFinite(footprintSizeMeters.x)
                ? Mathf.Max(0.01f, footprintSizeMeters.x)
                : 0.1f;
            footprintSizeMeters.y = float.IsFinite(footprintSizeMeters.y)
                ? Mathf.Max(0.01f, footprintSizeMeters.y)
                : 0.1f;
            heightMeters = float.IsFinite(heightMeters) ? Mathf.Max(0.01f, heightMeters) : 0.1f;
            safetyMarginMeters = float.IsFinite(safetyMarginMeters)
                ? Mathf.Max(0f, safetyMarginMeters)
                : 0f;
            stableYawDegrees ??= new[] { 0f };
            if (stableYawDegrees.Length == 0) stableYawDegrees = new[] { 0f };
            for (var i = 0; i < stableYawDegrees.Length; i++)
            {
                stableYawDegrees[i] = float.IsFinite(stableYawDegrees[i])
                    ? Mathf.Repeat(stableYawDegrees[i], 360f)
                    : 0f;
            }
        }

#if UNITY_EDITOR
        /// <summary>Editor Pipeline 与隔离测试共用的幂等配置入口。</summary>
        public void ConfigureForEditor(
            string configuredId,
            string configuredDisplayName,
            string configuredCategoryId,
            Vector2 configuredFootprintSizeMeters,
            float configuredHeightMeters,
            float configuredSafetyMarginMeters,
            bool configuredAllowAnyYaw,
            float[] configuredStableYawDegrees,
            NomadWorldItemPrototypeStyle configuredPrototypeStyle,
            Color configuredPrototypeColor)
        {
            id = configuredId;
            displayName = configuredDisplayName;
            categoryId = configuredCategoryId;
            footprintSizeMeters = configuredFootprintSizeMeters;
            heightMeters = configuredHeightMeters;
            safetyMarginMeters = configuredSafetyMarginMeters;
            allowAnyYaw = configuredAllowAnyYaw;
            stableYawDegrees = configuredStableYawDegrees != null
                ? (float[])configuredStableYawDegrees.Clone()
                : Array.Empty<float>();
            prototypeStyle = configuredPrototypeStyle;
            prototypeColor = configuredPrototypeColor;
            ValidateOrThrow();
            CreateFootprint();
        }

        public void ConfigureForTests(
            string configuredId,
            string configuredDisplayName,
            string configuredCategoryId,
            Vector2 configuredFootprintSizeMeters,
            float configuredHeightMeters,
            float configuredSafetyMarginMeters,
            bool configuredAllowAnyYaw,
            float[] configuredStableYawDegrees,
            NomadWorldItemPrototypeStyle configuredPrototypeStyle,
            Color configuredPrototypeColor) =>
            ConfigureForEditor(
                configuredId,
                configuredDisplayName,
                configuredCategoryId,
                configuredFootprintSizeMeters,
                configuredHeightMeters,
                configuredSafetyMarginMeters,
                configuredAllowAnyYaw,
                configuredStableYawDegrees,
                configuredPrototypeStyle,
                configuredPrototypeColor);
#endif
    }
}
