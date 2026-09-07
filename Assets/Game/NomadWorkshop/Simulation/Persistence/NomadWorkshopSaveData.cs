using System;
using System.Collections.Generic;

namespace Game.NomadWorkshop.Simulation.Persistence
{
    /// <summary>
    /// 《游牧工坊》存档格式的稳定入口。版本只在字段语义、结构，或新字段需要非零迁移默认值时递增；
    /// 不改变旧存档含义的普通新增字段仍可利用 JSON 缺省值兼容。
    /// </summary>
    public static class NomadWorkshopSaveSchema
    {
        public const int CurrentVersion = 9;
        public const int DefaultEntertainmentPermille = 680;
        public const int DefaultMoodPermille = 700;
        public const int DefaultHealthPermille = 1000;
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
    /// 物品相对 PlacementRegion 中心的二维量化姿态。它不携带甲板层；区域所有者与区域 id
    /// 已经确定支撑面，世界姿态在加载后重建。
    /// </summary>
    [Serializable]
    public struct QuantizedPlacementPose : IEquatable<QuantizedPlacementPose>
    {
        public int LocalXMillimeters;
        public int LocalZMillimeters;
        public int LocalYawDeciDegrees;

        public QuantizedPlacementPose(
            int localXMillimeters,
            int localZMillimeters,
            int localYawDeciDegrees)
        {
            LocalXMillimeters = localXMillimeters;
            LocalZMillimeters = localZMillimeters;
            LocalYawDeciDegrees = DeckPose.NormalizeYaw(localYawDeciDegrees);
        }

        public static QuantizedPlacementPose FromPlacementPose(in PlacementRegionPose pose) =>
            new(
                pose.LocalXMillimeters,
                pose.LocalZMillimeters,
                pose.LocalYawDeciDegrees);

        public PlacementRegionPose ToPlacementPose() =>
            new(LocalXMillimeters, LocalZMillimeters, LocalYawDeciDegrees);

        public bool Equals(QuantizedPlacementPose other) =>
            LocalXMillimeters == other.LocalXMillimeters &&
            LocalZMillimeters == other.LocalZMillimeters &&
            LocalYawDeciDegrees == other.LocalYawDeciDegrees;

        public override bool Equals(object obj) =>
            obj is QuantizedPlacementPose other && Equals(other);
        public override int GetHashCode() =>
            HashCode.Combine(LocalXMillimeters, LocalZMillimeters, LocalYawDeciDegrees);
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
        public NomadStopSaveData Stop;
        public List<NomadFacilitySaveData> Facilities = new();
        public List<NomadBlueprintSaveData> Blueprints = new();
        public List<NomadWorldItemSaveData> WorldItems = new();
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
        // Journey 存在时，它是有限路线的唯一真值；旧宏观字段仍供其他 Adapter 使用。
        public NomadJourneySaveData Journey;
    }

