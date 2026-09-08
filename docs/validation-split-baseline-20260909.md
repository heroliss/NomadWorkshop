# 独立仓库迁移基线（2026-09-09）

这份记录确认 NomadWorkshop 从旧单体仓库拆出后，能够在自己的工作区和固定 Framework 子模块版本上独立编译、运行测试。它是迁移后的工程基线，不替代场景体验验收。

## 环境

- 工程：`D:\SSFramework-Migration\nomad`
- Unity：`6000.3.22f1`
- 目标平台：`StandaloneWindows64`
- Framework 子模块：`f1e1439c8ef86be468b659b2b813309bd7d3086b`
- NomadWorkshop 提交：`f8ab0c0`（执行验证时）

## 验证步骤

1. 在 Unity Editor 中确认未处于 Play Mode、未编译且当前场景无未保存修改。
2. 执行 `SSFramework/诊断/AI 自动化/PlayMode 测试预检（保存脏场景）`，让 EditMode/PlayMode 共用的保存边界先收口。
3. 通过 Unity MCP 启动完整 EditMode 测试。
4. 等待同一测试任务完成后读取结果，不以启动阶段的 `editor_unfocused` 状态作为失败结论。

## 结果

- 编译错误：0
- EditMode：878/878 通过，0 失败，0 跳过
- 测试任务：`67b9fddfbe82`
- 用时：约 73.9 秒

迁移后首次回归暴露的 3 个失败均来自 Framework 审计测试把“存在 HybridCLR 模块”错误当成“每个消费方都必须拥有热更 Profile”。Framework 已在 `239e220`、`f1e1439` 修正为允许纯 AOT 消费方缺省 Profile，并由子模块版本更新收口；本次全量回归验证了该兼容边界。

## 结论与边界

独立仓库的代码、Framework 子模块和 EditMode 测试基线已经闭合，可以继续实现 Foundation §9 的 N1-B。当前记录没有声称 `StarterJourneySample` 已完成，也没有替代新场景的 PlayMode、视觉和玩家体验验收；这些证据随 N1-B 场景接线后单独补齐。
