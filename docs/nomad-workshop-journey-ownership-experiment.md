# 旅途所有权与自动化证据实验

> 2026-09-05。用户确认：游牧工坊同时用于验证游戏设计、SSFramework 与 AI 自动工作流；以新边界的学习价值安排小步实验，不以功能数量衡量进展。完整旅程仍是产品主线，近期队列由 [Foundation §9](nomad-workshop-foundation-vertical-slice.md) 维护。

## 本轮计划与边界

- [x] 复核现有时间、存档、Context/Bag 契约与上轮自动化摩擦。
- [x] 建立有限线段的精确路程 / 燃料规则；驾驶员离岗、目标变化、恢复使旧租约失效。
- [x] 通过真实 Context/Bag 验证驾驶岗位的所有权与迟到释放，不预建通用岗位框架。
- [x] 固化 MCP 测试发现 → 精确名单 → 实际结果对照，实际执行该流程。
- [x] 按实测更新 Skill 与协作文档，复核 diff、证据和 Editor 恢复状态。

本轮是无场景的旅途规则实验，尚未接入正式居民决策、驾驶台、表现或产品存档。它只回答“谁有权让车前进、离岗后是否还会前进、距离和燃料是否可信”。下一轮才把已成立的契约接到居民实际到岗与需求中断；不把测试直接授予租约描述成居民已学会驾驶。

复核入口：[旅途规则](../Assets/Game/NomadWorkshop/Simulation/NomadJourneySession.cs)、[纯规则反例](../Assets/Game/NomadWorkshop/Tests/EditMode/NomadJourneySessionTests.cs)、[Context/Bag 联合实验](../Assets/Game/NomadWorkshop/Tests/PlayMode/NomadJourneyOwnershipPlayModeTests.cs)、[测试证据工具](../Tools/UnityTestEvidence.psm1)。

## 选中的设计

比较直接保存 `isDriving` 与由执行 owner 持有唯一 `IDisposable` 租约：前者要求每个取消 / 读档 / 销毁调用方同步清字段，且旧回调可能清掉新驾驶员；后者由旅途 session 识别当前租约身份，租约放入既有 `DisposableBag`，旧租约只能失效，不能释放新岗位。本轮采用后者；Framework Core 不引入游戏岗位、Tick 调度或第二份状态真值。

有限路线仅含两个有稳定身份的端点。配置使用毫米、毫米 / 秒和纳升 / 毫米；精确积分状态使用微米和皮升（1 mm = 1000 µm，1 nL = 1000 pL）。这样 `(mm/s) × ms = µm`、`(nL/mm) × µm = pL`，无需逐帧浮点取整或亚毫米余量字段；到达或燃料不足时截断到真实可移动距离，不按整帧预扣。更换目标保留当前位置并安全离岗；取消不传送回起点。Session 恢复只恢复世界状态，不恢复执行租约，重新到岗后才可继续。这是纯规则计算精度，不要求玩家界面展示微米或皮升。

这个快照是实验的内存契约，不扩充当前正式 `NomadVehicleSaveData`。路线身份 / 参数必须匹配，非法恢复在改变旧 session 前拒绝；正式地点库存与版本迁移要等真实场景接线时一起设计。

仅防止旧租约释放仍不充分：旧目标的寻路可能尚未返回，期间玩家取消并重新选择同一地点。开始前往岗位时必须捕获 `DestinationRevision`，实际到岗领取租约时原样提交；目标变化与恢复递增运行时版本，旧请求返回失败。这个版本不存入世界快照，也不能在迟到回调中重新查询当前版本来“修复”请求。

## 自动化改进命题

上轮 `filter` 未被当前 Unity Adapter 消费，且宽正则曾意外执行更多用例。只有“发现非空”及“job succeeded”不足以证明选中范围准确；即使实际数量相同，也可能执行了错误用例。

