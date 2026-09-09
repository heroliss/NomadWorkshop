using System;
using System.Collections.Generic;
using Game.NomadWorkshop.Simulation;
using UnityEngine;

namespace Game.NomadWorkshop.Foundation
{
    public enum FoundationInteractionMode
    {
        Observe,
        Build,
    }

    public enum FoundationBuildTransactionPhase
    {
        Idle,
        UpdatingCandidateNavigation,
        RollingBackNavigation,
    }

    /// <summary>同时覆盖便宜几何预检与异步 NavMesh 事务的玩家可解释失败原因。</summary>
    public enum FoundationPlacementFailure
    {
        None,
        InvalidRequest,
        DuplicateInstanceId,
        DeckLevelUnavailable,
        FootprintOutOfBounds,
        FootprintOverlapsFacility,
        FunctionalClearanceOutOfBounds,
        FunctionalClearanceOverlapsFacility,
        NavigationUpdateFailed,
        RequiredInteractionUnreachable,
        TransactionInProgress,
    }

    /// <summary>设施从当前居民所在连通区访问必需交互位的运行时诊断。</summary>
    public enum FoundationFacilityAccess
    {
        Unknown,
        Reachable,
        PartiallyReachable,
        Unreachable,
    }

    public enum FoundationResidentPhase
    {
        // 显式保留既有数值，避免新增阶段后破坏 Inspector、存档或录制数据中的枚举值。
        WaitingForFacility = 0,
        Idle = 1,
        MovingToWaterCan = 2,
        PickingUpWaterCan = 3,
        MovingToWaterSource = 4,
        PickingUpWater = 5,
        MovingToDrinkingStation = 6,
        DeliveringWater = 7,
        Drinking = 8,
        MovingToLeisure = 9,
        Relaxing = 10,
        Blocked = 11,
        WaitingForRoute = 12,
        MovingToToilet = 13,
        UsingToilet = 14,
        MovingToHobby = 15,
        EnjoyingHobby = 16,
        RestingOnGround = 17,
        Dead = 18,
        MovingToWorldItemSource = 19,
        PickingUpWorldItem = 20,
        MovingToWorldItemDestination = 21,
        PlacingWorldItem = 22,
        MovingToRepairPart = 23,
        PickingUpRepairPart = 24,
        MovingToRepairTarget = 25,
        RepairingFacility = 26,
        MovingToDriver = 27,
        Driving = 28,
        MovingToStopWater = 29,
        FillingAtStop = 30,
        ReturningFromStopWater = 31,
        DeliveringStopWater = 32,
        MovingToWasteBucket = 33,
        DetachingWasteBucket = 34,
        MovingToWasteReceiver = 35,
        EmptyingWasteBucket = 36,
        ReturningWasteBucket = 37,
        InstallingWasteBucket = 38,
        MovingToStopSpare = 39,
        PickingUpStopSpare = 40,
        ReturningStopSpare = 41,
        DeliveringStopSpare = 42,
        LiftingWaterCan = 43,
        MovingToWaterCanParking = 44,
        PlacingWaterCan = 45,
        ReleasingWaterCan = 46,
    }

    /// <summary>当前基础休闲的可观察类型；以后新增爱好时不会把所有休闲继续压成一个计数。</summary>
    public enum FoundationLeisureKind
    {
        None,
        Daydream,
        Wander,
        GroundRest,
        Hobby,
    }

    /// <summary>唯一防漏水罐当前所在位置；水罐内容物由独立库存守恒，不能凭空挂在居民身上。</summary>
    public enum FoundationWaterCanLocation
    {
        VehicleWaterTank,
        DrinkingStation,
        Resident,
    }

    /// <summary>
    /// 一件世界物品在放置区域中的只读投影。局部量化姿态是存档真值，世界姿态只为 View、
    /// 调试和选择提供方便，仍由区域与局部姿态确定性推导。
    /// </summary>
    [Serializable]
    public struct FoundationItemPlacementState : IEquatable<FoundationItemPlacementState>
    {
        [SerializeField] private string itemId;
        [SerializeField] private string definitionId;
        [SerializeField] private string regionId;
        [SerializeField] private string ownerEntityId;
        [SerializeField] private int localXMillimeters;
        [SerializeField] private int localZMillimeters;
        [SerializeField] private int localYawDeciDegrees;
        [SerializeField] private int worldXMillimeters;
        [SerializeField] private int worldZMillimeters;
        [SerializeField] private int worldYawDeciDegrees;
        [SerializeField] private int deckLevel;
        [SerializeField] private int supportHeightMillimeters;

        public FoundationItemPlacementState(PlacementRegionItem placement)
        {
            if (placement == null) throw new ArgumentNullException(nameof(placement));
            itemId = placement.ItemId;
            definitionId = placement.Footprint.DefinitionId;
            regionId = placement.Region.RegionId;
            ownerEntityId = placement.Region.OwnerEntityId;
            localXMillimeters = placement.LocalPose.LocalXMillimeters;
            localZMillimeters = placement.LocalPose.LocalZMillimeters;
            localYawDeciDegrees = placement.LocalPose.LocalYawDeciDegrees;
            worldXMillimeters = placement.WorldPose.XMillimeters;
            worldZMillimeters = placement.WorldPose.ZMillimeters;
            worldYawDeciDegrees = placement.WorldPose.YawDeciDegrees;
            deckLevel = placement.WorldPose.DeckLevel;
            supportHeightMillimeters = placement.Region.SupportHeightMillimeters;
        }

