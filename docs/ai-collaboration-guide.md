# AI 协作方案（Codex 主路径、公共内核）

> 面向项目维护者：说明 SSFramework 如何让 Codex 完成当前开发，同时给未来 Agent 留出稳定、低维护的承接面。新 Agent 的实际接入步骤见 [`ai-agent-onboarding.md`](ai-agent-onboarding.md)，AI 游戏开发能力的长期演进见 [`ai-game-development-capability-map.md`](ai-game-development-capability-map.md)。

## 1. 当前真值

```text
SSFramework/
├── AGENTS.md                         # 全项目常驻协作与安全边界
├── Assets/Game/AGENTS.md             # Framework 业务调用约束
├── Assets/Game/Framework/AGENTS.md   # Framework 内部实现约束
├── Assets/Game/Framework/Demo/Scripts/Modules/AGENTS.md
│                                      # Demo 教学章节约束
├── CONTEXT.md / docs/adr/            # 领域语言与已接受设计决定
├── .agents/skills/                   # Project Skill 的唯一权威正文
├── docs/                             # guide / ADR / 专题文档，按需读取
├── Tests / Tools / Editor Seam       # 可独立运行的确定性门禁与 Harness
└── CLAUDE.md                         # 仅导入根规则的低成本入口
```

当前没有为 Cursor、Gemini、Copilot 等未实际采用的 Agent 提交仓库级预配置，也没有把 Unity MCP 的机器连接复制成产品配置。目录或格式存在不等于工具、权限和交付闭环已经验证。

《游牧工坊》的按需入口也区分职责：[AI 项目索引](ai-project-index.md)只保留当前状态、证据快照和唯一近期队列；[产品愿景](nomad-workshop-game-vision.md)维护目标与范围，生活 / 建造 / 导航专题维护各自契约，[Foundation](nomad-workshop-foundation-vertical-slice.md)维护实现与详细证据，游戏目录 README 负责导航。阶段重审记录观察、用户选择和验证，不再产生另一条长期队列。这样产品方向调整可以回到同一入口，而不靠多个“下一步”段落相互同步。

2026-09-06 美术评图推动了实际产品范围调整：小甲板起步、非矩形扩建、局部多层与围护。[可扩建甲板专题](nomad-workshop-expandable-decks-design.md)集中保存本次代码证据、建议边界和新 Harness 验收点，[美术样板记录](nomad-workshop-art-first-pass.md)保存图像与人工反馈；近期顺序仍由 Foundation 维护。纯规则分层字段、概念图与实机多层分别标状态，本轮不把尚未验证的空间方案加入 Framework 公共契约或常驻 Agent 规则。

## 2. 设计原则

### 2.1 Codex-primary，而不是 Codex-only

Codex 是当前日常编码、Unity MCP 和验证闭环的主路径，但项目契约描述的是目标、边界和完成条件，不是某个客户端的按钮或权限界面。其他 Agent 即使不能自动发现规则和 Skill，也能通过显式读取 Markdown、调用相同项目工具和执行相同验证逐步承接。

这是一种“稳定公共内核 + 按需接入”，不是同时维护所有产品兼容性。AI 产品变化快；未进入真实工作流前不猜测配置格式，不做空占位，也不要求每次改动在多个 Agent 上重复验证。

### 2.2 一条规则只有一个权威来源

- 项目常驻规则写在覆盖范围最小的 `AGENTS.md`。
- 有稳定触发条件的多步流程写在 `.agents/skills/<name>/SKILL.md`。
- 领域词义、完整原理和历史取舍写入 `CONTEXT.md`、guide 或 ADR。
- 确定性契约用测试、编译器约束、项目脚本或 Editor Seam 实现。
- 产品接入层只负责发现、工具连接或权限映射，不复制上述正文。

根 `CLAUDE.md` 仅导入根 `AGENTS.md`，作为已经存在且维护成本极低的例外；它不为 Claude 复制 Project Skill，也不构成 Claude 已接入的证明。

