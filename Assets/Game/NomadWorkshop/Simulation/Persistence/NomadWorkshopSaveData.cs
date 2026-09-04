using System;
using System.Collections.Generic;

namespace Game.NomadWorkshop.Simulation.Persistence
{
    /// <summary>
    /// 《游牧工坊》存档格式的稳定入口。版本只在字段语义或结构发生不兼容变化时递增；
    /// 普通新增字段继续利用 JSON 的缺省值兼容。
    /// </summary>
    public static class NomadWorkshopSaveSchema
    {
        public const int CurrentVersion = 2;
    }

    /// <summary>
    /// 车辆甲板局部平面上的确定性姿态。位置以毫米、朝向以 0.1 度保存，既允许视觉上的自由摆放，
    /// 又避免浮点噪声进入存档、撤销记录和自动测试。
    /// </summary>
    [Serializable]
    public struct QuantizedDeckPose : IEquatable<QuantizedDeckPose>
    {
        public int XMillimeters;
        public int ZMillimeters;
        public int YawDeciDegrees;
        public int DeckLevel;

        public QuantizedDeckPose(
            int xMillimeters,
            int zMillimeters,
            int yawDeciDegrees,
            int deckLevel = 0)
        {
            XMillimeters = xMillimeters;
            ZMillimeters = zMillimeters;
            YawDeciDegrees = DeckPose.NormalizeYaw(yawDeciDegrees);
            DeckLevel = deckLevel;
        }

        public float XMeters => XMillimeters / 1000f;
        public float ZMeters => ZMillimeters / 1000f;
        public float YawDegrees => YawDeciDegrees / 10f;

        public static QuantizedDeckPose FromMeters(
            float xMeters,
            float zMeters,
            float yawDegrees,
            int deckLevel = 0) => FromDeckPose(
            DeckPose.FromMeters(xMeters, zMeters, yawDegrees, deckLevel));

        /// <summary>把运行时连续姿态复制进可变 JSON DTO，不引入 Unity 类型或浮点噪声。</summary>
        public static QuantizedDeckPose FromDeckPose(in DeckPose pose) =>
            new(
                pose.XMillimeters,
                pose.ZMillimeters,
                pose.YawDeciDegrees,
                pose.DeckLevel);

        /// <summary>
        /// 转回纯模拟姿态。加载方应先调用 <see cref="NomadWorkshopSaveContract.PrepareAfterLoad"/>
        /// 校验原始 DTO，避免自动规范化掩盖损坏的存档字段。
        /// </summary>
        public DeckPose ToDeckPose() =>
            new(XMillimeters, ZMillimeters, YawDeciDegrees, DeckLevel);

        public bool Equals(QuantizedDeckPose other) =>
            XMillimeters == other.XMillimeters &&
            ZMillimeters == other.ZMillimeters &&
            YawDeciDegrees == other.YawDeciDegrees &&
            DeckLevel == other.DeckLevel;

        public override bool Equals(object obj) => obj is QuantizedDeckPose other && Equals(other);
        public override int GetHashCode() =>
            HashCode.Combine(XMillimeters, ZMillimeters, YawDeciDegrees, DeckLevel);

    }

    /// <summary>
    /// 一次完整、可恢复的游戏进度快照。它只保存业务真值；NavMesh 路径、局部避让速度、材质实例、
    /// 响应式订阅和调试指标均在加载后由 Adapter / View 重建。
    /// </summary>
    [Serializable]
    public sealed class NomadWorkshopSaveData
    {
        public int Version = NomadWorkshopSaveSchema.CurrentVersion;
        public int WorldSeed;

        /// <summary>
        /// 统一模拟时钟的毫秒 Tick。生活日、气候相位和离线推进都从它投影，
        /// 不分别保存可能互相漂移的“当前小时”和“当前季节进度”。
        /// </summary>
        public long SimulationTick;
        public NomadVehicleSaveData Vehicle = new();
        public List<NomadFacilitySaveData> Facilities = new();
        public List<NomadBlueprintSaveData> Blueprints = new();
        public List<NomadInventorySaveData> Inventories = new();
        public List<NomadResidentSaveData> Residents = new();
        public List<NomadRandomStreamSaveData> RandomStreams = new();
    }

