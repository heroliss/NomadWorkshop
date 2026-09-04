# 《游牧工坊》技术 Spike

> 状态：**共享吸附基格 / 跨设备镜头 + 上下文行动规划 + 实体水罐 / 旱厕 + 精确停靠 / 语义改道 + Framework 分层连续建造 / 可回滚 NavMesh + 自主休闲 + 导航 / 交互 Harness + 存档骨架 + 3D 资产 Harness v0.22**，更新于 2026-09-04。当前已经跑通“玩家连续摆放设施并预演新旧功能点 → 几何合法即可更新导航并提交 → 居民自动避让施工占地 → 完整水罐方案经 Utility 决策 → 装水 / 搬运 / 饮用 / 代谢 / 如厕 → 空闲时漫步或发呆 → 施工切路后自动换 Slot / 换同功能设施”的可恢复闭环；它还不是完整 Foundation Prototype、商业垂直切片或正式美术基线。产品真值见 [`docs/nomad-workshop-game-vision.md`](../../../docs/nomad-workshop-game-vision.md)。

## 当前证明了什么

- 居民决策内核可以脱离 Unity 场景运行：按需求压力、工作价值、玩家优先级、个人倾向、等待时间与执行成本评分；
- 候选生成前新增完整行动方案估算：取得物品 / 洗手 / 移动 / 排队 / 转移 / 使用 / 收尾按步骤汇总，并分别保留舒适、洁净、隐私、社交、耗时、体力、预期风险和晕车修正；硬阻塞不会被低权重伪装；
- 货物搬运按能力匹配：水要求防漏容器，普通箱子和徒手均不可行；食物可配置少量徒手搬运，手或容器的洁净度形成可解释污染暴露；大件或姿态敏感货物也能要求徒手 / 双手搬运而拒绝容器，所有可行搬运方式仍需放回完整路径比较；
- 正式 Foundation 的补水已消费上述规则：唯一水罐有独立容量、位置和预留键，首次从车辆水箱旁取得，倒空后留在饮水站旁；候选同时估算真实 NavMesh 行程、取罐 / 装水 / 交付 / 饮用时长、体力和污染风险，再进入 Utility AI；
- `NomadFoundationVerticalSlice` 已通过 `MonoGameContextBase + MonoModelBase + MonoSystemBase + MonoViewBase` 消费 SSFramework：View 只发 Command 和订阅读模型，实时行动只在 System 的 `Update` 推进，Model 在 Inspector 中暴露设施、需求、库存与阻塞状态；
- 玩家可选择饮水站、野战厨房或旱厕，把鼠标命中投射为毫米 / 0.1° 量化的连续甲板姿态；正式 UI 的位置吸附为自由、0.2 / 0.3 / 0.4 / 0.6 m，非零档位共用 0.1 m 基础格与甲板原点，网格同步当前步长，默认旋转 45°；
- 桌面中键拖动 / 滚轮与移动端双指拖动 / 捏合共用镜头姿态、距离和俯仰边界，但保留设备独立灵敏度；双指手势结束前会抑制模拟鼠标事件，避免误确认建造；
- 设施权威记录只保存稳定实例 id、定义 id 与 `DeckPose`，复合有向矩形 Footprint、候选交互位和 3D Transform 都从定义与姿态派生；首发定义使用 ScriptableObject，灰盒稳定后可只替换视觉子树；
- 拖动候选时，复用数组的纯 C# 连通预演会重算候选与既有设施的每个 InteractionGroup / Slot；同组任一 Slot 可达即保留该功能，越界 / 占地重叠是硬错误，功能点不可达是仍可确认的软警告；
- 建造视图无需悬停就持续显示全部设施的所有停靠位；完全不可达的既有设施标红，部分功能失效标橙，每个失效 Group 另有独立红色功能警示；
- 建成饮水站后，一名居民会在站内有水时就近饮用、缺水且口渴时从初始车辆水箱紧急搬一桶、空闲时低优先级补到 2/2 且不误饮；体内水暂满只形成等待代谢 / 如厕的背压，旱厕会真实接收膀胱废物；
- 正式 Foundation 已接入小型甲板 NavMesh：0.1 m 实时预检复用 Agent 净空并收紧 Slot 映射，几何通过后创建候选障碍并异步更新导航；设施动作前居民精确抵达毫米 Slot 和朝向，普通散步才允许宽松采样；
- 居民移动保存“任务 + 设施功能 + 首选设施”语义而非一次性坐标；施工切断旧目标后重选 Slot、改去同功能设施或限频重试，不再以“建造后无法重新规划”为永久 Block；
- 居民新增 Recreation 需求；无必要水任务时，可达空地漫步与原地发呆作为两个完整候选参与确定性 Utility / Softmax 选择，当前基础比约为 1:2 且停留时间缩短；固定长椅、画板或观景点是下一个数据驱动扩展；
- 已锁定官方 AI Navigation 2.0.14；隔离 `NavigationInteractionSpike` 中 `DeckNavigationUtility : MonoUtilityBase` 同步构建小型甲板 NavMesh，空旷路径长度比为 1.000，穿过 27° 旋转柜体的路径长度比为 1.058；
- 两名 NavMeshAgent 能相向通过，代表性最小间距约 0.69m；同一柜门的 left / center / right 是共享容量 1 的备选 Slot，A / B 会从相反方向选择 right / left，而不是沿格中心移动或同时穿手操作；
- 柜门 InteractionGroup 的租约覆盖接近、Docking、开门、真实库存交接与关门全周期；资源提交不会提前释放设施容量，最终柜内 2 件物品分别进入两名居民携带库存；
- 版本 1 存档骨架已覆盖世界 / 旅途、毫米 + 0.1° 甲板姿态、设施 / 蓝图、真实库存批次、居民需求、Group / Slot 与中途行动检查点；`OutcomeCommitted` 防止加载后重复交接，NavMesh 路径和 RVO 速度明确按派生缓存重建；
- `SaveNomadWorkshopProgressCommand` / `LoadNomadWorkshopProgressCommand` 已真实穿过 SSFramework Context 与 `IStorageUtility` 的 JSON、FIFO、原子写和备份边界；当前证明协议与介质往返，尚未冒充玩家可操作的保存 / 读取流程；
- `DeckPose` 与 `ContinuousFacilityPlacementLedger` 已在无 Unity 依赖的 Simulation 中实现毫米 / 0.1° 连续姿态、位置 / 旋转独立可关吸附、复合有向矩形占地、多层甲板、稳定排序和原子提交；正式玩家场景的 Model / System / View 与存档 DTO 已共用这套姿态真值；
- 同意图目标先归并，紧急候选优先进入选择池，再在相对高分短名单中用确定性 Softmax 抽样；
- 同一世界 Seed、居民稳定 ID 与决策序号会重现相同随机值、候选分解和选择；
- 目标、材料与设施交互位可以全有或全无地预留，失败不会残留部分占用；
- `ResourceInventory` 与 `ResourceFlowLedger` 已把离散资源、总容量、来源数量、居民携带容量、最终目的容量和交互位放入同一条可查询契约；
- 搬运预留不会瞬移资源：拾取后资源真实进入居民随身库存，再在送达时进入目的库存；拾取后取消会保留手中货物并显式要求恢复，不会静默丢失；
- 设施加工会在开始前原子预留全部输入、输出所需净容量和工作位，取消不结算，提交才同时消耗食材 / 清水并产生餐食 / 污水；
- `FoundationResourceFlowSpike` 已让 Ada 完成食材储柜与净水箱 → 随身库存 → 厨房输入 → 厨房本地输出 → 随身库存 → 餐架 / 污水罐的完整物质链；污水罐满时会留下厨房输出并报告具体容量阻塞；
- `ResidentWaterCycle` 把连续口渴、延迟代谢进度与体内水 / 膀胱排泄物库存分开；喝水、代谢和如厕仍经同一资源账本提交，膀胱或厕所满时不会吞掉物质；
- 同一场景已跑通净水箱 → 实体搬运 → 饮水台 → 体内水 → 膀胱 → 厕所暂存桶 → 实体搬运 → 车辆废物罐；厨房污水和人体排泄物共享总容量但保留资源身份；
- 参数化厨房的 `StorageAccess`、`WaterInput`、`WasteOutput`、`WorkPosition` 和中门 Pivot 已被运行时实际消费；门由任务阶段驱动，动画不拥有经济结算；
- Unity 展示层能把选中行动推进为“走到设施 → 执行 → 结算 → 再决策”，并显示中文诊断面板；
- Quaternius Universal Base Characters 的选定模型在 Unity 6000.3 中生成**有效 Humanoid Avatar**；
- Universal Animation Library 免费标准版的无 Root Motion FBX 导入出 43 个 30 FPS Human Motion；项目只抽取 Idle、Walk、Pickup、Fixing、Sitting 五个 `.anim`，并用稳定语义状态隔离上游 Clip 名；
- 运行时实际实例化共享 Humanoid、禁用 Root Motion、按模拟倍率播放动作；任一资产或状态契约失效时会回退程序假人；
- 六个灰盒设施都通过 `FacilityInteractionAnchor` 声明站位、朝向、动作语义和可选手部目标，已经覆盖站立取用、跪姿维修、拾取搬运与坐姿休息等三类以上接缝；
- 内嵌 FBX 材质被显式重映射为项目自有 URP Lit 材质，身体与眼睛法线贴图按 Normal Map 导入；
- Blender 5.2 的两个版本化 `bpy` Harness 已经生成 14 Mesh 的废土储物箱，以及从 28 个来源部件按材质合并为 3 Mesh 的水循环设施；两者都输出 `.blend`、FBX、预览和 manifest；
- 独立 AI Mesh 输入入口会读取已验证 `.blend`，隐藏棚拍地面和有色灯光，输出透明背景、中性三点光的 640×640 RGBA 单图及来源 / 产物 Hash；它不修改源资产，也不把外部 Provider 接入冒充为已经验证；
- 第二个 Harness 还会确定性生成三个材质族的 Base Color、OpenGL Normal、URP Metallic Smoothness 与 Occlusion，共 12 张 512×512 PBR 贴图，并在导出后重新导入 FBX 检查几何、封闭拓扑与 UV；
- 同一轮输出包含 Hero / Front / Side / Top / Wireframe / UV Checker 六视图 Contact Sheet；manifest 固定尺寸、布局、哈希和 `manual_review_required` 边界，自动化不能把证据存在误报为美术通过；
- Unity Editor 工具核对 FBX、生成脚本、Contact Sheet 与全部贴图哈希，按用途配置证据图和 PBR Texture Importer，创建三个外部 URP/Lit 材质并显式 Remap，再生成 Root / Visual Identity 的 Prefab、一个 Bounds BoxCollider、预览场景和审计报告；
- 两个静态 FBX 的坐标轴均已实际烘焙并验证直立。储物箱是 784 Blender Vertex / 3024 Runtime Vertex / 1512 Triangle；水循环设施是 3928 Blender Vertex / 8083 Runtime Vertex / 7772 Triangle，来源、FBX 回读与 Unity Triangle 一致，拓扑与 UV 质量门禁通过；其 1001.091 px/m 是包含材质平铺的有效密度，不是唯一贴图内存预算。
- 首个 Rodin Gen-2.5 Reviewed Hero 候选已通过 Blender Bridge 进入 Blender，并经通用 AI Mesh Intake 保留源字节、归档 PBR 贴图、清理副本、FBX / GLB 回读和 SHA-256；Bridge 实际交付 2K 而非网页 8K 设置，且必须人工保存图像或 Pack，不能把插件连接成功误报为文件已经归档；
- Rodin 野战厨房在 Unity 中是 1 Mesh / 1 Material Slot、18,924 Source Vertex / 22,058 Runtime Vertex、37,903 Triangle；Bounds、URP/Lit 通道、Prefab、BoxCollider 与预览场景审计成立，固定相机能看见青色旧漆、不锈钢、橙色安全件、织物与软管；源候选仍有 5 个重复面、6 个几何岛和不可拆分部件，保持人工复核且未批准为生产资产；
- 同一野战厨房另有一条 Unity 参数化代理路线：Profile 确定 `2.30 × 2.00 × 0.88 m` 玩法尺寸，用 ProBuilder 6.1.2 在 Editor 临时生成并按材质合并，落盘为 1 个静态主体 + 3 个独立门的普通 Mesh（4,856 Vertex / 2,172 Triangle），运行时不保留 `ProBuilderMesh`；
- 参数化 Prefab 把根 Collider、工作 / 手部 / 储物 / 进水 / 排污 Anchor 与 `Visual_Prototype` / `Visual_Final` 分离，三扇门有稳定 Pivot；版本化预览场景避免重复生成造成 local fileID 漂移，连续生成的资产 GUID 与 Dependency Hash 已稳定；
- 项目默认 `Renderer2D` 保持为 Universal RP Asset 的 index 0；游戏 Spike 从 URP 官方模板生成唯一的次级 `UniversalRendererData`（index 1），只有隔离预览相机显式选择它；
- 固定 3D Game View 已实际看到两个道具的体积、阴影、金属高光和青 / 橙 / 黑材质层级；水循环设施的划痕、细微法线与裸露金属响应可读，没有粉材质、黑屏或错误姿态。自动审计仍把最终审美结论留给人工。

