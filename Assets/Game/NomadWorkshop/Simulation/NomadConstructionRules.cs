using System;
using System.Collections.Generic;

namespace Game.NomadWorkshop.Simulation
{
    /// <summary>蓝图从规划到完成的纯规则阶段。</summary>
    public enum NomadConstructionStage
    {
        Planned,
        AwaitingMaterials,
        ReadyToBuild,
        Building,
        Commissioning,
        Complete,
        Cancelled,
        Demolished,
    }

    public enum NomadConstructionSiteKind
    {
        PermanentNode,
        TemporaryRoadside,
        VehicleMounted,
    }

    public enum NomadConstructionPlanFailure
    {
        None,
        InvalidRequest,
        DuplicateInstanceId,
        PlacementRejected,
        InvalidMaterials,
        InvalidWork,
    }

    public enum NomadConstructionActionFailure
    {
        None,
        UnknownBlueprint,
        InvalidStage,
        MaterialsMissing,
        MaterialsBusy,
        NoProgress,
    }

    /// <summary>规划点所属的持久化范围；路线进度使用毫米，避免用 Unity Transform 作为存档真值。</summary>
    public readonly struct NomadConstructionSite
    {
        public NomadConstructionSite(
            string siteId,
            NomadConstructionSiteKind kind,
            string routeId = "",
            long routeProgressMillimeters = 0,
            long retentionDistanceMillimeters = 0)
        {
            if (string.IsNullOrWhiteSpace(siteId))
                throw new ArgumentException("地点 id 不能为空。", nameof(siteId));
            if (!Enum.IsDefined(typeof(NomadConstructionSiteKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (routeProgressMillimeters < 0)
                throw new ArgumentOutOfRangeException(nameof(routeProgressMillimeters));
            if (retentionDistanceMillimeters < 0)
                throw new ArgumentOutOfRangeException(nameof(retentionDistanceMillimeters));
            if (kind == NomadConstructionSiteKind.TemporaryRoadside &&
                string.IsNullOrWhiteSpace(routeId))
                throw new ArgumentException("途中临时地点必须绑定路线。", nameof(routeId));

            SiteId = siteId.Trim();
            Kind = kind;
            RouteId = routeId?.Trim() ?? string.Empty;
            RouteProgressMillimeters = routeProgressMillimeters;
            RetentionDistanceMillimeters = retentionDistanceMillimeters;
        }

        public string SiteId { get; }
        public NomadConstructionSiteKind Kind { get; }
        public string RouteId { get; }
        public long RouteProgressMillimeters { get; }
        public long RetentionDistanceMillimeters { get; }
        public bool IsValid => !string.IsNullOrEmpty(SiteId) &&
            Enum.IsDefined(typeof(NomadConstructionSiteKind), Kind);
    }

    public enum NomadSiteRetentionDecision
    {
        Keep,
        Retire,
    }

    public static class NomadConstructionLifecycle
    {
        /// <summary>
        /// 永久节点与车载设施不因旅途进度清理；临时路边设施只在距离阈值内可回访。
        /// 恰好位于阈值上仍保留。本方法只给出判定，站点所有者负责提交一次清理及保留退役标记。
        /// </summary>
        public static NomadSiteRetentionDecision Evaluate(
            in NomadConstructionSite site,
            string currentRouteId,
            long currentRouteProgressMillimeters)
        {
            if (!site.IsValid) throw new ArgumentException("地点未初始化。", nameof(site));
            if (currentRouteProgressMillimeters < 0)
                throw new ArgumentOutOfRangeException(nameof(currentRouteProgressMillimeters));
            if (site.Kind != NomadConstructionSiteKind.TemporaryRoadside)
                return NomadSiteRetentionDecision.Keep;
            if (!string.Equals(site.RouteId, currentRouteId, StringComparison.Ordinal))
                throw new ArgumentException("不同路线的进度不可用于距离清理。", nameof(currentRouteId));

            long distance = Math.Abs(currentRouteProgressMillimeters - site.RouteProgressMillimeters);
            return distance <= site.RetentionDistanceMillimeters
                ? NomadSiteRetentionDecision.Keep
                : NomadSiteRetentionDecision.Retire;
        }
    }

    public readonly struct NomadConstructionMaterialRequirement
    {
        public NomadConstructionMaterialRequirement(ResourceId resource, int amount)
        {
            if (!resource.IsValid)
                throw new ArgumentException("建材资源 id 无效。", nameof(resource));
            if (resource.Measure != ResourceMeasure.Item)
                throw new ArgumentException("首版建材使用 Item 计量。", nameof(resource));
            if (amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            Resource = resource;
            Amount = amount;
        }

        public ResourceId Resource { get; }
        public int Amount { get; }
    }

    /// <summary>取消或拆除后留下的真实材料批次；它应继续由世界物品或新库存拥有。</summary>
    public sealed class NomadConstructionRecovery
    {
        internal NomadConstructionRecovery(
            string sourceBlueprintId,
            NomadConstructionSite site,
            IReadOnlyList<ResourceQuantity> materials)
        {
            SourceBlueprintId = sourceBlueprintId;
            Site = site;
            var copy = new ResourceQuantity[materials.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = materials[i];
            Materials = Array.AsReadOnly(copy);
        }

        public string SourceBlueprintId { get; }
        public NomadConstructionSite Site { get; }
        public IReadOnlyList<ResourceQuantity> Materials { get; }
    }

    /// <summary>
    /// 规划后的蓝图。材料先进入独立 ConstructionStaging 库存，施工完成前不会变成可用设施。
    /// </summary>
    public sealed class NomadConstructionBlueprint
    {
        private readonly Dictionary<ResourceId, int> _requirements;
        private readonly ResourceFlowLedger _flow;
        private readonly ResourceInventory _staging;
        private readonly ResourceInventory _installed;
        private ProcessTaskLease _buildLease;

        internal NomadConstructionBlueprint(
            ContinuousFacilityPlacementRequest placement,
            NomadConstructionSite site,
            IReadOnlyList<NomadConstructionMaterialRequirement> requirements,
            int requiredWorkUnits,
            ResourceFlowLedger flow)
        {
            Placement = placement;
            Site = site;
            RequiredWorkUnits = requiredWorkUnits;
            _flow = flow;
            _requirements = new Dictionary<ResourceId, int>();
            int totalMaterials = 0;
            for (int i = 0; i < requirements.Count; i++)
            {
                NomadConstructionMaterialRequirement requirement = requirements[i];
                if (_requirements.ContainsKey(requirement.Resource))
                    throw new ArgumentException("同一种建材只能声明一次。", nameof(requirements));
                _requirements.Add(requirement.Resource, requirement.Amount);
                totalMaterials = checked(totalMaterials + requirement.Amount);
            }

            RequiredMaterials = Array.AsReadOnly(CopyRequirements(requirements));
            _staging = new ResourceInventory(
                $"blueprint:{placement.InstanceId}:staging",
                ResourceMeasure.Item,
                totalMaterials);
            _installed = new ResourceInventory(
                $"blueprint:{placement.InstanceId}:installed", ResourceMeasure.Item, totalMaterials);
            Stage = NomadConstructionStage.Planned;
            RefreshStage();
        }

        public ContinuousFacilityPlacementRequest Placement { get; }
        public NomadConstructionSite Site { get; }
        public IReadOnlyList<NomadConstructionMaterialRequirement> RequiredMaterials { get; }
        public int StagingCapacity => _staging.Capacity;
        public IReadOnlyList<ResourceQuantity> StagedMaterials => _staging.GetContentsSnapshot();
        public IReadOnlyList<ResourceQuantity> InstalledMaterials => _installed.GetContentsSnapshot();
        public int RequiredWorkUnits { get; }
        public int CompletedWorkUnits { get; private set; }
        public NomadConstructionStage Stage { get; private set; }

        public bool HasAllRequiredMaterials
        {
            get
            {
                foreach (KeyValuePair<ResourceId, int> requirement in _requirements)
                {
                    ResourceInventory inventory = Stage == NomadConstructionStage.Complete ? _installed : _staging;
                    if (inventory.GetAmount(requirement.Key) != requirement.Value)
                        return false;
                }
                return true;
            }
        }

        public int GetRequiredAmount(ResourceId resource)
            => _requirements.TryGetValue(resource, out int amount) ? amount : 0;

        public int GetStagedAmount(ResourceId resource) => _staging.GetAmount(resource);

        /// <summary>
        /// 通过共享资源账本取得入料租约；同一蓝图首版一次接收一批。错料、超额和已关闭蓝图属于调用错误。
        /// 取消蓝图前须结束入料租约；拾取后的货物仍归携带者，不会随蓝图销毁。
        /// </summary>
        public bool TryReserveDelivery(
            string taskId, ulong ownerId, ResourceInventory source, ResourceInventory carrier,
            ResourceId resource, int amount, out HaulTaskLease lease, out ResourceFlowBlocker blocker)
        {
            RefreshStage();
            if (Stage is not (NomadConstructionStage.AwaitingMaterials or NomadConstructionStage.ReadyToBuild))
                throw new InvalidOperationException("蓝图已停止收料。");
            if (!_requirements.TryGetValue(resource, out int required) || amount <= 0 ||
                amount > required - _staging.GetAmount(resource))
                throw new ArgumentException("送货必须匹配蓝图尚缺的材料与数量。", nameof(amount));
            return _flow.TryReserveHaul(new HaulTaskRequest(
                taskId, ownerId, source, carrier, _staging, resource, amount,
                "运送蓝图建材", new[] { $"blueprint:{Placement.InstanceId}:delivery" }), out lease, out blocker);
        }

        /// <summary>在材料搬运提交后刷新阶段；该方法只观察库存，不拥有搬运租约。</summary>
        public void RefreshStage()
        {
            if (Stage is NomadConstructionStage.Complete or NomadConstructionStage.Cancelled or
                NomadConstructionStage.Demolished or NomadConstructionStage.Building or
                NomadConstructionStage.Commissioning)
                return;
            Stage = HasAllRequiredMaterials
                ? NomadConstructionStage.ReadyToBuild
                : NomadConstructionStage.AwaitingMaterials;
        }

        public bool TryStartBuilding(ulong workerId, out NomadConstructionActionFailure failure)
        {
            RefreshStage();
            if (Stage != NomadConstructionStage.ReadyToBuild)
            {
                failure = HasAllRequiredMaterials
                    ? NomadConstructionActionFailure.InvalidStage
                    : NomadConstructionActionFailure.MaterialsMissing;
                return false;
            }

            if (_flow.HasReservations(_staging))
            {
                failure = NomadConstructionActionFailure.MaterialsBusy;
                return false;
            }
            var inputs = new List<InventoryResourceQuantity>();
            var outputs = new List<InventoryResourceQuantity>();
            foreach (NomadConstructionMaterialRequirement required in RequiredMaterials)
            {
                inputs.Add(new InventoryResourceQuantity(_staging, required.Resource, required.Amount));
                outputs.Add(new InventoryResourceQuantity(_installed, required.Resource, required.Amount));
            }
            if (!_flow.TryReserveProcess(new ProcessTaskRequest(
                    $"blueprint:{Placement.InstanceId}:build", workerId, "原地组装设施", inputs, outputs,
                    new[] { $"blueprint:{Placement.InstanceId}:work" }), out _buildLease, out _))
            {
                failure = NomadConstructionActionFailure.MaterialsBusy;
                return false;
            }
            Stage = NomadConstructionStage.Building;
            failure = NomadConstructionActionFailure.None;
            return true;
        }

        public bool TryAdvanceWork(int workUnits, out NomadConstructionActionFailure failure)
        {
            if (Stage != NomadConstructionStage.Building)
            {
                failure = NomadConstructionActionFailure.InvalidStage;
                return false;
            }
            if (workUnits <= 0)
            {
                failure = NomadConstructionActionFailure.NoProgress;
                return false;
            }

            CompletedWorkUnits += Math.Min(workUnits, RequiredWorkUnits - CompletedWorkUnits);
            if (CompletedWorkUnits == RequiredWorkUnits)
                Stage = NomadConstructionStage.Commissioning;
            failure = NomadConstructionActionFailure.None;
            return true;
        }

        public bool TryCommission(out NomadConstructionActionFailure failure)
        {
            if (Stage != NomadConstructionStage.Commissioning || !HasAllRequiredMaterials)
            {
                failure = NomadConstructionActionFailure.InvalidStage;
                return false;
            }

            _buildLease.Commit();
            _buildLease = null;
            Stage = NomadConstructionStage.Complete;
            failure = NomadConstructionActionFailure.None;
            return true;
        }

        internal NomadConstructionRecovery Cancel()
        {
            if (Stage is NomadConstructionStage.Complete or NomadConstructionStage.Cancelled or
                NomadConstructionStage.Demolished)
                throw new InvalidOperationException($"蓝图 {Placement.InstanceId} 不能取消。 ");
            _buildLease?.Dispose();
            _buildLease = null;
            Stage = NomadConstructionStage.Cancelled;
            return RecoverMaterials();
        }

        internal NomadConstructionRecovery Demolish()
        {
            if (Stage != NomadConstructionStage.Complete)
                throw new InvalidOperationException($"蓝图 {Placement.InstanceId} 尚未完成，不能按成品拆除。 ");
            Stage = NomadConstructionStage.Demolished;
            return RecoverMaterials();
        }

        internal bool HasPendingDelivery => _flow.HasReservations(_staging) && _buildLease == null;

        private NomadConstructionRecovery RecoverMaterials()
            => new(
                Placement.InstanceId,
                Site,
                (Stage == NomadConstructionStage.Demolished ? _installed : _staging).TakeAll());

        private static NomadConstructionMaterialRequirement[] CopyRequirements(
            IReadOnlyList<NomadConstructionMaterialRequirement> requirements)
        {
            var result = new NomadConstructionMaterialRequirement[requirements.Count];
            for (int i = 0; i < result.Length; i++) result[i] = requirements[i];
            return result;
        }
    }

    /// <summary>
    /// 蓝图与连续占地的共同所有者。计划和取消只改写一份空间真值，避免蓝图先占位、完成后又重复落地。
    /// </summary>
    public sealed class NomadConstructionBlueprintLedger
    {
        private readonly ContinuousFacilityPlacementLedger _placements;
        private readonly ResourceFlowLedger _flow;
        private readonly Dictionary<string, NomadConstructionBlueprint> _blueprints =
            new(StringComparer.Ordinal);

        public NomadConstructionBlueprintLedger(
            ContinuousFacilityPlacementLedger placements, ResourceFlowLedger flow)
        {
            _placements = placements ?? throw new ArgumentNullException(nameof(placements));
            _flow = flow ?? throw new ArgumentNullException(nameof(flow));
        }

        public int Count => _blueprints.Count;

        public bool TryPlan(
            in ContinuousFacilityPlacementRequest placement,
            in NomadConstructionSite site,
            IReadOnlyList<NomadConstructionMaterialRequirement> requirements,
            int requiredWorkUnits,
            out NomadConstructionBlueprint blueprint,
            out NomadConstructionPlanFailure failure,
            out ContinuousPlacementFailure placementFailure)
        {
            blueprint = null;
            placementFailure = ContinuousPlacementFailure.None;
            if (string.IsNullOrWhiteSpace(placement.InstanceId) ||
                string.IsNullOrWhiteSpace(placement.DefinitionId) ||
                placement.Footprint == null ||
                !site.IsValid ||
                requirements == null || requirements.Count == 0)
            {
                failure = NomadConstructionPlanFailure.InvalidRequest;
                return false;
            }
            if (_blueprints.ContainsKey(placement.InstanceId))
            {
                failure = NomadConstructionPlanFailure.DuplicateInstanceId;
                return false;
            }
            if (requiredWorkUnits <= 0)
            {
                failure = NomadConstructionPlanFailure.InvalidWork;
                return false;
            }

            try
            {
                for (int i = 0; i < requirements.Count; i++)
                {
                    if (!requirements[i].Resource.IsValid || requirements[i].Amount <= 0 ||
                        requirements[i].Resource.Measure != ResourceMeasure.Item)
                    {
                        failure = NomadConstructionPlanFailure.InvalidMaterials;
                        return false;
                    }
                }

                var candidate = new NomadConstructionBlueprint(
                    placement, site, requirements, requiredWorkUnits, _flow);
                if (!_placements.TryPlace(placement, out _, out placementFailure))
                {
                    failure = NomadConstructionPlanFailure.PlacementRejected;
                    return false;
                }

                blueprint = candidate;
                _blueprints.Add(placement.InstanceId, blueprint);
                failure = NomadConstructionPlanFailure.None;
                return true;
            }
            catch (ArgumentException)
            {
                failure = NomadConstructionPlanFailure.InvalidMaterials;
                return false;
            }
            catch (OverflowException)
            {
                failure = NomadConstructionPlanFailure.InvalidMaterials;
                return false;
            }
        }

        public bool TryGet(string instanceId, out NomadConstructionBlueprint blueprint)
            => _blueprints.TryGetValue(instanceId ?? string.Empty, out blueprint);

        public bool TryCancel(
            string instanceId,
            out NomadConstructionRecovery recovery,
            out NomadConstructionActionFailure failure)
        {
            recovery = null;
            if (!_blueprints.TryGetValue(instanceId ?? string.Empty, out NomadConstructionBlueprint blueprint))
            {
                failure = NomadConstructionActionFailure.UnknownBlueprint;
                return false;
            }
            if (blueprint.Stage == NomadConstructionStage.Complete)
            {
                failure = NomadConstructionActionFailure.InvalidStage;
                return false;
            }
            if (blueprint.HasPendingDelivery)
            {
                failure = NomadConstructionActionFailure.MaterialsBusy;
                return false;
            }

            recovery = blueprint.Cancel();
            _placements.Remove(instanceId);
            _blueprints.Remove(instanceId);
            failure = NomadConstructionActionFailure.None;
            return true;
        }

        public bool TryDemolish(
            string instanceId,
            out NomadConstructionRecovery recovery,
            out NomadConstructionActionFailure failure)
        {
            recovery = null;
            if (!_blueprints.TryGetValue(instanceId ?? string.Empty, out NomadConstructionBlueprint blueprint))
            {
                failure = NomadConstructionActionFailure.UnknownBlueprint;
                return false;
            }
            if (blueprint.Stage != NomadConstructionStage.Complete)
            {
                failure = NomadConstructionActionFailure.InvalidStage;
                return false;
            }

            recovery = blueprint.Demolish();
            _placements.Remove(instanceId);
            _blueprints.Remove(instanceId);
            failure = NomadConstructionActionFailure.None;
            return true;
        }
    }
}
