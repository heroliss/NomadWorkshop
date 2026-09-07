using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.NomadWorkshop.Foundation;
using UnityEditor;
using UnityEngine;

namespace Game.NomadWorkshop.Editor
{
    /// <summary>自有水设施的侧面接触位生成与窄范围迁移；保留原停放区域、资产身份和美术引用。</summary>
    public static class NomadWaterCanHandlingPipeline
    {
        private const string GroupId = "water-can-access";

        internal static NomadFacilityInteractionGroupDefinition[] WithAccessGroup(NomadFacilityFunction function,
            IReadOnlyList<NomadFacilityInteractionGroupDefinition> groups,
            IReadOnlyList<NomadPlacementRegionDefinition> regions)
        {
            if (function is not (NomadFacilityFunction.VehicleWaterTank or NomadFacilityFunction.DrinkingStation))
                return groups.ToArray();
            NomadPlacementRegionDefinition region = regions.Single(r => r.RegionId == "water-can-parking");
            Vector3 offset = Quaternion.Euler(0f, region.LocalYawDegrees, 0f) * new Vector3(.5f, 0f, -.17f);
            var slot = new NomadFacilityInteractionSlotDefinition("side",
                region.LocalCenterMeters + new Vector2(offset.x, offset.z), region.LocalYawDegrees + 270f);
            return groups.Where(g => g.GroupId != GroupId)
                .Append(new NomadFacilityInteractionGroupDefinition(GroupId, true, new[] { slot })).ToArray();
        }

        /// <summary>仅改写派生的侧面工作组；其他工作组、Prefab、起步位置与区域坐标保持资产现值。</summary>
        internal static void EnsureAccessGroup(NomadFacilityDefinition definition)
        {
            if (definition.Function is not (NomadFacilityFunction.VehicleWaterTank or NomadFacilityFunction.DrinkingStation)) return;
            NomadFacilityInteractionGroupDefinition group = WithAccessGroup(definition.Function,
                definition.InteractionGroups, definition.PlacementRegions).Last();
            using var serialized = new SerializedObject(definition);
            SerializedProperty groups = serialized.FindProperty("interactionGroups");
            int index = -1;
            for (int i = 0; i < groups.arraySize; i++)
                if (groups.GetArrayElementAtIndex(i).FindPropertyRelative("groupId").stringValue == GroupId) index = i;
            if (index < 0) { index = groups.arraySize; groups.arraySize++; }
            SerializedProperty target = groups.GetArrayElementAtIndex(index);
            target.FindPropertyRelative("groupId").stringValue = GroupId;
            target.FindPropertyRelative("requiredForOperation").boolValue = true;
            SerializedProperty slots = target.FindPropertyRelative("alternativeSlots");
            slots.arraySize = 1;
            SerializedProperty slot = slots.GetArrayElementAtIndex(0);
            slot.FindPropertyRelative("slotId").stringValue = group.AlternativeSlots[0].SlotId;
            slot.FindPropertyRelative("localPositionMeters").vector2Value = group.AlternativeSlots[0].LocalPositionMeters;
            slot.FindPropertyRelative("localYawDegrees").floatValue = group.AlternativeSlots[0].LocalYawDegrees;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(definition);
        }

        [MenuItem("Assets/SSFramework/游牧工坊/首版美术/更新水罐侧面拿取位")]
        public static void UpdateDefinitions()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出 Play 再更新水罐侧面拿取位。");
            int updated = 0;
            foreach (string source in new[] { NomadFoundationVerticalSlicePipeline.VehicleWaterTankPath,
                         NomadFoundationVerticalSlicePipeline.DrinkingStationPath })
            foreach (string path in new[] { source, NomadWarmWorkshopArtPipeline.Root + "/Definitions/" + Path.GetFileName(source) })
            {
                var definition = AssetDatabase.LoadAssetAtPath<NomadFacilityDefinition>(path);
                if (definition == null) throw new InvalidOperationException("缺少当前样板水设施定义：" + path);
                EnsureAccessGroup(definition);
                updated++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[NomadWaterCanHandling] 已更新 {updated} 个自有定义的侧面拿取位，停放区域和既有场景保持原坐标。");
        }
    }
}