这些证据仍不能证明游戏好玩、正式画面达标、多人居民调度自然、IK 接触可靠或参数已经平衡。实体物流当前只有一名居民和固定任务链；3D Renderer 也只在可删除的 Harness 中闭环。程序磨损仍缺少基于 Mesh 边缘、遮挡、重力与用途的细节，正式车辆镜头、后处理、Shader / VFX、目标平台性能和统一艺术指导仍未成立。

## 目录与边界

```text
NomadWorkshop/
├── Simulation/       # 无 UnityEngine 依赖：效用、占用、物质链与版本化存档真值
├── Runtime/          # Framework 接线、灰盒表现、实体物流、导航 Adapter 与存档 Command
├── Foundation/       # 甲板与设施 ScriptableObject 定义资产
├── Editor/           # 游戏本地 Importer、动作抽取、材质 / Renderer 配置和审计工具
├── Animation/        # 五个项目动作和稳定 Animator Controller
├── Materials/        # 项目自有 URP Lit 材质，不直接修改第三方内嵌材质
├── ThirdParty/       # 最小选入资产、原始许可证、来源、下载哈希与重建说明
├── Scenes/           # 只经 Unity Editor 保存的 Spike 场景
├── Spikes/           # 可删除的 Blender Import 与 URP 3D Rendering 证据，不是 Runtime Content
├── Prototype/        # 参数化代理 Profile、普通 Mesh / 材质、Prefab 与版本化预览
└── Tests/
    ├── EditMode/     # 决策、库存 / 资源流、资产、Controller 与锚点契约
    └── PlayMode/     # 回退路径、真实 Humanoid 和可见搬运 / 加工闭环
```

