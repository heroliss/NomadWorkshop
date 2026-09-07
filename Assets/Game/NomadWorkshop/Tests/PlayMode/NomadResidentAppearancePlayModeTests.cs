#if UNITY_EDITOR
namespace Game.NomadWorkshop.PlayMode.Tests
{
    /// <summary>正常服装候选复用原美术场景的完整携物/工作/旅程契约；测试身份明确区分模型。</summary>
    public sealed class NomadResidentAppearancePlayModeTests : NomadWarmWorkshopPlayModeTests
    {
        protected override string ScenePath => "Assets/Game/NomadWorkshop/Scenes/ResidentAppearanceSample.unity";
        protected override string WorkshirtMaterialName => "NW2_Workwear";
    }

    /// <summary>正常服装候选复用完整上下楼、净空、暂停、取消与检查点契约。</summary>
    public sealed class NomadResidentStairAppearancePlayModeTests : NomadStairTraversalPlayModeTests
    {
        protected override string ScenePath => "Assets/Game/NomadWorkshop/Scenes/ResidentStairSample.unity";
    }
}
#endif
