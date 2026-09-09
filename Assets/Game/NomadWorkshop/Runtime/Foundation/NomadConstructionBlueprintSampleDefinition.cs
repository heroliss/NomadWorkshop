using System;
using System.Collections.Generic;
using Game.NomadWorkshop.Simulation;
using Game.NomadWorkshop.Simulation.Persistence;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>
    /// 由 Foundation Editor 管线生成的可重建蓝图样板。它是数据入口，不会自动把未完成设施
    /// 注入正式起点；教程或运行时接线可将其转换为同一份版本化蓝图 DTO。
    /// </summary>
    [CreateAssetMenu(
        fileName = "NW_BlueprintSample",
        menuName = "SSFramework/游牧工坊/蓝图样板")]
    public sealed class NomadConstructionBlueprintSampleDefinition : ScriptableObject
    {
        [SerializeField] private string blueprintId = "blueprint-kitchen-n2b";
        [SerializeField] private NomadFacilityDefinition facilityDefinition;
        [SerializeField] private Vector2 positionMeters = new(0f, -2f);
        [SerializeField] private float yawDegrees;
        [SerializeField] private NomadConstructionSiteKind siteKind =
            NomadConstructionSiteKind.VehicleMounted;
        [SerializeField] private string siteId = "vehicle-01";
        [SerializeField] private string routeId = string.Empty;
        [SerializeField] private long routeProgressMillimeters;
        [SerializeField] private long retentionDistanceMillimeters;
        [SerializeField, Min(1)] private int requiredWorkUnits = 12;
        [SerializeField] private NomadConstructionMaterialSample[] requiredMaterials =
        {
            new("steel", 2),
        };

        public string BlueprintId => blueprintId;
        public NomadFacilityDefinition FacilityDefinition => facilityDefinition;
        public DeckPose Pose => DeckPose.FromMeters(positionMeters.x, positionMeters.y, yawDegrees);
        public NomadConstructionSite Site => new(
            siteId,
            siteKind,
            routeId,
            routeProgressMillimeters,
            retentionDistanceMillimeters);
        public int RequiredWorkUnits => requiredWorkUnits;
        public IReadOnlyList<NomadConstructionMaterialSample> RequiredMaterials => requiredMaterials;

        public NomadBlueprintSaveData CreateSaveData()
        {
            var result = new NomadBlueprintSaveData
            {
                InstanceId = blueprintId,
                DefinitionId = facilityDefinition == null ? string.Empty : facilityDefinition.Id,
                Pose = QuantizedDeckPose.FromDeckPose(Pose),
                Stage = NomadBlueprintSaveStage.AwaitingMaterials,
                SiteKind = siteKind,
                SiteId = siteId,
                RouteId = routeId,
                RouteProgressMillimeters = routeProgressMillimeters,
                RetentionDistanceMillimeters = retentionDistanceMillimeters,
                RequiredWorkUnits = requiredWorkUnits,
            };
            for (var i = 0; i < requiredMaterials.Length; i++)
            {
                NomadConstructionMaterialSample material = requiredMaterials[i];
                result.RequiredMaterials.Add(new NomadResourceStackSaveData
                {
                    StackId = blueprintId + "/required/" + i.ToString("D2"),
                    ResourceId = material.ResourceId,
                    Measure = ResourceMeasure.Item,
                    AmountBaseUnits = material.Amount,
                    ConditionPermille = 1000,
                });
            }
            return result;
        }

#if UNITY_EDITOR
        public void ConfigureForEditor(
            string configuredBlueprintId,
            NomadFacilityDefinition configuredFacilityDefinition,
            Vector2 configuredPositionMeters,
            float configuredYawDegrees,
            NomadConstructionSiteKind configuredSiteKind,
            string configuredSiteId,
            string configuredRouteId,
            long configuredRouteProgressMillimeters,
            long configuredRetentionDistanceMillimeters,
            int configuredRequiredWorkUnits,
            NomadConstructionMaterialSample[] configuredRequiredMaterials)
        {
            blueprintId = configuredBlueprintId?.Trim() ?? string.Empty;
            facilityDefinition = configuredFacilityDefinition;
            positionMeters = configuredPositionMeters;
            yawDegrees = configuredYawDegrees;
            siteKind = configuredSiteKind;
            siteId = configuredSiteId?.Trim() ?? string.Empty;
            routeId = configuredRouteId?.Trim() ?? string.Empty;
            routeProgressMillimeters = configuredRouteProgressMillimeters;
            retentionDistanceMillimeters = configuredRetentionDistanceMillimeters;
            requiredWorkUnits = Mathf.Max(1, configuredRequiredWorkUnits);
            requiredMaterials = configuredRequiredMaterials ?? Array.Empty<NomadConstructionMaterialSample>();
            OnValidate();
            EditorUtility.SetDirty(this);
        }
#endif

        private void OnValidate()
        {
            blueprintId = blueprintId?.Trim() ?? string.Empty;
            siteId = siteId?.Trim() ?? string.Empty;
            routeId = routeId?.Trim() ?? string.Empty;
            requiredWorkUnits = Mathf.Max(1, requiredWorkUnits);
            routeProgressMillimeters = Math.Max(0L, routeProgressMillimeters);
            retentionDistanceMillimeters = Math.Max(0L, retentionDistanceMillimeters);
            if (!float.IsFinite(yawDegrees)) yawDegrees = 0f;
            requiredMaterials ??= Array.Empty<NomadConstructionMaterialSample>();
            for (var i = 0; i < requiredMaterials.Length; i++)
                requiredMaterials[i].Sanitize();
        }
    }

    [Serializable]
    public struct NomadConstructionMaterialSample
    {
        [SerializeField] private string resourceId;
        [SerializeField, Min(1)] private int amount;

        public NomadConstructionMaterialSample(string resourceId, int amount)
        {
            this.resourceId = resourceId?.Trim() ?? string.Empty;
            this.amount = Math.Max(1, amount);
        }

        public string ResourceId => resourceId;
        public int Amount => amount;

        internal void Sanitize()
        {
            resourceId = resourceId?.Trim() ?? string.Empty;
            amount = Math.Max(1, amount);
        }
    }
}
