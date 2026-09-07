using System;
using System.Collections.Generic;
using Game.NomadWorkshop.Simulation;
using Game.NomadWorkshop.Simulation.Persistence;

namespace Game.NomadWorkshop.Foundation
{
    public sealed partial class NomadFoundationSystem
    {
        private const string LegacyToiletInventoryId = "toilet-holding";

        /// <summary>
        /// 校验新实例库存，或将无来源信息的旧共享桶迁入稳定 id 最小的一座厕所。
        /// 只构造待恢复数据，不修改输入 DTO 和当前世界；混合新旧记录拒绝，避免重复污物。
        /// </summary>
        private static Dictionary<string, NomadInventorySaveData> ResolveToiletInventoriesForRestore(
            IReadOnlyDictionary<string, NomadFacilityFunction> functions,
            IReadOnlyDictionary<string, NomadInventorySaveData> inventories,
            ISet<string> consumedInventoryIds)
        {
            var ids = new List<string>();
            foreach (var facility in functions)
                if (facility.Value == NomadFacilityFunction.Toilet) ids.Add(facility.Key);
            ids.Sort(StringComparer.Ordinal);
            var result = new Dictionary<string, NomadInventorySaveData>(StringComparer.Ordinal);

            if (inventories.TryGetValue(LegacyToiletInventoryId, out var legacy))
            {
                if (legacy.OwnerEntityId != "vehicle-01" || legacy.CapacityBaseUnits <= 0)
                    throw new InvalidOperationException("旧共享厕所库存的归属或容量无效。");
                int amount = GetSingleResourceAmount(legacy, NomadResourceIds.HumanWaste);
                if (ids.Count == 0 && amount > 0)
                    throw new NotSupportedException("旧存档含共享厕所污物，却没有可承接它的厕所；当前世界未修改。");
                foreach (string id in ids)
                    if (inventories.ContainsKey(BuildToiletInventoryId(id)))
                        throw new InvalidOperationException("检查点混合共享桶和实例厕所库存，无法确定污物是否重复。");
                for (var i = 0; i < ids.Count; i++)
                {
                    ResourceQuantity[] contents = i == 0 && amount > 0
                        ? new[] { new ResourceQuantity(NomadResourceIds.HumanWaste, amount) }
                        : Array.Empty<ResourceQuantity>();
                    var inventory = new ResourceInventory(BuildToiletInventoryId(ids[i]),
                        ResourceMeasure.Milliliter, legacy.CapacityBaseUnits, contents);
                    result.Add(ids[i], CreateInventorySaveData(inventory, ids[i]));
                }
                consumedInventoryIds.Add(LegacyToiletInventoryId);
                return result;
            }

            foreach (string id in ids)
            {
                string inventoryId = BuildToiletInventoryId(id);
                NomadInventorySaveData inventory = RequireInventory(inventories, inventoryId,
                    ResourceMeasure.Milliliter);
                if (inventory.OwnerEntityId != id || inventory.CapacityBaseUnits <= 0)
                    throw new InvalidOperationException($"厕所库存 {inventoryId} 的实际归属或容量无效。");
                GetSingleResourceAmount(inventory, NomadResourceIds.HumanWaste);
                result.Add(id, inventory);
                consumedInventoryIds.Add(inventoryId);
            }
            return result;
        }
    }
}