        public bool Active => !string.IsNullOrWhiteSpace(itemId);
        public string ItemId => itemId;
        public string DefinitionId => definitionId;
        public string RegionId => regionId;
        public string OwnerEntityId => ownerEntityId;
        public PlacementRegionPose LocalPose =>
            new(localXMillimeters, localZMillimeters, localYawDeciDegrees);
        public DeckPose WorldPose =>
            new(worldXMillimeters, worldZMillimeters, worldYawDeciDegrees, deckLevel);
        public int SupportHeightMillimeters => supportHeightMillimeters;

        public bool Equals(FoundationItemPlacementState other) =>
            string.Equals(itemId, other.itemId, StringComparison.Ordinal) &&
            string.Equals(definitionId, other.definitionId, StringComparison.Ordinal) &&
            string.Equals(regionId, other.regionId, StringComparison.Ordinal) &&
            string.Equals(ownerEntityId, other.ownerEntityId, StringComparison.Ordinal) &&
            localXMillimeters == other.localXMillimeters &&
            localZMillimeters == other.localZMillimeters &&
            localYawDeciDegrees == other.localYawDeciDegrees &&
            worldXMillimeters == other.worldXMillimeters &&
            worldZMillimeters == other.worldZMillimeters &&
            worldYawDeciDegrees == other.worldYawDeciDegrees &&
            deckLevel == other.deckLevel &&
            supportHeightMillimeters == other.supportHeightMillimeters;

        public override bool Equals(object obj) =>
            obj is FoundationItemPlacementState other && Equals(other);

        public override int GetHashCode()
        {
            int identity = HashCode.Combine(itemId, definitionId, regionId, ownerEntityId);
            int local = HashCode.Combine(
                localXMillimeters,
                localZMillimeters,
                localYawDeciDegrees);
            int world = HashCode.Combine(
                worldXMillimeters,
                worldZMillimeters,
                worldYawDeciDegrees,
                deckLevel,
                supportHeightMillimeters);
            return HashCode.Combine(identity, local, world);
        }
    }

    /// <summary>
    /// 居民手中普通世界物品的短暂表现投影。物品移动事务仍由 System 独占，随时存档会回退到
    /// 来源区域；这里不伪造第三套持久位置真值。
    /// </summary>
    [Serializable]
    public struct FoundationCarriedWorldItemState : IEquatable<FoundationCarriedWorldItemState>
    {
        [SerializeField] private string itemId;
        [SerializeField] private string definitionId;

        public FoundationCarriedWorldItemState(string itemId, string definitionId)
        {
            this.itemId = itemId?.Trim() ?? string.Empty;
            this.definitionId = definitionId?.Trim() ?? string.Empty;
        }

        public bool Active =>
            !string.IsNullOrWhiteSpace(itemId) &&
            !string.IsNullOrWhiteSpace(definitionId);
        public string ItemId => itemId;
        public string DefinitionId => definitionId;

        public bool Equals(FoundationCarriedWorldItemState other) =>
            string.Equals(itemId, other.itemId, StringComparison.Ordinal) &&
            string.Equals(definitionId, other.definitionId, StringComparison.Ordinal);

        public override bool Equals(object obj) =>
            obj is FoundationCarriedWorldItemState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(itemId, definitionId);
    }

    /// <summary>
    /// 某个设施实例内一个真实库存隔间的只读投影。稳定设施 id 与库存 id 让调试、存档和 AI 工具
    /// 能区分同类型设施，而不是把多座饮水站误看成一个共享水池。
    /// </summary>
    [Serializable]
    public struct FoundationFacilityInventoryState : IEquatable<FoundationFacilityInventoryState>
    {
        [SerializeField] private string facilityInstanceId;
        [SerializeField] private string inventoryId;
        [SerializeField] private string compartmentId;
        [SerializeField] private string resourceId;
        [SerializeField] private ResourceMeasure measure;
        [SerializeField] private int amount;
        [SerializeField] private int capacity;

        public FoundationFacilityInventoryState(
            string facilityInstanceId,
            string inventoryId,
            string compartmentId,
            string resourceId,
            ResourceMeasure measure,
            int amount,
            int capacity)
        {
            this.facilityInstanceId = facilityInstanceId ?? string.Empty;
            this.inventoryId = inventoryId ?? string.Empty;
            this.compartmentId = compartmentId ?? string.Empty;
            this.resourceId = resourceId ?? string.Empty;
            this.measure = measure;
            this.amount = Math.Max(0, amount);
            this.capacity = Math.Max(0, capacity);
        }

        public string FacilityInstanceId => facilityInstanceId;
        public string InventoryId => inventoryId;
        public string CompartmentId => compartmentId;
        public string ResourceId => resourceId;
        public ResourceMeasure Measure => measure;
        public int Amount => amount;
        public int Capacity => capacity;