### 2.3 常驻上下文只保留主动判断

`AGENTS.md` 不是模块百科。只有无需关键词也必须改变每次判断的内容才常驻；可选模块的 API、故障处理和示例在任务命中后读取对应 guide / ADR / Skill。这样在 Framework 深目录工作时，不会被无关模块契约挤占上下文。

Demo 教学的内容组织规则放在最窄的 `Assets/Game/Framework/Demo/Scripts/Modules/AGENTS.md`。这是作者判断，不适合用 Hook 或锁死具体文案的测试强制。

### 2.4 规则是可审查的决策，不是宗教

规则按约束强度解释：安全、数据完整性、权限和确定性行为契约才使用“必须 / 不得”；有现实取舍的工程偏好使用“默认 / 优先 / 通常”；只在特定条件成立的流程应把触发条件写出来。项目事实与文字冲突时，Agent 需要复核目标、给出证据，并选择最窄的安全例外或修正规则，不能为了字面合规制造样板，也不能静默绕过后留下失真的真值。

规则演进不是每次例外都扩写 `AGENTS.md`。一次性例外留在任务说明或提交；反复出现才用 `propose-rule-evolution` 判断应落到规则、Skill、测试、Hook、代码结构还是个人配置。不可逆操作和外部授权仍以根目录“不可绕过的协作边界”为准。

### 2.5 机器门禁优于“请记得”

能写成行为契约的规则优先靠近事件源：

- EditMode / PlayMode Test Runner 前的脏场景处理都由 `SSFramework/诊断/AI 自动化/PlayMode 测试预检（保存脏场景）` 承担；菜单名是为现有自动化保留的历史 Interface；
- Editor 副作用动作由 `FrameworkEditorOperationGate` 在 UI 与 Implementation 两层校验；
- Module 依赖、菜单契约、Demo CodeRef、真实构建体积和关键 API 语义都有项目内验证入口；
- 工程外 Unity 发现与 headless 自动化经 `Tools/UnityAutomation.psm1` 统一，避免每个 Agent 重写机器路径。

这些 Seam 能被人工、MCP、CLI 与 CI 共同复用。具体流程见 `docs/unity-mcp-tips.md`、`docs/unity-cli-automation.md` 和相关 ADR。

游牧工坊的[模型替换与交互绑定计划](nomad-workshop-model-interaction-bindings.md)把“以后可替换”改成待验证的具体实验：改变接口位置、数量、机构轴与模型形体，通过同一任务检查交互、资源和恢复；现有单模型通过不能替代这份证据。该实验先留在游戏模块，尚未形成 SSFramework 公共能力或新的常驻规则。

用户随后否定方块人物的观感。[人物制作候选](nomad-workshop-character-authoring.md)因此先要求正常人物的实际俯瞰小样，再验证动作与参数变化。共享动作测试通过只能证明当时角色的行为边界，不能替代造型验收；选型的官网功能、许可与源码审阅也不等于本项目已经接入。经验先记录在游戏实验中，没有增加生成器专属 Skill 或框架接口。

现成人物对比随后直接复用 Warm / Stair 的相同行为断言，以独立 fixture 和场景记录新旧模型，避免把旧模型绿灯移用于新资产。近景另发现内袖穿过外层短袖：骨骼与把手接触正确仍不能证明蒙皮和衣物层次正确。四骨权重补齐后穿插仍在，实际修正来自派生资产的内袖遮罩；记录这一反证，避免把同时存在的质量设置问题误当成唯一原因。观察报告增加场景身份，资产/截图和实际执行名单分别留证；这些发现先落实在导入配方、验证入口和游戏文档。

