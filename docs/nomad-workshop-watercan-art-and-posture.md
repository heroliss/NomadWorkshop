# 水罐造型与自然携物姿态

2026-09-08 补充：[局部甲板回归](nomad-workshop-deck-support-spike.md)发现放罐末段固定蹲身偏移会造成臂长不足，已在共享 IK 中按实际腕部目标补偿骨盆，保留原手脚/避让门槛并新增固定脚底时臂长比 <1 的验证。容器绑定与 FBX 未修改；静态源配方仍保持下文版本。

接续 `f5739d6`。NW10 实机检查显示，楼梯携水时肘部过高；水罐仍是立方壳体和直角把手，与已更新的设施和人物不协调。本阶段在保持真实水量、身份和拿放事务的前提下，改善普通提桶姿势与手持物造型，不改变通行玩法或增加虚假携物动画。

## 可检查计划

- [x] 采集当前肩、肘、腕、掌心和罐体位置，明确高肘来自目标位置、避让求解还是 Animator 输出；保留同一人物和场景的基线。
- [x] 修正脚部支撑与手臂求解使用的肩膀位置，再检查自然下垂携行的肘部偏好；保留倾倒、地面拾放和抬起过程中的罐体避让，以实际骨骼接触和姿态边界验证。
- [x] 制作带圆角罐肩、握持空间、独立瓶盖和少量压筋的新水罐，明确视觉与握点/开口/净空边界，保留后续模型替换入口。
- [x] 新罐体接入后验证拿放、携行、倾倒、暂停/取消/检查点恢复，以及三款人物的楼梯动作；实际检查画面并整理阶段交付。地面蹲姿和水流质感仍列为后续美术改进，不将接触通过视为自然动作完成。

## 证据与边界

2026-09-08，`WorkwearMechanicStairs` 真实搬运至约 1.4 m 后暂停。骨盆支撑位移为 -0.18506 m，手臂求解使用的肩高为居民局部 1.47985 m，而 Animator 输出肩高为 1.29478 m，差值与骨盆位移吻合。实际肘高 1.34847 m，高于肩膀约 5.4 cm。基线 JSON 为 `Logs/AIValidation/nomad-warm-art/carry-pelvis-baseline.json`，实际截图为 `Screenshots/nomad-carry-pelvis-before.png`。单帧握点满足原验收，说明“手掌贴住”不足以证明整条手臂姿势成立。

实现改为由携物组件拥有一次 IK 回调：先调用可选踏面支撑，取得其施加的世界骨盆位移，再计算肩—肘—腕几何。楼梯组件不再单独订阅 `OnAnimatorIK`；地面拾放期间由已有蹲身支撑独占双脚。普通甲板调用方不配置额外支撑，保留原行为。没有改动罐体尺寸、握点、臂长、物品位置或逻辑身体；也没有用回调排序属性掩盖两个求解者之间的数据缺口。

### 姿态修正验证（2026-09-08）

以下均为本轮 PlayMode 精确 fixture，先完整发现、新鲜 READY 预检，再按原 job 取逐项终态核验。证据前缀位于 `Logs/AIValidation/nomad-warm-art/`。

| 范围 | 结果 | job / 前缀 |
|---|---|---|
| NW10 维修者，实际 18 踏面上下楼与暂停/取消/恢复 | 4/4 | `48121cac13a5` / `carry-pelvis-mechanic-02` |
| NW10 束发居民，同一楼梯契约 | 4/4 | `6d85db352ffa` / `carry-pelvis-caretaker-01` |
| NW10 驾驶者，同一楼梯契约 | 4/4 | `6ec819ee1735` / `carry-pelvis-driver-01` |
| 参考车辆，原拿放/携行/倾倒/杯具/旅程/检查点契约 | 23/23 | `b7e00956fbdd` / `carry-pelvis-reference-01` |
| 原始 Humanoid 楼梯实验 | 4/4 | `b8feb295516a` / `carry-pelvis-original-01` |

合计 39 项通过。楼梯路径持续采样实际肩/肘/腕，要求求解肩膀与实际肩膀误差小于 2.5 cm、直立携行肘部低于肩膀至少 2 cm，并保持手臂对罐体 3 cm 净空；原手掌、脚部、整罐环境碰撞、暂停和唯一身份检查保留。普通拿放/倾倒不套用楼梯的低肘要求，沿用该动作的真实接触和避让断言。首次维修者发现请求拼错 fixture 名，计划器拒绝空名单，没有启动空测试；后续使用正确发现结果，未将该次计入结果。

