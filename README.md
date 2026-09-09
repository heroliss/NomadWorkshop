# NomadWorkshop

NomadWorkshop 是正在开发的废土移动生存游戏。玩家从一辆只能提供驾驶位的微型车和一次拾荒开始，在恶劣环境中逐步扩展车辆、收集资源、研究与建造，并让成员形成有温度的移动小家。玩法、模拟、场景、美术资产和生产文档全部属于本仓库。

## 当前开发入口

当前可运行的最小起步切片是 `Assets/Game/NomadWorkshop/Scenes/StarterJourneySample.unity`：它覆盖玩家身份、微型车、拾荒、基础资源和持续旅途的 N1-B 目标。统一建造的 N2-A 纯规则与 N2-B Foundation 读模型、版本化检查点已经通过验证；可重建样板资产位于 `Assets/Game/NomadWorkshop/Foundation/Definitions/NW_Blueprint_FieldKitchenSample.asset`，正式蓝图 UI、居民施工动画和玩家建造入口仍在后续阶段。`ReferenceVehicleSample.unity` 继续保留三居民 Foundation 与暖工坊表现，用作后续扩展参考。

进入开发前阅读 [`docs/ai-project-index.md`](docs/ai-project-index.md) 和当前 Foundation 专题；它们记录当前唯一队列、证据与下一步边界。

## 仓库边界

- Unity：`6000.3.22f1`。
- Framework：`Packages/com.liss.ssframework` Git submodule，固定到已验证的 commit。
- 纯逻辑模拟：`Assets/Game/NomadWorkshop/Simulation/`，不依赖 Unity 或 Framework。

克隆后执行：

```powershell
git submodule update --init --recursive
```

## 分支、同步与验证

- `main`：可运行里程碑。
- `develop`：当前集成线。
- `feature/*`：短期功能分支。
- `v0.x.y` 或带里程碑后缀的标签：阶段发布。

升级 Framework 时在本仓库提交新的子模块 gitlink，并在 `docs/framework-compatibility.md` 记录 SHA/tag 与验证结果。通用安装与升级规则见 [Framework 接入与升级说明](https://github.com/heroliss/SSFramework/blob/main/docs/consuming-framework.md)。场景与 Prefab 只通过 Unity Editor/MCP 修改；纯模拟改动则运行对应的纯 C# 测试。

## 直接依赖

- [SSFramework](https://github.com/heroliss/SSFramework)：本项目消费的公共框架包。
