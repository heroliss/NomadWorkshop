using Game.Framework;
using Game.Framework.Context;
using Game.Framework.Storage;
using Game.Framework.Systems;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>
    /// 最小垂直切片的场景级组合根。当前不声明为全局 Context；存储先在场景级接通以验证完整契约，
    /// 真正的启动与跨场景流程成立后只需把同一 Utility 注册迁到全局根，业务 Command 不变。
    /// </summary>
    public sealed class NomadFoundationContext : MonoGameContextBase
    {
        protected override void InstallBindings(ContainerBuilder builder)
        {
            builder.RegisterValue(new LoggingCommandSystem(), typeof(ICommandSystem));
            builder.RegisterOwnedUtility(new StorageUtility());
        }
    }
}