同一维修者场景、同一观察菜单在约 1.4 m 楼梯位置重新暂停后，肘部低于肩膀 8.697 cm，肩部求解误差小于 0.1 mm，掌心误差约 1.0 mm、角差约 1.44°。前后为相近路径位置的两次真实运行，不是逐帧一致的动画基线。实际已查看 [修正前](../Screenshots/nomad-carry-pelvis-before.png)、[修正后](../Screenshots/nomad-carry-pelvis-after.png)和[近景](../Screenshots/nomad-carry-pelvis-close.png)。数据为 `carry-pelvis-mechanic-after.json` 和 `carry-pelvis-mechanic-review.json`。

另外实际查看了 [束发居民](../Screenshots/nomad-carry-pelvis-caretaker.png)与[驾驶者](../Screenshots/nomad-carry-pelvis-driver.png)的中段暂停画面；肘部下降量分别约 8.64 / 7.38 cm，掌心角差 2.54° / 1.44°，仍持有同一 8 L 水罐。对应报告为 `carry-pelvis-caretaker-review.json`、`carry-pelvis-driver-review.json`。截图观察只临时调整相机，没有挪动身体、脚或水罐。

`NomadStairTraversalReview` 现将实际肩膀、肘部、求解肩膀、骨盆位移、肘部下降量和臂长可达比例收入原观察报告，不再只记录手掌贴合。没有增加新菜单、Skill 或项目常驻规则。测试期四份 Runtime/测试源码哈希已复核未变；报告工具的字段扩充在上述前四组测试后完成，重新编译后执行原始楼梯 fixture 和实际观察菜单。

环境为 Unity 6000.3.22f1 / URP 17.3、StandaloneWindows64 Editor；Game View 1225 × 713，截图 2 倍。此处证明现有罐体与已测人物的姿态修正，不代表任意新骨架/新罐体、目标 Player 或整体美术已验收。水罐外壳、原衣摆/袖口及长楼梯仍需后续替换，水罐绑定和造型计划继续。

结束恢复干净、非 Play 的 `ReferenceVehicleSample`，楼梯观察器已撤销，CompilationPipeline 与 Console 错误均为 0；五份代码文件的最终快照为 `carry-pelvis-final-source-hashes.json`。使用临时 Play 世界与测试内存检查点，未覆盖玩家持久存档。完整 Foundation、旧 NW2/NW3 全部外观、Player 构建和性能没有在本轮重复验证。

物品仍由原业务事务持有；新外观不另建物品身份，手部 IK 不反向移动逻辑罐体。握点、开口和几何范围必须同时核对，不能只把水罐缩小或移远来让一个姿势通过。

## NW11 容器模型与绑定（2026-09-08，阶段验收完成）

接续姿态修正 `02e206a`，本轮让水罐成为实际的第二种模型：收肩壳体、圆弧把手、旋盖、底部接缝、压筋和 8 L 标记，保留独立封盖与液位显示。当前资产为 3 个 Mesh / 7518 三角形，外形约 0.29 × 0.185 × 0.512 m（宽 × 深 × 高）。它仍位于原物品 0.34 × 0.24 × 0.59 m 的保守空间包络内，不改动业务水量、物品身份或保存格式；不是为了避开上一轮高肘问题而缩小罐体，上轮已用原尺寸独立修正并验收。

源模型为 `ArtPipelineOutput/WaterCan/v03/NW11_WaterCan_Source.blend`，展示场景为同目录 `NW11_WaterCan.blend`，配方为 `Tools/ArtPipeline/Blender/blender_nomad_watercan.py` 0.1.1。Blender FBX 回读核对面数、UV、无退化三角形、尺寸和所有标记。2048² Color/Normal/Surface 图集由程序材质烘焙，Unity 使用 URP Lit；没有使用 AI 贴图或外部付费生成服务。已查看该版本离线预览，实际游戏效果仍单独取证。

`FoundationCarriedContainerRig` 持有携行枢轴、右掌接触、液体开口、封盖、液位显示、可变数量有向避让盒和地面拾放身体位移。旧灰盒工厂也产生同一绑定；运行期动作、IK、水流和观察器已不再通过模型子节点名或旧几何常量定位接口。来源 Adapter 根据导出 manifest 标记生成游戏自有 Prefab 的显式引用；后续来源可直接配置该外层，不需要沿用 Blender 节点名。

当前变体的携行枢轴为 (0, 0.490, -0.032) m，右掌为 (0, 0.512, -0.032) m，开口为 (0.075, 0.398, 0.026) m；旧灰盒枢轴高 0.560 m、开口 (0.120, 0.445, 0.085) m。避让体积由旧模型的一个盒变为罐身与收肩两个盒，地面最大身体位移从 (0, -0.55, 0.20) 调整为 (0, -0.62, 0.20)。这些是经验证的模型/姿态输入，不承诺自动适配任意身材；当前仍为单右手、单开口、单封盖机制，双手、大件和攀梯携物尚未扩展。