    /// <summary>一个已提交设施的持久真值。门动画等瞬时表现不在这里重复保存。</summary>
    [Serializable]
    public sealed class NomadFacilitySaveData
    {
        public string InstanceId = string.Empty;
        public string DefinitionId = string.Empty;
        // 空值为尚未生成模型空间的旧档；是否兼容由拥有设施定义的 Adapter 在恢复前判定。
        public string SpaceSignature = string.Empty;
        public QuantizedDeckPose Pose;
        // 旧版耐久 / 污染字段继续写入可读投影，供 v3 早期存档和外部工具兼容；
        // 精确继续事故轨迹必须使用下面的连续状态与风险积分字段。
        public int DurabilityPermille = 1000;
        public int DirtPermille;
        public long WearConditionUnits;
        public long MaintenanceDebtConditionUnits;
        public long DustConditionUnits;
        public long FailureThresholdMicroHazard;
        public long AccumulatedFailureMicroHazard;
        public long FailureHazardSubMicroRemainder;
        public long FailureCycleSequence;
        public FacilityFaultKind ActiveFault;
        public int FaultSeverityPermille;
        public long FaultTriggeredSimulationTick;
        public long ConditionLastSettledSimulationTick;
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
    /// 放在设施或支撑面上的离散物品。容器内容继续由 Inventory 保存，这里只记录实例身份、
    /// 支撑所有者与精确区域姿态，避免一件物品同时拥有两套位置真值。
    /// </summary>
    [Serializable]
    public sealed class NomadWorldItemSaveData
    {
        public string ItemId = string.Empty;
        public string DefinitionId = string.Empty;
        public string OwnerEntityId = string.Empty;
        public string PlacementRegionId = string.Empty;
        public QuantizedPlacementPose PlacementLocalPose;
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
        /// <summary>
        /// 可搬动实体当前占用的全局 PlacementRegion id；空表示固定库存或由居民携带。
        /// 该字段为可选扩展，旧 v4 存档会由领域加载器依据 Owner 推导中心姿态。
        /// </summary>
        public string PlacementRegionId = string.Empty;
        public QuantizedPlacementPose PlacementLocalPose;
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
    /// 居民的持久状态；健康、娱乐、心情、疲劳、压力、卫生与晕车都跨行动保留，
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
        /// <summary>正向健康值；0 表示已经死亡，1000 表示当前完全健康。</summary>
        public int HealthPermille = NomadWorkshopSaveSchema.DefaultHealthPermille;
        public int FatiguePermille;
        public int StressPermille;
        /// <summary>正向娱乐满足度；0 表示极度无聊，1000 表示充分满足。</summary>
        public int EntertainmentPermille = NomadWorkshopSaveSchema.DefaultEntertainmentPermille;
        /// <summary>正向心情值；0 表示极差，1000 表示极好。</summary>
        public int MoodPermille = NomadWorkshopSaveSchema.DefaultMoodPermille;
        public int BodyHygieneDeficitPermille;
        public int HandContaminationPermille;
        public int MotionSicknessPermille;
        /// <summary>
        /// 体内水已经进入连续代谢、但尚不足以提交为整数 mL 转移的量，单位为 nL。
        /// 它与体内水库存共同恢复，避免频繁保存 / 加载凭空损失小数余量。
        /// </summary>
        public long WaterMetabolismPendingNanoliters;
        /// <summary>已提交的水代谢资源事务序号；下一次事务在此基础上递增。</summary>
        public int WaterMetabolismSequence;
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
            if (data.Version < 2 || data.Version > NomadWorkshopSaveSchema.CurrentVersion)
            {
                string direction = data.Version > NomadWorkshopSaveSchema.CurrentVersion
                    ? "比当前游戏更新"
                    : "缺少对应的链式迁移";
                throw new NotSupportedException(
                    $"游牧工坊存档版本 {data.Version} {direction}；当前版本为 " +
                    $"{NomadWorkshopSaveSchema.CurrentVersion}，可迁移的最早版本为 2。");
            }

            RepairOptionalCollections(data);
            MigrateToCurrentVersion(data);
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
            if (data.Vehicle.Journey is { IsEmpty: false })
                data.Vehicle.Journey.ToValidatedSnapshot();
            if (data.Stop is { IsEmpty: false }) data.Stop.Validate();

            RequireCollections(data);
            var entityIds = new HashSet<string>(StringComparer.Ordinal);
            if (data.Stop is { IsEmpty: false }) entityIds.Add("site:" + data.Stop.SiteId);
            ValidateFacilities(data.Facilities, entityIds, data.SimulationTick);
            ValidateBlueprints(data.Blueprints, entityIds);
            ValidateWorldItems(data.WorldItems, entityIds);

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
            data.WorldItems ??= new List<NomadWorldItemSaveData>();
            data.Inventories ??= new List<NomadInventorySaveData>();
            data.Residents ??= new List<NomadResidentSaveData>();
            data.RandomStreams ??= new List<NomadRandomStreamSaveData>();
            for (var i = 0; i < data.Inventories.Count; i++)
            {
                NomadInventorySaveData inventory = data.Inventories[i];
                if (inventory != null) inventory.Contents ??= new List<NomadResourceStackSaveData>();
            }
            for (var i = 0; i < data.Residents.Count; i++)
            {
                NomadResidentSaveData resident = data.Residents[i];
                if (resident?.ActiveAction != null && IsSerializedEmptyAction(resident.ActiveAction))
                    resident.ActiveAction = null;
            }
        }