    /// <summary>
    /// 一个领域随机流的最小持久游标。世界 Seed 位于根快照；对象与领域共同确定流，
    /// 只保存下一事件序号即可恢复后续轨迹，不序列化运行时 PRNG 对象。
    /// </summary>
    [Serializable]
    public sealed class NomadRandomStreamSaveData
    {
        public string OwnerEntityId = string.Empty;
        public string StreamId = string.Empty;
        public long NextEventSequence;
    }

    /// <summary>宏观世界与车辆旅途的最小持久状态；地图表现和路边临时装饰按 Seed 重建。</summary>
    [Serializable]
    public sealed class NomadVehicleSaveData
    {
        public int MapXCentimeters;
        public int MapZCentimeters;
        public string CurrentRegionId = string.Empty;
        public string DestinationId = string.Empty;
        public int TravelProgressPermille;
        public int FuelMilliUnits;
        public bool IsTraveling;
    }

    /// <summary>一个已提交设施的持久真值。门动画等瞬时表现不在这里重复保存。</summary>
    [Serializable]
    public sealed class NomadFacilitySaveData
    {
        public string InstanceId = string.Empty;
        public string DefinitionId = string.Empty;
        public QuantizedDeckPose Pose;
        public int DurabilityPermille = 1000;
        public int DirtPermille;
    }

    public enum NomadBlueprintSaveStage
    {
        PendingValidation,
        AwaitingMaterials,
        Building,
    }

    /// <summary>
    /// 尚未成为已建成设施的蓝图。若保存发生在异步 NavMesh 更新期间，加载后回到
    /// <see cref="NomadBlueprintSaveStage.PendingValidation"/> 重新验证，而不保存半提交的引擎事务。
    /// </summary>
    [Serializable]
    public sealed class NomadBlueprintSaveData
    {
        public string InstanceId = string.Empty;
        public string DefinitionId = string.Empty;
        public QuantizedDeckPose Pose;
        public NomadBlueprintSaveStage Stage;
        public int ConstructionProgressPermille;
        public bool MaterialsCommitted;
    }

    /// <summary>
    /// 一个同质资源批次。批次 id 允许同一种食物按不同新鲜度分开保存，避免把腐败状态平均后失真。
    /// </summary>
    [Serializable]
    public sealed class NomadResourceStackSaveData
    {
        public string StackId = string.Empty;
        public string ResourceId = string.Empty;
        public ResourceMeasure Measure;
        public int AmountBaseUnits;
        public int ConditionPermille = 1000;
        public int ContaminationPermille;
    }

    /// <summary>
    /// 真实储物节点；容量与内容共享同一计量维度。复合设施保存多个库存隔间，不能把件数与 mL
    /// 相加成一个总容量。容器污染和每个批次的位置是存档真值，不保存运行期预留计数。
    /// </summary>
    [Serializable]
    public sealed class NomadInventorySaveData
    {
        public string InventoryId = string.Empty;
        public string OwnerEntityId = string.Empty;
        public ResourceMeasure Measure;
        public int CapacityBaseUnits;
        public int ContaminationPermille;
        public List<NomadResourceStackSaveData> Contents = new();
    }

    public enum NomadResidentActionSaveStage
    {
        None,
        Moving,
        Docking,
        Opening,
        Working,
        Closing,
        Blocked,
    }

    /// <summary>
    /// 可从任意可见阶段恢复的居民行动检查点。<see cref="OutcomeCommitted"/> 是防止加载后重复转移物品、
    /// 重复消耗材料或重复满足需求的关键幂等位。
    /// </summary>
    [Serializable]
    public sealed class NomadResidentActionSaveData
    {
        public string TaskId = string.Empty;
        public string ActionId = string.Empty;
        public string TargetEntityId = string.Empty;
        public string InteractionGroupId = string.Empty;
        public string InteractionSlotId = string.Empty;
        public QuantizedDeckPose DestinationPose;
        public NomadResidentActionSaveStage Stage;
        public int ProgressPermille;
        public bool OutcomeCommitted;
    }