选择项目 `Tools` 中的纯证据脚本：消费 MCP 返回的发现数据，生成 `mode + testNames` 精确请求；终态读取完整明细，与预期名单逐项比较。它不启动 Unity、不实现队列、不代替预检，也不处理用户凭据。相比复制 TestRunnerApi 包装层，这个载体可离线测试，也保留当前 MCP 的 job、后台与恢复机制。

## 验证记录

开始时基于上轮未提交工作树（基线提交 `41c562a`）；Unity 6000.3.22f1，StandaloneWindows64，正式 Foundation 场景正处于 Play。编辑代码前由 Unity MCP 退出 Play；所有 Test Runner 均先执行保存预检并观察 READY。

| 范围 | 结果与证据 | 能证明什么 |
|---|---|---|
| 旅途纯规则 `NomadJourneySessionTests` | 最终 EditMode **11/11**（job `dd6aab8ce7cf`，1.07 s） | 1 / 10 / 137 / 1000 ms 分块精确一致、取消与返程、到达 / 缺油不超扣、恢复原子性、旧目标晚到岗与旧租约晚释放 |
| Framework 联合 `NomadJourneyOwnershipPlayModeTests` | 最终 PlayMode **5/5**（job `caef53cd2b22`，0.49 s） | 显式 owned 的 Context → Bag → 行动子 Bag 释放链；外部 CreateBag 仍由调用方持有；晚登记与恢复后的旧行动不能影响新岗位 |
| 证据脚本 | `Tools/Tests/UnityTestEvidence.Tests.ps1` **20 项断言通过** | 空名单、截断、不可运行、重复身份、错误模式 / job、相同数量的错误用例、缺明细、矛盾汇总、真实失败与跳过的判定 |
| 真实流程闭环 | 上述两个最终 job 的发现、计划、dispatch 与完整结果均已保存；脚本核对实际身份分别为 **11/11、5/5** | 精确名单经过当前 MCP 执行并核验；不是只验证脚本 fake 数据 |

首轮联合测试 job `c5a7cfe5451d` 为 3/4：实验接线遗漏 `RegisterOwned` 的显式契约类型，脚本如实返回 `Failed`。修正为 `RegisterOwned(resident, typeof(DisposableBag))` 后 4/4（job `046547edf93c`）；随后独立自查增加晚到岗版本保护，再获得表中的最终 11/11 与 5/5。失败原始结果一起保存，便于检查自动化的失败路径。

原始证据位于本地忽略目录 `Logs/AIValidation/nomad-journey-ownership-20260905-1250/`：`summary.json` 汇总各轮 verdict，`intent-edit-*` 与 `intent-play-*` 是最终计划、dispatch 和明细，`finalSourceHashes` 仅记录最终五个实验 / 工具源文件，不冒充整个工程的构建指纹。

复核当前 MCP 源码 `MCPTestRunnerCommands.SaveToSessionState` 确认其域重载持久化不包含逐用例明细；本轮也观察到旧 job 保留通过汇总而 `tests` 为空。新的验收流程先保存明细，再继续代码刷新。Framework 的 `CreateBag` 实现与接口注释、guide 已明确区分“关联 Context 能力”和“Context 拥有释放”；没有改变 Runtime 行为，也没有增加通用岗位 API。验证 Skill 只增加该工具的路由与证据恢复边界，根 AGENTS 未增加常驻规则。

最终编译 0 error / 0 warning；Editor 处于 EditMode、正式 Foundation 场景无脏改动，没有场景 / Prefab 修改。此次只跑新实验的定向测试，没有把上轮全量数量充当本轮全工程回归。未验证正式居民到岗、需求导致的离岗、车外召回、正式存档升级、UI / 景物表现、目标 Player、性能或玩家体验。

下一次正式接线应沿“记录目标版本 → 实际寻路到岗 → 授予租约 → 需求 / 目标变化释放行动 Bag”的真实调用链推进，并让 Harness 穿过同一入口；不在测试里直接授予租约后声称产品体验成立。