菜单“Assets/SSFramework/游牧工坊/首版美术/导入手提水罐并打开参考车辆”核对源码/文件哈希，创建模型、材质和包装 Prefab，经编辑器接入 `ReferenceVehicleSample` 与三款 `Workwear*Stairs`。新旧绑定共用测试，新增三角形射线取样证明掌心确实在把手上表面、隐藏封盖后有真实罐口凹腔；错误引用、无效体积、节点改名和材质通道也有定向证据。

### 新模型验证与实机观察

以下为同一产品快照的 56 项定向检查（EditMode 4 + PlayMode 52）。每组保留完整发现、精确计划、新鲜 READY 预检、dispatch 和逐项终态；提交前再次用 `Test-UnityMcpTestEvidence` 核对原报告，未重跑累加数量。证据前缀均位于 `Logs/AIValidation/nomad-warm-art/`。

| 范围 | 结果 | job / 前缀 |
|---|---|---|
| 容器导入、真实把手/罐口、错误绑定和改名 | EditMode 4/4 | `731edb6cb07c` / `container-art-edit-01` |
| 参考车辆拿放/携行/倾倒、暂停/取消/检查点、绑定身份与原设施/旅程 | PlayMode 24/24 | `c7211defca18` / `container-reference-01` |
| NW10 维修者携新罐上下楼 | PlayMode 4/4 | `d63b35fd4a1f` / `container-stair-mechanic-01` |
| NW10 束发居民携新罐上下楼 | PlayMode 4/4 | `00a02750c6da` / `container-stair-caretaker-01` |
| NW10 驾驶者携新罐上下楼 | PlayMode 4/4 | `104e23bd891a` / `container-stair-driver-01` |
| 旧 Warm 工坊、原灰盒罐体回归 | PlayMode 12/12 | `8621eade7bb2` / `container-legacy-warm-01` |
| 原始 Humanoid 楼梯、原灰盒罐体回归 | PlayMode 4/4 | `755f13cbfe89` / `container-legacy-stair-01` |

已实际查看[地面拾取](../Screenshots/nomad-container-ground-02.png)、[携行](../Screenshots/nomad-container-carry-01.png)和[倾倒](../Screenshots/nomad-container-pour-01.png)。对应 `container-ground-review-01.json`、`container-carry-review-01.json`、`container-pour-review-01.json` 来自真实居民工作阶段；拾取时仍是地面的空罐，携行/倾倒时实际持有 2 L 水。掌心误差分别约 1.39 / 1.01 / 1.01 mm，角差约 1.44°。封盖按工作阶段隐藏，倾倒起点来自新罐口并连接到饮水站入口。第一张地面检查镜头被水箱遮挡，改相机后的第二张用于观察；没有移动居民或物品来摆拍。

三款人物分别实际运行到约 1.4 m 梯段后暂停，已查看[维修者](../Screenshots/nomad-container-stair-mechanic-01.png)、[束发居民](../Screenshots/nomad-container-stair-caretaker-01.png)、[驾驶者](../Screenshots/nomad-container-stair-driver-01.png)。均保留同一 8 L 水罐，肘部低于肩膀约 6.48 / 6.53 / 5.33 cm，肩部求解误差小于 0.01 mm，掌心角差约 1.44 / 2.54 / 1.45°。原报告保存为 `container-stair-{mechanic,caretaker,driver}-review-01.json`。抬脚阶段的目标误差不当作支撑失败，接触检查仍以实际接触权重为条件。

本轮造型已摆脱方壳和直角把手，但实机仍有明确不足：地面拾放接近深跪姿，膝盖外展且躯干过直；棚架横梁会遮挡近景人物；倾倒水流仍像均匀实心线。后续应分别改善弯腰/下蹲协调、剖切显示范围和流体视觉，不能靠降低接触标准或提高曝光来掩盖。衣摆、袖口和整体场景仍未达到 J 的最终效果。当前长直梯是动作基线，紧凑梯/局部扩建继续下一阶段。

38 份产品源码和资产与 `container-source-hashes-01.json` 全部一致，文档在取证后同步更新。结束恢复干净、非 Play 的 `ReferenceVehicleSample`，携行/设施/楼梯观察器均撤销，CompilationPipeline 与 Console 错误为 0。环境仍为 Unity 6000.3.22f1 / URP 17.3、StandaloneWindows64 Editor、RTX 4060 Laptop、Very Low，Game View 1225 × 713、截图 2 倍。使用临时世界与内存检查点，没有覆盖玩家持久存档；完整 Foundation、全部旧 NW2/NW3 外观、目标 Player 和性能未在本轮重复验证。阶段通过只覆盖已测容器、人物与路径，不扩大为任意 Rodin 模型或整体美术完成。