    /// <summary>
    /// 居民的持久状态；手部污染、身体卫生缺口和晕车跨行动保留，
    /// 寻路走廊与 RVO 速度则在加载后按位置、目标和行动阶段重新计算。
    /// </summary>
    [Serializable]
    public sealed class NomadResidentSaveData
    {
        public string ResidentId = string.Empty;
        public QuantizedDeckPose Pose;
        public string PersonalInventoryId = string.Empty;
        public int HungerPermille;
        public int ThirstPermille;
        public int FatiguePermille;
        public int StressPermille;
        public int BodyHygieneDeficitPermille;
        public int HandContaminationPermille;
        public int MotionSicknessPermille;
        public NomadResidentActionSaveData ActiveAction;
    }

    public enum NomadActionRestoreDisposition
    {
        None,
        ReplanPath,
        ReacquireInteractionAndResume,
        ResumePresentationAfterCommittedOutcome,
        RequeueBlockedAction,
    }

    /// <summary>
    /// 存档的业务校验与加载恢复规则。租约、路径和 Unity 对象引用不会序列化；加载方先重建世界，
    /// 再按稳定居民 id 顺序重新申请行动所需容量，避免读取列表顺序改变争用结果。
    /// </summary>
    public static class NomadWorkshopSaveContract
    {
        public static NomadWorkshopSaveData PrepareAfterLoad(NomadWorkshopSaveData data)
        {
            if (data == null) return null;
            if (data.Version != NomadWorkshopSaveSchema.CurrentVersion)
            {
                string direction = data.Version > NomadWorkshopSaveSchema.CurrentVersion
                    ? "比当前游戏更新"
                    : "缺少对应的链式迁移";
                throw new NotSupportedException(
                    $"游牧工坊存档版本 {data.Version} {direction}；当前只支持版本 " +
                    $"{NomadWorkshopSaveSchema.CurrentVersion}。");
            }

            RepairOptionalCollections(data);
            ValidateForSave(data);
            return data;
        }

        public static void ValidateForSave(NomadWorkshopSaveData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Version != NomadWorkshopSaveSchema.CurrentVersion)
                throw new InvalidOperationException(
                    $"只能写入当前版本 {NomadWorkshopSaveSchema.CurrentVersion}，实际为 {data.Version}。");
            if (data.SimulationTick < 0)
                throw new InvalidOperationException("SimulationTick 不能为负数。");
            if (data.Vehicle == null)
                throw new InvalidOperationException("存档缺少车辆状态。");
            ValidateRange(data.Vehicle.TravelProgressPermille, nameof(data.Vehicle.TravelProgressPermille));
            if (data.Vehicle.FuelMilliUnits < 0)
                throw new InvalidOperationException("车辆燃料不能为负数。");

            RequireCollections(data);
            var entityIds = new HashSet<string>(StringComparer.Ordinal);
            ValidateFacilities(data.Facilities, entityIds);
            ValidateBlueprints(data.Blueprints, entityIds);

            var inventoryIds = new HashSet<string>(StringComparer.Ordinal);
            var stackIds = new HashSet<string>(StringComparer.Ordinal);
            ValidateInventories(data.Inventories, inventoryIds, stackIds);
            ValidateResidents(data.Residents, entityIds, inventoryIds);
            ValidateRandomStreams(data.RandomStreams);
        }

        public static NomadActionRestoreDisposition GetRestoreDisposition(
            NomadResidentActionSaveData action)
        {
            if (action == null || action.Stage == NomadResidentActionSaveStage.None)
                return NomadActionRestoreDisposition.None;

            return action.Stage switch
            {
                NomadResidentActionSaveStage.Moving => NomadActionRestoreDisposition.ReplanPath,
                NomadResidentActionSaveStage.Docking or
                    NomadResidentActionSaveStage.Opening =>
                    NomadActionRestoreDisposition.ReacquireInteractionAndResume,
                NomadResidentActionSaveStage.Working => action.OutcomeCommitted
                    ? NomadActionRestoreDisposition.ResumePresentationAfterCommittedOutcome
                    : NomadActionRestoreDisposition.ReacquireInteractionAndResume,
                NomadResidentActionSaveStage.Closing =>
                    NomadActionRestoreDisposition.ResumePresentationAfterCommittedOutcome,
                NomadResidentActionSaveStage.Blocked =>
                    NomadActionRestoreDisposition.RequeueBlockedAction,
                _ => throw new ArgumentOutOfRangeException(nameof(action), action.Stage, "未知行动阶段。"),
            };
        }

