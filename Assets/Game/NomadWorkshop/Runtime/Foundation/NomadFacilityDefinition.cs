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

    /// <summary>
    /// 设施在自身局部空间提供的一片物品放置区域。它描述桌面、柜台或地面停放区，
    /// 不替代居民为了操作设施而使用的 <see cref="NomadFacilityInteractionGroupDefinition"/>。
    /// </summary>
    [Serializable]
    public struct NomadPlacementRegionDefinition
    {
        [SerializeField, Tooltip("设施内部稳定且唯一的区域 id；存档发布后不要随意改名。")]
        private string regionId;
        [SerializeField, Tooltip("区域中心相对设施枢轴的 X/Z 偏移（米）。")]
        private Vector2 localCenterMeters;
        [SerializeField, Tooltip("区域沿自身 X/Z 的完整尺寸（米）。")]
        private Vector2 sizeMeters;
        [SerializeField, Tooltip("区域相对设施枢轴的局部旋转角（度）。")]
        private float localYawDegrees;
        [SerializeField, Min(0f), Tooltip("物品根节点相对甲板的支撑高度（米）；地面停放区通常为 0。")]
        private float supportHeightMeters;
        [SerializeField, Min(0f), Tooltip("从区域边缘向内保留的安全距离（米），防止物品贴边或穿过挡板。")]
        private float edgeInsetMeters;
        [SerializeField, Tooltip("该区域允许接收的物品类别，例如 water-can、food 或 tool。")]
        private string[] acceptedCategories;

        public NomadPlacementRegionDefinition(
            string regionId,
            Vector2 localCenterMeters,
            Vector2 sizeMeters,
            float localYawDegrees,
            float supportHeightMeters,
            float edgeInsetMeters,
            params string[] acceptedCategories)
        {
            this.regionId = regionId;
            this.localCenterMeters = localCenterMeters;
            this.sizeMeters = sizeMeters;
            this.localYawDegrees = localYawDegrees;
            this.supportHeightMeters = supportHeightMeters;
            this.edgeInsetMeters = edgeInsetMeters;
            this.acceptedCategories = acceptedCategories != null
                ? (string[])acceptedCategories.Clone()
                : Array.Empty<string>();
            Sanitize();
        }

        public string RegionId => regionId;
        public Vector2 LocalCenterMeters => localCenterMeters;
        public Vector2 SizeMeters => sizeMeters;
        public float LocalYawDegrees => localYawDegrees;
        public float SupportHeightMeters => supportHeightMeters;
        public float EdgeInsetMeters => edgeInsetMeters;
        public IReadOnlyList<string> AcceptedCategories => acceptedCategories;

        public PlacementRegionDefinition CreateRegion(
            string ownerEntityId,
            in DeckPose ownerPose)
        {
            ValidateOrThrow(ownerEntityId);
            DeckPose local = DeckPose.FromMeters(
                localCenterMeters.x,
                localCenterMeters.y,
                localYawDegrees);
            DeckPose size = DeckPose.FromMeters(sizeMeters.x, sizeMeters.y, 0d);
            return new PlacementRegionDefinition(
                PlacementRegionLedger.ComposeRegionId(ownerEntityId, regionId),
                regionId,
                ownerEntityId,
                ownerPose.TransformLocal(
                    local.XMillimeters,
                    local.ZMillimeters,
                    local.YawDeciDegrees),
                size.XMillimeters,
                size.ZMillimeters,
                Mathf.RoundToInt(supportHeightMeters * 1000f),
                Mathf.RoundToInt(edgeInsetMeters * 1000f),
                acceptedCategories);
        }

        public DeckFootprintPart CreateFunctionalClearancePart()
        {
            DeckPose local = DeckPose.FromMeters(
                localCenterMeters.x,
                localCenterMeters.y,
                localYawDegrees);
            DeckPose size = DeckPose.FromMeters(sizeMeters.x, sizeMeters.y, 0d);
            return new DeckFootprintPart(
                local.XMillimeters,
                local.ZMillimeters,
                size.XMillimeters,
                size.ZMillimeters,
                local.YawDeciDegrees);
        }

        internal void ValidateOrThrow(string ownerName)
        {
            Sanitize();
            if (string.IsNullOrWhiteSpace(regionId))
                throw new InvalidOperationException($"设施 '{ownerName}' 存在空 PlacementRegion id。");
            if (acceptedCategories == null || acceptedCategories.Length == 0)
                throw new InvalidOperationException(
                    $"设施 '{ownerName}' 的 PlacementRegion '{regionId}' 没有允许的物品类别。");
            var categories = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < acceptedCategories.Length; i++)
            {
                string category = acceptedCategories[i];
                if (string.IsNullOrWhiteSpace(category))
                    throw new InvalidOperationException(
                        $"设施 '{ownerName}' 的 PlacementRegion '{regionId}' 第 {i} 个类别为空。");
                if (!categories.Add(category))
                    throw new InvalidOperationException(
                        $"设施 '{ownerName}' 的 PlacementRegion '{regionId}' 重复接受类别 '{category}'。");
            }
        }

        internal void Sanitize()
        {
            regionId = regionId?.Trim() ?? string.Empty;
            if (!float.IsFinite(localCenterMeters.x)) localCenterMeters.x = 0f;
            if (!float.IsFinite(localCenterMeters.y)) localCenterMeters.y = 0f;
            if (!float.IsFinite(localYawDegrees)) localYawDegrees = 0f;
            sizeMeters.x = float.IsFinite(sizeMeters.x) ? Mathf.Max(0.05f, sizeMeters.x) : 0.05f;
            sizeMeters.y = float.IsFinite(sizeMeters.y) ? Mathf.Max(0.05f, sizeMeters.y) : 0.05f;
            supportHeightMeters = float.IsFinite(supportHeightMeters)
                ? Mathf.Max(0f, supportHeightMeters)
                : 0f;
            edgeInsetMeters = float.IsFinite(edgeInsetMeters)
                ? Mathf.Clamp(edgeInsetMeters, 0f, Mathf.Min(sizeMeters.x, sizeMeters.y) * 0.49f)
                : 0f;
            acceptedCategories ??= Array.Empty<string>();
            for (var i = 0; i < acceptedCategories.Length; i++)
                acceptedCategories[i] = acceptedCategories[i]?.Trim() ?? string.Empty;
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

        [Header("物品放置区域（相对设施枢轴）")]
        [SerializeField, Tooltip("桌面、柜台或地面停放区；区域容量和物品占地由纯模拟账本裁定，不使用刚体落点作为真值。")]
        private NomadPlacementRegionDefinition[] placementRegions =
            Array.Empty<NomadPlacementRegionDefinition>();

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
        public IReadOnlyList<NomadPlacementRegionDefinition> PlacementRegions => placementRegions;
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

        /// <summary>
        /// 返回不参与 NavMesh 障碍、但设施摆放不得侵占的物品功能净空；没有区域时返回 null。
        /// </summary>
        public ContinuousFacilityFootprint CreateFunctionalClearanceFootprint()
        {
            ValidateOrThrow();
            if (placementRegions.Length == 0) return null;

            var parts = new DeckFootprintPart[placementRegions.Length];
            for (var i = 0; i < parts.Length; i++)
                parts[i] = placementRegions[i].CreateFunctionalClearancePart();
            return new ContinuousFacilityFootprint(parts);
        }

        public bool TryGetPlacementRegion(
            string localRegionId,
            out NomadPlacementRegionDefinition region)
        {
            for (var i = 0; i < placementRegions.Length; i++)
            {
                if (!string.Equals(
                        placementRegions[i].RegionId,
                        localRegionId,
                        StringComparison.Ordinal))
                    continue;
                region = placementRegions[i];
                return true;
            }

            region = default;
            return false;
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

            placementRegions ??= Array.Empty<NomadPlacementRegionDefinition>();
            var regionIds = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < placementRegions.Length; i++)
            {
                NomadPlacementRegionDefinition region = placementRegions[i];
                region.ValidateOrThrow(name);
                placementRegions[i] = region;
                if (!regionIds.Add(region.RegionId))
                    throw new InvalidOperationException(
                        $"设施定义 '{name}' 出现重复 PlacementRegion '{region.RegionId}'。");
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
            placementRegions ??= Array.Empty<NomadPlacementRegionDefinition>();
            for (var i = 0; i < placementRegions.Length; i++)
            {
                NomadPlacementRegionDefinition region = placementRegions[i];
                region.Sanitize();
                placementRegions[i] = region;
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
            NomadPlacementRegionDefinition[] configuredPlacementRegions,
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
            placementRegions = configuredPlacementRegions ??
                               Array.Empty<NomadPlacementRegionDefinition>();
            placeAtStart = configuredPlaceAtStart;
            startPositionMeters = configuredStartPositionMeters;
            startYawDegrees = configuredStartYawDegrees;
            prototypeSize = configuredPrototypeSize;
            prototypeColor = configuredPrototypeColor;
            prefab = null;
            OnValidate();
            CreateFootprint();
        }

        /// <summary>兼容没有物品放置区域的既有 Editor 生成器。</summary>
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
            Color configuredPrototypeColor) =>
            ConfigureForEditor(
                configuredId,
                configuredDisplayName,
                configuredFunction,
                configuredBuildable,
                configuredFootprintParts,
                configuredInteractionGroups,
                Array.Empty<NomadPlacementRegionDefinition>(),
                configuredPlaceAtStart,
                configuredStartPositionMeters,
                configuredStartYawDegrees,
                configuredPrototypeSize,
                configuredPrototypeColor);

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

        /// <summary>隔离测试可显式声明物品放置区域，验证正式定义的空间语义。</summary>
        public void ConfigureForTests(
            string configuredId,
            string configuredDisplayName,
            NomadFacilityFunction configuredFunction,
            bool configuredBuildable,
            NomadFacilityFootprintPartDefinition[] configuredFootprintParts,
            NomadFacilityInteractionGroupDefinition[] configuredInteractionGroups,
            NomadPlacementRegionDefinition[] configuredPlacementRegions,
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
                configuredPlacementRegions,
                configuredPlaceAtStart,
                configuredStartPositionMeters,
                configuredStartYawDegrees,
                configuredPrototypeSize,
                configuredPrototypeColor);
#endif
    }
}
