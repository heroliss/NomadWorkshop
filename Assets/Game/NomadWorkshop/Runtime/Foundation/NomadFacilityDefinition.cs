using System;
using System.Collections.Generic;
using Game.NomadWorkshop.Simulation;
using UnityEngine;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>Foundation 切片当前真正需要区分的设施能力；它不是最终生产配方系统。</summary>
    public enum NomadFacilityFunction
    {
        VehicleWaterTank,
        DrinkingStation,
        FieldKitchen,
        Toilet,
        Storage,
        HobbyPoint,
    }

    /// <summary>一个可由 Inspector 编辑、运行时转换为纯模拟有向矩形的占地部件。</summary>
    [Serializable]
    public struct NomadFacilityFootprintPartDefinition
    {
        [SerializeField, Tooltip("该占地部件中心相对设施枢轴的 X/Z 偏移（米）。")]
        private Vector2 localCenterMeters;
        [SerializeField, Tooltip("该占地部件沿自身 X/Z 的完整尺寸（米），不是半径。")]
        private Vector2 sizeMeters;
        [SerializeField, Tooltip("该占地部件相对设施枢轴的局部旋转角（度）。")]
        private float localYawDegrees;

        public NomadFacilityFootprintPartDefinition(
            Vector2 localCenterMeters,
            Vector2 sizeMeters,
            float localYawDegrees = 0f)
        {
            this.localCenterMeters = localCenterMeters;
            this.sizeMeters = sizeMeters;
            this.localYawDegrees = localYawDegrees;
            Sanitize();
        }

        public Vector2 LocalCenterMeters => localCenterMeters;
        public Vector2 SizeMeters => sizeMeters;
        public float LocalYawDegrees => localYawDegrees;

        public DeckFootprintPart CreatePart()
        {
            DeckPose localPose = DeckPose.FromMeters(
                localCenterMeters.x,
                localCenterMeters.y,
                localYawDegrees);
            DeckPose size = DeckPose.FromMeters(sizeMeters.x, sizeMeters.y, 0d);
            return new DeckFootprintPart(
                localPose.XMillimeters,
                localPose.ZMillimeters,
                size.XMillimeters,
                size.ZMillimeters,
                localPose.YawDeciDegrees);
        }

        internal void Sanitize()
        {
            if (!float.IsFinite(localCenterMeters.x)) localCenterMeters.x = 0f;
            if (!float.IsFinite(localCenterMeters.y)) localCenterMeters.y = 0f;
            if (!float.IsFinite(localYawDegrees)) localYawDegrees = 0f;
            sizeMeters.x = float.IsFinite(sizeMeters.x) ? Mathf.Max(0.05f, sizeMeters.x) : 0.05f;
            sizeMeters.y = float.IsFinite(sizeMeters.y) ? Mathf.Max(0.05f, sizeMeters.y) : 0.05f;
        }
    }

    /// <summary>一个 InteractionGroup 中的候选贴靠姿势；位置和朝向均相对设施枢轴。</summary>
    [Serializable]
    public struct NomadFacilityInteractionSlotDefinition
    {
        [SerializeField, Tooltip("同一交互组内稳定且唯一的候选位 id；存档和诊断会使用它。")]
        private string slotId;
        [SerializeField, Tooltip("居民最终贴靠位置，相对设施枢轴的 X/Z 偏移（米）。")]
        private Vector2 localPositionMeters;
        [SerializeField, Tooltip("居民完成贴靠后的面向角，相对设施枢轴（度）；用于对齐交互动画。")]
        private float localYawDegrees;

        public NomadFacilityInteractionSlotDefinition(
            string slotId,
            Vector2 localPositionMeters,
            float localYawDegrees = 0f)
        {
            this.slotId = slotId;
            this.localPositionMeters = localPositionMeters;
            this.localYawDegrees = localYawDegrees;
            Sanitize();
        }

        public string SlotId => slotId;
        public Vector2 LocalPositionMeters => localPositionMeters;
        public float LocalYawDegrees => localYawDegrees;

        public DeckPose Resolve(in DeckPose facilityPose)
        {
            DeckPose local = DeckPose.FromMeters(
                localPositionMeters.x,
                localPositionMeters.y,
                localYawDegrees);
            return facilityPose.TransformLocal(
                local.XMillimeters,
                local.ZMillimeters,
                local.YawDeciDegrees);
        }

        internal void Sanitize()
        {
            slotId = slotId?.Trim() ?? string.Empty;
            if (!float.IsFinite(localPositionMeters.x)) localPositionMeters.x = 0f;
            if (!float.IsFinite(localPositionMeters.y)) localPositionMeters.y = 0f;
            if (!float.IsFinite(localYawDegrees)) localYawDegrees = 0f;
        }
    }

    /// <summary>
    /// 一个具有真实容量语义的设施交互组。组内 Slot 是备选站姿；多个并行工位应拆成多个 Group。
    /// </summary>
    [Serializable]
    public sealed class NomadFacilityInteractionGroupDefinition
    {
        [SerializeField, Tooltip("设施内一个功能点的稳定 id，例如 faucet 或 cabinet。")]
        private string groupId = "interaction";
        [SerializeField, Tooltip("启用时，该功能点的全部候选停靠位都不可达会令设施显示不可用。")]
        private bool requiredForOperation = true;
        [SerializeField, Tooltip("共享同一份容量的备选站姿。需要两人并行使用时应创建两个交互组，而不是仅增加候选位。")]
        private NomadFacilityInteractionSlotDefinition[] alternativeSlots =
            Array.Empty<NomadFacilityInteractionSlotDefinition>();

        public NomadFacilityInteractionGroupDefinition(
            string groupId,
            bool requiredForOperation,
            NomadFacilityInteractionSlotDefinition[] alternativeSlots)
        {
            this.groupId = groupId;
            this.requiredForOperation = requiredForOperation;
            this.alternativeSlots = alternativeSlots != null
                ? (NomadFacilityInteractionSlotDefinition[])alternativeSlots.Clone()
                : throw new ArgumentNullException(nameof(alternativeSlots));
            ValidateOrThrow("瞬态设施定义");
        }

        public string GroupId => groupId;
        public bool RequiredForOperation => requiredForOperation;
        public IReadOnlyList<NomadFacilityInteractionSlotDefinition> AlternativeSlots =>
            alternativeSlots;

        internal void ValidateOrThrow(string ownerName)
        {
            groupId = groupId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(groupId))
                throw new InvalidOperationException($"设施 '{ownerName}' 存在空 InteractionGroup id。");
            if (alternativeSlots == null || alternativeSlots.Length == 0)
                throw new InvalidOperationException(
                    $"设施 '{ownerName}' 的 InteractionGroup '{groupId}' 没有候选位。");

            var slotIds = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < alternativeSlots.Length; i++)
            {
                NomadFacilityInteractionSlotDefinition slot = alternativeSlots[i];
                slot.Sanitize();
                alternativeSlots[i] = slot;
                if (string.IsNullOrWhiteSpace(slot.SlotId))
                    throw new InvalidOperationException(
                        $"设施 '{ownerName}' 的 InteractionGroup '{groupId}' 第 {i} 个 Slot id 为空。");
                if (!slotIds.Add(slot.SlotId))
                    throw new InvalidOperationException(
                        $"设施 '{ownerName}' 的 InteractionGroup '{groupId}' 出现重复 Slot '{slot.SlotId}'。");
            }
        }
    }

    /// <summary>
    /// 设施的数据驱动定义。连续占地、交互组和初始姿态属于玩法；Prefab 与灰盒尺寸属于表现替换点。
    /// </summary>
    [CreateAssetMenu(
        fileName = "NW_Facility",
        menuName = "SSFramework/游牧工坊/设施定义")]
    public sealed class NomadFacilityDefinition : ScriptableObject
    {
        [Header("身份与能力")]
        [SerializeField, Tooltip("跨运行与存档稳定的设施定义 id；发布后不要随意改名。")]
        private string id = "facility";
        [SerializeField, Tooltip("面向玩家和调试面板显示的中文名称。")]
        private string displayName = "设施";
        [SerializeField, Tooltip("当前垂直切片识别的设施能力；后续生产配方仍会使用独立数据。")]
        private NomadFacilityFunction function;
        [SerializeField, Tooltip("是否出现在玩家建造列表中；关闭后仍可作为初始设施生成。")]
        private bool buildable = true;

        [Header("连续占地（相对设施枢轴）")]
        [SerializeField, Tooltip("用于连续碰撞、甲板越界和 NavMesh 障碍的有向矩形部件；应贴合实体底座。")]
        private NomadFacilityFootprintPartDefinition[] footprintParts =
        {
            new(Vector2.zero, Vector2.one),
        };

        [Header("交互组与候选位")]
        [SerializeField, Tooltip("设施拥有的功能点；每组可有多个备选停靠位，但组本身默认只容纳一个使用者。")]
        private NomadFacilityInteractionGroupDefinition[] interactionGroups =
            Array.Empty<NomadFacilityInteractionGroupDefinition>();

        [Header("初始设施")]
        [SerializeField, Tooltip("是否在复位/新游戏时自动放置。")]
        private bool placeAtStart;
        [SerializeField, Tooltip("初始设施在甲板局部坐标中的 X/Z 位置（米）。")]
        private Vector2 startPositionMeters;
        [SerializeField, Tooltip("初始设施绕甲板 Y 轴的朝向（度）。")]
        private float startYawDegrees;

        [Header("可替换的灰盒表现")]
        [SerializeField, Tooltip("可替换的正式或灰盒 Prefab；为空时由 View 生成基础原型。")]
        private GameObject prefab;
        [SerializeField, Tooltip("没有 Prefab 时的灰盒完整尺寸（米），只影响表现，不替代连续占地。")]
        private Vector3 prototypeSize = Vector3.one;
        [SerializeField, Tooltip("没有 Prefab 时的 URP/PBR 灰盒主色。")]
        private Color prototypeColor = new(0.22f, 0.68f, 0.72f, 1f);

        public string Id => id;
        public string DisplayName => displayName;
        public NomadFacilityFunction Function => function;
        public bool Buildable => buildable;
        public bool PlaceAtStart => placeAtStart;
        public DeckPose StartPose => DeckPose.FromMeters(
            startPositionMeters.x,
            startPositionMeters.y,
            startYawDegrees);
        public IReadOnlyList<NomadFacilityInteractionGroupDefinition> InteractionGroups =>
            interactionGroups;
        public GameObject Prefab => prefab;
        public Vector3 PrototypeSize => prototypeSize;
        public Color PrototypeColor => prototypeColor;

        public ContinuousFacilityFootprint CreateFootprint()
        {
            ValidateOrThrow();
            var parts = new DeckFootprintPart[footprintParts.Length];
            for (var i = 0; i < parts.Length; i++) parts[i] = footprintParts[i].CreatePart();
            return new ContinuousFacilityFootprint(parts);
        }

        public void ValidateOrThrow()
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidOperationException($"设施定义 '{name}' 的稳定 id 为空。");
            if (footprintParts == null || footprintParts.Length == 0)
                throw new InvalidOperationException($"设施定义 '{name}' 没有连续占地。");
            if (interactionGroups == null || interactionGroups.Length == 0)
                throw new InvalidOperationException($"设施定义 '{name}' 没有 InteractionGroup。");

            var groupIds = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < interactionGroups.Length; i++)
            {
                NomadFacilityInteractionGroupDefinition group = interactionGroups[i] ??
                    throw new InvalidOperationException($"设施定义 '{name}' 第 {i} 个 InteractionGroup 为空。");
                group.ValidateOrThrow(name);
                if (!groupIds.Add(group.GroupId))
                    throw new InvalidOperationException(
                        $"设施定义 '{name}' 出现重复 InteractionGroup '{group.GroupId}'。");
            }
        }

        private void OnValidate()
        {
            id = id?.Trim() ?? string.Empty;
            displayName = displayName?.Trim() ?? string.Empty;
            if (!float.IsFinite(startPositionMeters.x)) startPositionMeters.x = 0f;
            if (!float.IsFinite(startPositionMeters.y)) startPositionMeters.y = 0f;
            if (!float.IsFinite(startYawDegrees)) startYawDegrees = 0f;
            prototypeSize.x = Mathf.Max(0.1f, prototypeSize.x);
            prototypeSize.y = Mathf.Max(0.1f, prototypeSize.y);
            prototypeSize.z = Mathf.Max(0.1f, prototypeSize.z);
            if (footprintParts != null)
            {
                for (var i = 0; i < footprintParts.Length; i++)
                {
                    NomadFacilityFootprintPartDefinition part = footprintParts[i];
                    part.Sanitize();
                    footprintParts[i] = part;
                }
            }
        }