        public bool Equals(FoundationFacilityInventoryState other) =>
            string.Equals(facilityInstanceId, other.facilityInstanceId, StringComparison.Ordinal) &&
            string.Equals(inventoryId, other.inventoryId, StringComparison.Ordinal) &&
            string.Equals(compartmentId, other.compartmentId, StringComparison.Ordinal) &&
            string.Equals(resourceId, other.resourceId, StringComparison.Ordinal) &&
            measure == other.measure &&
            amount == other.amount &&
            capacity == other.capacity;

        public override bool Equals(object obj) =>
            obj is FoundationFacilityInventoryState other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(
            facilityInstanceId,
            inventoryId,
            compartmentId,
            resourceId,
            measure,
            amount,
            capacity);
    }

    /// <summary>
    /// 一座设施的连续状态与具体故障只读投影。磨损、维护欠账和积尘彼此独立，风险进度只是
    /// 已累计暴露相对本轮预取样阈值的解释值；View 不得通过该投影直接修理或改写状态。
    /// </summary>
    [Serializable]
    public struct FoundationFacilityConditionState :
        IEquatable<FoundationFacilityConditionState>
    {
        [SerializeField] private string instanceId;
        [SerializeField] private NomadFacilityFunction function;
        [SerializeField] private int wearPermille;
        [SerializeField] private int maintenanceDebtPermille;
        [SerializeField] private int dustPermille;
        [SerializeField] private int failureRiskProgressPermille;
        [SerializeField] private long currentRiskRateMicroHazardPerSecond;
        [SerializeField] private long failureThresholdMicroHazard;
        [SerializeField] private long accumulatedFailureMicroHazard;
        [SerializeField] private FacilityConditionWarning warning;
        [SerializeField] private FacilityFaultKind activeFault;
        [SerializeField] private int faultSeverityPermille;
        [SerializeField] private long faultTriggeredSimulationTick;

        public FoundationFacilityConditionState(
            string instanceId,
            NomadFacilityFunction function,
            int wearPermille,
            int maintenanceDebtPermille,
            int dustPermille,
            int failureRiskProgressPermille,
            long currentRiskRateMicroHazardPerSecond,
            long failureThresholdMicroHazard,
            long accumulatedFailureMicroHazard,
            FacilityConditionWarning warning,
            FacilityFaultKind activeFault,
            int faultSeverityPermille,
            long faultTriggeredSimulationTick)
        {
            this.instanceId = instanceId ?? string.Empty;
            this.function = function;
            this.wearPermille = Math.Clamp(wearPermille, 0, 1000);
            this.maintenanceDebtPermille = Math.Clamp(
                maintenanceDebtPermille,
                0,
                1000);
            this.dustPermille = Math.Clamp(dustPermille, 0, 1000);
            this.failureRiskProgressPermille = Math.Clamp(
                failureRiskProgressPermille,
                0,
                1000);
            this.currentRiskRateMicroHazardPerSecond = Math.Max(
                0L,
                currentRiskRateMicroHazardPerSecond);
            this.failureThresholdMicroHazard = Math.Max(
                0L,
                failureThresholdMicroHazard);
            this.accumulatedFailureMicroHazard = Math.Max(
                0L,
                accumulatedFailureMicroHazard);
            this.warning = warning;
            this.activeFault = activeFault;
            this.faultSeverityPermille = Math.Clamp(faultSeverityPermille, 0, 1000);
            this.faultTriggeredSimulationTick = Math.Max(
                0L,
                faultTriggeredSimulationTick);
        }

        public string InstanceId => instanceId;
        public NomadFacilityFunction Function => function;
        public int WearPermille => wearPermille;
        public int MaintenanceDebtPermille => maintenanceDebtPermille;
        public int DustPermille => dustPermille;
        public int FailureRiskProgressPermille => failureRiskProgressPermille;
        public long CurrentRiskRateMicroHazardPerSecond =>
            currentRiskRateMicroHazardPerSecond;
        public long FailureThresholdMicroHazard => failureThresholdMicroHazard;
        public long AccumulatedFailureMicroHazard => accumulatedFailureMicroHazard;
        public FacilityConditionWarning Warning => warning;
        public FacilityFaultKind ActiveFault => activeFault;
        public int FaultSeverityPermille => faultSeverityPermille;
        public long FaultTriggeredSimulationTick => faultTriggeredSimulationTick;
        public bool IsOperational => activeFault == FacilityFaultKind.None;

        public bool Equals(FoundationFacilityConditionState other) =>
            string.Equals(instanceId, other.instanceId, StringComparison.Ordinal) &&
            function == other.function &&
            wearPermille == other.wearPermille &&
            maintenanceDebtPermille == other.maintenanceDebtPermille &&
            dustPermille == other.dustPermille &&
            failureRiskProgressPermille == other.failureRiskProgressPermille &&
            currentRiskRateMicroHazardPerSecond ==
            other.currentRiskRateMicroHazardPerSecond &&
            failureThresholdMicroHazard == other.failureThresholdMicroHazard &&
            accumulatedFailureMicroHazard == other.accumulatedFailureMicroHazard &&
            warning == other.warning &&
            activeFault == other.activeFault &&
            faultSeverityPermille == other.faultSeverityPermille &&
            faultTriggeredSimulationTick == other.faultTriggeredSimulationTick;

        public override bool Equals(object obj) =>
            obj is FoundationFacilityConditionState other && Equals(other);

        public override int GetHashCode()
        {
            int conditionHash = HashCode.Combine(
                instanceId,
                function,
                wearPermille,
                maintenanceDebtPermille,
                dustPermille,
                failureRiskProgressPermille,
                currentRiskRateMicroHazardPerSecond,
                failureThresholdMicroHazard);
            return HashCode.Combine(
                conditionHash,
                accumulatedFailureMicroHazard,
                warning,
                activeFault,
                faultSeverityPermille,
                faultTriggeredSimulationTick);
        }
    }