        /// <summary>
        /// Unity JSON 会把某些 null 嵌套 DTO 还原成全默认值对象。只正规化真正全空的行动；
        /// 若 Stage=None 却携带任何身份、进度或结算位，后续校验仍会按损坏数据拒绝。
        /// </summary>
        private static bool IsSerializedEmptyAction(NomadResidentActionSaveData action) =>
            action.Stage == NomadResidentActionSaveStage.None &&
            string.IsNullOrEmpty(action.TaskId) &&
            string.IsNullOrEmpty(action.ActionId) &&
            string.IsNullOrEmpty(action.TargetEntityId) &&
            string.IsNullOrEmpty(action.InteractionGroupId) &&
            string.IsNullOrEmpty(action.InteractionSlotId) &&
            action.DestinationPose.Equals(default(QuantizedDeckPose)) &&
            action.ProgressPermille == 0 &&
            !action.OutcomeCommitted;

        /// <summary>
        /// 按版本逐级迁移业务语义。v2 尚未保存娱乐与心情；不能依赖 JSON 反序列化器是否执行字段初始化，
        /// 因此统一赋予当时新游戏的中性初值，再升级版本。未来迁移继续在此 switch 中逐级追加。
        /// </summary>
        private static void MigrateToCurrentVersion(NomadWorkshopSaveData data)
        {
            while (data.Version < NomadWorkshopSaveSchema.CurrentVersion)
            {
                switch (data.Version)
                {
                    case 2:
                        for (var i = 0; i < data.Residents.Count; i++)
                        {
                            NomadResidentSaveData resident = data.Residents[i];
                            if (resident == null) continue;
                            resident.EntertainmentPermille =
                                NomadWorkshopSaveSchema.DefaultEntertainmentPermille;
                            resident.MoodPermille = NomadWorkshopSaveSchema.DefaultMoodPermille;
                        }

                        data.Version = 3;
                        break;
                    case 3:
                        // v3 尚未拥有健康真值；旧居民按当时“不会死亡”的实际语义迁移为健康。
                        for (var i = 0; i < data.Residents.Count; i++)
                        {
                            NomadResidentSaveData resident = data.Residents[i];
                            if (resident == null) continue;
                            resident.HealthPermille =
                                NomadWorkshopSaveSchema.DefaultHealthPermille;
                        }

                        data.Version = 4;
                        break;
                    case 4:
                        // v4 的 Foundation 尚无旅途。由运行 Adapter 为缺省旅途提供初始地点和燃料；
                        // 保留旧宏观字段，不支持它们的 Adapter 仍须拒绝，不能偷偷重置非空进度。
                        if (data.Vehicle != null) data.Vehicle.Journey = null;
                        data.Version = 5;
                        break;
                    case 5:
                        // 厕所库存归属依赖设施定义；由 Foundation Adapter 校验并迁移旧共享桶，
                        // 通用协议保留全部库存原文，不猜测哪种自定义设施是厕所。
                        data.Version = 6;
                        break;
                    case 6:
                        data.Stop = null;
                        data.Version = 7;
                        break;
                    case 7:
                        if (data.Stop is { IsEmpty: false })
                        {
                            data.Stop.WasteMilliliters = 0;
                            data.Stop.WasteCapacityMilliliters = 24_000;
                            data.Stop.WasteDisposalRequested = false;
                        }
                        data.Version = 8;
                        break;
                    case 8:
                        // 地点实体物品由 Foundation 按定义建立；旧 JSON 缺失的初始化标记为 false。
                        data.Version = 9;
                        break;
                    default:
                        throw new NotSupportedException(
                            $"游牧工坊存档版本 {data.Version} 缺少到版本 " +
                            $"{NomadWorkshopSaveSchema.CurrentVersion} 的链式迁移。");
                }
            }
        }