`ResidentActionPlanEvaluator` 先把完整步骤与上下文折算为可解释候选，`UtilityDecisionEngine` 再决定“现在做什么”；两者都不拥有寻路、任务进度、资源结算或动画。`CargoTransportPlanner` 只判定货物与徒手 / 容器能力及污染暴露，不越权选择脱离完整路程的局部最优容器。`ReservationLedger` 处理独占键，`ResourceFlowLedger` 在其上增加来源数量、携带容量、目的容量与设施加工的原子语义。`ResidentHumanoidPresentation` 只呈现模拟结果，Animator 不拥有移动或任务完成真值。`FacilityInteractionAnchor` 只声明表现接缝，尚未驱动 IK。

`Game.NomadWorkshop.Simulation` 继续保持无 Unity / Framework 依赖，用于确定性的效用、预留、库存、物质链、连续摆放与版本化存档真值；`Game.NomadWorkshop` Runtime 已显式引用 `Game.Framework`，由 Mono Context / Model / System / View 把纯内核接到 Unity 生命周期、Inspector、Command、NavMesh 和 3D 表现。当前没有为了游戏方便修改 Framework Core；只有产品中反复出现、且跨游戏成立的阻力才考虑回流。

存档同样遵守这条边界：`Simulation/Persistence` 只描述可迁移的业务快照与恢复决策，`Runtime/Persistence` 才通过异步 Command 借用框架 `IStorageUtility`。有长期价值的场景及明确退出条件见 [`docs/nomad-workshop-foundation-vertical-slice.md`](../../../docs/nomad-workshop-foundation-vertical-slice.md)；导航、物质链和资产 Preview 是开发 Harness，不进入正式 Player Build，也不写玩家默认存档槽。

