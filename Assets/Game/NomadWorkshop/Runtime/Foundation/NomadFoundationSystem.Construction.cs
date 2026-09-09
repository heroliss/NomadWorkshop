using System;
using System.Collections.Generic;
using Game.NomadWorkshop.Simulation;
using Game.NomadWorkshop.Simulation.Persistence;

namespace Game.NomadWorkshop.Foundation
{
    public sealed partial class NomadFoundationSystem
    {
        private void RefreshConstructionBlueprintProjection()
        {
            _constructionBlueprintProjection.Clear();
            if (_constructionBlueprintLedger != null)
            {
                IReadOnlyList<NomadConstructionBlueprintCheckpoint> checkpoints =
                    _constructionBlueprintLedger.GetCheckpointSnapshot();
                for (var i = 0; i < checkpoints.Count; i++)
                    _constructionBlueprintProjection.Add(
                        new FoundationConstructionBlueprintState(checkpoints[i]));
            }
            _constructionBlueprintProjection.Sort((left, right) =>
                string.CompareOrdinal(left.InstanceId, right.InstanceId));
            _model.ReplaceConstructionBlueprints(_constructionBlueprintProjection);
        }

        private void CaptureConstructionBlueprints(NomadWorkshopSaveData data)
        {
            if (_constructionBlueprintLedger == null) return;
            IReadOnlyList<NomadConstructionBlueprintCheckpoint> checkpoints =
                _constructionBlueprintLedger.GetCheckpointSnapshot();
            for (var i = 0; i < checkpoints.Count; i++)
            {
                NomadConstructionBlueprintCheckpoint checkpoint = checkpoints[i];
                var saved = new NomadBlueprintSaveData
                {
                    InstanceId = checkpoint.Placement.InstanceId,
                    DefinitionId = checkpoint.Placement.DefinitionId,
                    Pose = QuantizedDeckPose.FromDeckPose(checkpoint.Placement.Pose),
                    Stage = ToSaveStage(checkpoint.Stage),
                    ConstructionProgressPermille = checkpoint.RequiredWorkUnits <= 0
                        ? 0
                        : checked((int)((long)checkpoint.CompletedWorkUnits * 1000L /
                                       checkpoint.RequiredWorkUnits)),
                    MaterialsCommitted = checkpoint.InstalledMaterials.Count > 0,
                    SiteKind = checkpoint.Site.Kind,
                    SiteId = checkpoint.Site.SiteId,
                    RouteId = checkpoint.Site.RouteId,
                    RouteProgressMillimeters = checkpoint.Site.RouteProgressMillimeters,
                    RetentionDistanceMillimeters = checkpoint.Site.RetentionDistanceMillimeters,
                    RequiredWorkUnits = checkpoint.RequiredWorkUnits,
                    CompletedWorkUnits = checkpoint.CompletedWorkUnits,
                };
                AddMaterialStacks(saved.RequiredMaterials, checkpoint.RequiredMaterials,
                    saved.InstanceId + "/required/");
                AddResourceStacks(saved.StagedMaterials, checkpoint.StagedMaterials,
                    saved.InstanceId + "/staged/");
                AddResourceStacks(saved.InstalledMaterials, checkpoint.InstalledMaterials,
                    saved.InstanceId + "/installed/");
                data.Blueprints.Add(saved);
            }
        }

