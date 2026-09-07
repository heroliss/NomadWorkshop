# 《游牧工坊》Foundation 与技术实验

> 2026-09-08 阶段收尾：首版 HUD 布局/主题 15/15、NW5 19/19、Foundation 108/108。修正居民完成爱好后反复占位、错过让路轮询的时序，保留原始失败和明确交错的红/绿证据。常驻居民选择、水量/燃料、暂停与倍速已可用，详见[HUD 验证记录](../../../docs/nomad-workshop-hud-first-pass.md)。下文旧数量属于相应历史阶段。

> 状态：**Foundation 与技术实验 v0.51**，更新于 2026-09-06。正式场景已接入[首段驾驶](../../../docs/nomad-workshop-driving-integration.md)与[有限取水 / 污物清运 / 备件补给](../../../docs/nomad-workshop-stop-resource-ownership.md)，保留已有生活、水循环、建造与维修。[三居民接线](../../../docs/nomad-workshop-three-residents.md)已包括原生 Agent、个人行动所有权、共享工具、全员召回、多居民 v9 检查点和居民选择。原生测试与正式 1× 运行证据分开记录，当前进度及已知边界见[Foundation](../../../docs/nomad-workshop-foundation-vertical-slice.md)，产品目标见[愿景](../../../docs/nomad-workshop-game-vision.md)。玩家保存/读取/取消已接入旅程面板，身体碰撞、让路与续玩工程验收完成；最新 Foundation PlayMode 106/106。它仍是基础切片，不代表长期平衡或发行门槛已经成立。

切片同时用于检验游戏设计、SSFramework 与 AI 自动工作流。优先通过小任务探索未经历的边界，依据实际失败与摩擦改进工具、文档和框架；不以堆叠功能作为唯一进度指标。

2026-09-06 已按用户认可的 J 参考启动[温暖工坊美术样板](../../../docs/nomad-workshop-art-first-pass.md)。独立试玩场景是 `Scenes/NomadWarmWorkshop.unity`：三名工作服 Humanoid、六类实际设施、可分件履带车和沙漠接入同一套 Foundation 玩法。Unity 菜单“Assets/SSFramework/游牧工坊/首版美术”提供导入及实际持桶/启程观察入口。美术和动作仍在迭代，原 `NomadFoundation` 保留为工程基线；两者目前都是单层，[局部扩建、多层与围护](../../../docs/nomad-workshop-expandable-decks-design.md)尚未接通。

首批[设施工作反馈](../../../docs/nomad-workshop-facility-feedback.md)已接入实际装水阀门/软管、倾倒补水盖、维修上滑盖和局部故障灯；按有效工作位租约和绝对业务进度驱动。[人物接触](../../../docs/nomad-workshop-resident-contact.md)随后补充掌心朝向、肘部净空、站姿倾倒与完整携罐体积检查。现在水罐通过独立侧面工作位完成蹲身拿起、抬起、实际绕行、下放和松手；已取出的水在送达工作位暂不可用时保留交付任务，避免罐中残留无人续送的水。Foundation 当前 **106/106**，原人物美术场景 PlayMode **12/12**，包含连续拾放与双腿净空、下放中恢复、倾倒、高位回水、双饮水站隔离和模拟时间驱动的长帧扬尘。当前已有三种正常人物候选；[可替换模型与交互绑定](../../../docs/nomad-workshop-model-interaction-bindings.md)的水设施 P0 已验证可变锚点、区域及机构，空间快照与人物同步仍待推进；局部顶棚显示已接入下述 NW5；维修工具握持与 HUD 仍待完善。

此前单款 NW2 对比基线为 `Scenes/ResidentAppearanceSample.unity`，菜单“Assets/SSFramework/游牧工坊/首版美术/创建或打开现成人物对比”也可打开；独立持桶楼梯基线为 `Scenes/ResidentStairSample.unity`，对应菜单为“创建或打开现成人物楼梯对比”。一个 Quaternius 现成服装外观已接入三居民；修正内袖肩部穿插后，该模型工坊 12/12、楼梯 4/4 通过，并查看实际拿放和上楼画面。旧场景三人同外观；当前三种外观使用下面的 NW3 入口。UMA / MPFB 的候选研究、完整证据和后续可选游戏内捏人范围见[参数化人物制作](../../../docs/nomad-workshop-character-authoring.md)。

**当前美术试玩优先打开 `Scenes/ReferenceVehicleSample.unity`。** NW5 已将分层钢板边框、内凹检修盖、错缝甲板、折面车首、旧漆和局部棚架接入同一三居民场景，最新 PlayMode 19/19。默认剖开屋顶，左下按钮或 **H** 显示外观；进入建造自动剖开，退出恢复偏好，详见[顶棚显示样板](../../../docs/nomad-workshop-canopy-cutaway.md)。[首版玩家 HUD](../../../docs/nomad-workshop-hud-first-pass.md)提供三居民标签、常驻水量/燃料与右下时间控制，**空格**暂停/继续；详细面板可滚动，小窗口同步缩放输入。完整来源、再生成入口和实机差距见[参考结构造型](../../../docs/nomad-workshop-reference-form-study.md)。原人物外观基线 `Scenes/ResidentCrewSample.unity` 保留，两个场景只有车体 Prefab 引用不同，共用这套 HUD。当前棚顶尚无围护建造和天气效果。

三人已改为短发、束发和灰发胡须的不同外观，保留同一套真实玩法；原工坊/身份保持检查 13/13、三款人物各自的楼梯检查 4/4。菜单“Assets/SSFramework/游牧工坊/首版美术/生成并打开三居民外观候选”用于按固定离线配方重新接线；直接打开已保存场景即可 Play。上面的单款 NW2 和原 Warm 场景保留为对比基线。服装仍有中世纪特征，游戏内捏人尚未接入。