        public static IReadOnlyList<NomadResidentSaveData> GetReservationRestoreOrder(
            NomadWorkshopSaveData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            PrepareAfterLoad(data);
            var result = new List<NomadResidentSaveData>(data.Residents);
            result.Sort((left, right) =>
                string.Compare(left.ResidentId, right.ResidentId, StringComparison.Ordinal));
            return result;
        }

        private static void RepairOptionalCollections(NomadWorkshopSaveData data)
        {
            data.Vehicle ??= new NomadVehicleSaveData();
            data.Facilities ??= new List<NomadFacilitySaveData>();
            data.Blueprints ??= new List<NomadBlueprintSaveData>();
            data.Inventories ??= new List<NomadInventorySaveData>();
            data.Residents ??= new List<NomadResidentSaveData>();
            data.RandomStreams ??= new List<NomadRandomStreamSaveData>();
            for (var i = 0; i < data.Inventories.Count; i++)
            {
                NomadInventorySaveData inventory = data.Inventories[i];
                if (inventory != null) inventory.Contents ??= new List<NomadResourceStackSaveData>();
            }
        }

        private static void RequireCollections(NomadWorkshopSaveData data)
        {
            if (data.Facilities == null || data.Blueprints == null ||
                data.Inventories == null || data.Residents == null || data.RandomStreams == null)
                throw new InvalidOperationException("存档集合不能为 null；无内容时使用空列表。");
        }

