# AI 项目索引：游牧工坊

> 这是 NomadWorkshop 独立仓库的开发入口。产品方向、Foundation 队列、专题证据和代码测试各自维护唯一来源；本页只负责交接和路由。更新：2026-09-09。

## 当前交接

- **产品目标**：在恶劣生存环境中建立有温度的移动小家；从一名玩家化身、只有驾驶位的微型车和拾荒起步，逐步形成建造、研究、制造、共同生活、可控随机故事和车辆成长。
- **当前正式入口**：`Assets/Game/NomadWorkshop/Scenes/StarterJourneySample.unity`。它是 N1-B 的最小起步切片；`ReferenceVehicleSample.unity` 继续保留已验证的三居民 Foundation 与暖工坊表现，不被替换。
- **当前状态**：N0 平稳连续旅途、N1-A 微型车纯配置/身份/座位休息规则，以及 N1-B 最小拾荒场景已经落地。N2-A 统一建造纯规则与 N2-B Foundation 接线已经落地：蓝图占位、材料暂存、施工阶段、取消/拆除回收、地点生命周期、只读蓝图投影、带地点与材料批次的检查点恢复均有契约；Foundation 管线还生成了可转为存档 DTO 的野战厨房样板资产。独立仓库已在 Unity 6000.3.22f1 中完成编译；N2-A 定向 EditMode 9/9、蓝图快照恢复 10/10、存档契约 22/22、可重建样板管线 1/1，蓝图 Foundation PlayMode 回读 1/1 均通过；之前起步入口的旧运行时输入错误也已改用 Input System。
- **唯一近期队列**：[Foundation §9](nomad-workshop-foundation-vertical-slice.md#9-近期队列美术样板与可扩建建造)。下一项是 N2-C：把当前直接确认即落地的建造入口改为蓝图规划，并接入材料搬运、施工工作姿态与取消/拆除表现；在此之前不宣称玩家已经拥有完整蓝图 UI。
- **空间与建造细则**：[世界放置与建造契约](nomad-workshop-world-placement-and-construction.md#51-车内外统一建造与地点生命周期)。它明确车内外共用蓝图/材料/工作量，不引入工作台作为建造前置。
- **恢复现场**：以实际 Git、Unity 编译/Play 状态和当前测试证据为准。修改场景或 Prefab 前必须通过 Unity Editor/MCP，不能手改 YAML。

## 文档来源地图

| 问题 | 首先读取 | 需要时继续 |
|---|---|---|
| 产品方向与成长顺序 | [产品愿景](nomad-workshop-game-vision.md) | [设计重审](nomad-workshop-design-review-2026-09.md) |
| 当前实现与下一项 | [Foundation 垂直切片](nomad-workshop-foundation-vertical-slice.md#9-近期队列美术样板与可扩建建造) | [阶段检查点](nomad-workshop-stage-checkpoint-2026-09.md) |
| 车内外建造、材料和地点生命周期 | [世界放置与建造契约](nomad-workshop-world-placement-and-construction.md#51-车内外统一建造与地点生命周期) | [可扩建甲板](nomad-workshop-expandable-decks-design.md) |
| 旅途、驾驶和兴趣点 | [驾驶接线](nomad-workshop-driving-integration.md) | [导航与交互](nomad-workshop-navigation-interaction-design.md)、[停靠资源所有权](nomad-workshop-stop-resource-ownership.md) |
| 居民、生活与接力方向 | [三居民接线](nomad-workshop-three-residents.md) | [生活仿真](nomad-workshop-domestic-life-simulation.md)、[人物交互绑定](nomad-workshop-model-interaction-bindings.md) |
| 当前美术和替换资产 | [首版美术](nomad-workshop-art-first-pass.md) | [参考结构造型](nomad-workshop-reference-form-study.md)、[人物制作](nomad-workshop-character-authoring.md) |
| 工具与验证 | 根目录 `AGENTS.md`、`Packages/com.liss.ssframework/docs/unity-mcp-tips.md` | `Tools/UnityTestEvidence.psm1`、[Framework 兼容记录](framework-compatibility.md) |

## 协作边界

- `Assets/Game/NomadWorkshop/Simulation/` 保持纯 C#，不依赖 Unity 或 Framework。
- 游戏代码和资产留在本仓库；通用能力只有在两个真实消费方都证明后才回流 `com.liss.ssframework`。
- 一次集中推进一个可验收工作项。设计建议可以随证据调整，安全、数据守恒、场景写入和测试契约才是硬边界。
- 新场景/Prefab 的验证必须针对新资产取证；旧场景通过不能替代新场景证据。

## 迁移基线

- NomadWorkshop：`main` / `develop`，当前提交由远端仓库维护。
- Framework Package：`com.liss.ssframework`，通过 `Packages/com.liss.ssframework` 子模块固定版本。
- Unity：6000.3.22f1。
- 最近验证：[N1-B 起步样板](validation-starter-journey-20260909.md)；迁移基线与 878/878 历史证据仍见[独立仓库迁移基线](validation-split-baseline-20260909.md)。
- 迁移后的文档从旧单体仓库移动到本仓库；Framework API 和通用 Unity 文档继续由 Framework 子模块维护，不在这里复制。