正式 Foundation 已完成从逐格摆放 / 四方向 A* 到连续 `DeckPose`、可选吸附、InteractionGroup / Slot、可修复可达状态和可回滚 NavMesh 更新的迁移。旧格子算法只保留为无场景纯 C# 回归；多人局部避让仍由隔离 Harness 验证，尚未冒充正式多居民实现。详见 [`docs/nomad-workshop-navigation-interaction-design.md`](../../../docs/nomad-workshop-navigation-interaction-design.md)。

## 运行与观察

当前优先运行 Framework 分层的最小垂直切片：

1. 执行 `Assets/SSFramework/游牧工坊/Foundation/创建或打开最小垂直切片`，或直接打开 [`Scenes/NomadFoundationVerticalSlice.unity`](Scenes/NomadFoundationVerticalSlice.unity)；
2. 进入 Play，以数字键 `1–3` 或左侧按钮选择设施，移动鼠标连续预览，`Q / E` 或右键按当前步长旋转，左键确认，`Esc` / 面板按钮取消；面板可循环自由、0.2 / 0.3 / 0.4 / 0.6 m 档位并开关可视网格，滚轮缩放、中键环绕；触屏可用双指质心拖动旋转、捏合缩放；
3. 建造模式会始终显示所有设施 Slot 的绿 / 红可达标记；试着堵住既有设施，它会持续标红 / 橙但候选仍可确认。建成饮水站与旱厕后可观察搬水、饮用、代谢、如厕、空闲补货和自主休闲；施工封死当前饮水站时，任务应转向另一座可达饮水站或进入可重试等待；
4. 分层、摆放真值、验证范围和下一步迁移顺序见 [`docs/nomad-workshop-foundation-vertical-slice.md`](../../../docs/nomad-workshop-foundation-vertical-slice.md)。