三居民候选进一步暴露导入落盘时序：新建的十份 URP 材质在后续导入时改变了文件哈希。已核对本机 URP `MaterialPostprocessor` 会为新材质追加版本子资产并排队保存；日志记录了对应导入，但最初只存哈希，不能声称逐字段差异已被完整证明。生成菜单成功后，需让该批资产完成导入/保存，再固定验证快照；此例使用 Unity 公共 `AssetDatabase.ImportAsset` 与 `SaveAssetIfDirty` 定向处理候选材质，并保留前后快照。不得删掉漂移记录或静默覆盖旧哈希；没有修改 PackageCache 或调用 URP 私有保存接口。

## 3. Agent 接入边界

### 当前状态

| 路径 | 状态 | 可以承诺什么 |
|---|---|---|
| Codex | 当前主要路径；分层规则、Project Skills、Unity 工具与验证流程已在真实任务使用 | 具体任务仍按风险选择证据，不宣称一次验证覆盖所有游戏开发 |
| Claude 根规则导入 | 仅保留 `CLAUDE.md` → `AGENTS.md` | 可低成本读取根规则；Skill、嵌套规则、Unity 工具和闭环尚未验证 |
| 其他 Agent | 不预配置 | 可从 `ai-agent-onboarding.md` 和通用启动提示开始；真实采用后再记录已验证任务类型 |

接入的完成定义不是“有一个产品目录”，而是 Agent 在一条代表性任务中正确读取规则与 Skill、连接所需工具、产出与风险相称的证据，并能清理或解释失败状态。

### Codex 主路径

| 能力 | 当前入口 | 项目策略 |
|---|---|---|
| 项目规则 | 根到 cwd 的 `AGENTS.md` 指令链 | 分四层就近约束，越近越具体 |
| Project Skill | `.agents/skills/<name>/SKILL.md` | 唯一权威正文；`agents/openai.yaml` 仅是可选元数据 |
| Hook | Codex 项目配置 | 当前未配置；固定事件确需确定执行时才增加 |
| Subagent | Codex 内置能力 | 默认单 Agent；仅用户或已触发 Skill 明确要求时委派 |
| MCP / Connector | 当前客户端连接层 | Unity 使用现有 MCP；凭据和机器状态不提交仓库 |

### Project Skills

当前五个 Project Skill：

- `propose-rule-evolution`：为重复协作问题选择 AGENTS、Skill、Hook、测试或配置等正确载体；
- `improve-ssframework-architecture`：依据项目领域、ADR、调用链和测试证据改进 Module、所有权与可删除边界；
- `unity-background-automation`：后台运行或监控 Unity 测试与 Editor 自动化；
- `unity-screenshot`：捕获并实际检查 Unity 视图；
- `unity-validation-harness`：按改动风险选择、执行并汇总编译、测试、运行时、视觉、性能或隔离构建证据。

