using Game.NomadWorkshop.Simulation;
using UnityEngine;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>
    /// Foundation 一次决策中的可执行 Adapter 载荷。纯模拟层只认识 <see cref="ResidentActionCandidate"/>；
    /// 该对象把选中的候选重新关联到 Unity 设施、库存和散步目标，不参与评分，也不拥有任务租约。
    /// </summary>
    internal sealed class FoundationResidentDecisionOption
    {
        public FoundationResidentDecisionOption(
            FoundationResidentDecisionKind kind,
            ResidentActionPlanEvaluation evaluation)
        {
            Kind = kind;
            Evaluation = evaluation;
        }

        public FoundationResidentDecisionKind Kind { get; }
        public ResidentActionPlanEvaluation Evaluation { get; }
        public FoundationFacilityState SourceFacility { get; set; }
        public FoundationFacilityState TargetFacility { get; set; }
        public FoundationFacilityState WaterCanFacility { get; set; }
        public ResourceInventory TargetInventory { get; set; }
        public int TransferMilliliters { get; set; }
        public bool DrinkAfterDelivery { get; set; }
        public bool WaterCanAtSource { get; set; }
        public Vector3 WanderTarget { get; set; }
        public string WanderLabel { get; set; } = string.Empty;
    }

    internal enum FoundationResidentDecisionKind
    {
        Toilet,
        DirectDrink,
        WaterRestock,
        Wander,
        Daydream,
        Hobby,
    }
}