        private static void ValidateRandomStreams(
            IReadOnlyList<NomadRandomStreamSaveData> randomStreams)
        {
            var identities = new HashSet<(string OwnerEntityId, string StreamId)>();
            for (var i = 0; i < randomStreams.Count; i++)
            {
                NomadRandomStreamSaveData stream = randomStreams[i] ??
                    throw new InvalidOperationException($"随机流列表第 {i} 项为空。");
                RequireId(stream.OwnerEntityId, "随机流 owner");
                RequireId(stream.StreamId, "随机流");
                if (!string.Equals(
                        stream.OwnerEntityId,
                        stream.OwnerEntityId.Trim(),
                        StringComparison.Ordinal) ||
                    !string.Equals(stream.StreamId, stream.StreamId.Trim(), StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"随机流 {stream.OwnerEntityId}/{stream.StreamId} 必须使用无首尾空白的规范 id。");
                if (stream.NextEventSequence < 0)
                    throw new InvalidOperationException(
                        $"随机流 {stream.OwnerEntityId}/{stream.StreamId} 的下一事件序号不能为负数。");

                if (!identities.Add((stream.OwnerEntityId, stream.StreamId)))
                    throw new InvalidOperationException(
                        $"随机流 {stream.OwnerEntityId}/{stream.StreamId} 重复。");
            }
        }

        private static void ValidateFacilities(
            IReadOnlyList<NomadFacilitySaveData> facilities,
            HashSet<string> entityIds)
        {
            for (var i = 0; i < facilities.Count; i++)
            {
                NomadFacilitySaveData facility = facilities[i] ??
                    throw new InvalidOperationException($"设施列表第 {i} 项为空。");
                RequireUniqueId(facility.InstanceId, "设施实例", entityIds);
                RequireId(facility.DefinitionId, "设施定义");
                ValidatePose(facility.Pose, $"设施 {facility.InstanceId}");
                ValidateRange(facility.DurabilityPermille, $"设施 {facility.InstanceId} 耐久");
                ValidateRange(facility.DirtPermille, $"设施 {facility.InstanceId} 污染");
            }
        }

        private static void ValidateBlueprints(
            IReadOnlyList<NomadBlueprintSaveData> blueprints,
            HashSet<string> entityIds)
        {
            for (var i = 0; i < blueprints.Count; i++)
            {
                NomadBlueprintSaveData blueprint = blueprints[i] ??
                    throw new InvalidOperationException($"蓝图列表第 {i} 项为空。");
                RequireUniqueId(blueprint.InstanceId, "蓝图实例", entityIds);
                RequireId(blueprint.DefinitionId, "蓝图定义");
                ValidatePose(blueprint.Pose, $"蓝图 {blueprint.InstanceId}");
                ValidateRange(
                    blueprint.ConstructionProgressPermille,
                    $"蓝图 {blueprint.InstanceId} 建造进度");
                if (!Enum.IsDefined(typeof(NomadBlueprintSaveStage), blueprint.Stage))
                    throw new InvalidOperationException($"蓝图 {blueprint.InstanceId} 阶段无效。");
            }
        }

        private static void ValidateInventories(
            IReadOnlyList<NomadInventorySaveData> inventories,
            HashSet<string> inventoryIds,
            HashSet<string> stackIds)
        {
            for (var i = 0; i < inventories.Count; i++)
            {
                NomadInventorySaveData inventory = inventories[i] ??
                    throw new InvalidOperationException($"库存列表第 {i} 项为空。");
                RequireUniqueId(inventory.InventoryId, "库存", inventoryIds);
                if (!Enum.IsDefined(typeof(ResourceMeasure), inventory.Measure))
                    throw new InvalidOperationException(
                        $"库存 {inventory.InventoryId} 的计量维度无效。");
                if (inventory.CapacityBaseUnits < 0)
                    throw new InvalidOperationException($"库存 {inventory.InventoryId} 容量不能为负数。");
                ValidateRange(inventory.ContaminationPermille, $"库存 {inventory.InventoryId} 污染");
                if (inventory.Contents == null)
                    throw new InvalidOperationException($"库存 {inventory.InventoryId} 内容不能为 null。");

                var total = 0;
                for (var stackIndex = 0; stackIndex < inventory.Contents.Count; stackIndex++)
                {
                    NomadResourceStackSaveData stack = inventory.Contents[stackIndex] ??
                        throw new InvalidOperationException(
                            $"库存 {inventory.InventoryId} 的第 {stackIndex} 个批次为空。");
                    RequireUniqueId(stack.StackId, "资源批次", stackIds);
                    RequireId(stack.ResourceId, "资源");
                    if (!Enum.IsDefined(typeof(ResourceMeasure), stack.Measure) ||
                        stack.Measure != inventory.Measure)
                        throw new InvalidOperationException(
                            $"资源批次 {stack.StackId} 的计量维度与库存 {inventory.InventoryId} 不一致。");
                    if (stack.AmountBaseUnits <= 0)
                        throw new InvalidOperationException($"资源批次 {stack.StackId} 数量必须大于零。");
                    ValidateRange(stack.ConditionPermille, $"资源批次 {stack.StackId} 状态");
                    ValidateRange(stack.ContaminationPermille, $"资源批次 {stack.StackId} 污染");
                    total = checked(total + stack.AmountBaseUnits);
                }

                if (total > inventory.CapacityBaseUnits)
                    throw new InvalidOperationException(
                        $"库存 {inventory.InventoryId} 内容 {total} 超过容量 " +
                        $"{inventory.CapacityBaseUnits}。");
            }
        }

        private static void ValidateResidents(
            IReadOnlyList<NomadResidentSaveData> residents,
            HashSet<string> entityIds,
            HashSet<string> inventoryIds)
        {
            for (var i = 0; i < residents.Count; i++)
            {
                NomadResidentSaveData resident = residents[i] ??
                    throw new InvalidOperationException($"居民列表第 {i} 项为空。");
                RequireUniqueId(resident.ResidentId, "居民", entityIds);
                ValidatePose(resident.Pose, $"居民 {resident.ResidentId}");
                RequireId(resident.PersonalInventoryId, $"居民 {resident.ResidentId} 的随身库存");
                if (!inventoryIds.Contains(resident.PersonalInventoryId))
                    throw new InvalidOperationException(
                        $"居民 {resident.ResidentId} 引用不存在的库存 {resident.PersonalInventoryId}。");
                ValidateRange(resident.HungerPermille, $"居民 {resident.ResidentId} 饥饿");
                ValidateRange(resident.ThirstPermille, $"居民 {resident.ResidentId} 口渴");
                ValidateRange(resident.FatiguePermille, $"居民 {resident.ResidentId} 疲劳");
                ValidateRange(resident.StressPermille, $"居民 {resident.ResidentId} 压力");
                ValidateRange(resident.BodyHygieneDeficitPermille, $"居民 {resident.ResidentId} 身体卫生缺口");
                ValidateRange(resident.HandContaminationPermille, $"居民 {resident.ResidentId} 手部污染");
                ValidateRange(resident.MotionSicknessPermille, $"居民 {resident.ResidentId} 晕车");
                ValidateAction(resident);
            }
        }

        private static void ValidateAction(NomadResidentSaveData resident)
        {
            NomadResidentActionSaveData action = resident.ActiveAction;
            if (action == null) return;
            if (action.Stage == NomadResidentActionSaveStage.None)
                throw new InvalidOperationException(
                    $"居民 {resident.ResidentId} 的 ActiveAction 不能使用 None；无行动时设为 null。");
            if (!Enum.IsDefined(typeof(NomadResidentActionSaveStage), action.Stage))
                throw new InvalidOperationException($"居民 {resident.ResidentId} 的行动阶段无效。");
            RequireId(action.TaskId, "行动任务");
            RequireId(action.ActionId, "行动定义");
            RequireId(action.TargetEntityId, "行动目标");
            ValidatePose(action.DestinationPose, $"行动 {action.TaskId} 目标");
            ValidateRange(action.ProgressPermille, $"行动 {action.TaskId} 进度");

            bool hasGroup = !string.IsNullOrWhiteSpace(action.InteractionGroupId);
            bool hasSlot = !string.IsNullOrWhiteSpace(action.InteractionSlotId);
            if (hasGroup != hasSlot)
                throw new InvalidOperationException(
                    $"行动 {action.TaskId} 的 InteractionGroupId 与 InteractionSlotId 必须同时存在或同时为空。");
            bool requiresInteraction = action.Stage is
                NomadResidentActionSaveStage.Docking or
                NomadResidentActionSaveStage.Opening or
                NomadResidentActionSaveStage.Working or
                NomadResidentActionSaveStage.Closing;
            if (requiresInteraction && !hasGroup)
                throw new InvalidOperationException(
                    $"行动 {action.TaskId} 已进入设施交互阶段，却没有保存 Group / Slot 身份。");
            bool isBeforeOutcome = action.Stage is
                NomadResidentActionSaveStage.Moving or
                NomadResidentActionSaveStage.Docking or
                NomadResidentActionSaveStage.Opening;
            if (action.OutcomeCommitted && isBeforeOutcome)
                throw new InvalidOperationException(
                    $"行动 {action.TaskId} 尚未到结算阶段，不能标记 OutcomeCommitted。");
            if (action.Stage == NomadResidentActionSaveStage.Closing && !action.OutcomeCommitted)
                throw new InvalidOperationException(
                    $"行动 {action.TaskId} 进入 Closing 前必须先记录业务结算已经提交。");
        }

        private static void ValidatePose(QuantizedDeckPose pose, string label)
        {
            if (pose.YawDeciDegrees < 0 || pose.YawDeciDegrees >= 3600)
                throw new InvalidOperationException(
                    $"{label} 的 YawDeciDegrees 必须规范化到 [0, 3600)。");
        }

        private static void ValidateRange(int value, string label)
        {
            if (value < 0 || value > 1000)
                throw new InvalidOperationException($"{label} 必须位于 0 到 1000。");
        }

        private static void RequireId(string value, string label)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"{label} id 不能为空。");
        }

        private static void RequireUniqueId(string value, string label, HashSet<string> ids)
        {
            RequireId(value, label);
            if (!ids.Add(value))
                throw new InvalidOperationException($"{label} id '{value}' 重复。");
        }
    }
}