        private void RestoreConstructionBlueprints(
            IReadOnlyList<NomadBlueprintSaveData> savedBlueprints)
        {
            if (savedBlueprints == null || savedBlueprints.Count == 0)
            {
                RefreshConstructionBlueprintProjection();
                return;
            }
            if (_constructionBlueprintLedger == null)
                throw new InvalidOperationException("Foundation 蓝图账本尚未初始化。");

            for (var i = 0; i < savedBlueprints.Count; i++)
            {
                NomadBlueprintSaveData saved = savedBlueprints[i];
                if (string.IsNullOrWhiteSpace(saved.SiteId))
                    throw new NotSupportedException(
                        $"蓝图 {saved.InstanceId} 缺少 N2-B 地点检查点，不能猜测其恢复位置。");
                if (!_definitions.TryGetValue(saved.DefinitionId, out NomadFacilityDefinition definition))
                    throw new NotSupportedException(
                        $"蓝图 {saved.InstanceId} 使用当前版本不存在的定义 {saved.DefinitionId}。");

                var site = new NomadConstructionSite(
                    saved.SiteId,
                    saved.SiteKind,
                    saved.RouteId,
                    saved.RouteProgressMillimeters,
                    saved.RetentionDistanceMillimeters);
                ContinuousFacilityPlacementRequest placement = new(
                    saved.InstanceId,
                    saved.DefinitionId,
                    saved.Pose.ToDeckPose(),
                    definition.CreateFootprint(),
                    definition.CreateFunctionalClearanceFootprint());
                IReadOnlyList<NomadConstructionMaterialRequirement> requirements =
                    ToRequirements(saved.RequiredMaterials);
                NomadConstructionStage stage = ToConstructionStage(saved.Stage);
                if (!_constructionBlueprintLedger.TryRestore(
                        placement,
                        site,
                        requirements,
                        saved.RequiredWorkUnits,
                        saved.CompletedWorkUnits,
                        stage,
                        ToQuantities(saved.StagedMaterials),
                        ToQuantities(saved.InstalledMaterials),
                        out _,
                        out NomadConstructionPlanFailure failure,
                        out ContinuousPlacementFailure placementFailure))
                    throw new InvalidOperationException(
                        $"蓝图 {saved.InstanceId} 无法恢复：{failure} / {placementFailure}。");

                _navigationObstacles.Add(
                    saved.InstanceId,
                    _navigation.CreateFacilityObstacle(
                        saved.InstanceId,
                        placement.Pose,
                        placement.Footprint));
            }
            RefreshConstructionBlueprintProjection();
        }

        private void ValidateConstructionBlueprintsForRestore(
            IReadOnlyList<NomadBlueprintSaveData> savedBlueprints,
            ContinuousFacilityPlacementLedger validationPlacements)
        {
            if (savedBlueprints == null || savedBlueprints.Count == 0) return;
            var ledger = new NomadConstructionBlueprintLedger(
                validationPlacements,
                new ResourceFlowLedger());
            for (var i = 0; i < savedBlueprints.Count; i++)
            {
                NomadBlueprintSaveData saved = savedBlueprints[i];
                if (string.IsNullOrWhiteSpace(saved.SiteId))
                    throw new NotSupportedException(
                        $"蓝图 {saved.InstanceId} 缺少 N2-B 地点检查点，不能猜测其恢复位置。");
                if (!_definitions.TryGetValue(saved.DefinitionId, out NomadFacilityDefinition definition))
                    throw new NotSupportedException(
                        $"蓝图 {saved.InstanceId} 使用当前版本不存在的定义 {saved.DefinitionId}。");

                var site = new NomadConstructionSite(
                    saved.SiteId,
                    saved.SiteKind,
                    saved.RouteId,
                    saved.RouteProgressMillimeters,
                    saved.RetentionDistanceMillimeters);
                var placement = new ContinuousFacilityPlacementRequest(
                    saved.InstanceId,
                    saved.DefinitionId,
                    saved.Pose.ToDeckPose(),
                    definition.CreateFootprint(),
                    definition.CreateFunctionalClearanceFootprint());
                if (!ledger.TryRestore(
                        placement,
                        site,
                        ToRequirements(saved.RequiredMaterials),
                        saved.RequiredWorkUnits,
                        saved.CompletedWorkUnits,
                        ToConstructionStage(saved.Stage),
                        ToQuantities(saved.StagedMaterials),
                        ToQuantities(saved.InstalledMaterials),
                        out _,
                        out NomadConstructionPlanFailure failure,
                        out ContinuousPlacementFailure placementFailure))
                    throw new InvalidOperationException(
                        $"检查点蓝图 {saved.InstanceId} 无效：{failure} / {placementFailure}。");
            }
        }

