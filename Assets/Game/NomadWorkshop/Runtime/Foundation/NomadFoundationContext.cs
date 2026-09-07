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
#if UNITY_EDITOR
        private IStorageProvider _testStorageProvider;
        private bool _bindingsInstalled;

        /// <summary>仅供未激活的隔离夹具装配。Utility 接管 Provider 生命周期，不能运行时替换。</summary>
        public void ConfigureStorageForTests(IStorageProvider provider)
        {
            if (_bindingsInstalled) throw new System.InvalidOperationException("请在 Context.Awake 前配置测试存储。");
            _testStorageProvider = provider ?? throw new System.ArgumentNullException(nameof(provider));
        }
#endif
        protected override void InstallBindings(ContainerBuilder builder)
        {
            builder.RegisterValue(new LoggingCommandSystem(), typeof(ICommandSystem));
#if UNITY_EDITOR
            _bindingsInstalled = true;
            builder.RegisterOwnedUtility(new StorageUtility(_testStorageProvider));
#else
            builder.RegisterOwnedUtility(new StorageUtility());
#endif
        }
    }
}
