# 居民执行所有权：正式驾驶前置实验

2026-09-05；主线与完成门槛沿用 [Foundation §9](nomad-workshop-foundation-vertical-slice.md#9-下一步形成一次完整旅程)。本实验推进基础版本，不扩大功能清单。

## 观察与设计

正式场景目前只有一名居民。水、物品和维修行动的租约、计时、路径、需求与随机游标散在 `NomadFoundationSystem` 的不同部分；`FoundationInteractionSpaceRuntime` 内部还只容纳一份居民占用。直接追加驾驶或复制 System 会让取消顺序与多人互斥继续分散。

本轮选择一个具体的 `FoundationResidentExecution` 保存每名居民的需求、游标和行动状态，并统一取消资源事务、恢复实体物品、释放站位、清除计时与路径。共享库存、物品放置账本、设施拓扑仍由世界拥有；System 保留决策、导航查询和 Model 投影。先保留现有同步状态机，避免引入没有第二个真实用例的通用任务框架。

空间模块改为共享账本返回各居民持有的 `FoundationInteractionSpaceLease`。不同空间可并行，同一空间仍整组原子互斥，同一居民只持有一处站位。相比每名居民单独复制空间模块，这能保留全局互斥；相比把所有居民租约留在单人 System，它明确了行动取消应释放谁。

设施重建保留仍能独占的地址与句柄。若两处已有占用被合并成同一空间，双方租约都撤销并由执行层重新决策，避免设施或居民枚举顺序选出隐式赢家。读档和销毁撤销全部运行期占用。旧句柄无法释放新占用。

## 可检查计划

- [x] 收口单居民执行状态与取消顺序，迁移所有正式调用方。
- [x] 分离空间拓扑与居民租约，覆盖多占用、重建冲突、旧释放与销毁。
- [x] 编译；精确清单运行空间契约测试和正式 Foundation PlayMode 回归，归档完整结果。
- [x] 自查 diff、生命周期、读档与取消边界。
- [x] 将真实驾驶接线移到[首段驾驶接线](nomad-workshop-driving-integration.md)。

空间契约 EditMode **7/7**（job `3e6a294625ca`）；迁移后的正式 Foundation PlayMode **41/41**（job `d421d9ad7f21`）。两组都经过保存预检、精确名单请求、完整 job 明细归档与 `Test-UnityMcpTestEvidence` 身份核对；证据目录为 `Logs/AIValidation/nomad-resident-execution-20260905/`。编译 0 error / 0 warning。

“空间账本支持多人”不代表正式三居民或交通已经完成；居民位置投影、决策循环与移动执行器仍为单人适配。后续驾驶接线已沿该 owner 继续，并增加建造迁离驾驶员后的物理站位复核。本轮没有修改 SSFramework 公共运行时 API，也没有增加常驻规则。