连续导航目标可以单独观察：

1. 执行 `Assets/SSFramework/游牧工坊/Foundation/创建或打开连续导航与交互 Spike`，或打开 [`Scenes/NavigationInteractionSpike.unity`](Scenes/NavigationInteractionSpike.unity)；
2. Play 后自动观察两人相向会车、A 预留柜门、B 等待、A 取物关门、B 从另一候选位接替；
3. 左侧面板会显示空旷 / 绕障路径比、最小间距、候选位、全周期争用次数和物品实际位置；绿色圆点是同一柜门的备选姿势，不是三个并发容量。

更宽但仍是旧单体 Harness 的实体物流场景可用于对照：

1. 执行 `Assets/SSFramework/游牧工坊/Foundation/创建或打开实体物流灰盒`，或直接打开 [`Scenes/FoundationResourceFlowSpike.unity`](Scenes/FoundationResourceFlowSpike.unity)；
2. 进入 Play，观察 Ada 从食材储柜和净水箱拾取资源；左侧“Ada 携带”会变为 `1 / 1`，人物手上同时出现对应箱 / 桶；
3. 厨房只向本地餐食口和污水口产出，再由 Ada 清运到最终餐架和车辆废物罐；中门随任务阶段开合；
4. 第一批完成后点击“运行一次饮水 → 排泄 → 厕所清运链”，观察水依次进入饮水台、体内、膀胱、厕所暂存桶与居民携带库存；
5. 车辆废物罐容量为 2，一份厨房污水与一份人体排泄物会恰好装满；继续生产会把无法清运的物质留在原容器并显示目的地已满；
6. 完整不变量、取消语义与后续任务化路线见 [`docs/nomad-workshop-resource-flow-foundation.md`](../../../docs/nomad-workshop-resource-flow-foundation.md)。