另有独立[持桶楼梯实验](../../../docs/nomad-workshop-stair-traversal-spike.md)：`Scenes/StairCarrySpike.unity`。18 级实体台阶连接相隔 3.2 m 的平台，复用同一个 Humanoid、水罐和移动 Adapter，验证上楼/下楼、暂停、取消与恢复；它为后续多层建造提供证据，尚不属于正式跨层玩法。

2026-09-07 新增 `Scenes/FacilityBindingSample.unity`：沿用三居民玩法，水箱/饮水站更换接口位置、阀门方向与盖板转轴，并增加可选功能点；共享绑定执行器按实际工位与工作进度驱动，业务库存和存档结构不变。绑定 EditMode 11/11、变体 PlayMode 14/14；空间区域生成与人物开门同步仍待后续实验。完整阶段收尾与回归结果见[阶段检查点](../../../docs/nomad-workshop-stage-checkpoint-2026-09.md)。

## 当前证明了什么

- 旅途规则 `NomadJourneySession` 已接入正式驾驶台：2 km 的旧营地—干河驿站路线、实际到岗、需求离岗、建造迁离时停车、目标取消与返回、微米 / 皮升精确存取。Context/Bag 独立实验覆盖框架所有权；三名正式居民分别拥有驾驶、资源、物品与交互空间租约。旅程面板显示全员状态，可派遣取水 / 清运 / 备件补给和召回外勤；任何外勤携带者尚未归车时，其他居民也不能开车。地点水源与污物接收容量有限，离开和读档不刷新。污物桶按原厕所实例拆卸与回装，倾倒时才转移内容，拆卸期间所有人都不能使用该厕所。驿站另有两只实体维修包，可搬入维护托盘并用于后续维修；路边景物运动仍待实现；
- 三名居民由实际 NavMeshAgent 行走和局部避让，停靠/暂停保持脚底与朝向，取消停止原目的地。闲人通过正式意图实际让位；软移动过久无进展会重选活动，携物与工作租约保持原所有权。同步 Soak **0.10.0** 标记为角点快进，只证明业务规则，不提供原生交通结论；
- 居民决策内核可以脱离 Unity 场景运行：按需求压力、工作价值、玩家优先级、个人倾向、等待时间与执行成本评分；
- 候选生成前新增完整行动方案估算：取得物品 / 洗手 / 移动 / 排队 / 转移 / 使用 / 收尾按步骤汇总，并分别保留舒适、洁净、隐私、社交、耗时、体力、预期风险和晕车修正；硬阻塞不会被低权重伪装；
- 货物搬运按能力匹配：水要求防漏容器，普通箱子和徒手均不可行；食物可配置少量徒手搬运，手或容器的洁净度形成可解释污染暴露；大件或姿态敏感货物也能要求徒手 / 双手搬运而拒绝容器，所有可行搬运方式仍需放回完整路径比较；
- 正式 Foundation 的补水已消费上述规则：唯一水罐有独立容量、位置、精确设施锚点和预留键；每座饮水站按实例 id 拥有独立 6 L 库存，候选同时估算真实 NavMesh 行程、取罐 / 装水 / 交付 / 饮用时长、体力和污染风险，再进入 Utility AI；
- 车辆水箱已接入首条正式环境—状态链：每个十分钟生活日有一场由世界 Seed 与生活日序号独立采样的 90 秒沙尘暴；天气没有第二只可漂移的时钟或存档布尔值，设施积分会在天气边界精确分段，所以大步长、逐帧与读档续跑得到相同磨损、维护欠账、积尘和风险余量；
- 首个具体故障是“出水阀卡滞”。它会阻止新的取水方案，并在居民尚未取货时释放资源、交互位和路径租约回到可重试决策；居民随后把故障、已储水量和口渴风险放入同一 Utility 仲裁，真实走到 `maintenance-supply` 取得维修包，再精确抵达 `service-valve`，投入受工作效率影响的工时，成功时才原子消耗备件并开启新风险周期。开发 Command 仍保留为可审计捷径，不冒充玩家维修；
- `NomadFoundationVerticalSlice` 已通过 `MonoGameContextBase + MonoModelBase + MonoSystemBase + MonoViewBase` 消费 SSFramework：View 只发 Command 和订阅读模型，实时行动只在 System 的 `Update` 推进，Model 在 Inspector 中暴露设施、需求、库存与阻塞状态；
- 玩家可选择饮水站、野战厨房、旱厕或观景画架，把鼠标命中投射为毫米 / 0.1° 量化的连续甲板姿态；正式 UI 的位置吸附为自由、0.2 / 0.3 / 0.4 / 0.6 m，非零档位共用 0.1 m 基础格与甲板原点，网格同步当前步长，默认旋转 45°；
- 桌面中键拖动 / 滚轮与移动端双指拖动 / 捏合共用镜头姿态、距离和俯仰边界，但保留设备独立灵敏度；双指手势结束前会抑制模拟鼠标事件，避免误确认建造；
- 设施权威记录只保存稳定实例 id、定义 id 与 `DeckPose`，复合有向矩形 Footprint、候选交互位和 3D Transform 都从定义与姿态派生；首发定义使用 ScriptableObject，灰盒稳定后可只替换视觉子树；
- 拖动候选时，复用数组的纯 C# 连通预演会重算候选与既有设施的每个 InteractionGroup / Slot；同组任一 Slot 可达即保留该功能，越界 / 占地重叠是硬错误，功能点不可达是仍可确认的软警告；
- 建造视图无需悬停就持续显示全部设施的所有停靠位；完全不可达的既有设施标红，部分功能失效标橙，每个失效 Group 另有独立红色功能警示；
- 居民、建造、开发三个面板共用同一切换入口；只要目标不再是建造面板，就先发送正式退出 Command，因此幽灵、网格、停靠位和放置区域诊断不会残留在普通/开发视图；
- `PlacementRegionLedger` 已把设施局部放置区域、物品 footprint、稳定候选姿态与预留事务纳入无 Unity 依赖的空间真值；`NomadWorldItemDefinition` 统一声明类别、尺寸、安全边距、朝向和灰盒表现。车辆水箱/饮水站声明水罐停放区，野战厨房声明台面区，水箱另声明备件维护托盘。杯具移动会联合锁定精确来源恢复位与目标姿态；通用 Use Lease 则覆盖“保留 → 拿起 → 消耗”，取消或半途存档把维修包恢复到精确来源，成功维修才提交物品消失。当前右手锚点是可替换表现接缝，不冒充正式 IK；
- 建成饮水站后，三名居民分别在实际有至少 300 mL 水的可达实例就近饮用、缺水且口渴时争取唯一 5 L 防漏水罐搬运最多 2 L、空闲时把各站低优先级补到 4 L 且不误饮。工具或工作位被占用时等待重试；体内水暂满只形成等待代谢 / 如厕的背压，旱厕真实接收各自膀胱废物；
- 正式 Foundation 已接入小型甲板 NavMesh：0.1 m 实时预检复用 Agent 净空并收紧 Slot 映射，几何通过后创建候选障碍并异步更新导航；设施动作前居民精确抵达毫米 Slot 和朝向，普通散步才允许宽松采样；
- 居民逻辑位置与运行时 `Resident 01/02/03` 根节点统一代表脚底，Y 固定在甲板表面 0；胶囊中心的 0.55 m 半高只属于 View 子视觉，不再同时写入 System 根位置导致悬空；
- 居民移动保存“任务 + 设施功能 + 指定 InteractionGroup + 首选设施”语义而非一次性坐标；取水、送水、如厕、爱好、备件托盘和维修阀不会在重规划时偷换成同一设施上的另一功能点。施工切断携水目标后，会把目的库存容量、资源交互键和语义路径原子迁移到另一实例；尚未取货的任务则释放预留后重选，不再以“建造后无法重新规划”为永久 Block；
- 居民已拥有正向健康、娱乐满足与心情，以及负向疲劳与压力等独立连续状态；严重缺水平滑损害健康，健康归零进入可保存的死亡终态。发呆和闲逛只缓慢降低疲劳 / 压力并略微改善心情，不补充娱乐；缺少床椅时可坐卧地面进行恢复较慢、轻微损失心情的兜底休息。可建造观景画架提供三个共享容量的备选 Slot；居民把实际路程、作画偏好与娱乐收益放进同一 Utility 方案，只有精确到位后的作画阶段才连续恢复娱乐；
- 居民基础工作能力、健康、疲劳和压力共同形成连续工作效率；可控压力暂时提高动作速度，接近崩溃时增益回落，相关失误与健康代价则保留给风险通道。取得水罐、装水和交付等工作阶段各自在开始时固定采样一次小幅速度差异，不逐帧改变进度；
- 无必要水任务时，可达空地闲逛、原地发呆与已建爱好设施共同参与确定性 Utility / Softmax；没有画架时前两者的基础比仍约为 1:2，画架会按需求、个人倾向与路程自然分走概率。行动效果差异在开始时按命名随机流固定采样一次，不逐帧抖动；
- 个人偏好保持为 0–1 的人物属性，再由决策政策映射为有界 Utility 加分，不再把 `0.34` 的“喜欢程度”直接当成可压过整项方案的收益。首轮多 Seed 长跑正是因此发现并修复“画架把散步在短名单阶段清零”的退化；娱乐充足时三种休闲都能发生，娱乐缺口升高才通过真实需求收益提高爱好机会；
- 项目默认 Renderer 已切到共享 Universal 3D Renderer；Foundation 主相机仍显式选择它，三个既有 2D 场景则显式固定 Renderer2D，避免默认迁移静默改画面。Foundation 已有内部 HDR、ACES / 克制 Bloom、降采样 SSAO、两级软阴影、程序化天空与首帧一次性局部 Reflection Probe；实际 Game View 已校准深色甲板可读性，但仍只是灰盒图形基线，不冒充最终 Art Bible；
- 运行界面默认只显示居民 01 的紧凑状态卡与水分 / 健康 / 精力 / 心情进度条，长任务说明可完整换行；右上居民 / 建造 / 开发面板互斥展开，建造控制与开发 Harness 已分流。世界输入只拦截实际可见 HUD 矩形，不再因固定调试区把左侧约三分之一甲板错误冻结；
- `NeedPressureCurve` 提供可复用的平滑需求曲线；当前膀胱在 50% 及以下不产生如厕驱动力，之后非线性上升，90% 起成为必处理的紧急需求。意图概率只在行动边界用领域隔离 Seed 采样，不会因每帧重试把小概率放大成必然；
- 已锁定官方 AI Navigation 2.0.14；隔离 `NavigationInteractionSpike` 中 `DeckNavigationUtility : MonoUtilityBase` 同步构建小型甲板 NavMesh，空旷路径长度比为 1.000，穿过 27° 旋转柜体的路径长度比为 1.058；
- 两名 NavMeshAgent 能相向通过，代表性最小间距约 0.69m；同一柜门的 left / center / right 是共享容量 1 的备选 Slot，A / B 会从相反方向选择 right / left，而不是沿格中心移动或同时穿手操作；
- 柜门 InteractionGroup 的租约覆盖接近、Docking、开门、真实库存交接与关门全周期；资源提交不会提前释放设施容量，最终柜内 2 件物品分别进入两名居民携带库存；
- 版本 9 存档协议覆盖世界 / 旅途、有限地点水源、毫米 + 0.1° 甲板姿态、设施 / 蓝图、普通落位物品的支撑区域与局部姿态、带计量维度的真实库存批次、居民健康与连续身心状态、低于 1 mL 的 nL 代谢余量、随机流游标与中途行动检查点；`OutcomeCommitted` 防止加载后重复交接，NavMesh 路径和 RVO 速度明确按派生缓存重建；新增 `WorldItems` 是可选向后兼容字段，旧 v4 有厨房时会确定性补入开发期初始杯；v2 会显式补入娱乐 / 心情中性初值，v3 会把旧版“不会死亡”的居民迁移为完全健康，v1 无量纲开发存档仍明确拒绝；
- `Capture/Restore/Save/LoadFoundationCheckpointCommand` 已把正式 Foundation 的已提交设施、逐站库存、水罐锚点、身体 / 膀胱、身心状态、模拟 Tick、随机游标，以及每座设施的三类连续状态、风险阈值 / 累计值 / 亚微余量、故障周期与具体故障真实穿过 SSFramework Context 与 `IStorageUtility` 的 JSON、FIFO、原子写和备份边界；加载后重建 NavMesh、Slot 和可达诊断。未提交携物在检查点中回到精确来源的守恒边界；旅程面板提供手动保存/读取/取消，暂停与当前世界的异步所有权由 System 维护；
- `DeckPose` 与 `ContinuousFacilityPlacementLedger` 已在无 Unity 依赖的 Simulation 中实现毫米 / 0.1° 连续姿态、位置 / 旋转独立可关吸附、复合有向矩形占地、不同甲板层的几何隔离、稳定排序和原子提交；正式玩家场景的 Model / System / View 与存档 DTO 已共用这套姿态真值，但高度解析、局部楼板与跨层通行尚未接入；
- 同意图目标先归并，紧急候选优先进入选择池，再在相对高分短名单中用确定性 Softmax 抽样；
- Foundation 的移动、动作、生理与需求只消费 `NomadSimulationClock` 逐步提交的固定 10 ms。倍率改变墙钟预算，每帧最多执行 100 步，积压与小数余量保留；暂停冻结 Tick，复位 / 读取丢弃未执行的墙钟预算。日历从同一 Tick 投影，默认十分钟生活日、十二周一季、四季一年仍待试玩，完整昼夜表现尚未接入；
- 暂停态 `FoundationSoak` Harness 通过 Command 复用同一生产步；10–1000 ms 的任意整数输入分块内部都按 10 ms 执行和审计，总时长须为 10 ms 的正整数倍，上限为 200,000 个实际业务步。报告包含版本、Seed、终态、极值、行动、停滞、守恒及逐步轨迹。它拒绝实时运行和建造事务，死亡或停滞后不会补满输入分块；
- 同一 Seed / 场景配置可从零逐步重现；同一中途安全检查点经两次完整 NavMesh、库存、设施状态与随机流重建后，后续 90 秒的逐步轨迹校验和及行动增量也完全一致。与不中断分支相比，刻意在行动 20%–80% 处捕获后，当前 90 秒样本的完成行动差不超过 1，各连续身心值差不超过 0.005；这是首份显式业务差异预算，不宣称逐帧画面相同或参数已经平衡；
- `DeterministicRandom` 以世界 Seed、稳定 owner、领域 id、事件序号和显式样本槽隔离随机结果；Utility AI 已消费同一入口，领域流只需保存下一事件序号，新增调试采样不会挪动后续事件；
- `NomadEnvironmentSchedule` 只由 Seed、生活日与绝对 Tick 投影天气边界和强度；`FacilityConditionCycle` 再在 `FailureHazardAccumulator` 上建立可保存设施状态机。离散毫秒风险积分对天气分段和帧步长严格可加，暂停不推进，倍速只改变墙钟等待；水箱的磨损 / 欠保养 / 积尘、风险预警、出水阀故障与严重度已进入正式 Model / System / View，其他设施类型尚无各自暴露配置和故障谱；
- 同一世界 Seed、居民稳定 ID 与决策序号会重现相同随机值、候选分解和选择；
- 目标、材料与设施交互位可以全有或全无地预留，失败不会残留部分占用；
- `ResourceInventory` 与 `ResourceFlowLedger` 已把资源、同量纲容量、来源数量、居民携带容量、最终目的容量和交互位放入同一条可查询契约；液体使用整数 mL，物品使用件，复合设施以多个库存隔间表达，跨量纲混装立即拒绝；
- 搬运预留不会瞬移资源：拾取后资源真实进入居民随身库存，再在送达时进入目的库存；携带中改道会原子迁移目的容量与交互预留而不移动货物，失败则恢复旧契约；拾取后取消会保留手中货物并显式要求恢复，不会静默丢失；
- 设施加工会在开始前原子预留全部输入、输出所需净容量和工作位，取消不结算，提交才同时消耗食材 / 清水并产生餐食 / 污水；
- `FoundationResourceFlowSpike` 已让 Ada 完成食材储柜与净水箱 → 随身库存 → 厨房输入 → 厨房本地输出 → 随身库存 → 餐架 / 污水罐的完整物质链；污水罐满时会留下厨房输出并报告具体容量阻塞；
- `ResidentWaterCycle` 把连续口渴、按 mL 累积的代谢速率与体内水 / 膀胱排泄物库存分开；喝水、代谢和如厕仍经同一资源账本提交，膀胱或厕所满时不会吞掉物质；
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
- Universal RP Asset 保留 `Renderer2D` 为 index 0，并把共享 `UniversalRendererData`（index 1）设为项目默认；框架 Demo、Outpost 与 2D Scene Template 的 Camera 已显式固定 index 0，正式 Foundation 与 3D 预览相机显式固定 index 1；
- 固定 3D Game View 已实际看到两个道具的体积、阴影、金属高光和青 / 橙 / 黑材质层级；水循环设施的划痕、细微法线与裸露金属响应可读，没有粉材质、黑屏或错误姿态。自动审计仍把最终审美结论留给人工。