    /// <summary>
    /// 最近一次完整行动方案的 Inspector 投影。它只保存可解释结果，不把纯 C# 估算器或可变候选泄露给 View。
    /// TotalUtility 是 Utility AI 在本次需求快照上的最终分数，而不是策划长期平衡承诺。
    /// </summary>
    [Serializable]
    public struct FoundationActionPlanProjection : IEquatable<FoundationActionPlanProjection>
    {
        [SerializeField] private bool evaluated;
        [SerializeField] private string candidateId;
        [SerializeField] private string displayName;
        [SerializeField] private bool feasible;
        [SerializeField] private bool selected;
        [SerializeField] private string blocker;
        [SerializeField] private int stepCount;
        [SerializeField] private float totalDurationSeconds;
        [SerializeField] private float travelSeconds;
        [SerializeField] private float travelDistanceMeters;
        [SerializeField] private float contextBenefit;
        [SerializeField] private float travelCost;
        [SerializeField] private float activeCost;
        [SerializeField] private float effortCost;
        [SerializeField] private float expectedRiskCost;
        [SerializeField] private float planCost;
        [SerializeField] private float totalUtility;
        [SerializeField] private float selectionProbability;
        [SerializeField] private ResidentDecisionRiskTier riskTier;
        [SerializeField] private float riskPriority;

        public FoundationActionPlanProjection(
            string candidateId,
            string displayName,
            bool feasible,
            bool selected,
            string blocker,
            int stepCount,
            float totalDurationSeconds,
            float travelSeconds,
            float travelDistanceMeters,
            float contextBenefit,
            float travelCost,
            float activeCost,
            float effortCost,
            float expectedRiskCost,
            float planCost,
            float totalUtility,
            float selectionProbability,
            ResidentDecisionRiskTier riskTier,
            float riskPriority)
        {
            evaluated = true;
            this.candidateId = candidateId ?? string.Empty;
            this.displayName = displayName ?? string.Empty;
            this.feasible = feasible;
            this.selected = selected;
            this.blocker = blocker ?? string.Empty;
            this.stepCount = Math.Max(0, stepCount);
            this.totalDurationSeconds = Math.Max(0f, totalDurationSeconds);
            this.travelSeconds = Math.Max(0f, travelSeconds);
            this.travelDistanceMeters = Math.Max(0f, travelDistanceMeters);
            this.contextBenefit = contextBenefit;
            this.travelCost = Math.Max(0f, travelCost);
            this.activeCost = Math.Max(0f, activeCost);
            this.effortCost = Math.Max(0f, effortCost);
            this.expectedRiskCost = Math.Max(0f, expectedRiskCost);
            this.planCost = Math.Max(0f, planCost);
            this.totalUtility = totalUtility;
            this.selectionProbability = Mathf.Clamp01(selectionProbability);
            this.riskTier = riskTier;
            this.riskPriority = Mathf.Clamp01(riskPriority);
        }

        public bool Evaluated => evaluated;
        public string CandidateId => candidateId;
        public string DisplayName => displayName;
        public bool Feasible => feasible;
        public bool Selected => selected;
        public string Blocker => blocker;
        public int StepCount => stepCount;
        public float TotalDurationSeconds => totalDurationSeconds;
        public float TravelSeconds => travelSeconds;
        public float TravelDistanceMeters => travelDistanceMeters;
        public float ContextBenefit => contextBenefit;
        public float TravelCost => travelCost;
        public float ActiveCost => activeCost;
        public float EffortCost => effortCost;
        public float ExpectedRiskCost => expectedRiskCost;
        public float PlanCost => planCost;
        public float TotalUtility => totalUtility;
        public float SelectionProbability => selectionProbability;
        public ResidentDecisionRiskTier RiskTier => riskTier;
        public float RiskPriority => riskPriority;