旧的决策评分场景仍可单独观察，但不代表正式物流：

1. 用 Unity 打开 [`Scenes/UtilityAiSpike.unity`](Scenes/UtilityAiSpike.unity) 并进入 Play；
2. 左侧面板显示需求、动力损伤、积压、当前行动以及每个候选的效用分项、概率和排除原因；
3. “制造严重故障”用来观察紧急维修、跪姿动作和设施朝向，“提高 / 降低”用来观察玩家优先级；
4. “重置”恢复固定 Seed 和初始状态，便于重现同一决策序列；
5. 若场景中的模型或 Controller 引用被清空，标题下方会明确显示程序假人回退，不会假装 Humanoid 管线仍成立。

Editor 菜单 `Assets/SSFramework/游牧工坊/配置并审计 Humanoid 资产` 会重放角色导入、贴图类型、材质映射与最终产物审计。完整动作源默认不在仓库；重新抽取步骤与上游哈希见 [`ThirdParty/QuaterniusUniversalAnimationLibrary/SOURCE.md`](ThirdParty/QuaterniusUniversalAnimationLibrary/SOURCE.md)。

Editor 菜单 `Assets/SSFramework/游牧工坊/Blender Import Spike/配置并审计储物箱` 会重放 FBX 哈希校验、Importer、URP 材质 Remap、Prefab、Collider、预览场景和最终审计。重建步骤、已验证数值和删除边界见 [`Spikes/BlenderImport/NW_StorageCrate_01/README.md`](Spikes/BlenderImport/NW_StorageCrate_01/README.md)。完整原理见 [`docs/blender-art-pipeline.md`](../../../docs/blender-art-pipeline.md)。

Editor 菜单 `Assets/SSFramework/游牧工坊/Blender Import Spike/配置并审计纹理化水循环设施` 会重放 12 张 PBR 贴图、六视图 Contact Sheet、拓扑 / UV 和有效纹素密度证据，配置外部 URP/Lit 材质、3 Mesh Prefab、Collider 与独立 3D 预览。重建和验收边界见 [`Spikes/BlenderImport/NW_WaterRecycler_01/README.md`](Spikes/BlenderImport/NW_WaterRecycler_01/README.md)。

Editor 菜单 `Assets/SSFramework/游牧工坊/AI Mesh Spike/配置并审计 Rodin 野战厨房` 会从已入库的 Intake 报告开始，核对 Intake 脚本、FBX 与三张运行时贴图哈希，配置 Base Color / Normal / MetallicSmoothness、外部 URP/Lit 材质、单 Mesh Prefab、BoxCollider 与隔离预览场景。它只证明候选能安全进入 Unity，不会把 `candidate-only` 自动升级为生产美术。Bridge 陷阱、拓扑和删除边界见 [`Spikes/BlenderImport/NW_FieldKitchen_01_Rodin/README.md`](Spikes/BlenderImport/NW_FieldKitchen_01_Rodin/README.md)。