        private static void RequireCollections(NomadWorkshopSaveData data)
        {
            if (data.Facilities == null || data.Blueprints == null ||
                data.WorldItems == null ||
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
            HashSet<string> entityIds,
            long simulationTick)
        {
            for (var i = 0; i < facilities.Count; i++)
            {
                NomadFacilitySaveData facility = facilities[i] ??
                    throw new InvalidOperationException($"设施列表第 {i} 项为空。");
                RequireUniqueId(facility.InstanceId, "设施实例", entityIds);
                RequireId(facility.DefinitionId, "设施定义");
                if (!string.IsNullOrEmpty(facility.SpaceSignature))
                {
                    if (facility.SpaceSignature.Length != 64)
                        throw new InvalidOperationException($"设施 {facility.InstanceId} 的空间摘要长度无效。");
                    foreach (char c in facility.SpaceSignature)
                        if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f'))
                            throw new InvalidOperationException($"设施 {facility.InstanceId} 的空间摘要必须为小写 SHA-256。");
                }
                ValidatePose(facility.Pose, $"设施 {facility.InstanceId}");
                ValidateRange(facility.DurabilityPermille, $"设施 {facility.InstanceId} 耐久");
                ValidateRange(facility.DirtPermille, $"设施 {facility.InstanceId} 污染");
                ValidateFacilityCondition(facility, simulationTick);
            }
        }

        private static void ValidateFacilityCondition(
            NomadFacilitySaveData facility,
            long simulationTick)
        {
            if (!Enum.IsDefined(typeof(FacilityFaultKind), facility.ActiveFault))
                throw new InvalidOperationException(
                    $"设施 {facility.InstanceId} 的具体故障类型无效。");

            if (facility.FailureThresholdMicroHazard == 0L)
            {
                // v3 早期存档没有这些字段，Unity JSON 会全部还原为零。只把完整零集视为旧格式，
                // 防止损坏的新检查点被静默当作“全新设施”。
                if (facility.WearConditionUnits != 0L ||
                    facility.MaintenanceDebtConditionUnits != 0L ||
                    facility.DustConditionUnits != 0L ||
                    facility.AccumulatedFailureMicroHazard != 0L ||
                    facility.FailureHazardSubMicroRemainder != 0L ||
                    facility.FailureCycleSequence != 0L ||
                    facility.ActiveFault != FacilityFaultKind.None ||
                    facility.FaultSeverityPermille != 0 ||
                    facility.FaultTriggeredSimulationTick != 0L ||
                    facility.ConditionLastSettledSimulationTick != 0L)
                    throw new InvalidOperationException(
                        $"设施 {facility.InstanceId} 的状态检查点不完整：缺少故障阈值。");
                return;
            }

            if (facility.ConditionLastSettledSimulationTick > simulationTick)
                throw new InvalidOperationException(
                    $"设施 {facility.InstanceId} 的状态 Tick 晚于根 SimulationTick。");
            try
            {
                var checkpoint = new FacilityConditionCheckpoint(
                    facility.InstanceId,
                    facility.ConditionLastSettledSimulationTick,
                    facility.WearConditionUnits,
                    facility.MaintenanceDebtConditionUnits,
                    facility.DustConditionUnits,
                    facility.FailureThresholdMicroHazard,
                    facility.AccumulatedFailureMicroHazard,
                    facility.FailureHazardSubMicroRemainder,
                    facility.FailureCycleSequence,
                    facility.ActiveFault,
                    facility.FaultSeverityPermille,
                    facility.FaultTriggeredSimulationTick);
                int wearPermille = ToConditionPermille(checkpoint.WearUnits);
                int dustPermille = ToConditionPermille(checkpoint.DustUnits);
                if (facility.DurabilityPermille != 1000 - wearPermille ||
                    facility.DirtPermille != dustPermille)
                    throw new ArgumentException(
                        "兼容耐久 / 污染投影与精确设施状态不一致。");
            }
            catch (ArgumentException exception)
            {
                throw new InvalidOperationException(
                    $"设施 {facility.InstanceId} 的状态检查点无效：{exception.Message}",
                    exception);
            }
        }

        private static int ToConditionPermille(long conditionUnits) =>
            (int)Math.Clamp(
                (conditionUnits + FacilityConditionCycle.ConditionUnitsPerPermille / 2L) /
                FacilityConditionCycle.ConditionUnitsPerPermille,
                0L,
                1000L);

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

        private static void ValidateWorldItems(
            IReadOnlyList<NomadWorldItemSaveData> worldItems,
            HashSet<string> entityIds)
        {
            for (var i = 0; i < worldItems.Count; i++)
            {
                NomadWorldItemSaveData item = worldItems[i] ??
                    throw new InvalidOperationException($"世界物品列表第 {i} 项为空。");
                RequireUniqueId(item.ItemId, "世界物品实例", entityIds);
                RequireId(item.DefinitionId, $"世界物品 {item.ItemId} 定义");
                RequireId(item.OwnerEntityId, $"世界物品 {item.ItemId} 所有者");
                if (!entityIds.Contains(item.OwnerEntityId))
                    throw new InvalidOperationException(
                        $"世界物品 {item.ItemId} 引用不存在的支撑实体 {item.OwnerEntityId}。");
                RequireId(item.PlacementRegionId, $"世界物品 {item.ItemId} 放置区域");
                string ownerPrefix = item.OwnerEntityId + "/placement/";
                if (!item.PlacementRegionId.StartsWith(ownerPrefix, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"世界物品 {item.ItemId} 的区域不属于支撑实体 {item.OwnerEntityId}。");
                if (item.PlacementLocalPose.LocalYawDeciDegrees < 0 ||
                    item.PlacementLocalPose.LocalYawDeciDegrees >= 3600)
                    throw new InvalidOperationException(
                        $"世界物品 {item.ItemId} 的区域局部角度必须位于 [0, 3600)。");
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
                if (!string.IsNullOrWhiteSpace(inventory.PlacementRegionId) &&
                    (inventory.PlacementLocalPose.LocalYawDeciDegrees < 0 ||
                     inventory.PlacementLocalPose.LocalYawDeciDegrees >= 3600))
                    throw new InvalidOperationException(
                        $"库存 {inventory.InventoryId} 的区域局部角度必须位于 [0, 3600)。");
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
                ValidateRange(resident.HealthPermille, $"居民 {resident.ResidentId} 健康");
                ValidateRange(resident.FatiguePermille, $"居民 {resident.ResidentId} 疲劳");
                ValidateRange(resident.StressPermille, $"居民 {resident.ResidentId} 压力");
                ValidateRange(resident.EntertainmentPermille, $"居民 {resident.ResidentId} 娱乐满足");
                ValidateRange(resident.MoodPermille, $"居民 {resident.ResidentId} 心情");
                ValidateRange(resident.BodyHygieneDeficitPermille, $"居民 {resident.ResidentId} 身体卫生缺口");
                ValidateRange(resident.HandContaminationPermille, $"居民 {resident.ResidentId} 手部污染");
                ValidateRange(resident.MotionSicknessPermille, $"居民 {resident.ResidentId} 晕车");
                if (resident.WaterMetabolismPendingNanoliters < 0L)
                    throw new InvalidOperationException(
                        $"居民 {resident.ResidentId} 的待提交水代谢量不能为负数。");
                if (resident.WaterMetabolismSequence < 0)
                    throw new InvalidOperationException(
                        $"居民 {resident.ResidentId} 的水代谢序号不能为负数。");
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