        public bool Equals(FoundationActionPlanProjection other) =>
            evaluated == other.evaluated &&
            string.Equals(candidateId, other.candidateId, StringComparison.Ordinal) &&
            string.Equals(displayName, other.displayName, StringComparison.Ordinal) &&
            feasible == other.feasible &&
            selected == other.selected &&
            string.Equals(blocker, other.blocker, StringComparison.Ordinal) &&
            stepCount == other.stepCount &&
            totalDurationSeconds.Equals(other.totalDurationSeconds) &&
            travelSeconds.Equals(other.travelSeconds) &&
            travelDistanceMeters.Equals(other.travelDistanceMeters) &&
            contextBenefit.Equals(other.contextBenefit) &&
            travelCost.Equals(other.travelCost) &&
            activeCost.Equals(other.activeCost) &&
            effortCost.Equals(other.effortCost) &&
            expectedRiskCost.Equals(other.expectedRiskCost) &&
            planCost.Equals(other.planCost) &&
            totalUtility.Equals(other.totalUtility) &&
            selectionProbability.Equals(other.selectionProbability) &&
            riskTier == other.riskTier &&
            riskPriority.Equals(other.riskPriority);

        public override bool Equals(object obj) =>
            obj is FoundationActionPlanProjection other && Equals(other);

        public override int GetHashCode()
        {
            int identityHash = HashCode.Combine(
                evaluated,
                candidateId,
                displayName,
                feasible,
                selected,
                blocker,
                stepCount,
                totalDurationSeconds);
            int routeHash = HashCode.Combine(
                travelSeconds,
                travelDistanceMeters,
                contextBenefit,
                travelCost,
                activeCost,
                effortCost,
                expectedRiskCost,
                planCost);
            int decisionHash = HashCode.Combine(
                totalUtility,
                selectionProbability,
                riskTier,
                riskPriority);
            return HashCode.Combine(identityHash, routeHash, decisionHash);
        }

        public static FoundationActionPlanProjection None => default;
    }

    /// <summary>可序列化的设施实例真值；世界 Transform 只从连续量化姿态派生。</summary>
    [Serializable]
    public struct FoundationFacilityState : IEquatable<FoundationFacilityState>
    {
        [SerializeField] private string instanceId;
        [SerializeField] private string definitionId;
        [SerializeField] private int xMillimeters;
        [SerializeField] private int zMillimeters;
        [SerializeField] private int yawDeciDegrees;
        [SerializeField] private int deckLevel;

        public FoundationFacilityState(
            string instanceId,
            string definitionId,
            in DeckPose pose)
        {
            this.instanceId = instanceId;
            this.definitionId = definitionId;
            xMillimeters = pose.XMillimeters;
            zMillimeters = pose.ZMillimeters;
            yawDeciDegrees = pose.YawDeciDegrees;
            deckLevel = pose.DeckLevel;
        }

        public string InstanceId => instanceId;
        public string DefinitionId => definitionId;
        public DeckPose Pose => new(xMillimeters, zMillimeters, yawDeciDegrees, deckLevel);

        public bool Equals(FoundationFacilityState other) =>
            string.Equals(instanceId, other.instanceId, StringComparison.Ordinal) &&
            string.Equals(definitionId, other.definitionId, StringComparison.Ordinal) &&
            xMillimeters == other.xMillimeters &&
            zMillimeters == other.zMillimeters &&
            yawDeciDegrees == other.yawDeciDegrees &&
            deckLevel == other.deckLevel;

        public override bool Equals(object obj) =>
            obj is FoundationFacilityState other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(
                instanceId,
                definitionId,
                xMillimeters,
                zMillimeters,
                yawDeciDegrees,
                deckLevel);
    }

    /// <summary>
    /// 正式 Foundation 对未完成/已完成蓝图的只读投影。材料数量来自蓝图自有库存的快照，
    /// 不把暂存物料复制成设施库存，也不把施工阶段交给 View 推断。
    /// </summary>
    [Serializable]
    public struct FoundationConstructionBlueprintState : IEquatable<FoundationConstructionBlueprintState>
    {
        [SerializeField] private string instanceId;
        [SerializeField] private string definitionId;
        [SerializeField] private int xMillimeters;
        [SerializeField] private int zMillimeters;
        [SerializeField] private int yawDeciDegrees;
        [SerializeField] private int deckLevel;
        [SerializeField] private string siteId;
        [SerializeField] private NomadConstructionSiteKind siteKind;
        [SerializeField] private string routeId;
        [SerializeField] private long routeProgressMillimeters;
        [SerializeField] private long retentionDistanceMillimeters;
        [SerializeField] private NomadConstructionStage stage;
        [SerializeField] private int requiredWorkUnits;
        [SerializeField] private int completedWorkUnits;
        [SerializeField] private int requiredMaterialItems;
        [SerializeField] private int stagedMaterialItems;
        [SerializeField] private int installedMaterialItems;

        public FoundationConstructionBlueprintState(
            NomadConstructionBlueprintCheckpoint checkpoint)
        {
            if (checkpoint == null) throw new ArgumentNullException(nameof(checkpoint));
            instanceId = checkpoint.Placement.InstanceId;
            definitionId = checkpoint.Placement.DefinitionId;
            DeckPose pose = checkpoint.Placement.Pose;
            xMillimeters = pose.XMillimeters;
            zMillimeters = pose.ZMillimeters;
            yawDeciDegrees = pose.YawDeciDegrees;
            deckLevel = pose.DeckLevel;
            siteId = checkpoint.Site.SiteId;
            siteKind = checkpoint.Site.Kind;
            routeId = checkpoint.Site.RouteId;
            routeProgressMillimeters = checkpoint.Site.RouteProgressMillimeters;
            retentionDistanceMillimeters = checkpoint.Site.RetentionDistanceMillimeters;
            stage = checkpoint.Stage;
            requiredWorkUnits = checkpoint.RequiredWorkUnits;
            completedWorkUnits = checkpoint.CompletedWorkUnits;
            requiredMaterialItems = Sum(checkpoint.RequiredMaterials);
            stagedMaterialItems = Sum(checkpoint.StagedMaterials);
            installedMaterialItems = Sum(checkpoint.InstalledMaterials);
        }