Editor 菜单 `Assets/SSFramework/游牧工坊/参数化道具/创建或定位野战厨房配置` 与 `生成并审计野战厨房` 用于快速修改玩法尺寸、重新生成普通 Mesh / Prefab 并检查 Collider、Anchor、门轴、材质与预算。它是前期可直接承载玩法的精确代理，不取代 Art Bible 或正式美术；实现原则、替换契约和已记录坑见 [`docs/nomad-workshop-parametric-prop-harness.md`](../../../docs/nomad-workshop-parametric-prop-harness.md)。

Rodin Smart Mesh 的下柜可动化 Spike 已用 Blender 无头 `bpy` 跑通。首轮中央门矩形裁剪因融合大面误伤两侧柜体和台面，已判定无效并废弃；修正版不删除 Rodin 面，保留左侧通风/控制区与右侧抽屉为静态主体，在三个门区前重建内衬和独立门件，分别建立 `LeftLower`、`Center`、`RightLower` 铰链 Pivot。资产不含烘焙动画，FBX / GLB 往返均保留 4 个 Empty、44 个 Mesh 与 47 条父子关系，目标是在 Unity 由设施状态直接旋转 Pivot。它当前仍是被忽略目录内的 rigging Spike，不替换上述静态 Unity 候选；若玩法需要真实储物深度，再正式重拓扑整个下柜模块。

Editor 菜单 `Assets/SSFramework/游牧工坊/Rendering Spike/配置并审计 3D Renderer` 会从当前 URP 包的官方模板幂等生成次级 3D Renderer、材质和隔离预览场景。它拒绝覆盖未知默认 Renderer，审计默认 index 与相机 index，并把报告写到被忽略的 `ArtPipelineOutput/`。重建与删除边界见 [`Spikes/Rendering/Urp3D/README.md`](Spikes/Rendering/Urp3D/README.md)。

## 第三方资产边界

- 角色与动作均来自 Quaternius 官方免费标准包，许可证为 CC0 1.0；
- 仓库保留一个约 0.83 MB 基准角色、实际使用贴图和五个抽取动作，不保留 129 MB 角色压缩包或 23.75 MB 完整动作源 FBX；
- 当前 Base Character 是穿基础内衣的中性身体基体，只验证 Avatar、比例、材质和重定向，**不是废土服装或正式角色美术**；
- 上游免费包的 Roughness 贴图未进入仓库，因为当前 URP Lit 材质没有可靠的通道打包流程，不为“资产齐全”保留未使用文件；
- 许可证、官方下载页、upload id、文件大小与 SHA-256 分别记录在两个 `SOURCE.md` 中；
- 储物箱是项目脚本生成的自有技术探针，不依赖第三方模型；可重建 `.blend` 和批量输出被忽略，只提交一个小型 FBX + manifest 作为跨机器 Unity 导入证据。
- 水循环设施的 Mesh 和 12 张贴图同样由项目脚本确定性生成；它证明 PBR 契约和三材质合并，不代表程序噪声已达到正式手绘或 Mesh-specific 材质质量。
- Rodin 野战厨房来自用户账号下的受监督云端实验；仓库保留规范化 FBX、运行时必要贴图和机器可读 Intake 报告，不提交账号、Cookie、原始 Provider 会话或浏览器状态。商业使用前仍要归档生成当日条款、订阅层和输出权利。
- ProBuilder 6.1.2 是项目已有的 Unity Package，只被游戏本地 Editor 生成程序集引用；生成 Prefab 是普通 Mesh，不把 ProBuilder 变成 Runtime 或 Framework Core 依赖。

## 当前验证证据