这些证据仍不能证明游戏好玩、正式画面达标、多人居民调度自然、IK 接触可靠或参数已经平衡。实体物流已扩展为三人共用有限工具和固定任务链；现有后处理、SSAO 与反射只证明代表性灰盒渲染链路成立。程序磨损仍缺少基于 Mesh 边缘、遮挡、重力与用途的细节，正式车辆镜头、Surface Shader / VFX、目标平台质量分层、性能预算和统一艺术指导仍未成立。

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

`ResidentActionPlanEvaluator` 先把完整步骤与上下文折算为可解释候选，`UtilityDecisionEngine` 再决定“现在做什么”；两者都不拥有寻路、任务进度、资源结算或动画。选择器先排除不可执行方案，再按日常 / 紧迫 / 严重 / 危急四级动态后果仲裁，同层只在紧迫度容差内比较完整 Utility；这避免用一个无限膨胀的分数同时表示火灾、重病、膀胱和心情。`CargoTransportPlanner` 只判定货物与徒手 / 容器能力及污染暴露，不越权选择脱离完整路程的局部最优容器。`ReservationLedger` 处理独占键，`ResourceFlowLedger` 在其上增加来源数量、携带容量、目的容量与设施加工的原子语义。`ResidentHumanoidPresentation` 只呈现模拟结果，Animator 不拥有移动或任务完成真值。`FacilityInteractionAnchor` 只声明表现接缝，尚未驱动 IK。