#if UNITY_EDITOR
        /// <summary>供 Editor 生成管线与隔离测试建立可审查的设施定义。</summary>
        public void ConfigureForEditor(
            string configuredId,
            string configuredDisplayName,
            NomadFacilityFunction configuredFunction,
            bool configuredBuildable,
            NomadFacilityFootprintPartDefinition[] configuredFootprintParts,
            NomadFacilityInteractionGroupDefinition[] configuredInteractionGroups,
            bool configuredPlaceAtStart,
            Vector2 configuredStartPositionMeters,
            float configuredStartYawDegrees,
            Vector3 configuredPrototypeSize,
            Color configuredPrototypeColor)
        {
            id = configuredId;
            displayName = configuredDisplayName;
            function = configuredFunction;
            buildable = configuredBuildable;
            footprintParts = configuredFootprintParts;
            interactionGroups = configuredInteractionGroups;
            placeAtStart = configuredPlaceAtStart;
            startPositionMeters = configuredStartPositionMeters;
            startYawDegrees = configuredStartYawDegrees;
            prototypeSize = configuredPrototypeSize;
            prototypeColor = configuredPrototypeColor;
            prefab = null;
            OnValidate();
            CreateFootprint();
        }

        /// <summary>与正式 Editor 生成入口共用同一套校验，避免测试伪造第二种定义语义。</summary>
        public void ConfigureForTests(
            string configuredId,
            string configuredDisplayName,
            NomadFacilityFunction configuredFunction,
            bool configuredBuildable,
            NomadFacilityFootprintPartDefinition[] configuredFootprintParts,
            NomadFacilityInteractionGroupDefinition[] configuredInteractionGroups,
            bool configuredPlaceAtStart,
            Vector2 configuredStartPositionMeters,
            float configuredStartYawDegrees,
            Vector3 configuredPrototypeSize,
            Color configuredPrototypeColor) =>
            ConfigureForEditor(
                configuredId,
                configuredDisplayName,
                configuredFunction,
                configuredBuildable,
                configuredFootprintParts,
                configuredInteractionGroups,
                configuredPlaceAtStart,
                configuredStartPositionMeters,
                configuredStartYawDegrees,
                configuredPrototypeSize,
                configuredPrototypeColor);
#endif
    }
}