- 编译：CompilationPipeline 0 error / 0 warning；
- 共享吸附基格 + 镜头定向 EditMode 16/16（job `15a8261b90eb`），覆盖自由 / 0.2 / 0.3 / 0.4 / 0.6 m、共同 0.1 m 基础格、双指拖动 / 捏合分解与镜头限位；
- 正式 Foundation PlayMode 15/15（job `42c9a8476b07`），覆盖同步 0.2 / 0.6 m 网格、自由模式自动隐藏、精确停靠、45° 邻接预览 / 落地一致、施工后改去第二座饮水站，以及 `DestinationFull → 如厕 → 恢复饮水`；
- 可重建场景管线 EditMode 1/1（job `c3046ae76822`）；
- 最终 PlayMode 请求的类名过滤未被 Test Runner 正确收窄，实际完成了全项目 790/790（job `1b7dc395827c`，120.1 s）；它是有效的扩大回归，但过滤失效仍记为 Harness 问题；
- Game View：已实际查看 45° 紧邻既有饮水站时 0/3 Slot 可达但仍可确认的红色幽灵；`Screenshots/nomad-foundation-45deg-preview-parity.png` 同时显示既有设施三个绿色 Slot 与候选三个红色 Slot，无需悬停。同步 0.2 m 网格与基础建造模式见 `Screenshots/nomad-foundation-exact-docking-build-mode.png`（两者均为本地忽略证据）。

测试重点覆盖危险口渴压过休闲、多需求行动按实际压力得分、同 Seed 重现、不同 Seed 只在短名单内变化、重复设施不放大意图概率、无正效用时安全等待、冲突预留不泄漏，以及 Avatar / Clip / 材质 / Controller / 锚点的导入契约。

## 明确未做

- 三名以上居民同时投标、长时间拥堵、复杂设施队列与 20 人压力；当前正式水循环仍是一人，隔离导航 Harness 只证明两人局部避让和一个柜门的全周期互斥；
- 行动承诺和任务老化的长时间平衡统计；
- 水罐寻找与最小旱厕使用已接入正式 Foundation；厕所清运、食物容器 / 餐具、洗手、洁净度和晕车仍只有旧 Harness 或纯 C# 方案规则，尚未接入完整设施、动画或长期平衡；
- 正式废土服装、模块化发型/背包、人物差异、面部与布料；
- Animation Rigging、手部 IK、工具挂点实际消费与专用交互修正；
- 蓝图与居民搬料 / 施工、建造成本、拆除 / 搬移、边缘 / 连接口吸附、库存 UI，以及现有运行世界的完整捕获 / 加载重建和玩家存档界面；
- 多座同功能设施目前已有真实 Slot 改道，但 Foundation 饮水站水量仍是共享切片库存；每设施实例库存与改道时的资源租约迁移尚未完成；
- 正式 UI、艺术指导、音效、性能采样、Player Build 与玩家体验验证；
- 基于 Curvature / AO / Position 的 Mesh-specific 贴图、唯一 UV / 屏幕占比关联的正式 Texel Density 预算，以及目标平台贴图内存基线；
- 项目默认 Renderer 仍是 `Renderer2D`；次级 3D Renderer 只证明隔离镜头可用，尚未决定正式游戏场景的 Renderer 组织、后处理、VFX、灯光风格和性能预算。

下一步先把临时整数水单位迁移为 mL 定点量、建立设施实例库存，以及统一模拟时钟、可保存的独立随机流 / 行动样本 / 分布 Harness；完成共同地基后，先用一个设施验证积尘、老化、保养、故障与修理在暂停 / 加速 / 存档后的确定性，再用长椅或观景点抽离决策协调，并以一份餐食验证“脏手就地吃 / 先洗手 / 取餐具或去餐桌”的多候选选择和随机摄入量。完整四季不会在基础时钟成立前抢跑；随后才继续蓝图搬料施工、运行世界恢复和正式多居民 Agent。