`Game.NomadWorkshop.Simulation` 继续保持无 Unity / Framework 依赖，用于确定性的效用、预留、库存、物质链、连续摆放与版本化存档真值；`Game.NomadWorkshop` Runtime 已显式引用 `Game.Framework`，由 Mono Context / Model / System / View 把纯内核接到 Unity 生命周期、Inspector、Command、NavMesh 和 3D 表现。当前没有为了游戏方便修改 Framework Core；只有产品中反复出现、且跨游戏成立的阻力才考虑回流。

存档同样遵守这条边界：`Simulation/Persistence` 只描述可迁移的业务快照与恢复决策，`Runtime/Persistence` 才通过异步 Command 借用框架 `IStorageUtility`。有长期价值的场景及明确退出条件见 [`docs/nomad-workshop-foundation-vertical-slice.md`](../../../docs/nomad-workshop-foundation-vertical-slice.md)；导航、物质链和资产 Preview 是开发 Harness，不进入正式 Player Build，也不写玩家默认存档槽。

正式 Foundation 已完成从逐格摆放 / 四方向 A* 到连续 `DeckPose`、可选吸附、InteractionGroup / Slot、可修复可达状态和可回滚 NavMesh 更新的迁移。旧格子算法只保留为无场景纯 C# 回归；多人局部避让仍由隔离 Harness 验证，尚未冒充正式多居民实现。详见 [`docs/nomad-workshop-navigation-interaction-design.md`](../../../docs/nomad-workshop-navigation-interaction-design.md)。水罐、杯盘和地面建材的可计算放置区，以及蓝图→搬料→多人施工的分期契约见 [`docs/nomad-workshop-world-placement-and-construction.md`](../../../docs/nomad-workshop-world-placement-and-construction.md)。