        private static NomadBlueprintSaveStage ToSaveStage(NomadConstructionStage stage) => stage switch
        {
            NomadConstructionStage.Planned or NomadConstructionStage.AwaitingMaterials =>
                NomadBlueprintSaveStage.AwaitingMaterials,
            NomadConstructionStage.ReadyToBuild => NomadBlueprintSaveStage.ReadyToBuild,
            NomadConstructionStage.Complete => NomadBlueprintSaveStage.Complete,
            _ => throw new InvalidOperationException($"不能保存蓝图阶段 {stage}。"),
        };

        private static NomadConstructionStage ToConstructionStage(NomadBlueprintSaveStage stage) => stage switch
        {
            NomadBlueprintSaveStage.PendingValidation or NomadBlueprintSaveStage.AwaitingMaterials =>
                NomadConstructionStage.AwaitingMaterials,
            NomadBlueprintSaveStage.ReadyToBuild => NomadConstructionStage.ReadyToBuild,
            NomadBlueprintSaveStage.Complete => NomadConstructionStage.Complete,
            _ => throw new NotSupportedException($"不能恢复蓝图阶段 {stage}。"),
        };

        private static IReadOnlyList<NomadConstructionMaterialRequirement> ToRequirements(
            IReadOnlyList<NomadResourceStackSaveData> materials)
        {
            if (materials == null || materials.Count == 0)
                throw new InvalidOperationException("蓝图缺少必需材料配方。");
            var result = new NomadConstructionMaterialRequirement[materials.Count];
            for (var i = 0; i < result.Length; i++)
            {
                NomadResourceStackSaveData material = materials[i];
                result[i] = new NomadConstructionMaterialRequirement(
                    new ResourceId(material.ResourceId, material.Measure),
                    material.AmountBaseUnits);
            }
            return result;
        }

        private static IReadOnlyList<ResourceQuantity> ToQuantities(
            IReadOnlyList<NomadResourceStackSaveData> materials)
        {
            if (materials == null || materials.Count == 0)
                return Array.Empty<ResourceQuantity>();
            var result = new ResourceQuantity[materials.Count];
            for (var i = 0; i < result.Length; i++)
            {
                NomadResourceStackSaveData material = materials[i];
                result[i] = new ResourceQuantity(
                    new ResourceId(material.ResourceId, material.Measure),
                    material.AmountBaseUnits);
            }
            return result;
        }

        private static void AddMaterialStacks(
            List<NomadResourceStackSaveData> destination,
            IReadOnlyList<NomadConstructionMaterialRequirement> materials,
            string idPrefix)
        {
            for (var i = 0; i < materials.Count; i++)
            {
                NomadConstructionMaterialRequirement material = materials[i];
                destination.Add(new NomadResourceStackSaveData
                {
                    StackId = idPrefix + i.ToString("D2"),
                    ResourceId = material.Resource.Value,
                    Measure = material.Resource.Measure,
                    AmountBaseUnits = material.Amount,
                    ConditionPermille = 1000,
                });
            }
        }

        private static void AddResourceStacks(
            List<NomadResourceStackSaveData> destination,
            IReadOnlyList<ResourceQuantity> materials,
            string idPrefix)
        {
            for (var i = 0; i < materials.Count; i++)
            {
                ResourceQuantity material = materials[i];
                destination.Add(new NomadResourceStackSaveData
                {
                    StackId = idPrefix + i.ToString("D2"),
                    ResourceId = material.Resource.Value,
                    Measure = material.Resource.Measure,
                    AmountBaseUnits = material.Amount,
                    ConditionPermille = 1000,
                });
            }
        }
    }
}
