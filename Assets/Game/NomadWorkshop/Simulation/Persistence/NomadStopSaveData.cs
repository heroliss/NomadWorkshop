using System;

namespace Game.NomadWorkshop.Simulation.Persistence
{
    /// <summary>路线地点的有限水源、污物接收罐与玩家作业意图；重返时不得重新初始化库存。</summary>
    [Serializable]
    public sealed class NomadStopSaveData
    {
        public string SiteId = string.Empty;
        public int WaterMilliliters;
        public int WaterCapacityMilliliters;
        public bool WaterCollectionRequested;
        public int WasteMilliliters;
        public int WasteCapacityMilliliters;
        public bool WasteDisposalRequested;
        public bool SpareStockInitialized;
        public bool SpareCollectionRequested;
        // Unity JsonUtility 会把缺失的嵌套引用解为全默认对象；只有完全空对象允许旧档初始化。
        public bool IsEmpty => string.IsNullOrEmpty(SiteId) && WaterMilliliters == 0 &&
            WaterCapacityMilliliters == 0 && !WaterCollectionRequested && WasteMilliliters == 0 &&
            WasteCapacityMilliliters == 0 && !WasteDisposalRequested && !SpareStockInitialized && !SpareCollectionRequested;

        /// <summary>在修改运行世界前拒绝无主、负量、溢出容量的地点状态。</summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(SiteId) || SiteId != SiteId.Trim() ||
                WaterCapacityMilliliters <= 0 || WaterMilliliters < 0 || WaterMilliliters > WaterCapacityMilliliters ||
                WasteCapacityMilliliters <= 0 || WasteMilliliters < 0 || WasteMilliliters > WasteCapacityMilliliters)
                throw new InvalidOperationException("驿站库存的身份、容量或数量无效。");
        }
    }
}