## 运行与观察

当前优先运行 Framework 分层的最小垂直切片：

1. 执行 `Assets/SSFramework/游牧工坊/Foundation/创建或打开最小垂直切片`，或直接打开 [`Scenes/NomadFoundationVerticalSlice.unity`](Scenes/NomadFoundationVerticalSlice.unity)；
2. 进入 Play，以数字键 `1–4` 或面板按钮选择设施，移动鼠标连续预览，`Q / E` 或右键按当前步长旋转，左键确认，`Esc` / 面板按钮取消；面板可循环自由、0.2 / 0.3 / 0.4 / 0.6 m 档位并开关可视网格，滚轮缩放、中键环绕；触屏可用双指质心拖动旋转、捏合缩放；
3. 建造模式会始终显示所有设施 Slot 的绿 / 红可达标记；试着堵住既有设施，它会持续标红 / 橙但候选仍可确认。建成饮水站与旱厕后可观察搬水、饮用、代谢、如厕、空闲补货和自主休闲；施工封死当前饮水站时，任务应转向另一座可达饮水站或进入可重试等待。统一 Tick 会按世界 Seed 自动进入确定性沙尘天气，水箱故障后居民会从维护托盘拿取实体维修包、前往维修阀并工作到原子修复；开发面板的“注入 Harness 沙尘冲击 / 清洁保养 / 推进至故障 / Harness 瞬时修理”只用于快速验收，故障水箱在普通视图也会保持红色反馈；
4. 打开右上“旅程”，选择“前往干河驿站”；居民到驾驶台后车辆才开始行驶，补水/如厕时会停车换班。抵达后可取水、清运厕所桶；维护托盘空出位置后可取回维修包。返回旧营地会先召回车外人员和容器，再安排驾驶。地点物资有限，反复往返或读取不会刷新。
5. “旅程”面板顶部提供一个手动槽的保存/读取；保存覆盖上次旅程，读取替换当前旅程。操作期间暂停，取消/结束后恢复原暂停选择；开发面板可手动暂停或使用 1×/4×观察。
6. 分层、摆放真值、验证范围和下一步迁移顺序见 [`docs/nomad-workshop-foundation-vertical-slice.md`](../../../docs/nomad-workshop-foundation-vertical-slice.md)。

连续导航目标可以单独观察：

1. 执行 `Assets/SSFramework/游牧工坊/Foundation/创建或打开连续导航与交互 Spike`，或打开 [`Scenes/NavigationInteractionSpike.unity`](Scenes/NavigationInteractionSpike.unity)；
2. Play 后自动观察两人相向会车、A 预留柜门、B 等待、A 取物关门、B 从另一候选位接替；
3. 左侧面板会显示空旷 / 绕障路径比、最小间距、候选位、全周期争用次数和物品实际位置；绿色圆点是同一柜门的备选姿势，不是三个并发容量。

更宽但仍是旧单体 Harness 的实体物流场景可用于对照：

