using System.ComponentModel;
using Game.Framework.Command;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>开发 Harness 注入一次离散状态冲击；它不代表正在运行的持续天气。</summary>
    [Description("开发验收：向主水箱注入一次离散沙尘状态冲击")]
    public readonly struct ApplyPrimaryWaterTankSandstormCommand : ICommand<bool>
    {
        public bool Execute(ICommandContext ctx) =>
            ctx.GetSystem<NomadFoundationSystem>().ApplyPrimaryWaterTankSandstormStress();
    }

    /// <summary>完成普通清洁保养；它不会把等效磨损或已经发生的故障抹掉。</summary>
    [Description("清洁并保养主车辆水箱")]
    public readonly struct PerformPrimaryWaterTankMaintenanceCommand : ICommand<bool>
    {
        public bool Execute(ICommandContext ctx) =>
            ctx.GetSystem<NomadFoundationSystem>().PerformPrimaryWaterTankMaintenance();
    }

    /// <summary>把当前风险推进到预取样阈值，供自动验收稳定抵达故障分支。</summary>
    [Description("开发验收：推进主水箱直到故障触发")]
    public readonly struct ForcePrimaryWaterTankFaultCommand : ICommand<bool>
    {
        public bool Execute(ICommandContext ctx) =>
            ctx.GetSystem<NomadFoundationSystem>().ForcePrimaryWaterTankFault();
    }

    /// <summary>开发 Harness 跳过居民、备件与工时，瞬时修复水箱故障。</summary>
    [Description("开发验收：跳过实体维修链并瞬时修复主水箱")]
    public readonly struct RepairPrimaryWaterTankFaultCommand : ICommand<bool>
    {
        public bool Execute(ICommandContext ctx) =>
            ctx.GetSystem<NomadFoundationSystem>().RepairPrimaryWaterTankFault();
    }
}