        public string InstanceId => instanceId;
        public string DefinitionId => definitionId;
        public DeckPose Pose => new(xMillimeters, zMillimeters, yawDeciDegrees, deckLevel);
        public NomadConstructionSite Site => new(
            siteId,
            siteKind,
            routeId,
            routeProgressMillimeters,
            retentionDistanceMillimeters);
        public NomadConstructionStage Stage => stage;
        public int RequiredWorkUnits => requiredWorkUnits;
        public int CompletedWorkUnits => completedWorkUnits;
        public int RequiredMaterialItems => requiredMaterialItems;
        public int StagedMaterialItems => stagedMaterialItems;
        public int InstalledMaterialItems => installedMaterialItems;
        public int ConstructionProgressPermille => requiredWorkUnits <= 0
            ? 0
            : Math.Clamp((int)((long)completedWorkUnits * 1000L / requiredWorkUnits), 0, 1000);

        public bool Equals(FoundationConstructionBlueprintState other) =>
            string.Equals(instanceId, other.instanceId, StringComparison.Ordinal) &&
            string.Equals(definitionId, other.definitionId, StringComparison.Ordinal) &&
            xMillimeters == other.xMillimeters && zMillimeters == other.zMillimeters &&
            yawDeciDegrees == other.yawDeciDegrees && deckLevel == other.deckLevel &&
            string.Equals(siteId, other.siteId, StringComparison.Ordinal) && siteKind == other.siteKind &&
            string.Equals(routeId, other.routeId, StringComparison.Ordinal) &&
            routeProgressMillimeters == other.routeProgressMillimeters &&
            retentionDistanceMillimeters == other.retentionDistanceMillimeters &&
            stage == other.stage && requiredWorkUnits == other.requiredWorkUnits &&
            completedWorkUnits == other.completedWorkUnits &&
            requiredMaterialItems == other.requiredMaterialItems &&
            stagedMaterialItems == other.stagedMaterialItems &&
            installedMaterialItems == other.installedMaterialItems;

        public override bool Equals(object obj) => obj is FoundationConstructionBlueprintState other && Equals(other);
        public override int GetHashCode()
        {
            int pose = HashCode.Combine(
                instanceId, definitionId, xMillimeters, zMillimeters,
                yawDeciDegrees, deckLevel);
            int site = HashCode.Combine(
                siteId, siteKind, routeId, routeProgressMillimeters,
                retentionDistanceMillimeters);
            int progress = HashCode.Combine(
                stage, requiredWorkUnits, completedWorkUnits,
                requiredMaterialItems, stagedMaterialItems, installedMaterialItems);
            return HashCode.Combine(pose, site, progress);
        }

        private static int Sum(IReadOnlyList<NomadConstructionMaterialRequirement> values)
        {
            int total = 0;
            for (var i = 0; i < values.Count; i++) total = checked(total + values[i].Amount);
            return total;
        }

        private static int Sum(IReadOnlyList<ResourceQuantity> values)
        {
            int total = 0;
            for (var i = 0; i < values.Count; i++) total = checked(total + values[i].Amount);
            return total;
        }
    }

    /// <summary>
    /// 设施访问状态的只读投影。CommittedAccess 描述已提交 NavMesh；预览有效时，PreviewAccess
    /// 描述候选设施落地后的保守预测。它是可重建诊断，不应作为存档真值。
    /// </summary>
    [Serializable]
    public struct FoundationFacilityAccessState : IEquatable<FoundationFacilityAccessState>
    {
        [SerializeField] private string instanceId;
        [SerializeField] private FoundationFacilityAccess committedAccess;
        [SerializeField] private ulong committedReachableSlotMask;
        [SerializeField] private ulong committedSharedSpaceSlotMask;
        [SerializeField] private ulong committedOccupiedSlotMask;
        [SerializeField] private int interactionSlotCount;
        [SerializeField] private bool previewEvaluated;
        [SerializeField] private FoundationFacilityAccess previewAccess;
        [SerializeField] private ulong previewReachableSlotMask;
        [SerializeField] private ulong previewSharedSpaceSlotMask;