1. 执行 `Assets/SSFramework/游牧工坊/Foundation/创建或打开实体物流灰盒`，或直接打开 [`Scenes/FoundationResourceFlowSpike.unity`](Scenes/FoundationResourceFlowSpike.unity)；
2. 进入 Play，观察 Ada 从食材储柜和净水箱拾取资源；食材携带按件、水桶按 mL / L 分别显示，人物手上同时出现对应箱 / 桶；
3. 厨房只向本地餐食口和污水口产出，再由 Ada 清运到最终餐架和车辆废物罐；中门随任务阶段开合；
4. 第一批完成后点击“运行一次饮水 → 排泄 → 厕所清运链”，观察水依次进入饮水台、体内、膀胱、厕所暂存桶与居民携带库存；
5. 车辆液体废物罐容量为 1 L，厨房污水与当前尿液保留不同资源身份并共享液体容量；剩余空间不足时，继续生产会把无法清运的物质留在原容器并显示目的地已满；
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

Editor 菜单 `Assets/SSFramework/游牧工坊/Rendering Spike/配置并审计 3D Renderer` 会从当前 URP 包的官方模板幂等生成共享 3D Renderer 与保守 SSAO，迁移默认 index、显式固定既有 2D Camera，并维护天空盒、Volume Profile 和可删除的隔离预览。它拒绝覆盖未知 Renderer，审计项目 / 场景 / 子资产契约，并把报告写到被忽略的 `ArtPipelineOutput/`。正式基线见 [`docs/nomad-workshop-rendering-baseline.md`](../../../docs/nomad-workshop-rendering-baseline.md)，共享配置与可删预览的边界见 [`Spikes/Rendering/Urp3D/README.md`](Spikes/Rendering/Urp3D/README.md)。

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