Skill 只收口有稳定触发条件的多步流程。一次性判断、已被测试兜底的契约或与项目无关的个人偏好不创建 Skill。依赖 SSFramework 语义的能力先在项目内证明；真正跨项目、跨 Agent 稳定后再按开放 [Agent Skills](https://agentskills.io) 结构分发。

### MCP、CLI 与 Harness

[MCP](https://modelcontextprotocol.io) 是可移植协议，不是统一配置文件。稳定契约应保存在 `docs/unity-mcp-tips.md`、Project Skill、项目测试和 `Tools/UnityAutomation.psm1`：例如 Test Runner 脏场景预检、测试集合非空、截图后实际检查、超时恢复与清理。新 Agent 只需要按自己的连接方式接到同一个执行层；机器路径、Token、登录态和个人权限留在用户级。

Harness 也不是“让 AI 自动做一切”的另一个框架。它是任务驱动、可观察性、判定标准、恢复/重试和证据汇总的闭环。优先复用现有测试、BattleSim、PlayerPath、截图与性能工具，不另造第二套 Unity Runner。

`Tools/UnityMcpRecovery.mjs` 只处理已观察到的宿主 MCP 通道关闭：显式连接现有官方 stdio Server，核对项目/端口后继续相同 `unity_*` 工具与队列，并保存每次请求和响应。2026-09-06 已用于美术导入、场景保存、截图和 Test Runner；它不是第二套 Unity 控制协议，也不修改个人配置。恢复边界见 [Unity MCP §3](unity-mcp-tips.md#3-重编译会断连)。菜单受理、资产导入校验、实机动作测试和视觉评价仍分别提供各自证据，不能互相代替。

持桶楼梯实验还发现退出 Play 后 MCP Console 事件缓冲停在旧消息，但预检已向 Unity 原始日志输出 READY。按 `propose-rule-evolution` 的最窄载体原则，将后备取证放入 [Unity MCP 预检说明](unity-mcp-tips.md)：核对项目/实例、时间与本次新日志段，区分工具观察停滞和被观察操作失败，没有新增常驻规则或修改第三方源文件。

游牧工坊垂直切片同时用于探索游戏设计、检验 SSFramework 和改进 AI 工作流。每轮优先选择能验证尚未经历边界的小任务，记录业务行为、框架契约和自动化过程各自得到的证据；功能数量不是进度的唯一尺度。公共框架变更仍需真实调用方与反例支撑，重复流程先固化到最窄的工具 / 测试，经验成立后再更新 Skill 或规则。

2026-09 的 `Tools/UnityTestEvidence.psm1` 是这种演进的一个实例：只将完整 fixture 发现结果转换成精确 MCP 请求，并核对实际用例身份、job 和计数；执行、保存预检和后台队列仍由现有 MCP 承担。`unity-validation-harness` 路由到同一工具与 `docs/unity-mcp-tips.md` §5，避免每个 Agent 复制正则、接受空跑或在域重载后仅凭汇总宣称覆盖。事实与验证见[旅途所有权实验](nomad-workshop-journey-ownership-experiment.md)。

随后的[驾驶接线](nomad-workshop-driving-integration.md) 继续使用同一证据流程，并暴露“居民站着驾驶时不能按位置不变判为卡死”的局部问题。解决方式是扩充原有 Soak 签名并增加持续驾驶反例，保留 Harness 版本，不增加一条要求 Agent 每次记住例外的根规则。公共框架本轮没有对应缺口，因此只改游戏 owner、测试与证据文档。

[厕所实例库存回归](nomad-workshop-stop-resource-ownership.md) 则暴露真实鼠标改写 Command 建造预览的夹具干扰。共用助手用有界作用域隔离输入 Update、核对姿态并恢复 View，随后整组回归通过；业务链测试与实际输入测试分别声明证据。该检查留在执行路径，未将环境偶发性转成需要 Agent 手工提醒的新规则。

同一切片的有限水源阶段继续暴露“启动首帧必为空闲”“跨混合行动比较任意两帧”“任意终帧无背压就代表长期恢复”三种测试假设。修正在具体夹具中：用暂停 / 重置确定起点，用统一 10 ms 步长观察真实恢复阶段，用连续生活循环证明可恢复。失败与最终精确结果一并保留；未新增全局等待时长、抢焦点要求或宽容断言。业务路径通过后另采正式场景和 1× 返回段证据，区分 Harness 能证明的守恒与仍需试玩判断的镜头、设施表现和体验。

有限清运阶段进一步区分“资源结果提交”与“容器和居民归位”：前者使用现有 ProcessTaskLease 原子提交，后者由车外访问 owner 继续持有责任。对应回归同时观察稳定桶身份、倾倒前后内容、回装和驾驶等待；Soak 0.6.0 纳入接收容量与携带容器身份。首轮视觉断言取到同帧 Restore 后待销毁的旧树，修正在夹具准备边界等待 Unity 帧末清理，保留同一 Transform 的跟随 / 回装断言，没有新增框架 API 或通用等待规则。

有限备件阶段复用既有物品移动 / 使用租约扩展到驿站支撑实体，没有改公共框架。新边界由实际实例身份贯穿“来源 → 手持 → 托盘 → 维修消耗”验证，存档用显式初始化标记区别旧档和已耗尽地点。Harness 0.7.0 将稳定物品 id、定义、区域和局部姿态纳入轨迹，并以相同件数、不同身份的反例验证。旧档夹具构造只包含该版本已有的数据；未知地点用例也需先保证内部归属一致，才能真正穿过预期的 Adapter 边界，不能以放宽异常类型代替修正输入。

同一正式旅程在风暴期间发现 `Particle Velocity curves must all be in the same mode`。业务固定步可在一个引擎帧内穿过整场天气，因而此前天气账本回归没有覆盖粒子模块的实际执行。修复三个速度轴的模式后，新增测试让真实天气激活粒子对象、推进引擎帧并追踪同一粒子的位移；将失败留在运行证据中。此类差异由对应系统的可执行测试兜底，不要求 Agent 记住一种具体粒子配置。

设施绑定变体的阶段收尾进一步验证了生成器的落盘边界：菜单返回成功不代表接线有效，跨 Single 场景切换保留的 Unity 对象可能已经失效。`NomadFacilityBindingPipeline` 现在保存资产路径与业务 id，接线时重新加载持久对象，并重新打开已保存场景检查引用和绑定；真实工作测试随后覆盖端点位移及反向机构。修复放在产生引用的工具中，未扩张根规则。另一个旧补水测试曾把库存提交当成空桶归位；现分别等待两个真实阶段并核对最终库存，不放宽物品所有权断言。

### Hook 与专用 Agent

Hook 适合格式化、配置审计、危险命令拦截或固定事件验证。真正的检查逻辑优先做成可独立运行、幂等、超时可诊断的脚本或测试，再由实际使用的客户端薄调用。主观架构判断不塞进 Hook。

专用 Agent 能隔离搜索噪音，也会增加协调成本。只有相同隔离角色反复出现时才产品化；稳定角色先把输入、输出、停止条件和证据写成公共 Skill / Handoff 契约，再按实际客户端决定是否增加 custom agent。

## 4. 自动指令预算

Codex 从项目根沿当前工作目录向下拼接 `AGENTS.md`，越近的文件越晚出现、优先级越高；默认合计上限为 **32 KiB**。超过上限时，最重要的就近规则反而可能无法加入。

2026-09-01 按 UTF-8 实测：

| 最深工作位置 | 累计字节 | 约合 |
|---|---:|---:|
| 项目根 | 7,466 | 7.29 KiB |
| `Assets/Game` | 16,095 | 15.72 KiB |
| `Assets/Game/Framework` | 26,177 | 25.56 KiB |
| Demo Modules | 31,008 | 30.28 KiB |

最深链还剩 1,760 字节（约 1.72 KiB）。维护时测量**根到最深目录的 UTF-8 合计**，不要只看单文件行数。当前接近上限，新增细节必须优先下沉到 guide、ADR 或 Skill，并尽量删除等量过期常驻内容。

```powershell
$paths = @(
  'AGENTS.md',
  'Assets/Game/AGENTS.md',
  'Assets/Game/Framework/AGENTS.md',
  'Assets/Game/Framework/Demo/Scripts/Modules/AGENTS.md'
)
$total = 0
foreach ($path in $paths) {
  $total += [Text.Encoding]::UTF8.GetByteCount((Get-Content -LiteralPath $path -Raw))
  [pscustomobject]@{ Path = $path; RunningBytes = $total }
}
```

## 5. 修改协作体系

### 添加或修改项目规则

1. 判断它是否无需关键词也必须影响每次决策；否则优先文档、Skill 或测试。
2. 放到覆盖范围最小的 `AGENTS.md`，删除被替代的旧规则。
3. 测量最深指令链，并验证新规则不会与更近目录冲突。
4. 协作拓扑或重大策略改变时同步本文。

### 添加 Project Skill

1. 在 `.agents/skills/<name>/SKILL.md` 写清触发条件、步骤、验证和失败处理。
2. 长参考放 `references/`，重复机械动作优先提供脚本。
3. 验证“能发现、该触发时触发、不该触发时不触发”。
4. 不为未采用产品创建 Skill 副本；接入层只能路由到权威正文。

### 实际接入新的 Agent

1. 按 `docs/ai-agent-onboarding.md` 从通用启动提示开始，并查询该产品**当时的官方文档**。
2. 先验证显式读取公共真值；只有重复使用确有收益时，才增加极薄的自动发现或工具连接配置。
3. 先做只读 smoke，再完成一条用户授权的代表性小任务；记录产品版本、日期、可用工具、已验证任务类型和未覆盖边界。
4. 产品升级后只重测受影响层；官方入口变化或产品停用时删除旧适配。

### 添加 Hook 或 custom agent

只有固定事件确实需要确定执行时才添加 Hook；只有相同隔离角色反复出现时才添加 custom agent。创建产品配置后同步本文，记录触发边界、权限、失败语义和验证结果。仓库配置不能替用户授予外部权限，也不得提交凭据。

### 修改用户级配置

客户端配置、已安装能力、账号和凭据属于个人/机器状态，不是仓库真值。除非用户明确要求，不把项目任务扩张成用户配置变更；确需修改时说明影响，并按该产品当前推荐方式保留可追溯记录。

## 6. 排查清单

### 规则没有生效

1. 确认 cwd、项目根和目标文件所在目录。
2. 确认客户端实际支持哪些规则入口、从哪里扫描、是合并还是就近覆盖。
3. 用产品当前提供的自检能力查看实际加载文件；没有自检时，显式让 Agent 读取并复述来源。
4. Codex 还需测量根到 cwd 的指令链，并检查更近的 `AGENTS.override.md` / `AGENTS.md`。

### Skill 没出现或误触发

1. 确认权威目录是 `.agents/skills`，并核对 frontmatter 的 name / description 与真实触发边界。
2. 自动发现不是完成前提；先显式读取完整 `SKILL.md`，不要只依赖名称列表。
3. 如果同名 Skill 出现多次，删除产品副本或旧适配，保留一份权威正文。

### Unity 工具或验证没有成立

1. 区分“能读文档”“能连接 MCP / CLI”和“完成代表性任务”，不要相互推断。
2. 先做 Editor 状态、当前场景和测试列表等只读检查，再运行最小测试。
3. 测试必须确认集合非空；视觉结论必须实际查看截图；失败后核对 Play 状态、脏场景和临时资源清理。

## 7. 维护责任

- 改代码的人同步相关 guide / ADR / AGENTS / Demo / Test，避免文档与行为分叉。
- 改协作拓扑、Skill、Hook、专用 Agent 或实际产品接入层的人同步本文。
- 每次大版本重新测量指令链、检查空入口/断链，并核对仓库说明与真实能力。
- 未采用的 Agent 不维护预配置、不列兼容矩阵、不作能力承诺；未来接入从 `ai-agent-onboarding.md` 开始。

## 8. 官方参考

- [Codex 最佳实践](https://learn.chatgpt.com/guides/best-practices)
- [Codex：AGENTS.md](https://learn.chatgpt.com/docs/agent-configuration/agents-md)
- [Codex：Skills](https://learn.chatgpt.com/docs/build-skills)
- [Codex：Hooks](https://learn.chatgpt.com/docs/hooks)
- [Codex：Subagents](https://learn.chatgpt.com/docs/agent-configuration/subagents)
- [Agent Skills 规范](https://agentskills.io)
- [Model Context Protocol](https://modelcontextprotocol.io)

其他产品只在真实接入时查阅其最新官方文档，接入结论记录产品版本与日期；不在本指南长期维护易过时的产品支持矩阵。