        public FoundationFacilityAccessState(
            string instanceId,
            FoundationFacilityAccess committedAccess,
            ulong committedReachableSlotMask,
            ulong committedSharedSpaceSlotMask,
            ulong committedOccupiedSlotMask,
            int interactionSlotCount,
            bool previewEvaluated = false,
            FoundationFacilityAccess previewAccess = FoundationFacilityAccess.Unknown,
            ulong previewReachableSlotMask = 0UL,
            ulong previewSharedSpaceSlotMask = 0UL)
        {
            this.instanceId = instanceId ?? string.Empty;
            this.committedAccess = committedAccess;
            this.committedReachableSlotMask = committedReachableSlotMask;
            this.committedSharedSpaceSlotMask = committedSharedSpaceSlotMask;
            this.committedOccupiedSlotMask = committedOccupiedSlotMask;
            this.interactionSlotCount = Math.Clamp(interactionSlotCount, 0, 64);
            this.previewEvaluated = previewEvaluated;
            this.previewAccess = previewEvaluated
                ? previewAccess
                : FoundationFacilityAccess.Unknown;
            this.previewReachableSlotMask = previewEvaluated
                ? previewReachableSlotMask
                : 0UL;
            this.previewSharedSpaceSlotMask = previewEvaluated
                ? previewSharedSpaceSlotMask
                : 0UL;
        }

        public string InstanceId => instanceId;
        public FoundationFacilityAccess CommittedAccess => committedAccess;
        public int InteractionSlotCount => interactionSlotCount;
        public bool PreviewEvaluated => previewEvaluated;
        public FoundationFacilityAccess PreviewAccess => previewAccess;
        public FoundationFacilityAccess DisplayAccess => previewEvaluated
            ? previewAccess
            : committedAccess;
        public int DisplayReachableSlotCount
        {
            get
            {
                var result = 0;
                for (var i = 0; i < interactionSlotCount; i++)
                {
                    if (IsDisplaySlotReachable(i)) result++;
                }
                return result;
            }
        }

        public bool IsDisplaySlotReachable(int index)
        {
            if (index < 0 || index >= interactionSlotCount) return false;
            ulong mask = previewEvaluated
                ? previewReachableSlotMask
                : committedReachableSlotMask;
            return (mask & (1UL << index)) != 0;
        }

        /// <summary>
        /// 当前视图下该候选位是否与另一个 InteractionGroup 共用人体空间。组内备选位本来就共享
        /// 功能容量，不作为建造冲突噪声；跨组接近才需要黄色提示。
        /// </summary>
        public bool IsDisplaySlotSharingSpace(int index)
        {
            if (index < 0 || index >= interactionSlotCount) return false;
            ulong mask = previewEvaluated
                ? previewSharedSpaceSlotMask
                : committedSharedSpaceSlotMask;
            return (mask & (1UL << index)) != 0;
        }

        public bool IsDisplaySlotOccupied(int index) =>
            index >= 0 && index < interactionSlotCount &&
            (committedOccupiedSlotMask & (1UL << index)) != 0;

        public bool Equals(FoundationFacilityAccessState other) =>
            string.Equals(instanceId, other.instanceId, StringComparison.Ordinal) &&
            committedAccess == other.committedAccess &&
            committedReachableSlotMask == other.committedReachableSlotMask &&
            committedSharedSpaceSlotMask == other.committedSharedSpaceSlotMask &&
            committedOccupiedSlotMask == other.committedOccupiedSlotMask &&
            interactionSlotCount == other.interactionSlotCount &&
            previewEvaluated == other.previewEvaluated &&
            previewAccess == other.previewAccess &&
            previewReachableSlotMask == other.previewReachableSlotMask &&
            previewSharedSpaceSlotMask == other.previewSharedSpaceSlotMask;

        public override bool Equals(object obj) =>
            obj is FoundationFacilityAccessState other && Equals(other);

        public override int GetHashCode()
        {
            int committedHash = HashCode.Combine(
                instanceId,
                committedAccess,
                committedReachableSlotMask,
                committedSharedSpaceSlotMask,
                committedOccupiedSlotMask);
            return HashCode.Combine(
                committedHash,
                interactionSlotCount,
                previewEvaluated,
                previewAccess,
                previewReachableSlotMask,
                previewSharedSpaceSlotMask);
        }
    }

    /// <summary>当前建造幽灵的只读状态；姿态是连续真值，Failure 同时表达几何与导航事务结果。</summary>
    [Serializable]
    public struct FoundationPlacementPreviewState : IEquatable<FoundationPlacementPreviewState>
    {
        [SerializeField] private bool active;
        [SerializeField] private string definitionId;
        [SerializeField] private int xMillimeters;
        [SerializeField] private int zMillimeters;
        [SerializeField] private int yawDeciDegrees;
        [SerializeField] private int deckLevel;
        [SerializeField] private FoundationPlacementFailure failure;
        [SerializeField] private ulong reachableInteractionSlotMask;
        [SerializeField] private ulong sharedSpaceInteractionSlotMask;
        [SerializeField] private int interactionSlotCount;
        [SerializeField] private bool realtimeReachabilityEvaluated;
        [SerializeField] private float realtimeProbeMilliseconds;
        [SerializeField] private int realtimeVisitedCells;
        [SerializeField] private int realtimeProbeCellCount;