- 旅途所有权与精确测试证据实验：定向 EditMode 11/11、Framework 联合 PlayMode 5/5，证据脚本 20 项断言通过；当前 MCP 实际执行名单已逐项核对。完整原始结果、失败路径与范围限制见[实验记录](../../../docs/nomad-workshop-journey-ownership-experiment.md)；
- 2026-09 重审与固定业务步：全部 NomadWorkshop EditMode 189/189、PlayMode 49/49；正常 1×场景与居民卡裁切修复已实际查看。最新 job、跨输入分块对照和范围限制统一见[本轮重审](../../../docs/nomad-workshop-design-review-2026-09.md)，以下保留各阶段历史证据；
- 编译：CompilationPipeline 0 error / 0 warning；
- 共享吸附基格 + 镜头定向 EditMode 16/16（job `15a8261b90eb`），覆盖自由 / 0.2 / 0.3 / 0.4 / 0.6 m、共同 0.1 m 基础格、双指拖动 / 捏合分解与镜头限位；
- 正式 Foundation PlayMode 15/15（job `42c9a8476b07`），覆盖同步 0.2 / 0.6 m 网格、自由模式自动隐藏、精确停靠、45° 邻接预览 / 落地一致、施工后改去第二座饮水站，以及 `DestinationFull → 如厕 → 恢复饮水`；
- mL 水循环、量纲边界与 v2 存档字段定向 EditMode 26/26（job `025bdf77a02d`）；完整 Simulation EditMode 94/94（job `4664dcc8e86e`）；完整 Nomad PlayMode 23/23（job `7f4f6d0d2b57`）；
- 居民落地修复：场景生成契约 EditMode 1/1（job `c7c674513621`），完整 Foundation PlayMode 16/16（job `5853d84acfca`）；Game View 已确认胶囊底部接触甲板，本地忽略证据为 `Screenshots/nomad-foundation-resident-grounded.png`；
- 统一模拟时钟与日历投影 EditMode 6/6（job `76d96758587e`），完整 Foundation PlayMode 17/17（job `865ea8826059`），覆盖小于 1 ms 余量、倍率、存档 Tick 恢复、暂停冻结和同 Tick 日历投影；Game View 已检查两行时间诊断无截断，本地忽略证据为 `Screenshots/nomad-foundation-unified-clock.png`；
- 本轮 EditMode 扩大回归 744/744（job `d3dd925c4591`），覆盖资源目的地改道成功与交互冲突后的旧预留恢复；最终 Foundation PlayMode 17/17（job `24e665f3a611`），其中双饮水站断路用例证明第一站保持 `0 mL`、第二站收到 `2 L` 并饮用 `300 mL`，水罐锚点同步为第二站；
- 平滑需求与旧休闲稳态：最终全量 EditMode 748/748（job `26495d99656e`），曾覆盖 50% / 90% 曲线接缝、确定性行动机会、旧二级紧急保护与旧娱乐缺口灰盒下的 2:1 分布，并确认默认工作 / 生存短名单没有被休闲政策放宽；Foundation + Utility AI PlayMode 19/19（job `fc6fbf39406a`）证明发呆、散步都实际发生。该恢复语义已由下方“正向娱乐与首版居民 HUD”证据替代，历史 job 只保留回归沿革；
- 统一生活决策与四级风险仲裁：定向 EditMode 20/20（job `55ffbac2272b`），最终完整 Simulation EditMode 109/109（job `be4ff3aeabfb`），覆盖危急火灾与紧迫膀胱的分层冲突、不可行危急方案的降级备选、同层紧迫度容差、负效用最小伤害选择、不安全环境与能力不足硬约束；风险优先诊断、瞬态消息清理与公共命名收口后，最终完整 NomadWorkshop PlayMode 25/25（job `9b586441c81e`）通过；缺少防漏容器时候选会被拒绝并留下诊断，居民仍能执行可行备选，不再永久停在 `Blocked`；
- 正向娱乐与首版居民 HUD：身心 / 休整定向 EditMode 9/9（job `9f5a0e560c89`）；补入存档 v2→v3 迁移后完整 Simulation EditMode 116/116（job `2ea1350947c0`）、存档介质真往返 PlayMode 1/1（job `035b95187bcb`），此前完整 NomadWorkshop PlayMode 25/25（job `13a793c74ec3`）通过；Game View 已实际检查默认紧凑卡、居民详情和可滚动开发控制台，本地忽略证据为 `Screenshots/nomad-foundation-ui-compact-v1.png`、`nomad-foundation-ui-resident-detail-v1.png`、`nomad-foundation-ui-developer-v1.png`；
- 观景画架与灰盒灯光：爱好 / 风险层边界定向 EditMode 18/18（job `4a459b590407`），最终完整 Simulation 118/118（job `3254af71ce6a`）；可重建场景 / 第五份定义 / 主补光接线 EditMode 1/1（job `d29981da66a0`），真实建造、精确停靠与娱乐回升定向 PlayMode 1/1（job `c71b8951fe6f`），施工封堵后放弃软爱好并恢复新需求评估 1/1（job `3e204687c34e`），最终完整 NomadWorkshop PlayMode 27/27（job `f944839c7c85`）；Game View 已检查深色金属层次、四类已建设施和观景画架，本地忽略证据为 `Screenshots/nomad-material-lighting-fixed-v1.png`、`Screenshots/nomad-hobby-and-material-lighting-v1.png`、`Screenshots/nomad-hobby-graybox-clean-v1.png`；
- Foundation 3D Renderer 修复：场景生成与共享 Renderer 契约 EditMode 3/3（job `51662455c919`），最终完整 NomadWorkshop PlayMode 27/27（job `bece7a403135`）；固定机位下双灯默认与强度归零对照为 `Screenshots/nomad-foundation-urp3d-renderer-default.png` 与 `Screenshots/nomad-foundation-urp3d-lights-off-control.png`，已人工确认前者具有方向性明暗、高光与阴影，后者立即接近全黑；
- Game View 已实际检查“全部饮水站”聚合显示和水罐精确实例锚点无截断，3D 水罐仍位于匹配的车辆水箱旁；本地忽略证据为 `Screenshots/nomad-foundation-instance-inventory-game.png`；
- 可重建场景管线 EditMode 1/1（job `c3046ae76822`）；
- HUD 实际矩形 / 面板互斥 / 场景重建 EditMode 4/4（job `e3515b449afd`）；Game View 已检查长任务第二行、互斥建造面板和提亮后的深色材质，本地忽略证据为 `Screenshots/nomad-foundation-ui-readability-final.png`、`Screenshots/nomad-foundation-build-panel-final.png`；
- Foundation 运行检查点最终完整 Simulation EditMode 120/120（job `92daa614078c`），中途携水守恒恢复 1/1（job `fc2444c1460e`），真实文件往返 1/1（job `4621a9139d5a`），无效检查点重建失败后自动回到加载前状态 1/1（job `2b7aae95abb8`）；最终 Foundation + 存储 PlayMode 24/24（job `08763c3fef3a`）还覆盖了半行动检查点在改动世界前被明确拒绝；
- 首条设施状态与故障闭环：最终完整 Simulation EditMode 128/128（job `78d6d6e74d76`），最终 NomadWorkshop PlayMode 33/33（job `71de85b433c2`）；覆盖不同分帧等价、暂停、保养不回滚既有风险、修理开启新周期、精确存取风险轨迹、故障前取水预留安全释放和全链水量保持 60 L。Game View 已实际检查故障水箱红色反馈、四项状态条、阈值 / 累计值和四个 Harness 操作，本地忽略证据为 `Screenshots/nomad-foundation-facility-condition-v1.png`；
- 健康、死亡与工作效率闭环：框架命令列 + 身心规则 + v3→v4 存档迁移联合 EditMode 59/59（job `60a395a68d11`），最终身心 / 休息选择定向 10/10（job `71dc8eabd612`），最终 Foundation PlayMode 27/27（job `8bccf37982ff`）；覆盖严重缺水损害健康、零健康稳定死亡、健康存取、命名工作速度随机流、低健康 / 高疲劳的行动成本与地面休息反超、坐卧灰盒表现，以及可控压力动员与过载回落。Game View 已检查独立健康条、建造 / 开发面板分流和开发态工作效率，本地忽略证据为 `Screenshots/nomad-build-panel-health-2026-09-05.png`、`Screenshots/nomad-developer-panel-health-2026-09-05.png`；框架诊断的独立命令说明列见 `Screenshots/framework-command-description-column-2026-09-05.png`；
- 建造退出与物品空间 Phase A/B：区域几何 + 存档契约定向 EditMode 18/18（job `a8cc82597a4b`），移动账本定向 EditMode 8/8（job `0c4d0f57d9de`），最终全部 NomadWorkshop EditMode 174/174（job `0f1a3a4b0ceb`）；杯具拿放定向 PlayMode 1/1（job `9d0c9817be1d`），最终完整 Foundation PlayMode 30/30（job `bd407d99c9ae`）。覆盖建造切换到居民/开发/关闭后的完整诊断清理、两种 footprint 同区容量、杯具任意 37° 朝向、来源/目标联合预留、携带时检查点回到精确来源、目标原子提交和既有水循环/故障回归。Game View 已检查台面落位、居民手部携带与目标台面唯一实例，本地忽略证据为 `Screenshots/nomad-world-item-cup-close-v1.png`、`Screenshots/nomad-world-item-carry-v1.png`、`Screenshots/nomad-world-item-drop-v1.png`；
- 有界长时模拟与偏好量纲：最终全部 NomadWorkshop EditMode 176/176（job `f9efde31fe31`），完整 NomadWorkshop PlayMode 42/42（job `4ec2c16b78d7`）。四个 Seed 在接近正式时长下各跑六生活小时，均保持 60,000 mL 总水量、无停滞且同时完成发呆 / 散步 / 作画；同 Seed 从零重放和同一中途安全检查点两次恢复后的轨迹校验和均完全一致。一次过宽正则还扩大完成全项目 PlayMode 816/816（job `6cd55314e11c`，80.54 s）；明确 `testNames` / assembly 过滤可用，便利 `filter` 正则仍不作为可靠收窄手段。Game View 已实际检查开发面板按钮、结构化报告、极值与结束诊断换行，本地忽略证据为 `Screenshots/nomad-foundation-soak-harness-v1.png`；
- 检查点差异预算、确定性沙尘与实体维修：通用物品 Use Lease、天气边界和设施暴露定向 EditMode 22/22（job `9a3180f4e13d`），可重建场景契约 1/1（job `7ac96592b2ba`）；中途实体备件恢复和居民真实维修分别通过定向 PlayMode（job `d91edb516877`、`6b3e082a2846`），Harness 中途抢先修理也会整体取消路径、归还备件且不污染完成计数（job `88a369f58edd`）。90 秒对照中，守恒边界恢复相对不中断分支的完成行动差不超过 1，健康 / 口渴 / 娱乐 / 心情 / 疲劳 / 压力差均不超过 0.005；同一场 90 秒沙尘在 1000 ms 与 137 ms 步长、以及检查点恢复后产生完全相同的设施状态。最终完整 NomadWorkshop EditMode 183/183（job `99b04f4a0a5e`）、PlayMode 47/47（job `5b6da6610735`，含 Foundation 39 项）；Game View 已实际检查晴天下的维护托盘 / 维修包和可读沙尘表现，本地忽略证据为 `Screenshots/nomad-foundation-repair-clear.png`、`Screenshots/nomad-foundation-sandstorm-readable.png`；
- Game View：已实际查看 45° 紧邻既有饮水站时 0/3 Slot 可达但仍可确认的红色幽灵；`Screenshots/nomad-foundation-45deg-preview-parity.png` 同时显示既有设施三个绿色 Slot 与候选三个红色 Slot，无需悬停。同步 0.2 m 网格与基础建造模式见 `Screenshots/nomad-foundation-exact-docking-build-mode.png`（两者均为本地忽略证据）。