        public FoundationPlacementPreviewState(
            bool active,
            string definitionId,
            in DeckPose pose,
            FoundationPlacementFailure failure,
            ulong reachableInteractionSlotMask,
            ulong sharedSpaceInteractionSlotMask,
            int interactionSlotCount,
            bool realtimeReachabilityEvaluated,
            float realtimeProbeMilliseconds,
            int realtimeVisitedCells,
            int realtimeProbeCellCount)
        {
            this.active = active;
            this.definitionId = definitionId ?? string.Empty;
            xMillimeters = pose.XMillimeters;
            zMillimeters = pose.ZMillimeters;
            yawDeciDegrees = pose.YawDeciDegrees;
            deckLevel = pose.DeckLevel;
            this.failure = failure;
            this.reachableInteractionSlotMask = reachableInteractionSlotMask;
            this.sharedSpaceInteractionSlotMask = sharedSpaceInteractionSlotMask;
            this.interactionSlotCount = Math.Clamp(interactionSlotCount, 0, 64);
            this.realtimeReachabilityEvaluated = realtimeReachabilityEvaluated;
            this.realtimeProbeMilliseconds = Math.Max(0f, realtimeProbeMilliseconds);
            this.realtimeVisitedCells = Math.Max(0, realtimeVisitedCells);
            this.realtimeProbeCellCount = Math.Max(0, realtimeProbeCellCount);
        }

        public bool Active => active;
        public string DefinitionId => definitionId;
        public DeckPose Pose => new(xMillimeters, zMillimeters, yawDeciDegrees, deckLevel);
        public FoundationPlacementFailure Failure => failure;
        /// <summary>
        /// 可达性是可修复的软警告：玩家可先放下不可用设施，再通过移位或拆除障碍修通。
        /// 几何、身份、事务与 NavMesh 更新错误仍是硬失败。
        /// </summary>
        public bool CanConfirm => active &&
            failure is FoundationPlacementFailure.None or
                FoundationPlacementFailure.RequiredInteractionUnreachable;
        public bool HasReachabilityWarning =>
            failure == FoundationPlacementFailure.RequiredInteractionUnreachable;
        public int InteractionSlotCount => interactionSlotCount;
        public bool RealtimeReachabilityEvaluated => realtimeReachabilityEvaluated;
        public float RealtimeProbeMilliseconds => realtimeProbeMilliseconds;
        public int RealtimeVisitedCells => realtimeVisitedCells;
        public int RealtimeProbeCellCount => realtimeProbeCellCount;

        public int ReachableInteractionSlotCount
        {
            get
            {
                var result = 0;
                for (var i = 0; i < interactionSlotCount; i++)
                {
                    if (IsInteractionSlotReachable(i)) result++;
                }
                return result;
            }
        }

        public bool IsInteractionSlotReachable(int index) =>
            index >= 0 && index < interactionSlotCount &&
            (reachableInteractionSlotMask & (1UL << index)) != 0;

        public bool IsInteractionSlotSharingSpace(int index) =>
            index >= 0 && index < interactionSlotCount &&
            (sharedSpaceInteractionSlotMask & (1UL << index)) != 0;

        public bool Equals(FoundationPlacementPreviewState other) =>
            active == other.active &&
            string.Equals(definitionId, other.definitionId, StringComparison.Ordinal) &&
            xMillimeters == other.xMillimeters &&
            zMillimeters == other.zMillimeters &&
            yawDeciDegrees == other.yawDeciDegrees &&
            deckLevel == other.deckLevel &&
            failure == other.failure &&
            reachableInteractionSlotMask == other.reachableInteractionSlotMask &&
            sharedSpaceInteractionSlotMask == other.sharedSpaceInteractionSlotMask &&
            interactionSlotCount == other.interactionSlotCount &&
            realtimeReachabilityEvaluated == other.realtimeReachabilityEvaluated &&
            realtimeProbeMilliseconds.Equals(other.realtimeProbeMilliseconds) &&
            realtimeVisitedCells == other.realtimeVisitedCells &&
            realtimeProbeCellCount == other.realtimeProbeCellCount;

        public override bool Equals(object obj) =>
            obj is FoundationPlacementPreviewState other && Equals(other);

        public override int GetHashCode()
        {
            int identityHash = HashCode.Combine(
                active,
                definitionId,
                xMillimeters,
                zMillimeters,
                yawDeciDegrees,
                deckLevel,
                failure,
                reachableInteractionSlotMask);
            return HashCode.Combine(
                identityHash,
                sharedSpaceInteractionSlotMask,
                interactionSlotCount,
                realtimeReachabilityEvaluated,
                realtimeProbeMilliseconds,
                realtimeVisitedCells,
                realtimeProbeCellCount);
        }

        public static FoundationPlacementPreviewState Inactive => new(
            false,
            string.Empty,
            default,
            FoundationPlacementFailure.None,
            0UL,
            0UL,
            0,
            false,
            0f,
            0,
            0);
    }

    public readonly struct FoundationBuildOption
    {
        public FoundationBuildOption(string definitionId, string displayName, int shortcut)
        {
            DefinitionId = definitionId;
            DisplayName = displayName;
            Shortcut = shortcut;
        }

        public string DefinitionId { get; }
        public string DisplayName { get; }
        public int Shortcut { get; }
    }
}