测试重点覆盖危险口渴压过休闲、多需求行动按实际压力得分、同 Seed 重现、不同 Seed 只在短名单内变化、重复设施不放大意图概率、无正效用时安全等待、冲突预留不泄漏，以及 Avatar / Clip / 材质 / Controller / 锚点的导入契约。

## 明确未做

- 三名以上居民、任意布局长时间拥堵、复杂设施队列与 20 人压力；当前正式三人共用资源和空间账本，原生交通的身体/停靠/让路边界由逐帧用例验证；
- 早期长跑只覆盖单居民、六生活小时；现已增加三人有限旅程的同步业务与真实 PlayerLoop 证据，仍无大人口、季节跨度或玩家节奏的长期平衡统计；
- Foundation 运行时已接入娱乐、心情、疲劳与压力，并用一座观景画架打通首个真实爱好；姿势不适、睡眠、社交、多爱好居民档案，以及主行动 / 微活动 / 检查点绕行执行器尚未接入。当前作画偏好、休整速率和娱乐对疲劳的最大 18% 缓冲只是待试玩平衡值，不是现实生理结论；
- 四级风险仲裁已有纯 C# 火灾 / 生理极端冲突证据，但正式 Foundation 尚未生成火灾、疾病、求援、失禁事故或多居民事件任务；不将仲裁器可测误写成这些玩法已交付；
- 设施状态目前只有车辆水箱拥有非零暴露配置，也只有“出水阀卡滞”一个具体故障；正式环境只有每生活日一次的简单确定性沙尘窗口，维修链包含车载初始一只维修包与驿站两只有限备件。尚未接入区域 / 旅途天气、天气预报与遮蔽、备件制造、维修工具 / 专用动画、其他设施故障谱、多日故障分布或长时间参数平衡；
- 水罐寻找、旱厕使用和停靠清运已接入正式 Foundation；一只空杯已验证台面占地、原子拿放、取消/存档回滚与手持锚点，但目前仅由开发 Harness 触发，还没有成为饮水、清洁或整理的正式 Utility AI 行为。其他食物容器 / 餐具、洗手、洁净度和晕车仍只有旧 Harness 或纯 C# 方案规则，尚未接入完整设施、动画或长期平衡；
- 正式废土服装、模块化发型/背包、人物差异、面部与布料；
- Animation Rigging、手部 IK、工具挂点实际消费与专用交互修正；
- 蓝图与居民搬料 / 施工、建造成本、拆除 / 搬移、边缘 / 连接口吸附、库存 UI，以及自动保存节奏、多槽管理和未完成蓝图恢复；多居民运行检查点已支持安全边界恢复；
- 饮水站与每座旱厕已拥有独立实例库存，污物桶保留拆卸/回装身份；厨房等复合设施尚未迁入逐实例多隔间库存；
- 正式 UGUI / UI Toolkit、艺术指导、音效、性能采样、Player Build 与玩家体验验证；当前 IMGUI 是首版信息架构与开发 Harness，不是最终 UI 资产；
- 基于 Curvature / AO / Position 的 Mesh-specific 贴图、唯一 UV / 屏幕占比关联的正式 Texel Density 预算，以及目标平台贴图内存基线；
- 当前所有 Quality 仍共用一份 URP Asset；内部 HDR、后处理、SSAO、软阴影和局部反射已经形成桌面灰盒基线，但尚未拆成 Desktop / Mobile 质量资产。VFX、正式灯光风格、HDR 显示输出和目标设备性能预算仍未定型。

当前完整旅程主线已接通目标点与居民驾驶 → 停靠取货 / 清运 → 三人职责与召回 → 再出发及手动存读档，身体约束与续玩工程验收已完成。有限补给和厕所实例库存属于闭环条件；更多餐具、微活动、故障与完整蓝图按试玩需要后移。具体交付顺序只在 Foundation §9 维护。
